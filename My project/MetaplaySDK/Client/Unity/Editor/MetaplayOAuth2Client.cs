// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Tasks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using static System.FormattableString;

namespace Metaplay.Unity
{
    /// <summary>
    /// Helper implementing OAuth2 Native App authorization flow.
    /// </summary>
    public class MetaplayOAuth2Client
    {
        /// <summary>
        /// Provides custom overrides for default OAuth2 server settings.
        /// </summary>
        public interface IAuthorizationServerConfigOverride
        {
            /// <summary>
            /// Optional override: <c>null</c> for no effect. If set: <br/>
            /// <inheritdoc cref="AuthorizationServerConfig.ClientId"/>
            /// </summary>
            string ClientId { get; }

            /// <summary>
            /// Optional override: <c>null</c> for no effect. If set: <br/>
            /// <inheritdoc cref="Metaplay.Unity.MetaplayOAuth2Client.AuthorizationServerConfig.AuthorizationEndpoint"/>
            /// </summary>
            string AuthorizationEndpoint { get; }

            /// <summary>
            /// Optional override: <c>null</c> for no effect. If set: <br/>
            /// <inheritdoc cref="Metaplay.Unity.MetaplayOAuth2Client.AuthorizationServerConfig.TokenEndpoint"/>
            /// </summary>
            string TokenEndpoint { get; }

            /// <summary>
            /// Optional override: <c>null</c> for no effect. If set: <br/>
            /// <inheritdoc cref="Metaplay.Unity.MetaplayOAuth2Client.AuthorizationServerConfig.Audience"/>
            /// </summary>
            string Audience { get; }

            /// <summary>
            /// Optional override: <c>null</c> or empty for no effect. If set: <br/>
            /// <inheritdoc cref="Metaplay.Unity.MetaplayOAuth2Client.AuthorizationServerConfig.LocalCallbackUrls"/>
            /// </summary>
            List<string> LocalCallbackUrls { get; }
        }

        /// <summary>
        /// Identifies OAuth2 authorization server and configuration.
        /// </summary>
        public class AuthorizationServerConfig
        {
            /// <summary>
            /// If set, fetched authorization key is cached locally on the computer. While the key is cached, the interactive login flow is not needed.
            /// </summary>
            public bool EnableCaching;

            /// <summary>
            /// OAuth2 ClientId.
            /// </summary>
            public string ClientId;

            /// <summary>
            /// OAuth2 Authorization Endpoint. You can discover this via /.well-known/oauth-authorization-server or /.well-known/openid-configuration
            /// discovery endpoints.
            /// </summary>
            public string AuthorizationEndpoint;

            /// <summary>
            /// OAuth2 TokenEndpoint. You can discover this via /.well-known/oauth-authorization-server or /.well-known/openid-configuration
            /// discovery endpoints.
            /// </summary>
            public string TokenEndpoint;

            /// <summary>
            /// OAuth2 Audiences, space separated.
            /// </summary>
            public string Audience;

            /// <summary>
            /// OAuth2 callback urls.
            /// </summary>
            public List<string> LocalCallbackUrls;

            AuthorizationServerConfig() { }

            /// <summary>
            /// Authentication provider config for Metaplay Managed Game Servers.
            /// </summary>
            public static AuthorizationServerConfig CreateForMetaplayManagedGameServers()
            {
                AuthorizationServerConfig config = new AuthorizationServerConfig();
                config.EnableCaching = true;
                config.ClientId = "t0jQcGtHXcd8HZ21XWiruzdF1WwoZrCF";
                config.AuthorizationEndpoint = "https://metaplay.auth0.com/authorize";
                config.TokenEndpoint = "https://metaplay.auth0.com/oauth/token";
                config.Audience = "managed-gameservers";
                config.LocalCallbackUrls = new List<string>() { Invariant($"http://localhost:{UnityOAuth2Util.LocalhostCallbackPort}/oauth2/") };
                return config;
            }

            /// <summary>
            /// Applies optionally overrided config values from <paramref name="overrides"/>.
            /// </summary>
            public void ApplyOverrides(IAuthorizationServerConfigOverride overrides = null)
            {
                // Apply overrides, if any
                if (!string.IsNullOrEmpty(overrides?.ClientId))                                         ClientId = overrides.ClientId;
                if (!string.IsNullOrEmpty(overrides?.AuthorizationEndpoint))                            AuthorizationEndpoint = overrides.AuthorizationEndpoint;
                if (!string.IsNullOrEmpty(overrides?.TokenEndpoint))                                    TokenEndpoint = overrides.TokenEndpoint;
                if (overrides?.LocalCallbackUrls != null && overrides?.LocalCallbackUrls.Count > 0)     LocalCallbackUrls = overrides.LocalCallbackUrls;
                if (!string.IsNullOrEmpty(overrides?.Audience))                                         Audience = overrides.Audience;

            }

            /// <summary>
            /// Unconfigured Authentication provider config.
            /// </summary>
            public static AuthorizationServerConfig CreateEmpty()
            {
                AuthorizationServerConfig config = new AuthorizationServerConfig();
                config.EnableCaching = true;
                config.LocalCallbackUrls = new List<string>() { };
                return config;
            }
        }

        public struct LoginResult
        {
            public string AccessToken;
            public string IdToken;
            public DateTime ExpiresAt;

            public LoginResult(string accessToken, string idToken, DateTime expiresAt)
            {
                AccessToken = accessToken;
                IdToken = idToken;
                ExpiresAt = expiresAt;
            }
        }

        /// <summary>
        /// Performs OAuth2 login and returns the access token. If no suitable token is found in cache, this will
        /// open the external browser for authorization.
        /// </summary>
        public static async Task<LoginResult> LoginAsync(AuthorizationServerConfig config, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(config.ClientId))
                throw new ArgumentNullException("config.ClientId must be set");
            if (string.IsNullOrEmpty(config.AuthorizationEndpoint))
                throw new ArgumentNullException("config.AuthorizationEndpoint must be set");
            if (string.IsNullOrEmpty(config.TokenEndpoint))
                throw new ArgumentNullException("config.TokenEndpoint must be set");
            if (string.IsNullOrEmpty(config.Audience))
                throw new ArgumentNullException("config.Audience must be set");
            if (config.LocalCallbackUrls == null || config.LocalCallbackUrls.Count == 0)
                throw new ArgumentNullException("config.LocalCallbackUrls must be set");

            string cacheKey = TryGetCacheKey(config);
            if (cacheKey != null)
            {
                LoginResult? cached = TryGetFromCache(cacheKey);
                if (cached != null && DateTime.UtcNow < cached.Value.ExpiresAt)
                    return cached.Value;
            }

            LoginResult result = await MetaTask.Run(async () => await DoLoginAsync(config.ClientId, config.AuthorizationEndpoint, config.TokenEndpoint, config.Audience, config.LocalCallbackUrls, ct), MetaTask.UnityMainScheduler);

            if (cacheKey != null)
            {
                TryStoreToCache(cacheKey, result);
            }

            return result;
        }

        /// <summary>
        /// Simulates successful login flow
        /// </summary>
        public static void SimulateSuccess()
        {
            byte[] response = MetaplayOAuth2ClientHtmlTemplates.RenderSuccess();
            _ = SimulateOnceAsync(response);
        }

        /// <summary>
        /// Simulates failing login flow
        /// </summary>
        public static void SimulateFailure()
        {
            byte[] response = MetaplayOAuth2ClientHtmlTemplates.RenderFailure("An example error message");
            _ = SimulateOnceAsync(response);
        }

        static async Task SimulateOnceAsync(byte[] response)
        {
            string simulationUrl = Invariant($"http://localhost:{UnityOAuth2Util.LocalhostCallbackPort}/");
            using (HttpListener http = new HttpListener())
            {
                http.Prefixes.Add(simulationUrl);
                http.Start();

                Application.OpenURL(simulationUrl + "oauth2/login");

                HttpListenerContext request = await http.GetContextAsync();
                await LoginCallbackHandler.Reply200WithHtmlAsync(request, response);
            }
        }

        static async Task<LoginResult> DoLoginAsync(string clientId, string authorizeUrl, string tokenUrl, string audience, List<string> loginCallbackUrls, CancellationToken ct)
        {
            bool hasProgressbar = false;
            LogChannel log = new LogChannel("oauth2", new UnityLogger(), new MetaLogLevelSwitch(LogLevel.Information));
            LoginCallbackHandler handler = new LoginCallbackHandler(log, clientId, authorizeUrl, tokenUrl, audience, loginCallbackUrls, ct);

            // Start local listener to test out the port is free
            try
            {
                // Start local listener. This chooses the free port and returns the login url
                string loginUrl = handler.Start(timeout: TimeSpan.FromMinutes(4));

                // Inform user that we are about to redirect so it doesn't come as a suprise.
                if (!EditorUtility.DisplayDialog("OAuth2 Redirect", "You will be redirected into login service with your default browser.", "Ok", "Cancel"))
                    throw new OperationCanceledException();
                Application.OpenURL(loginUrl);

                bool waitingForAuth = false;
                for (;;)
                {
                    hasProgressbar = true;
                    string text = (waitingForAuth) ? "Waiting for Authorization Server to provide login token." : "Waiting for Authorization to complete in the browser (4min).";
                    if (EditorUtility.DisplayCancelableProgressBar("Authenticating", text, 0.7f))
                    {
                        handler.Cancel();
                        throw new OperationCanceledException();
                    }

                    LoginCallbackHandler.Status status = await handler.TryWaitAsync(TimeSpan.FromMilliseconds(100));
                    switch (status)
                    {
                        case LoginCallbackHandler.Status.WaitingForBrowser:
                            break;

                        case LoginCallbackHandler.Status.WaitingForAuthServer:
                            waitingForAuth = true;
                            break;

                        case LoginCallbackHandler.Status.Cancelled:
                            throw new OperationCanceledException();

                        case LoginCallbackHandler.Status.Error:
                            throw handler.GetError();

                        case LoginCallbackHandler.Status.Complete:
                            return handler.GetResult();
                    }
                }
            }
            finally
            {
                if (hasProgressbar)
                    EditorUtility.ClearProgressBar();
            }
        }

        static string TryGetCacheKey(AuthorizationServerConfig config)
        {
            if (!config.EnableCaching)
                return null;
            return Util.ComputeSHA1(string.Join(";", config.ClientId, config.AuthorizationEndpoint, config.TokenEndpoint, config.Audience, string.Join("|", config.LocalCallbackUrls)));
        }

        static LoginResult? TryGetFromCache(string cacheKey)
        {
            try
            {
                string contents = File.ReadAllText($"{MetaplaySDK.UnityTempDirectory}/oauth-key-{cacheKey}.txt");
                string[] parts = contents.Split('.');
                if (parts.Length != 3)
                    return null;

                LoginResult result = new LoginResult(
                    accessToken: Encoding.UTF8.GetString(Convert.FromBase64String(parts[0])),
                    idToken: parts[1] == "" ? null : Encoding.UTF8.GetString(Convert.FromBase64String(parts[1])),
                    expiresAt: new DateTime(ticks: BitConverter.ToInt64(Convert.FromBase64String(parts[2]), 0), DateTimeKind.Utc));
                return result;
            }
            catch
            {
                return null;
            }
        }

        static void TryStoreToCache(string cacheKey, LoginResult result)
        {
            try
            {
                string idTokenString = result.IdToken != null ? Convert.ToBase64String(Encoding.UTF8.GetBytes(result.IdToken)) : "";
                string contents = $"{Convert.ToBase64String(Encoding.UTF8.GetBytes(result.AccessToken))}.{idTokenString}.{Convert.ToBase64String(BitConverter.GetBytes(result.ExpiresAt.Ticks))}";
                File.WriteAllText($"{MetaplaySDK.UnityTempDirectory}/oauth-key-{cacheKey}.txt", contents);
            }
            catch
            {
            }
        }

        class LoginCallbackHandler
        {
            public enum Status
            {
                NotStarted,
                WaitingForBrowser,
                WaitingForAuthServer,
                Cancelled,
                Error,
                Complete,
            }

            [Serializable]
            class OAuth2AccessTokenJson
            {
                #pragma warning disable CS0649
                [SerializeField] public string id_token;
                [SerializeField] public string access_token;
                [SerializeField] public string refresh_token;
                [SerializeField] public int expires_in;
                [SerializeField] public string token_type;
                [SerializeField] public string scope;
                #pragma warning restore CS0649
            }

            readonly LogChannel _log;
            readonly string _clientId;
            readonly string _authorizeUrl;
            readonly string _tokenUrl;
            readonly string _audience;
            readonly List<string> _loginCallbackUrls;
            readonly CancellationToken _outerCt;

            Task<LoginResult> _task;
            CancellationTokenSource _cts;
            bool _hasReceivedCodeFromClient;

            public LoginCallbackHandler(LogChannel log, string clientId, string authorizeUrl, string tokenUrl, string audience, List<string> loginCallbackUrls, CancellationToken ct)
            {
                _log = log;
                _clientId = clientId;
                _authorizeUrl = authorizeUrl;
                _tokenUrl = tokenUrl;
                _audience = audience;
                _loginCallbackUrls = new List<string>(loginCallbackUrls);
                _outerCt = ct;
            }

            /// <summary>
            /// Start login listener and returns the login URL where the user log in and authrorizes this app.
            /// </summary>
            public string Start(TimeSpan timeout)
            {
                List<Uri> callbackUris = new List<Uri>();
                foreach (string uriString in _loginCallbackUrls)
                    callbackUris.Add(new Uri(uriString));

                (HttpListener http, Uri callbackUri) = UnityOAuth2Util.CreateListenerForCallbackIntoEditor(callbackUris);

                string codeVerifier = UnityOAuth2Util.CreateCodeVerifier();
                string authRequestUrl =
                    _authorizeUrl
                    + $"?response_type=code"
                    + $"&audience={_audience}"
                    + $"&client_id={UnityWebRequest.EscapeURL(_clientId)}"
                    + $"&redirect_uri={UnityWebRequest.EscapeURL(callbackUri.ToString())}"
                    + $"&scope=openid+profile+email"
                    + $"&code_challenge_method=S256"
                    + $"&code_challenge={UnityOAuth2Util.Base64UrlEncode(UnityOAuth2Util.CreateCodeChallengeS256(codeVerifier))}";

                _cts = CancellationTokenSource.CreateLinkedTokenSource(_outerCt);
                _cts.CancelAfter(timeout);
                _task = MetaTask.Run(async () => await Worker(http, callbackUri.ToString(), _tokenUrl, codeVerifier, _cts.Token));
                return authRequestUrl;
            }

            public void Cancel()
            {
                _cts.Cancel();
            }

            public async Task<Status> TryWaitAsync(TimeSpan waitTimeout)
            {
                if (_task == null)
                    return Status.NotStarted;

                await Task.WhenAny(_task, MetaTask.Delay(waitTimeout));

                switch (_task.Status)
                {
                    case TaskStatus.RanToCompletion:
                        return Status.Complete;

                    case TaskStatus.Faulted:
                        return Status.Error;

                    case TaskStatus.Canceled:
                        return Status.Cancelled;
                }

                if (!_hasReceivedCodeFromClient)
                    return Status.WaitingForBrowser;
                else
                    return Status.WaitingForAuthServer;
            }

            public LoginResult GetResult() => _task.GetCompletedResult();
            public Exception GetError() => _task.Exception;

            async Task<LoginResult> Worker(HttpListener http, string loginCallbackUrl, string tokenUrl, string codeVerifier, CancellationToken ct)
            {
                using (http)
                {
                    // Wait for login flow to provide us the code
                    string code;
                    HttpListenerContext request;
                    for (;;)
                    {
                        // Wait for login to happen or cancellation to trigger.
                        request = await http.GetContextAsync().WithCancelAsync(ct);

                        // All other callbacks are ignored
                        if (Uri.Compare(request.Request.Url, new Uri(loginCallbackUrl), UriComponents.Scheme | UriComponents.UserInfo | UriComponents.Host | UriComponents.Port | UriComponents.Path, UriFormat.Unescaped, StringComparison.Ordinal) != 0)
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
                        string errorMaybe;
                        (code, errorMaybe) = UnityOAuth2Util.ParseCodeFromCallback(request.Request.Url.ToString());

                        if (errorMaybe != null)
                        {
                            byte[] html = MetaplayOAuth2ClientHtmlTemplates.RenderFailure(errorMaybe);
                            await Reply200WithHtmlAsync(request, html);
                            throw new InvalidOperationException($"Authorization server refused to provide code: {errorMaybe}");
                        }

                        _log.Info("Got callback from auth provider. Exchanging code for access token.");
                        break;
                    }

                    // We got callback from auth provider and have the code. Next, exchanging code for access token.
                    _hasReceivedCodeFromClient = true;

                    // Try use the code to exchange it for access token.
                    try
                    {
                        string tokenRequestParams =
                            "grant_type=authorization_code"
                            + $"&code={code}"
                            + $"&redirect_uri={UnityWebRequest.EscapeURL(loginCallbackUrl)}"
                            + $"&client_id={UnityWebRequest.EscapeURL(_clientId)}"
                            + $"&code_verifier={codeVerifier}";

                        OAuth2AccessTokenJson result = await UnityOAuth2Util.ExchangeCodeForAccessTokenAsync<OAuth2AccessTokenJson>(tokenUrl, tokenRequestParams);
                        if (result.access_token == null)
                            throw new InvalidOperationException($"Invalid result, missing access_token");
                        // id_token is optional

                        byte[] html = MetaplayOAuth2ClientHtmlTemplates.RenderSuccess();
                        await Reply200WithHtmlAsync(request, html);

                        return new LoginResult(
                            accessToken: result.access_token,
                            idToken: result.id_token,
                            expiresAt: DateTime.UtcNow + TimeSpan.FromSeconds(result.expires_in));
                    }
                    catch(Exception ex)
                    {
                        byte[] html = MetaplayOAuth2ClientHtmlTemplates.RenderFailure("Failed to fetch token: " + ex.ToString());
                        await Reply200WithHtmlAsync(request, html);
                        throw;
                    }
                }
            }

            public static async Task Reply200WithHtmlAsync(HttpListenerContext context, byte[] payload)
            {
                context.Response.StatusCode = 200;
                context.Response.ContentType = "text/html";
                await context.Response.OutputStream.WriteAsync(payload, 0, payload.Length);
                context.Response.OutputStream.Close();
            }
        }
    }
}
