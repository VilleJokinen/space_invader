// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Cloud.Services.Geolocation;
using Metaplay.Core.Player;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Net;

namespace Metaplay.Server.AdminApi.Controllers
{
    /// <summary>
    /// Controller for echoing details back to the requester
    /// </summary>
    public class EchoController : MetaplayAdminApiController
    {
        public EchoController(ILogger<EchoController> logger, IActorRef adminApi) : base(logger, adminApi)
        {
        }

        /// <summary>
        /// Echo request details back to the requester
        /// Usage:  GET /api/echo
        /// Test:   curl http://localhost:5550/api/echo
        /// </summary>
        [HttpGet("echo")]
        [AllowAnonymous]
        public ActionResult Echo()
        {
            IPAddress remoteIp = TryGetRemoteIp();
            PlayerLocation? location = remoteIp != null ? Geolocation.Instance.TryGetPlayerLocation(remoteIp) : null;

            object result = new
            {
                Headers = Request.Headers.ToDictionary(item => item.Key, item => item.Value.ToList()),
                Method = Request.Method,
                ContentType = Request.ContentType,
                ContentLength = Request.ContentLength,
                Path = Request.Path,
                Host = Request.Host.ToString(),
                Protocol = Request.Protocol,
                Cookies = Request.Cookies.ToDictionary(item => item.Key, item => item.Value),
                QueryString = Request.QueryString.ToString(),
                Query = Request.Query.ToDictionary(item => item.Key, item => item.Value.ToList()),
                Metaplay = new
                {
                    UserId = GetUserId(),
                    RemoteIp = remoteIp?.ToString(),
                    Location = location.HasValue ? location.Value.Country.IsoCode : null,
                },
            };

            return Ok(result);
        }
    }
}
