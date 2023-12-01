// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using Metaplay.Core;
using Org.BouncyCastle.Crypto.Digests;
using System;
using System.ComponentModel;
using System.Text;

namespace Metaplay.Cloud.Web3
{
    [MetaSerializable]
    [TypeConverter(typeof(EthereumAddressStringConverter))]
    public struct EthereumAddress : IEquatable<EthereumAddress>
    {
        readonly ulong _f0_8; // bytes 0..8 in unspecified endianness
        readonly ulong _f8_16; // bytes 8..16 in unspecified endianness
        readonly uint _f16_20; // bytes 16_20 in unspecified endianness
        readonly ulong _isUpperBitset; // bit N is set if character N is uppercase

        /// <summary>
        /// Fake field that encodes/decodes the address into a blob
        /// </summary>
        [MetaMember(1)]
        byte[] SerializationProxy
        {
            get
            {
                return ToBytes();
            }
            set
            {
                this = FromBytes(value);
            }
        }

        EthereumAddress(ulong f0_8, ulong f8_16, uint f16_20, ulong isUpperBitset)
        {
            _f0_8 = f0_8;
            _f8_16 = f8_16;
            _f16_20 = f16_20;
            _isUpperBitset = isUpperBitset;
        }

        /// <summary>
        /// Parses Ethereum Address such as <c>0xcDA67b3936A21a8EAe84A3BF8B7Ec7123c4C3234</c>. The address must
        /// start with <c>0x</c>. The address is case sensitive and the character case checksum is checked as per EIP-55.
        /// On failure, throws <see cref="FormatException"/>.
        /// </summary>
        public static EthereumAddress FromString(string ethereumAddress) => FromStringInternal(ethereumAddress, checkCaseChecksum: true);

        /// <summary>
        /// Parses Ethereum Address such as <c>0xcDA67b3936A21a8EAe84A3BF8B7Ec7123c4C3234</c>. The address must
        /// start with <c>0x</c> (case sensitive). The address part is NOT CASE SENSITIVE, i.e. the character case checksum is NOT checked as per EIP-55.
        /// On failure, throws <see cref="FormatException"/>.
        /// </summary>
        public static EthereumAddress FromStringWithoutChecksumCasing(string ethereumAddress) => FromStringInternal(ethereumAddress, checkCaseChecksum: false);

        static EthereumAddress FromStringInternal(string ethereumAddress, bool checkCaseChecksum)
        {
            // Check format
            if (ethereumAddress == null)
                throw new ArgumentNullException(nameof(ethereumAddress));
            if (ethereumAddress.Length != 42)
                throw new FormatException($"Ethereum Address must be 42 characters long. Got {ethereumAddress}.");
            if (ethereumAddress.Substring(0, 2) != "0x")
                throw new FormatException($"Ethereum Address must start with 0x. Got {ethereumAddress}.");

            byte[] bytes = Convert.FromHexString(ethereumAddress.AsSpan(start: 2));
            ulong f0_8 = BitConverter.ToUInt64(bytes, startIndex: 0);
            ulong f8_16 = BitConverter.ToUInt64(bytes, startIndex: 8);
            uint f16_20 = BitConverter.ToUInt32(bytes, startIndex: 16);

            // Compute is-upper bitset
            ulong isUpper = ResolveAddressIsUpperBitset(bytes, offset: 0);

            // Verify checksum
            if (checkCaseChecksum)
            {
                for (int nibbleNdx = 0; nibbleNdx < 40; ++nibbleNdx)
                {
                    bool shouldBeUpper = ((isUpper >> nibbleNdx) & 0x01) != 0;
                    char c = ethereumAddress[2 + nibbleNdx];
                    if (c >= '0' && c <= '9')
                        continue;
                    if (char.IsUpper(c) != shouldBeUpper)
                        throw new FormatException($"Ethereum Address has incorrect upper and lowercase characters. Got {ethereumAddress}, expected {new EthereumAddress(f0_8, f8_16, f16_20, isUpper).GetAddressString()}.");
                }
            }

            return new EthereumAddress(f0_8, f8_16, f16_20, isUpper);
        }

        /// <summary>
        /// Computes Ethereum Address such as <c>0xcDA67b3936A21a8EAe84A3BF8B7Ec7123c4C3234</c> from the public key.
        /// <paramref name="keyBytes"/> must be exactly 64 bytes long.
        /// On failure, throws <see cref="FormatException"/>.
        /// </summary>
        public static EthereumAddress FromPublicKey(byte[] keyBytes)
        {
            if (keyBytes.Length != 64)
                throw new FormatException("Public key must be 64 bytes long");

            KeccakDigest digest = new KeccakDigest(bitLength: 256);
            digest.BlockUpdate(keyBytes, 0, keyBytes.Length);
            byte[] finalHash = new byte[32];
            digest.DoFinal(finalHash, 0);
            return FromBytes(finalHash, startIndex: 12);
        }

        public static EthereumAddress FromBytes(byte[] addressBytes, int startIndex = 0)
        {
            ulong f0_8 = BitConverter.ToUInt64(addressBytes, startIndex: startIndex);
            ulong f8_16 = BitConverter.ToUInt64(addressBytes, startIndex: startIndex + 8);
            uint f16_20 = BitConverter.ToUInt32(addressBytes, startIndex: startIndex + 16);
            ulong isUpper = ResolveAddressIsUpperBitset(addressBytes, offset: startIndex);
            return new EthereumAddress(f0_8, f8_16, f16_20, isUpper);
        }

        static ulong ResolveAddressIsUpperBitset(byte[] addressBytes, int offset)
        {
            byte[] addressAsLowerHexString = Encoding.UTF8.GetBytes(Convert.ToHexString(addressBytes, offset, length: 20).ToLowerInvariant());
            KeccakDigest digest = new KeccakDigest(bitLength: 256);
            digest.BlockUpdate(addressAsLowerHexString, 0, addressAsLowerHexString.Length);
            byte[] finalHash = new byte[32];
            digest.DoFinal(finalHash, 0);

            ulong result = 0;
            for (int byteNdx = 0; byteNdx < 20; ++byteNdx)
            {
                if ((finalHash[byteNdx] & 0x80) != 0)
                    result |= 1ul << (byteNdx * 2);
                if ((finalHash[byteNdx] & 0x08) != 0)
                    result |= 1ul << (byteNdx * 2 + 1);
            }
            return result;
        }

        public enum AddressStyle
        {
            With0xPrefix = 0,
            WithoutPrefix
        }

        /// <summary>
        /// Returns the Ethereum Address as string, such as <c>0xcDA67b3936A21a8EAe84A3BF8B7Ec7123c4C3234</c>. If <paramref name="style"/> is
        /// <see cref="AddressStyle.WithoutPrefix"/>, the <c>0x</c> prefix is omitted. The address is in EIP-55 format, i.e. the checksum is
        /// encoded in the character upper/lower cases.
        /// </summary>
        public string GetAddressString(AddressStyle style = AddressStyle.With0xPrefix)
        {
            static char ToChar(byte nibble, bool isUpperBit)
            {
                if (nibble <= 9)
                    return (char)('0' + nibble);
                char a = (isUpperBit) ? 'A' : 'a';
                return (char)(a + nibble - 10);
            }

            Span<char> chars = stackalloc char[42];
            chars[0] = '0';
            chars[1] = 'x';

            Span<byte> bytes = stackalloc byte[8];
            ulong isUpperCursor = _isUpperBitset;
            int writeNdx = 2;

            BitConverter.TryWriteBytes(bytes, _f0_8);
            for (int byteNdx = 0; byteNdx < 8; ++byteNdx)
            {
                chars[writeNdx++] = ToChar((byte)((bytes[byteNdx] >> 4) & 0x0F), (isUpperCursor & 1) != 0);
                isUpperCursor >>= 1;
                chars[writeNdx++] = ToChar((byte)(bytes[byteNdx] & 0x0F), (isUpperCursor & 1) != 0);
                isUpperCursor >>= 1;
            }

            BitConverter.TryWriteBytes(bytes, _f8_16);
            for (int byteNdx = 0; byteNdx < 8; ++byteNdx)
            {
                chars[writeNdx++] = ToChar((byte)((bytes[byteNdx] >> 4) & 0x0F), (isUpperCursor & 1) != 0);
                isUpperCursor >>= 1;
                chars[writeNdx++] = ToChar((byte)(bytes[byteNdx] & 0x0F), (isUpperCursor & 1) != 0);
                isUpperCursor >>= 1;
            }

            BitConverter.TryWriteBytes(bytes, _f16_20);
            for (int byteNdx = 0; byteNdx < 4; ++byteNdx)
            {
                chars[writeNdx++] = ToChar((byte)((bytes[byteNdx] >> 4) & 0x0F), (isUpperCursor & 1) != 0);
                isUpperCursor >>= 1;
                chars[writeNdx++] = ToChar((byte)(bytes[byteNdx] & 0x0F), (isUpperCursor & 1) != 0);
                isUpperCursor >>= 1;
            }

            if (style == AddressStyle.With0xPrefix)
                return new string(chars);
            else
                return new string(chars.Slice(start: 2));
        }

        /// <summary>
        /// Gets the address bytes. Returns 20-byte long byte array.
        /// </summary>
        public byte[] ToBytes()
        {
            byte[] bytes = new byte[20];
            Span<byte> cursor = bytes;
            BitConverter.TryWriteBytes(cursor, _f0_8);
            cursor = cursor.Slice(8);
            BitConverter.TryWriteBytes(cursor, _f8_16);
            cursor = cursor.Slice(8);
            BitConverter.TryWriteBytes(cursor, _f16_20);
            return bytes;
        }

        public override string ToString() => $"EthereumAddress{{{GetAddressString()}}}";
        public override int GetHashCode() => HashCode.Combine(_f0_8, _f8_16, _f16_20);
        public override bool Equals(object obj) => obj is EthereumAddress other && Equals(other);
        public bool Equals(EthereumAddress other) => this == other;
        public static bool operator!= (EthereumAddress a, EthereumAddress b) => !(a == b);
        public static bool operator== (EthereumAddress a, EthereumAddress b)
        {
            // \note: _isUpperBitset does not need to be checked as the character case is a function of address
            // and the address is represented by the _fXX fields.
            return a._f0_8 == b._f0_8
                && a._f8_16 == b._f8_16
                && a._f16_20 == b._f16_20;
        }
    }

    public class EthereumAddressStringConverter : StringTypeConverterHelper<EthereumAddress>
    {
        protected override EthereumAddress ConvertStringToValue(string str)
            => EthereumAddress.FromString(str);

        protected override string ConvertValueToString(EthereumAddress obj)
            => obj.GetAddressString();
    }
}
