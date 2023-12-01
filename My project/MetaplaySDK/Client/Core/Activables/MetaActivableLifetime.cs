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
    /// Defines an activable's lifetime, i.e. how long a single activation lasts.
    /// </summary>
    [MetaSerializable]
    public abstract class MetaActivableLifetimeSpec
    {
        public abstract MetaTime? GetExpiresAtTime(PlayerLocalTime startTime, MetaScheduleBase scheduleMaybe);
        public abstract MetaTime? GetVisibilityEndsAtTime(PlayerLocalTime startTime, MetaScheduleBase scheduleMaybe);
        public abstract MetaTime? GetEndingSoonStartsAtTime(PlayerLocalTime startTime, MetaScheduleBase scheduleMaybe);

        /// <summary>
        /// Specifies that the lifetime has a fixed duration.
        /// </summary>
        [MetaSerializableDerived(1)]
        public class Fixed : MetaActivableLifetimeSpec
        {
            [MetaMember(1)] public MetaDuration Duration { get; private set; }

            public Fixed(){ }
            public Fixed(MetaDuration duration)
            {
                if (duration < MetaDuration.Zero)
                    throw new ArgumentOutOfRangeException(nameof(duration), duration, "Duration cannot be negative");

                Duration = duration;
            }

            public override MetaTime? GetExpiresAtTime(PlayerLocalTime startTime, MetaScheduleBase scheduleMaybe)
                => startTime.Time + Duration;

            public override MetaTime? GetVisibilityEndsAtTime(PlayerLocalTime startTime, MetaScheduleBase scheduleMaybe)
                => GetExpiresAtTime(startTime, scheduleMaybe);

            public override MetaTime? GetEndingSoonStartsAtTime(PlayerLocalTime startTime, MetaScheduleBase scheduleMaybe)
                => GetExpiresAtTime(startTime, scheduleMaybe);
        }

        /// <summary>
        /// Specifies that the lifetime depends on the activable's schedule.
        /// </summary>
        [MetaSerializableDerived(2)]
        public class ScheduleBased : MetaActivableLifetimeSpec
        {
            public override MetaTime? GetExpiresAtTime(PlayerLocalTime startTime, MetaScheduleBase scheduleMaybe)
                => scheduleMaybe?.TryGetCurrentOrNextEnabledOccasion(startTime)?.EnabledRange.End;

            public override MetaTime? GetVisibilityEndsAtTime(PlayerLocalTime startTime, MetaScheduleBase scheduleMaybe)
                => scheduleMaybe?.TryGetCurrentOrNextEnabledOccasion(startTime)?.VisibleRange.End;

            public override MetaTime? GetEndingSoonStartsAtTime(PlayerLocalTime startTime, MetaScheduleBase scheduleMaybe)
                => scheduleMaybe?.TryGetCurrentOrNextEnabledOccasion(startTime)?.EndingSoonStartsAt;

            public static readonly ScheduleBased Instance = new ScheduleBased();
        }

        /// <summary>
        /// Specifies that each activation lasts forever, i.e. it has no time-based expiration.
        /// Note that activations can still end due to consumption.
        /// </summary>
        [MetaSerializableDerived(3)]
        public class Forever : MetaActivableLifetimeSpec
        {
            // \note Never expires
            public override MetaTime? GetExpiresAtTime(PlayerLocalTime startTime, MetaScheduleBase scheduleMaybe)
                => null;

            public override MetaTime? GetVisibilityEndsAtTime(PlayerLocalTime startTime, MetaScheduleBase scheduleMaybe)
                => null;

            public override MetaTime? GetEndingSoonStartsAtTime(PlayerLocalTime startTime, MetaScheduleBase scheduleMaybe)
                => null;

            public static readonly Forever Instance = new Forever();
        }

        public static MetaActivableLifetimeSpec Parse(ConfigLexer lexer)
        {
            if (lexer.CurrentToken.Type == ConfigLexer.TokenType.Identifier)
            {
                string type = lexer.ParseIdentifier();
                switch (type)
                {
                    case "Fixed":           return new Fixed(ConfigParser.Parse<MetaDuration>(lexer));
                    case "ScheduleBased":   return ScheduleBased.Instance;
                    case "Forever":         return Forever.Instance;
                    default:
                        throw new ParseError($"Invalid {nameof(MetaActivableLifetimeSpec)}: {type}");
                }
            }
            else
                return new Fixed(ConfigParser.Parse<MetaDuration>(lexer));
        }
    }
}
