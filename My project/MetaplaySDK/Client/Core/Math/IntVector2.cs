// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using System;
using static System.FormattableString;

namespace Metaplay.Core.Math
{
    [MetaSerializable]
    public struct IntVector2 : IEquatable<IntVector2>, IComparable<IntVector2>
    {
        // Constants
        public static IntVector2 Zero   => new IntVector2(0, 0);
        public static IntVector2 One    => new IntVector2(1, 1);
        public static IntVector2 Down   => new IntVector2(0, -1);
        public static IntVector2 Up     => new IntVector2(0, 1);
        public static IntVector2 Left   => new IntVector2(-1, 0);
        public static IntVector2 Right  => new IntVector2(1, 0);

        [MetaMember(1)] public int X;
        [MetaMember(2)] public int Y;

        public IntVector2(int x, int y) { X = x; Y = y; }

        public override string ToString() => Invariant($"({X}, {Y})");

        public static IntVector2 operator +(IntVector2 a, IntVector2 b) => new IntVector2(a.X + b.X, a.Y + b.Y);
        public static IntVector2 operator -(IntVector2 a, IntVector2 b) => new IntVector2(a.X - b.X, a.Y - b.Y);
        public static IntVector2 operator *(IntVector2 a, IntVector2 b) => new IntVector2(a.X * b.X, a.Y * b.Y);
        public static IntVector2 operator *(IntVector2 a, int b) => new IntVector2(a.X * b, a.Y * b);
        public static IntVector2 operator *(int a, IntVector2 b) => new IntVector2(a * b.X, a * b.Y);

        public static bool operator ==(IntVector2 a, IntVector2 b) => (a.X == b.X) && (a.Y == b.Y);
        public static bool operator !=(IntVector2 a, IntVector2 b) => (a.X != b.X) || (a.Y != b.Y);

        public readonly bool Equals(IntVector2 other) => this == other;
        public override readonly bool Equals(object obj) => (obj is IntVector2 other) ? (this == other) : false;

        public override readonly int GetHashCode() => Util.CombineHashCode(X.GetHashCode(), Y.GetHashCode());

        public readonly F32Vec2 ToF32Vec2() => F32Vec2.FromInt(X, Y);

        int IComparable<IntVector2>.CompareTo(IntVector2 other)
        {
            int v = X.CompareTo(other.X);
            return (v != 0) ? v : Y.CompareTo(other.Y);
        }
    }

    public static partial class VecExtensions
    {
        public static IntVector2 RoundToInt(this F32Vec2 vec) => new IntVector2(F32.RoundToInt(vec.X), F32.RoundToInt(vec.Y));
    }
}
