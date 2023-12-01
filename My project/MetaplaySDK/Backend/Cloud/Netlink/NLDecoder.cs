// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Netlink.Messages;
using Metaplay.Cloud.Netlink.Messages.SockDiag;
using System;

namespace Metaplay.Cloud.Netlink
{
    /// <summary>
    /// Decodes Netlink byte packet stream into messages.
    /// </summary>
    public class Decoder
    {
        public enum Result
        {
            NextPacket,
            Retry,
            InetDiagMsg,
            Error,
            Done,
        };
        enum State
        {
            FirstPacket,
            ReadHeader,
            ReadSockDiagByFamily,
            Error,
            Done,
        }

        NetlinkPacket _packet;
        uint _expectedSeq;
        int _position;
        State _nextState;
        string _error;
        NlMsgHdr _header;
        int _messageEndPosition;
        InetDiagMsg _inetDiagMessage;

        /// <summary>
        /// Data for <see cref="Result.InetDiagMsg"/>
        /// </summary>
        public InetDiagMsg InetDiagMsg => _inetDiagMessage;

        /// <summary>
        /// Data message for <see cref="Result.Error"/>
        /// </summary>
        public string Error => _error;

        public Decoder(uint expectedSeq)
        {
            _packet = null;
            _expectedSeq = expectedSeq;
            _nextState = State.FirstPacket;
        }

        /// <summary>
        /// Advances decoding and returns the type of parsed message or a pseudo-command
        /// the caller should perform to continue.
        /// </summary>
        public Result Advance()
        {
            switch(_nextState)
            {
                case State.FirstPacket:
                    return Advance_FirstPacket();
                case State.ReadHeader:
                    return Advance_ReadHeader();
                case State.ReadSockDiagByFamily:
                    return Advance_ReadSockDiagByFamily();
                case State.Done:
                    return Result.Done;
                case State.Error:
                    return Result.Error;
                default:
                    throw new InvalidOperationException();
            }
        }

        /// <summary>
        /// Call in response to <see cref="Result.NextPacket"/>
        /// </summary>
        public void NextPacket(NetlinkPacket packet)
        {
            _packet = packet;
            _position = 0;
        }

        Result Advance_FirstPacket()
        {
            if (_packet == null)
                return Result.NextPacket;
            _nextState = State.ReadHeader;
            return Result.Retry;
        }

        Result Advance_ReadHeader()
        {
            // Align
            _position += (int)AlignUtil.GetTrailingPaddingSize((uint)_position);

            // Next Header
            int available = _packet.ContentLength - _position;
            int needed = (int)NlMsgHdr.SizeOfCStruct;
            if (available < needed)
                return Result.NextPacket;

            _header = NlMsgHdr.ReadCStruct(_packet.Buffer.AsSpan(_position));
            if (_header.Length > available)
            {
                _error = "nlmsg header corrupted";
                _nextState = State.Error;
                return Result.Error;
            }
            else if (_header.Seq != _expectedSeq)
            {
                _error = "invalid nlmsg response";
                _nextState = State.Error;
                return Result.Error;
            }
            _messageEndPosition = _position + (int)_header.Length;
            _position += (int)NlMsgHdr.SizeOfCStruct;

            switch(_header.Type)
            {
                case MessageType.Done:
                {
                    _nextState = State.Done;
                    return Result.Done;
                }

                case MessageType.SockDiagByFamily:
                {
                    // Might be larger if it contains attributes
                    if (_header.Length < NlMsgHdr.SizeOfCStruct + AlignUtil.GetTrailingPaddingSize(NlMsgHdr.SizeOfCStruct) + InetDiagMsg.SizeOfCStruct)
                    {
                        _error = "nlmsg header unexpected sock_diag size";
                        _nextState = State.Error;
                        return Result.Error;
                    }

                    _nextState = State.ReadSockDiagByFamily;
                    return Result.Retry;
                }

                case MessageType.Error:
                {
                    _error = "got nl error response";
                    _nextState = State.Error;
                    return Result.Error;
                }

                case MessageType.Noop:
                default:
                {
                    _error = "unexpected nlmsg response type";
                    _nextState = State.Error;
                    return Result.Error;
                }
            }
        }

        Result Advance_ReadSockDiagByFamily()
        {
            // Align
            _position += (int)AlignUtil.GetTrailingPaddingSize((uint)_position);

            // Next Header
            int available = _packet.ContentLength - _position;
            int needed = (int)InetDiagMsg.SizeOfCStruct;
            if (available < needed)
            {
                _error = "not enough data for inet_diag_msg";
                _nextState = State.Error;
                return Result.Error;
            }

            _inetDiagMessage = InetDiagMsg.ReadCStruct(_packet.Buffer.AsSpan(_position));
            _position += (int)InetDiagMsg.SizeOfCStruct;

            // \todo: Parse attributes?

            _position = _messageEndPosition;

            if ((_header.Flags & MessageFlags.Multi) != 0)
                _nextState = State.ReadHeader;
            else
                _nextState = State.Done;

            return Result.InetDiagMsg;
        }
    }
}
