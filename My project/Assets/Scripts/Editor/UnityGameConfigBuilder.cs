// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Unity;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using FileUtil = Metaplay.Core.FileUtil;

// This file contains Metaplay sample code. It can be adapted to suit your project's needs or you can
// replace the functionality completely with your own.
namespace Metaplay.Sample
{
    /// <summary>
    /// Minimal Game Config building utility for building the StaticGameConfig.mpa (for server to use)
    /// and SharedGameConfig.mpa (for client and offline mode to use) config archives.
    ///
    /// This has just the minimal functionality required for the Hello World sample to work.
    /// See the Idler reference project for a more comprehensive config builder.
    /// </summary>
    public static class UnityGameConfigBuilder
    {
        const string SharedGameConfigPath   = "Assets/StreamingAssets/SharedGameConfig.mpa";
        const string StaticGameConfigPath   = "Backend/Server/GameConfig/StaticGameConfig.mpa";

        [MenuItem("Config Builder/Build GameConfig", isValidateFunction: false)]
        public static void BuildFullGameConfig()
        {
            EditorTask.Run(nameof(BuildFullGameConfig), BuildFullGameConfigAsync);
        }

        static async Task BuildFullGameConfigAsync()
        {
            // Build full config (Shared + Server) with minimal params
            ConfigArchive fullConfigArchive = await StaticFullGameConfigBuilder.BuildArchiveAsync(MetaTime.Now, parentId: MetaGuid.None, parent: null, buildParams: null, fetcherConfig: null);

            // Extract SharedGameConfig from full config & write to disk
            var sharedConfigArchive = ConfigArchive.FromBytes(fullConfigArchive.GetEntryByName("Shared.mpa").Bytes);
            Debug.Log($"Writing {SharedGameConfigPath} with {sharedConfigArchive.Entries.Count} entries:\n{string.Join("\n", sharedConfigArchive.Entries.Select(entry => $"  {entry.Name} ({entry.Bytes.Length} bytes): {entry.Hash}"))}");
            await ConfigArchiveBuildUtility.WriteToFileAsync(SharedGameConfigPath, sharedConfigArchive);

            // Write full StaticGameConfig to disk
            Debug.Log($"Writing {StaticGameConfigPath} with {fullConfigArchive.Entries.Count} entries:\n{string.Join("\n", fullConfigArchive.Entries.Select(entry => $"  {entry.Name} ({entry.Bytes.Length} bytes): {entry.Hash}"))}");
            await ConfigArchiveBuildUtility.WriteToFileAsync(StaticGameConfigPath, fullConfigArchive);

            // If in editor, refresh AssetDatabase to make sure Unity sees changed files
            AssetDatabase.Refresh();
        }
    }
}
