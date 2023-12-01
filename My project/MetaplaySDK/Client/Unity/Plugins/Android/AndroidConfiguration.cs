// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if UNITY_ANDROID

using Metaplay.Core.Tasks;
using System.Threading.Tasks;
using UnityEngine;

namespace Metaplay.Unity
{
    public static class AndroidConfiguration
    {
        static string _ssaid;

        /// <summary>
        /// Retrieves the (Settings Secure) ANDROID_ID, i.e. SSAID. If there is no
        /// SSAID, returns <i>EMPTY STRING</i>.
        /// </summary>
        public static async Task<string> GetAndroidIdAsync()
        {
            if (_ssaid != null)
                return _ssaid;

            return await MetaTask.Run(FetchAndroidIdOnJNIThread, MetaTask.BackgroundScheduler);
        }

        static string FetchAndroidIdOnJNIThread()
        {
            _ = AndroidJNI.AttachCurrentThread();
            try
            {
                string ssaid;
                using (AndroidJavaClass clazz = new AndroidJavaClass("io.metaplay.android.configuration.AndroidConfiguration"))
                {
                    ssaid = clazz.CallStatic<string>("GetAndroidId");
                    if (ssaid == null)
                        ssaid = "";
                    _ssaid = ssaid;
                }
                return ssaid;
            }
            finally
            {
                _ = AndroidJNI.DetachCurrentThread();
            }
        }
    }
}

#endif
