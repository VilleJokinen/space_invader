// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Metaplay.Unity
{
    /// <summary>
    /// Helper for common OAuth2 operations.
    /// </summary>
    public static class UnityOAuth2Util
    {
        /// <summary>
        /// Commonly used port for OAuth2 (localhost) callback.
        /// </summary>
        public const int LocalhostCallbackPort = 42543;

        public static string Base64UrlEncode(byte[] data)
        {
            return Convert.ToBase64String(data)
                .Replace("=", "")
                .Replace('+', '-')
                .Replace('/', '_');
        }

        public static string CreateCodeVerifier()
        {
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                byte[] buf = new byte[32];
                rng.GetBytes(buf);
                return Base64UrlEncode(buf);
            }
        }

        public static byte[] CreateCodeChallengeS256(string codeVerifier)
        {
            return GetSHA256Hash(Encoding.ASCII.GetBytes(codeVerifier));
        }

        public static byte[] GetSHA256Hash(byte[] bytes)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                return sha256.ComputeHash(bytes);
            }
        }

        /// <summary>
        /// Parses "code" from query params. URL prefix must be validated before.
        /// </summary>
        public static (string code, string error) ParseCodeFromCallback(string url)
        {
            // \note: HttpUtility is not supported on older Unity
            Uri parsedUrl = new Uri(url);
            string query = parsedUrl.Query;
            string codeMaybe = null;
            string errorMaybe = null;
            if (query.StartsWith("?", StringComparison.Ordinal))
            {
                string[] keyValues = query.Substring(1).Split('&');
                foreach (string keyValue in keyValues)
                {
                    int sep = keyValue.IndexOf('=');
                    if (sep == -1)
                        continue;
                    string key = keyValue.Substring(0, sep);
                    string value = keyValue.Substring(sep + 1);
                    if (key == "code")
                        codeMaybe = value;
                    else if (key == "error")
                        errorMaybe = value;
                }
            }

            if (errorMaybe != null)
                return (null, errorMaybe);
            if (codeMaybe == null)
                return (null, "missing code argument");
            return (codeMaybe, null);
        }

        public static async Task<TAccessToken> ExchangeCodeForAccessTokenAsync<TAccessToken>(string tokenUrl, string formEncodedRequestPayload)
        {
            HttpWebRequest tokenRequest = (HttpWebRequest)HttpWebRequest.CreateDefault(new Uri(tokenUrl));
            tokenRequest.Method = "POST";
            tokenRequest.ContentType = "application/x-www-form-urlencoded";
            using (Stream tokenRequestStream = await tokenRequest.GetRequestStreamAsync())
            {
                byte[] tokenRequestParamsBytes = Encoding.UTF8.GetBytes(formEncodedRequestPayload);
                tokenRequestStream.Write(tokenRequestParamsBytes, 0, tokenRequestParamsBytes.Length);
                tokenRequestStream.Close();
            }

            using (MemoryStream responseBuffer = new MemoryStream())
            {
                using (HttpWebResponse tokenResponse = (HttpWebResponse)await tokenRequest.GetResponseAsync())
                {
                    if (tokenResponse.StatusCode != HttpStatusCode.OK)
                        throw new InvalidOperationException($"Invalid status code, expected 200, got {tokenResponse.StatusCode}");
                    if (tokenResponse.ContentType != "application/json")
                        throw new InvalidOperationException($"Invalid content type, expected application/json, got {tokenResponse.ContentType}");
                    await tokenResponse.GetResponseStream().CopyToAsync(responseBuffer);
                }

                TAccessToken result = JsonUtility.FromJson<TAccessToken>(Encoding.UTF8.GetString(responseBuffer.GetBuffer()));
                return result;
            }
        }

        #if UNITY_EDITOR

        /// <summary>
        /// Chooses an available callback url, and opens a listener for it.
        /// </summary>
        public static (HttpListener, Uri) CreateListenerForCallbackIntoEditor(List<Uri> callbackUris)
        {
            // Try to open any login callback url and listen there
            Exception lastError = null;
            foreach (Uri callbackUri in callbackUris)
            {
                HttpListener http = null;
                try
                {
                    http = new HttpListener();
                    http.Prefixes.Add(callbackUri.GetLeftPart(UriPartial.Authority) + "/");
                    http.Start();
                }
                catch (Exception ex)
                {
                    ((IDisposable)http)?.Dispose();
                    lastError = ex;
                    continue;
                }

                return (http, callbackUri);
            }
            throw new InvalidOperationException("Could not open OAuth2 callback listener. Check firewall settings.", lastError);
        }

        #endif
    }
}
