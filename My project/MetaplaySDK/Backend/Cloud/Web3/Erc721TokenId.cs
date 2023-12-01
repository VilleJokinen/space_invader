// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;

namespace Metaplay.Cloud.Web3
{
    /// <summary>
    /// ERC721 token id. The token ID is an unsigned 256-bit number.
    /// </summary>
    public readonly struct Erc721TokenId : IEquatable<Erc721TokenId>, IComparable<Erc721TokenId>
    {
        const string MaxValue = "115792089237316195423570985008687907853269984665640564039457584007913129639935";
        readonly string _str;
        string Str
        {
            get
            {
                // make default init idential to "0".
                return _str ?? "0";
            }
            init
            {
                _str  = value;
            }
        }

        Erc721TokenId(string str)
        {
            _str = str;
        }

        /// <summary>
        /// Creates <see cref="Erc721TokenId"/> from a Uint256 decimal string, such as "125498080123".
        /// </summary>
        public static Erc721TokenId FromDecimalString(string value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            if (!IsValidDecimal(value))
                throw new FormatException($"Invalid value for ERC721 token id: {value}");
            return new Erc721TokenId(value);
        }

        static bool IsValidDecimal(string value)
        {
            if (value == null)
                return false;
            if (value.Length == 0 || value.Length > MaxValue.Length)
                return false;
            if (value == "0")
                return true;
            if (value[0] == '0')
                return false;
            foreach (char c in value)
            {
                if (c < '0' || c > '9')
                    return false;
            }
            if (value.Length == MaxValue.Length && string.CompareOrdinal(value, MaxValue) > 0)
                return false;
            return true;
        }

        /// <summary>
        /// Returns the Uint256 as a decimal string.
        /// </summary>
        public string GetTokenIdString() => Str;

        public override string ToString() => $"ERC721TokenId{{{Str}}}";
        public override int GetHashCode() => Str.GetHashCode();
        public override bool Equals(object obj) => obj is Erc721TokenId other && Equals(other);
        public bool Equals(Erc721TokenId other) => this == other;
        public static bool operator ==(Erc721TokenId a, Erc721TokenId b) => a.Str == b.Str;
        public static bool operator !=(Erc721TokenId a, Erc721TokenId b) => !(a == b);
        public int CompareTo(Erc721TokenId other)
        {
            if (Str.Length > other.Str.Length)
                return +1;
            if (Str.Length < other.Str.Length)
                return -1;
            return string.CompareOrdinal(Str, other.Str);
        }
    }
}
