// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud;
using Metaplay.Cloud.Services;
using Metaplay.Cloud.Web3;
using Metaplay.Core;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Threading.Tasks;

namespace Metaplay.Server.Web3
{
    public static class ImmutableXApi
    {
        readonly static HttpClient          s_httpClient        = HttpUtil.CreateJsonHttpClient();
        readonly static IMetaLogger         s_log               = MetaLogger.ForContext(typeof(ImmutableXApi));
        readonly static Prometheus.Counter  c_apiCallsIssued    = Prometheus.Metrics.CreateCounter("immutablex_api_call_requests_total", "Cumulative number of ImmutableX API calls issued (including paging but no retries)", "op");
        readonly static Prometheus.Counter  c_apiCallResults    = Prometheus.Metrics.CreateCounter("immutablex_api_call_responses_total", "Cumulative number of ImmutableX API call responses received (including paging and retries) by the HTTP status code", "op", "statuscode");

        /// <summary>
        /// Calls ImmutableX API. Handles injection of optional API key and retries.
        /// </summary>
        static async Task<TResponse> CallApiAsync<TResponse>(Web3Options web3Options, string opLabel, Func<HttpRequestMessage> makeRequest)
        {
            c_apiCallsIssued.WithLabels(opLabel).Inc();

            int retryNum = 0;
            for (;;)
            {
                try
                {
                    using (HttpRequestMessage request = makeRequest())
                    {
                        if (!string.IsNullOrEmpty(web3Options.ImmutableXApiKey))
                            request.Headers.Add("x-api-key", web3Options.ImmutableXApiKey);

                        TResponse response = await HttpUtil.RequestAsync<TResponse>(s_httpClient, request);
                        c_apiCallResults.WithLabels(opLabel, "200").Inc();
                        return response;
                    }
                }
                catch (HttpRequestException httpEx) when (httpEx.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    c_apiCallResults.WithLabels(opLabel, "429").Inc();

                    retryNum++;
                    const int maxRetries = 3;
                    if (retryNum < maxRetries)
                    {
                        TimeSpan delay = TimeSpan.FromSeconds(1 << retryNum);
                        s_log.Warning("Got rate limited when accessing IMX API on {ApiUrl}. Retry {RetryNum}/{MaxRetries} in {Delay}.", web3Options.ImmutableXApiUrl, retryNum, maxRetries, delay);
                        await Task.Delay(delay);
                        continue;
                    }
                    else
                    {
                        s_log.Error("Exceeded maximum number of retries to IMX API on {ApiUrl}.", web3Options.ImmutableXApiUrl);
                        throw;
                    }
                }
                catch (HttpRequestException otherHttpEx)
                {
                    int statusCode = 0;
                    if (otherHttpEx.StatusCode.HasValue)
                        statusCode = (int)otherHttpEx.StatusCode.Value;
                    c_apiCallResults.WithLabels(opLabel, statusCode.ToString(CultureInfo.InvariantCulture)).Inc();
                    throw;
                }
                catch
                {
                    int statusCode = 0;
                    c_apiCallResults.WithLabels(opLabel, statusCode.ToString(CultureInfo.InvariantCulture)).Inc();
                    throw;
                }
            }
        }

        public abstract class PagedResponseBase
        {
            [JsonProperty("cursor")] public string Cursor { get; set; }
            [JsonProperty("remaining")] public int HasAnyRemainingFlag { get; set; }
        }

        /// <summary>
        /// Calls Paged ImmutableX API with GET request and returns all response pages. Handles injection of optional API key and retries.
        /// </summary>
        static async IAsyncEnumerable<TPage> GetPagedAsync<TPage>(Web3Options web3Options, string opLabel, string url) where TPage : PagedResponseBase
        {
            TPage firstPage = await CallApiAsync<TPage>(web3Options, opLabel, () => new HttpRequestMessage(HttpMethod.Get, url));
            yield return firstPage;

            string cursor = firstPage.Cursor;
            bool hasAnyRemaining = firstPage.HasAnyRemainingFlag != 0;

            // prepare for "cursor=XXX" append
            string urlWithFragmentSeparator;
            if (url.Contains('?'))
                urlWithFragmentSeparator = url + "&";
            else
                urlWithFragmentSeparator = url + "?";

            while (!string.IsNullOrEmpty(cursor) && hasAnyRemaining)
            {
                TPage followupPage = await CallApiAsync<TPage>(web3Options, opLabel, () => new HttpRequestMessage(HttpMethod.Get, $"{urlWithFragmentSeparator}cursor={cursor}"));
                yield return followupPage;

                cursor = followupPage.Cursor;
                hasAnyRemaining = followupPage.HasAnyRemainingFlag != 0;
            }
        }

        struct UsersResponse
        {
            [JsonProperty("accounts")] public List<string> Accounts { get; set; }
        }

        /// <summary>
        /// Retrieves the IMX accounts (users) associated with the Ethereum address.
        /// </summary>
        public static async Task<StarkPublicKey[]> GetImxAccountsAsync(Web3Options web3Options, EthereumAddress ethAccount)
        {
            UsersResponse response = await CallApiAsync<UsersResponse>(web3Options, opLabel: "GetImxAccounts", () => new HttpRequestMessage(HttpMethod.Get, $"{web3Options.ImmutableXApiUrl}/users/{ethAccount.GetAddressString()}"));
            List<StarkPublicKey> imxKeys = new List<StarkPublicKey>();
            if (response.Accounts != null)
            {
                foreach (string imxAccountStr in response.Accounts)
                {
                    StarkPublicKey imxAccount = StarkPublicKey.FromString(imxAccountStr);
                    imxKeys.Add(imxAccount);
                }
            }
            return imxKeys.ToArray();
        }

        class SingleUserAssetsResponse : PagedResponseBase
        {
            public struct Response
            {
                [JsonProperty("status")] public string Status { get; set; }
                [JsonProperty("token_id")] public string TokenId { get; set; }
            }

            [JsonProperty("result")] public List<Response> Results { get; set; }
        }

        /// <summary>
        /// Retrieves the NFT instances of a collection (identified by its <paramref name="ntfContract"/>) owned by a
        /// certain user (identified by its <paramref name="userAddress"/>). The returned NFT token IDs are returned in
        /// an ascending order.
        /// </summary>
        public static async Task<Erc721TokenId[]> GetNftTokensInWalletAsync(Web3Options web3Options, EthereumAddress nftContract, EthereumAddress userAddress)
        {
            List<SingleUserAssetsResponse.Response> results = new List<SingleUserAssetsResponse.Response>();
            await foreach (SingleUserAssetsResponse page in GetPagedAsync<SingleUserAssetsResponse>(web3Options, opLabel: "GetNftTokensInWallet", url: $"{web3Options.ImmutableXApiUrl}/assets?collection={nftContract.GetAddressString()}&user={userAddress.GetAddressString()}"))
            {
                results.AddRange(page.Results);
            }

            List<Erc721TokenId> tokens = new List<Erc721TokenId>(capacity: results.Count);
            foreach (SingleUserAssetsResponse.Response asset in results)
            {
                Erc721TokenId token = Erc721TokenId.FromDecimalString(asset.TokenId);
                tokens.Add(token);
            }

            tokens.Sort();

            for (int ndx = 1; ndx < tokens.Count; ++ndx)
            {
                if (tokens[ndx-1] == tokens[ndx])
                    throw new InvalidOperationException("IMX API returned multiple copies of a same token");
            }

            return tokens.ToArray();
        }

        class SingleAssetResponse
        {
            [JsonProperty("user")] public string User { get; set; }
        }

        public static async Task<EthereumAddress?> TryGetNftTokenOwnerAddressAsync(Web3Options web3Options, EthereumAddress nftContract, Erc721TokenId tokenId)
        {
            SingleAssetResponse response;
            try
            {
                response = await CallApiAsync<SingleAssetResponse>(web3Options, opLabel: "GetNftTokenDetails", () => new HttpRequestMessage(HttpMethod.Get, $"{web3Options.ImmutableXApiUrl}/assets/{nftContract.GetAddressString()}/{tokenId.GetTokenIdString()}"));
            }
            catch (HttpRequestException httpEx) when (httpEx.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }

            return EthereumAddress.FromStringWithoutChecksumCasing(response.User);
        }

        public readonly struct TokenUpdate
        {
            public readonly Erc721TokenId TokenId;
            public readonly EthereumAddress User;
            public readonly DateTime Timestamp;

            public TokenUpdate(Erc721TokenId tokenId, EthereumAddress user, DateTime timestamp)
            {
                TokenId = tokenId;
                User = user;
                Timestamp = timestamp;
            }
        }

        class TokenUpdatesResponse : PagedResponseBase
        {
            public struct Response
            {
                [JsonProperty("status")] public string Status { get; set; }
                [JsonProperty("token_id")] public string TokenId { get; set; }
                [JsonProperty("user")] public string User { get; set; }
                [JsonProperty("updated_at")] public string UpdatedAt { get; set; }
            }

            [JsonProperty("result")] public List<Response> Results { get; set; }
        }

        /// <summary>
        /// Retrieves the NFT instance updates within the timespan [<paramref name="fromDate"/> .. <paramref name="toDate"/>]. Note that the range is closed, i.e. inclusive.
        /// </summary>
        public static async Task<TokenUpdate[]> GetTokenUpdatesInTimeRangeAsync(Web3Options web3Options, EthereumAddress nftContract, DateTime fromDate, DateTime toDate)
        {
            if (fromDate.Kind != DateTimeKind.Utc)
                throw new ArgumentException("time must be UTC kind", nameof(fromDate));
            if (toDate.Kind != DateTimeKind.Utc)
                throw new ArgumentException("time must be UTC kind", nameof(toDate));

            List<TokenUpdatesResponse.Response> results = new List<TokenUpdatesResponse.Response>();
            await foreach (TokenUpdatesResponse page in GetPagedAsync<TokenUpdatesResponse>(web3Options, opLabel: "GetTokenUpdates", url: $"{web3Options.ImmutableXApiUrl}/assets?collection={nftContract.GetAddressString()}&updated_min_timestamp={fromDate.ToString("o", CultureInfo.InvariantCulture)}&updated_max_timestamp={toDate.ToString("o", CultureInfo.InvariantCulture)}"))
            {
                results.AddRange(page.Results);
            }

            List<TokenUpdate> updates = new List<TokenUpdate>(capacity: results.Count);
            foreach (TokenUpdatesResponse.Response asset in results)
            {
                Erc721TokenId token = Erc721TokenId.FromDecimalString(asset.TokenId);
                EthereumAddress user = EthereumAddress.FromStringWithoutChecksumCasing(asset.User);
                DateTime updatedAt = DateTime.ParseExact(asset.UpdatedAt, "yyyy-MM-ddTHH\\:mm\\:ss.FFFFFFFZ", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

                updates.Add(new TokenUpdate(token, user, updatedAt));
            }

            return updates.ToArray();
        }

        public class CollectionInfo
        {
            public string Name;
            public string Description;
            public string IconUrl;
            public string CollectionImageUrl;
            public string MetadataApiUrl;

            public CollectionInfo(string name, string description, string iconUrl, string collectionImageUrl, string metadataApiUrl)
            {
                Name = name;
                Description = description;
                IconUrl = iconUrl;
                CollectionImageUrl = collectionImageUrl;
                MetadataApiUrl = metadataApiUrl;
            }
        }

        class CollectionDetailsResponse
        {
            [JsonProperty("name")] public string Name { get; set; }
            [JsonProperty("description")] public string Description { get; set; }
            [JsonProperty("icon_url")] public string IconUrl { get; set; }
            [JsonProperty("collection_image_url")] public string CollectionImageUrl { get; set; }
            [JsonProperty("metadata_api_url")] public string MetadataApiUrl { get; set; }
        }

        /// <summary>
        /// Retrieves Immutable X's info about the collection identified by <paramref name="nftContract"/>,
        /// or returns null if no such collection exists in Immutable X.
        /// </summary>
        public static async Task<CollectionInfo> TryGetCollectionInfoAsync(Web3Options web3Options, EthereumAddress nftContract)
        {
            CollectionDetailsResponse response;
            try
            {
                response = await CallApiAsync<CollectionDetailsResponse>(web3Options, opLabel: "GetCollectionDetails", () => new HttpRequestMessage(HttpMethod.Get, $"{web3Options.ImmutableXApiUrl}/collections/{nftContract.GetAddressString()}"));
            }
            catch (HttpRequestException httpEx) when (httpEx.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }

            return new CollectionInfo(
                name:               response.Name,
                description:        response.Description,
                iconUrl:            response.IconUrl,
                collectionImageUrl: response.CollectionImageUrl,
                metadataApiUrl:     response.MetadataApiUrl);
        }
    }
}
