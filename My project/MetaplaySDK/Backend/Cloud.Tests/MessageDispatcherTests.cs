// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Message;
using Metaplay.Core.Model;
using NUnit.Framework;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Cloud.Tests
{
    public class MessageDispatcherTests
    {
        [MetaMessage(30002, MessageDirection.ClientInternal)]
        public class TestMessage : MetaMessage
        {
            public TestMessage() { }
        }

        [MetaSerializableDerived(2001)]
        public class TestRequest : MetaRequest
        {
            public bool AutoResponse { get; private set; }
            public TestRequest() { }
            public TestRequest(bool autoResponse) { AutoResponse = autoResponse; }
        }

        [MetaSerializableDerived(2002)]
        public class TestResponse : MetaResponse
        {
            public TestResponse() { }
        }

        class TestMessageDispatcher : BasicMessageDispatcher
        {
            public TestMessageDispatcher() : base(LogChannel.Empty)
            {
            }

            protected override bool SendMessageInternal(MetaMessage message)
            {
                if (message is SessionMetaRequestMessage req && req.Payload is TestRequest payload)
                {
                    if (payload.AutoResponse)
                        _ = Task.Run(() => DispatchMessage(new SessionMetaResponseMessage() { Payload = new TestResponse(), RequestId = req.Id }));
                    return true;
                }
                throw new System.NotImplementedException();
            }

            public new bool DispatchMessage(MetaMessage message)
            {
                return base.DispatchMessage(message);
            }
        }

        [Test]
        public void TestAddMany()
        {
            TestMessageDispatcher dispatcher = new TestMessageDispatcher();
            int invokeCount = 0;

            void Handler(TestMessage msg)
            {
                invokeCount++;
            }

            dispatcher.DispatchMessage(new TestMessage());
            dispatcher.AddListener<TestMessage>(Handler);
            Assert.AreEqual(0, invokeCount);

            dispatcher.DispatchMessage(new TestMessage());
            Assert.AreEqual(1, invokeCount);

            dispatcher.AddListener<TestMessage>(Handler);
            dispatcher.DispatchMessage(new TestMessage());
            Assert.AreEqual(3, invokeCount);

            dispatcher.RemoveListener<TestMessage>(Handler);
            dispatcher.DispatchMessage(new TestMessage());
            Assert.AreEqual(4, invokeCount);

            dispatcher.RemoveListener<TestMessage>(Handler);
            dispatcher.DispatchMessage(new TestMessage());
            Assert.AreEqual(4, invokeCount);
        }

        [Test]
        public void TestThrowingHandler()
        {
            TestMessageDispatcher dispatcher = new TestMessageDispatcher();
            int invokeCountA = 0;
            int invokeCountB = 0;

            void NormalHandlerA(TestMessage msg)
            {
                invokeCountA++;
            }
            void NormalHandlerB(TestMessage msg)
            {
                invokeCountB++;
            }
            void ThrowingHandler(TestMessage msg)
            {
                throw new InvalidOperationException();
            }

            dispatcher.AddListener<TestMessage>(NormalHandlerA);
            dispatcher.AddListener<TestMessage>(ThrowingHandler);
            dispatcher.AddListener<TestMessage>(NormalHandlerB);

            dispatcher.DispatchMessage(new TestMessage());
            Assert.AreEqual(1, invokeCountA);
            Assert.AreEqual(1, invokeCountB);

            dispatcher.DispatchMessage(new TestMessage());
            Assert.AreEqual(2, invokeCountA);
            Assert.AreEqual(2, invokeCountB);
        }

        [Test]
        public void TestAddInHandler()
        {
            TestMessageDispatcher dispatcher = new TestMessageDispatcher();
            int invokeCountA = 0;
            int invokeCountB = 0;

            void NormalHandlerA(TestMessage msg)
            {
                invokeCountA++;
                dispatcher.AddListener<TestMessage>(NormalHandlerB);
            }
            void NormalHandlerB(TestMessage msg)
            {
                invokeCountB++;
            }

            dispatcher.AddListener<TestMessage>(NormalHandlerA);
            dispatcher.DispatchMessage(new TestMessage());
            Assert.AreEqual(1, invokeCountA);
            Assert.AreEqual(0, invokeCountB);

            dispatcher.DispatchMessage(new TestMessage());
            Assert.AreEqual(2, invokeCountA);
            Assert.AreEqual(1, invokeCountB);
        }

        [Test]
        public void TestRemoveInHandler()
        {
            TestMessageDispatcher dispatcher = new TestMessageDispatcher();
            int invokeCountA = 0;
            int invokeCountB = 0;

            void NormalHandlerA(TestMessage msg)
            {
                invokeCountA++;
                dispatcher.RemoveListener<TestMessage>(NormalHandlerB);
            }
            void NormalHandlerB(TestMessage msg)
            {
                invokeCountB++;
            }

            dispatcher.AddListener<TestMessage>(NormalHandlerA);
            dispatcher.AddListener<TestMessage>(NormalHandlerB);
            dispatcher.DispatchMessage(new TestMessage());
            Assert.AreEqual(1, invokeCountA);
            Assert.AreEqual(1, invokeCountB);

            dispatcher.DispatchMessage(new TestMessage());
            Assert.AreEqual(2, invokeCountA);
            Assert.AreEqual(1, invokeCountB);
        }

        [Test]
        public void TestAddAndRemoveInHandler()
        {
            TestMessageDispatcher dispatcher = new TestMessageDispatcher();
            int invokeCountA = 0;
            int invokeCountB = 0;
            int invokeCountC = 0;

            void NormalHandlerA(TestMessage msg)
            {
                invokeCountA++;
                dispatcher.RemoveListener<TestMessage>(NormalHandlerB);
                dispatcher.AddListener<TestMessage>(NormalHandlerC);
            }
            void NormalHandlerB(TestMessage msg)
            {
                invokeCountB++;
            }
            void NormalHandlerC(TestMessage msg)
            {
                invokeCountC++;
            }

            dispatcher.AddListener<TestMessage>(NormalHandlerA);
            dispatcher.AddListener<TestMessage>(NormalHandlerB);
            dispatcher.DispatchMessage(new TestMessage());
            Assert.AreEqual(1, invokeCountA);
            Assert.AreEqual(1, invokeCountB);
            Assert.AreEqual(0, invokeCountC);

            dispatcher.DispatchMessage(new TestMessage());
            Assert.AreEqual(2, invokeCountA);
            Assert.AreEqual(1, invokeCountB);
            Assert.AreEqual(1, invokeCountC);
        }

        [Test]
        public void TestHandlerOrder()
        {
            TestMessageDispatcher dispatcher = new TestMessageDispatcher();

            int[] counter = new int[100];
            for (int i = 0; i < counter.Length; ++i)
            {
                int capturedI = i;
                dispatcher.AddListener<TestMessage>((TestMessage msg) => { counter[capturedI] = capturedI == 0 ? 0 : counter[capturedI - 1] + 1; });
            }

            dispatcher.DispatchMessage(new TestMessage());

            for (int i = 0; i < counter.Length; ++i)
                Assert.AreEqual(i, counter[i]);
        }

        [Test]
        public async Task TestMetaRequest()
        {
            TestMessageDispatcher dispatcher = new TestMessageDispatcher();
            TestResponse response = await dispatcher.SendRequestAsync<TestResponse>(new TestRequest(true));
            Assert.IsNotNull(response);
        }

        [Test]
        public void TestMetaRequestCancel()
        {
            TestMessageDispatcher dispatcher = new TestMessageDispatcher();

            CancellationTokenSource cts = new CancellationTokenSource();
            Task<TestResponse> task = dispatcher.SendRequestAsync<TestResponse>(new TestRequest(false), cts.Token);
            cts.Cancel();
            Assert.ThrowsAsync<TaskCanceledException>(async () => await task);
        }
    }
}
