// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using System;
using UnityEngine;

namespace Metaplay.Unity.Android
{
    public static class MetaplayAndroidCustomTabs
    {
        public static void LaunchCustomTabs(string url)
        {
            if (!MetaplayCore.Options.FeatureFlags.EnableCompanyAccountClientIntegrations)
                throw new InvalidOperationException("FeatureFlags.EnableCompanyAccountClientIntegrations must be enabled to use MetaplayAndroidCustomTabs.");

            using (AndroidJavaClass clazz = new AndroidJavaClass("io.metaplay.android.customtabs.MetaplayAndroidCustomTabs"))
            {
                clazz.CallStatic("LaunchCustomTabs", url);
            }
        }
    }
}
