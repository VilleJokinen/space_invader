// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if !METAPLAY_DISABLE_GUILDS

using Akka.Actor;
using Metaplay.Core;
using Metaplay.Core.Guild;
using Metaplay.Core.Model;
using Metaplay.Server.AdminApi.AuditLog;
using Metaplay.Server.Guild.InternalMessages;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static Metaplay.Server.AdminApi.Controllers.Exceptions;

namespace Metaplay.Server.AdminApi.Controllers
{
    /// <summary>
    /// Controller for route to edit a guild member role
    /// </summary>
    [GuildsEnabledCondition]
    public class GuildEditMemberRoleController : GameAdminApiController
    {
        public GuildEditMemberRoleController(ILogger<GuildEditMemberRoleController> logger, IActorRef adminApi) : base(logger, adminApi)
        {
        }

        /// <summary>
        /// Audit log events
        /// </summary>
        [MetaSerializableDerived(MetaplayAuditLogEventCodes.GuildEventRolesChanged)]
        public class GuildEventRolesChanged : GuildEventPayloadBase
        {
            [MetaSerializable]
            public struct RoleChange
            {
                // \note: roles are encoded as strings to allow updating role enum layout
                [MetaMember(1)] public string   FromRole    { get; private set; }
                [MetaMember(2)] public string   ToRole      { get; private set; }

                public RoleChange(string fromRole, string toRole)
                {
                    FromRole = fromRole;
                    ToRole = toRole;
                }
            }

            [MetaMember(1)] public EntityId                         PlayerId        { get; private set; } // intended change
            [MetaMember(2)] public RoleChange                       IntendedChange  { get; private set; } // intended change
            [MetaMember(3)] public Dictionary<EntityId, RoleChange> AllChanges      { get; private set; } // true change

            public GuildEventRolesChanged() { }
            public GuildEventRolesChanged(EntityId playerId, RoleChange intendedChange, Dictionary<EntityId, RoleChange> allChanges)
            {
                PlayerId = playerId;
                IntendedChange = intendedChange;
                AllChanges = allChanges;
            }

            override public string EventTitle => "Roles changed";
            override public string EventDescription => $"{PlayerId} role was changed from {IntendedChange.FromRole} to {IntendedChange.ToRole}.";
        }
        [MetaSerializableDerived(MetaplayAuditLogEventCodes.PlayerGuildRoleEdited)]
        public class PlayerGuildRoleEdited : PlayerEventPayloadBase
        {
            [MetaMember(1)] public EntityId GuildId     { get; private set; }
            [MetaMember(2)] public string   FromRole    { get; private set; }
            [MetaMember(3)] public string   ToRole      { get; private set; }

            public PlayerGuildRoleEdited() { }
            public PlayerGuildRoleEdited(EntityId guildId, string fromRole, string toRole)
            {
                GuildId = guildId;
                FromRole = fromRole;
                ToRole = toRole;
            }

            override public string EventTitle => "Guild role edited";
            override public string EventDescription => $"Player role in {GuildId} was changed from {FromRole} to {ToRole}.";
        }

        /// <summary>
        /// HTTP request body for member role edit preview
        /// </summary>
        [JsonObject(ItemRequired = Required.Always)]
        public class MemberValidateRoleEdit
        {
            public string PlayerId;
            public string Role;
        }

        /// <summary>
        /// API endpoint to get changes from member role edit
        /// Usage:  Get /api/guilds/{GUILDID}/editRole
        /// Test:   curl http://localhost:5550/api/guilds/Guild:0000000000/validateEditRole -X POST -H "Content-Type: application/json" -d '{"PlayerId":"Player:0000000000", "Role":"Leader"}'
        /// </summary>
        [HttpPost("guilds/{guildIdStr}/validateEditRole")]
        [Consumes("application/json")]
        [RequirePermission(MetaplayPermissions.ApiGuildsEditRoles)]
        public async Task<ActionResult<Dictionary<string, string>>> ValidateEditRole(string guildIdStr)
        {
            // Parse parameters
            EntityId                guildId         = ParseEntityIdStr(guildIdStr, EntityKindCore.Guild);
            MemberValidateRoleEdit  request         = await ParseBodyAsync<MemberValidateRoleEdit>();
            EntityId                subjectMemberId = await ParsePlayerIdStrAndCheckForExistenceAsync(request.PlayerId);
            GuildMemberRole         subjectRole     = Enum.Parse<GuildMemberRole>(request.Role);
            IGuildModelBase         guildModel      = (IGuildModelBase)await GetEntityStateAsync(guildId);

            if (!guildModel.TryGetMember(subjectMemberId, out GuildMemberBase _))
                throw new MetaplayHttpException(400, "Player not in guild.", $"Player with ID {subjectMemberId} does not exist in guild with ID {guildId}.");

            OrderedDictionary<EntityId, GuildMemberRole>    changesNow      = guildModel.ComputeRoleChangesForRoleEvent(GuildMemberRoleEvent.MemberEdit, subjectMemberId, subjectRole);
            Dictionary<string, string>                      changesAsString = new Dictionary<string, string>(changesNow.Select(kv => new KeyValuePair<string, string>(kv.Key.ToString(), kv.Value.ToString())));
            return new ActionResult<Dictionary<string, string>>(changesAsString);
        }

        /// <summary>
        /// HTTP request body for member role edit
        /// </summary>
        [JsonObject(ItemRequired = Required.Always)]
        public class MemberEditRole
        {
            public string PlayerId;
            public string Role;
            public Dictionary<string, string> ExpectedChanges;
        }

        /// <summary>
        /// API endpoint to edit a member role
        /// Usage:  POST /api/guilds/{GUILDID}/editRole
        /// Test:   curl http://localhost:5550/api/guilds/Guild:0000000000/editRole -X POST -H "Content-Type: application/json" -d '{"PlayerId":"Player:0000000000", "Role":"Leader", "ExpectedChanges": {"Player:0000000000":"Leader", "Player:0000000001":"MiddleTier"}}'
        /// </summary>
        [HttpPost("guilds/{guildIdStr}/editRole")]
        [Consumes("application/json")]
        [RequirePermission(MetaplayPermissions.ApiGuildsEditRoles)]
        public async Task<IActionResult> EditRole(string guildIdStr)
        {
            // Parse parameters
            EntityId        guildId         = ParseEntityIdStr(guildIdStr, EntityKindCore.Guild);
            MemberEditRole  request         = await ParseBodyAsync<MemberEditRole>();
            EntityId        subjectMemberId = await ParsePlayerIdStrAndCheckForExistenceAsync(request.PlayerId);
            GuildMemberRole subjectRole     = Enum.Parse<GuildMemberRole>(request.Role);
            IGuildModelBase guildModel      = (IGuildModelBase)await GetEntityStateAsync(guildId);

            if (!guildModel.TryGetMember(subjectMemberId, out GuildMemberBase guildMember))
                throw new MetaplayHttpException(400, "Player not in guild.", $"Player with ID {subjectMemberId} does not exist in guild with ID {guildId}.");

            // check UI is not stale
            OrderedDictionary<EntityId, GuildMemberRole> changesNow = guildModel.ComputeRoleChangesForRoleEvent(GuildMemberRoleEvent.MemberEdit, subjectMemberId, subjectRole);
            bool isStaleRequest;

            if (changesNow.Count != request.ExpectedChanges.Count)
            {
                isStaleRequest = true;
            }
            else
            {
                isStaleRequest = false;
                foreach((EntityId entityId, GuildMemberRole role) in changesNow)
                {
                    if (!request.ExpectedChanges.TryGetValue(entityId.ToString(), out string expectedRoleString))
                    {
                        isStaleRequest = true;
                        break;
                    }
                    if (role.ToString() != expectedRoleString)
                    {
                        isStaleRequest = true;
                        break;
                    }
                }
            }
            if (isStaleRequest)
                throw new MetaplayHttpException(400, "Stale request", $"Attempt to modify roles in {guildId} were rejected because roles were modified after request began.");
            if (changesNow.Count == 0)
                throw new MetaplayHttpException(400, "Bad request", $"Attempt to modify roles in {guildId} were rejected because there were no changes.");

            _logger.LogInformation($"Changing guild {guildId} member {request.PlayerId} role to {request.Role}");

            // Tell guild to do the operation
            InternalGuildAdminEditRolesResponse response = await AskEntityAsync<InternalGuildAdminEditRolesResponse>(guildId, new InternalGuildAdminEditRolesRequest(subjectMemberId, subjectRole, changesNow));
            if (!response.Success)
                throw new MetaplayHttpException(400, "Stale request", $"Attempt to modify roles {guildId} were rejected because roles were modified after request began.");

            // Audit log events
            List<EventBuilder> events = new List<EventBuilder>();

            {
                Dictionary<EntityId, GuildEventRolesChanged.RoleChange> changesNowChanges = new Dictionary<EntityId, GuildEventRolesChanged.RoleChange>();
                foreach((EntityId entityId, GuildMemberRole toRole) in changesNow)
                {
                    if (guildModel.TryGetMember(entityId, out GuildMemberBase changedMember))
                        changesNowChanges.Add(entityId, new GuildEventRolesChanged.RoleChange(changedMember.Role.ToString(), toRole.ToString()));
                }
                events.Add(new GuildEventBuilder(guildId, new GuildEventRolesChanged(subjectMemberId, new GuildEventRolesChanged.RoleChange(guildMember.Role.ToString(), subjectRole.ToString()), changesNowChanges)));
            }

            foreach((EntityId playerId, GuildMemberRole toRole) in changesNow)
            {
                if (guildModel.TryGetMember(playerId, out GuildMemberBase changedMember))
                    events.Add(new PlayerEventBuilder(playerId, new PlayerGuildRoleEdited(guildId, changedMember.Role.ToString(), toRole.ToString())));
            }

            await WriteRelatedAuditLogEventsAsync(events);

            return NoContent();
        }
    }
}

#endif
