// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using System;

namespace Metaplay.Unity
{
    /// <summary>
    /// Utility for getting <see cref="ClientPlatform"/> on Unity.
    /// </summary>
    public static class ClientPlatformUnityUtil
    {
#if UNITY_EDITOR
        /// <summary>
        /// Editor only internal hook.
        /// </summary>
        public static Func<ClientPlatform> EditorHookGetClientPlatform = null;
#endif

        public static ClientPlatform GetRuntimePlatform()
        {
#if UNITY_EDITOR
            if (EditorHookGetClientPlatform != null)
                return EditorHookGetClientPlatform();
            return ClientPlatform.UnityEditor;
#elif UNITY_IOS
            return ClientPlatform.iOS;
#elif UNITY_ANDROID
            return ClientPlatform.Android;
#elif UNITY_WEBGL
            return ClientPlatform.WebGL;
#else
            return ClientPlatform.Unknown;
#endif
        }
    }
}
