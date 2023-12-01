// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Cloud.Services;
using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Core.Web3;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace Metaplay.Server.Web3
{
    [MetaSerializableDerived(2)]
    public class DownloadNftMetadatasTask : BackgroundTask
    {
        [MetaSerializableDerived(2)]
        public class Output : IBackgroundTaskOutput
        {
            [MetaMember(1)] public int NumDownloaded;
            [MetaMember(2)] public OrderedDictionary<NftId, byte[]> Metadatas;

            Output() { }
            public Output(int numDownloaded, OrderedDictionary<NftId, byte[]> metadatas)
            {
                NumDownloaded = numDownloaded;
                Metadatas = metadatas;
            }
        }

        readonly static HttpClient _httpClient = new HttpClient();

        [MetaMember(1)] public NftCollectionId CollectionId;
        [MetaMember(2)] public NftId FirstTokenId;
        [MetaMember(3)] public int NumTokens;

        DownloadNftMetadatasTask() { }

        public DownloadNftMetadatasTask(NftCollectionId collectionId, NftId firstTokenId, int numTokens)
        {
            CollectionId = collectionId;
            FirstTokenId = firstTokenId;
            NumTokens = numTokens;
        }

        public override async Task<IBackgroundTaskOutput> Run(BackgroundTaskContext context)
        {
            Web3Options web3Options = RuntimeOptionsRegistry.Instance.GetCurrent<Web3Options>();
            Web3Options.NftCollectionSpec collection = web3Options.GetNftCollection(CollectionId);

            IEnumerable<NftId> nftIds = NftUtil.GetNftIdRange(FirstTokenId, NumTokens);

            OrderedDictionary<NftId, byte[]> nftMetadatas = new();

            context.UpdateTaskOutput(new Output(numDownloaded: 0, metadatas: null));

            foreach ((NftId nftId, int nftIndex) in nftIds.ZipWithIndex())
            {
                string metadataUrl = web3Options.GetNftMetadataUrl(collection, nftId);

                byte[] metadataBytes;

                const int numRetries = 3;
                int tryNdx = 0;
                while (true)
                {
                    try
                    {
                        metadataBytes = await HttpUtil.RequestRawGetAsync(_httpClient, metadataUrl);
                        break;
                    }
                    catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                    {
                        throw new Exception($"Metadata not found for token {nftId} at {metadataUrl} .", ex);
                    }
                    catch (HttpRequestException ex)
                    {
                        if (tryNdx >= numRetries)
                            throw new Exception($"Failure while downloading metadata for NFT {nftId} from {metadataUrl}. Attempted {tryNdx+1} times.", ex);
                        tryNdx++;
                    }
                }

                nftMetadatas.Add(nftId, metadataBytes);

                if ((nftIndex + 1) % 10 == 0)
                    context.UpdateTaskOutput(new Output(numDownloaded: nftIndex + 1, metadatas: null));
            }

            return new Output(numDownloaded: NumTokens, nftMetadatas);
        }
    }
}
