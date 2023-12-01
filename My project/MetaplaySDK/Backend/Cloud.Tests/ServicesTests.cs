// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Services;
using Newtonsoft.Json;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace Cloud.Tests
{
    class ServicesTests
    {
        class TestJWKSet
        {
            public TestJWK[] keys = default;
        };
        class TestJWK
        {
            public string kid = default;
        }

        class OpenIdConfig
        {
            [JsonProperty("issuer")] public string Issuer = default;
            [JsonProperty("jwks_uri")] public string JWKSUri = default;
        }

        [TestCase]
        public async Task TestGoogleOAuth2PublicKeyCacheExistingKey()
        {
            using (HttpClient client = HttpUtil.CreateJsonHttpClient())
            {
                // test all keys in v1
                Dictionary<string, string> certBodyById = await HttpUtil.RequestJsonGetAsync<Dictionary<string, string>>(client, "https://www.googleapis.com/oauth2/v1/certs").ConfigureAwait(false);
                foreach (string keyid in certBodyById.Keys)
                    Assert.NotNull(await GoogleOAuth2PublicKeyCache.Instance.GetPublicKeyAsync(keyid).ConfigureAwait(false));

                // also all in V3 endpoint
                TestJWKSet keyset = await HttpUtil.RequestJsonGetAsync<TestJWKSet>(client, "https://www.googleapis.com/oauth2/v3/certs").ConfigureAwait(false);
                for (int ndx = 0; ndx < keyset.keys.Length; ++ndx)
                    Assert.NotNull(await GoogleOAuth2PublicKeyCache.Instance.GetPublicKeyAsync(keyset.keys[ndx].kid).ConfigureAwait(false));
            }
        }

        [TestCase]
        public void TestGoogleOAuth2PublicKeyCacheNoSuchKey()
        {
            Assert.ThrowsAsync<GoogleOAuth2PublicKeyCache.NoSuchKeyException>(async () => await GoogleOAuth2PublicKeyCache.Instance.GetPublicKeyAsync("bad-id"));
        }

        [TestCase]
        public async Task TestAppleGameCenterPublicKeyCacheGoodKey()
        {
            string validKeyUrl = "https://static.gc.apple.com/public-key/gc-prod-6.cer";
            DateTime timestamp = new DateTime(2021, 10, 08, 12, 23, 23, DateTimeKind.Utc);
            Assert.NotNull(await AppleGameCenterPublicKeyCache.Instance.GetPublicKeyAsync(validKeyUrl, timestamp));
        }

        [TestCase]
        public async Task TestAppleGameCenterPublicKeyCacheSameKeys()
        {
            string validKeyUrl = "https://static.gc.apple.com/public-key/gc-prod-6.cer";
            DateTime timestamp = new DateTime(2021, 10, 08, 12, 23, 23, DateTimeKind.Utc);
            var fetch1 = AppleGameCenterPublicKeyCache.Instance.GetPublicKeyAsync(validKeyUrl, timestamp);
            var fetch2 = AppleGameCenterPublicKeyCache.Instance.GetPublicKeyAsync(validKeyUrl, timestamp);
            Assert.AreEqual(await fetch1, await fetch2);
        }

        [TestCase]
        public void TestAppleGameCenterPublicKeyCacheCannotFetch()
        {
            string notAKeyUrl = "https://test-invalid.gc.apple.com/notreal";
            DateTime timestamp = new DateTime(2020, 10, 08, 12, 23, 23, DateTimeKind.Utc);
            Assert.ThrowsAsync<AppleGameCenterPublicKeyCache.KeyCacheTemporarilyUnavailable>(async () => await AppleGameCenterPublicKeyCache.Instance.GetPublicKeyAsync(notAKeyUrl, timestamp));
        }

        [TestCase]
        public void TestAppleGameCenterPublicKeyCacheInvalidKey()
        {
            string notAKeyUrl = "https://static.gc.apple.com/sap/setup.crt"; // not a GameCenter key
            DateTime timestamp = new DateTime(2020, 10, 08, 12, 23, 23, DateTimeKind.Utc);
            Assert.ThrowsAsync<AppleGameCenterPublicKeyCache.InvalidKeyException>(async () => await AppleGameCenterPublicKeyCache.Instance.GetPublicKeyAsync(notAKeyUrl, timestamp));
        }

        [TestCase]
        public async Task TestAppleGameCenterPublicKeyCacheOldKey()
        {
            string validKeyUrl = "https://static.gc.apple.com/public-key/gc-prod-3.cer";
            DateTime timestamp = new DateTime(2020, 10, 08, 12, 23, 23, DateTimeKind.Utc);
            try
            {
                _ = await AppleGameCenterPublicKeyCache.Instance.GetPublicKeyAsync(validKeyUrl, timestamp);
                Assert.Fail("Expected AppleGameCenterPublicKeyCache.InvalidKeyException, got no exception");
            }
            catch (AppleGameCenterPublicKeyCache.InvalidKeyException)
            {
            }
            catch (AppleGameCenterPublicKeyCache.KeyCacheTemporarilyUnavailable)
            {
                // tolerate key cache not finding the old key
            }
        }

        [TestCase]
        public async Task TestAppleGameCenterPublicKeyCacheFutureProofness()
        {
            // This key has not yet been issued. We check that the key is not available or it is valid. The
            // goal is to guarantee we notice if a new key is being issued and we cannot read it (cert issues
            // again). The timestamp should be a valid time for the upcoming certificate. We cannot really really
            // know this, but sightly after the expiration of the previous (i.e. currently latest) cert is a
            // safe bet.
            string futureKeyUrl = "https://static.gc.apple.com/public-key/gc-prod-10.cer";
            DateTime timestamp = new DateTime(2024, 05, 28, 0, 0, 0, DateTimeKind.Utc);

            try
            {
                await AppleGameCenterPublicKeyCache.Instance.GetPublicKeyAsync(futureKeyUrl, timestamp);

                // Key works. This test is no longer meaningful
                // \todo: we could automatically step to the next index, but that is complex and presumably as fragile as this.
                Assert.Warn("Future key has been issued successfully. This is fine, but you should update the 'futureKeyUrl' and 'timestamp' to keep the future proofness test working");
            }
            catch (AppleGameCenterPublicKeyCache.KeyCacheTemporarilyUnavailable)
            {
                // Expected. Key has not yet been issued.
                return;
            }
            catch (AppleGameCenterPublicKeyCache.InvalidKeyException)
            {
                // Unexpected, fix your certificates
                throw;
            }
        }

        [TestCase]
        public async Task TestSignInWithAppleKeyCacheExistingKeys()
        {
            using (HttpClient client = HttpUtil.CreateJsonHttpClient())
            {
                string      certsUri    = "https://appleid.apple.com/auth/keys";
                TestJWKSet  keyset      = await HttpUtil.RequestJsonGetAsync<TestJWKSet>(client, certsUri).ConfigureAwait(false);

                // test all keys
                for (int ndx = 0; ndx < keyset.keys.Length; ++ndx)
                    Assert.NotNull(await AppleSignInPublicKeyCache.Instance.GetPublicKeyAsync(keyset.keys[ndx].kid).ConfigureAwait(false));
            }
        }

        [TestCase]
        public void TestSignInWithAppleKeyCacheNoSuchKey()
        {
            Assert.ThrowsAsync<AppleSignInPublicKeyCache.NoSuchKeyException>(async () => await AppleSignInPublicKeyCache.Instance.GetPublicKeyAsync("bad-id"));
        }

        [TestCase]
        public async Task TestFacebookGraphApiAccessible()
        {
            using (HttpClient client = HttpUtil.CreateJsonHttpClient())
            {
                string                      apiUrl          = "https://graph.facebook.com/debug_token";
                HttpResponseMessage         response        = await client.GetAsync(apiUrl).ConfigureAwait(false);
                string                      responsePayload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                // We don't check response.StatusCode. If API cannot be connected to, the GET will fail.
                _ = responsePayload;
            }
        }

        [TestCase]
        public async Task TestFacebookLoginPublicKeyCacheExistingKeys()
        {
            using (HttpClient client = HttpUtil.CreateJsonHttpClient())
            {
                string      certsUri    = "https://www.facebook.com/.well-known/oauth/openid/jwks/";
                TestJWKSet  keyset      = await HttpUtil.RequestJsonGetAsync<TestJWKSet>(client, certsUri).ConfigureAwait(false);

                // test all keys
                for (int ndx = 0; ndx < keyset.keys.Length; ++ndx)
                    Assert.NotNull(await FacebookLoginPublicKeyCache.Instance.GetPublicKeyAsync(keyset.keys[ndx].kid).ConfigureAwait(false));
            }
        }

        [TestCase]
        public void TestFacebookLoginPublicKeyCacheNoSuchKey()
        {
            Assert.ThrowsAsync<FacebookLoginPublicKeyCache.NoSuchKeyException>(async () => await FacebookLoginPublicKeyCache.Instance.GetPublicKeyAsync("bad-id"));
        }

        [TestCase]
        public async Task TestFacebookOpenIdConfig()
        {
            using (HttpClient client = HttpUtil.CreateJsonHttpClient())
            {
                string configUri = "https://www.facebook.com/.well-known/openid-configuration/";
                OpenIdConfig config = await HttpUtil.RequestJsonGetAsync<OpenIdConfig>(client, configUri).ConfigureAwait(false);

                Assert.AreEqual(config.Issuer, FacebookLoginService.OpenIdIssuer);
                Assert.AreEqual(config.JWKSUri, FacebookLoginPublicKeyCache.Instance.JWKSUrl);
            }
        }
    }
}
