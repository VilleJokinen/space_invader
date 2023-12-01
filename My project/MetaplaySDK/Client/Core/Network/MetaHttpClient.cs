// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if UNITY_WEBGL && !UNITY_EDITOR
#   define UNITY_WEBGL_BUILD
#endif

// Use UnityWebRequest in WebGL builds (HttpClient won't work)
#if UNITY_WEBGL_BUILD
#   define USE_UNITY_HTTP
#endif

using Metaplay.Core.Tasks;
using System;
using System.Net;
using System.Threading;
using static System.FormattableString;

#if USE_UNITY_HTTP
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using UnityEngine.Networking;
#else
using System.Net.Http;
using System.Threading.Tasks;
#endif

namespace Metaplay.Core.Network
{
    /// <summary>
    /// Response from <see cref="MetaHttpClient"/>. Also includes requests where server responded with a
    /// failure code (non-2xx). Requests that fail to get a response from the server (eg, due to network
    /// issues) throw the <see cref="MetaHttpRequestError"/> exception.
    ///
    /// For now, only HTTP GET verb is supported, more can be added later.
    /// </summary>
    public class MetaHttpResponse : IDisposable
    {
        public HttpStatusCode   StatusCode  { get; private set; }   // HTTP status code of the response
        public byte[]           Content     { get; private set; }   // Bytes of the response body -- may be null!

        public bool IsSuccessStatusCode => (int)StatusCode >= 200 && (int)StatusCode < 300;

        public MetaHttpResponse(HttpStatusCode statusCode, byte[] content)
        {
            StatusCode = statusCode;
            Content = content;
        }

        public void Dispose()
        {
            // \note Not really needed but implement IDisposable to keep API more compatible with HttpClient
            StatusCode = 0;
            Content = null;
        }

        public override string ToString() => Invariant($"MetaHttpResponse: statusCode={StatusCode}, content length={Content?.Length}");
    }

    /// <summary>
    /// Error occurred while making a HTTP request. This is typically caused by various networking issues.
    /// Any failures returned by the HTTP server (non-2xx status codes) are returned as <see cref="MetaHttpResponse"/>.
    /// </summary>
    public class MetaHttpRequestError : Exception
    {
        public MetaHttpRequestError(string reason, Exception innerException = null) : base(reason, innerException)
        {
        }
    }

    /// <summary>
    /// Simple platform-specific HTTP client library wrapper. Can use either <c>System.Net.HttpClient</c>
    /// or <c>UnityWebRequest</c> (only one that works with WebGL). Only supports the basic operations
    /// required by the Metaplay client SDK (eg, no specifying headers, no response streaming, only supports
    /// GET for now, etc.). More features to be added as required. Only intended to be used in client builds
    /// for WebGL compatibility, not on the server or in the Unity Editor tools.
    /// </summary>
    public partial class MetaHttpClient : IDisposable
    {
        /// <summary>
        /// Default instance that can be used everywhere. Can also create your own instance.
        /// </summary>
        public static readonly MetaHttpClient DefaultInstance = new MetaHttpClient();

        public Task<MetaHttpResponse> GetAsync(string requestUri) => GetAsync(requestUri, CancellationToken.None);
    }

#if USE_UNITY_HTTP
    public partial class MetaHttpClient : IDisposable
    {
        public MetaHttpClient()
        {
        }

        public void Dispose()
        {
        }

        public async Task<MetaHttpResponse> GetAsync(string requestUri, CancellationToken ct)
        {
            //Metaplay.Core.DebugLog.Info("GetAsync(): {0}", requestUri);
            using (UnityWebRequest request = UnityWebRequest.Get(requestUri))
            {
                Task<UnityWebRequest> fetchTask =
                    AsTask(request.SendWebRequest())
                    .WithCancelAsync(ct);

                // If cancel is triggered, abort the request
                _ = fetchTask.ContinueWithCtx(_ => { request.Abort(); }, TaskContinuationOptions.OnlyOnCanceled);

                await fetchTask;

                //Metaplay.Core.DebugLog.Info("GetAsync(): handle result {0}", request.result);
                switch (request.result)
                {
                    case UnityWebRequest.Result.ConnectionError:
                        throw new MetaHttpRequestError($"Request to '{requestUri}' failed with ConnectionError: {request.error}");
                    case UnityWebRequest.Result.DataProcessingError:
                        throw new MetaHttpRequestError($"Request to '{requestUri}' failed with DataProcessingError: {request.error}");
                    case UnityWebRequest.Result.ProtocolError:
                        return new MetaHttpResponse((HttpStatusCode)request.responseCode, request.downloadHandler.data);
                    case UnityWebRequest.Result.Success:
                        return new MetaHttpResponse((HttpStatusCode)request.responseCode, request.downloadHandler.data);
                    default:
                        throw new MetaHttpRequestError($"Request to '{requestUri}' failed with invalid result: {request.result}");
                }
            }
        }

        static async Task<UnityWebRequest> AsTask(UnityWebRequestAsyncOperation op)
        {
            return await op;
        }
    }
#else
#pragma warning disable MP_WGL_00 // "Feature is poorly supported in WebGL" (regarding HttpClient). False positive, this is non-WebGL.
    public partial class MetaHttpClient : IDisposable
    {
        HttpClient _httpClient = new HttpClient();

        public MetaHttpClient()
        {
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }

        public async Task<MetaHttpResponse> GetAsync(string requestUri, CancellationToken ct)
        {
            try
            {
                using (HttpResponseMessage response = await _httpClient.GetAsync(requestUri, ct).ConfigureAwait(false))
                {
                    // Read content also in case of errors
                    byte[] content = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    return new MetaHttpResponse(response.StatusCode, content);
                }
            }
            catch (HttpRequestException ex)
            {
                // Remap network errors to platform-agnostic exception
                throw new MetaHttpRequestError($"Request to '{requestUri}' failed: {ex.Message}", ex);
            }
        }
    }
#pragma warning restore MP_WGL_00
#endif
}
