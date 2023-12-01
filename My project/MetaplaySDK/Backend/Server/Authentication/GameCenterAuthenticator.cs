// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Application;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Cloud.Services;
using Metaplay.Core;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Metaplay.Server.Authentication.Authenticators
{
    public abstract class GameCenterAuthenticatorBase : SocialPlatformAuthenticatorBase
    {
        protected static async Task VerifyGameCenterClaimAsync(AppleStoreOptions storeOpts, string claimedUserId, string publicKeyUrl, ulong timestamp, string signature64, string salt64, string bundleId)
        {
            // Check that bundleId matches
            if (bundleId != storeOpts.IosBundleId)
                throw new AuthenticationError($"Mismatched iOS bundle id for Game Center authentication: got {bundleId}, expecting {storeOpts.IosBundleId}");

            // Check that timestamp is not too old & not in future
            // NOTE: none of the reference implementations are checking timestamp validity, so we're doing the same
            DateTime signingTimestamp = DateTime.UnixEpoch + TimeSpan.FromMilliseconds(timestamp);
            //const long MinTimestampAge = -3600;             // 1 hour in future
            //const long MaxTimestampAge = 7 * 24 * 3600;     // 1 week old
            //long curTimeSec = Util.GetUtcUnixTimeSeconds();
            //long timestampSec = (long)timestamp / 1000L;
            //long timestampSince = curTimeSec - timestampSec;
            //if (timestampSince < MinTimestampAge)
            //    throw new AuthenticationError($"Timestamp for authentication request is in the future ({-timestampSince} seconds)");
            //if (timestampSince > MaxTimestampAge)
            //    throw new AuthenticationError($"Timestamp for authentication request is too old ({timestampSince} seconds)");

            // Base64 decode salt & signature
            byte[] salt = Convert.FromBase64String(salt64);
            byte[] signature = Convert.FromBase64String(signature64);

            // Fetch certificate
            RSA gcPublicKey;
            try
            {
                gcPublicKey = await AppleGameCenterPublicKeyCache.Instance.GetPublicKeyAsync(publicKeyUrl, signingTimestamp);
            }
            catch (AppleGameCenterPublicKeyCache.InvalidKeyException ex)
            {
                throw new AuthenticationError($"Failed to verify Game Center certificate: {ex.Message}");
            }
            catch (AppleGameCenterPublicKeyCache.KeyCacheTemporarilyUnavailable)
            {
                throw new AuthenticationTemporarilyUnavailable($"Game Center certificate is temporarily unavailable: {publicKeyUrl}");
            }

            // Hash signature (from concatenated values)
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] sig = Util.ConcatBytes(Encoding.UTF8.GetBytes(claimedUserId), Encoding.UTF8.GetBytes(bundleId), Util.GetBigEndianBytes(timestamp), salt);
                byte[] hash = sha256.ComputeHash(sig);

                // Verify hash
                if (!gcPublicKey.VerifyHash(hash, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                    throw new AuthenticationError("Failed to verify Game Center certificate: hash verification failed");
            }
        }
    }

    public class GameCenterAuthenticator : GameCenterAuthenticatorBase
    {
        public static async Task<AuthenticatedSocialClaimKeys> AuthenticateAsync(SocialAuthenticationClaimGameCenter gameCenter)
        {
            AppleStoreOptions storeOpts = RuntimeOptionsRegistry.Instance.GetCurrent<AppleStoreOptions>();
            if (!storeOpts.EnableAppleAuthentication)
                throw new AuthenticationError($"GameCenter authentication is disabled in {nameof(AppleStoreOptions)}");

            // Legacy claim is always present, validate it first.
            await VerifyGameCenterClaimAsync(storeOpts, gameCenter.LegacyUserId, gameCenter.PublicKeyUrl, gameCenter.Timestamp, gameCenter.Signature, gameCenter.Salt, gameCenter.BundleId);

            // If there is a Team-scoped ID present, use it as the primary ID and Legacy as the secondary. Additionally, unauthenticated GamePlayerId is stored for convenience.
            if (gameCenter.GameCenter2020MigrationClaim != null)
            {
                await VerifyGameCenterClaimAsync(storeOpts, gameCenter.GameCenter2020MigrationClaim.TeamPlayerId, gameCenter.GameCenter2020MigrationClaim.PublicKeyUrl, gameCenter.GameCenter2020MigrationClaim.Timestamp, gameCenter.GameCenter2020MigrationClaim.Signature, gameCenter.GameCenter2020MigrationClaim.Salt, gameCenter.GameCenter2020MigrationClaim.BundleId);

                return AuthenticatedSocialClaimKeys.FromPrimaryAndSecondaryKeys(
                    new AuthenticationKey(AuthenticationPlatform.GameCenter2020, gameCenter.GameCenter2020MigrationClaim.TeamPlayerId),
                    new AuthenticationKey(AuthenticationPlatform.GameCenter, gameCenter.LegacyUserId),
                    new AuthenticationKey(AuthenticationPlatform.GameCenter2020UAGT, gameCenter.GameCenter2020MigrationClaim.GamePlayerId));
            }

            // Only legacy claim present. Use it.
            return AuthenticatedSocialClaimKeys.FromSingleKey(new AuthenticationKey(AuthenticationPlatform.GameCenter, gameCenter.LegacyUserId));
        }
    }

    public class GameCenter2020Authenticator : GameCenterAuthenticatorBase
    {
        public static async Task<AuthenticatedSocialClaimKeys> AuthenticateAsync(SocialAuthenticationClaimGameCenter2020 gameCenter)
        {
            AppleStoreOptions storeOpts = RuntimeOptionsRegistry.Instance.GetCurrent<AppleStoreOptions>();
            if (!storeOpts.EnableAppleAuthentication)
                throw new AuthenticationError($"GameCenter authentication is disabled in {nameof(AppleStoreOptions)}");

            await VerifyGameCenterClaimAsync(storeOpts, gameCenter.TeamPlayerId, gameCenter.PublicKeyUrl, gameCenter.Timestamp, gameCenter.Signature, gameCenter.Salt, gameCenter.BundleId).ConfigureAwait(false);

            // \note: unauthenticated GamePlayerId is stored for convenience.
            return AuthenticatedSocialClaimKeys.FromPrimaryAndSecondaryKeys(
                    new AuthenticationKey(AuthenticationPlatform.GameCenter2020, gameCenter.TeamPlayerId),
                    new AuthenticationKey(AuthenticationPlatform.GameCenter2020UAGT, gameCenter.GamePlayerId));
        }
    }
}
