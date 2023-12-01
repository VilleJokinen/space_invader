// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Cloud.Services;
using Metaplay.Core;
using Metaplay.Server.Authentication;
using Metaplay.Server.Database;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace Metaplay.Server.AdminApi.Controllers
{
    /// <summary>
    /// Controller for handling Facebook Deauthorize Callback URL requests.
    /// </summary>
    public class FacebookDeauthorizationWebhookController : MetaplayWebhookController
    {
        public FacebookDeauthorizationWebhookController(ILogger<FacebookDeauthorizationWebhookController> logger, IActorRef adminApi) : base(logger, adminApi)
        {
        }

        class DeauthorizationRequestJson
        {
            public string user_id = default;
            public int expires = default;
        }

        /// <summary>
        /// Usage:  POST /webhook/facebookdeauthorize
        /// Test:   curl -X POST -F 'signed_request=abcdef.ghijkl' http://localhost:5550/webhook/facebookdeauthorize
        /// </summary>
        [HttpPost("facebookdeauthorize")]
        [Consumes("application/octet-stream")]
        public async Task<IActionResult> PostFacebookDeauthorize()
        {
            FacebookOptions facebookOpts = RuntimeOptionsRegistry.Instance.GetCurrent<FacebookOptions>();
            if (string.IsNullOrEmpty(facebookOpts.AppSecret))
            {
                _logger.LogError("Facebook User Deauthorization request failed. FacebookOptions.AppSecret was not set.");
                return StatusCode(503);
            }

            DeauthorizationRequestJson request;
            try
            {
                request = FacebookSignedRequestParser.ParseSignedRequest<DeauthorizationRequestJson>(Request, facebookOpts.AppSecret);
            }
            catch (FacebookSignedRequestParser.BadSignedRequestException ex)
            {
                _logger.LogWarning("Facebook User Deauthorization request rejected: {0}", ex.Message);
                return BadRequest(ex.Message);
            }

            if (request.expires != 0 && Util.GetUtcUnixTimeSeconds() > request.expires)
            {
                _logger.LogWarning("Facebook User Deauthorization request rejected. Request was expired.");
                return BadRequest("request expired");
            }

            // request validated, start deauthorization

            _logger.LogInformation("Received Facebook User Deauthorization request for Facebook user {0}.", request.user_id);
            await DoDeauthorizeFacebookUser(request.user_id);
            return Ok();
        }

        async Task DoDeauthorizeFacebookUser(string userId)
        {
            AuthenticationKey               facebookUser    = new AuthenticationKey(AuthenticationPlatform.FacebookLogin, userId);
            PersistedAuthenticationEntry    entry           = await Authenticator.TryGetAuthenticationEntryAsync(facebookUser);

            if (entry == null)
            {
                // User did not log in, or has logged out. User is deauthorized, so we are done.
                return;
            }

            PersistedPlayerBase player = await MetaDatabase.Get().TryGetAsync<PersistedPlayerBase>(entry.PlayerEntityId.ToString());
            if (player == null || player.Payload == null)
            {
                // Player has been deleted. We have no login records, so we are done.
            }
            else
            {
                // Detach Facebook Authentication from the player, if it had one
                await AskEntityAsync<InternalPlayerFacebookAuthenticationRevokedResponse>(entry.PlayerEntityId, new InternalPlayerFacebookAuthenticationRevokedRequest(
                    source:             InternalPlayerFacebookAuthenticationRevokedRequest.RevocationSource.DeauthorizationRequest,
                    authenticationKey:  facebookUser,
                    confirmationCode:   null));
            }

            // Remove the auth record
            await Authenticator.RemoveAuthenticationEntryAsync(facebookUser);
        }
    }
}
