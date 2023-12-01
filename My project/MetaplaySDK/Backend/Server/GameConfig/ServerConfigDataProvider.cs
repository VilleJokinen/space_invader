// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Cloud.Services;
using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Localization;
using System;

namespace Metaplay.Server.GameConfig
{
    public class ServerConfigDataProvider
    {
        public IBlobStorage                  PublicBlobStorage;
        public LocalizationLanguageProvider  LocalizationLanguageProvider;

        public static ServerConfigDataProvider Instance;

        ServerConfigDataProvider()
        {
            BlobStorageOptions blobStorageOpts = RuntimeOptionsRegistry.Instance.GetCurrent<BlobStorageOptions>();

            // Setup public blob storage for game config data
            PublicBlobStorage = blobStorageOpts.CreatePublicBlobStorage("GameConfig");

            // Setup localization languages server-side provider (for accessing localization data from server code)
            IBlobProvider gameDataProvider = new StorageBlobProvider(PublicBlobStorage);
            LocalizationLanguageProvider = new LocalizationLanguageProvider(gameDataProvider, "Localizations");
        }

        public static void Initialize()
        {
            if (Instance != null)
                throw new InvalidOperationException("Double initialization of ServerConfigDataProvider");
            Instance = new ServerConfigDataProvider();
        }
    }
}
