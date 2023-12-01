// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Config;
using Metaplay.Core.IO;
using Metaplay.Core.Model;
using Metaplay.Core.Serialization;
using System;

namespace Metaplay.Core.Network
{
    /// <summary>
    /// Type of payload for a wire message. Bits 0..2 of flags.
    /// </summary>
    public enum WirePacketType
    {
        None                = 0,        // Empty flags
        Message             = 0x1,      // Payload is a MetaMessage
        Ping                = 0x2,      // Payload is an echo request
        PingResponse        = 0x3,      // Payload is an echo response
        HealthCheck         = 0x4,      // Payload is HealthCheckType bits
    }

    /// <summary>
    /// Compression type to use for payload. Bits 3..4 of flags.
    /// </summary>
    public enum WirePacketCompression
    {
        None            = 0x0,      // Payload not compressed.
        Deflate         = 0x1,      // Payload compressed using deflate (System.IO.Compression.DeflateStream)
    }

    /// <summary>
    /// Bit flags for health check types. Used both by request and response.
    /// </summary>
    ///
    [Flags]
    public enum HealthCheckTypeBits : int
    {
        GlobalState        = 0x1,      // Global state is accessible
        Database           = 0x2,      // Database is operational
    }

    /// <summary>
    /// Bit offsets and masks for flags in wire packet header.
    /// </summary>
    public static class WirePacketFlagBits
    {
        public const int TypeOffset         = 0;    // Bit offset for WirePacketType in flags
        public const int TypeMask           = 0x7;  // Mask for WirePacketType (unshifted)

        public const int CompressionOffset  = 3;    // Bit offset for WirePacketCompression in flags
        public const int CompressionMask    = 0x3;  // Bit mask for WirePacketCompression (unshifted)
    }

    /// <summary>
    /// Decoded header of a wire packet.
    /// </summary>
    public struct WirePacketHeader
    {
        public WirePacketType           Type;
        public WirePacketCompression    Compression;
        public int                      PayloadSize;

        public WirePacketHeader(WirePacketType type, WirePacketCompression compression, int payloadSize)
        {
            Type = type;
            Compression = compression;
            PayloadSize = payloadSize;
        }
    }

    /// <summary>
    /// Status of a client connection protocol.
    /// </summary>
    [MetaSerializable]
    public enum ProtocolStatus
    {
        // Client-side defined
        Pending,                        // Waiting for connection
        InvalidGameMagic,               // GameMagic identification failed
        WireProtocolVersionMismatch,    // WireProtocolVersion mismatch between client and server

        // Provided by server
        ClusterRunning,                 // Cluster is up and running, connections allowed.
        ClusterStarting,                // Cluster is still starting and doesn't yet accept connections. Try again soon.
        ClusterShuttingDown,            // Cluster is shutting down and doesn't accept connections anymore.
        [Obsolete("Please look for LoginMaintenanceFailure MetaMessage instead.")]
        InMaintenance,                  // Cluster is in maintenance mode, no connections allowed. (Deprecated)
    }

    /// <summary>
    /// Utilities for handling wire protocol.
    /// </summary>
    public static class WireProtocol
    {
        public const int    CompressionThresholdBytes     = 10 * 1024;   // Packets smaller than this won't be compressed.

        public const int    ProtocolHeaderSize            = 8;            // Size of the protocol header (sent by the server at the beginning of each connection).
        public const int    MinClientWireProtocolVersion  = 10;           // Minimum supported core protocol version by client.
        public const int    MaxClientWireProtocolVersion  = 10;           // Maximum supported core protocol version by client.
        // \note: For maximum compatibility with released clients, server advertises MinWireProtocolVersion here.
        public const int    WireProtocolVersion           = MinClientWireProtocolVersion; // Current wire protocol version (advertised by server).

        public const int    PacketHeaderSize              = 4;

        // \note If you change these limits for the purpose of supporting larger PlayerModels,
        //       please consider also the value configured for maximum-frame-size in Akka.Net
        //       (see Application.GenerateAkkaConfig on the server). That value also needs to
        //       be large enough so the backend can internally pass around large PlayerModels.
        public const int    MaxPacketWirePayloadSize            = 1024 * 1024;      // Limit for the on-wire payload, which may be compressed.
        public const int    MaxPacketUncompressedPayloadSize    = 5 * 1024 * 1024;  // Limit for the uncompressed form of the payload.

        /// <summary>
        /// Encodes the global protocol header. The header is always the initial packet sent by the server
        /// at the beginning of a connection, to make sure that the client is talking to the right server.
        /// The header includes information about the current state of the system (see <see cref="ProtocolStatus"/>)
        /// as well as the WireProtocolVersion used by the server.
        /// </summary>
        /// <param name="protocolStatus">Current state of the servers</param>
        /// <returns>The protocol header encoded as byte array</returns>
        public static byte[] EncodeProtocolHeader(ProtocolStatus protocolStatus, string gameMagic)
        {
            return new byte[]
            {
                (byte)gameMagic[0], (byte)gameMagic[1], (byte)gameMagic[2], (byte)gameMagic[3],
                WireProtocolVersion,
                (byte)protocolStatus,
                0, 0,
            };
        }

        public static ProtocolStatus ParseProtocolHeader(byte[] buffer, int offset, string expectedGameMagic)
        {
            // Check GameMagic first
            if (buffer[offset+0] != expectedGameMagic[0] || buffer[offset+1] != expectedGameMagic[1] || buffer[offset+2] != expectedGameMagic[2] || buffer[offset+3] != expectedGameMagic[3])
                return ProtocolStatus.InvalidGameMagic;

            int serverProtocolVersion = buffer[offset + 4];
            if (serverProtocolVersion < MinClientWireProtocolVersion || serverProtocolVersion > MaxClientWireProtocolVersion)
                return ProtocolStatus.WireProtocolVersionMismatch;

            ProtocolStatus systemStatus = (ProtocolStatus)buffer[offset + 5];
            return systemStatus;
        }

        public static WirePacketHeader DecodePacketHeader(byte[] buffer, int offset, bool enforcePacketPayloadSizeLimit)
        {
            byte flags = buffer[offset];
            WirePacketType packetType = (WirePacketType)((flags >> WirePacketFlagBits.TypeOffset) & WirePacketFlagBits.TypeMask);
            WirePacketCompression compression = (WirePacketCompression)((flags >> WirePacketFlagBits.CompressionOffset) & WirePacketFlagBits.CompressionMask);

            int payloadSize = (buffer[offset + 1] << 16) + (buffer[offset + 2] << 8) + buffer[offset + 3];
            if (payloadSize < 0)
                throw new InvalidOperationException($"Negative encoded packet size: {payloadSize}");
            if (enforcePacketPayloadSizeLimit && payloadSize > MaxPacketWirePayloadSize)
                throw new InvalidOperationException($"Too large encoded packet size: {payloadSize}");

            return new WirePacketHeader(packetType, compression, payloadSize);
        }

        public static void EncodePacketHeader(WirePacketHeader header, byte[] buffer, int dstIndex = 0)
        {
            uint flags = ((uint)header.Type << WirePacketFlagBits.TypeOffset) | ((uint)header.Compression << WirePacketFlagBits.CompressionOffset);
            buffer[dstIndex+0] = (byte)flags;
            buffer[dstIndex+1] = (byte)(header.PayloadSize >> 16);
            buffer[dstIndex+2] = (byte)(header.PayloadSize >> 8);
            buffer[dstIndex+3] = (byte)(header.PayloadSize >> 0);
        }

        public static MetaMessage DecodeMessage(byte[] buffer, int payloadOffset, int payloadSize, IGameConfigDataResolver resolver)
        {
            try
            {
                using (IOReader reader = new IOReader(buffer, payloadOffset, payloadSize))
                {
                    MetaMessage message = MetaSerialization.DeserializeTagged<MetaMessage>(reader, MetaSerializationFlags.SendOverNetwork, resolver, logicVersion: null);
                    return message;
                }
            }
            catch (Exception ex)
            {
                string payloadBytes = Convert.ToBase64String(buffer, payloadOffset, payloadSize);
                string msgName = MetaSerializationUtil.PeekMessageName(buffer, payloadOffset);
                throw new MetaSerializationException($"Failed to decode message ({msgName}, payloadSize={payloadSize}): {payloadBytes}", ex);
            }
        }
    }
}
