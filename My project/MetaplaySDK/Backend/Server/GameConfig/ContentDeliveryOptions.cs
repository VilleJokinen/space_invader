// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;

namespace Metaplay.Server.GameConfig
{
    [RuntimeOptions("ContentDelivery", isStatic: false, "Configuration options for game config delivery via CDN.")]
    public class ContentDeliveryOptions : RuntimeOptionsBase
    {
        // Compression mode for the game config archive.
        [MetaDescription("The default compression mode for the game config archive (`None` or `Deflate`).")]
        public CompressionAlgorithm     ArchiveCompressionAlgorithm         { get; private set; }   = CompressionAlgorithm.Deflate;
        [MetaDescription("Content that is smaller than this value is not considered for compression. The value is measured in bytes.")]
        public int                      ArchiveMinimumSizeBeforeCompression { get; private set; }   = 500;
    }
}
