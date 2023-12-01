// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if UNITY_EDITOR

using Metaplay.Core.Tasks;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using static System.FormattableString;

namespace Metaplay.Unity.CompanyId
{
    static partial class CompanyIdLoginFlowHelper
    {
        static Task<GameLoginAccessToken> EditorLogin(string companyIdBaseUrl, string companyIdEnvironment)
        {
            Uri callbackUri = new Uri(Invariant($"http://127.0.0.1:{UnityOAuth2Util.LocalhostCallbackPort}/editorAuthCallback"));
            string codeVerifier = UnityOAuth2Util.CreateCodeVerifier();
            string authorizeUrl = GetAuthorizeUrl(companyIdBaseUrl, companyIdEnvironment, codeVerifier, callbackUri.ToString());
            string tokenUrl = GetTokenUrl(companyIdBaseUrl);

            CompanyIdLog.Log.Info("Opening default browser into the login window.");
            Application.OpenURL(authorizeUrl);

            return MetaTask.Run(async () =>
            {
                (HttpListener http, Uri chosenCallbackUri) = UnityOAuth2Util.CreateListenerForCallbackIntoEditor(new List<Uri>() { callbackUri });
                return await ServeHttpCallbackAsync(http, chosenCallbackUri, tokenUrl, codeVerifier);
            }, scheduler: MetaTask.BackgroundScheduler);
        }

        static async Task<GameLoginAccessToken> ServeHttpCallbackAsync(HttpListener http, Uri loginCallbackUrl, string tokenUrl, string codeVerifier)
        {
            using (http)
            {
                http.Prefixes.Add(loginCallbackUrl.GetLeftPart(UriPartial.Authority) + "/");
                http.Start();

                // Wait for login
                HttpListenerContext request;
                for (;;)
                {
                    request = await http.GetContextAsync();

                    if (Uri.Compare(request.Request.Url, loginCallbackUrl, UriComponents.Scheme | UriComponents.UserInfo | UriComponents.Host | UriComponents.Port | UriComponents.Path, UriFormat.Unescaped, StringComparison.Ordinal) != 0)
                    {
                        request.Response.StatusCode = 404;
                        request.Response.ContentType = "text/plain";
                        byte[] payload = Encoding.UTF8.GetBytes("404");
                        await request.Response.OutputStream.WriteAsync(payload, 0, payload.Length);
                        request.Response.OutputStream.Close();
                        request.Response.Close();
                        continue;
                    }

                    // Handle response
                    (string codeMaybe, string errorMaybe) = UnityOAuth2Util.ParseCodeFromCallback(request.Request.Url.ToString());
                    if (errorMaybe != null)
                    {
                        request.Response.StatusCode = 200;
                        request.Response.ContentType = "text/plain";
                        byte[] payload = Encoding.UTF8.GetBytes("got error in in login: " + errorMaybe);
                        await request.Response.OutputStream.WriteAsync(payload, 0, payload.Length);
                        request.Response.OutputStream.Close();
                        request.Response.Close();
                        throw new InvalidOperationException($"Authorization server refused to provide code: {errorMaybe}");
                    }

                    CompanyIdLog.Log.Info("Got callback from auth provider. Exchanging code for access token.");

                    // Try use the code to exchange it for access token.
                    try
                    {
                        GameLoginAccessToken result = await ExchangeCodeForAccessTokenAsync(tokenUrl, codeMaybe, codeVerifier, loginCallbackUrl.ToString());

                        CompanyIdLog.Log.Info("Code exchange complete. Got Access Token.");

                        request.Response.StatusCode = 200;
                        request.Response.ContentType = "text/plain";
                        byte[] payload = Encoding.UTF8.GetBytes("CompanyId login success. You may now close this tab and return to the Unity Editor.");
                        await request.Response.OutputStream.WriteAsync(payload, 0, payload.Length);
                        request.Response.OutputStream.Close();
                        request.Response.Close();

                        return result;
                    }
                    catch(Exception ex)
                    {
                        CompanyIdLog.Log.Warning("Code exchange failed: {Error}", ex);

                        request.Response.StatusCode = 200;
                        request.Response.ContentType = "text/plain";
                        byte[] payload = Encoding.UTF8.GetBytes("got error in token exchange: " + ex.ToString());
                        await request.Response.OutputStream.WriteAsync(payload, 0, payload.Length);
                        request.Response.OutputStream.Close();
                        request.Response.Close();
                        throw;
                    }
                }
            }
        }
    }
}

#endif
