// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if UNITY_IOS

using Metaplay.Core.Model;
using Metaplay.Core.Serialization;
using System.Threading.Tasks;

namespace Metaplay.Unity
{
    partial class CredentialsStore
    {
        public class IosKeychain
        {
            const string KeychainAccountName = "deviceauth";

            [MetaSerializable]
            public class DeviceAuthKeychainValue
            {
                [MetaMember(1)] public int Version;
                [MetaMember(2)] public byte[] Credentials;
            }

            /// <summary>
            /// Retrieves the credentials from the keychain. If there are no credentials stored, return null. On failure, throws.
            /// </summary>
            public static async Task<GuestCredentials> TryGetGuestCredentialsAsync()
            {
                byte[] blob = await MetaplayIosKeychain.TryGetGenericPasswordAsync(MetaplayIosKeychain.KeychainStorage.DeviceLocalStorage, MetaplayIosKeychain.GetDefaultGenericPasswordServiceName(), KeychainAccountName);
                if (blob == null)
                    return null;

                DeviceAuthKeychainValue value = MetaSerialization.DeserializeTagged<DeviceAuthKeychainValue>(blob, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);
                if (value.Version != 1)
                    return null;

                return GuestCredentialsSerializer.TryDeserialize(value.Credentials);
            }

            /// <summary>
            /// Stores the credentials into keychain. On failure, throws.
            /// </summary>
            public static async Task StoreGuestCredentialsAsync(GuestCredentials credentials)
            {
                DeviceAuthKeychainValue value = new DeviceAuthKeychainValue()
                {
                    Version = 1,
                    Credentials = GuestCredentialsSerializer.Serialize(credentials)
                };

                byte[] blob = MetaSerialization.SerializeTagged<DeviceAuthKeychainValue>(value, MetaSerializationFlags.IncludeAll, logicVersion: null);
                await MetaplayIosKeychain.SetGenericPasswordAsync(MetaplayIosKeychain.KeychainStorage.DeviceLocalStorage, MetaplayIosKeychain.GetDefaultGenericPasswordServiceName(), KeychainAccountName, blob);
            }

            /// <summary>
            /// Clears the credentials in keychain. On failure, throws.
            /// </summary>
            public static async Task ClearGuestCredentialsAsync()
            {
                await MetaplayIosKeychain.ClearGenericPasswordAsync(MetaplayIosKeychain.KeychainStorage.DeviceLocalStorage, MetaplayIosKeychain.GetDefaultGenericPasswordServiceName(), KeychainAccountName);
            }
        }
    }
}

#endif
