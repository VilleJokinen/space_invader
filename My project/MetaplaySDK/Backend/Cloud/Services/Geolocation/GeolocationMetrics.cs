// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using System;
using System.Threading.Tasks;

namespace Metaplay.Cloud.Services.Geolocation
{
    /// <summary>
    /// Periodically reports certain geolocation-related metrics
    /// </summary>
    internal class GeolocationMetricsReporter
    {
        static Prometheus.Gauge c_residentDatabaseAge = Prometheus.Metrics.CreateGauge("game_geolocation_database_age", "Age of currently-resident geolocation database (in seconds)");

        GeolocationResidentStorage _residentStorage;

        public GeolocationMetricsReporter(GeolocationResidentStorage residentStorage)
        {
            _residentStorage = residentStorage ?? throw new ArgumentNullException(nameof(residentStorage));

            // \note No cancellation implemented for now. After initialization, geolocation system remains active until app shuts down.
            // \todo [nuutti] Could implement cancellation anyway. Would be more proper in situations
            //                where a static Geolocation singleton isn't desired, like maybe in tests.
            _ = Task.Run(MainLoopAsync);
        }

        static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(10);

        async Task MainLoopAsync()
        {
            while (true)
            {
                try
                {
                    ReportMetrics();
                }
                catch (Exception)
                {
                }

                await Task.Delay(TickInterval).ConfigureAwait(false);
            }
        }

        void ReportMetrics()
        {
            MetaTime                        currentTime             = MetaTime.Now;
            GeolocationDatabaseMetadata?    residentMetadataMaybe   = _residentStorage.ResidentDatabaseMaybe?.Metadata;

            if (residentMetadataMaybe.HasValue)
            {
                GeolocationDatabaseMetadata residentMetadata = residentMetadataMaybe.Value;
                c_residentDatabaseAge.Set((currentTime - residentMetadata.BuildDate).ToSecondsDouble());
            }
        }
    }
}
