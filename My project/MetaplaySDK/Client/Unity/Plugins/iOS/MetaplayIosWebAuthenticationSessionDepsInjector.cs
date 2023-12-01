// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

// \note: Require UNITY_IOS to avoid reference into UnityEditor.iOS, i.e. having iOS Build Support installed.
#if UNITY_EDITOR && UNITY_IOS

using Metaplay.Core;
using System;
using System.IO;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.iOS.Xcode;

namespace Metaplay.Unity.Ios
{
    class MetaplayIosWebAuthenticationSessionDepsInjector : IPostprocessBuildWithReport
    {
        public int callbackOrder => 40;

        public void OnPostprocessBuild(BuildReport report)
        {
            try
            {
                if (report.summary.platformGroup != UnityEditor.BuildTargetGroup.iOS)
                    return;

                bool enabled = MetaplayCore.Options.FeatureFlags.EnableCompanyAccountClientIntegrations;
                if (enabled)
                    PatchProject(Path.Combine(report.summary.outputPath, "Unity-iPhone.xcodeproj/project.pbxproj"));
            }
            catch (Exception ex)
            {
                throw new BuildFailedException(ex);
            }
        }

        static void PatchProject(string path)
        {
            PBXProject b = new PBXProject();
            b.ReadFromFile(path);
            b.AddFrameworkToProject(b.GetUnityFrameworkTargetGuid(), "AuthenticationServices.framework", weak: true);
            AddPreprocessorDefine(b, "IS_WEBAUTH_ENABLED");
            b.WriteToFile(path);
        }

        static void AddPreprocessorDefine(PBXProject project, string defineName)
        {
            foreach (string buildConfig in project.BuildConfigNames())
            {
                string buildConfigGuid = project.BuildConfigByName(project.GetUnityFrameworkTargetGuid(), buildConfig);
                string previous = project.GetBuildPropertyForConfig(buildConfigGuid, "GCC_PREPROCESSOR_DEFINITIONS");
                string updated = (string.IsNullOrEmpty(previous) ? "$(inherited) " : " ") + $"{defineName}=1";

                project.SetBuildPropertyForConfig(buildConfigGuid, "GCC_PREPROCESSOR_DEFINITIONS", updated);
            }
        }
    }
}

#endif
