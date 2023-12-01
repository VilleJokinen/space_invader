// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Google.Apis.Auth.OAuth2;
using Metaplay.Core.Tasks;
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Metaplay.Unity
{
    public static class AdminApiAuthorizationUtil
    {
        class GoogleIdTokenResponse
        {
            public string token = null;
        }

        // \note The GCP project must have 'Identity and Access Management(IAM) API' enabled *and*
        //       the service account must have the role 'roles/iam.serviceAccountTokenCreator' to be able to create OpenID tokens.
        // See: https://cloud.google.com/iam/docs/granting-roles-to-service-accounts
        static async Task<string> GetGoogleIdTokenAsync(ServiceAccountCredential credential, string accessToken)
        {
            using (HttpClient httpClient = new HttpClient())
            {
                string url = $"https://iamcredentials.googleapis.com/v1/projects/-/serviceAccounts/{credential.Id}:generateIdToken";
                using (var request = new HttpRequestMessage(HttpMethod.Post, url))
                {
                    // Make POST request to fetch idToken
                    string body = $"{{ \"delegates\": [], \"audience\": \"{credential.Id}\", \"includeEmail\": \"true\" }}";
                    request.Content = new StringContent(body);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                    using (HttpResponseMessage response = await httpClient.SendAsync(request))
                    {
                        response.EnsureSuccessStatusCode();

                        // Parse token from response
                        string json = await response.Content.ReadAsStringAsync();
                        GoogleIdTokenResponse idToken = JsonUtility.FromJson<GoogleIdTokenResponse>(json);
                        if (string.IsNullOrEmpty(idToken.token))
                            throw new InvalidOperationException($"Received invalid idToken response: {json}");
                        return idToken.token;
                    }
                }
            }
        }

        public static Task<string> FetchGoogleIdTokenAsync(string credentialsPath)
        {
            // Use a Task to avoid deadlocking Unity.
            return MetaTask.Run(async () => await InternalFetchGoogleIdTokenAsync(credentialsPath), MetaTask.BackgroundScheduler);
        }

        public static async Task<string> InternalFetchGoogleIdTokenAsync(string credentialsPath)
        {
            using (var stream = new FileStream(credentialsPath, FileMode.Open, FileAccess.Read))
            {
                try
                {
                    // Get credentials
                    string[] scopes = new string[] { "openid profile", "https://www.googleapis.com/auth/cloud-platform" };
                    ServiceAccountCredential credential = (ServiceAccountCredential)GoogleCredential
                        .FromStream(stream)
                        .CreateScoped(scopes)
                        .UnderlyingCredential;

                    // Fetch Google-specific access token, which can be used to generate JWT
                    string accessToken = await credential.GetAccessTokenForRequestAsync();
                    if (string.IsNullOrEmpty(accessToken))
                        throw new InvalidOperationException($"Unable to get Google access token for service account {credential.Id}");

                    // Fetch and return OAuth idToken (JWT)
                    return await GetGoogleIdTokenAsync(credential, accessToken);
                }
                catch (Exception)
                {
                    Debug.LogError("Failed to fetch OpenID token for GCP service account, make sure the GCP project has IAM API enabled *and* the service account has role 'roles/iam.serviceAccountTokenCreator'");
                    throw;
                }
            }
        }
    }
}
