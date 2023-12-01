// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Util.Internal;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;

namespace Metaplay.Server.AdminApi
{
    public class AnonymousAuthenticationOptions : AuthenticationSchemeOptions
    {
    }

    /// <summary>
    /// Authentication handler for ASP.NET that allows anonymous access to all the AdminApi endpoints.
    /// Should only be used with <see cref="AuthenticationType.None"/>. This handlers supports assuming
    /// roles by specifying them with the request using the <code>Metaplay-AssumedUserRoles</code> HTTP
    /// header -- this is intended for simulating dashboard roles during development.
    /// </summary>
    public class AnonymousAuthenticationHandler : AuthenticationHandler<AnonymousAuthenticationOptions>
    {
        public AnonymousAuthenticationHandler(IOptionsMonitor<AnonymousAuthenticationOptions> options, ILoggerFactory logger, UrlEncoder encoder, ISystemClock clock)
            : base(options, logger, encoder, clock)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            // Construct an anonymous claim with all the assumed roles (defaulting to game-admin)
            // \note we allow freely assuming roles when authentication is disabled
            string[] roles = MetaplayAdminApiController.GetUserRolesUntrusted(Context);
            IEnumerable<Claim> claims =
                new Claim[] { new Claim(ClaimTypes.Name, "anonymous") }
                .Concat(roles.Select(role => new Claim("https://schemas.metaplay.io/roles", role)))
                .ToList();

            ClaimsIdentity identity = new ClaimsIdentity(claims, "NoAuth");
            ClaimsPrincipal principal = new ClaimsPrincipal(identity);
            AuthenticationTicket ticket = new AuthenticationTicket(principal, "NoAuth");

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
