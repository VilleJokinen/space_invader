// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.EventLog;
using Metaplay.Core.Player;
using Metaplay.Server.EventLog;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System;
using static System.FormattableString;
#if !METAPLAY_DISABLE_GUILDS
using Metaplay.Core.Guild;
#endif

namespace Metaplay.Server.AdminApi.Controllers
{
    public class PlayerEventLogController : EntityEventLogControllerBase
    {
        public PlayerEventLogController(ILogger<PlayerEventLogController> logger, IActorRef adminApi) : base(logger, adminApi) { }

        /// <summary>
        /// API endpoint to scan entries from a player's event log.
        /// See <see cref="RequestEventLogAsync"/> for more info.
        ///
        /// Usage:  GET /api/players/{PLAYERID}/eventLog
        /// Test:   curl http://localhost:5550/api/players/{PLAYERID}/eventLog?startCursor=STARTCURSOR&numEntries=NUMENTRIES
        /// </summary>
        [HttpGet("players/{ownerIdStr}/eventLog")]
        [RequirePermission(MetaplayPermissions.ApiPlayersView)]
        public async Task<ActionResult<EntityEventLogResult<PlayerEventLogEntry>>> GetFromPlayer(string ownerIdStr, [FromQuery] string startCursor, [FromQuery] int numEntries)
        {
            PlayerDetails details = await GetPlayerDetailsAsync(ownerIdStr);
            return await RequestEventLogAsync<PlayerEventLogEntry>(details.PlayerId, startCursor, numEntries);
        }
    }

    #if !METAPLAY_DISABLE_GUILDS
    [GuildsEnabledCondition]
    public class GuildEventLogController : EntityEventLogControllerBase
    {
        public GuildEventLogController(ILogger<GuildEventLogController> logger, IActorRef adminApi) : base(logger, adminApi) { }

        /// <summary>
        /// API endpoint to scan entries from a guild's event log.
        /// See <see cref="RequestEventLogAsync"/> for more info.
        ///
        /// Usage:  GET /api/guilds/{GUILDID}/eventLog
        /// Test:   curl http://localhost:5550/api/guilds/{GUILDID}/eventLog?startCursor=STARTCURSOR&numEntries=NUMENTRIES
        /// </summary>
        [HttpGet("guilds/{ownerIdStr}/eventLog")]
        [RequirePermission(MetaplayPermissions.ApiGuildsView)]
        public async Task<ActionResult<EntityEventLogResult<GuildEventLogEntry>>> GetFromGuild(string ownerIdStr, [FromQuery] string startCursor, [FromQuery] int numEntries)
        {
            IGuildModelBase guild = (IGuildModelBase)await GetEntityStateAsync(ParseEntityIdStr(ownerIdStr, EntityKindCore.Guild));
            return await RequestEventLogAsync<GuildEventLogEntry>(guild.GuildId, startCursor, numEntries);
        }
    }
    #endif

    /// <summary>
    /// Controller for stock Metaplay SDK routes to work with an entity's event log.
    /// </summary>
    public abstract class EntityEventLogControllerBase : GameAdminApiController
    {
        public class EntityEventLogResult<TEntry> where TEntry : MetaEventLogEntry
        {
            public List<TEntry> Entries            { get; set; }
            public string       ContinuationCursor { get; set; }
        }

        /// <inheritdoc />
        protected EntityEventLogControllerBase(ILogger logger, IActorRef adminApi) : base(logger, adminApi) { }


        /// <summary>
        /// Scan entries from an entity's event log.
        ///
        /// The scan is cursor-based, such that each scan returns (besides the
        /// scanned items) a continuation cursor which can be used as the start
        /// cursor of a subsequent scan.
        ///
        /// <paramref name="startCursorStr"/> is a string representation of a
        /// <see cref="EntityEventLogCursor"/>: segment id and entry index
        /// separated by an underscore, as encoded by <see cref="EventLogCursorToString"/>.
        ///
        /// If the start cursor points to a log entry earlier than the earliest
        /// still available entry, the scan starts at the earliest available
        /// entry instead. In particular, a <paramref name="startCursorStr"/>
        /// string "0_0" (without the quotes) is guaranteed to start at the
        /// earliest available entry.
        ///
        /// The query may return fewer entries than requested if the end of
        /// the log is reached.
        ///
        /// \todo [nuutti] This currently only supports scanning forwards
        ///       (i.e. towards newer entries starting from the cursor).
        ///       Implement backwards scanning if needed.
        /// </summary>
        protected async Task<ActionResult<EntityEventLogResult<TEntry>>> RequestEventLogAsync<TEntry>(EntityId ownerId, string startCursorStr, int numEntries) where TEntry : MetaEventLogEntry
        {
            EntityEventLogCursor               startCursor = ParseEventLogCursor(startCursorStr);
            EntityEventLogScanResponse<TEntry> logResponse = await AskEntityAsync<EntityEventLogScanResponse<TEntry>>(ownerId, new EntityEventLogScanRequest(startCursor, numEntries));

            // Need config resolver to deserialize the event log.
            ActiveGameConfig        activeGameConfig = GlobalStateProxyActor.ActiveGameConfig.Get();
            ISharedGameConfig       sharedGameConfig = activeGameConfig.BaselineGameConfig.SharedConfig;
            IGameConfigDataResolver resolver         = sharedGameConfig;

            return new EntityEventLogResult<TEntry>
            {
                Entries            = logResponse.Entries.Deserialize(resolver, logicVersion: null),
                ContinuationCursor = EventLogCursorToString(logResponse.ContinuationCursor),
            };
        }

        protected static string EventLogCursorToString(EntityEventLogCursor cursor)
        {
            return Invariant($"{cursor.SegmentId}_{cursor.EntryIndexWithinSegment}");
        }

        protected static EntityEventLogCursor ParseEventLogCursor(string cursorStr)
        {
            string[] parts = cursorStr.Split("_");
            if (parts.Length != 2)
                throw new FormatException("Expected two underscore-separated parts");

            return new EntityEventLogCursor(
                segmentId: int.Parse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture),
                entryIndexWithinSegment: int.Parse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture));
        }
    }
}
