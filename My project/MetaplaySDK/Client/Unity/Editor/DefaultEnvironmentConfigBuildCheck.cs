// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Client;
using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using static Metaplay.Core.Client.DefaultEnvironmentConfigProvider;

namespace Metaplay.Unity
{
    public class DefaultEnvironmentConfigBuildCheck : IPreprocessBuildWithReport
    {
        public int callbackOrder { get; } = int.MaxValue;

        public void OnPreprocessBuild(BuildReport report)
        {
            try
            {
                if (!DefaultEnvironmentConfigProvider.Instance.IsBuilt())
                {
                    throw new BuildFailedException(
                        "Metaplay environment configs have not been built! See DefaultEnvironmentConfigBuilder " +
                        "for ways to build the environment configs. Check also for other errors in the build log " +
                        "originating from DefaultEnvironmentConfigBuilder.");
                }
            }
            catch (NotDefaultEnvironmentConfigProviderIntegrationException)
            {
                // DefaultEnvironmentConfigProvider is not in use. Doing nothing.
            }
        }
    }
}
