// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Server.AdminApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebSockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net.WebSockets;
using System.Threading.Tasks;

namespace Metaplay.Server.WebSockets
{
    public class WebSocketStartup
    {
        public WebSocketStartup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILogger<WebSocketStartup> logger)
        {
            app.UseWebSockets();
            app.UseMiddleware<WebSocketHandlerMiddleware>();
        }
    }

    public class WebSocketHandlerMiddleware
    {
        readonly ILogger<WebSocketHandlerMiddleware> _logger;
        readonly IActorRef                           _entityShard;
        readonly RequestDelegate                     _next;

        public WebSocketHandlerMiddleware(ILogger<WebSocketHandlerMiddleware> logger, IActorRef entityShard, RequestDelegate next)
        {
            _logger      = logger;
            _entityShard = entityShard;
            _next        = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (context.Request.Path == "/ws")
            {
                if (context.WebSockets.IsWebSocketRequest)
                {
                    using WebSocket      webSocket = await context.WebSockets.AcceptWebSocketAsync();
                    TaskCompletionSource tcs       = new TaskCompletionSource();

                    _logger.LogDebug("WebSocket connection accepted.");

                    _entityShard.Tell(new WebSocketConnected(
                        webSocket,
                        tcs,
                        context.Connection.LocalIpAddress,
                        context.Connection.RemoteIpAddress));

                    // Keep Middleware alive until WebSocket is disconnected
                    await tcs.Task;
                }
                else
                {
                    _logger.LogDebug("Received a non-websocket request in WebSocket endpoint.");
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                }
            }
            else
                await _next(context);
        }
    }
}

