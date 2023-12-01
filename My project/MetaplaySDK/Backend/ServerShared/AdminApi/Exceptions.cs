// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Sharding;
using Metaplay.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using System;

namespace Metaplay.Server.AdminApi.Controllers
{
    public static class Exceptions
    {
        /// <summary>
        /// Metaplay HTTP exception format. When any API endpoint code encounters an error it
        /// should always try to raise one of these exceptions. They will get handled cleanly and
        /// the information will be presented clearly to the endpoint user
        /// </summary>
        public class MetaplayHttpException : Exception
        {
            public readonly int     ExStatusCode;
            public readonly string  ExMessage;
            public readonly string  ExDetails;

            public MetaplayHttpException(int statusCode, string message, string details) : base(message)
            {
                ExStatusCode = statusCode;
                ExMessage = message;
                ExDetails = details;
            }
        }


        /// <summary>
        /// Exception filter to handle all exceptions that are thrown during Http processing. We will
        /// intercept MetaplayHttpException exceptions and use the infromation inside to create a nicely
        /// formatted JSON error message. For any any exception types (ie: anything that we didn't
        /// expect or handle correctly) we will return a 500 error in the same JSON error message format
        /// </summary>
        public class MetaplayHttpExceptionFilter : ExceptionFilterAttribute
        {
            private readonly ILogger _logger;
            public MetaplayHttpExceptionFilter(ILogger<MetaplayHttpExceptionFilter> logger)
            {
                _logger = logger;
            }


            public override void OnException(ExceptionContext context)
            {
                int statusCode;
                string message;
                string details;
                string[] stackTrace;
                string userId = MetaplayAdminApiController.GetUserId(context.HttpContext.User);

                if (context.Exception is MetaplayHttpException)
                {
                    // It was a MetaplayHttpException exception, so it has proper information about
                    // the exception that we can report nicely
                    MetaplayHttpException ex = context.Exception as MetaplayHttpException;
                    statusCode = ex.ExStatusCode;
                    message    = ex.ExMessage;
                    details    = ex.ExDetails;
                    stackTrace = ex.StackTrace?.Split(Environment.NewLine);
                    _logger.LogWarning("AdminApi exception: url=\"{Path}\", statusCode={StatusCode}, message=\"{Message}\", details=\"{Details}\", userId=\"{UserId}\"", Util.SanitizePathForDisplay(context.HttpContext.Request.Path), statusCode, message, details, userId);
                }
                else if (context.Exception is EntityShard.UnexpectedEntityAskError unexpectedEntityAsk)
                {
                    statusCode = 500;
                    message    = "Exception occured in AdminAPI.";
                    details    = unexpectedEntityAsk.HandlerErrorMessage;
                    stackTrace = unexpectedEntityAsk.HandlerStackTrace?.Split(Environment.NewLine);
                    _logger.LogWarning("AdminApi exception: url=\"{Path}\", statusCode={StatusCode}, message=\"{Message}\", userId=\"{UserId}\"", context.HttpContext.Request.Path, statusCode, message, userId);
                }
                else
                {
                    // It was "any other" exception, so we can just wrap it inside our error message
                    // format as some sort of general unhandled/unexpected error

                    Exception ex = context.Exception;
                    if (ex is AggregateException)
                    {
                        AggregateException aggregate = (ex as AggregateException).Flatten();
                        ex = aggregate.InnerExceptions.Count == 1 ? aggregate.InnerException : aggregate;
                    }

                    statusCode = 500;
                    message    = "Internal server error.";
                    details    = ex.Message;
                    stackTrace = ex.StackTrace?.Split(Environment.NewLine);
                    // \note: we omit printing the details since it is verbose and part of the exception itself which is printed too.
                    _logger.LogError("AdminApi exception: url=\"{Url}\", statusCode={StatusCode}, message=\"{Message}\", details=(omitted), userId=\"{UserId}\": {Ex}", Util.SanitizePathForDisplay(context.HttpContext.Request.Path), statusCode, message, userId, ex);
                }

                // Generate the standardised error response and set the contents to be what we
                // decided it should be above
                context.HttpContext.Response.ContentType = "application/json";
                context.HttpContext.Response.StatusCode = statusCode;
                context.Result = new JsonResult(new
                {
                    error = new
                    {
                        statusCode = statusCode,
                        message = message,
                        details = details,
                        stackTrace = stackTrace,
                    }
                }, AdminApiJsonSerialization.UntypedSerializationSettings);
            }
        }
    }
}
