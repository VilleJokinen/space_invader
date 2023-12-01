// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using System;
using System.Threading.Tasks;

namespace Metaplay.Server
{
    [RuntimeOptions("AppleStore", isStatic: true, "Configuration options for Apple AppStore app distribution.")]
    public class AppleStoreOptions : RuntimeOptionsBase
    {
        [MetaDescription("The application's \"bundle ID\" on the iOS platform. If not set, valid IAPs for any application are accepted.")]
        public string       IosBundleId                             { get; private set; } = null;
        [MetaDescription("Enables sandbox mode for validating purchases on the Apple App Store. When disabled, only production purchases are accepted, except for players explicitly marked as developers in the backend.")]
        public bool         AcceptSandboxPurchases                  { get; private set; } = true;
        [MetaDescription("Enables production mode for validating purchases on the Apple App Store. When disabled, only sandbox purchases are accepted.")]
        public bool         AcceptProductionPurchases               { get; private set; } = IsProductionEnvironment;
        [MetaDescription("The Apple App Store's shared secret for the application. Set this if you are using auto-renewable subscriptions. See [Apple's Store Connect Help](https://help.apple.com/app-store-connect/#/devf341c0f01) for more details.")]
        public string       AppleSharedSecret                       { get; private set; } = null;
        [MetaDescription("Enables authentication using Apple Sign-In and Game Center.")]
        public bool         EnableAppleAuthentication               { get; private set; } = false;

        public override Task OnLoadedAsync()
        {
            if (EnableAppleAuthentication)
            {
                if (string.IsNullOrEmpty(IosBundleId))
                    throw new InvalidOperationException($"AppleStore:{nameof(IosBundleId)} must be set if {nameof(EnableAppleAuthentication)} is true");
            }

            if (string.IsNullOrEmpty(IosBundleId))
                Log.Warning($"AppleStore:{nameof(IosBundleId)} is not set. IAP VALIDATION WILL ACCEPT PURCHASE RECEIPTS OF ANY APPLICATION!");

            if (!AcceptSandboxPurchases && !AcceptProductionPurchases)
                Log.Warning($"AppleStore:{nameof(AcceptSandboxPurchases)} and AppleStore:{nameof(AcceptProductionPurchases)} are both false. All App Store purchases will be rejected, except that players who are marked as developers can make sandbox purchases.");

            return Task.CompletedTask;
        }
    }
}
