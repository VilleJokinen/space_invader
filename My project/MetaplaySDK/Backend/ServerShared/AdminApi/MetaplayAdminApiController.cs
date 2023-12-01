// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using Metaplay.Server.AdminApi.AuditLog;
using Metaplay.Server.AdminApi.Controllers;
using Metaplay.Server.Database;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Metaplay.Server.AdminApi
{
    /// <summary>
    /// Configure the authentication domain "AdminApi" HTTP endpoints. These are used by the LiveOps Dashboard.
    /// </summary>
    public class AdminApiAuthenticationConfig : AuthenticationDomainConfig
    {
        public override void ConfigureServices(IServiceCollection services, AdminApiOptions opts)
        {
            // Warn if auth is disabled (unless we're running in a local environment, when it's ok to do that)
            if (opts.Type == AuthenticationType.None && RuntimeOptionsRegistry.Instance.EnvironmentFamily != EnvironmentFamily.Local)
                _log.Warning("Authentication disabled for HTTP API! Are you sure?");

            // Validate AdminApi controllers
            ValidateAdminApiControllers(opts);

            // Configure CORS
            services.AddCors(options =>
            {
                options.AddPolicy(MetaplayAdminApiController.CorsPolicy, builder =>
                {
                    // If running locally, allow requests from 5551 (from 'npm run serve')
                    if (RuntimeOptionsBase.IsLocalEnvironment)
                    {
                        builder
                          .AllowAnyHeader()
                          .AllowAnyMethod()
                          .AllowCredentials()
                          .WithOrigins("http://localhost:5551");
                    }
                });
            });

            // Register authentication scheme for ASP.NET based on authentication method used
            AuthenticationBuilder authBuilder = services.AddAuthentication();
            switch (opts.Type)
            {
                case AuthenticationType.None:
                    authBuilder.AddScheme<AnonymousAuthenticationOptions, AnonymousAuthenticationHandler>(MetaplayAdminApiController.AuthenticationScheme, options =>
                    {
                    });
                    break;

                case AuthenticationType.JWT:
                    authBuilder.AddJwtBearer(MetaplayAdminApiController.AuthenticationScheme, options =>
                    {
                        options.Authority = opts.GetAdminApiDomain();
                        options.Audience  = opts.JwtConfiguration.Audience;
                    });
                    break;

                default:
                    throw new InvalidOperationException($"Invalid or incomplete auth type '{opts.Type}' chosen.");
            }

            // Register the authorization handler for [RequirePermission]
            services.AddSingleton<IAuthorizationHandler, AdminApiPermissionHandler>();
        }

        public override void ConfigureApp(WebApplication app, AdminApiOptions opts)
        {
            // Register a 404 handler with the same CORS policy as proper endpoints (to get sensible error messages)
            Register404Handler(app, pathPrefix: MetaplayAdminApiController.RoutePathPrefix, corsPolicy: MetaplayAdminApiController.CorsPolicy, handler: (HttpContext ctx, string sanitizedPath) =>
            {
                _log.Warning("Request to non-existent admin API endpoint {Method} '{SanitizedPath}'", ctx.Request.Method, sanitizedPath);
            });
        }

        void ValidateAdminApiControllers(AdminApiOptions adminOpts)
        {
            // Check that all AdminApi controller endpoints specify either an [RequirePermission] or an [AllowAnonymous] endpoints.
            foreach (Type controller in TypeScanner.GetDerivedTypes<MetaplayAdminApiController>())
            {
                IEnumerable<MethodInfo> handlerMethods =
                    controller.GetMethods(BindingFlags.Instance | BindingFlags.DeclaredOnly | BindingFlags.Public)
                    .Where(methodInfo => methodInfo.GetCustomAttribute<HttpMethodAttribute>() != null);
                foreach (MethodInfo methodInfo in handlerMethods)
                {
                    bool hasPermissionAttribute = methodInfo.GetCustomAttribute<RequirePermissionAttribute>() != null;
                    bool hasAnonymousAttribute = methodInfo.GetCustomAttribute<AllowAnonymousAttribute>() != null;
                    if (!hasPermissionAttribute && !hasAnonymousAttribute)
                        throw new InvalidOperationException($"AdminApi controller endpoint {controller.ToGenericTypeString()}.{methodInfo.Name}() must specify either [RequirePermission] or [AllowAnonymous] attribute.");
                    else if (hasPermissionAttribute && hasAnonymousAttribute)
                        throw new InvalidOperationException($"AdminApi controller endpoint {controller.ToGenericTypeString()}.{methodInfo.Name}() must not specify both [RequirePermission] or [AllowAnonymous] attributes.");
                }
            }

            // Check that all defined permissions are used at least once by a controller. The only exception are
            // permissions that are only ever checked on the Dashboard - these are marked as DashboardOnly inside
            // the Permission attribute
            string[] activePermissions = GetAllUniqueEndpointPermissions();
            PermissionGroupDefinition[] allPermissionGroups = AdminApiOptions.GetAllPermissions();
            IEnumerable<PermissionDefinition> allPermissionDefs = allPermissionGroups.SelectMany(group => group.Permissions);
            foreach (string permission in adminOpts.ResolvedPermissions.RolesForPermission.Keys)
            {
                if (!allPermissionDefs.Single(permissionDef => permissionDef.Name == permission).IsDashboardOnly && !activePermissions.Contains(permission))
                    throw new Exception($"The permission {permission} is defined as a [Permission] option but it is not referenced inside an [RequirePermission] attribute by any server code.");
            }

            // Check that all permissions used by the endpoints are using valid declared Permissions.
            HashSet<string> nonPermissionScopes = new HashSet<string>(activePermissions);
            nonPermissionScopes.ExceptWith(adminOpts.ResolvedPermissions.RolesForPermission.Keys);
            if (nonPermissionScopes.Count > 0)
                throw new Exception($"The permissions [{string.Join(", ", nonPermissionScopes)}] are referenced inside an [RequirePermission] attribute but have no corresponding [Permission] definition.");
        }

        /// <summary>
        /// Get all unique required permission strings from all <see cref="MetaplayAdminApiController"/> endpoints.
        /// The required permission strings are specified using the <see cref="RequirePermissionAttribute"/> on the endpoints.
        /// Any controllers of disabled features are skipped.
        /// </summary>
        /// <returns></returns>
        static string[] GetAllUniqueEndpointPermissions()
        {
            return TypeScanner
                .GetDerivedTypes<MetaplayAdminApiController>()
                .Where(type => type.IsMetaFeatureEnabled())
                .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.DeclaredOnly | BindingFlags.Public))
                .Select(x => x.GetCustomAttribute<RequirePermissionAttribute>()?.PermissionName)
                .Where(x => x != null)
                .Distinct()
                .ToArray();
        }
    }

    /// <summary>
    /// Base class for all Dashboard-facing AdminApi endpoints. Any controller derived from this
    /// class will have routes starting with '/api'.
    ///
    /// <see cref="OkObjectResult"/>, <see cref="ObjectResult"/>, and <see cref="JsonResult"/> results will be wrapped in a <see cref="JsonResultWithErrors"/>
    /// This means that any errors thrown in serialization will not stop serialization, the serializer will ignore the offending property and move on to the next property.
    ///
    /// Includes helpers for:
    /// - Authorization helpers
    /// - Writing audit logs
    /// </summary>
    [Route(RoutePathPrefix)]
    [EnableCors(CorsPolicy)]
    [Authorize(AuthenticationSchemes = AuthenticationScheme)]
    public abstract class MetaplayAdminApiController : MetaplayController
    {
        public const string RoutePathPrefix      = "api";
        public const string CorsPolicy           = "AdminApiCorsPolicy";
        public const string AuthenticationScheme = "AdminApi";

        static readonly JsonSerializerSettings _baseSettings = new JsonSerializerSettings();

        static MetaplayAdminApiController()
        {
            AdminApiJsonSerialization.ApplySettings(_baseSettings);
        }

        protected MetaplayAdminApiController(ILogger logger, IActorRef adminApi) : base(logger, adminApi)
        {
        }

        [NonAction]
        public override void OnActionExecuted(ActionExecutedContext context)
        {
            if (context.Result is JsonResult jsonResult)
            {
                context.Result = new JsonResultWithErrors(jsonResult.Value, jsonResult.SerializerSettings ?? _baseSettings, _logger)
                {
                    ContentType = jsonResult.ContentType,
                    StatusCode = jsonResult.StatusCode,
                };
            }
            else if (context.Result is ObjectResult objectResult && objectResult.Value is not string)
            {
                context.Result = new JsonResultWithErrors(objectResult.Value, _baseSettings, _logger)
                {
                    ContentType = objectResult.ContentTypes.FirstOrDefault(),
                    StatusCode  = objectResult.StatusCode,
                };
            }

            base.OnActionExecuted(context);
        }

        /// <summary>
        /// Return the id of the current user. If auth /is not/ enabled then this
        /// simply returns "auth_not_enabled". If auth /is/ enabled then we return
        /// one of the following:
        ///     1) Email of the user, or if that cannot be found
        ///     2) user_id from the oauth token, of if that cannot be found
        ///     3) "no_id" - this is returned if the user is not authenticated yet
        /// There are two version of this function:
        ///     1) GetUserId() for use inside endpoints where user is implicitly known
        ///     2) GetUserId(user) for use outside endpoints (eg: exception filter)
        /// </summary>
        /// <returns></returns>
        protected string GetUserId()
        {
            return GetUserId(User);
        }

        static public string GetUserId(ClaimsPrincipal user)
        {
            AdminApiOptions authOpts = RuntimeOptionsRegistry.Instance.GetCurrent<AdminApiOptions>();

            switch (authOpts.Type)
            {
                case AuthenticationType.None:
                    return "auth_not_enabled";

                case AuthenticationType.JWT:
                    string userId = user.Claims.FirstOrDefault(claim => claim.Type == "https://schemas.metaplay.io/email")?.Value;
                    if (string.IsNullOrEmpty(userId))
                        userId = user.Claims.FirstOrDefault(claim => claim.Type == ClaimTypes.Email)?.Value;
                    if (string.IsNullOrEmpty(userId))
                        userId = user.Claims.FirstOrDefault(claim => claim.Type == ClaimTypes.NameIdentifier)?.Value;
                    if (string.IsNullOrEmpty(userId))
                        return "no_id";
                    return userId;

                default:
                    throw new InvalidOperationException("Invalid or incomplete auth type chosen.");
            }
        }

        /// <summary>
        /// Return a list of roles belonging to the user that made the HTTP request. The request
        /// is allowed to specify a set of assumed roles for development purposes, so this method
        /// must not be used when determining whether a user can actually access an endpoint!
        /// </summary>
        /// <param name="httpContext"></param>
        /// <returns></returns>
        static public string[] GetUserRolesUntrusted(HttpContext httpContext)
        {
            AdminApiOptions authOpts = RuntimeOptionsRegistry.Instance.GetCurrent<AdminApiOptions>();

            switch (authOpts.Type)
            {
                case AuthenticationType.None:
                    // Try to grab the list of assumed roles from the special request header and
                    // fall back to a default value if the cookie does not exist
                    string[] assumedRoles = httpContext.Request.Headers.GetCommaSeparatedValues("Metaplay-AssumedUserRoles");
                    if (assumedRoles.Length == 0)
                        assumedRoles = new string[] { DefaultRole.GameAdmin };
                    return assumedRoles;

                case AuthenticationType.JWT:
                    // NB: There is no issuer check here, so these may have come from a different
                    // issuer and cannot be trusted
                    return httpContext.User
                        .FindAll(claim => claim.Type == "https://schemas.metaplay.io/roles")
                        .Select(claim => claim.Value)
                        .ToArray();

                default:
                    // Shouldn't get this far if auth is not set to a valid type
                    throw new InvalidOperationException("Should not be here");
            }
        }

        /// <summary>
        /// Given a list of roles, return the set of unique permissions that
        /// are available to these roles
        /// </summary>
        /// <param name="roles"></param>
        /// <returns></returns>
        static public string[] GetPermissionsFromRoles(string[] roles)
        {
            AdminApiOptions adminApiOpts = RuntimeOptionsRegistry.Instance.GetCurrent<AdminApiOptions>();
            return adminApiOpts.RolePermissions
                .Where(rolePermissions => roles.Contains(rolePermissions.Key))
                .SelectMany(rolePermissions => rolePermissions.Value)
                .Distinct()
                .ToArray();
        }

        /// <summary>
        /// Check if any role in a set of roles has the given permission. The role names must not
        /// contain the environment prefix, but must be the pure role name, eg, 'game-admin'.
        /// </summary>
        /// <param name="roles"></param>
        /// <param name="permission"></param>
        /// <returns></returns>
        public static bool HasPermissionForRoles(string[] roles, string permission)
        {
            AdminApiOptions adminApiOpts = RuntimeOptionsRegistry.Instance.GetCurrent<AdminApiOptions>();
            return adminApiOpts.ResolvedPermissions.RolesForPermission[permission].Any(role => roles.Contains(role));
        }

        // \note Audit log writes are here because they access the AdminApi-specific userId. The methods
        //       could be moved to MetaplayBaseController with injection of the userId from here.
        #region Auditlog

        /// <summary>
        /// Log a single audit log event
        /// </summary>
        /// <param name="builder"></param>
        /// <returns></returns>
        protected async Task WriteAuditLogEventAsync(EventBuilder builder)
        {
            MetaTime timeNow = MetaTime.Now;

            // Create persisted audit log event
            AuditLog.EventId eventId = AuditLog.EventId.FromTime(timeNow);
            EventSource source = EventSource.FromAdminApi(GetUserId());
            IPAddress sourceIpAddress = TryGetRemoteIp();
            PersistedAuditLogEvent entry = new PersistedAuditLogEvent(eventId, source, builder.Target, sourceIpAddress, builder.Payload);
            LogEventToConsole(builder.Target, builder.Payload, eventId);

            // Write to DB
            await MetaDatabaseBase.Get().InsertAsync(entry);
        }

        /// <summary>
        /// Log a group of related audit log events with many-to-many relationships
        /// </summary>
        /// <param name="builders"></param>
        /// <returns></returns>
        protected async Task WriteRelatedAuditLogEventsAsync(List<EventBuilder> builders)
        {
            MetaTime timeNow = MetaTime.Now;

            // Give each event an id
            List<(EventBuilder builder, AuditLog.EventId id)> buildersWithIds = new List<(EventBuilder, AuditLog.EventId)>();
            List<AuditLog.EventId> eventIds = new List<AuditLog.EventId>();
            builders.ForEach(builder =>
            {
                AuditLog.EventId eventId = AuditLog.EventId.FromTime(timeNow);
                buildersWithIds.Add((builder, eventId));
                eventIds.Add(eventId);
            });

            // Create persisted audit log events
            EventSource source = EventSource.FromAdminApi(GetUserId());
            IPAddress sourceIpAddress = TryGetRemoteIp();
            List<PersistedAuditLogEvent> entries = new List<PersistedAuditLogEvent>();
            buildersWithIds.ForEach(builderWithId =>
            {
                List<AuditLog.EventId> relatedIds = eventIds.FindAll(x => x != builderWithId.id).ToList();
                builderWithId.builder.Payload.SetRelatedEventIds(relatedIds);
                PersistedAuditLogEvent entry = new PersistedAuditLogEvent(builderWithId.id, source, builderWithId.builder.Target, sourceIpAddress, builderWithId.builder.Payload);
                entries.Add(entry);
                LogEventToConsole(builderWithId.builder.Target, builderWithId.builder.Payload, builderWithId.id);
            });

            // Write to DB
            await MetaDatabaseBase.Get().MultiInsertOrIgnoreAsync(entries);
        }


        /// <summary>
        /// Log a parent audit log event with a one-to-many relationship to children events
        /// </summary>
        /// <param name="parentBuilder"></param>
        /// <param name="childBuilders"></param>
        /// <returns></returns>
        protected async Task WriteParentWithChildrenAuditLogEventsAsync(EventBuilder parentBuilder, List<EventBuilder> childBuilders)
        {
            MetaTime timeNow = MetaTime.Now;

            // Give each child event and id
            List<(EventBuilder builder, AuditLog.EventId id)> childBuildersWitIds = new List<(EventBuilder, AuditLog.EventId)>();
            List<AuditLog.EventId> childEventIds = new List<AuditLog.EventId>();
            childBuilders.ForEach(builder =>
            {
                AuditLog.EventId eventId = AuditLog.EventId.FromTime(timeNow);
                childBuildersWitIds.Add((builder, eventId));
                childEventIds.Add(eventId);
            });

            EventSource source = EventSource.FromAdminApi(GetUserId());
            List<PersistedAuditLogEvent> entries = new List<PersistedAuditLogEvent>();

            // Create parent persisted audit log events
            IPAddress sourceIpAddress = TryGetRemoteIp();
            AuditLog.EventId parentEventId = AuditLog.EventId.FromTime(timeNow);
            parentBuilder.Payload.SetRelatedEventIds(childEventIds);
            PersistedAuditLogEvent parentEntry = new PersistedAuditLogEvent(parentEventId, source, parentBuilder.Target, sourceIpAddress, parentBuilder.Payload);
            entries.Add(parentEntry);
            LogEventToConsole(parentBuilder.Target, parentBuilder.Payload, parentEventId);

            // Create children persisted audit log events
            List<AuditLog.EventId> parentIdAsList = new List<AuditLog.EventId> { parentEventId };
            childBuildersWitIds.ForEach(builderWithId =>
            {
                builderWithId.builder.Payload.SetRelatedEventIds(parentIdAsList);
                PersistedAuditLogEvent entry = new PersistedAuditLogEvent(builderWithId.id, source, builderWithId.builder.Target, sourceIpAddress, builderWithId.builder.Payload);
                entries.Add(entry);
                LogEventToConsole(builderWithId.builder.Target, builderWithId.builder.Payload, builderWithId.id);
            });

            // Write to DB
            await MetaDatabaseBase.Get().MultiInsertOrIgnoreAsync(entries);
        }

        private void LogEventToConsole(EventTarget target, EventPayloadBase payload, Server.AdminApi.AuditLog.EventId id)
        {
            _logger.LogInformation("Audit Log event: {Target} {EventClass} {EventId}", target, payload.GetType().FullName, id);
        }

        #endregion // Auditlog
    }
}
