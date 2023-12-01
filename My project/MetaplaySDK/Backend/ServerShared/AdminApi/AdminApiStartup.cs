// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using Metaplay.Server.Forms;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static Metaplay.Server.AdminApi.Controllers.Exceptions;

namespace Metaplay.Server.AdminApi
{
    /// <summary>
    /// A custom feature provider that filters out any controllers that are not enabled via having a
    /// <see cref="MetaplayFeatureEnabledConditionAttribute"/> that returns false for IsEnabled. Any
    /// controllers without this attribute are included normally.
    /// </summary>
    public class MetaplayEnabledControllerFeatureProvider : ControllerFeatureProvider
    {
        protected override bool IsController(TypeInfo typeInfo)
        {
            bool isController = base.IsController(typeInfo);

            if (isController && !typeInfo.IsMetaFeatureEnabled())
                isController = false;

            return isController;
        }
    }

    /// <summary>
    /// Base class for declaring new authentication domains for the web server.
    /// </summary>
    public abstract class AuthenticationDomainConfig : IMetaIntegration<AuthenticationDomainConfig>
    {
        protected IMetaLogger _log;

        protected AuthenticationDomainConfig()
        {
            _log = MetaLogger.ForContext(this.GetType());
        }

        public abstract void ConfigureServices(IServiceCollection services, AdminApiOptions opts);
        public abstract void ConfigureApp(WebApplication app, AdminApiOptions opts);

        /// <summary>
        /// Helper for registering a 404 handler for a given authentication domain. The method handles
        /// all HTTP methods (GET, POST, DELETE, PUT, PATCH) and uses the provided CORS policy so that
        /// the 404 error is shown in the browser instead of a CORS error.
        /// </summary>
        /// <param name="app"></param>
        /// <param name="pathPrefix"></param>
        /// <param name="corsPolicy"></param>
        /// <param name="handler"></param>
        protected void Register404Handler(WebApplication app, string pathPrefix, string corsPolicy, Action<HttpContext, string> handler)
        {
            _log.Information($"Mapping any unknown API endpoints under '/{pathPrefix}' to 404.");
            app.Map($"/{pathPrefix}/{{*dummyPath}}", (HttpContext ctx) =>
            {
                string sanitizedPath = Util.SanitizePathForDisplay(ctx.Request.Path);
                handler(ctx, sanitizedPath);
                ctx.Response.StatusCode = 404;
                return Task.CompletedTask;
            })
                .WithMetadata(new HttpMethodMetadata(new string[] { "GET", "POST", "DELETE", "PUT", "PATCH" }))
                .RequireCors(corsPolicy);
        }
    }

    /// <summary>
    /// Dummy handler for scheme-less Forbid() calls.
    /// </summary>
    public class DefaultForbidSchemeHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public DefaultForbidSchemeHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, ISystemClock clock) : base(options, logger, encoder, clock)
        {
        }
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => throw new NotImplementedException();
    }

    public static class AdminApiStartup
    {
        // This method gets called by the runtime. Use this method to add services to the container.
        public static void ConfigureServices(IServiceCollection services, AuthenticationDomainConfig[] authDomains)
        {
            AdminApiOptions opts = RuntimeOptionsRegistry.Instance.GetCurrent<AdminApiOptions>();
            services.AddMvcCore(options =>
                {
                    // Catch unhandled exceptions and wrap them in our own formatted error response
                    options.Filters.Add(typeof(MetaplayHttpExceptionFilter));
                })
                .AddRazorViewEngine()
                .ConfigureApplicationPartManager(
                    manager =>
                    {
                        // Replace default Controller provider with a new one.
                        manager.FeatureProviders.Remove(manager.FeatureProviders.OfType<ControllerFeatureProvider>().FirstOrDefault());
                        manager.FeatureProviders.Add(new MetaplayEnabledControllerFeatureProvider());

                        // Register all SDK core and userland assemblies as AssemblyParts in case they contain controllers
                        HashSet<Assembly> existingAssemblies = manager.ApplicationParts
                            .Where(part => part is AssemblyPart)
                            .Cast<AssemblyPart>()
                            .Select(asmPart => asmPart.Assembly)
                            .ToHashSet();
                        foreach (Assembly ownAssembly in TypeScanner.GetOwnAssemblies().Where(asm => !existingAssemblies.Contains(asm)))
                            manager.ApplicationParts.Add(new AssemblyPart(ownAssembly));
                    })
                .AddNewtonsoftJson(options =>
                {
                    AdminApiJsonSerialization.ApplySettings(options.SerializerSettings);
                })
                .AddCors();

            // Always register authentication & authorization
            services.AddAuthentication(options =>
            {
                // Forbidden() is authentication scheme specific and requires either specifying the scheme at call-site
                // or a default scheme. Set the default scheme and register a minimal handler for it.
                options.DefaultForbidScheme = "DefaultForbidScheme";
                options.AddScheme<DefaultForbidSchemeHandler>("DefaultForbidScheme", "DefaultForbidScheme");
            });
            services.AddAuthorization();

            // Configure registered authentication domains
            foreach (AuthenticationDomainConfig authDomain in authDomains)
                authDomain.ConfigureServices(services, opts);

            // Register authorization handlers/middleware
            services.AddSingleton<IAuthorizationMiddlewareResultHandler, MissingPermissionMiddlewareResultHandler>();

            // Form schema provider
            services.AddSingleton<IMetaFormSchemaProvider, MetaFormSchemaProvider>();
            services.AddSingleton<IMetaFormValidationEngine, MetaFormValidationEngine>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public static void Configure(WebApplication app, IMetaLogger logger, AuthenticationDomainConfig[] authDomains)
        {
            IEnumerable<Type> enabledControllers = TypeScanner.GetDerivedTypes<MetaplayController>().Where(type => type.IsMetaFeatureEnabled());

            AdminApiOptions opts = RuntimeOptionsRegistry.Instance.GetCurrent<AdminApiOptions>();
            CheckRouteConfiguration(enabledControllers);

            // Register early custom and sdk middlewares
            foreach (Type type in TypeScanner.GetClassesWithAttribute<MetaplayMiddlewareAttribute>())
            {
                if (type.GetCustomAttribute<MetaplayMiddlewareAttribute>().Phase == MetaplayMiddlewareAttribute.RegisterPhase.Early)
                    app.UseMiddleware(type);
            }

            if (app.Environment.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();

                // Force https when not in development mode
                // \note Not requiring this since assuming a reverse proxy terminates https in front
                //app.UseHttpsRedirection();
            }

            // Serve static files from specified path (only serve if directory actually exists, which it doesn't when running locally & dashboard hasn't been built)
            string wwwPath = Path.Combine(Directory.GetCurrentDirectory(), opts.WebRootPath);
            string fullWebRootPath = Path.GetFullPath(wwwPath);
            bool hasPrebuiltDashboard = Directory.Exists(wwwPath) && File.Exists(Path.Join(wwwPath, "index.html"));
            IFileProvider fileProvider;
            if (hasPrebuiltDashboard)
            {
                logger.Info("Pre-built dashboard found at '{WebRootPath}' -- serving it on port {ListenPort}", fullWebRootPath, opts.ListenPort);
                fileProvider = new PhysicalFileProvider(wwwPath);
            }
            else
            {
                logger.Info("Pre-built dashboard NOT found at '{WebRootPath}' -- serving an error page on port {ListenPort}", fullWebRootPath, opts.ListenPort);
                fileProvider = DashboardFallback.CreateFallbackFileProvider(fullWebRootPath);
            }

            app.UseDefaultFiles(new DefaultFilesOptions
            {
                FileProvider = fileProvider,
            });

            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider    = fileProvider,
                RequestPath     = new PathString(""),
            });

            // Enable routing
            app.UseRouting();

            // Allow using per-authentication domain CORS policies
            app.UseCors();

            // Enable auth
            app.UseAuthentication();
            app.UseAuthorization();

            // Configure authentication domains
            foreach (AuthenticationDomainConfig authDomain in authDomains)
                authDomain.ConfigureApp(app, opts);

            // Register late custom and sdk middlewares
            foreach (Type type in TypeScanner.GetClassesWithAttribute<MetaplayMiddlewareAttribute>())
            {
                if (type.GetCustomAttribute<MetaplayMiddlewareAttribute>().Phase == MetaplayMiddlewareAttribute.RegisterPhase.Late)
                    app.UseMiddleware(type);
            }

            app.MapControllers();

            // Check all routes are properly annotated
#pragma warning disable ASP0014 // ASP0014: Suggest using top level route registrations instead of UseEndpoints
            app.UseEndpoints(endpoints =>
            {
                foreach (EndpointDataSource endpointSource in endpoints.DataSources)
                {
                    foreach (Endpoint endpoint in endpointSource.Endpoints)
                    {
                        bool hasHttpMethod = endpoint.Metadata.Any(metadata => metadata is HttpMethodAttribute || metadata is HttpMethodMetadata);
                        if (!hasHttpMethod)
                            throw new InvalidOperationException($"HTTP endpoint {endpoint.DisplayName} is missing HttpMethodAttribute. If this methods is not intended to be an Http Endpoint, the method should not be public.");
                    }
                }
            });
#pragma warning restore ASP0014

            // With a SPA, we need to make all 404s fallback to serving index.html.
            // This has the side effect of also returning index.html when any unhandled
            // API endpoints routes are hit - see above for how this is addressed.
            app.MapFallbackToFile("/index.html", new StaticFileOptions
            {
                FileProvider    = fileProvider,
                RequestPath     = new PathString(""),
            });
        }

        static void CheckRouteConfiguration(IEnumerable<Type> enabledControllers)
        {
            // Check that all controllers properly inherit from one of the MetaplayXXXController classes so that
            // they get a route properly defined for them
            enabledControllers
                .Where(controller => controller.GetCustomAttribute<RouteAttribute>() == null)
                .ToList()
                .ForEach(controller =>
                {
                    throw new Exception($"Controller {controller.Name} has no route. It should inherit from one of the MetaplayXXXController classes.");
                });

            // Check that all controllers' methods that looks like route handlers are accessible
            // Except for methods tagged with the NonActionAttribute, indicating that this method is purposefully publicly accessible.
            foreach (Type controller in enabledControllers)
            {
                foreach (MethodInfo method in controller.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    bool isNonAction = method.GetCustomAttribute<NonActionAttribute>() != null;

                    if (isNonAction)
                        continue;

                    bool isHandler = method.GetCustomAttribute<HttpMethodAttribute>() != null;

                    if (isHandler && !method.IsPublic)
                        throw new Exception($"Controller {controller.ToGenericTypeString()} endpoint handler {method.Name} is not public and cannot be used. Either mark the method as public or remove handler attribute.");
                    if (isHandler && method.IsStatic)
                        throw new Exception($"Controller {controller.ToGenericTypeString()} endpoint handler {method.Name} is not static and cannot be used. Method as instance method.");

                    if (!isHandler && method.IsPublic && !method.IsStatic && !method.IsAbstract)
                        throw new Exception($"Controller {controller.ToGenericTypeString()} method is public {method.Name} but has no Http route attached to it. Mark method as protected, or attach a route.");
                }
            }
        }
    }
}
