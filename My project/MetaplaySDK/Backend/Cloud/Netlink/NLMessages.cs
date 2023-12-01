// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;

namespace Metaplay.Cloud.Netlink.Messages
{
    public enum MessageType
    {
        /// <summary>
        /// NLMSG_NOOP
        /// </summary>
        Noop = 1,

        /// <summary>
        /// NLMSG_ERROR
        /// </summary>
        Error = 2,

        /// <summary>
        /// NLMSG_DONE
        /// </summary>
        Done = 3,

        /// <summary>
        /// SOCK_DIAG_BY_FAMILY
        /// </summary>
        SockDiagByFamily = 20,
    }

    [Flags]
    public enum MessageFlags
    {
        /// <summary>
        /// NLM_F_REQUEST
        /// </summary>
        Request = 0x01,

        /// <summary>
        /// NLM_F_MULTI
        /// </summary>
        Multi = 0x02,

        /// <summary>
        /// NLM_F_ROOT
        /// </summary>
        Root = 0x100,

        /// <summary>
        /// NLM_F_MATCH
        /// </summary>
        Match = 0x200,
    }

    /// <summary>
    /// nlmsghdr
    /// </summary>
    public struct NlMsgHdr
    {
        public UInt32       Length;
        public MessageType  Type;
        public MessageFlags Flags;
        public UInt32       Seq;
        public UInt32       Pid;

        public const uint SizeOfCStruct = 16;

        public void WriteCStruct(Span<byte> dst)
        {
            Span<byte> cursor = dst.Slice(0, (int)SizeOfCStruct);

            // struct nlmsghdr {
            //     __u32 nlmsg_len;
            BitConverter.TryWriteBytes(cursor, Length);
            cursor = cursor.Slice(4);

            //     __u16 nlmsg_type;
            BitConverter.TryWriteBytes(cursor, (UInt16)Type);
            cursor = cursor.Slice(2);

            //     __u16 nlmsg_flags;
            BitConverter.TryWriteBytes(cursor, (UInt16)Flags);
            cursor = cursor.Slice(2);

            //     __u32 nlmsg_seq;
            BitConverter.TryWriteBytes(cursor, Seq);
            cursor = cursor.Slice(4);

            //     __u32 nlmsg_pid;
            // };
            BitConverter.TryWriteBytes(cursor, Pid);
            cursor = cursor.Slice(4);
        }

        public static NlMsgHdr ReadCStruct(ReadOnlySpan<byte> s)
        {
            if (s.Length < SizeOfCStruct)
                throw new InvalidOperationException();

            ReadOnlySpan<byte> cursor = s.Slice(0, (int)SizeOfCStruct);

            NlMsgHdr msg = new NlMsgHdr();

            // struct nlmsghdr {
            //     __u32 nlmsg_len;
            msg.Length = BitConverter.ToUInt32(cursor);
            cursor = cursor.Slice(4);

            //     __u16 nlmsg_type;
            msg.Type = (MessageType)BitConverter.ToUInt16(cursor);
            cursor = cursor.Slice(2);

            //     __u16 nlmsg_flags;
            msg.Flags = (MessageFlags)BitConverter.ToUInt16(cursor);
            cursor = cursor.Slice(2);

            //     __u32 nlmsg_seq;
            msg.Seq = BitConverter.ToUInt32(cursor);
            cursor = cursor.Slice(4);

            //     __u32 nlmsg_pid;
            // };
            msg.Pid = BitConverter.ToUInt32(cursor);
            cursor = cursor.Slice(4);

            return msg;
        }
    }

    public static class AlignUtil
    {
        public static uint GetTrailingPaddingSize(uint structSizeInBytes)
        {
            return ((structSizeInBytes + 3) & ~0x3u) - structSizeInBytes;
        }
    }

    namespace SockDiag
    {
        public enum InetFamily
        {
            IPv4 = 2,
            IPv6 = 10,
        }
        public enum InetProtocol
        {
            TCP = 6,
        }
        [Flags]
        public enum InetSocketStateFlags
        {
            LISTEN      =  (1 << 10),
            ESTABLISHED =  (1 << 1),
        }

        public struct InetAddr128
        {
            uint v0;
            uint v1;
            uint v2;
            uint v3;

            public const uint SizeOfCStruct = 16;

            public void WriteCStruct(Span<byte> dst)
            {
                // BE
                ReadOnlySpan<byte> blob = stackalloc byte[]
                {
                    (byte)(v0 >> 24), (byte)(v0 >> 16), (byte)(v0 >> 8), (byte)(v0),
                    (byte)(v1 >> 24), (byte)(v1 >> 16), (byte)(v1 >> 8), (byte)(v1),
                    (byte)(v2 >> 24), (byte)(v2 >> 16), (byte)(v2 >> 8), (byte)(v2),
                    (byte)(v3 >> 24), (byte)(v3 >> 16), (byte)(v3 >> 8), (byte)(v3),
                };
                blob.CopyTo(dst);
            }
            public static InetAddr128 ReadCStruct(ReadOnlySpan<byte> s)
            {
                if (s.Length < SizeOfCStruct)
                    throw new InvalidOperationException();

                // BE
                InetAddr128 id = new InetAddr128();
                id.v0 = (uint)(s[0*4 + 0] << 24) | (uint)(s[0*4 + 1] << 16) | (uint)(s[0*4 + 2] << 8) | (uint)(s[0*4 + 3]);
                id.v1 = (uint)(s[1*4 + 0] << 24) | (uint)(s[1*4 + 1] << 16) | (uint)(s[1*4 + 2] << 8) | (uint)(s[1*4 + 3]);
                id.v2 = (uint)(s[2*4 + 0] << 24) | (uint)(s[2*4 + 1] << 16) | (uint)(s[2*4 + 2] << 8) | (uint)(s[2*4 + 3]);
                id.v3 = (uint)(s[3*4 + 0] << 24) | (uint)(s[3*4 + 1] << 16) | (uint)(s[3*4 + 2] << 8) | (uint)(s[3*4 + 3]);
                return id;
            }
        }

        /// <summary>
        /// inet_diag_sockid
        /// </summary>
        public struct InetDiagSockId
        {
            public ushort       SourcePort;
            public ushort       DestinationPort;
            public InetAddr128  SourceAddr;
            public InetAddr128  DestinationAddr;
            public uint         IfNdx;
            public ulong        Cookie;

            public const uint SizeOfCStruct = 48;

            public void WriteCStruct(Span<byte> dst)
            {
                Span<byte> cursor = dst.Slice(0, (int)SizeOfCStruct);

                // struct inet_diag_sockid {
                //     __be16  idiag_sport;
                //     __be16  idiag_dport;
                ReadOnlySpan<byte> blob = stackalloc byte[]
                {
                    (byte)(SourcePort >> 8),        (byte)SourcePort,
                    (byte)(DestinationPort >> 8),   (byte)DestinationPort,
                };
                blob.CopyTo(cursor);
                cursor = cursor.Slice(4);

                //     __be32  idiag_src[4];
                SourceAddr.WriteCStruct(cursor);
                cursor = cursor.Slice(16);

                //     __be32  idiag_dst[4];
                DestinationAddr.WriteCStruct(cursor);
                cursor = cursor.Slice(16);

                //     __u32   idiag_if;
                BitConverter.TryWriteBytes(cursor, IfNdx);
                cursor = cursor.Slice(4);

                //     __u32   idiag_cookie[2];
                // };
                BitConverter.TryWriteBytes(cursor, Cookie);
                cursor = cursor.Slice(8);
            }
            public static InetDiagSockId ReadCStruct(ReadOnlySpan<byte> s)
            {
                if (s.Length < SizeOfCStruct)
                    throw new InvalidOperationException();

                ReadOnlySpan<byte> cursor = s.Slice(0, (int)SizeOfCStruct);

                InetDiagSockId msg = new InetDiagSockId();

                // struct inet_diag_sockid {
                //     __be16  idiag_sport;
                //     __be16  idiag_dport;
                msg.SourcePort = (ushort)((cursor[0] << 8) + cursor[1]);
                msg.DestinationPort = (ushort)((cursor[2] << 8) + cursor[3]);
                cursor = cursor.Slice(4);

                //     __be32  idiag_src[4];
                msg.SourceAddr = InetAddr128.ReadCStruct(cursor);
                cursor = cursor.Slice(16);

                //     __be32  idiag_dst[4];
                msg.DestinationAddr = InetAddr128.ReadCStruct(cursor);
                cursor = cursor.Slice(16);

                //     __u32   idiag_if;
                msg.IfNdx = BitConverter.ToUInt32(cursor);
                cursor = cursor.Slice(4);

                //     __u32   idiag_cookie[2];
                // };
                msg.Cookie = BitConverter.ToUInt64(cursor);
                cursor = cursor.Slice(8);

                return msg;
            }
        }

        /// <summary>
        /// inet_diag_req_v2
        /// </summary>
        public struct InetDiagReqV2
        {
            public InetFamily           Family;
            public InetProtocol         Protocol;
            public InetSocketStateFlags SelectedStates;
            public InetDiagSockId       SelectedSocketId;

            public const uint SizeOfCStruct = 8 + InetDiagSockId.SizeOfCStruct;

            public void WriteCStruct(Span<byte> dst)
            {
                Span<byte> cursor = dst.Slice(0, (int)SizeOfCStruct);

                //struct inet_diag_req_v2 {
                //    __u8    sdiag_family;
                //    __u8    sdiag_protocol;
                //    __u8    idiag_ext;
                //    __u8    pad;
                ReadOnlySpan<byte> blob = stackalloc byte[]
                {
                    (byte)Family,
                    (byte)Protocol,
                    0,
                    0,
                };
                blob.CopyTo(cursor);
                cursor = cursor.Slice(4);

                //    __u32   idiag_states;
                BitConverter.TryWriteBytes(cursor, (uint)SelectedStates);
                cursor = cursor.Slice(4);

                //    struct inet_diag_sockid id;
                //};
                SelectedSocketId.WriteCStruct(cursor);
                cursor = cursor.Slice((int)InetDiagSockId.SizeOfCStruct);
            }
        }

        /// <summary>
        /// inet_diag_msg
        /// </summary>
        public struct InetDiagMsg
        {
            public InetFamily           Family;
            public InetSocketStateFlags States;
            public byte                 Timer;
            public byte                 Retrans;
            public InetDiagSockId       Id;
            public UInt32               Expires;
            public UInt32               Rqueue;
            public UInt32               Wqueue;
            public UInt32               Uid;
            public UInt32               Inode;

            public const uint SizeOfCStruct = 72;

            public static InetDiagMsg ReadCStruct(ReadOnlySpan<byte> s)
            {
                if (s.Length < SizeOfCStruct)
                    throw new InvalidOperationException();

                ReadOnlySpan<byte> cursor = s.Slice(0, (int)SizeOfCStruct);

                InetDiagMsg msg = new InetDiagMsg();

                // struct nlmsghdr {
                //  __u8    idiag_family;
                //  __u8    idiag_state;
                //  __u8    idiag_timer;
                //  __u8    idiag_retrans;
                msg.Family = (InetFamily)cursor[0];
                msg.States = (InetSocketStateFlags)cursor[1];
                msg.Timer = cursor[2];
                msg.Retrans = cursor[3];
                cursor = cursor.Slice(4);

                //  struct inet_diag_sockid id;
                msg.Id = InetDiagSockId.ReadCStruct(cursor);
                cursor = cursor.Slice((int)InetDiagSockId.SizeOfCStruct);

                //  __u32   idiag_expires;
                //  __u32   idiag_rqueue;
                //  __u32   idiag_wqueue;
                //  __u32   idiag_uid;
                //  __u32   idiag_inode;
                // };
                msg.Expires = BitConverter.ToUInt32(cursor);
                cursor = cursor.Slice(4);
                msg.Rqueue = BitConverter.ToUInt32(cursor);
                cursor = cursor.Slice(4);
                msg.Wqueue = BitConverter.ToUInt32(cursor);
                cursor = cursor.Slice(4);
                msg.Uid = BitConverter.ToUInt32(cursor);
                cursor = cursor.Slice(4);
                msg.Inode = BitConverter.ToUInt32(cursor);
                cursor = cursor.Slice(4);

                return msg;
            }
        }
    }
}
