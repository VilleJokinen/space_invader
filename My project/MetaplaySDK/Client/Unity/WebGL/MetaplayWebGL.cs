// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if UNITY_WEBGL

using AOT;
using Metaplay.Core;
using System.Runtime.InteropServices;

namespace Metaplay.Unity
{
    public static class MetaplayWebGL
    {
        public delegate void OnApplicationQuitDelegate();

        static OnApplicationQuitDelegate _onApplicationQuitCallback;

        public static void Initialize(OnApplicationQuitDelegate onApplicationQuitCallback)
        {
            // Register application quit callback
            _onApplicationQuitCallback = onApplicationQuitCallback;
            MetaplayWebGLJs_Initialize(JavascriptOnApplicationQuitCallback);

            // Initialize WebGL sub-systems
#if !UNITY_EDITOR
            WebSocketConnector.Initialize();
            WebGLTimerManager.Initialize();
#endif
            WebApiBridge.Initialize();
            AtomicBlobStore.Initialize();
            _ = WebBlobStore.InitializeAsync();
            // \todo [petri] better error handling if async initialization fails
        }

        [MonoPInvokeCallback(typeof(OnApplicationQuitDelegate))]
        static void JavascriptOnApplicationQuitCallback()
        {
            _onApplicationQuitCallback?.Invoke();
        }

        [DllImport("__Internal")] static extern string MetaplayWebGLJs_Initialize(OnApplicationQuitDelegate onApplicationQuitCallback);
    }
}

#endif
