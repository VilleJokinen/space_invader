// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Debug = UnityEngine.Debug;

namespace Metaplay.Unity
{
    public static class UnityBuildUtil
    {
        /// <summary>
        /// Resolve the relative path from the project root to the Metaplay SDK root.
        /// This works by parsing the Metaplay SDK dependency in Packages/manifest.json.
        /// </summary>
        public static string ResolveMetaplaySdkPath(Action<string> debugLog = null)
        {
            string metaplayClientPath = ResolveMetaplayClientPath(debugLog);
            return FileUtil.NormalizePath(Path.Combine(metaplayClientPath, ".."));
        }

        /// <summary>
        /// Resolve the relative path from the project root to the Metaplay SDK's Client folder.
        /// This works by parsing the Metaplay SDK dependency in Packages/manifest.json.
        /// </summary>
        public static string ResolveMetaplayClientPath(Action<string> debugLog = null)
        {
            string manifestJson = File.ReadAllText("Packages/manifest.json");
            debugLog?.Invoke($"Packages/manifest.json:\n{manifestJson}");
            ManifestContent content = Newtonsoft.Json.JsonConvert.DeserializeObject<ManifestContent>(manifestJson);

            if (!content.Dependencies.ContainsKey(MetaplayPackageId))
                throw new InvalidOperationException($"Unable to find '{MetaplayPackageId}' from Packages/manifest.json");

            string pathToMetaplay = content.Dependencies[MetaplayPackageId];
            debugLog?.Invoke($"Path to MetaplaySDK (from manifest): {pathToMetaplay}");

            if (!pathToMetaplay.StartsWith("file:../"))
                throw new InvalidOperationException($"Expecting a relative path to Metaplay SDK (should start with 'file:../'), got '{pathToMetaplay}'");

            string resolvedMetaplayClientPath = FileUtil.NormalizePath(Path.Combine("Packages", pathToMetaplay.Replace("file:", "")));
            debugLog?.Invoke($"Resolved path to MetaplaySDK: {resolvedMetaplayClientPath}");

            return resolvedMetaplayClientPath;
        }

        const string MetaplayPackageId = "io.metaplay.unitysdk";

        /// <summary>
        /// Contents of Unity Packages/manifest.json (the parts we are interested in).
        /// </summary>
        class ManifestContent
        {
            #pragma warning disable CS0649
            [Newtonsoft.Json.JsonProperty("dependencies")]
            public Dictionary<string, string> Dependencies;
            #pragma warning restore CS0649
        }

        /// <summary>
        /// Runs shell command and waits for it to complete. If command completes with a non-zero exit code,
        /// prints the stdout and stderr and throws InvalidOperationException.
        /// </summary>
        public static void RunShell(string workingDirectory, string command, OrderedDictionary<string, string> environment = null)
        {
            DoRunShell(workingDirectory, command, environment);
        }

        /// <summary>
        /// Runs process and waits for it to complete. If command completes with a non-zero exit code,
        /// prints the stdout and stderr and throws InvalidOperationException.
        /// </summary>
        public static void RunProcess(string workingDirectory, string executablePath, string[] args, OrderedDictionary<string, string> environment = null, string commandDisplayName = null)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo()
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
            };
            startInfo.FileName = executablePath;
            foreach (string arg in args)
                startInfo.ArgumentList.Add(arg);
            if (environment != null)
            {
                foreach ((string k, string v) in environment)
                    startInfo.Environment.Add(k, v);
            }

            // Fill in default display name (if not provided)
            commandDisplayName ??= $"{executablePath} ; args=[{string.Join(";", args)}]";

            RunAndWaitProcess(startInfo, commandDisplayName);
        }

        static void DoRunShell(string workingDirectory, string command, OrderedDictionary<string, string> environment = null)
        {
#if UNITY_EDITOR_WIN
            RunProcess(workingDirectory, "cmd.exe", new string[] { "/D", "/S", "/C", command }, environment, commandDisplayName: command);
#else
            RunProcess(workingDirectory, "/bin/sh", new string[] { "-c", command }, environment, commandDisplayName: command);
#endif
        }

        static void RunAndWaitProcess(ProcessStartInfo startInfo, string commandDisplayName)
        {
            Process process = Process.Start(startInfo);
            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                stdoutTask.Wait();
                stderrTask.Wait();

                string stdout = stdoutTask.GetCompletedResult().Trim();
                if (!string.IsNullOrEmpty(stdout))
                    Debug.Log(stdout);

                string stderr = stderrTask.GetCompletedResult().Trim();
                if (!string.IsNullOrEmpty(stderr))
                    Debug.LogError(stderr);

                throw new InvalidOperationException($"Failed to run \"{commandDisplayName}\"");
            }
        }
    }
}
