// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;

namespace Metaplay.Cloud.Services
{
    public class FacebookLoginPublicKeyCache : JWKSPublicKeyCache
    {
        static Lazy<FacebookLoginPublicKeyCache> s_instance = new Lazy<FacebookLoginPublicKeyCache>(() => new FacebookLoginPublicKeyCache());
        public static FacebookLoginPublicKeyCache Instance => s_instance.Value;

        FacebookLoginPublicKeyCache() : base("https://www.facebook.com/.well-known/oauth/openid/jwks/", productName: "Facebook OIDC")
        {
        }
    }
}
