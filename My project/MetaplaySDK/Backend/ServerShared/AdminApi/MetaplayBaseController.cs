// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.Persistence;
using Metaplay.Core;
using Metaplay.Server.Database;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using static Metaplay.Server.AdminApi.Controllers.Exceptions;

namespace Metaplay.Server.AdminApi
{
    /// <summary>
    /// Configure the authentication domain "Webhook" for HTTP endpoints at /webhook.
    /// The controller does not apply any authentication on the endpoints and it is
    /// the responsibility of the individual endpoints to handle their own authentication.
    /// </summary>
    public class WebhookAuthenticationConfig : AuthenticationDomainConfig
    {
        public override void ConfigureServices(IServiceCollection services, AdminApiOptions opts)
        {
        }

        public override void ConfigureApp(WebApplication app, AdminApiOptions opts)
        {
            // Register a 404 handler with the same CORS policy as proper endpoints (to get sensible error messages)
            Register404Handler(app, pathPrefix: MetaplayWebhookController.RoutePathPrefix, corsPolicy: null, handler: (HttpContext ctx, string sanitizedPath) =>
            {
                _log.Warning("Request to non-existent webhook endpoint {Method} '{SanitizedPath}'", ctx.Request.Method, sanitizedPath);
            });
        }
    }

    /// <summary>
    /// User-facing base class for all webhook API endpoints. Any controller derived from this
    /// class will have routes starting with '/webhook'
    /// </summary>
    [Route(RoutePathPrefix)]
    public abstract class MetaplayWebhookController : MetaplayController
    {
        public const string RoutePathPrefix = "webhook";

        protected MetaplayWebhookController(ILogger logger, IActorRef adminApi) : base(logger, adminApi)
        {
        }
    }

    /// <summary>
    /// Internal base class for all Metaplay API controllers. Includes functionality for:
    /// - Parsing request body data
    /// - Querying various entity types
    ///
    /// NB: Client code should not derive from this controller directly as it has no route, rather
    /// you should derive from one of the MetaplayXXXControllers above
    ///
    /// Note: Inherits Controller (instead of ControllerBase) to get MVC view functionality.
    /// </summary>
    [ApiController]
    public abstract class MetaplayController : Controller, IEntityAsker
    {
        protected readonly ILogger      _logger;
        protected readonly IActorRef    _adminApi;

        protected MetaplayController(ILogger logger, IActorRef adminApi)
        {
            _logger     = logger;
            _adminApi   = adminApi;
        }

        /// <summary>
        /// Parse and deserialise the request body as the specified class type
        /// Throws MetaplayHttpException on error
        /// </summary>
        /// <typeparam name="T">Type of class</typeparam>
        /// <returns>Deserialised object</returns>
        protected async Task<T> ParseBodyAsync<T>()
        {
            try
            {
                using (StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8))
                using (TextReader textReader = new StringReader(await reader.ReadToEndAsync()))
                using (JsonReader jsonReader = new JsonTextReader(textReader))
                {
                    T result = AdminApiJsonSerialization.Serializer.Deserialize<T>(jsonReader);
                    if (result == null)
                        throw new Exception("Expecting body data but no body was supplied.");
                    return result;
                }
            }
            catch (JsonReaderException ex)
            {
                throw new MetaplayHttpException(400, "Cannot parse body JSON.", ex.Message);
            }
            catch (Exception ex)
            {
                throw new MetaplayHttpException(400, "Cannot parse body.", ex.Message);
            }
        }

        /// <summary>
        /// Parse and deserialise the request body as the specified class type
        /// Throws MetaplayHttpException on error
        /// </summary>
        /// <typeparam name="T">Type of class</typeparam>
        /// <param name="type"></param>
        /// <returns>Deserialised object</returns>
        protected async Task<T> ParseBodyAsync<T>(Type type) where T : class
        {
            if (!type.IsDerivedFrom<T>())
                throw new MetaplayHttpException(400, "Bad class type in request.", $"Class {type.Name} is not derived from {typeof(T).Name}.");

            try
            {
                using (StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8))
                using (TextReader textReader = new StringReader(await reader.ReadToEndAsync()))
                using (JsonReader jsonReader = new JsonTextReader(textReader))
                {
                    T result = (T)AdminApiJsonSerialization.Serializer.Deserialize(jsonReader, type);
                    if (result == null)
                        throw new Exception("Expecting body data but no body was supplied.");
                    return result;
                }
            }
            catch (JsonReaderException ex)
            {
                throw new MetaplayHttpException(400, "Cannot parse body JSON.", ex.Message);
            }
            catch (Exception ex)
            {
                throw new MetaplayHttpException(400, "Cannot parse body.", ex.Message);
            }
        }

        /// <summary>
        /// Parse and deserialise the request body as the specified class type
        /// Throws MetaplayHttpException on error
        /// </summary>
        /// <param name="type"></param>
        /// <returns>Deserialised object</returns>
        protected async Task<object> ParseBodyAsync(System.Type type)
        {
            try
            {
                using (StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8))
                using (TextReader textReader = new StringReader(await reader.ReadToEndAsync()))
                using (JsonReader jsonReader = new JsonTextReader(textReader))
                {
                    object result = AdminApiJsonSerialization.Serializer.Deserialize(jsonReader, type);
                    if (result == null)
                        throw new Exception("Expecting body data but no body was supplied.");
                    return result;
                }
            }
            catch (JsonReaderException ex)
            {
                throw new MetaplayHttpException(400, "Cannot parse body JSON.", ex.Message);
            }
            catch (Exception ex)
            {
                throw new MetaplayHttpException(400, "Cannot parse body.", ex.Message);
            }
        }

        protected async Task<byte[]> ReadBodyBytesAsync()
        {
            using (MemoryStream memStream = new MemoryStream(8192))
            {
                await Request.Body.CopyToAsync(memStream);
                return memStream.ToArray();
            }
        }

        /// <summary>
        /// Routes an emulated "CastMessage" to the target entity, but such that it's transmitted as an EntityAsk
        /// so that any failures are thrown as <see cref="EntityAskExceptionBase"/>s such as <see cref="UnexpectedEntityAskError"/>
        /// or <see cref="EntityAskRefusal"/>.
        /// </summary>
        protected async Task TellEntityAsync(EntityId entityId, MetaMessage message)
        {
            // \note Throws on failure, otherwise just ignore the return value
            _ = await AskEntityAsync<HandleMessageResponse>(entityId, new HandleMessageRequest(message));
        }

        protected Task<TResult> AskEntityAsync<TResult>(EntityId entityId, MetaMessage message) where TResult : MetaMessage
        {
            return AdminApiActor.ForwardAskToEntity.ExecuteAsync<TResult>(_adminApi, entityId, message);
        }

        Task<TResult> IEntityAsker.EntityAskAsync<TResult>(EntityId entity, MetaMessage request)
        {
            return AskEntityAsync<TResult>(entity, request);
        }

        /// <summary>
        /// Utility function to parse and validate a MetaGuid from a string
        /// Throws MetaplayHttpException on error
        /// </summary>
        /// <param name="metaGuid">String representation of the MetaGuid</param>
        /// <returns>EntityId of the player</returns>
        public static MetaGuid ParseMetaGuidStr(string metaGuidStr)
        {
            MetaGuid metaGuid;
            try
            {
                metaGuid = MetaGuid.Parse(metaGuidStr);
            }
            catch (FormatException ex)
            {
                throw new MetaplayHttpException(400, "Invalid MetaGuid.", $"MetaGuid {metaGuidStr} is not valid: {ex.Message}");
            }
            return metaGuid;
        }

        /// <summary>
        /// Utility function to parse an entity id of a certain kind. If the format is incorrect or the resolved entity is not of kind <paramref name="expectedKind"/>, throws 400.
        /// </summary>
        /// <param name="entityIdStr">String representation of the Entity Id</param>
        public static EntityId ParseEntityIdStr(string entityIdStr, EntityKind expectedKind)
        {
            EntityId entityId;
            try
            {
                entityId = EntityId.ParseFromString(entityIdStr);
            }
            catch (FormatException ex)
            {
                throw new MetaplayHttpException(400, $"{expectedKind} not found.", $"{entityIdStr} is not a valid {expectedKind} entity ID: {ex.Message}");
            }
            if (!entityId.IsOfKind(expectedKind))
            {
                throw new MetaplayHttpException(400, $"{expectedKind} not found.", $"{entityIdStr} is not {expectedKind} entity ID.");
            }
            return entityId;
        }

        /// <summary>
        /// Returns the Persisted state of an entity. If the entity does not exist, throws 404.
        /// </summary>
        protected async Task<IPersistedEntity> GetPersistedEntityAsync(EntityId entityId)
        {
            if (!EntityConfigRegistry.Instance.TryGetPersistedConfig(entityId.Kind, out PersistedEntityConfig entityConfig))
                throw new MetaplayHttpException(400, "Invalid Entity kind.", $"Entity kind {entityId.Kind} does not refer to a PersistedEntity");

            IPersistedEntity persisted = await MetaDatabaseBase.Get().TryGetAsync<IPersistedEntity>(type: entityConfig.PersistedType, entityId.ToString()).ConfigureAwait(false);
            if (persisted == null || persisted.Payload == null)
                throw new MetaplayHttpException(404, "Persisted entity not found.", $"Cannot find {entityId}.");

            return persisted;
        }

        /// <summary>
        /// Return the IP of the remote address that made the Http request.
        /// </summary>
        /// <returns>IPAddress, can be null</returns>
        protected IPAddress TryGetRemoteIp()
        {
            // return IPAddress.Parse($"{new Random().Next(1,250)}.184.101.0");      // PKG - Change this to fake remote IP address for testing Audit Logs, etc

            if (Request.Headers.TryGetValue("X-Forwarded-For", out StringValues forwardedIps))
                if (IPAddress.TryParse(forwardedIps.First(), out IPAddress forwardedIpAddress))
                   return forwardedIpAddress;
            return HttpContext.Connection.RemoteIpAddress;
        }
    }
}
