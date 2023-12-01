// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using System;

namespace Metaplay.Server.UdpPassthrough
{
    /// <summary>
    /// Utility for retrieving the public gateways of the UDP passthrough.
    /// </summary>
    public static class UdpPassthroughGateways
    {
        public readonly struct Gateway
        {
            /// <summary>
            /// The publicly accessible domain or address of the gateway to the passthrough listener.
            /// </summary>
            public readonly string FullyQualifiedDomainNameOrAddress;

            /// <summary>
            /// The publicly accessible port of the gateway to the passthrough listener.
            /// </summary>
            public readonly int Port;

            /// <summary>
            /// The EntityId of the passthrough server host entity.
            /// </summary>
            public readonly EntityId AssociatedEntityId;

            public Gateway(string fullyQualifiedDomainNameOrAddress, int port, EntityId associatedEntityId)
            {
                FullyQualifiedDomainNameOrAddress = fullyQualifiedDomainNameOrAddress;
                Port = port;
                AssociatedEntityId = associatedEntityId;
            }
        }

        internal static Gateway[] _gateways = Array.Empty<Gateway>();

        /// <summary>
        /// Returns the public gateways of the UDP Passthrough. Returns an empty set
        /// if UDP services haven't been set up or if UDP passthrough is not enabled.
        /// </summary>
        public static Gateway[] GetPublicGateways() => _gateways;
    }
}
