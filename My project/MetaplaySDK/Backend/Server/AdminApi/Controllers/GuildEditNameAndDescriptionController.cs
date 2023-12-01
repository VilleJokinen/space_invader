// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if !METAPLAY_DISABLE_GUILDS

using Akka.Actor;
using Metaplay.Core;
using Metaplay.Core.Guild;
using Metaplay.Core.Model;
using Metaplay.Server.Guild.InternalMessages;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace Metaplay.Server.AdminApi.Controllers
{
    /// <summary>
    /// Controller for stock Metaplay SDK routes to change an individual guild's name and description.
    /// </summary>
    [GuildsEnabledCondition]
    public class GuildEditNameAndDescriptionController : GameAdminApiController
    {
        public GuildEditNameAndDescriptionController(ILogger<GuildEditNameAndDescriptionController> logger, IActorRef adminApi) : base(logger, adminApi)
        {
        }

        /// <summary>
        /// Audit log events
        /// </summary>
        [MetaSerializableDerived(MetaplayAuditLogEventCodes.GuildNameAndDescriptionChanged)]
        public class GuildEventNameAndDescriptionChanged : GuildEventPayloadBase
        {
            [MetaMember(1)] public string NewDisplayName { get; private set; }
            [MetaMember(2)] public string NewDescription { get; private set; }
            public GuildEventNameAndDescriptionChanged() { }
            public GuildEventNameAndDescriptionChanged(string newDisplayName, string newDescription)
            {
                NewDisplayName = newDisplayName;
                NewDescription = newDescription;
            }
            override public string EventTitle => "Name and description changed";
            override public string EventDescription => $"Guild's name changed to {NewDisplayName}.";
        }

        /// <summary>
        /// HTTP request and response for guild details change and validation endpoints
        /// </summary>
        public class GuildChangeDetailsBody
        {
            public string NewDisplayName { get; private set; }
            public string NewDescription { get; private set; }
        }
        public class GuildChangeDetailsReturn
        {
            public bool DisplayNameWasValid { get; private set; }
            public bool DescriptionWasValid { get; private set; }
            public GuildChangeDetailsReturn(bool displayNameWasValid, bool descriptionWasValid)
            {
                DisplayNameWasValid = displayNameWasValid;
                DescriptionWasValid = descriptionWasValid;
            }
        }

        /// <summary>
        /// API endpoint to validate a change to a guild's details
        /// Usage:  POST /api/guilds/{GUILDID}/validateDetails
        /// Test:   curl http://localhost:5550/api/guilds/{GUILDID}/validateName -X POST -H "Content-Type:application/json" -d '{"NewDisplayName":"bob", "NewDisplayDetails":"A gild called bob"}'
        /// </summary>
        [HttpPost("guilds/{guildIdStr}/validateDetails")]
        [Consumes("application/json")]
        [RequirePermission(MetaplayPermissions.ApiGuildsEditDetails)]
        public async Task<IActionResult> ValidateDetails(string guildIdStr, [FromBody] GuildChangeDetailsBody body)
        {
            // Parse and validate parameters
            EntityId guildId = ParseEntityIdStr(guildIdStr, EntityKindCore.Guild);
            _ = (IGuildModelBase)await GetEntityStateAsync(guildId);

            // Validate the name
            InternalGuildChangeDisplayNameAndDescriptionRequest request = new InternalGuildChangeDisplayNameAndDescriptionRequest(GuildEventInvokerInfo.ForAdmin(), body.NewDisplayName, body.NewDescription, validateOnly: true);
            InternalGuildChangeDisplayNameAndDescriptionResponse response = await AskEntityAsync<InternalGuildChangeDisplayNameAndDescriptionResponse>(guildId, request);

            return Ok(new GuildChangeDetailsReturn(response.DisplayNameWasValid, response.DescriptionWasValid));
        }

        /// <summary>
        /// API endpoint to change a guild's name
        /// Usage:  POST /api/guilds/{GUILDID}/changeDetails
        /// Test:   curl http://localhost:5550/api/guilds/{GUILDID}/changeName -X POST -H "Content-Type:application/json" -d '{"NewDisplayName":"bob", "NewDisplayDetails":"A gild called bob"}'
        /// </summary>
        [HttpPost("guilds/{guildIdStr}/changeDetails")]
        [Consumes("application/json")]
        [RequirePermission(MetaplayPermissions.ApiGuildsEditDetails)]
        public async Task<IActionResult> ChangeDetails(string guildIdStr, [FromBody] GuildChangeDetailsBody body)
        {
            // Parse and validate parameters
            EntityId guildId = ParseEntityIdStr(guildIdStr, EntityKindCore.Guild);
            _ = (IGuildModelBase)await GetEntityStateAsync(guildId);

            // Validate the name
            InternalGuildChangeDisplayNameAndDescriptionRequest request = new InternalGuildChangeDisplayNameAndDescriptionRequest(GuildEventInvokerInfo.ForAdmin(), body.NewDisplayName, body.NewDescription, validateOnly: false);
            InternalGuildChangeDisplayNameAndDescriptionResponse response = await AskEntityAsync<InternalGuildChangeDisplayNameAndDescriptionResponse>(guildId, request);

            if (response.ChangeWasCommitted)
            {
                await WriteAuditLogEventAsync(new GuildEventBuilder(guildId, new GuildEventNameAndDescriptionChanged(body.NewDisplayName, body.NewDescription)));
                return Ok();
            }
            else
            {
                return StatusCode(400);
            }
        }
    }
}

#endif
