// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Akka.IO;
using Metaplay.Cloud.Entity;
using Metaplay.Core;
using Microsoft.AspNetCore.Connections;
using System;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace Metaplay.Server.WebSockets
{
    public class WebSocketClientConnection : ClientConnection
    {
        readonly WebSocket            _socket;
        readonly TaskCompletionSource _socketFinished;

        class SendBytesCommand
        {
            public ByteString Data { get; private set; }

            public SendBytesCommand(ByteString data)
            {
                Data = data;
            }
        }

        class CloseSocketCommand
        {
            public static CloseSocketCommand Instance = new CloseSocketCommand();
        }

        public WebSocketClientConnection(
            EntityId entityId,
            IPAddress srcAddress,
            int localPort,
            WebSocket socket,
            TaskCompletionSource socketFinished) : base(entityId, srcAddress, localPort)
        {
            _socket         = socket;
            _socketFinished = socketFinished;
        }

        protected override Task Initialize()
        {
            _ = ReceiveLoopAsync();
            return Task.CompletedTask;
        }

        protected override void PostStop()
        {
            if (_socket.State == WebSocketState.Open) // Socket should not be open anymore.
            {
                _log.Debug("Closing WebSocket abnormally...");
                _socket.Abort();
            }
            // Set socket finished even if it was closed already, just in case.
            _socketFinished?.TrySetResult();
            base.PostStop();
        }

        protected override void CloseSocket()
        {
            _self.Tell(CloseSocketCommand.Instance);
        }

        protected override void WriteBytesToSocket(ByteString data)
        {
            _self.Tell(new SendBytesCommand(data));
        }

        async Task ReceiveLoopAsync()
        {
            byte[] receiveBuffer = new byte[1024];
            try
            {
                for (;;)
                {
                    WebSocketReceiveResult result;
                    try
                    {
                        result = await _socket.ReceiveAsync(receiveBuffer, CancellationToken.None);
                    }
                    catch (WebSocketException ex) when (ex.InnerException is ConnectionResetException)
                    {
                        _log.Warning("Websocket connection was reset without shutdown handshake.");
                        await GracefulStopAsync();
                        return;
                    }
                    catch (Exception ex)
                    {
                        _log.Warning("Websocket failure: {ex}", ex);
                        await GracefulStopAsync();
                        return;
                    }

                    // Handle result

                    if (result.CloseStatus.HasValue)
                    {
                        if (_sessionId.IsValid)
                            _log.Debug("WebSocket Connection closed by client: {CloseStatus}, {CloseDescription}", result.CloseStatus, result.CloseStatusDescription);

                        await GracefulStopAsync();
                        return;
                    }

                    // \todo [nomi] Optimize ByteString usage and allocations.
                    await OnReceiveSocketData(ByteString.CopyFrom(receiveBuffer, 0, result.Count));
                }
            }
            catch (Exception ex)
            {
                _log.Error("Unexpected error: {ex}", ex);
                await GracefulStopAsync();
            }
        }

        [CommandHandler]
        async Task HandleClose(CloseSocketCommand close)
        {
            try
            {
                // \note: Continue on Threadpool so that if actor scheduler is torn down the task still continues.
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Tolerate close failures. They are mostly caused by connection loss before shutdown handshake completion.
            }
            _socketFinished?.TrySetResult();
        }

        [CommandHandler]
        async Task HandleSend(SendBytesCommand send)
        {
            if (_socket.State == WebSocketState.Open)
            {
                // \todo [nomi] This blocks message processing. Probably not ok.
                // Socket can only handle a single SendAsync in parallel.
                await _socket.SendAsync(
                    send.Data.ToArray(),
                    WebSocketMessageType.Binary,
                    true, CancellationToken.None);
            }
            else
                _log.Warning("Trying to send a WebSocket message while socket is in state {State}", _socket.State);
        }
    }
}
