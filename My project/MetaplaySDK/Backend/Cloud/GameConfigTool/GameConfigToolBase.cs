// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Application;
using Metaplay.Core.Config;
using Metaplay.Core.Serialization;
using System;
using System.IO;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Linq;
using static System.FormattableString;

namespace Metaplay.Core.GameConfigTool
{
    /// <summary>
    /// Experimental utility for building CLI tools to operate on game configs. Allows building, printing,
    /// and publishing (to localhost only for now) of game configs.
    /// WARNING: This class is highly experimental and likely to get changed significantly in the future!
    /// </summary>
    public abstract class GameConfigToolBase
    {
        protected GameConfigToolBase()
        {
            // Force-load local assemblies so they are available through reflection
            _ = Application.LoadLocalAssemblies();

            // Initialize Metaplay core and serialization
            MetaplayCore.Initialize();
            MetaSerialization.Initialize(GenerateSerializer(), actorSystem: null);
        }

        static Type GenerateSerializer()
        {
            // Generate serializer
            string executablePath = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            string errorPath = Path.Join(executablePath, "Errors");
            Assembly assembly = RoslynSerializerCompileCache.GetOrCompileAssembly(
                outputDir: executablePath,
                dllFileName: "Metaplay.Generated.dll",
                errorDir: errorPath,
                useMemberAccessTrampolines: false);

            return assembly.GetType("Metaplay.Generated.TypeSerializer");
        }

        protected async Task BuildStaticGameConfigAsync(GameConfigBuildParameters buildParams, bool writeOutputFiles, GameConfigBuildDebugOptions debugOptions = null)
        {
            // \note Setting EnableDebugPrints=true by default. Pass a custom debugOptions from the project's GameConfigTool implementation if you want to disable it.
            if (debugOptions == null)
            {
                debugOptions = new GameConfigBuildDebugOptions
                {
                    EnableDebugPrints = true,
                };
            }

            // Parent config identity (used if making an incremental build, leave empty if full build)
            MetaGuid        parentConfigId  = MetaGuid.None;
            ConfigArchive   parentConfig    = null;

            // If performing an incremental build, load the current StaticGameConfig.mpa from disk as parent
            if (buildParams.IsIncremental)
            {
                // \note GameConfigs only get the configId when uploaded so the server, so the local StaticGameConfig.mpa
                //       does not have one. We just invent one here which is good enough to test the game config builds but
                //       not to generate proper ones for uploading.
                // \todo Figure out a better solution.
                parentConfigId = MetaGuid.New();
                parentConfig = await ConfigArchive.FromFileAsync(StaticGameConfigPath);
            }

            try
            {
                // Build the StaticGameConfig ConfigArchive
                ConfigArchive staticGameConfigArchive = await StaticFullGameConfigBuilder.BuildArchiveAsync(MetaTime.Now, parentConfigId, parentConfig, buildParams, FetcherConfig, debugOptions);

                // Extract SharedGameConfig from StaticGameConfig
                ReadOnlyMemory<byte> sharedArchiveBytes = staticGameConfigArchive.GetEntryBytes("Shared.mpa");
                ConfigArchive sharedGameConfigArchive = ConfigArchive.FromBytes(sharedArchiveBytes);

                // \todo Read into SharedConfig & ServerConfig and validate?

                // Write outputs (if requested to do so)
                if (writeOutputFiles)
                {
                    // Write StaticGameConfig archive to disk
                    byte[] staticBytes = ConfigArchiveBuildUtility.ToBytes(staticGameConfigArchive);
                    string staticEntriesStr = string.Join("\n", staticGameConfigArchive.Entries.Select(entry => Invariant($"  {entry.Name}: {entry.Bytes.Length}")));
                    Console.WriteLine("\nWriting StaticGameConfig as {0} ({1} bytes):\n{2}", StaticGameConfigPath, staticBytes.Length, staticEntriesStr);
                    await FileUtil.WriteAllBytesAsync(StaticGameConfigPath, staticBytes);

                    // Write SharedGameConfig archive to disk
                    byte[] sharedBytes = ConfigArchiveBuildUtility.ToBytes(sharedGameConfigArchive);
                    string sharedEntriesStr = string.Join("\n", sharedGameConfigArchive.Entries.Select(entry => Invariant($"  {entry.Name}: {entry.Bytes.Length}")));
                    Console.WriteLine("\nWriting SharedGameConfig as {0} ({1} bytes):\n{2}", SharedGameConfigPath, sharedBytes.Length, sharedEntriesStr);
                    await FileUtil.WriteAllBytesAsync(SharedGameConfigPath, sharedBytes);
                }
            }
            catch (GameConfigBuildFailed failed) // only handle soft failure here, let unexpected errors propagate through
            {
                // Build failed due to bad inputs, report the errors
                Console.WriteLine("BUILD FAILED WITH ERRORS (GameConfigBuildFailed was thrown):");
                failed.BuildReport.PrintToConsole();
            }
        }

        protected async Task PrintGameConfigAsync()
        {
            // Read StaticGameConfig.mpa from disk
            ConfigArchive staticGameConfigArchive = await ConfigArchive.FromFileAsync(StaticGameConfigPath);

            // Extract SharedGameConfig.mpa from parent archive
            ReadOnlyMemory<byte> sharedGameConfigBytes   = staticGameConfigArchive.GetEntryBytes("Shared.mpa");
            ConfigArchive        sharedGameConfigArchive = ConfigArchive.FromBytes(sharedGameConfigBytes);

            // \todo [petri] dump files within the archives

            // Parse SharedGameConfig (without specializing)
            ISharedGameConfig sharedGameConfig = GameConfigUtil.ImportSharedConfig(sharedGameConfigArchive);

            // Print contents of SharedGameConfig
            GameConfigRepository.GameConfigTypeInfo typeInfo = GameConfigRepository.Instance.GetGameConfigTypeInfo(sharedGameConfig.GetType());
            foreach ((string entryName, GameConfigRepository.GameConfigEntryInfo entryInfo) in typeInfo.Entries)
            {
                IGameConfigEntry entry = (IGameConfigEntry)entryInfo.MemberInfo.GetDataMemberGetValueOnDeclaringType()(sharedGameConfig);
                if (entry is IGameConfigLibraryEntry library)
                {
                    Console.WriteLine("{0}:", entryName);
                    foreach ((object key, object value) in library.EnumerateAll())
                        Console.WriteLine(" {0}: {1}", key, PrettyPrint.Compact(value));
                    Console.WriteLine("");
                }
                else
                    Console.WriteLine("{0}: {1}", entryName, PrettyPrint.Verbose(entry));
            }
        }

        protected async Task PublishGameConfigAsync(string publishEndpoint, string archiveName, string authorizationToken, string queryParams)
        {
            // Send ConfigArchive as POST request to publish endpoint
            ConfigArchive configArchive = await ConfigArchive.FromFileAsync(StaticGameConfigPath);
            byte[] bytes = ConfigArchiveBuildUtility.ToBytes(configArchive);

            string publishUri = $"{publishEndpoint}{archiveName}";
            if (queryParams != null)
                publishUri += "?" + queryParams;

            using (HttpClient httpClient = new HttpClient())
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, publishUri))
            {
                request.Content = new ByteArrayContent(bytes);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                if (authorizationToken != null)
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authorizationToken);

                // \todo The endpoint returns the generated ConfigId, do something with it
                using (HttpResponseMessage response = await httpClient.SendAsync(request))
                {
                    if (!response.IsSuccessStatusCode)
                        throw new InvalidOperationException($"Failed to upload {archiveName} to server (responseCode={response.StatusCode}): {await response.Content.ReadAsStringAsync()}");
                }
            }
        }

        protected virtual string StaticGameConfigPath   => "Backend/Server/GameConfig/StaticGameConfig.mpa";
        protected virtual string SharedGameConfigPath   => "Assets/StreamingAssets/SharedGameConfig.mpa";
        protected virtual string LocalizationsPath      => "Assets/StreamingAssets/Localizations";

        protected abstract IGameConfigSourceFetcherConfig FetcherConfig { get; }
    }
}
