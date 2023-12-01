// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using System;

namespace Metaplay.Core.Player
{
    /// <summary>
    /// Info about a player's time zone.
    /// </summary>
    /// <remarks>
    /// Doesn't contain comprehensive information about a time zone, such as daylight saving time adjustments.
    /// Should be good enough for short-term usage, e.g. per session.
    /// Shouldn't be assumed to be fresh, in case player's actual time zone or offset changes.
    /// </remarks>
    [MetaSerializable]
    public class PlayerTimeZoneInfo
    {
        [MetaMember(1)] public MetaDuration CurrentUtcOffset { get; set; }

        public PlayerTimeZoneInfo() {}
        public PlayerTimeZoneInfo(MetaDuration currentUtcOffset)
        {
            CurrentUtcOffset = currentUtcOffset;
        }

        public PlayerTimeZoneInfo GetCorrected()
        {
            // \note The offset is clamped to [-18h, 18h]. This is the same as Noda Time's range for Offset.
            //       In reality, the range probably something smaller, like [-12h, 14h], and the parts outside
            //       that are only historical, but this shouldn't really cause trouble.
            MetaDuration correctedCurrentUtcOffset = Util.Clamp(CurrentUtcOffset, MetaDuration.FromHours(-18), MetaDuration.FromHours(18));
            return new PlayerTimeZoneInfo(correctedCurrentUtcOffset);
        }

        public static PlayerTimeZoneInfo CreateForCurrentDevice()
        {
            TimeSpan utcOffsetTimeSpan = TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow);
            MetaDuration utcOffset = MetaDuration.FromMilliseconds((long)utcOffsetTimeSpan.TotalMilliseconds);
            return new PlayerTimeZoneInfo(utcOffset);
        }
    }
}
