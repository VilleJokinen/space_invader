// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Application;
using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Localization;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using NUnit.Framework;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

[assembly: Parallelizable(ParallelScope.Fixtures)]

// \note In global namespace to make sure it covers all the game-specific tests, too
[SetUpFixture]
[SuppressMessage("Microsoft.Design", "CA1050:DeclareTypesInNamespaces", Scope = "type", Target = "OutputWriter")]
public class TestSetUp
{
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
        public override EntityId PlayerId { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
        public override string PlayerName { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
        public override int PlayerLevel { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }

        protected override void GameInitializeNewPlayerModel(MetaTime now, ISharedGameConfig gameConfig, EntityId playerId, string name)
        {
            throw new System.NotImplementedException();
        }

        protected override int GetTicksPerSecond()
        {
            throw new System.NotImplementedException();
        }
    }

    [OneTimeSetUp]
    public void SetUp()
    {
        // Initialize for tests & set working directory to project root.
        TestHelper.SetupForTests();
    }
}
