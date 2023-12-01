// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;

namespace Metaplay.Cloud.Services
{
    public class AppleSignInPublicKeyCache : JWKSPublicKeyCache
    {
        static Lazy<AppleSignInPublicKeyCache> s_instance = new Lazy<AppleSignInPublicKeyCache>(() => new AppleSignInPublicKeyCache());
        public static AppleSignInPublicKeyCache Instance => s_instance.Value;

        AppleSignInPublicKeyCache() : base("https://appleid.apple.com/auth/keys", productName: "Sign in with Apple")
        {
        }
    }
}
