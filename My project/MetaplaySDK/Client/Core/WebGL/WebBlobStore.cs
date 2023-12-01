// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if UNITY_WEBGL

using AOT;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;

namespace Metaplay.Core
{
    public static class WebBlobStore
    {
        delegate void CallbackDelegate(int reqId, string reply, string errorStr);

        static int                                      _nextRequestId      = 1;
        static Dictionary<int, Action<string, string>>  _callbackHandlers   = new Dictionary<int, Action<string, string>>();
        static Task<bool>                               _initialized;

        public static async Task InitializeAsync()
        {
            if (_initialized == null)
            {
                // Register callback handler
                int reqId = AllocRequestId();
                TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
                _initialized = tcs.Task;
                _callbackHandlers.Add(reqId, (reply, errorStr) =>
                {
                    if (errorStr == null)
                        tcs.TrySetResult(true);
                    else
                        tcs.TrySetException(new InvalidOperationException(errorStr));
                });

                WebBlobStoreJs_Initialize(reqId, MetaplayCore.Options.ProjectName, JavaScriptCallback);
            }

            // Wait for initialization to complete
            await _initialized;
        }

        #region Public API

        public static async Task WriteFileAsync(string filePath, byte[] bytes)
        {
            // Wait for initialization to be done
            await InitializeAsync();

            // Invoke JavaScript (pass in bytes as base64)
            (int reqId, Task<string> task) = RegisterRequestHandler();
            // Debug.Log($"WebBlobStore.WriteFileAsync({filePath}): reqId={reqId}");
            WebBlobStoreJs_WriteBlob(reqId, filePath, Convert.ToBase64String(bytes));
            _ = await task; // returns "success"
        }

        public static async Task<byte[]> ReadFileAsync(string filePath)
        {
            // Wait for initialization to be done
            await InitializeAsync();

            // Invoke JavaScript & convert base64 to byte[] -- or pass on null for non-existent file
            (int reqId, Task<string> task) = RegisterRequestHandler();
            WebBlobStoreJs_ReadBlob(reqId, filePath);
            string result64 = await task;
            return (result64 != null) ? Convert.FromBase64String(result64) : null;
        }

        public static async Task DeleteFileAsync(string filePath)
        {
            // Wait for initialization to be done
            await InitializeAsync();

            // Invoke JavaScript (returns bytes as base64)
            (int reqId, Task<string> task) = RegisterRequestHandler();
            WebBlobStoreJs_DeleteBlob(reqId, filePath);
            _ = await task; // returns "success"
        }

        public static async Task<string[]> ScanBlobsInDirectory(string directoryPath, bool recursive)
        {
            // Wait for initialization to be done
            await InitializeAsync();

            // Invoke JavaScript & return result
            (int reqId, Task<string> task) = RegisterRequestHandler();
            WebBlobStoreJs_ScanBlobsInDirectory(reqId, directoryPath, recursive);
            string result = await task;
            if (result == "")
                return Array.Empty<string>();
            return result.Split("//");
        }

        #endregion

        static int AllocRequestId()
        {
            return _nextRequestId++;
        }

        #region JavaScript bridge methods

        /// <summary>
        /// Allocate JavaScript invocation requestId and register a TaskCompletionSource for the
        /// callback. The actual JS invocation needs to be done outside of this method.
        /// </summary>
        /// <returns></returns>
        static (int reqId, Task<string> task) RegisterRequestHandler()
        {
            int reqId = AllocRequestId();
            TaskCompletionSource<string> tcs = new TaskCompletionSource<string>();
            _callbackHandlers.Add(reqId, (reply, errorStr) =>
            {
                if (errorStr == null)
                    tcs.TrySetResult(reply);
                else
                    tcs.TrySetException(new InvalidOperationException(errorStr));
            });

            return (reqId, tcs.Task);
        }

        [MonoPInvokeCallback(typeof(CallbackDelegate))]
        public static void JavaScriptCallback(int reqId, string reply, string errorStr)
        {
            if (_callbackHandlers.Remove(reqId, out Action<string, string> handler))
                handler(reply, errorStr);
            else
                Debug.LogWarning($"WebBlobStore.JavaScriptCallback(): No handler for requestId #{reqId}");
        }

        [DllImport("__Internal")] static extern void WebBlobStoreJs_Initialize(int reqId, string projectName, CallbackDelegate callback);
        [DllImport("__Internal")] static extern void WebBlobStoreJs_WriteBlob(int reqId, string filePath, string bytes64);
        [DllImport("__Internal")] static extern void WebBlobStoreJs_ReadBlob(int reqId, string filePath);
        [DllImport("__Internal")] static extern void WebBlobStoreJs_DeleteBlob(int reqId, string filePath);
        [DllImport("__Internal")] static extern void WebBlobStoreJs_ScanBlobsInDirectory(int reqId, string directoryPath, bool recursive);

        #endregion
    }

}

#endif // Unity WebGL build
