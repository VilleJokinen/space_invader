// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Message;
using Metaplay.Core.Network;
using Metaplay.Core.Serialization;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Metaplay.Unity
{
    public class OfflineServerTransport : MessageTransport
    {
        LogChannel _log = MetaplaySDK.Logs.CreateChannel("offline-transport");
        IOfflineServer _server;
        bool _isConnected;
        bool _openEnqueued;
        bool _closeEnqueued;
        object _closeErrorPayload;
        List<MetaMessage> _incomingMessages = new List<MetaMessage>();
        List<Info> _outgoingInfos = new List<Info>();
        TaskCompletionSource<int> _updateCompletion;

        public OfflineServerTransport(IOfflineServer offlineServer)
        {
            _server = offlineServer;
            offlineServer.RegisterTransport(this);
        }

        public void Update()
        {
            if (_openEnqueued && !_isConnected)
            {
                _isConnected = true;

                InvokeOnConnect(
                    serverHello: new Handshake.ServerHello(
                                    MetaplaySDK.BuildVersion.Version,
                                    "offline",
                                    MetaSerializerTypeRegistry.FullProtocolHash,
                                    commitId: null),
                    transportHandshake: new TransportHandshakeReport(
                        chosenHostname:     "offline",
                        chosenProtocol:     System.Net.Sockets.AddressFamily.InterNetwork,
                        tlsPeerDescription: "offline"));

                _incomingMessages.Insert(0, new Handshake.ClientHello(
                        clientVersion: MetaplaySDK.BuildVersion.Version,
                        buildNumber: MetaplaySDK.BuildVersion.BuildNumber,
                        supportedLogicVersions: MetaplayCore.Options.SupportedLogicVersions,
                        fullProtocolHash: MetaSerializerTypeRegistry.FullProtocolHash,
                        commitId: MetaplaySDK.BuildVersion.CommitId,
                        timestamp: MetaTime.Now,
                        appLaunchId: BitConverter.ToUInt32(MetaplaySDK.AppLaunchId.ToByteArray(), startIndex: 0),
                        clientSessionNonce: 123,
                        clientSessionConnectionNdx: 0,
                        platform: ClientPlatformUnityUtil.GetRuntimePlatform(),
                        loginProtocolVersion: MetaplayCore.LoginProtocolVersion));
            }

            int numInfos = _outgoingInfos.Count;
            for (int ndx = 0; ndx < numInfos; ++ndx)
                InvokeOnInfo(_outgoingInfos[ndx]);
            _outgoingInfos.RemoveRange(0, numInfos);

            // Don't process messages generated in after update started.
            // On error, remove the message that caused the error from the queue.
            int numMessages = _incomingMessages.Count;
            int numMessagesHandled = 0;
            try
            {
                for (int ndx = 0; ndx < numMessages; ++ndx)
                {
                    numMessagesHandled = ndx + 1;
                    _server.HandleMessage(_incomingMessages[ndx]);
                }
            }
            finally
            {
                _incomingMessages.RemoveRange(0, numMessagesHandled);
            }

            if (_updateCompletion != null)
            {
                _updateCompletion.TrySetResult(0);
                _updateCompletion = null;
            }

            if (_closeEnqueued && _isConnected)
            {
                _isConnected = false;
                InvokeOnError(new MessageTransport.EnqueuedCloseError(_closeErrorPayload));
            }
        }

        public void SendToClient(MetaMessage message)
        {
            InvokeOnReceive(message);
        }

        public override void Dispose()
        {
            _isConnected = false;
            _openEnqueued = false;
            _closeEnqueued = false;
        }

        public override void EnqueueClose(object closedErrorPayload)
        {
            if (!_closeEnqueued)
            {
                _closeEnqueued = true;
                _closeErrorPayload = closedErrorPayload;
            }
        }

        public override void EnqueueInfo(Info info)
        {
            _outgoingInfos.Add(info);
        }

        public override void EnqueueSendMessage(MetaMessage message)
        {
            if (!_isConnected)
            {
                _log.Error("Game attempted to send message {MessageType} but game is not connected to the (offline) server. Message dropped.", message.GetType().ToGenericTypeString());
                return;
            }
            _incomingMessages.Add(message);
        }

        public override MessageTransportWriteFence EnqueueWriteFence()
        {
            if (_updateCompletion == null)
                _updateCompletion = new TaskCompletionSource<int>();
            return new MessageTransportWriteFence(_updateCompletion.Task);
        }

        public override void Open()
        {
            _openEnqueued = true;
        }

        public override void SetDebugDiagnosticsRef(LoginTransportDebugDiagnostics debugDiagnostics)
        {
        }
    }
}
