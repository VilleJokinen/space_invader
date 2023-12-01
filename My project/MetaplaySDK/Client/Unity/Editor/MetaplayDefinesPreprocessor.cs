// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System.IO;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Metaplay.Unity
{
    /// <summary>
    /// MetaplayDefinesPreprocessor was removed in Metaplay R24. This build processor checks there are no
    /// METAPLAY_UNITY_DEFINES present. If METAPLAY_UNITY_DEFINES were allowed to exist but had no effect,
    /// it could cause subtle build issues.
    /// </summary>
    public class MetaplayDefinesPreprocessor : IPreprocessBuildWithReport
    {
        const string DefinesFileName = "METAPLAY_UNITY_DEFINES";

        public int callbackOrder => -2;

        public void OnPreprocessBuild(BuildReport report)
        {
            // If the file exists, kill the build.
            if (File.Exists(DefinesFileName))
            {
                string extraDefines = File.ReadAllText(DefinesFileName).Trim();
                throw new BuildFailedException($"METAPLAY_UNITY_DEFINES file is no longer supported. For overriding build target environment, set METAPLAY_ACTIVE_ENVIRONMENT_ID environment variable. The contents of METAPLAY_UNITY_DEFINES were: '{extraDefines}'");
            }
        }
    }
}
