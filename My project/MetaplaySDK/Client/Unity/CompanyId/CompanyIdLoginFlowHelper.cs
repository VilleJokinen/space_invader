// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Metaplay.Unity.CompanyId
{
    /// <summary>
    /// Login Flow helper manages the native device (or editor) login flow state though the
    /// AccountAPI-CompanyId authentication.
    /// </summary>
    static partial class CompanyIdLoginFlowHelper
    {
        static string GetAuthorizeUrl(string companyIdBaseUrl, string companyIdEnvironment, string codeVerifier, string callbackUri)
        {
            // \hack: hacked into /selfServe controller
            return
                $"{companyIdBaseUrl}/selfServe/deviceGameAuthorize"
                + $"?project={MetaplayCore.Options.ProjectName}"
                + $"&env={companyIdEnvironment}"
                + $"&code_challenge={UnityOAuth2Util.Base64UrlEncode(UnityOAuth2Util.CreateCodeChallengeS256(codeVerifier))}"
                + $"&callback={UnityWebRequest.EscapeURL(callbackUri)}";
        }

        static string GetTokenUrl(string companyIdBaseUrl)
        {
            // \hack: hacked into /selfServe controller
            return $"{companyIdBaseUrl}/selfServe/deviceGameToken";
        }

        /// <summary>
        /// The (JSON) CompanyID Access Token for game access.
        /// </summary>
        [Serializable]
        public class GameLoginAccessToken
        {
            #pragma warning disable CS0649
            #pragma warning disable IDE1006
            [SerializeField] public string access_token;
            [SerializeField] public int expires_in;
            #pragma warning restore CS0649
            #pragma warning restore IDE1006
        }

        static async Task<GameLoginAccessToken> ExchangeCodeForAccessTokenAsync(string tokenUrl, string code, string codeVerifier, string callbackUrl)
        {
            // \hack: device=none for temporary compatibility
            string tokenRequestParams =
                $"code={code}"
                + $"&code_verifier={codeVerifier}"
                + $"&device=none"
                + $"&redirect_uri={UnityWebRequest.EscapeURL(callbackUrl)}";

            GameLoginAccessToken result = await UnityOAuth2Util.ExchangeCodeForAccessTokenAsync<GameLoginAccessToken>(tokenUrl, tokenRequestParams);
            if (result.access_token == null)
                throw new InvalidOperationException($"Invalid result, missing access_token");

            return result;
        }

        /// <summary>
        /// Performs CompanyID login on the current platform. The login uses the platform native browser to log player in.
        /// Since login is performed in an external process, it not possible to reliably detect user cancellation on all
        /// platforms. This means the returned Task may never complete.
        /// </summary>
        public static Task<GameLoginAccessToken> LoginAsync(string companyIdBaseUrl, string companyIdEnvironment)
        {
            #if UNITY_EDITOR
            return EditorLogin(companyIdBaseUrl, companyIdEnvironment);
            #elif UNITY_ANDROID
            return AndroidLogin(companyIdBaseUrl, companyIdEnvironment);
            #elif UNITY_IOS
            return IosLogin(companyIdBaseUrl, companyIdEnvironment);
            #else
            return Task.FromException<GameLoginAccessToken>(new NotImplementedException("unsupported platform"));
            #endif
        }
    }
}
