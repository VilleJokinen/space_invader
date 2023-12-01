// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Cluster;
using Metaplay.Cloud.Utility;
using Metaplay.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using System;
using System.IO.Compression;
using System.Threading.Tasks;
using static System.FormattableString;

namespace Metaplay.Cloud.Application
{
    /// <summary>
    /// System HTTP server, used to communicate with the infrastructure. Supports health checks, readiness
    /// checks, and request to gracefully shutdown the cluster.
    /// </summary>
    public class MetaplaySystemHttpServer
    {
        static readonly Serilog.ILogger _log = Log.ForContext<MetaplaySystemHttpServer>();

        public static async Task<WebApplication> StartAsync(EnvironmentOptions envOpts)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(Array.Empty<string>());

            // Don't capture Ctrl-C
            builder.Services.AddSingleton<IHostLifetime>(new SimpleHostLifetime());

            // Use Serilog for logging
            builder.Logging.ClearProviders();
            builder.Logging.AddSerilog();

            builder.AddAndConfigureResponseCompression();

            WebApplication app = builder.Build();

            app.UseResponseCompression();

            // Configure listen host & port
            app.Urls.Add(Invariant($"http://{envOpts.SystemHttpListenHost}:{envOpts.SystemHttpPort}"));

            // Root endpoint
            app.MapGet("/", () => "Ok, but nothing here!");

            // Basic health check (always respond with healthy status)
            app.MapGet("/healthz", () =>
            {
                _log.Debug("Responding to health check with OK (StatusCode=200)");
                return "Node is healthy!";
            });

            // Check whether node is ready (cluster services are initialized -- we're ready to receive traffic)
            app.MapGet("/isReady", () =>
            {
                if (ClusterCoordinatorActor.IsReady())
                    return Results.Content("Node is ready!");
                else
                    return Results.Problem("Node is not ready!", statusCode: 500);
            });

            // Request cluster to shut down gracefully
            app.MapPost("/gracefulShutdown", () =>
            {
                _log.Information("Graceful cluster shutdown requested");
                ClusterCoordinatorActor.RequestClusterShutdown();
                return "Cluster graceful shutdown sequence initiated!";
            });

            // Warn about accidentally using GET /gracefulShutdown (instead of POST)
            app.MapGet("/gracefulShutdown", () =>
            {
                _log.Error("Received GET request to /gracefulShutdown -- must use POST instead!");
                return "Received GET request to /gracefulShutdown -- must use POST instead!";
            });

            // Return 404 for all unknown paths
            app.MapFallback(async ctx =>
            {
                string sanitizedPath = Util.SanitizePathForDisplay(ctx.Request.Path);
                _log.Warning("Request to invalid path: {SystemHttpPath}", sanitizedPath);
                ctx.Response.StatusCode = 404;
                await ctx.Response.WriteAsync($"Error: Invalid path '{ctx.Request.Path}'");
            });

            // Start the application
            await app.StartAsync();

            return app;
        }
    }
}
