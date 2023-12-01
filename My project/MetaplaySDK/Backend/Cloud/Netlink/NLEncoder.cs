// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Netlink.Messages;
using Metaplay.Cloud.Netlink.Messages.SockDiag;
using System;

namespace Metaplay.Cloud.Netlink
{
    /// <summary>
    /// Encodes netlink message into byte packet stream.
    /// </summary>
    public static class Encoder
    {
        /// <summary>
        /// Appends a netlink message if it fits and returns true. If packet does not fit, returns false.
        /// </summary>
        public static bool TryAppendMessage(NetlinkPacket into, MessageFlags flags, UInt32 seq, in InetDiagReqV2 message)
        {
            uint postHeaderPadding = AlignUtil.GetTrailingPaddingSize(NlMsgHdr.SizeOfCStruct);
            NlMsgHdr header = new NlMsgHdr
            {
                Length = NlMsgHdr.SizeOfCStruct + postHeaderPadding + InetDiagReqV2.SizeOfCStruct,
                Type = MessageType.SockDiagByFamily,
                Flags = flags,
                Seq = seq,
                Pid = 0
            };

            int preceedingPadding = (int)AlignUtil.GetTrailingPaddingSize((uint)into.ContentLength);
            int totalSize = preceedingPadding + (int)header.Length;
            int bytesAvailable = into.Buffer.Length - into.ContentLength;
            if (bytesAvailable < totalSize)
                return false;

            Span<byte> cursor = into.Buffer.AsSpan(into.ContentLength);
            cursor.Slice(0, preceedingPadding).Fill(0);
            cursor = cursor.Slice(preceedingPadding);

            header.WriteCStruct(cursor);
            cursor = cursor.Slice((int)NlMsgHdr.SizeOfCStruct);

            cursor.Slice(0, (int)postHeaderPadding).Fill(0);
            cursor = cursor.Slice((int)postHeaderPadding);

            message.WriteCStruct(cursor);

            into.ContentLength += totalSize;
            return true;
        }
    }
}
