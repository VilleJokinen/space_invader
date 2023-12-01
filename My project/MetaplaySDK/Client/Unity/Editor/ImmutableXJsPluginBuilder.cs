// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using System;
using System.Diagnostics;
using System.IO;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Metaplay.Unity
{
    /// <summary>
    /// Unity build hooks for generating ImmutableX plugin js bundle.
    /// </summary>
    public class MetaplayImmutableXJsLibBuilder : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public int callbackOrder => 100;
        bool _enabled;
        string _jsLibFolder;

        [Serializable]
        public class PackageJson
        {
#pragma warning disable IDE1006
            public string version;
#pragma warning restore IDE1006
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            try
            {
                _enabled = report.summary.platformGroup == UnityEditor.BuildTargetGroup.WebGL && MetaplayCore.Options.FeatureFlags.EnableImmutableXLinkClientLibrary;
                if (!_enabled)
                    return;

                // Check if already cached
                _jsLibFolder = InternalFindBuildScripts();
                PackageJson package = JsonUtility.FromJson<PackageJson>(File.ReadAllText(Path.Combine(_jsLibFolder, "package.json")));
                string buildVersionPath = Path.Combine(_jsLibFolder, "dist", "version");
                if (File.Exists(buildVersionPath) && File.ReadAllText(buildVersionPath) == package.version)
                    return;

                Debug.Log("Building MetaplayImxLinkSdkJsPlugin...");
                UnityBuildUtil.RunShell(_jsLibFolder, command: "npm ci");
                UnityBuildUtil.RunShell(_jsLibFolder, command: "npm run build");
                Debug.Log("Building MetaplayImxLinkSdkJsPlugin... DONE");

                File.WriteAllText(buildVersionPath, package.version);
            }
            catch (Exception ex)
            {
                throw new BuildFailedException(ex);
            }
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            try
            {
                if (!_enabled)
                    return;

                File.Copy(
                    sourceFileName: Path.Combine(_jsLibFolder, "dist", "metaplay-imx-link-sdk-plugin.min.js"),
                    destFileName: Path.Combine(report.summary.outputPath, "metaplay-imx-link-sdk-plugin.min.js"),
                    overwrite: true
                    );
                File.Copy(
                    sourceFileName: Path.Combine(_jsLibFolder, "dist", "metaplay-imx-link-sdk-plugin.min.js.LICENSE.txt"),
                    destFileName: Path.Combine(report.summary.outputPath, "metaplay-imx-link-sdk-plugin.min.js.LICENSE.txt"),
                    overwrite: true
                    );
            }
            catch (Exception ex)
            {
                throw new BuildFailedException(ex);
            }
        }

        static string InternalFindBuildScripts()
        {
            string sdkPath = UnityBuildUtil.ResolveMetaplaySdkPath();
            string buildScriptsPath = Path.Combine(sdkPath, "Scripts", "MetaplayImxLinkSdkJsPlugin");

            if (!Directory.Exists(buildScriptsPath))
                throw new InvalidOperationException($"Could not find MetaplayImxLinkSdkJsPlugin build directory: expected it to be in {buildScriptsPath}");

            return buildScriptsPath;
        }
    }
}
