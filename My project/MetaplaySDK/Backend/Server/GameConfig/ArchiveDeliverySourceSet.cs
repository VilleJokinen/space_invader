// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using Metaplay.Core.Message;
using System.Collections.Generic;

namespace Metaplay.Server.GameConfig
{
    /// <summary>
    /// A potential source for an CDN-delivered ConfigArchive, and the requirements
    /// for client to be able to use it.
    /// </summary>
    public class ArchiveDeliverySource
    {
        public readonly ContentHash ConfigVersion;

        public ArchiveDeliverySource(ContentHash configVersion)
        {
            ConfigVersion = configVersion;
        }

        public SessionProtocol.SessionResourceCorrection.ConfigArchiveUpdateInfo CreateConfigUpdateCorrection()
        {
            return new SessionProtocol.SessionResourceCorrection.ConfigArchiveUpdateInfo(
                sharedGameConfigVersion: ConfigVersion,
                urlSuffix: null);
        }
    }

    /// <summary>
    /// A priority-ordered set of potential Sources for a CDN-delivered ConfigArchive.
    /// </summary>
    public class ArchiveDeliverySourceSet
    {
        public readonly IEnumerable<ArchiveDeliverySource> PreferredSources;
        public readonly ArchiveDeliverySource FallbackSource;

        public ArchiveDeliverySourceSet(IEnumerable<ArchiveDeliverySource> preferredSources, ArchiveDeliverySource fallbackSource)
        {
            PreferredSources = preferredSources;
            FallbackSource = fallbackSource;
        }

        /// <summary>
        /// Returns the best Correction for a client, e.g. selects compressed version if such is available and supported.
        /// </summary>
        public SessionProtocol.SessionResourceCorrection.ConfigArchiveUpdateInfo GetCorrection(CompressionAlgorithmSet supportedArchiveCompressions)
        {
            // Choose the best method

            foreach (ArchiveDeliverySource preferredSource in PreferredSources)
            {
                // Check if we have a suitable compressed version available
                // if (preferredSource.RequiredCompression != CompressionAlgorithm.None && !supportedArchiveCompressions.Contains(preferredSource.RequiredCompression))
                //     continue;

                // \todo: check if any client-available config version can be used as a delta-source
                // if (preferredVersion.IsDelta && preferredVersion.DeltaSource != request.Source)
                //    continue;

                // Success:
                return preferredSource.CreateConfigUpdateCorrection();
            }

            // Preferred version are not suitable. Use fallback.

            return FallbackSource.CreateConfigUpdateCorrection();
        }

        /// <summary>
        /// Returns a Correction using fallback resource for any CDN-delivered ConfigArchive.
        /// </summary>
        public static SessionProtocol.SessionResourceCorrection.ConfigArchiveUpdateInfo GetFallbackCorrectionForConfig(ContentHash configVersion)
        {
            return new SessionProtocol.SessionResourceCorrection.ConfigArchiveUpdateInfo(
                sharedGameConfigVersion: configVersion,
                urlSuffix: null);
        }
    }
}
