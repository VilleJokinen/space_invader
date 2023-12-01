// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Metaplay.Server.UdpPassthrough
{
    /// <summary>
    /// Base class for custom UDP services utilizing the UDP Passthough. Your implementation of this class
    /// will be spawned for each defined UDP passthrough port defined in the backend configuration and is
    /// responsible for opening the UDP socket and running your custom UDP service on that socket.
    ///
    /// For more information, see UDP Passthrough documentation.
    /// </summary>
    public abstract class UdpPassthroughHostActorBase : EphemeralEntityActor, IMetaIntegration<UdpPassthroughHostActorBase>
    {
        protected sealed override AutoShutdownPolicy ShutdownPolicy => AutoShutdownPolicy.ShutdownNever();

        CancellationTokenSource _cts;
#pragma warning disable IDE0052
        Task _serveTask;
#pragma warning restore IDE0052

        protected UdpPassthroughHostActorBase(EntityId entityId) : base(entityId)
        {
        }

        protected override async Task Initialize()
        {
            UdpPassthroughOptions options = RuntimeOptionsRegistry.Instance.GetCurrent<UdpPassthroughOptions>();

            // Start UDP server. If setup fails, actor init fails.
            await InitializeSocketAsync(options.LocalServerPort);

            // Setup succeeded, start serving on the socket.
            CancellationTokenSource cts = new CancellationTokenSource();
            CancellationToken ct = cts.Token;
            Task serverTask = ServeAsync(ct);

            // This server now has the UDP socket running. Inform Kubernetes by setting up Health Check tcp listener. Infrastructure
            // approximates the availability of the UDP socket by observing the TCP socket.
            TcpListener healthCheckListener;
            try
            {
                healthCheckListener = TcpListener.Create(options.LocalServerPort);
                healthCheckListener.Start();
            }
            catch (Exception ex)
            {
                // Abort UDP task.
                cts.Cancel();

                // Forward
                throw new InvalidOperationException("Failed to setup UDP passthrough health check TCP listener", innerException: ex);
            }

            // Serve health check requests
            _ = Task.Run(async () =>
            {
                for (;;)
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        TcpClient client = await healthCheckListener.AcceptTcpClientAsync(ct);
                        client.Close();
                    }
                    catch
                    {
                    }
                }
            });

            // Log any future crashes, and kill the host with them
            serverTask = serverTask.ContinueWith(task =>
            {
                // Errors after CT is triggered are most often just cancellations and such. Only warn about them.
                // In that case, there's no need to kill the actor, as the actor is already being killed.
                if (ct.IsCancellationRequested)
                {
                    _log.Warning("UDP passthrough server exception during shutdown: {Error}", task.Exception);
                    return;
                }

                if (task.Status == TaskStatus.Faulted)
                {
                    _log.Error("UDP passthrough server crashed: {Error}", task.Exception);
                    _self.Tell(Kill.Instance);
                }
                else if (task.Status == TaskStatus.RanToCompletion)
                {
                    _log.Error("UDP passthrough server run to completion before CT was triggered. Restarting.");
                    _self.Tell(Kill.Instance);
                }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

            // Done
            _cts = cts;
            _serveTask = serverTask;
        }

        protected override Task OnShutdown()
        {
            StopServer();
            return _serveTask;
        }

        protected override void PostStop()
        {
            StopServer();
            base.PostStop();
        }

        /// <summary>
        /// Requests the task stated in <see cref="ServeAsync"/> to stop by triggering the cancellation token.
        /// </summary>
        protected void StopServer()
        {
            _cts?.Cancel();
        }

        /// <summary>
        /// Implementation should initialize UDP server on <paramref name="port"/> and fail if any initialization error
        /// occurs.
        /// </summary>
        protected abstract Task InitializeSocketAsync(int port);

        /// <summary>
        /// Implementation should start serving on the UDP socket initialized earlier at <see cref="InitializeSocketAsync(int)"/>.
        /// This task should not complete unless there is unrecoverable error, or <paramref name="ct"/> is triggered.
        /// </summary>
        protected abstract Task ServeAsync(CancellationToken ct);
    }
}
