// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Cloud;
using Metaplay.Core;
using Metaplay.Server.Database;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace Metaplay.Server.AdminApi.Controllers
{
    /// <summary>
    /// Controller for various endpoints devoted to testing
    /// </summary>
    public class TestingController : GameAdminApiController
    {
        IMetaLogger _log = MetaLogger.ForContext<TestingController>();

        public TestingController(ILogger<TestingController> logger, IActorRef adminApi) : base(logger, adminApi)
        {
        }

        /// <summary>
        /// HTTP request/response formats
        /// </summary>
        public class NewPlayerResponse
        {
            public EntityId Id;
            public NewPlayerResponse(EntityId id) { Id = id; }
        }

        /// <summary>
        /// Creates a new player without any authentication
        /// Usage:  POST /api/testing/createPlayer
        /// Test:   curl http://localhost:5550/api/testing/createPlayer --request POST
        /// </summary>
        [HttpPost("testing/createPlayer")]
        [RequirePermission(MetaplayPermissions.ApiTestsAll)]
        public async Task<ActionResult<GlobalStatusResponse>> CreatePlayer()
        {
            EntityId playerId = await DatabaseEntityUtil.CreateNewPlayerAsync(_log);
            await TellEntityAsync(playerId, PlayerResetState.Instance);
            return Ok(new NewPlayerResponse(playerId));
        }
    }
}
