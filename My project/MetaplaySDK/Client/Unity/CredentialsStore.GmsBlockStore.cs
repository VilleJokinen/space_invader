// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if UNITY_ANDROID

using Metaplay.Core.Model;
using Metaplay.Core.Serialization;
using Metaplay.Unity.Android;
using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Metaplay.Unity
{
    /// <summary>
    /// GMS Block Store is not available on device. The phone is non-google android.
    /// </summary>
    class NoGmsBlockStoreAvailableOnDeviceError : Exception
    {
    }

    partial class CredentialsStore
    {
        public class GmsBlockStoreStore
        {
            const string GmsBlockStoreLabel = "metaplay.deviceauth";

            [MetaSerializable]
            public class DeviceAuthBlock
            {
                [MetaMember(1)] public int Version;
                [MetaMember(2)] public string AndroidId;
                [MetaMember(3)] public byte[] Credentials;
            }

            /// <summary>
            /// Retrieves the credentials from the block store. If there are no credentials stored, return null. On failure, throws.
            /// </summary>
            public static async Task<GuestCredentials> TryGetGuestCredentialsAsync()
            {
                byte[] blob;
                try
                {
                    blob = await MetaplayGmsBlockStore.ReadBlockAsync(label: GmsBlockStoreLabel);
                }
                catch (AndroidJavaException ex)
                {
                    TransformKnownExceptionToDescriptiveException(ex);
                    throw;
                }
                if (blob == null || blob.Length == 0)
                    return null;

                DeviceAuthBlock block = MetaSerialization.DeserializeTagged<DeviceAuthBlock>(blob, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);
                if (block.Version != 1)
                    return null;

                string deviceAndroidId = await AndroidConfiguration.GetAndroidIdAsync();
                if (block.AndroidId != deviceAndroidId)
                    return null;

                return GuestCredentialsSerializer.TryDeserialize(block.Credentials);
            }

            /// <summary>
            /// Stores the credentials into block store. On failure, throws.
            /// </summary>
            public static async Task StoreGuestCredentialsAsync(GuestCredentials credentials)
            {
                string deviceAndroidId = await AndroidConfiguration.GetAndroidIdAsync();
                DeviceAuthBlock block = new DeviceAuthBlock()
                {
                    Version = 1,
                    AndroidId = deviceAndroidId,
                    Credentials = GuestCredentialsSerializer.Serialize(credentials)
                };

                byte[] blob = MetaSerialization.SerializeTagged<DeviceAuthBlock>(block, MetaSerializationFlags.IncludeAll, logicVersion: null);

                try
                {
                    await MetaplayGmsBlockStore.WriteBlockAsync(label: GmsBlockStoreLabel, blob, shouldBackupToCloud: false);
                }
                catch (AndroidJavaException ex)
                {
                    TransformKnownExceptionToDescriptiveException(ex);
                    throw;
                }
            }

            /// <summary>
            /// Clear the credentials in block store. On failure, throws.
            /// </summary>
            public static async Task ClearGuestCredentialsAsync()
            {
                try
                {
                    await MetaplayGmsBlockStore.DeleteBlockAsync(label: GmsBlockStoreLabel);
                }
                catch (AndroidJavaException ex)
                {
                    TransformKnownExceptionToDescriptiveException(ex);
                    throw;
                }
            }

            /// <summary>
            /// In the case of a known/expected exception, throw a more descriptive error instead.
            /// </summary>
            static void TransformKnownExceptionToDescriptiveException(AndroidJavaException javaEx)
            {
                // Missing libs are caused by missing GMS
                string errorString = javaEx.ToString();
                if (errorString.Contains("java.lang.NoClassDefFoundError"))
                    throw new NoGmsBlockStoreAvailableOnDeviceError();
            }
        }
    }
}

#endif
