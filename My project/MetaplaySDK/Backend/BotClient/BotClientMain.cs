// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud;
using Metaplay.Cloud.Application;
using Metaplay.Cloud.Cluster;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Localization;
using Metaplay.Core.Network;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Metaplay.BotClient
{
    // BotClientMain

    public class BotClientMain : Application
    {
        public BotClientMain()
        {
        }

        public Task<int> RunBotsAsync(string[] cmdLineArgs) => RunApplicationMainAsync(applicationSymbolicName: "BotClient", cmdLineArgs);

        protected override List<IClusterService> InitClusterServices()
        {
            return new List<IClusterService>
            {
                // Local service: register globals, etc.
                new ClusterServiceLocal("LocalInit", StartLocalService, StopLocalService),

                // EntityShards
                CreateEntityShard(EntityKindBotCore.BotClient),
            };
        }

        void InitializeGameConfigProviders()
        {
            BotOptions botOpts = RuntimeOptionsRegistry.Instance.GetCurrent<BotOptions>();
            MetaDebug.Assert(!string.IsNullOrEmpty(botOpts.GameConfigCachePath), "Must specify valid BotClientConfig.GameConfigCachePath");

            // Setup http-based GameConfigProvider with on-disk caching
            string cacheDirectory = botOpts.GameConfigCachePath;
            DiskBlobStorage cacheStorage = new DiskBlobStorage(cacheDirectory);
            MetaplayCdnAddress cdnAddress = MetaplayCdnAddress.Create(botOpts.CdnBaseUrl, prioritizeIPv4: true).GetSubdirectoryAddress("GameConfig");
            HttpBlobProvider httpProvider = new HttpBlobProvider(MetaHttpClient.DefaultInstance, cdnAddress);
            StorageBlobProvider cacheProvider = new StorageBlobProvider(cacheStorage);
            CachingBlobProvider cachingProvider = new CachingBlobProvider(httpProvider, cacheProvider);

            BotGameConfigProvider.Instance.Initialize(botOpts.GameConfigCachePath, cdnAddress);
            BotLocalizationLanguageProvider.Instance = new LocalizationLanguageProvider(cachingProvider, "Localizations");
        }

        Task StartLocalService()
        {
            // Setup GameConfig providers
            InitializeGameConfigProviders();

            return Task.CompletedTask;
        }

        Task StopLocalService()
        {
            _logger.Information("BotClient stopped");

            return Task.CompletedTask;
        }

        protected override void HandleKeyPress(ConsoleKeyInfo key)
        {
            BotOptions botOpts = RuntimeOptionsRegistry.Instance.GetCurrent<BotOptions>();
            switch (key.Key)
            {
                case ConsoleKey.OemPlus:
                    botOpts.MaxBots = 2 * botOpts.MaxBots;
                    Console.WriteLine("MaxBots = {0}", botOpts.MaxBots);
                    break;

                case ConsoleKey.OemMinus:
                    botOpts.MaxBots = Math.Max(1, botOpts.MaxBots / 2);
                    Console.WriteLine("MaxBots = {0}", botOpts.MaxBots);
                    break;
            }
        }
    }
}
