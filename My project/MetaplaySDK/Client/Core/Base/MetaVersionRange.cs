// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using System;
using static System.FormattableString;

namespace Metaplay.Core
{
    /// <summary>
    /// Include range of versions. Intended to be used with <c>LogicVersion</c>s and entity schema versions.
    /// Note that both min and max values are inclusive!
    /// </summary>
    [MetaSerializable]
    public class MetaVersionRange
    {
        [MetaMember(1)] public int MinVersion { get; private set; } // Minimum accepted version (inclusive)
        [MetaMember(2)] public int MaxVersion { get; private set; } // Maximum accepted version (inclusive)

        public MetaVersionRange() { }
        public MetaVersionRange(int minVersion, int maxVersion)
        {
            if (minVersion > maxVersion)
                throw new ArgumentException($"MinVersion ({minVersion}) cannot be greater than MaxVersion ({maxVersion})");

            MinVersion = minVersion;
            MaxVersion = maxVersion;
        }

        public static bool operator ==(MetaVersionRange a, MetaVersionRange b)
        {
            if (ReferenceEquals(a, b))
                return true;
            else if (a is null || b is null)
                return false;
            else
                return a.MinVersion == b.MinVersion && a.MaxVersion == b.MaxVersion;
        }
        public static bool operator !=(MetaVersionRange a, MetaVersionRange b) => !(a == b);

        public override bool Equals(object obj) => (obj is MetaVersionRange other) ? (this == other) : false;
        public override int GetHashCode() => Util.CombineHashCode(MinVersion, MaxVersion);
        public override string ToString() => Invariant($"{MinVersion}..{MaxVersion}");
    }
}
