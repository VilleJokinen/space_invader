// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Tasks;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Metaplay.Unity
{
    // \note: placeholder for the proper config manager
    public static class MetaplayConfigManager
    {
        public static void OnSessionStarted()
        {
            // Clean unused config files. CleanConfigCacheDirectoryOnBackgroundAsync may do nothing based on its internal heuristics.
            CleanConfigCacheDirectoryOnBackgroundAsync("SharedGameConfig", retainedVersions: new OrderedSet<ContentHash>(MetaplaySDK.Connection.SessionStartResources.GameConfigBaselineVersions.Values));
            CleanConfigCacheDirectoryOnBackgroundAsync("SharedGameConfigPatches", retainedVersions: new OrderedSet<ContentHash>(MetaplaySDK.Connection.SessionStartResources.GameConfigPatchVersions.Values));
        }

        /// <summary>
        /// Maintain upper bound for "uncleanness" in cached folder: If number of cached versions exceeds a certain internal limit, remove all
        /// cached versions that are not currently in use (i.e. in <paramref name="retainedVersions"/>). Otherwise, do nothing. This prevents
        /// the cache from growing boundlessly while still keeping a reasonable number of recent entries in cache. Unlike more intelligent schemes
        /// like LRU, this requires no state.
        /// </summary>
        static void CleanConfigCacheDirectoryOnBackgroundAsync(string cacheDirName, OrderedSet<ContentHash> retainedVersions)
        {
            _ = MetaTask.Run(async () => await DoCleanConfigCacheDirectoryOnBackgroundAsync(cacheDirName, retainedVersions), MetaTask.BackgroundScheduler);
        }
        static async Task DoCleanConfigCacheDirectoryOnBackgroundAsync(string cacheDirName, OrderedSet<ContentHash> retainedVersions)
        {
            string downloadPath = Path.Combine(MetaplaySDK.DownloadCachePath, cacheDirName);
            string[] cachedVersions;

            try
            {
                cachedVersions = await DirectoryUtil.GetDirectoryFilesAsync(downloadPath);
            }
            catch (DirectoryNotFoundException)
            {
                return;
            }

            // Maintain upper bound for "uncleanness" in cached versions: If number of cached versions
            // exceeds the limit, remove all cached versions that are not currently in use. Otherwise,
            // do nothing. This prevents the cache from growing boundlessly while still keeping a
            // reasonable number of recent entries in cache. Unlike more intelligent schemes like LRU,
            // this requires no state.
            const int cleanupTriggerNumVersions = 10;
            if (cachedVersions.Length < cleanupTriggerNumVersions)
                return;

            foreach (string existingCacheEntryPath in cachedVersions)
            {
                if (!ContentHash.TryParseString(Path.GetFileName(existingCacheEntryPath), out ContentHash existingVersion))
                    continue;
                if (retainedVersions.Contains(existingVersion))
                    continue;

                MetaplaySDK.Logs.Config.Info("Pruning cached {Name} version {Version}", cacheDirName, existingVersion);
                try
                {
                    await FileUtil.DeleteAsync(existingCacheEntryPath);
                }
                catch (Exception ex)
                {
                    MetaplaySDK.Logs.Config.Warning("Failed to prune cached {Name} version {Version}: {Error}", cacheDirName, existingVersion, ex);
                }
            }
        }
    }
}
