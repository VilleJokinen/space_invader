// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if UNITY_ANDROID

#if METAPLAY_HAS_PLUGIN_ANDROID_CUSTOMTABS && !METAPLAY_HAS_GOOGLE_EDM4U
#error io.metaplay.unitysdk.androidcustomtabs package is enabled but the dependency External Dependency Manager is not. Follow the instructions in MetaplaySDK/Plugins/AndroidCustomTabs/README.md
#endif

using Metaplay.Core;
using System;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Metaplay.Unity.Android
{
    /// <summary>
    /// Checks the Unity project configuration and and the EnableCompanyAccountClientIntegrations match.
    /// </summary>
    class MetaplayAndroidCustomTabsChecker : IPreprocessBuildWithReport
    {
        public int callbackOrder => -10;

        // Detect configuration errors on build.
        void IPreprocessBuildWithReport.OnPreprocessBuild(BuildReport report)
        {
            try
            {
                CheckSetupConsistent();
            }
            catch (Exception ex)
            {
                throw new BuildFailedException(ex);
            }
        }

#if UNITY_EDITOR
        // Detect configuration errors on load
        [UnityEditor.InitializeOnLoadMethod]
        private static void InitializeForEditor()
        {
            // Make sure MetaplayCore is init before trying to access it. Ignoring the
            // errors is not a problem -- if there is an exception then the normal init
            // flow will throw that very same exception.
            try
            {
                MetaplayCore.Initialize();
            }
            catch
            {
                return;
            }

            CheckSetupConsistent();
        }
#endif

        static void CheckSetupConsistent()
        {
            if (MetaplayCore.Options.FeatureFlags.EnableCompanyAccountClientIntegrations)
            {
                #if !METAPLAY_HAS_PLUGIN_ANDROID_CUSTOMTABS
                throw new InvalidOperationException(
                    "Company account support is enabled in MetaplayFeatureFlags.EnableCompanyAcountClientIntegrations, but the required Plugin Package is not installed. "
                    + "Add io.metaplay.unitysdk.androidcustomtabs (MetaplaySDK/Plugins/AndroidCustomTabs/Package) package into the Unity project using Unity Package Manager. "
                    + "See instructions in MetaplaySDK/Plugins/AndroidCustomTabs/README.md"
                    );
                #endif
            }
            else
            {
                #if METAPLAY_HAS_PLUGIN_ANDROID_CUSTOMTABS
                throw new InvalidOperationException(
                    "Company account support is disabled in MetaplayFeatureFlags.EnableCompanyAcountClientIntegrations, but the associated Plugin Package is installed. "
                    + "Either enable client integration too, or remove io.metaplay.unitysdk.androidcustomtabs (MetaplaySDK/Plugins/AndroidCustomTabs/Package) package from the Unity project using Unity Package Manager.");
                #endif
            }
        }
    }
}

#endif
