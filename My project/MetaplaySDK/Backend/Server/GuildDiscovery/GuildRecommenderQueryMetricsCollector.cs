// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if !METAPLAY_DISABLE_GUILDS

using System.Diagnostics;

namespace Metaplay.Server.GuildDiscovery
{
    /// <summary>
    /// Collects query metrics from requester's vantage point.
    /// </summary>
    public struct GuildRecommenderQueryMetricsCollector
    {
        static readonly Prometheus.Counter      c_discoveryReqs         = Prometheus.Metrics.CreateCounter("game_guild_recommender_requests_total", "Total amount of Guild Recommendation requests made");
        static readonly Prometheus.Counter      c_discoveryReqErrors    = Prometheus.Metrics.CreateCounter("game_guild_recommender_request_errors_total", "Total amount of errors during Guild Recommendation requests");
        static readonly Prometheus.Histogram    c_discoveryDuration     = Prometheus.Metrics.CreateHistogram("game_guild_recommender_duration", "Guild Recommendation duration", Metaplay.Cloud.Metrics.Defaults.LatencyDurationConfig);

        bool        _running;
        Stopwatch   _sw;

        public static GuildRecommenderQueryMetricsCollector Begin()
        {
            c_discoveryReqs.Inc();

            GuildRecommenderQueryMetricsCollector collector = default;
            collector._running = true;
            collector._sw = Stopwatch.StartNew();
            return collector;
        }

        public void OnSuccess()
        {
            if (!_running)
                return;
            _running = false;

            c_discoveryDuration.Observe(_sw.Elapsed.TotalSeconds);
        }

        public void OnTimeout()
        {
            if (!_running)
                return;
            _running = false;

            c_discoveryReqErrors.Inc();
        }
    }
}

#endif
