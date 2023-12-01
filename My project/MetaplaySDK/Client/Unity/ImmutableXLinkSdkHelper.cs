// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using System;
using System.Threading.Tasks;

namespace Metaplay.Unity
{
    /// <summary>
    /// Client side helper for using Immutable Link SDK. To use this helper, you need to enable
    /// ImmutableX Link SDK feature flag <see cref="MetaplayFeatureFlags.EnableImmutableXLinkClientLibrary"/>.
    /// </summary>
    public static class ImmutableXLinkSdkHelper
    {
        /// <summary>
        /// Performs the Login flow for ImmutableX. This includes registering or logging in into ImmutableX in the browser and
        /// then using the ImmutableX account to associate it with the game account. If <paramref name="forceResetup"/> is given,
        /// the ImmutableX account and registering is done again even if has been done previously.
        /// </summary>
        #pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        public static async Task LoginWithImmutableXAsync(bool forceResetup = false)
        #pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            if (!MetaplayCore.Options.FeatureFlags.EnableImmutableXLinkClientLibrary)
                throw new NotSupportedException("ImmutableXLinkSdkHelper requires EnableImmutableXLinkClientLibrary feature flag");

            #if UNITY_WEBGL && !UNITY_EDITOR
            ImmutableXApiBridge.GetWalletAddressResponse wallet = await ImmutableXApiBridge.GetWalletAddressAsync(new ImmutableXApiBridge.GetWalletAddressRequest() { ForceResetup = forceResetup });
            ImmutableXLoginChallengeResponse challenge = await MetaplaySDK.MessageDispatcher.SendRequestAsync<ImmutableXLoginChallengeResponse>(new ImmutableXLoginChallengeRequest(wallet.ImxAddress, wallet.EthAddress));
            ImmutableXApiBridge.SignLoginChallengeResponse signature = await ImmutableXApiBridge.SignLoginChallengeAsync(new ImmutableXApiBridge.SignLoginChallengeRequest() {  Message = challenge.Message, Description = challenge.Description });

            MetaplaySDK.SocialAuthentication.StartValidation(new SocialAuthenticationClaimImmutableX(wallet.ImxAddress, wallet.EthAddress, challenge.PlayerId, challenge.Timestamp, signature.Signature));
            #else
            throw new NotSupportedException("LoginWithImmutableXAsync not implemented on this platform");
            #endif
        }
    }
}
