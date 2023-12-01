// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Cloud.Services;
using Metaplay.Cloud.Utility;
using Metaplay.Core;
using Metaplay.Server.Authentication;
using Metaplay.Server.Database;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;

namespace Metaplay.Server.AdminApi.Controllers
{
    /// <summary>
    /// Controller for handling Facebook Data Deletion requests.
    /// </summary>
    public class FacebookDataDeletionWebhookController : MetaplayWebhookController
    {
        public FacebookDataDeletionWebhookController(ILogger<FacebookDataDeletionWebhookController> logger, IActorRef adminApi) : base(logger, adminApi)
        {
        }

        class DeletionRequestJson
        {
            public string user_id = default;
            public int expires = default;
        }
        class DeletionResponseJson
        {
            public string url = default;
            public string confirmation_code = default;
        }

        static readonly string ResultHtmlTemplate =
@"
<!DOCTYPE html>
<html lang=""en"">
 <head>
  <meta charset=""utf-8"">
  <title>Data Removal Status</title>
  <style>table, td, th {{ border: 1px solid black; border-collapse: collapse; padding: 4px; }}</style>
 </head>
 <body>
  <h1>Data Removal Status</h1>
  <p>This page tracks the status of the data removal requests. Timestamps are in UTC.</p>
  <table>
    <tr><th>Request Id</th><td>{0}</td></tr>
    <tr><th>Status</th><td>Complete</td></tr>
    <tr><th>Received At</th><td>{1}</td></tr>
    <tr><th>Completed At</th><td>{2}</td></tr>
  </table>
 </body>
</html>
";

        /// <summary>
        /// Usage:  POST /webhook/facebookdatadeletion
        /// Test:   curl -X POST -F 'signed_request=abcdef.ghijkl' http://localhost:5550/webhook/facebookdatadeletion
        /// </summary>
        [HttpPost("facebookdatadeletion")]
        [Consumes("application/octet-stream")]
        public async Task<ActionResult> PostFacebookDataDeletion()
        {
            (FacebookOptions facebookOpts, BlobStorageOptions blobStorageOpts) = RuntimeOptionsRegistry.Instance.GetCurrent<FacebookOptions, BlobStorageOptions>();
            if (string.IsNullOrEmpty(facebookOpts.AppSecret))
            {
                _logger.LogError("Facebook Data Deletion request failed. FacebookOptions.AppSecret was not set.");
                return StatusCode(503);
            }
            if (string.IsNullOrEmpty(blobStorageOpts.S3PublicBucketCdnUrl))
            {
                _logger.LogError("Facebook Data Deletion request failed. BlobStorageOptions.S3PublicBucketCdnUrl was not set.");
                return StatusCode(503);
            }

            DeletionRequestJson request;
            try
            {
                request = FacebookSignedRequestParser.ParseSignedRequest<DeletionRequestJson>(Request, facebookOpts.AppSecret);
            }
            catch (FacebookSignedRequestParser.BadSignedRequestException ex)
            {
                _logger.LogWarning("Facebook Data Deletion request rejected: {0}", ex.Message);
                return BadRequest(ex.Message);
            }

            if (request.expires != 0 && Util.GetUtcUnixTimeSeconds() > request.expires)
            {
                _logger.LogWarning("Facebook Data Deletion request rejected. Request was expired.");
                return BadRequest("request expired");
            }

            // request validated, start deletion

            string      confirmationCode    = SecureTokenUtil.GenerateRandomStringToken(length: 32);
            DateTime    startAt             = DateTime.UtcNow;

            _logger.LogInformation("Received Data Deletion request for Facebook user {0}. ConfirmationCode={1}.", request.user_id, confirmationCode.ToString());
            await DoDeleteFacebookData(request.user_id, confirmationCode);

            DateTime    completeAt          = DateTime.UtcNow;

            // Delete complete, create state page that shows the request was completed
            string pageName = $"{confirmationCode}.html";
            using (IBlobStorage storage = blobStorageOpts.CreatePublicBlobStorage("FacebookDataDeletionReport"))
            {
                string                  rendered    = string.Format(CultureInfo.InvariantCulture, ResultHtmlTemplate, confirmationCode.ToString(), startAt.ToString("o", CultureInfo.InvariantCulture), completeAt.ToString("o", CultureInfo.InvariantCulture));
                byte[]                  content     = Encoding.UTF8.GetBytes(rendered);
                BlobStoragePutHints     hints       = new BlobStoragePutHints();

                hints.ContentType = "text/html";
                await storage.PutAsync(pageName, content, hints);
            }

            Uri cdnBaseUri  = new Uri(blobStorageOpts.S3PublicBucketCdnUrl);
            Uri fileUrl     = new Uri(cdnBaseUri, $"FacebookDataDeletionReport/{pageName}");
            return Ok(new DeletionResponseJson()
            {
                url = fileUrl.ToString(),
                confirmation_code = confirmationCode.ToString(),
            });
        }

        async Task DoDeleteFacebookData(string userId, string confirmationCode)
        {
            AuthenticationKey               facebookUser    = new AuthenticationKey(AuthenticationPlatform.FacebookLogin, userId);
            PersistedAuthenticationEntry    entry           = await Authenticator.TryGetAuthenticationEntryAsync(facebookUser);

            if (entry == null)
            {
                // User did not log in, or has logged out. We have no data of the user, so we are done.
                return;
            }

            PersistedPlayerBase player = await MetaDatabase.Get().TryGetAsync<PersistedPlayerBase>(entry.PlayerEntityId.ToString());
            if (player == null || player.Payload == null)
            {
                // User has been deleted. We have no data of the user, so we are done.
            }
            else
            {
                // Detach Facebook Authentication from the player, if it had one
                await AskEntityAsync<InternalPlayerFacebookAuthenticationRevokedResponse>(entry.PlayerEntityId, new InternalPlayerFacebookAuthenticationRevokedRequest(
                    source:             InternalPlayerFacebookAuthenticationRevokedRequest.RevocationSource.DataDeletionRequest,
                    authenticationKey:  facebookUser,
                    confirmationCode:   confirmationCode));
            }

            // Remove the auth record
            await Authenticator.RemoveAuthenticationEntryAsync(facebookUser);
        }
    }
}
