// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using System;
using static System.FormattableString;

namespace Metaplay.Core.Math
{
    [MetaSerializable]
    public struct IntVector3 : IEquatable<IntVector3>, IComparable<IntVector3>
    {
        // Constants
        public static IntVector3 Zero       => new IntVector3(0, 0, 0);
        public static IntVector3 One        => new IntVector3(1, 1, 1);
        public static IntVector3 Down       => new IntVector3(0, -1, 0);
        public static IntVector3 Up         => new IntVector3(0, 1, 0);
        public static IntVector3 Left       => new IntVector3(-1, 0, 0);
        public static IntVector3 Right      => new IntVector3(1, 0, 0);
        public static IntVector3 Forward    => new IntVector3(0, 0, 1);
        public static IntVector3 Back       => new IntVector3(0, 0, -1);

        [MetaMember(1)] public int X;
        [MetaMember(2)] public int Y;
        [MetaMember(3)] public int Z;

        public IntVector3(int x, int y, int z) { X = x; Y = y; Z = z; }

        public override readonly string ToString() => Invariant($"({X}, {Y}, {Z})");

        public static IntVector3 operator +(IntVector3 a, IntVector3 b) => new IntVector3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static IntVector3 operator -(IntVector3 a, IntVector3 b) => new IntVector3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static IntVector3 operator *(IntVector3 a, IntVector3 b) => new IntVector3(a.X * b.X, a.Y * b.Y, a.Z * b.Z);
        public static IntVector3 operator *(IntVector3 a, int b) => new IntVector3(a.X * b, a.Y * b, a.Z * b);
        public static IntVector3 operator *(int a, IntVector3 b) => new IntVector3(a * b.X, a * b.Y, a * b.Z);

        public static bool operator ==(IntVector3 a, IntVector3 b) => (a.X == b.X) && (a.Y == b.Y) && (a.Z == b.Z);
        public static bool operator !=(IntVector3 a, IntVector3 b) => (a.X != b.X) || (a.Y != b.Y) || (a.Z != b.Z);

        public readonly bool Equals(IntVector3 other) => this == other;
        public override readonly bool Equals(object obj) => (obj is IntVector3 other) ? (this == other) : false;

        public override readonly int GetHashCode() => Util.CombineHashCode(X.GetHashCode(), Y.GetHashCode(), Z.GetHashCode());

        public readonly F32Vec3 ToF32Vec3() => F32Vec3.FromInt(X, Y, Z);

        int IComparable<IntVector3>.CompareTo(IntVector3 other)
        {
            int v = X.CompareTo(other.X);
            if (v != 0) return v;
            v = Y.CompareTo(other.Y);
            if (v != 0) return v;
            return Z.CompareTo(other.Z);
        }
    }

    public static partial class VecExtensions
    {
        public static IntVector3 RoundToInt(this F32Vec3 vec) => new IntVector3(F32.RoundToInt(vec.X), F32.RoundToInt(vec.Y), F32.RoundToInt(vec.Z));
    }
}
