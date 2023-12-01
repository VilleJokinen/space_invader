// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Activables;
using Metaplay.Core.Analytics;
using Metaplay.Core.Client;
using Metaplay.Core.Config;
using Metaplay.Core.Localization;
using Metaplay.Core.Math;
using Metaplay.Core.Message;
using Metaplay.Core.Model;
using Metaplay.Core.Profiling;
using Metaplay.Core.Serialization;
using Metaplay.Core.Tasks;
using Metaplay.Core.Web3;
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using static System.FormattableString;

[assembly: InternalsVisibleTo("Metaplay.Cloud.Tests")]
[assembly: InternalsVisibleTo("Metaplay.Cloud.Serialization.Compilation.Tests")]
[assembly: InternalsVisibleTo("Cloud.Tests")]

namespace Metaplay.Core
{
    /// <summary>
    /// Flags to specify which optional Metaplay SDK features should be enabled in your project.
    /// </summary>
    public class MetaplayFeatureFlags
    {
        /// <summary>
        /// Enable Metaplay-powered localizations in the game.
        /// </summary>
        public bool EnableLocalizations { get; set; }

#if !METAPLAY_DISABLE_GUILDS
        /// <summary>
        /// Enable Metaplay-powered guilds in the game.
        /// </summary>
        public bool EnableGuilds { get; set; }
#endif

        /// <summary>
        /// Enable Metaplay's Web3 features in the game.
        /// </summary>
        public bool EnableWeb3 { get; set; }

        /// <summary>
        /// Enable building ImmutableX Link library on Client. Only applies to WebGL builds.
        /// </summary>
        public bool EnableImmutableXLinkClientLibrary { get; set; }

        /// <summary>
        /// Enable player leagues system in the game.
        /// </summary>
        public bool EnablePlayerLeagues { get; set; }

        /// <summary>
        /// Enable Company Account support on Client.
        /// <para>
        /// Logging in into a Company Account requires the use of a platform native browser.
        /// </para>
        /// <para>
        /// On iOS, enabling this injects <c>AuthenticationServices.framework</c> dependency automatically into the app. This dependency brings in ASWebAuthenticationSession
        /// which allows opening a in-app browser for authentication purposes. Company Account client integration then uses it automatically.
        /// </para>
        /// <para>
        /// On Android, we want to inject <c>androidx.browser:browser</c> dependency for an in-app browser but we cannot do it directly. Instead, in addition to this flag, you
        /// need to add io.metaplay.unitysdk.androidcustomtabs (MetaplaySDK/Plugins/AndroidCustomTabs/Package) package into the Unity project using Unity's Package Manager. See
        /// MetaplaySDK/Plugins/AndroidCustomTabs/README.md for details.
        /// </para>
        /// </summary>
        public bool EnableCompanyAccountClientIntegrations { get; set; }

#if METAPLAY_LEGACY_GAMECONFIG_PIPELINE
        /// <summary>
        /// Use legacy (pre-R25) config parsing pipeline by default for all config content.
        /// This is a temporary fallback solution in case the new pipeline (with <see cref="GameConfigSyntaxAdapterAttribute"/>)
        /// doesn't work from the get-go.
        /// Note:
        /// - When this is false, the legacy pipeline can still be enabled per-library
        ///   using the <see cref="GameConfigUseLegacyParsingPipelineAttribute"/>.
        /// - When this is true, the new pipeline can still be enabled per-library
        ///   by specifying the syntax adapter with the <see cref="GameConfigSyntaxAdapterAttribute"/>.
        /// </summary>
        public bool UseLegacyConfigParsingPipelineByDefault { get; set; }
#endif
    }

    public class MissingIntegrationException : Exception
    {
        public MissingIntegrationException(string msg): base(msg) { }
    }

    /// <summary>
    /// Game-specific constant options for core Metaplay SDK.
    /// </summary>
    public class MetaplayCoreOptions
    {
        public static readonly Regex ProjectNameRegex = new Regex(@"^[a-zA-Z0-9_]+$"); // Allow characters, numbers, and underscores.

        /// <summary>
        /// Technical name of the project. Must only contain letters, numbers, and underscores.
        /// </summary>
        public readonly string ProjectName;

        /// <summary>
        /// Magic identifier to ensure that only client and server of the same game talk to each other.
        /// Must be exactly 4 characters long.
        /// </summary>
        public readonly string GameMagic;

        /// <summary>
        /// Supported range of LogicVersions (inclusive min and max) for this build. Applies to both client
        /// and server.
        /// </summary>
        public readonly MetaVersionRange SupportedLogicVersions;

        /// <summary>
        /// Public salt used for guild invite code. Game-specific. Not a secret. Magic value to early detect
        /// and reject invalid invite codes.
        /// </summary>
        public readonly byte GuildInviteCodeSalt;

        /// <summary>
        /// List of all namespaces which are shared between the client and the server (and therefore part
        /// of the protocol between client and server).
        /// </summary>
        public readonly string[] SharedNamespaces;

        /// <summary>
        /// Default language of the application.
        /// </summary>
        public readonly LanguageId DefaultLanguage;

        /// <summary>
        /// Flags for enabling/disabling optional Metaplay SDK features.
        /// </summary>
        public readonly MetaplayFeatureFlags FeatureFlags;

        public MetaplayCoreOptions(string projectName, string gameMagic, MetaVersionRange supportedLogicVersions, byte guildInviteCodeSalt, string[] sharedNamespaces, LanguageId defaultLanguage, MetaplayFeatureFlags featureFlags)
        {
            if (projectName == null)
                throw new ArgumentNullException(nameof(projectName));
            if (projectName == "")
                throw new ArgumentException("ProjectName must be non-empty!", nameof(projectName));
            if (!ProjectNameRegex.IsMatch(projectName))
                throw new ArgumentException("ProjectName must only contain letters, numbers, and underscores!", nameof(projectName));

            if (gameMagic == null)
                throw new ArgumentNullException(nameof(gameMagic));
            if (gameMagic.Length != 4)
                throw new ArgumentException("GameMagic must be exactly 4 characters long", nameof(gameMagic));

            if (defaultLanguage == null)
            {
                if (FeatureFlags.EnableLocalizations)
                    throw new ArgumentException("Default language must be set when Localizations are enabled.", nameof(defaultLanguage));
                else
                    throw new ArgumentException("Default language must be set. For example LanguageId.FromString(\"en\").", nameof(defaultLanguage));
            }

            ProjectName = projectName;
            GameMagic = gameMagic;
            SupportedLogicVersions = supportedLogicVersions ?? throw new ArgumentNullException(nameof(supportedLogicVersions));
            GuildInviteCodeSalt = guildInviteCodeSalt;
            SharedNamespaces = sharedNamespaces ?? throw new ArgumentNullException(nameof(sharedNamespaces));
            DefaultLanguage = defaultLanguage;
            FeatureFlags = featureFlags;
        }
    }

    /// <summary>
    /// The game integration API for providing Metaplay core options. An implementation of this interface
    /// is expected to be found in the shared game code for providing mandatory Metaplay SDK options.
    /// </summary>
    public interface IMetaplayCoreOptionsProvider : IMetaIntegrationSingleton<IMetaplayCoreOptionsProvider>
    {
        MetaplayCoreOptions Options { get; }
    }

    public static class MetaplayCore
    {
        public static bool IsInitialized => _options != null;

        static MetaplayCoreOptions _options = null;
        public static MetaplayCoreOptions Options => _options ?? throw new InvalidOperationException($"{nameof(MetaplayCore)}.{nameof(Initialize)} must be called before accessing {nameof(MetaplayCore)}.{nameof(Options)}");

        /// <summary>
        /// The current login protocol version for Metaplay SDK.
        /// Applies to both client and server.
        /// </summary>
        public const int LoginProtocolVersion = 2;

        internal static void OverrideOptions(MetaplayCoreOptions options) => _options = options;

        public static void Initialize()
        {
            Initialize(overrideTypeInfoProvider: null);
        }

        internal static void Initialize(IMetaSerializerTypeInfoProvider overrideTypeInfoProvider)
        {
            // Avoid duplicate initialization
            if (IsInitialized)
                return;

            using (ProfilerScope.Create("MetaplayCore.Initialize()"))
            {
                // Initialize integrations, accept only Metaplay API
                using (ProfilerScope.Create("MetaplayCore.Initialize.IntegrationRegistry"))
                    IntegrationRegistry.Init(type => type.Namespace?.StartsWith("Metaplay.", StringComparison.Ordinal) ?? false);

                try
                {
                    _options = IntegrationRegistry.Get<IMetaplayCoreOptionsProvider>().Options;
                }
                catch (InvalidOperationException)
                {
                    // Special treatment for missing IMetaplayCoreOptionsProvider: throw MissingIntegrationException to indicate that
                    // game-side integration is missing completely.
                    throw new MissingIntegrationException($"No implementation of {nameof(IMetaplayCoreOptionsProvider)} found. " +
                        $"This usually means that the Metaplay integration to the game is missing or undiscoverable.");
                }
                if (IntegrationRegistry.MissingIntegrationTypes().Any())
                {
                    throw new InvalidOperationException("Game must provide concrete implementations of integration types: " +
                        $"{string.Join(", ", IntegrationRegistry.MissingIntegrationTypes().Select(x => x.Name))}");
                }

                // Initialize MetaTask
                MetaTask.Initialize();

                // Register PrettyPrinter for various types
                PrettyPrinter.RegisterFormatter<IntVector2>((v, isCompact) => Invariant($"({v.X}, {v.Y})"));
                PrettyPrinter.RegisterFormatter<IntVector3>((v, isCompact) => Invariant($"({v.X}, {v.Y}, {v.Z})"));

                using (ProfilerScope.Create("MetaplayCore.Initialize.Entities"))
                {
                    EntityKindRegistry.Initialize();
                    SchemaMigrationRegistry.Initialize();
                }

                using (ProfilerScope.Create("MetaplayCore.Initialize.GameConfigRepository"))
                    GameConfigRepository.InitializeSingleton();

                // Register default types to csv reader
                using (ProfilerScope.Create("MetaplayCore.Initialize.ConfigParser"))
                {
                    ConfigParser.RegisterBasicTypeParsers();
                    ConfigParser.RegisterCustomParsers();
                    ConfigParser.RegisterGameConfigs();
                }

                using (ProfilerScope.Create("MetaplayCore.Initialize.NftTypeRegistry"))
                    NftTypeRegistry.Initialize();

                // \note do this after the registrations above to make sure CloudCore module is loaded
                using (ProfilerScope.Create("MetaplayCore.Initialize.MetaSerializerTypeRegistry"))
                {
                    if(overrideTypeInfoProvider != null)
                        MetaSerializerTypeRegistry.OverrideTypeScanner(overrideTypeInfoProvider);
                    else
                        MetaSerializerTypeRegistry.Initialize();
                }

                using (ProfilerScope.Create("MetaplayCore.Initialize.ModelActionRepository"))
                    ModelActionRepository.Initialize();

                using (ProfilerScope.Create("MetaplayCore.Initialize.MetaMessageRepository"))
                    MetaMessageRepository.Initialize();

                using (ProfilerScope.Create("MetaplayCore.Initialize.MetaActivableRepository"))
                    MetaActivableRepository.InitializeSingleton();

                using (ProfilerScope.Create("MetaplayCore.Initialize.AnalyticsEventRegistry"))
                    AnalyticsEventRegistry.Initialize();

                using (ProfilerScope.Create("MetaplayCore.Initialize.FirebaseAnalyticsFormatter"))
                    FirebaseAnalyticsFormatter.Initialize();

                using (ProfilerScope.Create("MetaplayCore.Initialize.EnvironmentConfigProvider"))
                    IEnvironmentConfigProvider.Initialize();
            }
        }
    }
}
