using System.Globalization;
using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Localization;
using Metaplay.Core.Math;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Tasks;
using Metaplay.Core.Web3;
using NUnit.Framework;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;
using System;
using System.Linq;
using static System.FormattableString;

[assembly: Parallelizable(ParallelScope.Fixtures)]

namespace Metaplay.Cloud.Tests
{
    // Needs to be in a Metaplay namespace for discoverability
    class TestOptions : IMetaplayCoreOptionsProvider
    {
        public MetaplayCoreOptions Options => new MetaplayCoreOptions(
            projectName: "Metaplay_Cloud_Tests",
            gameMagic: "TEST",
            supportedLogicVersions: new MetaVersionRange(1, 1),
            guildInviteCodeSalt: 0x17,
            sharedNamespaces: new string[] {},
            defaultLanguage: LanguageId.FromString("default-lang"),
            featureFlags: new MetaplayFeatureFlags
            {
                EnableLocalizations = true,
#if !METAPLAY_DISABLE_GUILDS
                EnableGuilds = true,
#endif
            });
    }

    [SupportedSchemaVersions(1, 1)]
    [MetaSerializableDerived(1)]
    public class TestPlayerModel : PlayerModelBase<TestPlayerModel, PlayerStatisticsCore>
    {
        public override EntityId PlayerId    { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
        public override string   PlayerName  { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
        public override int      PlayerLevel { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }

        protected override void GameInitializeNewPlayerModel(MetaTime now, ISharedGameConfig gameConfig, EntityId playerId, string name)
        {
            throw new System.NotImplementedException();
        }

        protected override int GetTicksPerSecond()
        {
            throw new System.NotImplementedException();
        }
    }
}

namespace Cloud.Serialization.Compilation.Tests
{
    [SetUpFixture]
    public class SetUpFixture
    {
        [OneTimeSetUp]
        public void SetUp()
        {
            // Instead of using TestHelper.SetupForTests, we're doing a partial setup so that we can configure MetaSerializerTypeRegistry to only serialize the type we are trying to test.

            // Pipe serilog to Console. This will be then captured by the test framework.
            Serilog.Log.Logger =
                new LoggerConfiguration()
                    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture, theme: ConsoleTheme.None)
                    .CreateLogger();

            // Initialize integrations, accept only Metaplay API
            IntegrationRegistry.Init(type => type.Namespace?.StartsWith("Metaplay.", StringComparison.Ordinal) ?? false);

            try
            {
                MetaplayCoreOptions options = IntegrationRegistry.Get<IMetaplayCoreOptionsProvider>().Options;
                MetaplayCore.OverrideOptions(options);
            }
            catch (InvalidOperationException)
            {
                // Special treatment for missing IMetaplayCoreOptionsProvider: throw MissingIntegrationException to indicate that
                // game-side integration is missing completely.
                throw new MissingIntegrationException(
                    $"No implementation of {nameof(IMetaplayCoreOptionsProvider)} found. " +
                    $"This usually means that the Metaplay integration to the game is missing or undiscoverable.");
            }

            if (IntegrationRegistry.MissingIntegrationTypes().Any())
            {
                throw new InvalidOperationException(
                    "Game must provide concrete implementations of integration types: " +
                    $"{string.Join(", ", IntegrationRegistry.MissingIntegrationTypes().Select(x => x.Name))}");
            }

            // Initialize MetaTask
            MetaTask.Initialize();

            // Register PrettyPrinter for various types
            PrettyPrinter.RegisterFormatter<IntVector2>((v, isCompact) => Invariant($"({v.X}, {v.Y})"));
            PrettyPrinter.RegisterFormatter<IntVector3>((v, isCompact) => Invariant($"({v.X}, {v.Y}, {v.Z})"));

            EntityKindRegistry.Initialize();
            SchemaMigrationRegistry.Initialize();

            GameConfigRepository.InitializeSingleton();

            // Register default types to csv reader
            ConfigParser.RegisterBasicTypeParsers();
            ConfigParser.RegisterCustomParsers();
            ConfigParser.RegisterGameConfigs();

            NftTypeRegistry.Initialize();
        }
    }
}
