//
// FixPointCS
//
// Copyright(c) 2018 Jere Sanisalo, Petri Kero
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
//
using FixPointCS;
using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

namespace Metaplay.Core.Math
{
    /// <summary>
    /// Signed 32.32 fixed point value struct.
    /// </summary>
    public struct F64 : IComparable<F64>, IEquatable<F64>, IComparable
    {
        // Constants
        public static F64 Neg1      { [MethodImpl(FixedUtil.AggressiveInlining)] get { return FromRaw(Fixed64.Neg1); } }
        public static F64 Zero      { [MethodImpl(FixedUtil.AggressiveInlining)] get { return FromRaw(Fixed64.Zero); } }
        public static F64 Half      { [MethodImpl(FixedUtil.AggressiveInlining)] get { return FromRaw(Fixed64.Half); } }
        public static F64 One       { [MethodImpl(FixedUtil.AggressiveInlining)] get { return FromRaw(Fixed64.One); } }
        public static F64 Two       { [MethodImpl(FixedUtil.AggressiveInlining)] get { return FromRaw(Fixed64.Two); } }
        public static F64 Pi        { [MethodImpl(FixedUtil.AggressiveInlining)] get { return FromRaw(Fixed64.Pi); } }
        public static F64 Pi2       { [MethodImpl(FixedUtil.AggressiveInlining)] get { return FromRaw(Fixed64.Pi2); } }
        public static F64 PiHalf    { [MethodImpl(FixedUtil.AggressiveInlining)] get { return FromRaw(Fixed64.PiHalf); } }
        public static F64 E         { [MethodImpl(FixedUtil.AggressiveInlining)] get { return FromRaw(Fixed64.E); } }

        public static F64 MinValue  { [MethodImpl(FixedUtil.AggressiveInlining)] get { return FromRaw(Fixed64.MinValue); } }
        public static F64 MaxValue  { [MethodImpl(FixedUtil.AggressiveInlining)] get { return FromRaw(Fixed64.MaxValue); } }

        // Raw fixed point value
        public long Raw;

        // Construction
        [MethodImpl(FixedUtil.AggressiveInlining)] public static F64 FromRaw(long raw) { F64 v; v.Raw = raw; return v; }
        [MethodImpl(FixedUtil.AggressiveInlining)] public static F64 FromInt(int v) { return FromRaw(Fixed64.FromInt(v)); }
        [MethodImpl(FixedUtil.AggressiveInlining)] public static F64 FromFloat(float v) { return FromRaw(Fixed64.FromFloat(v)); }
        [MethodImpl(FixedUtil.AggressiveInlining)] public static F64 FromDouble(double v) { return FromRaw(Fixed64.FromDouble(v)); }
        [MethodImpl(FixedUtil.AggressiveInlining)] public static F64 FromF32(F32 v) { return FromRaw((long)v.Raw << 16); }

        // Conversions
        public static int FloorToInt(F64 a) { return Fixed64.FloorToInt(a.Raw); }
        public static int CeilToInt(F64 a) { return Fixed64.CeilToInt(a.Raw); }
        public static int RoundToInt(F64 a) { return Fixed64.RoundToInt(a.Raw); }
        [IgnoreDataMember]
        public readonly float  Float  => Fixed64.ToFloat(Raw);
        [IgnoreDataMember]
        public readonly double Double => Fixed64.ToDouble(Raw);
        [IgnoreDataMember]
        public readonly F32    F32    => F32.FromRaw((int)(Raw >> 16));

        // Creates the fixed point number that's a divided by b.
        public static F64 Ratio(int a, int b) { return F64.FromRaw(((long)a << 32) / b); }
        // Creates the fixed point number that's a divided by 10.
        public static F64 Ratio10(int a) { return F64.FromRaw(((long)a << 32) / 10); }
        // Creates the fixed point number that's a divided by 100.
        public static F64 Ratio100(int a) { return F64.FromRaw(((long)a << 32) / 100); }
        // Creates the fixed point number that's a divided by 1000.
        public static F64 Ratio1000(int a) { return F64.FromRaw(((long)a << 32) / 1000); }

        // Operators
        public static F64 operator -(F64 v1) { return FromRaw(-v1.Raw); }

        public static F64 operator +(F64 v1, F64 v2) { return FromRaw(v1.Raw + v2.Raw); }
        public static F64 operator -(F64 v1, F64 v2) { return FromRaw(v1.Raw - v2.Raw); }
        public static F64 operator *(F64 v1, F64 v2) { return FromRaw(Fixed64.Mul(v1.Raw, v2.Raw)); }
        public static F64 operator /(F64 v1, F64 v2) { return FromRaw(Fixed64.DivPrecise(v1.Raw, v2.Raw)); }
        public static F64 operator %(F64 v1, F64 v2) { return FromRaw(Fixed64.Mod(v1.Raw, v2.Raw)); }

        public static F64 operator +(F64 v1, int v2) { return FromRaw(v1.Raw + Fixed64.FromInt(v2)); }
        public static F64 operator +(int v1, F64 v2) { return FromRaw(Fixed64.FromInt(v1) + v2.Raw); }
        public static F64 operator -(F64 v1, int v2) { return FromRaw(v1.Raw - Fixed64.FromInt(v2)); }
        public static F64 operator -(int v1, F64 v2) { return FromRaw(Fixed64.FromInt(v1) - v2.Raw); }
        public static F64 operator *(F64 v1, int v2) { return FromRaw(v1.Raw * (long)v2); }
        public static F64 operator *(int v1, F64 v2) { return FromRaw((long)v1 * v2.Raw); }
        public static F64 operator /(F64 v1, int v2) { return FromRaw(v1.Raw / (long)v2); }
        public static F64 operator /(int v1, F64 v2) { return FromRaw(Fixed64.DivPrecise(Fixed64.FromInt(v1), v2.Raw)); }
        public static F64 operator %(F64 v1, int v2) { return FromRaw(Fixed64.Mod(v1.Raw, Fixed64.FromInt(v2))); }
        public static F64 operator %(int v1, F64 v2) { return FromRaw(Fixed64.Mod(Fixed64.FromInt(v1), v2.Raw)); }

        public static F64 operator ++(F64 v1) { return FromRaw(v1.Raw + Fixed64.One); }
        public static F64 operator --(F64 v1) { return FromRaw(v1.Raw - Fixed64.One); }

        public static bool operator ==(F64 v1, F64 v2) { return v1.Raw == v2.Raw; }
        public static bool operator !=(F64 v1, F64 v2) { return v1.Raw != v2.Raw; }
        public static bool operator <(F64 v1, F64 v2) { return v1.Raw < v2.Raw; }
        public static bool operator <=(F64 v1, F64 v2) { return v1.Raw <= v2.Raw; }
        public static bool operator >(F64 v1, F64 v2) { return v1.Raw > v2.Raw; }
        public static bool operator >=(F64 v1, F64 v2) { return v1.Raw >= v2.Raw; }

        public static bool operator ==(int v1, F64 v2) { return Fixed64.FromInt(v1) == v2.Raw; }
        public static bool operator ==(F64 v1, int v2) { return v1.Raw == Fixed64.FromInt(v2); }
        public static bool operator !=(int v1, F64 v2) { return Fixed64.FromInt(v1) != v2.Raw; }
        public static bool operator !=(F64 v1, int v2) { return v1.Raw != Fixed64.FromInt(v2); }
        public static bool operator <(int v1, F64 v2) { return Fixed64.FromInt(v1) < v2.Raw; }
        public static bool operator <(F64 v1, int v2) { return v1.Raw < Fixed64.FromInt(v2); }
        public static bool operator <=(int v1, F64 v2) { return Fixed64.FromInt(v1) <= v2.Raw; }
        public static bool operator <=(F64 v1, int v2) { return v1.Raw <= Fixed64.FromInt(v2); }
        public static bool operator >(int v1, F64 v2) { return Fixed64.FromInt(v1) > v2.Raw; }
        public static bool operator >(F64 v1, int v2) { return v1.Raw > Fixed64.FromInt(v2); }
        public static bool operator >=(int v1, F64 v2) { return Fixed64.FromInt(v1) >= v2.Raw; }
        public static bool operator >=(F64 v1, int v2) { return v1.Raw >= Fixed64.FromInt(v2); }

        public static bool operator ==(F32 a, F64 b) { return F64.FromF32(a) == b; }
        public static bool operator ==(F64 a, F32 b) { return a == F64.FromF32(b); }
        public static bool operator !=(F32 a, F64 b) { return F64.FromF32(a) != b; }
        public static bool operator !=(F64 a, F32 b) { return a != F64.FromF32(b); }
        public static bool operator <(F32 a, F64 b) { return F64.FromF32(a) < b; }
        public static bool operator <(F64 a, F32 b) { return a < F64.FromF32(b); }
        public static bool operator <=(F32 a, F64 b) { return F64.FromF32(a) <= b; }
        public static bool operator <=(F64 a, F32 b) { return a <= F64.FromF32(b); }
        public static bool operator >(F32 a, F64 b) { return F64.FromF32(a) > b; }
        public static bool operator >(F64 a, F32 b) { return a > F64.FromF32(b); }
        public static bool operator >=(F32 a, F64 b) { return F64.FromF32(a) >= b; }
        public static bool operator >=(F64 a, F32 b) { return a >= F64.FromF32(b); }

        public static F64 RadToDeg(F64 a) { return FromRaw(Fixed64.Mul(a.Raw, 246083499198)); } // 180 / F64.Pi
        public static F64 DegToRad(F64 a) { return FromRaw(Fixed64.Mul(a.Raw, 74961320)); }     // F64.Pi / 180

        public static F64 Div2(F64 a) { return FromRaw(a.Raw >> 1); }
        public static F64 Abs(F64 a) { return FromRaw(Fixed64.Abs(a.Raw)); }
        public static F64 Nabs(F64 a) { return FromRaw(Fixed64.Nabs(a.Raw)); }
        public static int Sign(F64 a) { return Fixed64.Sign(a.Raw); }
        public static F64 Ceil(F64 a) { return FromRaw(Fixed64.Ceil(a.Raw)); }
        public static F64 Floor(F64 a) { return FromRaw(Fixed64.Floor(a.Raw)); }
        public static F64 Round(F64 a) { return FromRaw(Fixed64.Round(a.Raw)); }
        public static F64 Fract(F64 a) { return FromRaw(Fixed64.Fract(a.Raw)); }
        public static F64 Div(F64 a, F64 b) { return FromRaw(Fixed64.Div(a.Raw, b.Raw)); }
        public static F64 DivFast(F64 a, F64 b) { return FromRaw(Fixed64.DivFast(a.Raw, b.Raw)); }
        public static F64 DivFastest(F64 a, F64 b) { return FromRaw(Fixed64.DivFastest(a.Raw, b.Raw)); }
        public static F64 SqrtPrecise(F64 a) { return FromRaw(Fixed64.SqrtPrecise(a.Raw)); }
        public static F64 Sqrt(F64 a) { return FromRaw(Fixed64.Sqrt(a.Raw)); }
        public static F64 SqrtFast(F64 a) { return FromRaw(Fixed64.SqrtFast(a.Raw)); }
        public static F64 SqrtFastest(F64 a) { return FromRaw(Fixed64.SqrtFastest(a.Raw)); }
        public static F64 RSqrt(F64 a) { return FromRaw(Fixed64.RSqrt(a.Raw)); }
        public static F64 RSqrtFast(F64 a) { return FromRaw(Fixed64.RSqrtFast(a.Raw)); }
        public static F64 RSqrtFastest(F64 a) { return FromRaw(Fixed64.RSqrtFastest(a.Raw)); }
        public static F64 Rcp(F64 a) { return FromRaw(Fixed64.Rcp(a.Raw)); }
        public static F64 RcpFast(F64 a) { return FromRaw(Fixed64.RcpFast(a.Raw)); }
        public static F64 RcpFastest(F64 a) { return FromRaw(Fixed64.RcpFastest(a.Raw)); }
        public static F64 Exp(F64 a) { return FromRaw(Fixed64.Exp(a.Raw)); }
        public static F64 ExpFast(F64 a) { return FromRaw(Fixed64.ExpFast(a.Raw)); }
        public static F64 ExpFastest(F64 a) { return FromRaw(Fixed64.ExpFastest(a.Raw)); }
        public static F64 Exp2(F64 a) { return FromRaw(Fixed64.Exp2(a.Raw)); }
        public static F64 Exp2Fast(F64 a) { return FromRaw(Fixed64.Exp2Fast(a.Raw)); }
        public static F64 Exp2Fastest(F64 a) { return FromRaw(Fixed64.Exp2Fastest(a.Raw)); }
        public static F64 Log(F64 a) { return FromRaw(Fixed64.Log(a.Raw)); }
        public static F64 LogFast(F64 a) { return FromRaw(Fixed64.LogFast(a.Raw)); }
        public static F64 LogFastest(F64 a) { return FromRaw(Fixed64.LogFastest(a.Raw)); }
        public static F64 Log2(F64 a) { return FromRaw(Fixed64.Log2(a.Raw)); }
        public static F64 Log2Fast(F64 a) { return FromRaw(Fixed64.Log2Fast(a.Raw)); }
        public static F64 Log2Fastest(F64 a) { return FromRaw(Fixed64.Log2Fastest(a.Raw)); }

        public static F64 Sin(F64 a) { return FromRaw(Fixed64.Sin(a.Raw)); }
        public static F64 SinFast(F64 a) { return FromRaw(Fixed64.SinFast(a.Raw)); }
        public static F64 SinFastest(F64 a) { return FromRaw(Fixed64.SinFastest(a.Raw)); }
        public static F64 Cos(F64 a) { return FromRaw(Fixed64.Cos(a.Raw)); }
        public static F64 CosFast(F64 a) { return FromRaw(Fixed64.CosFast(a.Raw)); }
        public static F64 CosFastest(F64 a) { return FromRaw(Fixed64.CosFastest(a.Raw)); }
        public static F64 Tan(F64 a) { return FromRaw(Fixed64.Tan(a.Raw)); }
        public static F64 TanFast(F64 a) { return FromRaw(Fixed64.TanFast(a.Raw)); }
        public static F64 TanFastest(F64 a) { return FromRaw(Fixed64.TanFastest(a.Raw)); }
        public static F64 Asin(F64 a) { return FromRaw(Fixed64.Asin(a.Raw)); }
        public static F64 AsinFast(F64 a) { return FromRaw(Fixed64.AsinFast(a.Raw)); }
        public static F64 AsinFastest(F64 a) { return FromRaw(Fixed64.AsinFastest(a.Raw)); }
        public static F64 Acos(F64 a) { return FromRaw(Fixed64.Acos(a.Raw)); }
        public static F64 AcosFast(F64 a) { return FromRaw(Fixed64.AcosFast(a.Raw)); }
        public static F64 AcosFastest(F64 a) { return FromRaw(Fixed64.AcosFastest(a.Raw)); }
        public static F64 Atan(F64 a) { return FromRaw(Fixed64.Atan(a.Raw)); }
        public static F64 AtanFast(F64 a) { return FromRaw(Fixed64.AtanFast(a.Raw)); }
        public static F64 AtanFastest(F64 a) { return FromRaw(Fixed64.AtanFastest(a.Raw)); }
        public static F64 Atan2(F64 y, F64 x) { return FromRaw(Fixed64.Atan2(y.Raw, x.Raw)); }
        public static F64 Atan2Fast(F64 y, F64 x) { return FromRaw(Fixed64.Atan2Fast(y.Raw, x.Raw)); }
        public static F64 Atan2Fastest(F64 y, F64 x) { return FromRaw(Fixed64.Atan2Fastest(y.Raw, x.Raw)); }
        public static F64 Pow(F64 a, F64 b) { return FromRaw(Fixed64.Pow(a.Raw, b.Raw)); }
        public static F64 PowFast(F64 a, F64 b) { return FromRaw(Fixed64.PowFast(a.Raw, b.Raw)); }
        public static F64 PowFastest(F64 a, F64 b) { return FromRaw(Fixed64.PowFastest(a.Raw, b.Raw)); }

        public static F64 Min(F64 a, F64 b) { return FromRaw(Fixed64.Min(a.Raw, b.Raw)); }
        public static F64 Max(F64 a, F64 b) { return FromRaw(Fixed64.Max(a.Raw, b.Raw)); }
        public static F64 Clamp(F64 a, F64 min, F64 max) { return FromRaw(Fixed64.Clamp(a.Raw, min.Raw, max.Raw)); }
        public static F64 Clamp01(F64 a) { return FromRaw(Fixed64.Clamp(a.Raw, Fixed64.Zero, Fixed64.One)); }

        public static F64 Lerp(F64 a, F64 b, F64 t)
        {
            long tb = t.Raw;
            long ta = Fixed64.One - tb;
            return FromRaw(Fixed64.Mul(a.Raw, ta) + Fixed64.Mul(b.Raw, tb));
        }

        public readonly bool Equals(F64 other)
        {
            return (Raw == other.Raw);
        }

        public override readonly bool Equals(object obj)
        {
            if (!(obj is F64))
                return false;
            return ((F64)obj).Raw == Raw;
        }

        public readonly int CompareTo(F64 other)
        {
            if (Raw < other.Raw) return -1;
            if (Raw > other.Raw) return +1;
            return 0;
        }

        static readonly long[] _longPow10Table = new long[]
        {
            1,
            10,
            100,
            1_000,
            10_000,
            100_000,
            1_000_000,
            10_000_000,
            100_000_000,
            1_000_000_000,
            10_000_000_000,
            100_000_000_000,
            1_000_000_000_000,
            10_000_000_000_000,
            100_000_000_000_000,
            1_000_000_000_000_000,
        };

        // \note System char.IsDigit() also returns true for some non-ASCII Unicode chars which we don't want!
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool IsDigit(char c) => c >= '0' && c <= '9';

#if METAPLAY_USE_LEGACY_FIXED_POINT_PARSING
        static long IntPow(long a, int b)
        {
            long res = 1;
            for (int i = 0; i < b; i++)
                res *= a;
            return res;
        }

        /// <summary>
        /// Support for matching fixed-point parsing exactly with the pre-R25 version.
        /// In R25, the parsing and ToString() methods were improved to behave much better,
        /// but this is here in case the change is too big for an existing project.
        /// </summary>
        public static F64 Parse(string str)
        {
            // \todo [petri] naive implementation
            str = str.Trim();
            string[] parts = str.Split(new char[] { '.', ',' });
            if (parts.Length > 2)
                throw new InvalidOperationException($"F64.Parse(): invalid format '{str}'");
            int integer = int.Parse(parts[0], CultureInfo.InvariantCulture);

            if (parts.Length == 2)
            {
                // cap frac length to 15, to avoid overflows
                string frac = (parts[1].Length > 15) ? parts[1].Substring(0, 15) : parts[1];
                int fracLen = frac.Length;
                long fracInt = long.Parse(frac, CultureInfo.InvariantCulture);
                long divisor = IntPow(10, fracLen);
                int sign = (parts[0][0] == '-') ? -1 : +1;
                return FromInt(integer) + F64.FromRaw(sign * fracInt) / F64.FromRaw(divisor);
            }
            else
                return FromInt(integer);
        }
#else
        /// <summary>
        /// Parse a fixed-point number from a string. This method is deterministic.
        /// Only the dot ('.') is allowed as a separator between the integer and fraction
        /// parts, regardless of currently active culture. Only '{integer}.{fraction}' format
        /// is supported, i.e., scientific format is not. The accepted format otherwise follows
        /// C# float/double formatting rules for parsing.
        /// </summary>
        /// <exception cref="ArgumentException">Thrown if the input is not a valid decimal number.</exception>
        /// <exception cref="OverflowException">Thrown if the input value is too big to fit into an F64.</exception>
        public static F64 Parse(ReadOnlySpan<char> str)
        {
            // Trim leading/trailing whitespace
            str = str.Trim();

            // Check for empty input
            if (str.Length == 0)
                throw new ArgumentException($"Invalid fixed-point string value '{new string(str)}'", nameof(str));

            // Parse sign (both + and - accepted)
            int offset = 0;
            bool isNegative = str[0] == '-';
            if (isNegative || str[0] == '+')
                offset = 1; // skip sign
            int sign = isNegative ? -1 : +1;

            // Parse all digits (they form the integer part)
            // \note Parse as long so that int.MinValue parses correctly
            int intStartOffset = offset;
            while (offset < str.Length && IsDigit(str[offset]))
                offset++;
            int numIntDigits = offset - intStartOffset;
            long integer = 0;
            if (numIntDigits > 0)
            {
                integer = sign * long.Parse(str.Slice(intStartOffset, offset - intStartOffset), NumberStyles.Integer, CultureInfo.InvariantCulture); // \note throws OverflowException for large values
                if (integer < int.MinValue || integer > int.MaxValue)
                    throw new OverflowException($"Input '{new string(str)}' does not fit into F64");
            }

            // Skip the decimal separator, if exists
            if (offset < str.Length && str[offset] == '.')
                offset += 1;

            // If input ends, there are no fractional digits
            if (offset == str.Length)
            {
                // If no fractional digits, then some integer digits must exist (we don't accept just a dot '.')
                if (numIntDigits == 0)
                    throw new ArgumentException($"Invalid fixed-point string value '{new string(str)}'", nameof(str));

                return FromInt((int)integer);
            }

            // Parse the fractional part
            int fracStartOffset = offset;
            while (offset < str.Length && IsDigit(str[offset]))
                offset++;
            int numFracDigits = offset - fracStartOffset;

            // Must have digits in at least one of integer or fraction part
            if (numIntDigits == 0 && numFracDigits == 0)
                throw new ArgumentException($"Invalid fixed-point string value '{new string(str)}'", nameof(str));

            // Must be at end-of-input
            if (offset != str.Length)
                throw new ArgumentException($"Invalid fixed-point string value '{new string(str)}'", nameof(str));

            // Resolve fractional part (cap number of parsed digits to avoid overflow)
            int fracLen = System.Math.Min(offset - fracStartOffset, 10);
            int fracEnd = fracStartOffset + fracLen;
            int preShift = fracLen - 1;
            long fracInt = long.Parse(str[fracStartOffset..fracEnd], NumberStyles.Integer, CultureInfo.InvariantCulture);
            long divisor = _longPow10Table[fracLen] >> preShift;
            long fraction = sign * ((fracInt << (32 - preShift)) + (divisor >> 1)) / divisor;
            if (integer == int.MinValue && fraction < 0)
                throw new OverflowException($"Input '{new string(str)}' does not fit into F64");
            return FromRaw((integer << 32) + fraction);
        }

        public static F64 Parse(string str)
        {
            return Parse(str.AsSpan());
        }
#endif // METAPLAY_USE_LEGACY_FIXED_POINT_PARSING

        /// <summary>
        /// Convert the fixed-point value to string. The output string is deterministic, can be parsed
        /// back to the original value using <see cref="Parse"/> and is the shortest string that satisfies
        /// the condition for better human-readability. The dot ('.') is used as the separator between
        /// integer and fraction parts, regardless of currently active culture. The output is always in
        /// the '{integer}.{fraction}' format, i.e., scientific format is not used.
        /// </summary>
        public override readonly string ToString()
        {
            return Fixed64.ToString(Raw);
        }

        public override readonly int GetHashCode()
        {
            return Raw.GetHashCode();
        }

        int IComparable.CompareTo(object obj)
        {
            if (obj is F64 other)
                return CompareTo(other);
            else if (obj is null)
                return 1;
            // don't allow comparisons with other numeric or non-numeric types.
            throw new ArgumentException("F64 can only be compared against another F64.");
        }
    }
}
