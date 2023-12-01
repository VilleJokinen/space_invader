// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;

namespace Metaplay.Core.League.Player
{
    /// <summary>
    /// The avatar, i.e. the visible representation, of a Player in a Player Division. Default implementation
    /// <see cref="Default"/> contains only the name of the player. More data may be
    /// added into the avatar by inheriting this class with a custom implementation and filling it in PlayerActor.
    /// </summary>
    [PlayerLeaguesEnabledCondition]
    [MetaSerializable]
    [MetaImplicitMembersDefaultRangeForMostDerivedClass(1, 100)]
    public abstract class PlayerDivisionAvatarBase
    {
        [PlayerLeaguesEnabledCondition]
        [MetaSerializableDerived(101)]
        public sealed class Default : PlayerDivisionAvatarBase
        {
            [MetaMember(1)] public string DisplayName;

            Default() { }
            public Default(string displayName)
            {
                DisplayName = displayName;
            }
        }

        protected PlayerDivisionAvatarBase() { }
    }
}
