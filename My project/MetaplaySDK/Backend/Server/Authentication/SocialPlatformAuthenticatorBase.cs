// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using JWT;
using Metaplay.Cloud.Services;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Metaplay.Server.Authentication.Authenticators
{
    /// <summary>
    /// Authenticator for a social platform is responsible for validating
    /// social authentication claims and resolving its authentication keys.
    /// </summary>
    public abstract class SocialPlatformAuthenticatorBase
    {
        /// <summary>
        /// Parses and validates JWT. Returns the parsed json payload on success.
        /// <para>
        /// Throws: <br/>
        /// * AuthenticationError: if token signature does not match or no such key exists <br/>
        /// * AuthenticationTemporarilyUnavailable: if key cache is temporarily unavailable <br/>
        /// * InvalidOperationException: if token is malformed <br/>
        /// </para>
        /// </summary>
        protected static async ValueTask<TJsonType> ParseJWTAsync<TJsonType>(string token, JWKSPublicKeyCache keycache)
        {
            try
            {
                JWT.JwtParts                parts               = new JWT.JwtParts(token);
                var                         jsonSerializer      = new JWT.Serializers.JsonNetSerializer();
                var                         urlEncoder          = new JWT.JwtBase64UrlEncoder();
                JWT.JwtDecoder              decoder             = new JWT.JwtDecoder(jsonSerializer, urlEncoder);
                Dictionary<string, string>  header              = decoder.DecodeHeader<Dictionary<string, string>>(parts);

                // Extract signing key claim and validate the payload
                string                      keyid               = header["kid"];
                RSA                         pubKey              = await keycache.GetPublicKeyAsync(keyid);
                var                         algo                = new JWT.Algorithms.RS256Algorithm(publicKey: pubKey);
                byte[]                      sig                 = urlEncoder.Decode(parts.Signature);
                byte[]                      signedBytes         = Encoding.UTF8.GetBytes(String.Concat(parts.Header, ".", parts.Payload));

                // Check signature
                if (algo.Verify(signedBytes, sig))
                    return decoder.DecodeToObject<TJsonType>(parts);
            }
            catch(JWKSPublicKeyCache.NoSuchKeyException ex)
            {
                throw new AuthenticationError($"Malformed {keycache.ProductName} login token, no such signing key: {token}, key: {ex.KeyId}", ex);
            }
            catch(JWKSPublicKeyCache.KeyCacheTemporarilyUnavailable)
            {
                throw new AuthenticationTemporarilyUnavailable($"JWT key cache for {keycache.ProductName} is temporarily unavailable");
            }
            catch(Exception ex)
            {
                throw new InvalidOperationException($"Malformed token: {token}", ex);
            }
            throw new AuthenticationError($"Signature check failed failed: {token}");
        }
    }
}
