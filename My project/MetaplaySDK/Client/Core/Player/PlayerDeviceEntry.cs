// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;

namespace Metaplay.Core.Player
{
    /// <summary>
    /// Per-device usage data for player
    /// </summary>
    [MetaSerializable]
    [MetaBlockedMembers(1)]
    public class PlayerDeviceEntry
    {
        [MetaMember(2)] public string   DeviceModel                         { get; private set; }   // Human-readable device model (SystemInfo.deviceModel in Unity, eg, iPhone6,1)
        [MetaMember(3)] public MetaTime FirstSeenAt                         { get; private set; }   // Timestamp of when first seen
        [MetaMember(4)] public MetaTime LastLoginAt                         { get; private set; }   // Timestamp of latest login with this device
        [MetaMember(5)] public bool     IncompleteHistory                   { get; private set; } = true; // Device history entry has been created through migration, so history data not complete
        [MetaMember(6)] public int      NumLogins                           { get; private set; }   // How many times has the device been seen?
        [MetaMember(7)] public ClientPlatform ClientPlatform                { get; set; }           // Client platform of the device
        [MetaMember(8)] public OrderedSet<AuthenticationKey> LoginMethods   { get; private set; } = new OrderedSet<AuthenticationKey>(); // Login methods used with this device

        PlayerDeviceEntry() { }

        public void RecordNewLogin(AuthenticationKey loginMethodUsed, MetaTime timestamp)
        {
            LoginMethods.Add(loginMethodUsed);
            NumLogins++;
            if (timestamp > LastLoginAt)
                LastLoginAt = timestamp;
        }

        public static PlayerDeviceEntry Create(string deviceModel, ClientPlatform platform, MetaTime firstSeenAt)
        {
            PlayerDeviceEntry e = new PlayerDeviceEntry();
            e.DeviceModel = deviceModel;
            e.ClientPlatform = platform;
            e.FirstSeenAt = firstSeenAt;
            e.IncompleteHistory = false;
            return e;
        }
    }
}
