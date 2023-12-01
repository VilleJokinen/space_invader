// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;

namespace Metaplay.Core.Player
{
    /// <summary>
    /// Information related to an authentication method of a player. May be inherited
    /// to add per-platform information. Default implementation <see cref="Default"/>.
    /// DeviceID authentication uses <see cref="PlayerDeviceIdAuthEntry"/>.
    /// </summary>
    [MetaSerializable]
    [MetaReservedMembers(100, 200)]
    public abstract class PlayerAuthEntryBase
    {
        /// <summary>
        /// Timestamp when the authentication was attached into an account.
        /// </summary>
        [MetaMember(100)] public MetaTime AttachedAt { get; private set; }

        protected PlayerAuthEntryBase() { }
        protected PlayerAuthEntryBase(MetaTime attachedAt)
        {
            AttachedAt = attachedAt;
        }

        /// <summary>
        /// Updates AttachedAt. Useful for when AuthEntry is moved from player to player
        /// and we want to keep other data except AttachedAt which should be the timestamp
        /// when the entry was added to the current entity.
        /// </summary>
        public void RefreshAttachedAt(MetaTime attachedAt)
        {
            AttachedAt = attachedAt;
        }

        /// <summary>
        /// Default convenience implementation of <see cref="PlayerAuthEntryBase"/>.
        /// </summary>
        [MetaSerializableDerived(100)]
        public sealed class Default : PlayerAuthEntryBase
        {
            Default() { }
            public Default(MetaTime attachedAt)
            {
                AttachedAt = attachedAt;
            }
        }
    }
}
