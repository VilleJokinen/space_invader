// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Client;
using System;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using static Metaplay.Core.Client.DefaultEnvironmentConfigProvider;

namespace Metaplay.Unity
{
    /// <summary>
    /// Handles automatically building the JSON environment configs used by <see cref="DefaultEnvironmentConfigProvider"/>
    /// in <see cref="OnPreprocessBuild"/>, and deleting the built environment configs in <see cref="OnPostprocessBuild"/>.
    /// <para>
    /// The supported ways to define the active environment for a client build are (in order of priority):
    /// 1. Unity command line argument '-MetaplayActiveEnvironmentId=ID'
    /// 2. Environment variable <see cref="ActiveEnvironmentIdEnvironmentVariable"/>
    /// 3. Manually calling <see cref="DefaultEnvironmentConfigProvider.BuildEnvironmentConfigs"/> with the active environment ID parameter
    /// 4. Selecting the active environment ID in the Environment Configs editor (in the menu 'Metaplay/Environment Configs')
    /// </para>
    /// </summary>
    public class DefaultEnvironmentConfigBuilder : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        const string ActiveEnvironmentIdEnvironmentVariable = "METAPLAY_ACTIVE_ENVIRONMENT_ID";

        public int callbackOrder { get; } = -1;

        public void OnPostprocessBuild(BuildReport report)
        {
            try
            {
                DefaultEnvironmentConfigProvider.Instance.DeleteBuiltFiles();
            }
            catch (NotDefaultEnvironmentConfigProviderIntegrationException)
            {
                // DefaultEnvironmentConfigProvider is not in use. Doing nothing.
            }
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            BuildEnvironmentConfigs();
        }

        public virtual void BuildEnvironmentConfigs()
        {
            try
            {
                DefaultEnvironmentConfigProvider provider = DefaultEnvironmentConfigProvider.Instance;

                // Command line parameter forces building for the chosen environment
                string commandLineOverride = TryGetCommandLineOverrideId();
                if (commandLineOverride != null)
                {
                    // Build using command line override
                    DebugLog.Info($"[Metaplay] Building environment configs with active environment Id from command line: '{commandLineOverride}'.");
                    provider.BuildEnvironmentConfigs(commandLineOverride, new string[] {commandLineOverride});
                    return;
                }

                // Environment variable override
                string environmentVariableOverride = Environment.GetEnvironmentVariable(ActiveEnvironmentIdEnvironmentVariable);
                if (!string.IsNullOrEmpty(environmentVariableOverride))
                {
                    // Build using environment variable
                    DebugLog.Info($"[Metaplay] Building environment configs with active environment Id from environment variable: '{environmentVariableOverride}'.");
                    provider.BuildEnvironmentConfigs(environmentVariableOverride, new string[] {environmentVariableOverride});
                    return;
                }

                // Skip automatic environment config building if files already exist
                if (provider.IsBuilt())
                {
                    DebugLog.Info($"[Metaplay] Built environment config files already exist (before automatic environment config building). Active environment: '{provider.GetActiveEnvironmentIdFromBuiltFile()}'. Automatic environment config building skipped.");
                    return;
                }

                // Build using active environment Id set in editor window
                string activeEnvironmentId = provider.GetActiveEnvironmentId();
                if (string.IsNullOrEmpty(activeEnvironmentId))
                {
                    throw new BuildFailedException($"Failed to resolve currently active Environment Id, nor is there {ActiveEnvironmentIdEnvironmentVariable} environment variable set or -MetaplayActiveEnvironmentId= command line argument given");
                }

                DebugLog.Info($"[Metaplay] Building environment configs with active environment Id from editor: '{activeEnvironmentId}'.");
                provider.BuildEnvironmentConfigs();
            }
            catch (NotDefaultEnvironmentConfigProviderIntegrationException)
            {
                // DefaultEnvironmentConfigProvider is not in use. Doing nothing.
            }
            catch (Exception ex)
            {
                throw new BuildFailedException(ex);
            }
        }

        static string TryGetCommandLineOverrideId()
        {
            // Search for:
            // -MetaplayActiveEnvironmentId ID
            // --MetaplayActiveEnvironmentId ID
            // -MetaplayActiveEnvironmentId=ID
            // --MetaplayActiveEnvironmentId=ID

            string[] cmdLineArgs = Environment.GetCommandLineArgs();
            for (int ndx = 0; ndx < cmdLineArgs.Length; ++ndx)
            {
                string arg = cmdLineArgs[ndx];
                if (arg.Equals("-MetaplayActiveEnvironmentId", StringComparison.OrdinalIgnoreCase) || arg.Equals("--MetaplayActiveEnvironmentId", StringComparison.OrdinalIgnoreCase))
                {
                    if (ndx + 1 >= cmdLineArgs.Length)
                        throw new InvalidOperationException("-MetaplayActiveEnvironmentId command line command is missing the value");
                    return cmdLineArgs[ndx + 1];
                }
                else if (arg.StartsWith("-MetaplayActiveEnvironmentId=", StringComparison.OrdinalIgnoreCase))
                {
                    return arg.Substring("-MetaplayActiveEnvironmentId=".Length);
                }
                else if (arg.StartsWith("--MetaplayActiveEnvironmentId=", StringComparison.OrdinalIgnoreCase))
                {
                    return arg.Substring("--MetaplayActiveEnvironmentId=".Length);
                }
            }
            return null;
        }
    }
}
