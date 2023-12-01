// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Metaplay.Cloud.RuntimeOptions
{
    /// <summary>
    /// The environment variables represented as a list of RuntimeOptions definitions.
    /// </summary>
    public class EnvironmentRuntimeOptionsSource
    {
        readonly OrderedDictionary<string, string> _fields;
        public IReadOnlyDictionary<string, string> Definitions => _fields;
        public IConfigurationSource ConfigurationSource => new FixedFieldsConfigurationSource(_fields);

        EnvironmentRuntimeOptionsSource(OrderedDictionary<string, string> fields)
        {
            _fields = fields;
        }

        /// <summary>
        /// Parse the environment and returns EnvironmentRuntimeOptionsSource form the contents.
        /// </summary>
        public static EnvironmentRuntimeOptionsSource Parse(string prefix)
        {
            return Parse(prefix, GetEnvironmentVariables());
        }

        /// <summary>
        /// Parse the given environment and returns EnvironmentRuntimeOptionsSource form the contents.
        /// </summary>
        public static EnvironmentRuntimeOptionsSource Parse(string prefix, OrderedDictionary<string, string> environmentVariables)
        {
            OrderedDictionary<string, string> fields = new OrderedDictionary<string, string>();

            foreach ((string environmentVariable, string value) in environmentVariables)
            {
                if (IsIgnoredEnvironmentVariable(environmentVariable))
                    continue;
                if (!environmentVariable.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                string configKey = environmentVariable.Substring(prefix.Length).Replace("__", ":");
                fields.Add(configKey, value);
            }

            return new EnvironmentRuntimeOptionsSource(fields);
        }

        static OrderedDictionary<string, string> GetEnvironmentVariables()
        {
            OrderedDictionary<string, string> values = new OrderedDictionary<string, string>();
            foreach (string environmentVariable in Environment.GetEnvironmentVariables().Keys)
            {
                string value = Environment.GetEnvironmentVariable(environmentVariable) ?? "";
                values.Add(environmentVariable, value);
            }
            return values;
        }

        static bool IsIgnoredEnvironmentVariable(string sourceEnvVar)
        {
            // Ignore certain well-known environment variables here. They look like options, so the parser is confused, but are harmless
            // \note: refer to env vars with complete name to make Grepping easier.
            string[] ignoredEnvironmentVariables = new string[]
            {
                "METAPLAY_ENVIRONMENT_FAMILY",
                "METAPLAY_OPTIONS",
                "METAPLAY_EXTRA_OPTIONS",

                // legacy options. Avoid warnings in backwards-compatible environments
                "METAPLAY_GRAFANAURL",
                "METAPLAY_KUBERNETESNAMESPACE",
                "METAPLAY_IP",
                "METAPLAY_REMOTINGHOSTSUFFIX",
                "METAPLAY_ENVIRONMENT",
                "METAPLAY_FILELOGPATH",
                "METAPLAY_LOGFORMAT",
                "METAPLAY_LOGFILERETAINCOUNT",
                "METAPLAY_FILELOGSIZELIMIT",
                "METAPLAY_CLUSTERCOOKIE",
                "METAPLAY_CLIENTPORTS",

                // environment variables that look like options. Avoid warnings.
                "METAPLAY_CLUSTERCONFIGJSON",
                "METAPLAY_CLIENT_SVC_*",
            };

            foreach (string ignoredVariable in ignoredEnvironmentVariables)
            {
                // allow minimal wildcard syntax
                if (ignoredVariable.EndsWith('*'))
                {
                    string prefix = ignoredVariable.Substring(0, ignoredVariable.Length - 1);
                    if (sourceEnvVar.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                else
                {
                    if (string.Equals(sourceEnvVar, ignoredVariable, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            return false;
        }
    }
}
