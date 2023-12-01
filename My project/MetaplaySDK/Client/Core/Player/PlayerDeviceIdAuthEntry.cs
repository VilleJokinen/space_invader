// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;

namespace Metaplay.Core.Player
{
    /// <summary>
    /// A physical device attached to a <see cref="PlayerModel"/>.
    /// </summary>
    [MetaSerializableDerived(101)]
    [MetaReservedMembers(200, 300)]
    public class PlayerDeviceIdAuthEntry : PlayerAuthEntryBase
    {
        /// <summary>
        /// Human-readable device model (SystemInfo.deviceModel in Unity, eg, iPhone6,1)
        /// </summary>
        [MetaMember(201)] public string DeviceModel { get; private set; }

        PlayerDeviceIdAuthEntry() { }
        public PlayerDeviceIdAuthEntry(MetaTime attachedAt, string deviceModel) : base(attachedAt)
        {
            DeviceModel = deviceModel;
        }
    }
}
