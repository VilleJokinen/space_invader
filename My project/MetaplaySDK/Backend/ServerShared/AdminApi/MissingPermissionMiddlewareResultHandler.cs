// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Metaplay.Server.AdminApi
{
    class MissingPermissionFailureReason : AuthorizationFailureReason
    {
        public string Permission { get; private set; }

        public MissingPermissionFailureReason(IAuthorizationHandler handler, string permission) : base(handler, $"missing {permission} permission")
        {
            Permission = permission;
        }
    }

    class MissingPermissionMiddlewareResultHandler : IAuthorizationMiddlewareResultHandler
    {
        readonly AuthorizationMiddlewareResultHandler _defaultHandler = new AuthorizationMiddlewareResultHandler();

        public MissingPermissionMiddlewareResultHandler()
        {
        }

        public Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
        {
            // If request failed only due to explicitly Missing Permission and nothing else, send the client the error message.
            if (authorizeResult.Forbidden && TryExtractOnlyMissingPermissionFailure(authorizeResult, out MissingPermissionFailureReason missingPermission))
                return HandleForbiddenDueToMissingPermission(context, missingPermission.Permission);

            // Otherwise, default handling.
            return _defaultHandler.HandleAsync(next, context, policy, authorizeResult);
        }

        static bool TryExtractOnlyMissingPermissionFailure(PolicyAuthorizationResult authorizeResult, out MissingPermissionFailureReason missingPermissionFailureReason)
        {
            if (authorizeResult.AuthorizationFailure == null)
                goto fail;
            if (authorizeResult.AuthorizationFailure.FailedRequirements.Any())
                goto fail;

            IEnumerator<AuthorizationFailureReason> enumerator = authorizeResult.AuthorizationFailure.FailureReasons.GetEnumerator();
            if (!enumerator.MoveNext())
                goto fail;
            AuthorizationFailureReason first = enumerator.Current;
            if (!(first is MissingPermissionFailureReason extracted))
                goto fail;
            if (enumerator.MoveNext())
                goto fail;

            missingPermissionFailureReason = extracted;
            return true;

        fail:
            missingPermissionFailureReason = null;
            return false;
        }

        struct MissingPermissionResult
        {
            public struct MissingPermissionError
            {
                [JsonProperty("statusCode")]
                public int StatusCode;

                [JsonProperty("message")]
                public string Message;

                [JsonProperty("details")]
                public string Details;
            }

            [JsonProperty("error")]
            public MissingPermissionError Error;
        }

        public static async Task HandleForbiddenDueToMissingPermission(HttpContext context, string missingPermission)
        {
            MissingPermissionResult result = new MissingPermissionResult();
            result.Error.StatusCode = (int)HttpStatusCode.Forbidden;
            result.Error.Message = "Access Forbidden";
            result.Error.Details = $"This API request requires {missingPermission} permission.";
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
            await context.Response.WriteAsync(JsonConvert.SerializeObject(result));
            await context.Response.CompleteAsync();
        }
    }
}
