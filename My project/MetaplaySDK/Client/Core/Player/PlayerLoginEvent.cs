// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;

namespace Metaplay.Core.Player
{
    /// <summary>
    /// Login event for a player. Can be used to retain the latest player logins for
    /// security purposes, eg, when trying to figure out whether an account has been
    /// legitimately lost vs. someone trying to steal it.
    /// </summary>
    [MetaSerializable]
    public class PlayerLoginEvent
    {
        [MetaMember(1)] public MetaTime          Timestamp         { get; private set; }   // Timestamp of login event
        [MetaMember(2)] public string            DeviceId          { get; private set; }   // Unique identifier of device
        [MetaMember(3)] public string            DeviceModel       { get; private set; }   // Human-readable device model (SystemInfo.deviceModel in Unity, eg, iPhone6,1)
        [MetaMember(4)] public string            ClientVersion     { get; private set; }   // Game client version (Application.version in Unity)
        [MetaMember(5)] public PlayerLocation?   Location          { get; private set; }   // Location of login
        [MetaMember(6)] public AuthenticationKey AuthenticationKey { get; private set; }   // Authentication key used for login

        public PlayerLoginEvent() { }
        public PlayerLoginEvent(MetaTime timestamp, string deviceId, string deviceModel, string clientVersion, PlayerLocation? location, AuthenticationKey authKey)
        {
            Timestamp         = timestamp;
            DeviceId          = deviceId;
            DeviceModel       = deviceModel;
            ClientVersion     = clientVersion;
            Location          = location;
            AuthenticationKey = authKey;
        }
    }
}
