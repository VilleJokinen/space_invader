// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Network;
using Metaplay.Core.Tasks;
using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using FileUtil = Metaplay.Core.FileUtil;
#if UNITY_2017_1_OR_NEWER
using UnityEngine;
#endif
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
#endif

namespace Metaplay.Core.Client
{
    public class DefaultEnvironmentConfigProvider : IEnvironmentConfigProvider
    {
        protected const string ActiveEnvOverrideEditorPrefsKey         = "METAPLAY_ACTIVE_EDITOR_ENVIRONMENT";
        protected const string ActiveEnvClientOverridePlayerPrefsKey   = "METAPLAY_ACTIVE_ENVIRONMENT_OVERRIDE";
        
        protected string EditorEnvironmentConfigsFileName            => "EnvironmentConfigs";
        protected string EnvironmentConfigsFileExt                   => ".json";
        protected string ActiveEnvironmentConfigIdFileExt            => ".txt";
        protected string ResourcesFolder                             => "Assets/Resources";
        protected string EditorEnvironmentConfigsFolderPath          => "Metaplay/Editor";
        protected string BuildEnvironmentConfigsFileName             => "BuildEnvironmentConfigs";
        protected string BuildEnvironmentConfigsFolderPath           => "Metaplay/BuiltEnvironmentConfigs";
        protected string ActiveEnvironmentConfigIdFileName           => "BuildActiveEnvironmentId";

        /// <summary>
        /// Cache for active environment id, set in <see cref="InitializeSingleton"/> and in <see cref="SetActiveEnvironmentId"/>
        /// </summary>
        protected string _activeEnvironmentId = "";
        protected string _activeEnvironmentIdEditorOverride;

        protected List<EnvironmentConfig> _environmentConfigs;

        public bool Initialized = false; // Used by editor window

        public EnvironmentConfig GetCurrent() => GetEnvironmentConfig(GetActiveEnvironmentId());

        /// <inheritdoc cref="GetInstance"/>
        public static DefaultEnvironmentConfigProvider Instance => GetInstance();

        /// <summary>
        /// Gets the provider singleton instance. Throws an InvalidOperationException if the current IEnvironmentConfigProvider
        /// implementation is not of type DefaultEnvironmentConfigProvider, meaning another provider implementation is in use.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private static DefaultEnvironmentConfigProvider GetInstance()
        {
            if (IntegrationRegistry.Get<IEnvironmentConfigProvider>() is DefaultEnvironmentConfigProvider provider)
            {
                return provider;
            }
            else
            {
                throw new NotDefaultEnvironmentConfigProviderIntegrationException("[Metaplay] Current IEnvironmentConfigProvider implementation is not of type DefaultEnvironmentConfigProvider.");
            }
        }

        public class NotDefaultEnvironmentConfigProviderIntegrationException : Exception
        {
            public NotDefaultEnvironmentConfigProviderIntegrationException(string message) : base(message) { }
        }
        
        public class EnvironmentConfigClientBuildOptions
        {
            public string              ActiveEnvironmentId;
            public EnvironmentConfig[] EnvironmentsToIncludeInBuild;

            public EnvironmentConfigClientBuildOptions(string activeEnvironmentId, EnvironmentConfig[] environments)
            {
                ActiveEnvironmentId          = activeEnvironmentId;
                EnvironmentsToIncludeInBuild = environments;
            }
        }

        public DefaultEnvironmentConfigProvider() { }

        public virtual void InitializeSingleton()
        {
            // Load configs
#if NETCOREAPP
            return;
#elif UNITY_EDITOR
            _environmentConfigs = LoadConfigs(EditorEnvironmentConfigsFolderPath, EditorEnvironmentConfigsFileName);
            _activeEnvironmentIdEditorOverride = LoadActiveEnvironmentIdEditorOverride();
#else
            _environmentConfigs = LoadConfigs(BuildEnvironmentConfigsFolderPath, BuildEnvironmentConfigsFileName);
#endif
#pragma warning disable 0162 // Unreachable code detected
            _activeEnvironmentId   = GetActiveEnvironmentId();

            Initialized = true;
#pragma warning restore 0162 // Unreachable code detected
        }

#if UNITY_EDITOR
        /// <summary>
        /// Reloads environment configs from file. 
        /// </summary>
        public virtual void ReloadConfigs()
        {
            _environmentConfigs = LoadConfigs(EditorEnvironmentConfigsFolderPath, EditorEnvironmentConfigsFileName);
        }

        /// <summary>
        /// Gets the environment config build options defined in-editor.  
        /// </summary>
        public virtual EnvironmentConfigClientBuildOptions GetEditorDefinedBuildOptions()
        {
            string activeEnvironmentId = LoadActiveEnvironmentId();

            List<EnvironmentConfig> environmentsToIncludeInBuild = new List<EnvironmentConfig>()
            {
                GetEnvironmentConfig(activeEnvironmentId),
            };

            return new EnvironmentConfigClientBuildOptions(activeEnvironmentId, environmentsToIncludeInBuild.ToArray());
        }

        /// <summary>
        /// Gets the JSON preview of the filtered environment configs that would be included in a client build. Used in Environment Configs editor window.
        /// </summary>
        public virtual string GetEditorDefinedBuildEnvironmentsPreview()
        {
            EnvironmentConfigClientBuildOptions buildOptions = GetEditorDefinedBuildOptions();
            EnvironmentConfig[] filteredEnvironments = FilterEnvironmentConfigs(buildOptions.EnvironmentsToIncludeInBuild);
            return ConvertToJson(filteredEnvironments);
        }

        /// <summary>
        /// Prepare the provider for client build by copying and creating necessary environment config files in a client accessible location.
        /// Filters out unwanted environments from files included in the build, and filters the included environments to not include unwanted data.
        /// Uses the active environment and build included environments values stored internally and set in the Unity Environment Configs editor window.
        /// </summary>
        public virtual void BuildEnvironmentConfigs()
        {
            try
            {
                ReloadConfigs();

                BuildEnvironmentConfigs(GetEditorDefinedBuildOptions());
            }
            catch (Exception ex)
            {
                throw new BuildFailedException("[Metaplay] Environment configs failed to build: " + ex);
            }
        }

        /// <summary>
        /// Prepare the provider for client build by copying and creating necessary environment config files in a client accessible location.
        /// Filters out unwanted environments from files included in the build, and filters the included environments to not include unwanted data.
        /// Finds the given environments from the environment configs master file. 
        /// </summary>
        public virtual void BuildEnvironmentConfigs(string activeEnvironmentId, string[] includedEnvironmentsIds)
        {
            if (!includedEnvironmentsIds.Contains(activeEnvironmentId))
            {
                throw new BuildFailedException("[Metaplay] Given active environment Id is not included in given build included environments.");
            }

            try
            {
                ReloadConfigs();

                List<EnvironmentConfig> environments = new List<EnvironmentConfig>();
                foreach (string envId in includedEnvironmentsIds)
                {
                    environments.Add(GetEnvironmentConfig(envId));
                }

                BuildEnvironmentConfigs(new EnvironmentConfigClientBuildOptions(activeEnvironmentId, environments.ToArray()));
            }
            catch (Exception ex)
            {
                throw new BuildFailedException("[Metaplay] Environment configs failed to build: " + ex);
            }
        }

        /// <summary>
        /// Prepare the provider for client build by copying and creating necessary files in a client accessible location.
        /// Filters out unwanted environments from files included in the build, and filters the included environments to not include unwanted data.
        /// </summary>
        public virtual void BuildEnvironmentConfigs(EnvironmentConfigClientBuildOptions options)
        {
            // Configs are stored in an Editor folder, therefore they will not be included in build by default.
            // So we create a new file outside the Editor folder to hold only the filtered configs that will be included in the build.

            EnvironmentConfig[] filteredEnvironmentsInBuild = FilterEnvironmentConfigs(options.EnvironmentsToIncludeInBuild);

            // Write active environment id to file
            WriteActiveEnvironmentIdToFile(Path.Join(ResourcesFolder, BuildEnvironmentConfigsFolderPath), ActiveEnvironmentConfigIdFileName, options.ActiveEnvironmentId);

            // Write build-included environments to file that will be included in build
            WriteEnvironmentConfigsToFile(Path.Join(ResourcesFolder, BuildEnvironmentConfigsFolderPath), BuildEnvironmentConfigsFileName, filteredEnvironmentsInBuild.ToArray());
        }

        /// <summary>
        /// Creates filtered copies of the given environment configs. <see cref="EnvironmentConfig.CreateClientBuildCopy"/>
        /// </summary>
        public virtual EnvironmentConfig[] FilterEnvironmentConfigs(EnvironmentConfig[] environments)
        {
            List<EnvironmentConfig> environmentsInBuild = new List<EnvironmentConfig>();
            foreach (EnvironmentConfig config in environments)
            {
                environmentsInBuild.Add(config.CreateClientBuildCopy());
            }

            return environmentsInBuild.ToArray();
        }

        public virtual void DeleteBuiltFiles()
        {
#pragma warning disable MP_WGL_00 // Feature is poorly supported in WebGL
            string builtActiveEnvironmentIdFilePath = Path.Join(ResourcesFolder, BuildEnvironmentConfigsFolderPath, ActiveEnvironmentConfigIdFileName) + ActiveEnvironmentConfigIdFileExt;
            File.Delete(builtActiveEnvironmentIdFilePath);
            File.Delete(builtActiveEnvironmentIdFilePath + ".meta");
            string builtEnvironmentsFilePath = Path.Join(ResourcesFolder, BuildEnvironmentConfigsFolderPath, BuildEnvironmentConfigsFileName) + EnvironmentConfigsFileExt;
            File.Delete(builtEnvironmentsFilePath);
            File.Delete(builtEnvironmentsFilePath + ".meta");

            string folderPath = Path.Join(ResourcesFolder, BuildEnvironmentConfigsFolderPath);
            if (Directory.GetFiles(folderPath).Length == 0 && Directory.GetDirectories(folderPath).Length == 0)
            {
                Directory.Delete(folderPath);
                File.Delete(folderPath + ".meta");
            }
#pragma warning restore MP_WGL_00 // Feature is poorly supported in WebGL

            AssetDatabase.Refresh();
        }

        public virtual bool IsBuilt()
        {
#pragma warning disable MP_WGL_00 // Feature is poorly supported in WebGL
            return (File.Exists(Path.Join(ResourcesFolder, BuildEnvironmentConfigsFolderPath, ActiveEnvironmentConfigIdFileName) + ActiveEnvironmentConfigIdFileExt)
                && File.Exists(Path.Join(ResourcesFolder, BuildEnvironmentConfigsFolderPath, BuildEnvironmentConfigsFileName) + EnvironmentConfigsFileExt));
#pragma warning restore MP_WGL_00 // Feature is poorly supported in WebGL
        }
        
        /// <summary>
        /// Use to set and save to file a modified or new environment
        /// </summary>
        /// <param name="environment"></param>
        public virtual void SetEnvironmentConfig(EnvironmentConfig environment)
        {
            ReloadConfigs();

            // Replace or add environment in cached list
            EnvironmentConfig oldEnv = TryGetEnvironmentConfig(environment.Id);
            if (oldEnv != null)
            {
                int index = _environmentConfigs.IndexOf(oldEnv);
                _environmentConfigs.Remove(oldEnv);
                _environmentConfigs.Insert(index, environment);
            }
            else
            {
                _environmentConfigs.Add(environment);
            }

            WriteEnvironmentConfigsToFile(Path.Join(ResourcesFolder, EditorEnvironmentConfigsFolderPath), EditorEnvironmentConfigsFileName, _environmentConfigs.ToArray());
        }

        public virtual string ConvertToJson(EnvironmentConfig[] environments)
        {
            // Convert environments array to a json string
            // Add a converter to serialize enums as strings
            StringEnumConverter converter = new StringEnumConverter();
            string jsonSerializedObject = JsonConvert.SerializeObject(environments, Formatting.Indented, converter);
            // Use \n on all platforms
            return jsonSerializedObject.Replace("\r\n", "\n");
        }

        public virtual void SetAllEnvironmentConfigs(EnvironmentConfig[] environmentConfigs)
        {
            _environmentConfigs = environmentConfigs.ToList();
            WriteEnvironmentConfigsToFile(Path.Join(ResourcesFolder, EditorEnvironmentConfigsFolderPath), EditorEnvironmentConfigsFileName, _environmentConfigs.ToArray());
        }
        
        /// <summary>
        /// Serializes the given environments to a file as json.
        /// </summary>
        protected virtual void WriteEnvironmentConfigsToFile(string folderPath, string fileName, EnvironmentConfig[] configs)
        {
            // Convert to a json string
            string jsonSerializedObject = ConvertToJson(configs);

            // Ensure folder exists
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            // Write to file
#pragma warning disable MP_WGL_00 // Feature is poorly supported in WebGL
            File.WriteAllText(Path.Join(folderPath, fileName) + EnvironmentConfigsFileExt, jsonSerializedObject);
#pragma warning restore MP_WGL_00 // Feature is poorly supported in WebGL

            AssetDatabase.Refresh();
        }

        /// <summary>
        /// Writes the given environment Id to the active environment file. 
        /// </summary>
        protected virtual void WriteActiveEnvironmentIdToFile(string folderPath, string fileName, string environmentId)
        {
            // Ensure folder exists
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            // Write active environment Id to file
#pragma warning disable MP_WGL_00 // Feature is poorly supported in WebGL
            File.WriteAllText(Path.Join(folderPath, fileName) + ActiveEnvironmentConfigIdFileExt, environmentId);
#pragma warning restore MP_WGL_00 // Feature is poorly supported in WebGL

            AssetDatabase.Refresh();
        }
        
        public virtual void SetActiveEnvironmentIdEditorOverride(string editorEnvironmentId)
        {
            _activeEnvironmentIdEditorOverride = editorEnvironmentId;
            PlayerPrefs.SetString(ActiveEnvOverrideEditorPrefsKey, editorEnvironmentId);
        }

        public virtual string LoadActiveEnvironmentIdEditorOverride()
        {
            return PlayerPrefs.GetString(ActiveEnvOverrideEditorPrefsKey, "");
        }
#endif // if UNITY_EDITOR

        public virtual string GetActiveEnvironmentId()
        {
            if (string.IsNullOrEmpty(_activeEnvironmentId))
            {
                _activeEnvironmentId = LoadActiveEnvironmentId();
            }

            return _activeEnvironmentId;
        }

        /// <summary>
        /// Loads the stored active environment Id, but prioritizes the editor or runtime override if they are set.
        /// </summary>
        protected virtual string LoadActiveEnvironmentId()
        {
#if UNITY_EDITOR
            // Return editor override if it's enabled and set
            if (!string.IsNullOrEmpty(_activeEnvironmentIdEditorOverride))
            {
                return _activeEnvironmentIdEditorOverride;
            }
            else
            {
                return "";
            }
#elif UNITY_2017_1_OR_NEWER
            // Load from PlayerPrefs if set (runtime active environment override)
            string playerPrefsOverride = PlayerPrefs.GetString(ActiveEnvClientOverridePlayerPrefsKey);
            if (!string.IsNullOrEmpty(playerPrefsOverride))
            {
                DebugLog.Debug($"[Metaplay] Using active environment Id from PlayerPrefs: '{playerPrefsOverride}'");
                return playerPrefsOverride;
            }

            // Else load from file
            return GetActiveEnvironmentIdFromBuiltFile();
#else
            return null;
#endif
        }

        public virtual string[] GetAllEnvironmentIds()
        {
            return _environmentConfigs.Select(x => x.Id).ToArray();
        }

        public virtual string GetActiveEnvironmentIdFromBuiltFile()
        {
            return LoadActiveEnvironmentIdFromFile(Path.Join(BuildEnvironmentConfigsFolderPath, ActiveEnvironmentConfigIdFileName));
        }

        public virtual void SetActiveEnvironmentId(string environmentId)
        {
            _activeEnvironmentId = environmentId;
#if UNITY_EDITOR
            // Check that config for given Id exists
            if (!EnvironmentExists(environmentId))
                DebugLog.Debug($"[Metaplay] Setting active environment id to '{environmentId}', but no environment with that id can be found!");

            SetActiveEnvironmentIdEditorOverride(environmentId);
#elif UNITY_2017_1_OR_NEWER
            // Check that config for given Id exists
            if (!EnvironmentExists(environmentId))
            {
                throw new ArgumentException($"[Metaplay] Trying to set active environment id to '{environmentId}', but no environment with that id can be found! Active environment id has not been changed.");
            }

            PlayerPrefs.SetString(ActiveEnvClientOverridePlayerPrefsKey, environmentId);
#endif
        }

#if UNITY_EDITOR || UNITY_2017_1_OR_NEWER
        public virtual void ClearActiveEnvironmentClientOverride()
        {
            PlayerPrefs.DeleteKey(ActiveEnvClientOverridePlayerPrefsKey);
        }
#endif

        /// <summary>
        /// Returns the first environment with a matching id. Throws an ArgumentException if no matching environment is found.
        /// </summary>
        public virtual EnvironmentConfig GetEnvironmentConfig(string environmentId)
        {
            EnvironmentConfig environmentConfig = TryGetEnvironmentConfig(environmentId);
            if (environmentConfig == null)
                throw new ArgumentException($"[Metaplay] Environment Config with Id '{environmentId}' does not exist! " +
                    $"Set the active environment ID in the Environment Configs editor (Metaplay/Environment Configs), " +
                    $"or in another way specified in DefaultEnvironmentConfigBuilder.");

            return environmentConfig;
        }

        /// <summary>
        /// Returns the first environment with a matching id, or null if none are found. 
        /// </summary>
        public virtual EnvironmentConfig TryGetEnvironmentConfig(string environmentId)
        {
            EnvironmentConfig environmentConfig = _environmentConfigs.Find(spec => spec.Id == environmentId);

            return environmentConfig;
        }

        public virtual EnvironmentConfig[] GetAllEnvironmentConfigs()
        {
            return _environmentConfigs.ToArray();
        }

        /// <summary>
        /// Loads all environment configs from file. 
        /// </summary>
        public virtual List<EnvironmentConfig> LoadConfigs(string folderPath, string fileName)
        {
#if NETCOREAPP
            return null;
#endif
#if UNITY_EDITOR || UNITY_2017_1_OR_NEWER
            string environmentsJson;
            string filePath = Path.Join(ResourcesFolder, folderPath, fileName);
#endif
#if UNITY_EDITOR
            // Create default config file if file not found
#pragma warning disable MP_WGL_00 // Feature is poorly supported in WebGL
            if (!File.Exists(filePath + EnvironmentConfigsFileExt))
            {
#pragma warning restore MP_WGL_00 // Feature is poorly supported in WebGL
                DebugLog.Warning($"[Metaplay] Environment Configs file not found, creating new one at: {filePath + EnvironmentConfigsFileExt}.");
                EnvironmentConfig[] defaultEnvironments = CreateDefaultEnvironments();
                WriteEnvironmentConfigsToFile(Path.Join(ResourcesFolder, folderPath), fileName, defaultEnvironments);
                return defaultEnvironments.ToList();
            }
            else
            {
                // Deserialize from JSON
                // Read from file 
                Task<string> readTask = MetaTask.Run(async () => await FileUtil.ReadAllTextAsync(filePath + EnvironmentConfigsFileExt));
                readTask.Wait();
                environmentsJson = readTask.Result;
            }
#elif UNITY_2017_1_OR_NEWER
            // Load environment configs file from Resources folder
            environmentsJson = Resources.Load<TextAsset>(Path.Join(folderPath, fileName))?.text;
            if (environmentsJson == null)
            {
                throw new FileNotFoundException("[Metaplay] Build Environment Config file not found!");
            }
#endif
#if UNITY_EDITOR || UNITY_2017_1_OR_NEWER
            // Convert json to config objects
            Type                configType      = IntegrationRegistry.GetSingleIntegrationType<EnvironmentConfig>();
            Type                configArrayType = Array.CreateInstance(configType, 0).GetType();
            EnvironmentConfig[] environments    = (EnvironmentConfig[])JsonConvert.DeserializeObject(environmentsJson, configArrayType);
            if (environments == null)
                throw new SerializationException("[Metaplay] Environment Config file is invalid!");

            return environments.ToList();
#endif
        }

        /// <summary>
        /// Loads the active environment's id from file. 
        /// </summary>
        public virtual string LoadActiveEnvironmentIdFromFile(string path)
        {
#if UNITY_EDITOR
            // Editor shouldn't need to load the file at any point as the file is created at build-time
            return null;
#elif UNITY_2017_1_OR_NEWER
            string activeEnvId = Resources.Load<TextAsset>(path)?.text;
            if (activeEnvId == null)
            {
                // File should exist, so something has deleted the file? Or Unity has not included it in build?
                throw new FileNotFoundException("[Metaplay] Active Environment Config file not found!");
            }

            // Check that a config for the Id is found
            if (!EnvironmentExists(activeEnvId))
                throw new ArgumentException($"[Metaplay] Active Environment with Id: '{activeEnvId}' does not exist!");

            return activeEnvId;
#else
            // TODO: server should get its own environment info from somewhere
            return null;
#endif
        }

        /// <summary>
        /// Checks whether an environment with the given Id can be found. 
        /// </summary>
        public bool EnvironmentExists(string environmentId)
        {
            return TryGetEnvironmentConfig(environmentId) != null;
        }

        /// <summary>
        /// Creates default environments for new projects.
        /// </summary>
        protected virtual EnvironmentConfig[] CreateDefaultEnvironments()
        {
            return new EnvironmentConfig[]
            {
                new EnvironmentConfig
                {
                    Id = "offline",
                    Description = "",
                    ConnectionEndpointConfig = new ConnectionEndpointConfig() {
                        ServerHost             = null, // offline (using mock server)
                        ServerPort             = 0,
                        ServerPortForWebSocket = 0,
                        EnableTls              = false,
                        CdnBaseUrl             = null,
                        CommitIdCheckRule      = ClientServerCommitIdCheckRule.Disabled,
                        BackupGateways         = new List<ServerGatewaySpec>(),
                    },
                    ClientDebugConfig = new ClientDebugConfig()
                    {
                        EnablePlayerConsistencyChecks = true,
                        LogLevel                      = LogLevel.Verbose,
                    },
                    ClientGameConfigBuildApiConfig = new ClientGameConfigBuildApiConfig()
                    {
                        // ...
                    },
                },
                new EnvironmentConfig
                {
                    Id = "localhost",
                    Description = "",
                    ConnectionEndpointConfig = new ConnectionEndpointConfig() {
                        ServerHost             = "localhost",
                        ServerPort             = 9339,
                        ServerPortForWebSocket = 9380,
                        EnableTls              = false,
                        CdnBaseUrl             = "http://localhost:5552/",
                        CommitIdCheckRule      = ClientServerCommitIdCheckRule.Disabled,
                        BackupGateways         = new List<ServerGatewaySpec>(),
                    },
                    ClientDebugConfig = new ClientDebugConfig()
                    {
                        EnablePlayerConsistencyChecks = true,
                        LogLevel                      = LogLevel.Verbose,
                    },
                    ClientGameConfigBuildApiConfig = new ClientGameConfigBuildApiConfig()
                    {
#if UNITY_EDITOR
                        AdminApiBaseUrl         = "http://localhost:5550/api/",
                        GameConfigPublishPolicy = GameConfigPublishPolicy.Allowed,
#endif
                    },
                },
                // ...
            };
        }
    }
}
