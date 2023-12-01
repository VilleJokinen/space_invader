// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;

namespace Metaplay.Cloud.Services
{
    public class GoogleOAuth2PublicKeyCache : JWKSPublicKeyCache
    {
        static Lazy<GoogleOAuth2PublicKeyCache> s_instance = new Lazy<GoogleOAuth2PublicKeyCache>(() => new GoogleOAuth2PublicKeyCache());
        public static GoogleOAuth2PublicKeyCache Instance => s_instance.Value;

        GoogleOAuth2PublicKeyCache() : base("https://www.googleapis.com/oauth2/v3/certs", productName: "Google Sign In")
        {
        }
    }
}
