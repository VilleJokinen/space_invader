// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if UNITY_IOS

using AOT;
using Metaplay.Core.Tasks;
using System;
using System.Runtime.InteropServices;

namespace Metaplay.Unity
{
    public static class MetaplayIosWebAuthenticationSession
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void Callback(
            string url,
            string errorString);

        [DllImport("__Internal")]
        static extern void MetaplayIosWebAuthenticationSession_Authenticate(string url, string scheme, [MarshalAs(UnmanagedType.FunctionPtr)]Callback callback);

        public struct Result
        {
            /// <summary>
            /// Callback URL, or <c>null</c> if session failed or was cancelled.
            /// </summary>
            public string Url;

            /// <summary>
            /// Description of errors encountered, or <c>null</c> if there were no errors.
            /// </summary>
            public string ErrorString;
        }
        public delegate void ResultCallback(Result result);
        static ResultCallback _pendingQuery;

        public static void BeginAuthenticate(string url, string scheme, ResultCallback cb)
        {
            if (url == null)
                throw new ArgumentNullException(nameof(url));
            if (scheme == null)
                throw new ArgumentNullException(nameof(scheme));
            if (cb == null)
                throw new ArgumentNullException(nameof(cb));
            if (_pendingQuery != null)
                throw new InvalidOperationException("Another WebAuthenticationSession is already in progress");

            _pendingQuery = cb;
            MetaplayIosWebAuthenticationSession_Authenticate(url, scheme, CompleteAuthenticate);
        }

        [MonoPInvokeCallback(typeof(Callback))]
        static void CompleteAuthenticate(
            string url,
            string errorString)
        {
            Result result = new Result();
            if (!string.IsNullOrEmpty(url))
                result.Url = url;
            else if (!string.IsNullOrEmpty(errorString))
                result.ErrorString = errorString;
            else
                result.ErrorString = "unknown error";

            ResultCallback cb = _pendingQuery;
            _pendingQuery = null;

            MetaTask.Run(() =>
            {
                cb(result);
            }, MetaTask.UnityMainScheduler);
        }
    }
}

#endif
