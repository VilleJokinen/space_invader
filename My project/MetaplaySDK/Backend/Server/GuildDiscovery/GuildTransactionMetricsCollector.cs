// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if !METAPLAY_DISABLE_GUILDS

using System;
using System.Diagnostics;

namespace Metaplay.Server.GuildDiscovery
{
    /// <summary>
    /// Collects transaction metrics from session point of view.
    /// </summary>
    public struct GuildTransactionMetricsCollector : IDisposable
    {
        static readonly Prometheus.Counter      c_transactions          = Prometheus.Metrics.CreateCounter("game_guild_transactions_total", "Total amount of Guild Transactions attempted", "type");
        static readonly Prometheus.Counter      c_transactionCancels    = Prometheus.Metrics.CreateCounter("game_guild_transaction_cancels_total", "Total amount of Guild Transaction attempts that ended up in cancel, by source", "source");
        static readonly Prometheus.Counter      c_transactionErrors     = Prometheus.Metrics.CreateCounter("game_guild_transaction_errors_total", "Total amount of Guild Transaction attempts that ended up in error, by error source", "source");
        static readonly Prometheus.Histogram    c_transactionDuration   = Prometheus.Metrics.CreateHistogram("game_guild_transaction_duration", "Guild Transaction duration, as measured from Session", Metaplay.Cloud.Metrics.Defaults.LatencyDurationConfig);

        bool        _running;
        Stopwatch   _sw;

        public static GuildTransactionMetricsCollector Begin(string transactionName)
        {
            // Remove dots, replace with _. Remove implicit "Transaction" suffix
            string labelName = transactionName.Replace('.', '_');
            if (labelName.EndsWith("_Transaction", StringComparison.Ordinal))
                labelName = labelName.Substring(0, labelName.Length - "_Transaction".Length);
            c_transactions.WithLabels(labelName).Inc();

            GuildTransactionMetricsCollector collector = default;
            collector._running = true;
            collector._sw = Stopwatch.StartNew();
            return collector;
        }

        public void OnPlayerError()
        {
            if (TryEnd())
                c_transactionErrors.WithLabels("player").Inc();
        }

        public void OnPlayerCancel(bool wasClientForcedCancel)
        {
            if (TryEnd())
            {
                string label = wasClientForcedCancel ? "client" : "player";
                c_transactionCancels.WithLabels(label).Inc();
            }
        }

        public void OnGuildError()
        {
            if (TryEnd())
                c_transactionErrors.WithLabels("guild").Inc();
        }

        public void OnGuildCancel()
        {
            if (TryEnd())
                c_transactionCancels.WithLabels("guild").Inc();
        }

        public void OnComplete()
        {
            if (TryEnd())
                c_transactionDuration.Observe(_sw.Elapsed.TotalSeconds);
        }

        void IDisposable.Dispose()
        {
            if (TryEnd())
                c_transactionErrors.WithLabels("unhandled").Inc();
        }

        bool TryEnd()
        {
            if (_running)
            {
                _running = false;
                return true;
            }
            return false;
        }
    }
}

#endif
