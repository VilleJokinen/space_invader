// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Application;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Cloud.Web3;
using Metaplay.Core;
using Metaplay.Server.Web3;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using static System.FormattableString;

namespace Metaplay.Server.Authentication.Authenticators
{
    public class ImmutableXAuthenticator : SocialPlatformAuthenticatorBase
    {
        public static async Task<AuthenticatedSocialClaimKeys> AuthenticateAsync(EntityId playerId, SocialAuthenticationClaimImmutableX claim)
        {
            (Web3Options web3Options, EnvironmentOptions envOptions) = RuntimeOptionsRegistry.Instance.GetCurrent<Web3Options, EnvironmentOptions>();
            if (!web3Options.EnableImmutableXPlayerAuthentication)
                throw new AuthenticationError("ImmutableX player authentication is not enabled");

            EthereumNetworkProperties networkProperties = EthereumNetworkProperties.GetPropertiesForNetwork(web3Options.ImmutableXNetwork);

            // First, verify if challenge is still valid
            MetaTime issuedAt = claim.ChallengeTimestamp;
            MetaTime now = MetaTime.Now;
            if (issuedAt > now)
                throw new AuthenticationError("ChallengeTimestamp is in the future");
            if (issuedAt < now - MetaDuration.FromTimeSpan(web3Options.ImmutableXPlayerAuthenticationTimeLimit))
                throw new AuthenticationError("Challenge has expired. ChallengeTimestamp is too far in the past.");

            // Check the challenge is for the current player. This is less of a security mechanism (in order to succeed, claimer needs to control
            // the claimed wallet anyway), but more sanity check to avoid claims and challenges from leaking between sessions.
            if (claim.ChallengePlayerId != playerId)
                throw new AuthenticationError($"Player claim was generated for account {claim.ChallengePlayerId}, but attempted to be used for account {playerId}");

            EthereumAddress ethAccount = EthereumAddress.FromStringWithoutChecksumCasing(claim.ClaimedEthereumAccount);
            StarkPublicKey imxAccount = StarkPublicKey.FromString(claim.ClaimedImmutableXAccount);

            // Validate signature. To avoid storing any information about the challenge on the server-side, we recreate
            // the challenge message again with the supplied parameters and verify the hash of it. We don't need to worry
            // about client supplying incorrect parameters since the original message is HMAC'd. Giving mutated params will
            // simply generate a different message (and due to the HMAC, with contents attacker cannot predict), and will
            // inevitably fail signature check.
            ImmutableXLoginChallenge challenge = ImmutableXLoginChallenge.Create(web3Options, envOptions, issuedAt, claim.ChallengePlayerId, ethAccount, imxAccount);

            try
            {
                EthereumSignatureVerifier.ValidatePersonalSignature(ethAccount, challenge.Message, claim.ChallengeSignature, networkProperties.ChainId);
            }
            catch (EthereumSignatureVerifier.SignatureValidationException ex)
            {
                throw new AuthenticationError("Invalid ETH signature", ex);
            }

            // ETH account is now valid. Next, let's get the IMX accounts:

            StarkPublicKey[] imxAccounts = await ImmutableXApi.GetImxAccountsAsync(web3Options, ethAccount);
            List<AuthenticationKey> imxKeys = new List<AuthenticationKey>();
            bool foundClaimedKey = false;

            foreach (StarkPublicKey attachedImxAccount in imxAccounts)
            {
                string imxAuthId = CreateImmutableXAuthenticationUserId(web3Options, attachedImxAccount);
                imxKeys.Add(new AuthenticationKey(AuthenticationPlatform.ImmutableX, imxAuthId));

                if (attachedImxAccount == imxAccount)
                    foundClaimedKey = true;
            }

            // For claim to succeed, the claimed IMX account should be in the attached accounts.
            if (!foundClaimedKey)
                throw new AuthenticationError($"The IMX account {imxAccount.GetPublicKeyString()} is not attached to the ETH address {imxAccount.GetPublicKeyString()}");

            string ethAuthId = CreateEthereumAuthenticationUserId(web3Options, ethAccount);
            return AuthenticatedSocialClaimKeys.FromPrimaryAndSecondaryKeys(
                primaryAuthenticationKey: new AuthenticationKey(AuthenticationPlatform.Ethereum, ethAuthId),
                secondaryAuthenticationKeys: imxKeys.ToArray());
        }

        /// <summary>
        /// Create the user id to use in an <see cref="AuthenticationKey"/> for <see cref="AuthenticationPlatform.ImmutableX"/>.
        /// The user id is of form {NetworkId}:{ChainId}:{ImxAccount}
        /// </summary>
        public static string CreateImmutableXAuthenticationUserId(Web3Options web3Options, StarkPublicKey imxAccount)
        {
            EthereumNetworkProperties networkProperties = EthereumNetworkProperties.GetPropertiesForNetwork(web3Options.ImmutableXNetwork);

            int     networkId       = networkProperties.NetworkId;
            int     chainId         = networkProperties.ChainId;
            string  imxAccountStr   = imxAccount.GetPublicKeyString(StarkPublicKey.PublicKeyStyle.WithoutPrefix);

            return Invariant($"{networkId}:{chainId}:{imxAccountStr}");
        }

        /// <summary>
        /// Create the user id to use in an <see cref="AuthenticationKey"/> for <see cref="AuthenticationPlatform.Ethereum"/>.
        /// The user id is of form {NetworkId}:{ChainId}:{EthAccount}
        /// </summary>
        public static string CreateEthereumAuthenticationUserId(Web3Options web3Options, EthereumAddress ethAccount)
        {
            EthereumNetworkProperties networkProperties = EthereumNetworkProperties.GetPropertiesForNetwork(web3Options.ImmutableXNetwork);

            int     networkId       = networkProperties.NetworkId;
            int     chainId         = networkProperties.ChainId;
            string  ethAccountStr   = ethAccount.GetAddressString(EthereumAddress.AddressStyle.WithoutPrefix);

            return Invariant($"{networkId}:{chainId}:{ethAccountStr}");
        }

        /// <summary>
        /// Parse a user id previously created by <see cref="CreateEthereumAuthenticationUserId"/>,
        /// and return the Ethereum address part but only if the network and chain parts match
        /// those in <paramref name="web3Options"/>. If they don't match, null is returned.
        /// </summary>
        public static EthereumAddress? ParseEthereumAuthenticationUserIdAndCheckNetwork(Web3Options web3Options, string userId)
        {
            EthereumNetworkProperties networkProperties = EthereumNetworkProperties.GetPropertiesForNetwork(web3Options.ImmutableXNetwork);

            ParsedEthereumAuthenticationUserId parsed = ParseEthereumAuthenticationUserId(userId);
            if (parsed.NetworkId != networkProperties.NetworkId)
                return null;
            if (parsed.ChainId != networkProperties.ChainId)
                return null;
            return parsed.EthereumAddress;
        }

        /// <summary>
        /// Parse a user id previously created by <see cref="CreateEthereumAuthenticationUserId"/>.
        /// </summary>
        public static ParsedEthereumAuthenticationUserId ParseEthereumAuthenticationUserId(string userId)
        {
            string[] parts = userId.Split(":");
            if (parts.Length != 3)
                throw new FormatException($"Malformed Ethereum authentication key user id: {userId} . Expected <networkId>:<chainId>:<ethereumAddress> .");

            int             networkId       = int.Parse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture);
            int             chainId         = int.Parse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture);
            EthereumAddress ethereumAddress = EthereumAddress.FromString("0x" + parts[2]);

            return new ParsedEthereumAuthenticationUserId(networkId: networkId, chainId: chainId, ethereumAddress);
        }

        public struct ParsedEthereumAuthenticationUserId
        {
            public int NetworkId;
            public int ChainId;
            public EthereumAddress EthereumAddress;

            public ParsedEthereumAuthenticationUserId(int networkId, int chainId, EthereumAddress ethereumAddress)
            {
                NetworkId = networkId;
                ChainId = chainId;
                EthereumAddress = ethereumAddress;
            }
        }
    }
}
