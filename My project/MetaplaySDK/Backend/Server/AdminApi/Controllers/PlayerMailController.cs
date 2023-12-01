// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Core;
using Metaplay.Core.InGameMail;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using static System.FormattableString;

namespace Metaplay.Server.AdminApi.Controllers
{
    /// <summary>
    /// Controller for stock Metaplay SDK route to send mail to an individual player.
    /// </summary>
    public class PlayerMailController : GameAdminApiController
    {
        public PlayerMailController(ILogger<PlayerMailController> logger, IActorRef adminApi) : base(logger, adminApi)
        {
        }

        /// <summary>
        /// Audit log events
        /// </summary>
        [MetaSerializableDerived(MetaplayAuditLogEventCodes.PlayerMailSent)]
        public class PlayerEventMailSent : PlayerEventPayloadBase
        {
            [MetaMember(1)] public MetaInGameMail Mail { get; private set; }
            public PlayerEventMailSent() { }
            public PlayerEventMailSent(MetaInGameMail mail)
            {
                Mail = mail;
            }
            override public string EventTitle => "Mail sent";
            override public string EventDescription =>
                Invariant($"Mail \"{Mail.Description}\" (ID: {Mail.Id}) sent to player.");
        }
        [MetaSerializableDerived(MetaplayAuditLogEventCodes.PlayerMailDeleted)]
        public class PlayerEventMailDeleted : PlayerEventPayloadBase
        {
            [MetaMember(1)] public int LegacyMailId { get; private set; }
            [MetaMember(2)] public MetaGuid MailId { get; private set; }
            public PlayerEventMailDeleted() { }
            public PlayerEventMailDeleted(MetaGuid mailId)
            {
                MailId = mailId;
            }
            override public string EventTitle => "Mail deleted";
            override public string EventDescription => MailId.IsValid ?
                Invariant($"Mail (ID: {MailId}) deleted from player.") :
                Invariant($"Legacy mail (ID: {LegacyMailId}) deleted from player.");
        }

        /// <summary>
        /// API endpoint to send mail to a player
        /// Usage:  POST /api/players/{PLAYERID}/sendMail
        /// </summary>
        [HttpPost("players/{playerIdStr}/sendMail")]
        [Consumes("application/json")]
        [RequirePermission(MetaplayPermissions.ApiPlayersMail)]
        public async Task SendMail(string playerIdStr)
        {
            _logger.LogInformation("Sending mail to {0}", playerIdStr);

            // Validate player Id
            EntityId playerId = await ParsePlayerIdStrAndCheckForExistenceAsync(playerIdStr);

            // Deserialize mail contents
            MetaInGameMail mail = await ParseBodyAsync<MetaInGameMail>();

            // Enqueue action
            await EnqueuePlayerServerActionAsync(playerId, new PlayerAddMail(mail, mail.CreatedAt));

            // Audit log event
            await WriteAuditLogEventAsync(new PlayerEventBuilder(playerId, new PlayerEventMailSent(mail)));
        }

        /// <summary>
        /// API endpoint to delete mail from a player
        /// Usage:  POST /api/players/{PLAYERID}/deleteMail
        /// </summary>
        [HttpDelete("players/{playerIdStr}/deleteMail/{mailIdStr}")]
        [RequirePermission(MetaplayPermissions.ApiPlayersMail)]
        public async Task DeleteMail(string playerIdStr, string mailIdStr)
        {
            // Parse mail ID
            MetaGuid mailId = MetaGuid.Parse(mailIdStr);

            // Validate player Id
            EntityId playerId = await ParsePlayerIdStrAndCheckForExistenceAsync(playerIdStr);

            _logger.LogInformation($"Deleting mail {mailId} from {playerId}");

            // Delete the mail
            await EnqueuePlayerServerActionAsync(playerId, new PlayerForceDeleteMail(mailId));

            // Audit log event
            await WriteAuditLogEventAsync(new PlayerEventBuilder(playerId, new PlayerEventMailDeleted(mailId)));
        }
    }
}
