// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Cloud;
using Metaplay.Cloud.Application;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Cloud.Sharding;
using Metaplay.Cloud.Utility;
using Metaplay.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static System.FormattableString;

namespace Metaplay.Server.AdminApi
{
    // AdminApiActor

    [EntityConfig]
    public class AdminApiConfig : EphemeralEntityConfig
    {
        public override EntityKind          EntityKind              => EntityKindCloudCore.AdminApi;
        public override Type                EntityActorType         => typeof(AdminApiActor);
        public override IShardingStrategy   ShardingStrategy        => new ServiceShardingStrategy(EntityKindCloudCore.AdminApi);
        public override TimeSpan            ShardShutdownTimeout    => TimeSpan.FromSeconds(30);
    }

    public class AdminApiActor : EphemeralEntityActor
    {
        public class ForwardAskToEntity
        {
            public EntityId     EntityId    { get; private set; }
            public MetaMessage  Message     { get; private set; }

            ForwardAskToEntity(EntityId entityId, MetaMessage message) { EntityId = entityId; Message = message; }

            public static async Task<TResult> ExecuteAsync<TResult>(IActorRef hostActor, EntityId entityId, MetaMessage message)
            {
                object response = await hostActor.Ask(new ForwardAskToEntity(entityId, message), TimeSpan.FromSeconds(10));
                if (response is TResult result)
                    return result;
                else if (response is Exception ex)
                    throw ex;
                else
                    throw new InvalidOperationException($"Invalid response type for EntityAskAsync<{typeof(TResult).ToGenericTypeString()}>({message.GetType().ToGenericTypeString()}) to {entityId}: got {response.GetType().ToGenericTypeString()} (expecting {typeof(TResult).ToGenericTypeString()})");
            }

            public void HandleReceive(IActorRef sender, IMetaLogger log, IEntityAsker asker)
            {
                _ = asker.EntityAskAsync<MetaMessage>(EntityId, Message)
                    .ContinueWith(response =>
                    {
                        if (response.IsFaulted)
                        {
                            // \note This flattening code is duplicated in EntityActor.EntityAskAsync
                            AggregateException flattened = response.Exception.Flatten();
                            Exception effectiveException = flattened.InnerException is EntityAskExceptionBase askException ? askException : flattened;
                            sender.Tell(effectiveException);
                        }
                        else if (response.IsCompletedSuccessfully)
                            sender.Tell(response.GetCompletedResult());
                        else if (response.IsCanceled)
                            sender.Tell(new OperationCanceledException($"EntityAsk<{Message.GetType().ToGenericTypeString()}> was canceled"));
                        else
                        {
                            log.Error("Invalid response status to EntityAsk<{RequestType}>: {TaskStatus}", Message.GetType().ToGenericTypeString(), response.Status);
                            sender.Tell(new InvalidOperationException($"EntityAsk Task faulted with status {response.Status}"));
                        }
                    }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }

        WebApplication _webApp;
        IHost _cdnEmulatorHost;

        protected override AutoShutdownPolicy ShutdownPolicy => AutoShutdownPolicy.ShutdownNever();

        public AdminApiActor(EntityId entityId) : base(entityId)
        {
        }

        protected override async Task Initialize()
        {
            _webApp = CreateWebServerApp();
            await _webApp.StartAsync();

            // Start CDN emulator server (only when running locally)
            // Note: Requires BlobStorageBackend == Disk (otherwise, S3/CloudFront should be used)
            CdnEmulatorOptions cdnOpts = RuntimeOptionsRegistry.Instance.GetCurrent<CdnEmulatorOptions>();
            if (cdnOpts.Enable)
            {
                _cdnEmulatorHost = CreateCdnEmulatorHostBuilder(cdnOpts).Build();
                await _cdnEmulatorHost.StartAsync();
            }
        }

        protected override async Task OnShutdown()
        {
            _log.Info("Stopping AdminApi HTTP server");

            // Stop the host
            TimeSpan timeoutUntilGracefulShutdownBecomesForceShutdown = TimeSpan.FromSeconds(2);
            await _webApp.StopAsync(timeoutUntilGracefulShutdownBecomesForceShutdown);
            await _webApp.DisposeAsync();
            _webApp = null;

            // Stop CdnEmulator
            if (_cdnEmulatorHost != null)
            {
                await _cdnEmulatorHost.StopAsync(timeout: TimeSpan.FromSeconds(2));
                _cdnEmulatorHost.Dispose();
                _cdnEmulatorHost = null;
            }
        }

        protected override void PostStop()
        {
            // Abnormal termination. Stop the host synchronously
            if (_webApp != null)
            {
                _log.Info("Stopping AdminApi HTTP server immediately");
                _webApp.StopAsync(timeout: TimeSpan.Zero).ConfigureAwait(false).GetAwaiter().GetResult();
                _webApp.DisposeAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                _webApp = null;
            }

            // Stop CdnEmulator synchronously.
            if (_cdnEmulatorHost != null)
            {
                _log.Info("Stopping CDN emulator HTTP server immediately");
                _cdnEmulatorHost.StopAsync(timeout: TimeSpan.Zero).ConfigureAwait(false).GetAwaiter().GetResult();
                _cdnEmulatorHost.Dispose();
                _cdnEmulatorHost = null;
            }

            base.PostStop();
        }

        protected override void RegisterHandlers()
        {
            Receive<ForwardAskToEntity>(ReceiveForwardAskToEntity);

            base.RegisterHandlers();
        }

        void ReceiveForwardAskToEntity(ForwardAskToEntity request)
        {
            request.HandleReceive(Sender, _log, this);
        }

        public WebApplication CreateWebServerApp()
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder();

            builder.AddAndConfigureResponseCompression();

            builder.Host.UseSerilog();

            // Register self for dependency injection
            builder.Services.AddSingleton(_self);
            builder.Services.AddSingleton<IHostLifetime>(new SimpleHostLifetime());

            // Figure out all registered authentication domains
            AuthenticationDomainConfig[] authDomains = IntegrationRegistry.CreateAll<AuthenticationDomainConfig>().ToArray();
            AdminApiStartup.ConfigureServices(builder.Services, authDomains);

            // Build
            WebApplication webApp = builder.Build();
            webApp.UseResponseCompression();

            // Configure listen host/port
            // \note only using HTTP as HTTPS is terminated in the load balancer
            AdminApiOptions adminApiOptions = RuntimeOptionsRegistry.Instance.GetCurrent<AdminApiOptions>();
            string urls = Invariant($"http://{adminApiOptions.ListenHost}:{adminApiOptions.ListenPort}");
            _log.Info($"Binding AdminApi to listen on {urls}");
            webApp.Urls.Add(urls);

            // Configure app
            AdminApiStartup.Configure(webApp, _log, authDomains);

            return webApp;
        }

        public IHostBuilder CreateCdnEmulatorHostBuilder(CdnEmulatorOptions opts)
        {
            return Host.CreateDefaultBuilder()
                .UseSerilog()
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<CdnEmulatorStartup>();

                    // Configure listen host/port
                    string urls = Invariant($"http://{opts.ListenHost}:{opts.ListenPort}");
                    _log.Info($"Binding CDN emulator to listen on {urls}");
                    webBuilder.UseUrls(urls);
                })
                .ConfigureServices(services =>
                {
                    services.AddSingleton<IHostLifetime>(new SimpleHostLifetime());
                });
        }
    }
}
