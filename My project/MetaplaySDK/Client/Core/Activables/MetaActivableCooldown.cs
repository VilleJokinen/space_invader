// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

// \todo [nuutti] Make activables not specific to Player?

using Metaplay.Core.Config;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Schedule;
using System;

namespace Metaplay.Core.Activables
{
    /// <summary>
    /// Defines an activable's cooldown, i.e. how long an activable stays unavailable after being deactivated.
    /// </summary>
    [MetaSerializable]
    public abstract class MetaActivableCooldownSpec
    {
        public abstract MetaTime GetCooldownEndTime(PlayerLocalTime activeStartTime, MetaTime activeEndTime, MetaScheduleBase scheduleMaybe);

        /// <summary>
        /// Specifies a cooldown with a fixed duration.
        /// </summary>
        [MetaSerializableDerived(1)]
        public class Fixed : MetaActivableCooldownSpec
        {
            [MetaMember(1)] public MetaDuration Duration { get; private set; }

            public Fixed(){ }
            public Fixed(MetaDuration duration)
            {
                if (duration < MetaDuration.Zero)
                    throw new ArgumentOutOfRangeException(nameof(duration), duration, "Duration cannot be negative");

                Duration = duration;
            }

            public override MetaTime GetCooldownEndTime(PlayerLocalTime activeStartTime, MetaTime activeEndTime, MetaScheduleBase scheduleMaybe)
                => activeEndTime + Duration;

            public static readonly Fixed Zero = new Fixed(MetaDuration.Zero);
        }

        /// <summary>
        /// Specifies a cooldown which lasts until the end of the current occasion of the schedule.
        /// I.e., only one activation is available per schedule occasion.
        /// </summary>
        [MetaSerializableDerived(2)]
        public class ScheduleBased : MetaActivableCooldownSpec
        {
            public override MetaTime GetCooldownEndTime(PlayerLocalTime activeStartTime, MetaTime activeEndTime, MetaScheduleBase scheduleMaybe)
            {
                MetaTime? scheduleEnd = scheduleMaybe?.TryGetCurrentOrNextEnabledOccasion(activeStartTime)?.EnabledRange.End;
                if (scheduleEnd.HasValue)
                    return MetaTime.Max(activeEndTime, scheduleEnd.Value);
                else
                    return activeEndTime;
            }

            public static readonly ScheduleBased Instance = new ScheduleBased();
        }

        public static MetaActivableCooldownSpec Parse(ConfigLexer lexer)
        {
            if (lexer.CurrentToken.Type == ConfigLexer.TokenType.Identifier)
            {
                string type = lexer.ParseIdentifier();
                switch (type)
                {
                    case "Fixed":           return new Fixed(ConfigParser.Parse<MetaDuration>(lexer));
                    case "ScheduleBased":   return ScheduleBased.Instance;
                    default:
                        throw new ParseError($"Invalid {nameof(MetaActivableCooldownSpec)}: {type}");
                }
            }
            else
                return new Fixed(ConfigParser.Parse<MetaDuration>(lexer));
        }
    }
}
