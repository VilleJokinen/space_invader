// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Message;
using Metaplay.Core.Network;
using Metaplay.Core.Serialization;
using NUnit.Framework;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using static Metaplay.Core.Network.MessageTransport;

namespace Cloud.Tests
{
    [MetaMessage(30001, MessageDirection.ClientInternal)]
    public class TestMessage : MetaMessage
    {
        public TestMessage() { }
    }

    [Parallelizable]
    class MessageTransportTests
    {
        static byte[] ConcatByteArrays(byte[][] buffers)
        {
            if (buffers.Length == 0)
                return new byte[0];
            if (buffers.Length == 1)
                return buffers[0];

            int totalSize = 0;
            foreach (byte[] msg in buffers)
                totalSize += msg.Length;

            byte[] buffer = new byte[totalSize];
            int outNdx = 0;
            foreach (byte[] msg in buffers)
            {
                Buffer.BlockCopy(msg, 0, buffer, outNdx, msg.Length);
                outNdx += msg.Length;
            }
            return buffer;
        }

        static byte[] CreateMockServerHello()
        {
            Handshake.ServerHello message = new Handshake.ServerHello(
                serverVersion: "1",
                buildNumber: "1",
                fullProtocolHash: 123,
                commitId: "1");
            byte[] payload = MetaSerialization.SerializeTagged<MetaMessage>(message, MetaSerializationFlags.SendOverNetwork, logicVersion: null);
            byte[] buffer = new byte[WireProtocol.PacketHeaderSize + payload.Length];
            WireProtocol.EncodePacketHeader(new WirePacketHeader(WirePacketType.Message, WirePacketCompression.None, payload.Length), buffer);
            Buffer.BlockCopy(payload, 0, buffer, WireProtocol.PacketHeaderSize, payload.Length);
            return buffer;
        }

        class MockNetworkStream : Stream
        {
            public delegate void OnWriteHandler(byte[] message);

            public event OnWriteHandler OnWrite;
            public bool WriteHangs = false;
            public TimeSpan? WriteTakesTime;

            public TimeSpan? ReadTakesTime;
            public int? ReadTakesTimeAfterNumBytes;

            int numReadTotal = 0;

            byte[] _readBuffer;
            int _readCursor;
            bool _expectHello;

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

            public MockNetworkStream(params byte[][] bytes)
            {
                _readBuffer = ConcatByteArrays(bytes);
                _expectHello = true;
            }

            int InternalRead(byte[] buffer, int offset, int count)
            {
                int remaining = _readBuffer.Length - _readCursor;
                int numToRead = Math.Min(remaining, count);
                Buffer.BlockCopy(_readBuffer, _readCursor, buffer, offset, numToRead);
                _readCursor += numToRead;
                return numToRead;
            }

            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            {
                int numToRead;
                if (ReadTakesTimeAfterNumBytes.HasValue && ReadTakesTimeAfterNumBytes.Value > numReadTotal)
                    numToRead = Math.Min(count, ReadTakesTimeAfterNumBytes.Value - numReadTotal);
                else
                    numToRead = count;

                int numRead = InternalRead(buffer, offset, numToRead);
                numReadTotal += numRead;

                if (numRead == 0)
                {
                    await Task.Delay(-1, (CancellationToken)ct);
                    ((CancellationToken)ct).ThrowIfCancellationRequested();
                }
                else if (ReadTakesTime.HasValue)
                {
                    if (ReadTakesTimeAfterNumBytes.HasValue == false || ReadTakesTimeAfterNumBytes.Value == numReadTotal - numRead)
                        await Task.Delay(ReadTakesTime.Value, (CancellationToken)ct);
                }
                return numRead;
            }

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct)
            {
                byte[] temp = new byte[buffer.Length];
                int numRead = await ReadAsync(temp, 0, buffer.Length, ct);
                temp.CopyTo(buffer);
                return numRead;
            }

            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            {
                if (OnWrite != null)
                {
                    byte[] slice = new byte[count];
                    Buffer.BlockCopy(buffer, offset, slice, 0, count);
                    OnWrite.Invoke(slice);
                }

                // Handshake
                if (_expectHello)
                {
                    WirePacketHeader header = WireProtocol.DecodePacketHeader(buffer, offset, enforcePacketPayloadSizeLimit: false);
                    byte[] slice = new byte[header.PayloadSize];
                    Buffer.BlockCopy(buffer, offset + WireProtocol.PacketHeaderSize, slice, 0, header.PayloadSize);

                    Handshake.ClientHello hello = (Handshake.ClientHello)MetaSerialization.DeserializeTagged<MetaMessage>(slice, MetaSerializationFlags.SendOverNetwork, null, null);
                    _ = hello;

                    _expectHello = false;
                    return Task.CompletedTask;
                }

                if (WriteHangs)
                    return Task.Delay(-1, (CancellationToken)ct);
                if (WriteTakesTime != null)
                    return Task.Delay(WriteTakesTime.Value);
                return Task.CompletedTask;
            }

            public override void Flush() => throw new NotSupportedException();
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
        class TestStreamMessageTransport : StreamMessageTransport
        {
            Func<CancellationToken, Task<Stream>> _streamFactory;
            Func<IMessageTransport, Task> _transportOp;

            public TestStreamMessageTransport(Action<ConfigArgs> editConfig, Func<CancellationToken, Task<Stream>> streamFactory)
                : base(LogChannel.Empty, GenConfig(editConfig))
            {
                _streamFactory = streamFactory;
                _transportOp = null;
            }

            public TestStreamMessageTransport(Action<ConfigArgs> editConfig, Func<CancellationToken, Task<Stream>> streamFactory, Func<IMessageTransport, Task> transportOp)
                : base(LogChannel.Empty, GenConfig(editConfig))
            {
                _streamFactory = streamFactory;
                _transportOp = transportOp;
            }

            static ConfigArgs GenConfig(Action<ConfigArgs> editConfig)
            {
                ConfigArgs ca = new ConfigArgs();
                ca.GameMagic = "TEST";
                ca.SupportedLogicVersions = new MetaVersionRange(0, 0);
                editConfig(ca);
                return ca;
            }

            protected override async Task<(Stream, TransportHandshakeReport)> OpenStream(CancellationToken ct)
            {
                Stream stream = await _streamFactory(ct);
                TransportHandshakeReport report = new TransportHandshakeReport("hostname", System.Net.Sockets.AddressFamily.InterNetwork, "test");
                return (stream, report);
            }

            public async Task<ErrorT> ExpectError<ErrorT>()
            {
                TaskCompletionSource<ErrorT> result = new TaskCompletionSource<ErrorT>();
                this.OnConnect += (Handshake.ServerHello hello, TransportHandshakeReport handshakeReport) => { result.SetException(new AssertionException("expected error")); };
                this.OnReceive += (MetaMessage msg) => { result.SetException(new AssertionException("expected error")); };
                this.OnError += (MessageTransport.Error e) =>
                {
                    if (e is ErrorT et)
                        result.SetResult(et);
                    else
                        result.SetException(new AssertionException($"expected error: got {e.GetType().Name}, expected {typeof(ErrorT).Name}"));
                };
                Open();
                if (_transportOp != null)
                    await _transportOp.Invoke(this);
                try
                {
                    return await result.Task;
                }
                finally
                {
                    Dispose();
                }
            }

            public async Task<ErrorT> ExpectConnectThenError<ErrorT>()
            {
                TaskCompletionSource<ErrorT> result = new TaskCompletionSource<ErrorT>();
                bool gotConnect = false;
                this.OnConnect += (Handshake.ServerHello hello, TransportHandshakeReport handshakeReport) =>
                {
                    if (gotConnect)
                        result.SetException(new AssertionException("double connect"));
                    gotConnect = true;
                };
                this.OnReceive += (MetaMessage msg) => { result.SetException(new AssertionException("expected error")); };
                this.OnError += (MessageTransport.Error e) =>
                {
                    if (!gotConnect)
                        result.SetException(new AssertionException($"expected connect: got {e.GetType().Name}"));
                    else if (e is ErrorT et)
                        result.SetResult(et);
                    else
                        result.SetException(new AssertionException($"expected error: got {e.GetType().Name}, expected {typeof(ErrorT).Name}"));
                };
                Open();
                if (_transportOp != null)
                    await _transportOp.Invoke(this);
                try
                {
                    return await result.Task;
                }
                finally
                {
                    Dispose();
                }
            }

            public async Task ExpectConnectThenInfo<InfoT>(Func<InfoT, bool?> infoValidator, bool tolerateCycleInfos)
            {
                TaskCompletionSource<int> result = new TaskCompletionSource<int>();
                bool gotConnect = false;
                this.OnConnect += (Handshake.ServerHello hello, TransportHandshakeReport handshakeReport) =>
                {
                    if (gotConnect)
                        result.SetException(new AssertionException("double connect"));
                    gotConnect = true;
                };
                this.OnReceive += (MetaMessage msg) => { result.SetException(new AssertionException("expected info, got receive")); };
                this.OnError += (MessageTransport.Error e) => { result.SetException(new AssertionException($"expected info, got error {PrettyPrint.Compact(e)}")); };
                this.OnInfo += (MessageTransport.Info info) =>
                {
                    if (!gotConnect)
                    {
                        result.SetException(new AssertionException($"expected connect before info: {PrettyPrint.Compact(info)}"));
                    }
                    else if (info is InfoT typedInfo)
                    {
                        bool? valid = infoValidator(typedInfo);
                        if (!valid.HasValue)
                            return;
                        else if (valid.Value)
                            result.SetResult(0);
                        else
                            result.SetException(new AssertionException($"validator rejected info"));
                    }
                    else if (tolerateCycleInfos && info is ThreadCycleUpdateInfo)
                    {
                    }
                    else
                        result.SetException(new AssertionException($"expected info: got {info.GetType().Name}, expected {typeof(InfoT).Name}"));
                };
                Open();
                if (_transportOp != null)
                    await _transportOp.Invoke(this);
                await result.Task;
            }
        };

        [Test]
        public async Task TestStreamConnectionTimeout()
        {
            TestStreamMessageTransport transport = new TestStreamMessageTransport(
                (StreamMessageTransport.ConfigArgs config) =>
                {
                    config.ConnectTimeout = TimeSpan.FromSeconds(1);
                },
                async (CancellationToken ct) =>
                {
                    await Task.Delay(2000, ct);
                    return null;
                });
            await transport.ExpectError<StreamMessageTransport.ConnectTimeoutError>();
        }

        [Test]
        public async Task TestStreamHeaderTimeout()
        {
            TestStreamMessageTransport transport = new TestStreamMessageTransport(
                (StreamMessageTransport.ConfigArgs config) =>
                {
                    config.ConnectTimeout = TimeSpan.FromSeconds(1);
                    config.HeaderReadTimeout = TimeSpan.FromSeconds(1);
                },
                (CancellationToken ct) =>
                {
                    MockNetworkStream stream = new MockNetworkStream();
                    return Task.FromResult((Stream)stream);
                });
            await transport.ExpectError<StreamMessageTransport.HeaderTimeoutError>();
        }

        [Test]
        public async Task TestStreamHelloTimeout()
        {
            TestStreamMessageTransport transport = new TestStreamMessageTransport(
                (StreamMessageTransport.ConfigArgs config) =>
                {
                    config.ConnectTimeout = TimeSpan.FromSeconds(1);
                    config.HeaderReadTimeout = TimeSpan.FromSeconds(1);
                    config.ReadTimeout = TimeSpan.FromSeconds(1);
                },
                (CancellationToken ct) =>
                {
                    MockNetworkStream stream = new MockNetworkStream(
                        WireProtocol.EncodeProtocolHeader(ProtocolStatus.ClusterRunning, "TEST")
                        );
                    return Task.FromResult((Stream)stream);
                });
            await transport.ExpectError<StreamMessageTransport.ReadTimeoutError>();
        }

        [Test]
        public async Task TestStreamReadTimeout()
        {
            TestStreamMessageTransport transport = new TestStreamMessageTransport(
                (StreamMessageTransport.ConfigArgs config) =>
                {
                    config.ConnectTimeout = TimeSpan.FromSeconds(1);
                    config.HeaderReadTimeout = TimeSpan.FromSeconds(1);
                    config.ReadTimeout = TimeSpan.FromSeconds(1);
                    config.WriteTimeout = TimeSpan.FromSeconds(10);
                },
                (CancellationToken ct) =>
                {
                    MockNetworkStream stream = new MockNetworkStream(
                        WireProtocol.EncodeProtocolHeader(ProtocolStatus.ClusterRunning, "TEST"),
                        CreateMockServerHello()
                        );
                    return Task.FromResult((Stream)stream);
                });
            await transport.ExpectConnectThenError<StreamMessageTransport.ReadTimeoutError>();
        }

        [Test]
        public async Task TestStreamWriteTimeout()
        {
            TestStreamMessageTransport transport = new TestStreamMessageTransport(
                (StreamMessageTransport.ConfigArgs config) =>
                {
                    config.ConnectTimeout = TimeSpan.FromSeconds(1);
                    config.HeaderReadTimeout = TimeSpan.FromSeconds(1);
                    config.ReadTimeout = TimeSpan.FromSeconds(10);
                    config.WriteTimeout = TimeSpan.FromSeconds(1);
                },
                (CancellationToken ct) =>
                {
                    MockNetworkStream stream = new MockNetworkStream(
                        WireProtocol.EncodeProtocolHeader(ProtocolStatus.ClusterRunning, "TEST"),
                        CreateMockServerHello()
                        );
                    stream.WriteHangs = true;
                    return Task.FromResult((Stream)stream);
                });
            transport.OnConnect += (Handshake.ServerHello hello, TransportHandshakeReport handshakeReport) => { transport.EnqueueSendMessage(new TestMessage()); };
            await transport.ExpectConnectThenError<StreamMessageTransport.WriteTimeoutError>();
        }

        [Test]
        public async Task TestStreamWriteWarning()
        {
            TestStreamMessageTransport transport = new TestStreamMessageTransport(
                (StreamMessageTransport.ConfigArgs config) =>
                {
                    config.ConnectTimeout = TimeSpan.FromSeconds(1);
                    config.HeaderReadTimeout = TimeSpan.FromSeconds(1);
                    config.ReadTimeout = TimeSpan.FromSeconds(10);
                    config.WriteTimeout = TimeSpan.FromSeconds(10);
                    config.WarnAfterWriteDuration = TimeSpan.FromSeconds(1);
                },
                (CancellationToken ct) =>
                {
                    MockNetworkStream stream = new MockNetworkStream(
                        WireProtocol.EncodeProtocolHeader(ProtocolStatus.ClusterRunning, "TEST"),
                        CreateMockServerHello()
                        );
                    stream.WriteHangs = true;
                    return Task.FromResult((Stream)stream);
                });
            transport.OnConnect += (Handshake.ServerHello hello, TransportHandshakeReport handshakeReport) => { transport.EnqueueSendMessage(new TestMessage()); };
            await transport.ExpectConnectThenInfo<StreamMessageTransport.WriteDurationWarningInfo>(info => info.IsBegin, tolerateCycleInfos: true);
        }

        [Test]
        public async Task TestStreamReadWarning()
        {
            TestStreamMessageTransport transport = new TestStreamMessageTransport(
                (StreamMessageTransport.ConfigArgs config) =>
                {
                    config.ConnectTimeout = TimeSpan.FromSeconds(1);
                    config.HeaderReadTimeout = TimeSpan.FromSeconds(1);
                    config.ReadTimeout = TimeSpan.FromSeconds(10);
                    config.WriteTimeout = TimeSpan.FromSeconds(10);
                    config.WarnAfterReadDuration = TimeSpan.FromSeconds(1);
                },
                (CancellationToken ct) =>
                {
                    MockNetworkStream stream = new MockNetworkStream(
                        WireProtocol.EncodeProtocolHeader(ProtocolStatus.ClusterRunning, "TEST"),
                        CreateMockServerHello()
                        );
                    return Task.FromResult((Stream)stream);
                });
            transport.OnConnect += (Handshake.ServerHello hello, TransportHandshakeReport handshakeReport) => { transport.EnqueueSendMessage(new TestMessage()); };
            await transport.ExpectConnectThenInfo<StreamMessageTransport.ReadDurationWarningInfo>(info => info.IsBegin, tolerateCycleInfos: true);
        }

        [Test]
        public async Task TestStreamWriteWarningEnds()
        {
            TestStreamMessageTransport transport = new TestStreamMessageTransport(
                (StreamMessageTransport.ConfigArgs config) =>
                {
                    config.ConnectTimeout = TimeSpan.FromSeconds(1);
                    config.HeaderReadTimeout = TimeSpan.FromSeconds(1);
                    config.ReadTimeout = TimeSpan.FromSeconds(10);
                    config.WriteTimeout = TimeSpan.FromSeconds(10);
                    config.WarnAfterWriteDuration = TimeSpan.FromMilliseconds(500);
                },
                (CancellationToken ct) =>
                {
                    MockNetworkStream stream = new MockNetworkStream(
                        WireProtocol.EncodeProtocolHeader(ProtocolStatus.ClusterRunning, "TEST"),
                        CreateMockServerHello()
                        );
                    stream.WriteTakesTime = TimeSpan.FromSeconds(1);
                    return Task.FromResult((Stream)stream);
                });
            transport.OnConnect += (Handshake.ServerHello hello, TransportHandshakeReport handshakeReport) => { transport.EnqueueSendMessage(new TestMessage()); };

            int state = 0;
            await transport.ExpectConnectThenInfo<StreamMessageTransport.WriteDurationWarningInfo>(info =>
            {
                if (info.IsBegin && state == 0)
                {
                    state = 1;
                    return default;
                }
                else if (info.IsEnd && state == 1)
                    return true;
                else
                    return false;
            }, tolerateCycleInfos: true);
        }

        [Test]
        public async Task TestStreamReadWarningEnds()
        {
            TestStreamMessageTransport transport = new TestStreamMessageTransport(
                (StreamMessageTransport.ConfigArgs config) =>
                {
                    config.ConnectTimeout = TimeSpan.FromSeconds(1);
                    config.HeaderReadTimeout = TimeSpan.FromSeconds(1);
                    config.ReadTimeout = TimeSpan.FromSeconds(10);
                    config.WriteTimeout = TimeSpan.FromSeconds(10);
                    config.WarnAfterReadDuration = TimeSpan.FromMilliseconds(500);
                },
                (CancellationToken ct) =>
                {
                    byte[] header = WireProtocol.EncodeProtocolHeader(ProtocolStatus.ClusterRunning, "TEST");
                    byte[] hello = CreateMockServerHello();
                    byte[] chatter = new byte[4];
                    WireProtocol.EncodePacketHeader(new WirePacketHeader(WirePacketType.Ping, WirePacketCompression.None, 0), chatter);

                    MockNetworkStream stream = new MockNetworkStream(
                        header,
                        hello,
                        chatter
                        );

                    stream.ReadTakesTime = TimeSpan.FromSeconds(1);
                    stream.ReadTakesTimeAfterNumBytes = header.Length + hello.Length;
                    return Task.FromResult((Stream)stream);
                });
            transport.OnConnect += (Handshake.ServerHello hello, TransportHandshakeReport handshakeReport) => { transport.EnqueueSendMessage(new TestMessage()); };

            int state = 0;
            await transport.ExpectConnectThenInfo<StreamMessageTransport.ReadDurationWarningInfo>(info =>
            {
                if (info.IsBegin && state == 0)
                {
                    state = 1;
                    return default;
                }
                else if (info.IsEnd && state == 1)
                    return true;
                else
                    return false;
            }, tolerateCycleInfos: true);
        }

        [Test]
        public async Task TestStreamKeepalive()
        {
            TaskCompletionSource<int> result = new TaskCompletionSource<int>();
            int numKeepalivesObserved = 0;

            TestStreamMessageTransport transport = new TestStreamMessageTransport(
                (StreamMessageTransport.ConfigArgs config) =>
                {
                    config.ConnectTimeout = TimeSpan.FromSeconds(1);
                    config.HeaderReadTimeout = TimeSpan.FromSeconds(1);
                    config.ReadTimeout = TimeSpan.FromSeconds(10);
                    config.WriteTimeout = TimeSpan.FromSeconds(1);
                    config.WriteKeepaliveInterval = TimeSpan.FromMilliseconds(100);
                },
                (CancellationToken ct) =>
                {
                    MockNetworkStream stream = new MockNetworkStream(
                        WireProtocol.EncodeProtocolHeader(ProtocolStatus.ClusterRunning, "TEST"),
                        CreateMockServerHello()
                        );
                    stream.OnWrite += (byte[] buf) =>
                    {
                        WirePacketHeader header = WireProtocol.DecodePacketHeader(buf, 0, enforcePacketPayloadSizeLimit: false);
                        if (header.Type == WirePacketType.Ping)
                        {
                            numKeepalivesObserved++;
                            if (numKeepalivesObserved == 2)
                                result.SetResult(0);
                        }
                        else if (header.Type == WirePacketType.Message)
                        {
                            MetaMessage message = WireProtocol.DecodeMessage(buf, WireProtocol.PacketHeaderSize, header.PayloadSize, null);
                            if (message is Handshake.ClientHello)
                            {
                                // ok
                            }
                            else
                                Assert.Fail($"got unexpected message: {message.GetType().GetNestedClassName()}");
                        }
                        else
                            Assert.Fail("got unexpected message");
                    };
                    return Task.FromResult((Stream)stream);
                });

            transport.Open();
            try
            {
                Task timeout = Task.Delay(TimeSpan.FromSeconds(60));
                Task completed = await Task.WhenAny(result.Task, timeout);
                if (completed == timeout)
                    Assert.Fail("no keepalives detected");
            }
            finally
            {
                transport.Dispose();
            }
        }

        [Test]
        public async Task TestStreamEnqueueCloseDuringOpenStream()
        {
            object myPayload = new object();
            TestStreamMessageTransport transport = new TestStreamMessageTransport(
                (StreamMessageTransport.ConfigArgs config) =>
                {
                    config.ConnectTimeout = TimeSpan.FromSeconds(10);
                },
                async (CancellationToken ct) =>
                {
                    await Task.Delay(2000, ct);
                    return null;
                },
                (IMessageTransport transport_) =>
                {
                    transport_.EnqueueClose(myPayload);
                    return Task.CompletedTask;
                });

            EnqueuedCloseError error = await transport.ExpectError<MessageTransport.EnqueuedCloseError>();
            Assert.IsTrue(ReferenceEquals(myPayload, error.Payload));
        }

        [Test]
        [NonParallelizable]
        public async Task TestStreamEnqueueCloseDuringWrite()
        {
            object myPayload = new object();
            TaskCompletionSource<int> enteredWait = new TaskCompletionSource<int>();
            TestStreamMessageTransport transport = new TestStreamMessageTransport(
                (StreamMessageTransport.ConfigArgs config) =>
                {
                    config.ConnectTimeout = TimeSpan.FromSeconds(1);
                    config.HeaderReadTimeout = TimeSpan.FromSeconds(1);
                    config.ReadTimeout = TimeSpan.FromSeconds(10);
                    config.WriteTimeout = TimeSpan.FromSeconds(10);
                },
                (CancellationToken ct) =>
                {
                    MockNetworkStream stream = new MockNetworkStream(
                        WireProtocol.EncodeProtocolHeader(ProtocolStatus.ClusterRunning, "TEST"),
                        CreateMockServerHello()
                        );
                    stream.OnWrite += (byte[] buf) =>
                    {
                        // hang on the test message
                        WirePacketHeader header = WireProtocol.DecodePacketHeader(buf, 0, enforcePacketPayloadSizeLimit: false);
                        if (header.Type == WirePacketType.Message)
                        {
                            MetaMessage msg = WireProtocol.DecodeMessage(buf, WireProtocol.PacketHeaderSize, header.PayloadSize, resolver: null);
                            if (msg is TestMessage)
                            {
                                enteredWait.TrySetResult(0);
                                stream.WriteTakesTime = TimeSpan.FromSeconds(1);
                            }
                        }
                    };
                    return Task.FromResult((Stream)stream);
                },
                async (IMessageTransport transport_) =>
                {
                    // Enqueue close when the test message is written (and write takes long).
                    await enteredWait.Task;
                    transport_.EnqueueClose(myPayload);
                });
            // send some garbage to trigger a long OnWrite()
            transport.OnConnect += (Handshake.ServerHello hello, TransportHandshakeReport handshakeReport) => { transport.EnqueueSendMessage(new TestMessage()); };
            EnqueuedCloseError error = await transport.ExpectConnectThenError<MessageTransport.EnqueuedCloseError>();
            Assert.IsTrue(ReferenceEquals(myPayload, error.Payload));
        }

        [Test]
        public async Task TestStreamEnqueueCloseFlushes()
        {
            int numTestMessageSeen = 0;
            TestStreamMessageTransport transport = new TestStreamMessageTransport(
                (StreamMessageTransport.ConfigArgs config) =>
                {
                    config.ConnectTimeout = TimeSpan.FromSeconds(10);
                },
                (CancellationToken ct) =>
                {
                    MockNetworkStream stream = new MockNetworkStream(
                        WireProtocol.EncodeProtocolHeader(ProtocolStatus.ClusterRunning, "TEST"),
                        CreateMockServerHello()
                        );
                    stream.OnWrite += (byte[] buf) =>
                    {
                        int offset = 0;
                        while (offset < buf.Length)
                        {
                            WirePacketHeader header = WireProtocol.DecodePacketHeader(buf, offset, enforcePacketPayloadSizeLimit: false);
                            if (header.Type == WirePacketType.Message)
                            {
                                MetaMessage msg = WireProtocol.DecodeMessage(buf, offset + WireProtocol.PacketHeaderSize, header.PayloadSize, resolver: null);
                                if (msg is TestMessage)
                                {
                                    numTestMessageSeen++;
                                }
                            }
                            else
                                Assert.Fail("got unexpected message");
                            offset += WireProtocol.PacketHeaderSize + header.PayloadSize;
                        }
                    };
                    return Task.FromResult((Stream)stream);
                });

            object myPayload = new object();
            transport.OnConnect += (Handshake.ServerHello hello, TransportHandshakeReport handshakeReport) =>
            {
                transport.EnqueueSendMessage(new TestMessage());
                transport.EnqueueSendMessage(new TestMessage());
                transport.EnqueueSendMessage(new TestMessage());
                transport.EnqueueClose(payload: myPayload);
            };

            EnqueuedCloseError error = await transport.ExpectConnectThenError<MessageTransport.EnqueuedCloseError>();
            Assert.AreEqual(3, numTestMessageSeen, "expected 3 testmessage writes");
            Assert.IsTrue(ReferenceEquals(myPayload, error.Payload));
        }

        [Test]
        public async Task TestStreamEnqueueCloseBeforeOpen()
        {
            TestStreamMessageTransport transport = new TestStreamMessageTransport(
                (StreamMessageTransport.ConfigArgs config) =>
                {
                    // nada
                },
                (CancellationToken ct) =>
                {
                    // never gonna get here
                    return Task.FromResult((Stream)null);
                });

            object myPayload = new object();
            TaskCompletionSource<int> result = new TaskCompletionSource<int>();
            transport.OnError += (MessageTransport.Error e) =>
            {
                if (e is MessageTransport.EnqueuedCloseError close)
                {
                    if (ReferenceEquals(close.Payload, myPayload))
                        result.SetResult(0);
                    else
                        result.SetException(new AssertionException($"expected set custom payload"));
                }
                else
                    result.SetException(new AssertionException($"expected error: got {e.GetType().Name}, expected IMessageTransport.EnqueuedCloseError"));
            };

            transport.EnqueueClose(payload: myPayload);
            await await Task.WhenAny(result.Task, Task.Delay(1000).ContinueWith(_ =>
            {
                throw new TimeoutException();
            }, TaskScheduler.Default));
        }
    }
}
