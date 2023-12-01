// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Core;
using Metaplay.Core.Json;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Serialization;
using Metaplay.Server.AdminApi.AuditLog;
using Metaplay.Server.Database;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

#if !METAPLAY_DISABLE_GUILDS
using Metaplay.Server.Guild.InternalMessages;
#endif

namespace Metaplay.Server.AdminApi.Controllers
{
    /// <summary>
    /// Controller for stock Metaplay SDK route to export player data.
    /// </summary>
    public class PlayerGdprExportController : GameAdminApiController
    {
        public PlayerGdprExportController(ILogger<PlayerGdprExportController> logger, IActorRef adminApi) : base(logger, adminApi)
        {
        }


        /// <summary>
        /// Audit log events
        /// </summary>
        [MetaSerializableDerived(MetaplayAuditLogEventCodes.PlayerGdprExported)]
        public class PlayerEventGdprExported : PlayerEventPayloadBase
        {
            public PlayerEventGdprExported() { }
            override public string EventTitle => "GDPR data exported";
            override public string EventDescription => "GDPR data of player exported.";
        }

        /// <summary>
        /// HTTP response for exporting an individual player's data for them
        /// </summary>
        public class PlayerGdprExportItem
        {
            public object ExportDetails { get; set; }
            public object Model { get; set; }
            public object EventLogSegments { get; set; }
            public object AuditLogEvents { get; set; }
            public object GuildMembership { get; set; }
        }

        public class PlayerGdprExportEventLogSegment
        {
            public int                          SegmentSequentialId { get; set; }
            public List<PlayerEventLogEntry>    Entries             { get; set; }
        }

        /// <summary>
        /// API endpoint to return a player-facing export of their personal information
        /// Usage:  GET /api/players/{PLAYERID}/gdprExport
        /// Test:   curl http://localhost:5550/api/players/{PLAYERID}/gdprExport
        /// </summary>
        //
        [HttpGet("players/{playerIdStr}/gdprExport")]
        [RequirePermission(MetaplayPermissions.ApiPlayersGdprExport)]
        public async Task<ActionResult<PlayerGdprExportItem>> GetGdprExport(string playerIdStr)
        {
            PlayerDetails details = await GetPlayerDetailsAsync(playerIdStr);

            // Read event log segments
            MetaDatabase db = MetaDatabase.Get(QueryPriority.Low);
            IEnumerable<PlayerGdprExportEventLogSegment> eventLogSegments =
                (await db.GetAllEventLogSegmentsOfEntityAsync<PersistedPlayerEventLogSegment>(details.PlayerId))
                .Select((PersistedPlayerEventLogSegment persistedSegment) =>
                {
                    PlayerEventLogSegmentPayload payload = MetaSerialization.DeserializeTagged<PlayerEventLogSegmentPayload>(persistedSegment.UncompressPayload(), MetaSerializationFlags.Persisted, details.GameConfigResolver, details.LogicVersion);
                    return new PlayerGdprExportEventLogSegment
                    {
                        SegmentSequentialId = persistedSegment.SegmentSequentialId,
                        Entries             = payload.Entries,
                    };
                });

            // Audit log events
            (string playerEntityKind, string playerEntityValue) = details.PlayerId.GetKindValueStrings();
            int maxEvents = 1000;   // This is just an arbitray value..
            List<AuditLogEvent> auditLogEvents = (await db.QueryAuditLogEventsAsync(eventIdLessThan: null, targetType: playerEntityKind, targetId: playerEntityValue, source: null, sourceIpAddress: null, sourceCountryIsoCode: null, pageSize: maxEvents)).ConvertAll(x => new AuditLogEvent(x));

            // Guild data
            string guildMembershipJsonString = "null";
            #if !METAPLAY_DISABLE_GUILDS
            if (details.Model.GuildState.GuildId != EntityId.None)
            {
                InternalGuildMemberGdprExportResponse guildResponse = await AskEntityAsync<InternalGuildMemberGdprExportResponse>(details.Model.GuildState.GuildId, new InternalGuildMemberGdprExportRequest(details.PlayerId));
                if (guildResponse.IsSuccess)
                    guildMembershipJsonString = guildResponse.ExportJsonString;
            }
            #endif

            // Audit log event
            await WriteAuditLogEventAsync(new PlayerEventBuilder(details.PlayerId, new PlayerEventGdprExported()));

            // Respond to browser
            return Ok(new PlayerGdprExportItem
            {
                // Note that want to export this in JSON format but the default serializer does not support the
                // [ExcludeFromGdpr] attribute so we can't just return the source objects. To get around this, we
                // do the "serialize to a string then deserialize back to an object" trick. This ensure that
                // a) we export as a JSON object, and b) any [ExcludeFromGdpr] items get stripped from the output
                ExportDetails = JsonSerialization.Deserialize<object>(JsonSerialization.SerializeToString(new
                {
                    PlayerId = playerIdStr,
                    ExportedAt = MetaTime.Now,
                })),
                Model = JsonSerialization.Deserialize<object>(JsonSerialization.SerializeToString(details.Model, JsonSerialization.GdprSerializer)),
                EventLogSegments = JsonSerialization.Deserialize<object>(JsonSerialization.SerializeToString(eventLogSegments, JsonSerialization.GdprSerializer)),
                AuditLogEvents = JsonSerialization.Deserialize<object>(JsonSerialization.SerializeToString(auditLogEvents, JsonSerialization.GdprSerializer)),
                GuildMembership = JsonSerialization.Deserialize<object>(guildMembershipJsonString),
            });

        }
    }
}
