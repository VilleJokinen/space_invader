// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Cloud.Services;
using Metaplay.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.IO;

namespace Metaplay.Server.AdminApi
{
    [RuntimeOptions("CdnEmulator", isStatic: true, "Configuration options for CDN emulator HTTP server.")]
    public class CdnEmulatorOptions : RuntimeOptionsBase
    {
        [MetaDescription("Enable the CDN emulator HTTP server? Should only be used on locally running game servers")]
        public bool Enable { get; set; } = IsServerApplication && IsLocalEnvironment;

        [MetaDescription("Host/interface that the CDN emulator listens on. Setting 0.0.0.0 listens on all IPv4 interfaces, 'localhost' only allows local connections.")]
        public string ListenHost { get; set; } = "0.0.0.0";

        [MetaDescription("Port on which the CDN emulator should listen for connections")]
        public int ListenPort { get; set; } = 5552;
    }

    public class CdnEmulatorStartup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddCors(
                options =>
                {
                    options.AddPolicy(
                        name: "AllowAll",
                        policy =>
                        {
                            policy.AllowAnyOrigin()
                                .AllowAnyHeader();
                        });
                });
        }
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILogger<CdnEmulatorStartup> logger)
        {
            if (env.IsDevelopment())
                app.UseDeveloperExceptionPage();
            else
                app.UseHsts(); // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.

            // Server the public directory as static files
            BlobStorageOptions opts = RuntimeOptionsRegistry.Instance.GetCurrent<BlobStorageOptions>();
            if (opts.Backend != BlobStorageBackend.Disk)
                throw new InvalidOperationException("CDN emulator only works with BlobStorageBackend.Disk");
            string publicDirPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), opts.DiskPublicPath));
            logger.LogInformation("Serving CDN emulator files from '{publicDirPath}'", publicDirPath);

            app.UseCors("AllowAll");

            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider            = new PhysicalFileProvider(publicDirPath),
                RequestPath             = new PathString(""),
                ServeUnknownFileTypes   = true,
            });
        }
    }
}
