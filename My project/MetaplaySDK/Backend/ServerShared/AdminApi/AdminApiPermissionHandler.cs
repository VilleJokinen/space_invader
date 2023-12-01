// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using Metaplay.Server.AdminApi.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;

// \note Inject this into the controllers namespace so users don't need additional using statements
namespace Metaplay.Server.AdminApi.Controllers
{
    /// <summary>
    /// Mark an HTTP endpoint to require a specific AdminApi permission. Alternatively, you can use <code>[AllowAnonymous]</code>
    /// to specify that no specific permission is required (though access in general to the dashboard is still required).
    /// See <see cref="MetaplayPermissions"/> for any built-in permissions, or define your own, typically in <code>GamePermissions</code>.
    /// </summary>
    public class RequirePermissionAttribute : TypeFilterAttribute
    {
        public readonly string PermissionName;

        public RequirePermissionAttribute(string permissionName) : base(typeof(RequirePermissionFilter))
        {
            PermissionName = permissionName;

            Arguments = new[] { new AdminApiPermissionRequirement(permissionName) };
            Order = int.MinValue;
        }
    }
}

namespace Metaplay.Server.AdminApi
{
    /// <summary>
    /// Convert <see cref="RequirePermissionAttribute"/> to <see cref="AdminApiPermissionRequirement"/> so that they can be
    /// checked by <see cref="AdminApiPermissionHandler"/>.
    /// </summary>
    public class RequirePermissionFilter : Attribute, IAsyncAuthorizationFilter
    {
        private readonly IAuthorizationService _authService;
        private readonly AdminApiPermissionRequirement _requirement;

        public RequirePermissionFilter(IAuthorizationService authService, AdminApiPermissionRequirement requirement)
        {
            _authService = authService;
            _requirement = requirement;
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            AuthorizationResult result = await _authService.AuthorizeAsync(context.HttpContext.User, null, _requirement);
            if (!result.Succeeded)
                context.Result = new ForbidResult();
        }
    }

    /// <summary>
    /// The <see cref="RequirePermissionAttribute"/> are converted to this type using the <see cref="RequirePermissionFilter"/>
    /// for validation using <see cref="AdminApiPermissionHandler"/>.
    /// </summary>
    public class AdminApiPermissionRequirement : IAuthorizationRequirement
    {
        public string Permission { get; } // Required permission for the endpoint

        public AdminApiPermissionRequirement(string permission)
        {
            Permission = permission ?? throw new ArgumentNullException(nameof(permission));
        }
    }

    /// <summary>
    /// Handler for validating that invocations to AdminApi endpoints are done with the required role(s) as defined by
    /// <see cref="RequirePermissionAttribute"/> (subsequently converted to <see cref="AdminApiPermissionRequirement"/>.
    /// </summary>
    public class AdminApiPermissionHandler : AuthorizationHandler<AdminApiPermissionRequirement>
    {
        private readonly ILogger            _logger;
        private readonly AuthenticationType _authType;
        private readonly string             _issuer;        // Required issuer for the role to be valid
        private readonly string             _rolePrefix;    // Required prefix for the role to be valid

        public AdminApiPermissionHandler(ILogger<AdminApiPermissionHandler> logger)
        {
            _logger = logger;

            // Cache authentication type to avoid changes due to hot-loading
            AdminApiOptions adminApiOpts = RuntimeOptionsRegistry.Instance.GetCurrent<AdminApiOptions>();
            _authType = adminApiOpts.Type;

            // Only allow claims from the expected issuer
            _issuer = adminApiOpts.GetAdminApiDomain();
            _rolePrefix = adminApiOpts.GetRolePrefix();
            if (_authType == AuthenticationType.None)
            {
                MetaDebug.Assert(_issuer == null, "Must have Issuer==null when AdminApi AuthenticationType==None");
                MetaDebug.Assert(_rolePrefix == "", "Must have RolePrefix==\"\" when AdminApi AuthenticationType==None");
            }
            else
            {
                MetaDebug.Assert(!string.IsNullOrEmpty(_issuer), "Must have a valid Issuer when AdminApi authentication is enabled");
                MetaDebug.Assert(!string.IsNullOrEmpty(_rolePrefix), "Must have valid RolePrefix when AdminApi authentication is enabled");
            }
        }

        /// <summary>
        /// This handler validates whether the requester is allowed to access the given endpoint (ie, satisfies the permission requirement):
        /// - Resolve the valid role claims for this environment from the identity in the ASP.NET request context
        /// - Check whether the specified permission requirement is satisfied by any of the valid roles associated with the identity
        /// </summary>
        /// <param name="context">The HTTP request context</param>
        /// <param name="requirement">The permission requirement for the endpoint being accessed</param>
        /// <returns></returns>
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AdminApiPermissionRequirement requirement)
        {
            // Get all valid roles for the user
            string[] roles = context.User
                .FindAll(claim => _issuer == null || claim.Issuer == _issuer)               // Find all claims with matching issuer (or all claim if no issuer specified)
                .Where(claim => claim.Type == "https://schemas.metaplay.io/roles")          // Select all Metaplay role claims
                .Select(claim => claim.Value)                                               // Grab values (names of roles)
                .Where(value => value.StartsWith(_rolePrefix, StringComparison.Ordinal))    // Only choose roles starting with this environment's prefix (eg, 'dashboard-idler-develop.p1.metaplay.io.')
                .Select(value => value.Substring(_rolePrefix.Length))                       // Drop the environment name to get the pure role names of the role (eg, 'game-admin')
                .ToArray();

            // Check if any of the roles are allowed to access the given permission
            if (MetaplayAdminApiController.HasPermissionForRoles(roles, requirement.Permission))
                context.Succeed(requirement);
            else
            {
                _logger.LogWarning("Requirement check for permission {Permission} could not be satisfied by roles: {Roles}", requirement.Permission, string.Join(", ", roles));
                context.Fail(new MissingPermissionFailureReason(this, requirement.Permission));
            }

            return Task.CompletedTask;
        }
    }
}
