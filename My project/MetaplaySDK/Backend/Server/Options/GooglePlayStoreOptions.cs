// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Google.Apis.AndroidPublisher.v3;
using Google.Apis.Auth.OAuth2;
using Metaplay.Cloud.Crypto;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Cloud.Utility;
using Metaplay.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Metaplay.Server
{
    [RuntimeOptions("GooglePlayStore", isStatic: true, "Configuration options for Google Play Games app distribution.")]
    public class GooglePlayStoreOptions : RuntimeOptionsBase
    {
        [MetaDescription("The application's \"package name\" on the Android platform.")]
        public string       AndroidPackageName                      { get; private set; } = null;
        [MetaDescription("The application's Base64-encoded public key for Google Play. This is available in the Google Play console.")]
        public string       GooglePlayPublicKey                     { get; private set; } = null;
        [MetaDescription("Enables authentication using Google Play Games and Google Sign In.")]
        public bool         EnableGoogleAuthentication              { get; private set; } = false;
        [MetaDescription("The Google Play OAuth2 client credentials JSON path. This web client JSON is available for download at [Google Cloud Console](https://console.developers.google.com/apis/credentials).")]
        public string       GooglePlayOAuth2ClientCredentialsPath   { get; private set; } = null;
        [MetaDescription("The Google Play Application ID from the Google Play developer console. On Configuration page, it is listed as the \"Project ID\".")]
        public string       GooglePlayApplicationId                  { get; private set; } = null;

        [MetaDescription("Enables the game server to access the Google Play Publisher API. This is used for IAP refunds, subscription IAP validation, and detection of test purchases.")]
        public bool         EnableAndroidPublisherApi               { get; private set; } = false;
        [MetaDescription("The path to the Google Play Publisher service account certificate file. See [Google Cloud Console](https://developers.google.com/android-publisher/getting_started#using_a_service_account) for more details.")]
        public string       AndroidPublisherApiServiceAccountPath   { get; private set; } = null;
        [MetaDescription("Enables the LiveOps Dashboard controls to request refunds for Google Play purchases via the Google Play Publisher API.")]
        public bool         EnableGooglePlayInAppPurchaseRefunds    { get; private set; } = false;

        [ComputedValue]
        [MetaDescription("The Google Play OAuth2 client ID. Resolved from GooglePlayOAuth2ClientCredentialsPath.")]
        public string       GooglePlayClientId                      { get; private set; } = null;
        [ComputedValue]
        [Sensitive]
        [MetaDescription("The Google Play OAuth2 client secret. Resolved from GooglePlayOAuth2ClientCredentialsPath.")]
        public string       GooglePlayClientSecret                  { get; private set; } = null;

        [IgnoreDataMember]
        public GoogleCredential AndroidPublisherApiServiceAccountCredentials { get; private set; } = null;

        public override async Task OnLoadedAsync()
        {
            if (EnableAndroidPublisherApi)
            {
                if (string.IsNullOrEmpty(AndroidPublisherApiServiceAccountPath))
                    throw new InvalidOperationException($"GooglePlayStore:{nameof(AndroidPublisherApiServiceAccountPath)} must be set if {nameof(EnableAndroidPublisherApi)} is true");
                if (string.IsNullOrEmpty(AndroidPackageName))
                    throw new InvalidOperationException($"GooglePlayStore:{nameof(AndroidPackageName)} must be set if {nameof(EnableAndroidPublisherApi)} is true");

                string credentialsJson = await SecretUtil.ResolveSecretAsync(Log, AndroidPublisherApiServiceAccountPath);
                ResolveAndroidPublisherApiServiceAccountCredentials(credentialsJson);
            }

            if (EnableGoogleAuthentication)
            {
                if (string.IsNullOrEmpty(GooglePlayOAuth2ClientCredentialsPath))
                    throw new InvalidOperationException($"GooglePlayStore:{nameof(GooglePlayOAuth2ClientCredentialsPath)} must be set if {nameof(EnableGoogleAuthentication)} is true");
                if (string.IsNullOrEmpty(GooglePlayApplicationId))
                    throw new InvalidOperationException($"GooglePlayStore:{nameof(GooglePlayApplicationId)} must be set if {nameof(EnableGoogleAuthentication)} is true");

                string credentialsJson = await SecretUtil.ResolveSecretAsync(Log, GooglePlayOAuth2ClientCredentialsPath);
                ResolveGooglePlayClientCredentials(credentialsJson);
            }

            if (string.IsNullOrEmpty(AndroidPackageName))
                Log.Warning($"GooglePlayStore:{nameof(AndroidPackageName)} is not set. IAP VALIDATION WILL ACCEPT PURCHASE RECEIPTS OF ANY APPLICATION!");

            // Check the loaded values can be read

            try
            {
                _ = GetGooglePlayPublicKeyRSA();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Platform options: {nameof(GooglePlayPublicKey)} cannot be loaded", ex);
            }
        }

        /// <summary>
        /// Decode Google Play public key (if present). If there is no key, returns null.
        /// </summary>
        public RSACryptoServiceProvider GetGooglePlayPublicKeyRSA()
        {
            if (string.IsNullOrEmpty(GooglePlayPublicKey))
                return null;
            return PEMKeyLoader.CryptoServiceProviderFromPublicKeyInfo(GooglePlayPublicKey);
        }

        void ResolveAndroidPublisherApiServiceAccountCredentials(string credentialsJson)
        {
            GoogleCredential credentials = GoogleCredential.FromJson(credentialsJson);
            if (credentials.IsCreateScopedRequired)
            {
                credentials = credentials.CreateScoped(new string[] { AndroidPublisherService.Scope.Androidpublisher });
            }
            AndroidPublisherApiServiceAccountCredentials = credentials;
        }

        void ResolveGooglePlayClientCredentials(string credentialsJson)
        {
            try
            {
                JObject credentialsRoot = (JObject)JsonConvert.DeserializeObject(credentialsJson);
                JObject webCredentials = (JObject)credentialsRoot["web"];

                GooglePlayClientId = webCredentials["client_id"]?.Value<string>();
                GooglePlayClientSecret = webCredentials["client_secret"]?.Value<string>();

                if (string.IsNullOrEmpty(GooglePlayClientId))
                    throw new InvalidOperationException("Could not find oath client id in credentials json");
                if (string.IsNullOrEmpty(GooglePlayClientSecret))
                    throw new InvalidOperationException("Could not find oath client secret in credentials json");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to parse Google Play OAuth Web credentials json. This file should be downloaded from Google Cloud Console. Expected format \"{\"web\": {\"client_id\": XX, \"client_secret\": XX, ...}}\"", ex);
            }
        }
    }
}
