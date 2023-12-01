// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Net;
using System.Security.Cryptography;
using static System.FormattableString;

namespace Metaplay.Server
{
    public static class IPAddressRedactionUtil
    {
        /// <summary>
        /// For IPv4, produces strings like (123.45.X.X-3c1f83b9).
        /// For IPv6, produces strings like (fe80:0000:X:X:X:X:X:X-3c1f83b9).
        /// This is a concatenation of a the IP address with only a truncated
        /// network prefix visible, and a Hashed IP number.
        ///
        /// <para>
        /// Hashed IP is a number that can be used to distinguish different IP addresses
        /// from each other with a high probability, and conversely, identify same IP
        /// addresses with a high probability. It is derived by hashing the IP address
        /// with a keyed hashing while using a secret, cluster-wide generated nonce as
        /// the key.
        /// </para>
        /// <para>
        /// This construct makes redacted strings useful for correlating source IPs
        /// in short time windows, without having to process personal (-ly identifiable)
        /// information.
        /// </para>
        /// </summary>
        public static string ToRedactedString(IPAddress address)
        {
            ReadOnlySpan<byte>  nonce256   = GlobalStateProxyActor.ActiveSharedClusterNonce.Get().Nonce;
            byte[]              buffer      = new byte[32+16];

            nonce256.CopyTo(buffer);

            Span<byte> ipBytes = buffer.AsSpan().Slice(nonce256.Length);
            address.TryWriteBytes(ipBytes, out int numIPBytes);
            ipBytes.Slice(numIPBytes).Fill(value: 0);

            uint truncatedHash;
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] fullHash = sha256.ComputeHash(buffer);
                truncatedHash = BitConverter.ToUInt32(fullHash);
            }

            if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                // print only /16
                return Invariant($"({(int)ipBytes[0]}.{(int)ipBytes[1]}.X.X-{truncatedHash:x8})");
            }
            else if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                // /48 and /56 are recommended assignments for end user customers. So we only
                // print /32 prefix.
                ushort group0 = (ushort)((ipBytes[0] << 8) + ipBytes[1]);
                ushort group1 = (ushort)((ipBytes[2] << 8) + ipBytes[3]);
                return $"({group0:x4}:{group1:x4}:X:X:X:X:X:X-{truncatedHash:x8})";
            }
            else
                return "()";
        }

        /// <summary>
        /// Like <see cref="ToRedactedString"/>, except that private
        /// IP addresses (such as 10.*.*.*) are not redacted, but are
        /// returned as just <paramref name="address"/>.ToString().
        /// Private addresses are not considered personal information.
        ///
        /// <para>
        /// Note that "private address" here means "address reserved
        /// for private/internal use", not "a person's private-information
        /// address".
        /// </para>
        /// </summary>
        public static string ToPrivacyProtectingString(IPAddress address)
        {
            if (IsPrivateAddress(address))
                return address.ToString();
            else
                return ToRedactedString(address);
        }

        static bool IsPrivateAddress(IPAddress address)
        {
            // \todo [nuutti] Check also IPv6?

            if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                Span<byte> ipBytes = stackalloc byte[4];
                if (!address.TryWriteBytes(ipBytes, out int numIPBytes) || numIPBytes != 4)
                    return false;

                return ipBytes[0] == 10
                    || (ipBytes[0] == 172 && ipBytes[1] >= 16 && ipBytes[1] <= 31)
                    || (ipBytes[0] == 192 && ipBytes[1] == 168);
            }
            else
                return false;
        }
    }
}
