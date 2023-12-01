// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if !METAPLAY_DISABLE_GUILDS

using Metaplay.Core.Analytics;
using Metaplay.Core.EventLog;
using Metaplay.Core.Model;

namespace Metaplay.Core.Guild
{
    /// <summary>
    /// Entry type specific for a guild's event log.
    /// </summary>
    [MetaSerializable]
    public class GuildEventLogEntry : EntityEventLogEntry<GuildEventBase, GuildEventDeserializationFailureSubstitute>
    {
        // \todo [nuutti] Additional guild-specific fields? For example guild member list?

        GuildEventLogEntry() : base() { }
        public GuildEventLogEntry(BaseParams baseParams, MetaTime modelTime, int payloadSchemaVersion, GuildEventBase payload)
            : base(baseParams, modelTime, payloadSchemaVersion, payload)
        {
        }
    }

    /// <summary>
    /// Base class for guild-specific analytics events, both Metaplay core and
    /// game-specific event types.
    /// </summary>
    [AnalyticsEventCategory("Guild")]
    public abstract class GuildEventBase : EntityEventBase
    {
    }

    [MetaSerializable]
    public class GuildEventLog : EntityEventLog<GuildEventBase, GuildEventDeserializationFailureSubstitute, GuildEventLogEntry>
    {
    }
}

#endif
