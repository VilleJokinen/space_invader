// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Google.Apis.AndroidPublisher.v3;
using Google.Apis.Services;
using Metaplay.Cloud.RuntimeOptions;
using System;

namespace Metaplay.Server.Services
{
    /// <summary>
    /// Utility class to cache the Android Publisher API service authentication
    /// </summary>
    public static class AndroidPublisherServiceSingleton
    {
        static AndroidPublisherService _instance = null;

        public static AndroidPublisherService Instance => _instance ?? throw new InvalidOperationException(
            $"{nameof(AndroidPublisherServiceSingleton)} isn't initialized. "
            + $"Make sure you've set runtime option GooglePlayStore:{nameof(GooglePlayStoreOptions.EnableAndroidPublisherApi)} to true, "
            + $"and GooglePlayStore:{nameof(GooglePlayStoreOptions.AndroidPublisherApiServiceAccountPath)} to the service account credentials path.");

        public static void Initialize()
        {
            if (_instance != null)
                throw new InvalidOperationException("Already initialized");

            GooglePlayStoreOptions storeOpts = RuntimeOptionsRegistry.Instance.GetCurrent<GooglePlayStoreOptions>();

            if (storeOpts.EnableAndroidPublisherApi)
            {
                // Create API service singleton
                _instance = new AndroidPublisherService(new BaseClientService.Initializer()
                {
                    HttpClientInitializer = storeOpts.AndroidPublisherApiServiceAccountCredentials,
                    ApplicationName = "Game server",
                });
            }
        }
    }
}
