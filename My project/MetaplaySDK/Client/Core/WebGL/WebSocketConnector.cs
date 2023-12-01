// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if UNITY_WEBGL
using AOT;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Metaplay.Core
{
    public static class WebSocketConnector
    {
        public delegate void SocketError(string errorStr);
        public delegate void SocketClosed(int code, string reason, bool wasClean);
        public delegate void SocketMessage(byte[] msg);

        class SocketState
        {
            public TaskCompletionSource<bool> ConnectTcs = new TaskCompletionSource<bool>();
            public string                     CurrentError;
            public bool                       IsConnected = false;

            // callback functions
            public SocketError OnError;
            public SocketClosed OnClosed;
            public SocketMessage OnReceive;
        }

        delegate void MessageCallbackDelegate(int connId, System.IntPtr msgPtr, int msgLength);
        delegate void OpenCallbackDelegate(int connId);
        delegate void CloseCallbackDelegate(int connId, int code, string reason, bool wasClean);
        delegate void ErrorCallbackDelegate(int connId, string errorStr);

        static readonly Dictionary<int, SocketState> _sockets = new Dictionary<int, SocketState>();
        static int _nextConnIdx = 0;

        public static void Initialize()
        {
            WebSocketConnectorJs_Initialize(CloseCallback, ErrorCallback, OpenCallback, MessageCallback);
        }

        public static async Task<int> Connect(string url, SocketError errorCallback, SocketClosed closedCallback, SocketMessage messageCallback)
        {
            SocketState socket = new SocketState()
            {
                OnError = errorCallback,
                OnClosed = closedCallback,
                OnReceive = messageCallback
            };

            int connectionId = _nextConnIdx++;
            _sockets[connectionId] = socket;

            WebSocketConnectorJs_Open(connectionId, url);

            bool result = await socket.ConnectTcs.Task;
            socket.ConnectTcs = null;

            if (!result)
            {
                // Remove user callbacks, socket state will be cleaned up in close callback
                socket.OnError = null;
                socket.OnClosed = null;
                socket.OnReceive = null;
                throw new InvalidOperationException(socket.CurrentError);
            }

            return connectionId;
        }

        static void DisposeState(int connId)
        {
            _sockets.Remove(connId);
        }

        public static void Close(int connId)
        {
            if (!_sockets.TryGetValue(connId, out SocketState socket))
                throw new InvalidOperationException($"Cannot close a non-existent socket with id {connId}");

            if (!socket.IsConnected)
            {
                // JS counterpart already closed, can dispose immediately
                DisposeState(connId);
                return;
            }

            // Request JS socket close, don't communicate further socket events to user
            socket.OnClosed = null;
            socket.OnReceive = null;
            socket.OnError = null;
            WebSocketConnectorJs_Close(connId, 1000, "Connection closed by client");
        }

        public static void Send(int connId, byte[] data)
        {
            Send(connId, data, 0, data.Length);
        }

        public static void Send(int connId, byte[] data, int dataStart, int dataLength)
        {
            if (!_sockets.TryGetValue(connId, out SocketState socket))
                throw new InvalidOperationException($"Cannot send to a non-existent socket with id {connId}");
            if (!socket.IsConnected)
                throw new InvalidOperationException("Cannot send data when connection is not open!");
            WebSocketConnectorJs_Send(connId, data, dataStart, dataLength);
        }

        [MonoPInvokeCallback(typeof(MessageCallbackDelegate))]
        static void MessageCallback(int connId, System.IntPtr msgPtr, int msgLength)
        {
            if (!_sockets.TryGetValue(connId, out SocketState socket))
                throw new InvalidOperationException($"Message callback received for non-existent socket with id {connId}");

            byte[] data = new byte[msgLength];
            Marshal.Copy(msgPtr, data, 0, msgLength);
            socket.OnReceive?.Invoke(data);
        }

        [MonoPInvokeCallback(typeof(OpenCallbackDelegate))]
        static void OpenCallback(int connId)
        {
            if (!_sockets.TryGetValue(connId, out SocketState socket))
                throw new InvalidOperationException($"Open callback received for non-existent socket with id {connId}");

            socket.IsConnected = true;
            socket.ConnectTcs.SetResult(true);
        }

        [MonoPInvokeCallback(typeof(CloseCallbackDelegate))]
        static void CloseCallback(int connId, int code, string reason, bool wasClean)
        {
            if (!_sockets.TryGetValue(connId, out SocketState socket))
                throw new InvalidOperationException($"Close callback received for non-existent socket with id {connId}");

            socket.IsConnected = false;
            if (socket.OnClosed != null)
            {
                socket.OnClosed(code, reason, wasClean);
            }
            else
            {
                DisposeState(connId);
            }
        }

        [MonoPInvokeCallback(typeof(ErrorCallbackDelegate))]
        static void ErrorCallback(int connId, string errorStr)
        {
            if (!_sockets.TryGetValue(connId, out SocketState socket))
                throw new InvalidOperationException($"Error callback received for non-existent socket with id {connId}");

            if (socket.ConnectTcs != null)
            {
                // in the middle of connecting, report error via Connect() return value
                socket.CurrentError = errorStr;
                socket.ConnectTcs.SetResult(false);
            }
            else
            {
                socket.OnError?.Invoke(errorStr);
            }
        }

        [DllImport("__Internal")] static extern void WebSocketConnectorJs_Initialize(
            CloseCallbackDelegate closeCallback,
            ErrorCallbackDelegate errorCallback,
            OpenCallbackDelegate openCallback,
            MessageCallbackDelegate messageCallback);
        [DllImport("__Internal")] static extern void WebSocketConnectorJs_Open(int connId, string url);
        [DllImport("__Internal")] static extern void WebSocketConnectorJs_Close(int connId, int code, string reason);
        [DllImport("__Internal")] static extern void WebSocketConnectorJs_Send(int connId, byte[] data, int start, int size);
    }
}
#endif
