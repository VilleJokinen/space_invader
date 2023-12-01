// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if UNITY_IOS

using Metaplay.Core.Tasks;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using static System.FormattableString;

namespace Metaplay.Unity
{
    public static class MetaplayIosKeychain
    {
        [DllImport("__Internal")]
        static extern int MetaplayIosKeychain_SetGenericPassword(int storage, string service, string account, IntPtr bytes, int numBytes);

        [DllImport("__Internal")]
        static extern int MetaplayIosKeychain_GetGenericPassword(int storage, string service, string account, ref IntPtr bytes, ref int numBytes);

        [DllImport("__Internal")]
        static extern void MetaplayIosKeychain_GetGenericPassword_ReleaseBuf(IntPtr ptr);

        [DllImport("__Internal")]
        static extern int MetaplayIosKeychain_ClearGenericPassword(int storage, string service, string account);

        const int OneMegabyteInBytes = 1024*1024; // 1MB

        public enum KeychainStorage
        {
            /// <summary>
            /// Key storage where keys are stored locally on device. These are not synchronized with other devices.
            /// </summary>
            DeviceLocalStorage = 0,

            /// <summary>
            /// Key storage where keys may be synchronized with user's other devices via thei iCloud account.
            /// </summary>
            IcloudSynchronizableStorage = 1,
        }

        /// <summary>
        /// Stores a generic password into the keychain. A password is identified by the combination of Service and Account identifiers.
        /// </summary>
        public static Task SetGenericPasswordAsync(KeychainStorage storage, string service, string account, byte[] bytes)
        {
            // Keychain access blocks the thread. Run in thread pool to avoid accidentally blocking unity thread.
            return MetaTask.Run(() =>
            {
                if (bytes == null)
                    throw new ArgumentNullException(nameof(bytes));

                // This is not a hard limit, but keychain can become unstable with large objects.
                if (bytes.Length > OneMegabyteInBytes)
                    throw new ArgumentException("Keychain value may not exceed 1MB", nameof(bytes));

                IntPtr ptr = IntPtr.Zero;
                int result;
                try
                {
                    ptr = Marshal.AllocHGlobal(bytes.Length);
                    Marshal.Copy(source: bytes, startIndex: 0, destination: ptr, length: bytes.Length);
                    result = MetaplayIosKeychain_SetGenericPassword((int)storage, service, account, ptr, bytes.Length);
                }
                finally
                {
                    Marshal.FreeHGlobal(ptr);
                }

                if (result != 0)
                    throw new InvalidOperationException(Invariant($"keychain write failed with code: {result}"));

            }, MetaTask.BackgroundScheduler);
        }

        /// <summary>
        /// Retrieves a generic password from the keychain. A password is identified by the combination of Service and Account identifiers.
        /// If no password is found, returns null. On failure, throws.
        /// </summary>
        public static Task<byte[]> TryGetGenericPasswordAsync(KeychainStorage storage, string service, string account)
        {
            return MetaTask.Run(() =>
            {
                IntPtr outputBufPtr = IntPtr.Zero;
                int outputBufLen = 0;
                try
                {
                    int result = MetaplayIosKeychain_GetGenericPassword((int)storage, service, account, ref outputBufPtr, ref outputBufLen);
                    if (result != 0)
                        throw new InvalidOperationException(Invariant($"keychain read failed with code: {result}"));

                    // Not found?
                    if (outputBufLen == 0 || outputBufPtr == IntPtr.Zero)
                        return null;

                    // Sanity
                    if (outputBufLen < 0 || outputBufLen > OneMegabyteInBytes)
                        throw new InvalidOperationException(Invariant($"keychain read failed. Too large: {outputBufLen}"));

                    // Success.
                    byte[] data = new byte[outputBufLen];
                    Marshal.Copy(source: outputBufPtr, destination: data, startIndex: 0, length: outputBufLen);
                    return data;
                }
                finally
                {
                    if (outputBufPtr != IntPtr.Zero)
                        MetaplayIosKeychain_GetGenericPassword_ReleaseBuf(outputBufPtr);
                }
            });
        }

        /// <summary>
        /// Clears a generic password in the keychain identified by the combination of Service and Account identifiers.
        /// </summary>
        public static Task ClearGenericPasswordAsync(KeychainStorage storage, string service, string account)
        {
            return MetaTask.Run(() =>
            {
                int result = MetaplayIosKeychain_ClearGenericPassword((int)storage, service, account);

                // Delete success
                if (result == 0)
                    return;
                // errSecItemNotFound (didn't exist before, this is fine too).
                if (result == -25300)
                    return;

                throw new InvalidOperationException(Invariant($"keychain clear failed with code: {result}"));

            }, MetaTask.BackgroundScheduler);
        }

        /// <summary>
        /// Default service name for password storage. There's really no "service" here, so let's use it as a namespace
        /// for metaplay related keys. The keys are app-scoped so bundle name is not strictly required. We use it
        /// anyway to make metaplay-related keys obvious if keys are ever inspected.
        /// </summary>
        public static string GetDefaultGenericPasswordServiceName()
        {
            return $"{MetaplaySDK.AppleBundleId}.metaplay";
        }
    }
}

#endif
