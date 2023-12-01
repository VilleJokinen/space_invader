// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Cloud.RuntimeOptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;

namespace Metaplay.Server.AdminApi.Controllers
{
    /// <summary>
    /// Controller for various user related functions
    /// </summary>
    public class UserController : GameAdminApiController
    {
        public UserController(ILogger<UserController> logger, IActorRef adminApi) : base(logger, adminApi)
        {
        }


        /// <summary>
        /// Returns information about the current user
        /// Usage:  GET /api/authdetails/user
        /// </summary>
        /// <returns></returns>
        [HttpGet("authDetails/user")]
        [AllowAnonymous]
        public ActionResult GetUser()
        {
            // NB: The return values here are untrusted. If we are using authentication
            // then these could have been spoofed in the JWT - that's ok, the Dashboard
            // cannot do anything malicious with them and we don't trust them inside
            // the server code
            string[] roles       = GetUserRolesUntrusted(HttpContext);
            string[] permissions = GetPermissionsFromRoles(roles);

            return Ok(new
            {
                Roles       = roles,
                Permissions = permissions,
            });
        }


        /// <summary>
        /// Returns a list of all defined permissions and roles. Used by the Dashboard
        /// Usage:  GET /api/authdetails/allPermissionsAndRoles
        /// </summary>
        /// <returns></returns>
        [HttpGet("authDetails/allPermissionsAndRoles")]
        [AllowAnonymous]
        public ActionResult GetAllPermissionsAndRoles()
        {
            AdminApiOptions adminApiOpts = RuntimeOptionsRegistry.Instance.GetCurrent<AdminApiOptions>();
            return Ok(new
            {
                PermissionGroups = AdminApiOptions.GetAllPermissions()
                    .Select(permissionGroup => new
                        {
                            Title = permissionGroup.Title,
                            Permissions = permissionGroup.Permissions
                                .Select(permission => new
                                {
                                    Name =  permission.Name,
                                    Description = permission.Description
                                })
                        }
                    )
                    .ToList(),
                Roles = adminApiOpts.Roles.ToList()
            });
        }
    }
}
