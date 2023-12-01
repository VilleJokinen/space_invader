// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

namespace Metaplay.Cloud.Utility
{
    /// <summary>
    /// Utilities and definitions to help with device authentication.
    /// </summary>
    public static class DeviceAuthentication
    {
        public const int DeviceIdLength     = 48;   // Length of a DeviceId token string
        public const int AuthTokenLength    = 48;   // Length of an authentication token string

        public static bool IsValidDeviceId(string deviceId) => SecureTokenUtil.IsValidToken(deviceId, DeviceIdLength);
        public static bool IsValidAuthToken(string authToken) => SecureTokenUtil.IsValidToken(authToken, AuthTokenLength);
    }
}
