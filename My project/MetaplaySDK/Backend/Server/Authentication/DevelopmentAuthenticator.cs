// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Application;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using Newtonsoft.Json;
using System.Threading.Tasks;

namespace Metaplay.Server.Authentication.Authenticators
{
    public class DevelopmentAuthenticator : SocialPlatformAuthenticatorBase
    {
        class DevelopmentAuthToken
        {
            [JsonProperty("id")]            public string   SocialId        { get; set; }
            [JsonProperty("token")]         public string   AuthToken       { get; set; }
            [JsonProperty("force_failure")] public bool     ForceFailure    { get; set; }
            [JsonProperty("migration_id")]  public string   MigrationId     { get; set; }
        }

        public static async Task<AuthenticatedSocialClaimKeys> AuthenticateAsync(SocialAuthenticationClaimDevelopment development)
        {
            // \note development always succeeds (if dev features enabled)
            EnvironmentOptions envOpts = RuntimeOptionsRegistry.Instance.GetCurrent<EnvironmentOptions>();
            if (!envOpts.EnableDevelopmentFeatures)
                throw new AuthenticationError("Trying to authenticate with Development platform when dev features are disabled");

            // Simulate delay
            await Task.Delay(1_000).ConfigureAwait(false);

            // Parse socialId from json
            DevelopmentAuthToken token = JsonConvert.DeserializeObject<DevelopmentAuthToken>(development.AuthToken);

            if (token.ForceFailure)
                throw new AuthenticationError("ForceFailure was set on development platform");

            // Simulate migrations. If migration id is given, the social authentication attempts to use both ids. In this case
            // the migration id would be the old token.
            if (!string.IsNullOrEmpty(token.MigrationId))
            {
                return AuthenticatedSocialClaimKeys.FromPrimaryAndSecondaryKeys(
                    new AuthenticationKey(AuthenticationPlatform.Development, token.SocialId),
                    new AuthenticationKey(AuthenticationPlatform.Development, token.MigrationId));
            }

            return AuthenticatedSocialClaimKeys.FromSingleKey(new AuthenticationKey(AuthenticationPlatform.Development, token.SocialId));
        }
    }
}
