// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Netlink.Messages;
using Metaplay.Cloud.Netlink.Messages.SockDiag;
using System;
using System.Collections.Generic;

namespace Metaplay.Cloud.Netlink
{
    public class SockDiagException : Exception
    {
        public SockDiagException(string message) : base(message)
        {
        }
        public SockDiagException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// Exposes sock_diag(7) kernel netlink interface
    /// </summary>
    public class SockDiag : IDisposable
    {
        NetlinkSocket   _socket;
        NetlinkPacket   _sendPacket;
        uint            _sequenceNdx;

        /// <summary>
        /// Creates sock_diag connection. Throws <see cref="SockDiagException"/> on failure.
        /// </summary>
        public SockDiag()
        {
            try
            {
                _socket = NetlinkSocket.ConnectToKernel();
            }
            catch (Exception ex)
            {
                throw new SockDiagException("could not open netlink socket", ex);
            }

            _sendPacket = NetlinkPacket.CreateMaxSizeEmptyPacket();
            _sequenceNdx = 0;
        }

        public struct Filter
        {
            public InetFamily           Family;
            public InetProtocol         Protocol;
            public InetSocketStateFlags States;
        };

        /// <summary>
        /// Enumerates all sockets on match the filter. Throws <see cref="SockDiagException"/> on failure.
        /// </summary>
        public List<InetDiagMsg> GetAll(Filter filter)
        {
            _sendPacket.Clear();
            _sequenceNdx++;

            InetDiagReqV2 request = new InetDiagReqV2
            {
                Family = filter.Family,
                Protocol = filter.Protocol,
                SelectedStates = filter.States,
                SelectedSocketId = new InetDiagSockId()
            };

            bool encodeSuccess = Encoder.TryAppendMessage(
                into:   _sendPacket,
                flags:  MessageFlags.Request | MessageFlags.Root | MessageFlags.Match,
                seq:    _sequenceNdx,
                request
                );

            if (!encodeSuccess)
                throw new SockDiagException("could not encode message");

            try
            {
                _socket.Send(_sendPacket);
            }
            catch (Exception ex)
            {
                throw new SockDiagException("could not send netlink packet", ex);
            }

            Decoder decoder = new Decoder(_sequenceNdx);
            List<InetDiagMsg> results = new List<InetDiagMsg>();
            for (;;)
            {
                switch (decoder.Advance())
                {
                    case Decoder.Result.Retry:
                        break;

                    case Decoder.Result.NextPacket:
                        NetlinkPacket msg;
                        try
                        {
                            msg = _socket.Receive();
                        }
                        catch (Exception ex)
                        {
                            throw new SockDiagException("could not receive netlink packet", ex);
                        }
                        decoder.NextPacket(msg);
                        break;

                    case Decoder.Result.InetDiagMsg:
                        results.Add(decoder.InetDiagMsg);
                        break;

                    case Decoder.Result.Done:
                        return results;

                    case Decoder.Result.Error:
                        throw new SockDiagException($"could not parse netlink protocol: {decoder.Error}");

                    default:
                        throw new SockDiagException("decoder invalid state");
                }
            }
        }

        public void Dispose()
        {
            _socket?.Dispose();
            _socket = null;
        }
    }
}
