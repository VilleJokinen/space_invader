// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;

namespace Metaplay.Cloud.Web3
{
    public readonly struct StarkPublicKey : IEquatable<StarkPublicKey>
    {
        readonly ulong _f0_8; // bytes 0..8 in unspecified endianness
        readonly ulong _f8_16; // bytes 8..16 in unspecified endianness
        readonly ulong _f16_24; // bytes 16..24 in unspecified endianness
        readonly ulong _f24_32; // bytes 24..32 in unspecified endianness

        public StarkPublicKey(ulong f0_8, ulong f8_16, ulong f16_24, ulong f24_32)
        {
            _f0_8 = f0_8;
            _f8_16 = f8_16;
            _f16_24 = f16_24;
            _f24_32 = f24_32;
        }

        /// <summary>
        /// Parses public key such as <c>0x04ca6e59a29db2e76941e96d82d3bed3f3e5f4fa3812ca64a59833c4c6196bc0</c>. The parsing is not case sensitive.
        /// On failure, throws <see cref="FormatException"/>.
        /// </summary>
        public static StarkPublicKey FromString(string starkKeyString)
        {
            // Check format
            if (starkKeyString == null)
                throw new ArgumentNullException(nameof(starkKeyString));
            if (starkKeyString.Length != 66)
                throw new FormatException($"Stark Public Key must be 66 characters long. Got {starkKeyString}.");
            if (starkKeyString.Substring(0, 2) != "0x")
                throw new FormatException($"Stark Public Key must start with 0x. Got {starkKeyString}.");

            byte[] bytes = Convert.FromHexString(starkKeyString.AsSpan(start: 2));
            ulong f0_8 = BitConverter.ToUInt64(bytes, startIndex: 0);
            ulong f8_16 = BitConverter.ToUInt64(bytes, startIndex: 8);
            ulong f16_24 = BitConverter.ToUInt64(bytes, startIndex: 16);
            ulong f24_32 = BitConverter.ToUInt64(bytes, startIndex: 24);

            return new StarkPublicKey(f0_8, f8_16, f16_24, f24_32);
        }

        public enum PublicKeyStyle
        {
            With0xPrefix = 0,
            WithoutPrefix
        }

        /// <summary>
        /// Returns the Stark public key as string, such as <c>0x04ca6e59a29db2e76941e96d82d3bed3f3e5f4fa3812ca64a59833c4c6196bc0</c>.
        /// The key is always formatted in lower case.
        /// </summary>
        public string GetPublicKeyString(PublicKeyStyle style = PublicKeyStyle.With0xPrefix)
        {
            static char ToChar(byte nibble)
            {
                if (nibble <= 9)
                    return (char)('0' + nibble);
                return (char)('a' + nibble - 10);
            }

            Span<char> chars = stackalloc char[66];
            chars[0] = '0';
            chars[1] = 'x';

            Span<byte> bytes = stackalloc byte[8];
            int writeNdx = 2;

            for (int wordNdx = 0; wordNdx < 4; ++wordNdx)
            {
                ulong word;
                switch (wordNdx)
                {
                    case 0: word = _f0_8; break;
                    case 1: word = _f8_16; break;
                    case 2: word = _f16_24; break;
                    default: word = _f24_32; break;
                }

                BitConverter.TryWriteBytes(bytes, word);
                for (int byteNdx = 0; byteNdx < 8; ++byteNdx)
                {
                    chars[writeNdx++] = ToChar((byte)((bytes[byteNdx] >> 4) & 0x0F));
                    chars[writeNdx++] = ToChar((byte)(bytes[byteNdx] & 0x0F));
                }
            }

            if (style == PublicKeyStyle.With0xPrefix)
                return new string(chars);
            else
                return new string(chars.Slice(start: 2));
        }

        public override string ToString() => $"StarkPublicKey{{{GetPublicKeyString()}}}";
        public override int GetHashCode() => HashCode.Combine(_f0_8, _f8_16, _f16_24, _f24_32);
        public override bool Equals(object obj) => obj is StarkPublicKey other && Equals(other);
        public bool Equals(StarkPublicKey other) => this == other;
        public static bool operator!= (StarkPublicKey a, StarkPublicKey b) => !(a == b);
        public static bool operator== (StarkPublicKey a, StarkPublicKey b)
        {
            return a._f0_8 == b._f0_8
                && a._f8_16 == b._f8_16
                && a._f16_24 == b._f16_24
                && a._f24_32 == b._f24_32;
        }
    }
}
