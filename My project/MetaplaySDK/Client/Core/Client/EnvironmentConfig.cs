// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Json;
using Metaplay.Core.Message;
using Metaplay.Core.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using System.IO;
#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
#endif

namespace Metaplay.Core.Client
{
#if !UNITY_EDITOR // Dummy tooltip Attribute when outside Unity Editor
    internal class TooltipAttribute : Attribute
    {
        public TooltipAttribute(string tooltip) { }
    }
#endif

    [Serializable]
    public class ServerGatewaySpec
    {
        public string Id;

        [Tooltip("Empty and null default to the top level Server Host.")]
        public string ServerHost;
        public int ServerPort;

        public ServerGatewaySpec() { }

        public ServerGatewaySpec(string id, string serverHost, int serverPort)
        {
            Id = id;
            ServerHost = serverHost;
            ServerPort = serverPort;
        }

        public ServerGateway ToGateway(bool enableTls) => new ServerGateway(ServerHost, ServerPort, enableTls);
    }

    /// <summary>
    /// Configuration for a client-side deployment/environment.
    /// </summary>
    [Serializable]
    public class ConnectionEndpointConfig
    {
        [Tooltip("Hostname of the server, eg, 'localhost' or 'mygame-develop.p1.metaplay.io' (empty for offline mode)")]
        public string ServerHost;

        [Tooltip("Port number to use for connections (when using normal sockets, i.e. not WebGL with WebSockets)")]
        public int ServerPort;

        [Tooltip("Port number to use for connections when using WebGL with WebSockets")]
        public int ServerPortForWebSocket;

        [Tooltip("Enable TLS crypto for connection (should be enabled for all cloud deployments, disabled for localhost)")]
        public bool EnableTls;

        [Tooltip("Base URL to asset CDN (eg, 'http://localhost:5552/' or 'https://mygame-develop-assets.p1.metaplay.io/')")]
        public string CdnBaseUrl;

        [Tooltip(
            "Configure whether the server and client must be built from the same commit id for the connection to work.\n"
            + "For development environments, 'Only If Defined' is recommended to allow safe iteration of client and server without needing "
            + "to bump version numbers for each build (the commit id check prevents accidentally using mismatched client and server versions).\n"
            + "For locally running servers, disabling the checks is recommended.\n"
            + "For production and staging environments, the checks should also be disabled, and instead manual versioning be used.")]
        public ClientServerCommitIdCheckRule CommitIdCheckRule;

        [Tooltip("Alternative gateways to the server. These will be used if above server host cannot be connected to.")]
        public List<ServerGatewaySpec> BackupGateways;

        public bool IsOfflineMode => string.IsNullOrEmpty(ServerHost);

        public ServerEndpoint GetServerEndpoint()
        {
            int serverPort;
#if UNITY_WEBGL && !UNITY_EDITOR
            serverPort = ServerPortForWebSocket;
#else
            serverPort = ServerPort;
#endif

            // \note: EnableTLS value for backup gateways is not used, but for clarity the value from primary config is copied over anyway.
            return new ServerEndpoint(ServerHost, serverPort, EnableTls, CdnBaseUrl, BackupGateways.Select(x => x.ToGateway(enableTls: EnableTls)).ToList());
        }
    }

    [Serializable]
    public class LogLevelOverrideSpec
    {
        public string ChannelName;
        public LogLevel LogLevel = LogLevel.Debug;

        public LogLevelOverrideSpec() { }

        public LogLevelOverrideSpec(LogLevel logLevel, string channelName)
        {
            LogLevel = logLevel;
            ChannelName = channelName;
        }
    }

    [Serializable]
    public class ClientDebugConfig
    {
        [Tooltip("Enable additional consistency checks for PlayerModel updates (re-run ticks/action, check for modifications outside Ticks/Actions, etc.)")]
        public bool EnablePlayerConsistencyChecks = false;
        [Tooltip("Determines the granularity of checksum computation, and lowers the quality of checksum mismatch report. Any level other than PerOperation cannot resolve the exact failure point.")]
        public ChecksumGranularity PlayerChecksumGranularity = ChecksumGranularity.PerActionSingleTickPerFrame;

        [Tooltip("Default log level to use for in-game logging")]
        public LogLevel LogLevel = LogLevel.Debug;

        [Tooltip("Log level overrides per log channel")]
        public List<LogLevelOverrideSpec> LogLevelOverrides = new List<LogLevelOverrideSpec>();
    }

    /// <summary>
    /// Policy for publishing game configs from editor
    /// </summary>
    [Serializable]
    public enum GameConfigPublishPolicy
    {
        NotAllowed,         // No GameConfig publishing or uploading allowed
        UploadOnly,         // Can upload new GameConfigs but publish must be done in dashboard
        AskForConfirmation, // Show confirm dialog before publishing
        Allowed             // Ok to publish without confirmation
    }

    /// <summary>
    /// Admin API authentication token source
    /// </summary>
    [Serializable]
    public enum AuthenticationTokenProvider
    {
        None,
        Google,
        OAuth2,
    }

#if UNITY_EDITOR
    [Serializable]
    public class OAuth2AuthorizationServerSpec
    {
        /// <inheritdoc cref="MetaplayOAuth2Client.IAuthorizationServerConfigOverride.ClientId"/>
        public string ClientId;

        /// <inheritdoc cref="MetaplayOAuth2Client.IAuthorizationServerConfigOverride.AuthorizationEndpoint"/>
        [Tooltip("(Editor only) OAuth2 Authorization server that is used for authorizing access to the game server.")]
        public string AuthorizationEndpoint;

        /// <inheritdoc cref="MetaplayOAuth2Client.IAuthorizationServerConfigOverride.TokenEndpoint"/>
        public string TokenEndpoint;

        /// <inheritdoc cref="MetaplayOAuth2Client.IAuthorizationServerConfigOverride.Audience"/>
        public string Audience;

        /// <inheritdoc cref="MetaplayOAuth2Client.IAuthorizationServerConfigOverride.LocalCallbackUrls"/>
        [Tooltip("Optional")] public List<string> LocalCallbackUrls;
    }
#endif

    [Serializable]
    public class ClientGameConfigBuildApiConfig
    {
#if UNITY_EDITOR
        [Tooltip("(Editor only) Base URL to Admin API (eg, 'http://localhost:5550/api/' or 'https://mygame-develop-api.p1.metaplay.io/api/')")]
        public string AdminApiBaseUrl;

        [Tooltip("(Editor only) Policy for GameConfig publishing from editor")]
        public GameConfigPublishPolicy GameConfigPublishPolicy;

        [Tooltip("(Editor only) Authentication token provider service for Admin API access")]
        public AuthenticationTokenProvider AdminApiAuthenticationTokenProvider;

        [Tooltip("(Editor only) Path to file containing service account credentials for authorizing access to the game server")]
        public string AdminApiCredentialsPath;

        [Tooltip("(Editor only) OAuth2 Authorization server that is used for authorizing access to the game server.")]
        public OAuth2AuthorizationServerSpec AdminApiOAuth2Settings;
#endif
    }

    /// <summary>
    /// Specifications for an environment identified by <see cref="Id"/>. Contains Metaplay specific environment specs.
    /// Inherit this to extend the contained specs and override relevant members in your custom Environment Config Provider
    /// inheriting from <see cref="EnvironmentConfigProvider"/>.
    /// </summary>
    [Serializable]
    public class EnvironmentConfig : IMetaIntegrationConstructible<EnvironmentConfig>
    {
        [Tooltip("Unique identifier for this environment, such as 'development' or 'feature-foo'")]
        public string Id;
        [Tooltip("Optional description of the environment to provide developers with extra information")]
        public string Description;
        public ConnectionEndpointConfig       ConnectionEndpointConfig;
        public ClientDebugConfig              ClientDebugConfig;
        public ClientGameConfigBuildApiConfig ClientGameConfigBuildApiConfig;

        /// <summary>
        /// Creates a filtered copy of the EnvironmentConfig. See <see cref="FilterForClientBuild"/>. 
        /// </summary>
        /// <returns></returns>
        public virtual EnvironmentConfig CreateClientBuildCopy()
        {
            string            jsonObj               = JsonConvert.SerializeObject(this);
            Type              environmentConfigType = IntegrationRegistry.GetSingleIntegrationType(typeof(EnvironmentConfig));
            EnvironmentConfig clientBuildCopy       = (EnvironmentConfig)JsonConvert.DeserializeObject(jsonObj, environmentConfigType);

            clientBuildCopy.FilterForClientBuild();
            
            return clientBuildCopy;
        }

        /// <summary>
        /// Filter out any information from the environment config that we don't want to include in client builds.
        /// Override this to filter out custom extended information. 
        /// </summary>
        protected virtual void FilterForClientBuild()
        {
            Description = "";
            ClientGameConfigBuildApiConfig = new ClientGameConfigBuildApiConfig();
        }
    }

    /// <summary>
    /// Base API for an Environment Config Provider. Inherit to extend or customize config source.
    /// See <see cref="DefaultEnvironmentConfigProvider"/>.
    /// </summary>
    public interface IEnvironmentConfigProvider : IMetaIntegrationSingleton<IEnvironmentConfigProvider>
    {
        public static void Initialize() => IntegrationRegistry.Get<IEnvironmentConfigProvider>().InitializeSingleton();
        public static EnvironmentConfig Get() => IntegrationRegistry.Get<IEnvironmentConfigProvider>().GetCurrent();

        /// <summary>
        /// Used to initialize the environment config provider, for example to load configs from files. 
        /// </summary>
        public void InitializeSingleton();

        /// <summary>
        /// Returns the currently active environment config.
        /// </summary>
        public EnvironmentConfig GetCurrent();
    }
}
