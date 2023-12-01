// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Core.Serialization;

namespace Metaplay.Unity
{
    /// <summary>
    /// Credentials required to log in to the game server to a normal guest account. Guest accounts
    /// are generated automatically on the first connection and then used on the following connections.
    /// </summary>
    public class GuestCredentials
    {
        /// <summary>
        /// Unique id of the device.
        /// </summary>
        public string DeviceId;

        /// <summary>
        /// Secret authentication token (ie, password).
        /// </summary>
        public string AuthToken;

        /// <summary>
        /// Id of the player these credentials are for.
        /// </summary>
        public EntityId PlayerId;

        public static bool operator ==(GuestCredentials lhs, GuestCredentials rhs)
        {
            if (ReferenceEquals(lhs, rhs))
                return true;
            else if (lhs is null || rhs is null)
                return false;
            else
                return lhs.DeviceId == rhs.DeviceId
                    && lhs.AuthToken == rhs.AuthToken
                    && lhs.PlayerId == rhs.PlayerId;
        }

        public static bool operator !=(GuestCredentials lhs, GuestCredentials rhs)
        {
            return !(lhs == rhs);
        }

        public override bool Equals(object obj)
        {
            return ((obj is GuestCredentials other) && this == other);
        }

        public override int GetHashCode()
        {
            return DeviceId?.GetHashCode() ?? 0;
        }
    }

    /// <summary>
    /// Utility for serializing <see cref="GuestCredentials"/>.
    /// </summary>
    public static class GuestCredentialsSerializer
    {
        [MetaSerializable]
        public class CredentialsData
        {
            [MetaMember(1)] public string   DeviceId;
            [MetaMember(2)] public string   AuthToken;
            [MetaMember(3)] public EntityId PlayerId;
        }

        public static byte[] Serialize(GuestCredentials credentials)
        {
            CredentialsData data = new CredentialsData()
            {
                DeviceId = credentials.DeviceId,
                AuthToken = credentials.AuthToken,
                PlayerId = credentials.PlayerId,
            };
            return MetaSerialization.SerializeTagged(data, MetaSerializationFlags.IncludeAll, logicVersion: null);
        }

        public static GuestCredentials TryDeserialize(byte[] blob)
        {
            try
            {
                CredentialsData data = MetaSerialization.DeserializeTagged<CredentialsData>(blob, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);

                if (string.IsNullOrEmpty(data.DeviceId) || string.IsNullOrEmpty(data.AuthToken) || !data.PlayerId.IsValid)
                    return null;

                GuestCredentials creds = new GuestCredentials
                {
                    DeviceId    = data.DeviceId,
                    AuthToken   = data.AuthToken,
                    PlayerId    = data.PlayerId,
                };
                return creds;
            }
            catch
            {
                return null;
            }
        }
    }
}
