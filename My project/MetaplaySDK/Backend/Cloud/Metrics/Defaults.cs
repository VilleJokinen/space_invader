// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Prometheus;

namespace Metaplay.Cloud.Metrics
{
    public static class Defaults
    {
        public static readonly double[] LatencyDurationBuckets              = new double[] { 0.001, 0.002, 0.005, 0.01, 0.02, 0.05, 0.1, 0.2, 0.5, 1.0, 2.0, 5.0, 10.0 };
        public static readonly double[] CoarseLatencyDurationBuckets        = new double[] { 0.003, 0.01, 0.03, 0.1, 0.3, 1.0, 3.0 };
        public static readonly double[] SessionDurationUntilResumeBuckets   = new double[] { 0.1, 0.2, 0.5, 1.0, 2.0, 5.0, 10.0, 20.0, 30.0, 60.0 };
        public static readonly double[] EntitySizeBuckets                   = new double[] { 100, 200, 500, 1_000, 2_000, 5_000, 10_000, 20_000, 50_000, 100_000, 200_000, 500_000, 1_000_000 };

        public static readonly HistogramConfiguration LatencyDurationConfig             = new HistogramConfiguration { Buckets = Defaults.LatencyDurationBuckets };
        public static readonly HistogramConfiguration SessionDurationUntilResumeConfig  = new HistogramConfiguration { Buckets = Defaults.SessionDurationUntilResumeBuckets };
    }
}
