// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;

namespace Metaplay.Cloud.Web3
{
    public enum EthereumNetwork
    {
        /// <summary>
        /// No network
        /// </summary>
        None,

        /// <summary>
        /// Ethereum mainnet (production)
        /// </summary>
        EthereumMainnet,

        /// <summary>
        /// Ropsten Test network (testing only)
        /// </summary>
        RopstenTestnet,

        /// <summary>
        /// Görli Test network (testing only)
        /// </summary>
        GoerliTestnet,
    }

    public readonly struct EthereumNetworkProperties
    {
        /// <summary>
        /// The Network ID of the Ethereum network
        /// </summary>
        public readonly int NetworkId;

        /// <summary>
        /// The ChainId ID of the Ethereum network
        /// </summary>
        public readonly int ChainId;

        /// <summary>
        /// ImmutableX API URL, or null if the network has no ImmutableX API.
        /// </summary>
        public readonly string ImmutableXApiUrl;

        /// <summary>
        /// ImmutableX Link SDK API URL, or null if the network has no URL for ImmutableX Link SDK.
        /// </summary>
        public readonly string ImmutableXLinkApiUrl;

        EthereumNetworkProperties(int networkId, int chainId, string immutableXApiUrl, string immutableXLinkApiUrl)
        {
            NetworkId = networkId;
            ChainId = chainId;
            ImmutableXApiUrl = immutableXApiUrl;
            ImmutableXLinkApiUrl = immutableXLinkApiUrl;
        }

        /// <summary>
        /// Retrieves network properties for any non-None network. If <paramref name="network"/> is <see cref="EthereumNetwork.None"/>,
        /// returns <c>null</c>.
        /// </summary>
        public static EthereumNetworkProperties? TryGetPropertiesForNetwork(EthereumNetwork network)
        {
            switch (network)
            {
                case EthereumNetwork.EthereumMainnet:
                    return new EthereumNetworkProperties(
                        networkId:              1,
                        chainId:                1,
                        immutableXApiUrl:       "https://api.x.immutable.com/v1",
                        immutableXLinkApiUrl:   "https://link.x.immutable.com"
                        );

                case EthereumNetwork.RopstenTestnet:
                    return new EthereumNetworkProperties(
                        networkId:              3,
                        chainId:                3,
                        immutableXApiUrl:       "https://api.ropsten.x.immutable.com/v1",
                        immutableXLinkApiUrl:   "https://link.ropsten.x.immutable.com"
                        );

                case EthereumNetwork.GoerliTestnet:
                    return new EthereumNetworkProperties(
                        networkId:              5,
                        chainId:                5,
                        immutableXApiUrl:       "https://api.sandbox.x.immutable.com/v1",
                        immutableXLinkApiUrl:   "https://link.sandbox.x.immutable.com"
                        );

                default:
                    return null;
            }
        }

        /// <summary>
        /// Retrieves network properties for any non-None network. If <paramref name="network"/> is <see cref="EthereumNetwork.None"/>,
        /// <see cref="ArgumentException"/> is thrown.
        /// </summary>
        public static EthereumNetworkProperties GetPropertiesForNetwork(EthereumNetwork network)
        {
            EthereumNetworkProperties? properties = TryGetPropertiesForNetwork(network);
            if (properties.HasValue)
                return properties.Value;
            throw new ArgumentException($"No such Ethereum network: {network}.");
        }
    }
}
