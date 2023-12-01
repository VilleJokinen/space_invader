// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Math;
using Metaplay.Core.Model;
using System;
using System.Security.Cryptography;

namespace Metaplay.Core
{
    /// <summary>
    /// Hash value used to identify unique versions of a given piece of data. Main use case is to
    /// identify versions of config files and archives.
    ///
    /// Implemented as a 128-bit version of SHA1. Not intended to be safe against malicious attacks.
    /// </summary>
    [MetaSerializable]
    public struct ContentHash : IEquatable<ContentHash>
    {
        [MetaMember(1)] public MetaUInt128 Value { get; private set; }

        public static readonly ContentHash None = new ContentHash(MetaUInt128.Zero);

        public bool IsValid => Value != MetaUInt128.Zero;

        public ContentHash(MetaUInt128 value) { Value = value; }

        public static bool operator ==(ContentHash a, ContentHash b) => (a.Value == b.Value);
        public static bool operator !=(ContentHash a, ContentHash b) => (a.Value != b.Value);

        public bool Equals(ContentHash other) => this == other;

        public override bool Equals(object obj) => (obj is ContentHash other) ? (this == other) : false;
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => $"{Value.High:X16}-{Value.Low:X16}";

        public static ContentHash ParseString(string str)
        {
            string[] parts = str.Split('-');
            if (parts.Length != 2 || parts[0].Length != 16 || parts[1].Length != 16)
                throw new ArgumentException($"Invalid hash string '{str}'");
            return new ContentHash(new MetaUInt128(Convert.ToUInt64(parts[0], 16), Convert.ToUInt64(parts[1], 16)));
        }

        public static bool TryParseString(string str, out ContentHash contentHash)
        {
            try
            {
                contentHash = ParseString(str);
                return true;
            }
            catch
            {
                contentHash = ContentHash.None;
                return false;
            }
        }

        public static ContentHash ComputeFromBytes(byte[] bytes)
        {
            using (SHA1 sha = SHA1.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                return new ContentHash(new MetaUInt128(BitConverter.ToUInt64(hash, 0), BitConverter.ToUInt64(hash, 8)));
            }
        }
    }
}
