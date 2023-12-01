// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.RuntimeOptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Metaplay.Server.AdminApi
{
    [MetaplayMiddleware(MetaplayMiddlewareAttribute.RegisterPhase.Early)]
    public class RequestDurationMiddleware
    {
        private readonly RequestDelegate                    _next;
        private readonly ILogger<RequestDurationMiddleware> _logger;

        public RequestDurationMiddleware(RequestDelegate next, ILogger<RequestDurationMiddleware> logger)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task Invoke(HttpContext context)
        {
            Stopwatch sw = Stopwatch.StartNew();
            string originalPath = null;
            string originalQueryString = null;
            try
            {
                // Store the original paths. Other middlewares, such as the 404 handler, may modify this.
                originalPath = context.Request.Path;
                originalQueryString = context.Request.QueryString.ToString();

                await _next.Invoke(context);
            }
            finally
            {
                // Don't care about OPTIONS
                if (context.Request.Method != "OPTIONS")
                {
                    TimeSpan                elapsed             = sw.Elapsed;
                    AdminApiOptions         opts                = RuntimeOptionsRegistry.Instance.GetCurrent<AdminApiOptions>();
                    bool                    didTakeLongTime     = elapsed.TotalMilliseconds >= opts.LongRequestThreshold;
                    bool                    unexpectedLongTime  = didTakeLongTime && !context.Items.ContainsKey("_MetaplayLongRunningQuery");
                    if (opts.LogAllRequests || unexpectedLongTime)
                    {
                        // \note These can be used the get the name of the ASP.NET controller and action, but they're more useful for metrics
                        //object action = context.Request.RouteValues["action"];
                        //object controller = context.Request.RouteValues["controller"];
                        string userName = MetaplayAdminApiController.GetUserId(context.User);
                        _logger.Log(
                            unexpectedLongTime ? LogLevel.Warning : LogLevel.Information,
                            "Request {RequestMethod} {RequestPath}{RequestQuery} from {UserName} with status {ResponseStatus} processed in {RequestDuration}ms",
                            context.Request.Method, originalPath, originalQueryString, userName, context.Response.StatusCode, elapsed.TotalMilliseconds);
                    }
                }
            }
        }
    }
}
