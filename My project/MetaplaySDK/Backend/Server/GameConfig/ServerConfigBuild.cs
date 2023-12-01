// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Application;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Cloud.Utility;
using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Model;
using Metaplay.Server.Database;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Metaplay.Server.GameConfig
{
    public class ServerConfigBuildInfo
    {
        public ServerConfigBuildInfo()
        {
            GameConfigBuildIntegration integration = IntegrationRegistry.Get<GameConfigBuildIntegration>();

            GameConfigBuildParametersType = integration.GetDefaultBuildParametersType();
            if (GameConfigBuildParametersType == null)
                return;

            GameConfigBuildParametersNamespaceQualifiedName = GameConfigBuildParametersType.ToNamespaceQualifiedTypeString();

            SlotToAvailableSourcesMapping = IntegrationRegistry.Get<GameConfigBuildIntegration>()
                .GetAllAvailableBuildSources(GameConfigBuildParametersType)
                .ToOrderedDictionary(x => x.SourceProperty, x => x.Sources.ToList());

            GameConfigBuildSupported = SlotToAvailableSourcesMapping.Any();
        }

        // Whether this server supports building game configs (via SystemGameConfigController endpoint)
        public bool GameConfigBuildSupported { get; }

        // Game-specific game config build parameters type.
        public Type GameConfigBuildParametersType { get; }
        public string GameConfigBuildParametersNamespaceQualifiedName { get; }
        public OrderedDictionary<string, List<GameConfigBuildSource>> SlotToAvailableSourcesMapping { get; }
    }

    [MetaSerializableDerived(1)]
    public class BuildStaticGameConfigTask : BackgroundTask
    {
        [MetaSerializableDerived(1)]
        class Progress : IBackgroundTaskOutput
        {
            [MetaMember(1)] public string CurrentOperation { get; private set; }

            public Progress() { }

            public Progress(string currentOp)
            {
                CurrentOperation = currentOp;
            }
        }

        [MetaMember(1)] public MetaGuid GameConfigId { get; private set; }
        [MetaMember(2)] public MetaGuid ParentConfigId { get; private set; }

        [MetaMember(3)] public GameConfigBuildParameters BuildParams { get; private set; }

        public BuildStaticGameConfigTask() { }

        public BuildStaticGameConfigTask(MetaGuid id, MetaGuid parentId, GameConfigBuildParameters buildParams)
        {
            GameConfigId   = id;
            ParentConfigId = parentId;
            BuildParams    = buildParams;
        }

        IGameConfigSourceFetcherConfig FetcherConfig()
        {
            GameConfigSourceFetcherConfigCore ret = GameConfigSourceFetcherConfigCore.Create();

            // Google sheets
            GoogleSheetOptions opts            = RuntimeOptionsRegistry.Instance.GetCurrent<GoogleSheetOptions>();
            string             credentialsJson = opts.CredentialsJson;
            if (credentialsJson != null)
                ret = ret.WithGoogleCredentialsJson(credentialsJson);

            return ret;
        }

        async Task RunInternal(BackgroundTaskContext context)
        {
            ConfigArchive parent         = null;
            MetaGuid      parentConfigId = MetaGuid.None;

            if (BuildParams.IsIncremental)
            {
                parentConfigId = ParentConfigId;
                if (!parentConfigId.IsValid)
                {
                    // Default to using current active
                    GlobalStatusResponse status = await context.AskEntityAsync<GlobalStatusResponse>(GlobalStateManager.EntityId, GlobalStatusRequest.Instance);
                    parentConfigId = status.ActiveStaticGameConfigId;
                    if (!parentConfigId.IsValid)
                        throw new ArgumentException("No ParentConfigVersion given and no current active GameConfig version");
                }

                context.UpdateTaskOutput(new Progress("Loading parent config"));
                MetaDatabase              db              = MetaDatabase.Get(QueryPriority.Normal);
                PersistedStaticGameConfig persistedParent = await db.TryGetAsync<PersistedStaticGameConfig>(parentConfigId.ToString());
                parent = ConfigArchive.FromBytes(persistedParent.ArchiveBytes);
            }

            // Build the static full game config
            context.UpdateTaskOutput(new Progress("Building GameConfig archive"));
            // \note Memory-expensive debug check is only enabled in local servers.
            //       It checks for code bugs, not config content bugs (though whether the bug triggers can
            //       depend on config content), so those bugs are likely to be caught at development time.
            GameConfigBuildDebugOptions debugOpts = new GameConfigBuildDebugOptions() { EnableDebugDumpCheck = RuntimeOptionsBase.IsLocalEnvironment };
            ConfigArchive               archive   = await StaticFullGameConfigBuilder.BuildArchiveAsync(MetaTime.Now, parentConfigId, parent, BuildParams, FetcherConfig(), debugOpts);

            // Upload the built archive and optionally publish
            context.UpdateTaskOutput(new Progress("Persisting GameConfig archive"));

            byte[] bytes = ConfigArchiveBuildUtility.ToBytes(archive, CompressionAlgorithm.None, 0);
            await context.AskEntityAsync<CreateOrUpdateGameDataResponse>(
                GlobalStateManager.EntityId,
                new CreateOrUpdateGameConfigRequest()
                {
                    Id      = GameConfigId,
                    Content = bytes
                });
        }

        public override async Task<IBackgroundTaskOutput> Run(BackgroundTaskContext context)
        {
            try
            {
                await RunInternal(context);
            }
            catch (Exception ex)
            {
                await context.AskEntityAsync<CreateOrUpdateGameDataResponse>(GlobalStateManager.EntityId,
                    new CreateOrUpdateGameConfigRequest()
                    {
                        Id          = GameConfigId,
                        FailureInfo = ex.ToString()
                    });
            }

            return new Progress("Done");
        }
    }
}
