// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using JWT;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using System;
using System.Security.Cryptography;
using System.Text;

namespace Metaplay.Server.AdminApi.Controllers
{
    public static class FacebookSignedRequestParser
    {
        public class BadSignedRequestException : Exception
        {
            public BadSignedRequestException(string message) : base(message)
            {
            }
        }

        public static PayloadT ParseSignedRequest<PayloadT>(HttpRequest request, string facebookAppSecret)
        {
            if (request.Form == null)
                throw new BadSignedRequestException("missing form-data");
            if (!request.Form.ContainsKey("signed_request"))
                throw new BadSignedRequestException("missing signed_request form-data");

            string signedRequest = request.Form["signed_request"];
            if (string.IsNullOrEmpty(signedRequest))
                throw new BadSignedRequestException("invalid signed_request form-data");

            string[] parts = signedRequest.Split('.');
            if (parts.Length != 2)
                throw new BadSignedRequestException("malformed signed_request form-data");

            string              encodedSignature    = parts[0];
            string              encodedRequest      = parts[1];
            JwtBase64UrlEncoder encoder             = new JwtBase64UrlEncoder();
            byte[]              signature           = encoder.Decode(encodedSignature);
            byte[]              key                 = Encoding.UTF8.GetBytes(facebookAppSecret);
            byte[]              expectedSignature;
            using (HMACSHA256 hmac = new HMACSHA256(key))
            {
                byte[] encodedRequestBytes  = Encoding.UTF8.GetBytes(encodedRequest);
                byte[] hash                 = hmac.ComputeHash(encodedRequestBytes);
                expectedSignature = hash;
            }

            if (!CryptographicOperations.FixedTimeEquals(expectedSignature, signature))
                throw new BadSignedRequestException("invalid signature");

            byte[]  requestJsonBytes    = encoder.Decode(encodedRequest);
            string  requestJson         = Encoding.UTF8.GetString(requestJsonBytes);
            return JsonConvert.DeserializeObject<PayloadT>(requestJson);
        }
    }
}
