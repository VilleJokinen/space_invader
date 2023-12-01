// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Analytics;
using Metaplay.Core.EventLog;
using Metaplay.Core.Model;

namespace Metaplay.Core.Player
{
    /// <summary>
    /// Entry type specific for a player's event log.
    /// </summary>
    [MetaSerializable]
    public class PlayerEventLogEntry : EntityEventLogEntry<PlayerEventBase, PlayerEventDeserializationFailureSubstitute>
    {
        PlayerEventLogEntry() : base() { }
        public PlayerEventLogEntry(BaseParams baseParams, MetaTime modelTime, int payloadSchemaVersion, PlayerEventBase payload)
            : base(baseParams, modelTime, payloadSchemaVersion, payload)
        {
        }
    }

    /// <summary>
    /// Base class for player-specific analytics events, both Metaplay core and
    /// game-specific event types.
    /// </summary>
    [AnalyticsEventCategory("Player")]
    public abstract class PlayerEventBase : EntityEventBase
    {
    }

    [MetaSerializable]
    public class PlayerEventLog : EntityEventLog<PlayerEventBase, PlayerEventDeserializationFailureSubstitute, PlayerEventLogEntry>
    {
    }
}
