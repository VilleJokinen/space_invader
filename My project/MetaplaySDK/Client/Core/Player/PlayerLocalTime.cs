// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

namespace Metaplay.Core.Player
{
    /// <summary>
    /// Convenience struct holding both global time and player's local utc offset.
    /// </summary>
    public struct PlayerLocalTime
    {
        public readonly MetaTime        Time;
        public readonly MetaDuration    UtcOffset;

        public PlayerLocalTime (MetaTime time, MetaDuration utcOffset)
        {
            Time        = time;
            UtcOffset   = utcOffset;
        }
    }
}
