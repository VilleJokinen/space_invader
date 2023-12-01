// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Metaplay.Cloud.Netlink
{
    class ConnectionException : Exception
    {
        public ConnectionException(string message) : base(message) { }
    };

    public class NetlinkPacket
    {
        /// <summary>
        /// Buffer for data, size up to <see cref="NetlinkSocket.MaxPacketSize"/>.
        /// </summary>
        public byte[] Buffer;

        /// <summary>
        /// Number of content bytes in <see cref="Buffer"/>.
        /// </summary>
        public int ContentLength;

        /// <summary>
        /// Resets ContentLength to 0
        /// </summary>
        public void Clear()
        {
            ContentLength = 0;
        }

        NetlinkPacket(byte[] buffer, int contentLength)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (contentLength > buffer.Length || contentLength > NetlinkSocket.MaxPacketSize)
                throw new ArgumentOutOfRangeException(nameof(contentLength));

            Buffer = buffer;
            ContentLength = contentLength;
        }

        public static NetlinkPacket CreateMaxSizeEmptyPacket()
        {
            return new NetlinkPacket(new byte[NetlinkSocket.MaxPacketSize], 0);
        }
    }

    /// <summary>
    /// Netlink socket.
    /// </summary>
    public class NetlinkSocket : IDisposable
    {
        static class LibC
        {
            public enum Errno
            {
                EAGAIN_EWOULDBLOCK = 11,
                EINTR = 4,
            }

            public const int AF_NETLINK = 16;
            public const int SOCK_DGRAM = 2;
            public const int SOCK_CLOEXEC = 0x80000;
            public const int NETLINK_SOCK_DIAG = 4;
            public const int SOL_SOCKET = 1;
            public const int SO_RCVTIMEO = 20;

            [StructLayout(LayoutKind.Sequential)]
            public struct Csockaddr_nl
            {
                public UInt16   nl_family;
                public UInt16   nl_pad;
                public Int32    nl_pid;
                public UInt32   nl_groups;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct Ctimeval
            {
                public Int64    tv_sec;
                public Int64    tv_usec;
            };

            [DllImport("libc", CallingConvention=CallingConvention.Cdecl, SetLastError=true)]
            public static extern int socket(int domain, int type, int protocol);

            [DllImport("libc", CallingConvention=CallingConvention.Cdecl, SetLastError=true)]
            public static extern int close(int fd);

            [DllImport("libc", CallingConvention=CallingConvention.Cdecl, SetLastError=true)]
            public static extern int bind(int sockfd, ref Csockaddr_nl addr, int addrLen);

            [DllImport("libc", CallingConvention=CallingConvention.Cdecl, SetLastError=true)]
            public static extern int connect(int sockfd, ref Csockaddr_nl addr, int addrLen);

            [DllImport("libc", CallingConvention=CallingConvention.Cdecl, SetLastError=true)]
            public static extern int setsockopt(int sockfd, int level, int optname, ref Ctimeval timeval, int timevalSize);

            [DllImport("libc", CallingConvention=CallingConvention.Cdecl, SetLastError=true)]
            public static extern long recv(int sockfd, byte[] buf, long bufSize, int flags);

            [DllImport("libc", CallingConvention=CallingConvention.Cdecl, SetLastError=true)]
            public static extern long send(int sockfd, byte[] buf, long bufSize, int flags);
        }

        // Minimum maximum size (NLMSG_GOODSIZE) of the skb is 8k. Maximum maximum is 16k in 3.15
        // and after 4.4 32k. Be conservative here, and choose the minimum.
        public const int MaxPacketSize = 8192;

        int _fd;
        NetlinkPacket _recvbuf;

        NetlinkSocket(int fd)
        {
            _fd = fd;
        }
        ~NetlinkSocket()
        {
            Dispose(disposing: false);
        }
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
        void Dispose(bool disposing)
        {
            if (_fd >= 0)
            {
                _ = LibC.close(_fd);
                _fd = -1;
            }
        }

        /// <summary>
        /// Sends the message. Message maximum size is <see cref="MaxPacketSize"/>.
        /// Throws <see cref="ConnectionException"/> on failure.
        /// </summary>
        public void Send(NetlinkPacket packet)
        {
            if (_fd == -1)
                throw new ObjectDisposedException(nameof(_fd));
            if (packet.ContentLength > packet.Buffer.Length)
                throw new ArgumentException("size > buffer size");

            Stopwatch sw = Stopwatch.StartNew();

            while (true)
            {
                long numWritten = LibC.send(_fd, packet.Buffer, packet.ContentLength, flags: 0);

                // write must be successful
                if (numWritten < 0)
                {
                    int errno = Marshal.GetLastWin32Error();
                    if (errno == (int)LibC.Errno.EINTR)
                    {
                        // make sure repeated intrs don't lock up
                        if (sw.ElapsedMilliseconds > 1000)
                            throw new ConnectionException("send() timed out");

                        // retry
                    }
                    else
                        throw new ConnectionException("send() failed");
                }
                // and atomic
                else if (numWritten != packet.ContentLength)
                    throw new ConnectionException("send() was not atomic");
                else
                {
                    // success
                    return;
                }
            }
        }

        /// <summary>
        /// Reads a message. On success returns a view to the received data. The contents
        /// of the view are valid until <see cref="Receive"/> is called again. On failure
        /// or timeout, throws <see cref="ConnectionException"/>.
        /// </summary>
        public NetlinkPacket Receive()
        {
            if (_fd == -1)
                throw new ObjectDisposedException(nameof(_fd));
            if (_recvbuf == null)
                _recvbuf = NetlinkPacket.CreateMaxSizeEmptyPacket();
            _recvbuf.Clear();

            Stopwatch sw = Stopwatch.StartNew();

            while (true)
            {
                long numRead = LibC.recv(_fd, _recvbuf.Buffer, _recvbuf.Buffer.LongLength, 0);
                if (numRead < 0)
                {
                    int errno = Marshal.GetLastWin32Error();
                    if (errno == (int)LibC.Errno.EINTR)
                    {
                        // make sure repeated intrs don't lock up
                        if (sw.ElapsedMilliseconds > 1000)
                            throw new ConnectionException("recv() timed out");

                        // retry
                    }
                    else if (errno == (int)LibC.Errno.EAGAIN_EWOULDBLOCK)
                        throw new ConnectionException("recv() timed out");
                    else
                        throw new ConnectionException("recv() failed");
                }
                else
                {
                    _recvbuf.ContentLength = (int)numRead;
                    return _recvbuf;
                }
            }
        }

        /// <summary>
        /// Opens a connection to the Linux Kernel. Throws <see cref="ConnectionException"/> on failure.
        /// </summary>
        public static NetlinkSocket ConnectToKernel()
        {
            int err;
            LibC.Csockaddr_nl localAddress;
            LibC.Csockaddr_nl remoteAddress;
            LibC.Ctimeval recvTimeout;
            int fd;

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                throw new ConnectionException("platform must be linux");
            if (IntPtr.Size != 8) // size_t == long
                throw new ConnectionException("platform must be 64bit");

            fd = LibC.socket(LibC.AF_NETLINK, LibC.SOCK_DGRAM | LibC.SOCK_CLOEXEC, LibC.NETLINK_SOCK_DIAG);
            if (fd < 0)
                throw new ConnectionException($"socket() failed: {Marshal.GetLastWin32Error()}");

            localAddress = GetLocalNLAddr();
            err = LibC.bind(fd, ref localAddress, Marshal.SizeOf(localAddress));
            if (err != 0)
            {
                _ = LibC.close(fd);
                throw new ConnectionException("bind() failed");
            }

            remoteAddress = GetKernelNLUnicastAddr();
            err = LibC.connect(fd, ref remoteAddress, Marshal.SizeOf(remoteAddress));
            if (err != 0)
            {
                _ = LibC.close(fd);
                throw new ConnectionException("connect() failed");
            }

            // \todo: In .NET 5.0 we can wrap the FD into a Socket. That would happen at this point.

            recvTimeout = new LibC.Ctimeval() { tv_sec = 1, tv_usec = 0 };
            err = LibC.setsockopt(fd, LibC.SOL_SOCKET, LibC.SO_RCVTIMEO, ref recvTimeout, Marshal.SizeOf(recvTimeout));
            if (err != 0)
            {
                _ = LibC.close(fd);
                throw new ConnectionException("setsockopt() failed");
            }

            return new NetlinkSocket(fd);
        }

        static LibC.Csockaddr_nl GetLocalNLAddr()
        {
            LibC.Csockaddr_nl addr = new LibC.Csockaddr_nl();
            addr.nl_family = LibC.AF_NETLINK;
            addr.nl_pad = 0;
            addr.nl_pid = 0; // kernel will set proper pid
            addr.nl_groups = 0;
            return addr;
        }
        static LibC.Csockaddr_nl GetKernelNLUnicastAddr()
        {
            LibC.Csockaddr_nl addr = new LibC.Csockaddr_nl();
            addr.nl_family = LibC.AF_NETLINK;
            addr.nl_pad = 0;
            addr.nl_pid = 0; // destination: kernel
            addr.nl_groups = 0;
            return addr;
        }
    }
}
