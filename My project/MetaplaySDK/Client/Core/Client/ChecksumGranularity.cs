// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

namespace Metaplay.Core.Client
{
    /// <summary>
    /// Determines the granularity of checksum computation, and lowers the quality of checksum
    /// mismatch report. Any level other than PerOperation cannot resolve the exact failure point.
    /// </summary>
    public enum ChecksumGranularity
    {
        /// <summary>
        /// Compute checksums for each operation applied to the model.
        /// </summary>
        PerOperation,

        /// <summary>
        /// Compute checksum for each flushed batch.
        /// </summary>
        PerBatch,

        /// <summary>
        /// Compute checksum for each action, but only for the last Tick of the frame. However, if a single frame
        /// is too large for a single batch, the last tick of each batch is checksummed.
        /// </summary>
        PerActionSingleTickPerFrame,
    };
}
