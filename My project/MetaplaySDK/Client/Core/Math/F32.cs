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
    /// Signed 16.16 fixed point value struct.
    /// </summary>
    public struct F32 : IComparable<F32>, IEquatable<F32>, IComparable
    {
        // Constants
        public static F32 Neg1      { [MethodImpl(FixedUtil.AggressiveInlining)] get { return FromRaw(Fixed32.Neg1); } }
        public static F32 Zero      { [MethodImpl(FixedUtil.AggressiveInlining)] get { return FromRaw(Fixed32.Zero); } }
        public static F32 Half      { [MethodImpl(FixedUtil.AggressiveInlining)] get { return FromRaw(Fixed32.Half); } }
        public static F32 One       { [MethodImpl(FixedUtil.AggressiveInlining)] get { return FromRaw(Fixed32.One); } }
        public static F32 Two       { [MethodImpl(FixedUtil.AggressiveInlining)] get { return FromRaw(Fixed32.Two); } }
        public static F32 Pi        { [MethodImpl(FixedUtil.AggressiveInlining)] get { return FromRaw(Fixed32.Pi); } }
        public static F32 Pi2       { [MethodImpl(FixedUtil.AggressiveInlining)] get { return FromRaw(Fixed32.Pi2); } }
        public static F32 PiHalf    { [MethodImpl(FixedUtil.AggressiveInlining)] get { return FromRaw(Fixed32.PiHalf); } }
        public static F32 E         { [MethodImpl(FixedUtil.AggressiveInlining)] get { return FromRaw(Fixed32.E); } }

        public static F32 MinValue  { [MethodImpl(FixedUtil.AggressiveInlining)] get { return FromRaw(Fixed32.MinValue); } }
        public static F32 MaxValue  { [MethodImpl(FixedUtil.AggressiveInlining)] get { return FromRaw(Fixed32.MaxValue); } }

        // Raw fixed point value
        public int Raw;

        // Construction
        [MethodImpl(FixedUtil.AggressiveInlining)] public static F32 FromRaw(int raw) { F32 v; v.Raw = raw; return v; }
        [MethodImpl(FixedUtil.AggressiveInlining)] public static F32 FromInt(int v) { return FromRaw(Fixed32.FromInt(v)); }
        [MethodImpl(FixedUtil.AggressiveInlining)] public static F32 FromFloat(float v) { return FromRaw(Fixed32.FromFloat(v)); }
        [MethodImpl(FixedUtil.AggressiveInlining)] public static F32 FromDouble(double v) { return FromRaw(Fixed32.FromDouble(v)); }
        [MethodImpl(FixedUtil.AggressiveInlining)] public static F32 FromF64(F64 v) { return FromRaw((int)(v.Raw >> 16)); }

        // Conversions
        public static int FloorToInt(F32 a) { return Fixed32.FloorToInt(a.Raw); }
        public static int CeilToInt(F32 a) { return Fixed32.CeilToInt(a.Raw); }
        public static int RoundToInt(F32 a) { return Fixed32.RoundToInt(a.Raw); }
        [IgnoreDataMember]
        public readonly float  Float  => Fixed32.ToFloat(Raw);
        [IgnoreDataMember]
        public readonly double Double => Fixed32.ToDouble(Raw);
        [IgnoreDataMember]
        public readonly F64    F64    => F64.FromRaw((long)Raw << 16);

        // Creates the fixed point number that's a divided by b.
        public static F32 Ratio(int a, int b) { return F32.FromRaw((int)(((long)a << 16) / b)); }
        // Creates the fixed point number that's a divided by 10.
        public static F32 Ratio10(int a) { return F32.FromRaw((int)(((long)a << 16) / 10)); }
        // Creates the fixed point number that's a divided by 100.
        public static F32 Ratio100(int a) { return F32.FromRaw((int)(((long)a << 16) / 100)); }
        // Creates the fixed point number that's a divided by 1000.
        public static F32 Ratio1000(int a) { return F32.FromRaw((int)(((long)a << 16) / 1000)); }

        // Operators
        public static F32 operator -(F32 v1) { return FromRaw(-v1.Raw); }

        //public static F32 operator +(F32 v1, F32 v2) { F32 r; r.raw = v1.raw + v2.raw; return r; }
        public static F32 operator +(F32 v1, F32 v2) { return FromRaw(v1.Raw + v2.Raw); }
        public static F32 operator -(F32 v1, F32 v2) { return FromRaw(v1.Raw - v2.Raw); }
        public static F32 operator *(F32 v1, F32 v2) { return FromRaw(Fixed32.Mul(v1.Raw, v2.Raw)); }
        public static F32 operator /(F32 v1, F32 v2) { return FromRaw(Fixed32.DivPrecise(v1.Raw, v2.Raw)); }
        public static F32 operator %(F32 v1, F32 v2) { return FromRaw(Fixed32.Mod(v1.Raw, v2.Raw)); }

        public static F32 operator +(F32 v1, int v2) { return FromRaw(v1.Raw + Fixed32.FromInt(v2)); }
        public static F32 operator +(int v1, F32 v2) { return FromRaw(Fixed32.FromInt(v1) + v2.Raw); }
        public static F32 operator -(F32 v1, int v2) { return FromRaw(v1.Raw - Fixed32.FromInt(v2)); }
        public static F32 operator -(int v1, F32 v2) { return FromRaw(Fixed32.FromInt(v1) - v2.Raw); }
        public static F32 operator *(F32 v1, int v2) { return FromRaw(v1.Raw * (int)v2); }
        public static F32 operator *(int v1, F32 v2) { return FromRaw((int)v1 * v2.Raw); }
        public static F32 operator /(F32 v1, int v2) { return FromRaw(v1.Raw / (int)v2); }
        public static F32 operator /(int v1, F32 v2) { return FromRaw(Fixed32.DivPrecise(Fixed32.FromInt(v1), v2.Raw)); }
        public static F32 operator %(F32 v1, int v2) { return FromRaw(Fixed32.Mod(v1.Raw, Fixed32.FromInt(v2))); }
        public static F32 operator %(int v1, F32 v2) { return FromRaw(Fixed32.Mod(Fixed32.FromInt(v1), v2.Raw)); }

        public static F32 operator ++(F32 v1) { return FromRaw(v1.Raw + Fixed32.One); }
        public static F32 operator --(F32 v1) { return FromRaw(v1.Raw - Fixed32.One); }

        public static bool operator ==(F32 v1, F32 v2) { return v1.Raw == v2.Raw; }
        public static bool operator !=(F32 v1, F32 v2) { return v1.Raw != v2.Raw; }
        public static bool operator <(F32 v1, F32 v2) { return v1.Raw < v2.Raw; }
        public static bool operator <=(F32 v1, F32 v2) { return v1.Raw <= v2.Raw; }
        public static bool operator >(F32 v1, F32 v2) { return v1.Raw > v2.Raw; }
        public static bool operator >=(F32 v1, F32 v2) { return v1.Raw >= v2.Raw; }

        public static bool operator ==(int v1, F32 v2) { return Fixed32.FromInt(v1) == v2.Raw; }
        public static bool operator ==(F32 v1, int v2) { return v1.Raw == Fixed32.FromInt(v2); }
        public static bool operator !=(int v1, F32 v2) { return Fixed32.FromInt(v1) != v2.Raw; }
        public static bool operator !=(F32 v1, int v2) { return v1.Raw != Fixed32.FromInt(v2); }
        public static bool operator <(int v1, F32 v2) { return Fixed32.FromInt(v1) < v2.Raw; }
        public static bool operator <(F32 v1, int v2) { return v1.Raw < Fixed32.FromInt(v2); }
        public static bool operator <=(int v1, F32 v2) { return Fixed32.FromInt(v1) <= v2.Raw; }
        public static bool operator <=(F32 v1, int v2) { return v1.Raw <= Fixed32.FromInt(v2); }
        public static bool operator >(int v1, F32 v2) { return Fixed32.FromInt(v1) > v2.Raw; }
        public static bool operator >(F32 v1, int v2) { return v1.Raw > Fixed32.FromInt(v2); }
        public static bool operator >=(int v1, F32 v2) { return Fixed32.FromInt(v1) >= v2.Raw; }
        public static bool operator >=(F32 v1, int v2) { return v1.Raw >= Fixed32.FromInt(v2); }

        public static F32 RadToDeg(F32 a) { return FromRaw(Fixed32.Mul(a.Raw, 3754943)); }  // 180 / F32.Pi
        public static F32 DegToRad(F32 a) { return FromRaw(Fixed32.Mul(a.Raw, 1143)); }     // F32.Pi / 180

        public static F32 Div2(F32 a) { return FromRaw(a.Raw >> 1); }
        public static F32 Abs(F32 a) { return FromRaw(Fixed32.Abs(a.Raw)); }
        public static F32 Nabs(F32 a) { return FromRaw(Fixed32.Nabs(a.Raw)); }
        public static int Sign(F32 a) { return Fixed32.Sign(a.Raw); }
        public static F32 Ceil(F32 a) { return FromRaw(Fixed32.Ceil(a.Raw)); }
        public static F32 Floor(F32 a) { return FromRaw(Fixed32.Floor(a.Raw)); }
        public static F32 Round(F32 a) { return FromRaw(Fixed32.Round(a.Raw)); }
        public static F32 Fract(F32 a) { return FromRaw(Fixed32.Fract(a.Raw)); }
        public static F32 Div(F32 a, F32 b) { return FromRaw(Fixed32.Div(a.Raw, b.Raw)); }
        public static F32 DivFast(F32 a, F32 b) { return FromRaw(Fixed32.DivFast(a.Raw, b.Raw)); }
        public static F32 DivFastest(F32 a, F32 b) { return FromRaw(Fixed32.DivFastest(a.Raw, b.Raw)); }
        public static F32 SqrtPrecise(F32 a) { return FromRaw(Fixed32.SqrtPrecise(a.Raw)); }
        public static F32 Sqrt(F32 a) { return FromRaw(Fixed32.Sqrt(a.Raw)); }
        public static F32 SqrtFast(F32 a) { return FromRaw(Fixed32.SqrtFast(a.Raw)); }
        public static F32 SqrtFastest(F32 a) { return FromRaw(Fixed32.SqrtFastest(a.Raw)); }
        public static F32 RSqrt(F32 a) { return FromRaw(Fixed32.RSqrt(a.Raw)); }
        public static F32 RSqrtFast(F32 a) { return FromRaw(Fixed32.RSqrtFast(a.Raw)); }
        public static F32 RSqrtFastest(F32 a) { return FromRaw(Fixed32.RSqrtFastest(a.Raw)); }
        public static F32 Rcp(F32 a) { return FromRaw(Fixed32.Rcp(a.Raw)); }
        public static F32 RcpFast(F32 a) { return FromRaw(Fixed32.RcpFast(a.Raw)); }
        public static F32 RcpFastest(F32 a) { return FromRaw(Fixed32.RcpFastest(a.Raw)); }
        public static F32 Exp(F32 a) { return FromRaw(Fixed32.Exp(a.Raw)); }
        public static F32 ExpFast(F32 a) { return FromRaw(Fixed32.ExpFast(a.Raw)); }
        public static F32 ExpFastest(F32 a) { return FromRaw(Fixed32.ExpFastest(a.Raw)); }
        public static F32 Exp2(F32 a) { return FromRaw(Fixed32.Exp2(a.Raw)); }
        public static F32 Exp2Fast(F32 a) { return FromRaw(Fixed32.Exp2Fast(a.Raw)); }
        public static F32 Exp2Fastest(F32 a) { return FromRaw(Fixed32.Exp2Fastest(a.Raw)); }
        public static F32 Log(F32 a) { return FromRaw(Fixed32.Log(a.Raw)); }
        public static F32 LogFast(F32 a) { return FromRaw(Fixed32.LogFast(a.Raw)); }
        public static F32 LogFastest(F32 a) { return FromRaw(Fixed32.LogFastest(a.Raw)); }
        public static F32 Log2(F32 a) { return FromRaw(Fixed32.Log2(a.Raw)); }
        public static F32 Log2Fast(F32 a) { return FromRaw(Fixed32.Log2Fast(a.Raw)); }
        public static F32 Log2Fastest(F32 a) { return FromRaw(Fixed32.Log2Fastest(a.Raw)); }

        public static F32 Sin(F32 a) { return FromRaw(Fixed32.Sin(a.Raw)); }
        public static F32 SinFast(F32 a) { return FromRaw(Fixed32.SinFast(a.Raw)); }
        public static F32 SinFastest(F32 a) { return FromRaw(Fixed32.SinFastest(a.Raw)); }
        public static F32 Cos(F32 a) { return FromRaw(Fixed32.Cos(a.Raw)); }
        public static F32 CosFast(F32 a) { return FromRaw(Fixed32.CosFast(a.Raw)); }
        public static F32 CosFastest(F32 a) { return FromRaw(Fixed32.CosFastest(a.Raw)); }
        public static F32 Tan(F32 a) { return FromRaw(Fixed32.Tan(a.Raw)); }
        public static F32 TanFast(F32 a) { return FromRaw(Fixed32.TanFast(a.Raw)); }
        public static F32 TanFastest(F32 a) { return FromRaw(Fixed32.TanFastest(a.Raw)); }
        public static F32 Asin(F32 a) { return FromRaw(Fixed32.Asin(a.Raw)); }
        public static F32 AsinFast(F32 a) { return FromRaw(Fixed32.AsinFast(a.Raw)); }
        public static F32 AsinFastest(F32 a) { return FromRaw(Fixed32.AsinFastest(a.Raw)); }
        public static F32 Acos(F32 a) { return FromRaw(Fixed32.Acos(a.Raw)); }
        public static F32 AcosFast(F32 a) { return FromRaw(Fixed32.AcosFast(a.Raw)); }
        public static F32 AcosFastest(F32 a) { return FromRaw(Fixed32.AcosFastest(a.Raw)); }
        public static F32 Atan(F32 a) { return FromRaw(Fixed32.Atan(a.Raw)); }
        public static F32 AtanFast(F32 a) { return FromRaw(Fixed32.AtanFast(a.Raw)); }
        public static F32 AtanFastest(F32 a) { return FromRaw(Fixed32.AtanFastest(a.Raw)); }
        public static F32 Atan2(F32 y, F32 x) { return FromRaw(Fixed32.Atan2(y.Raw, x.Raw)); }
        public static F32 Atan2Fast(F32 y, F32 x) { return FromRaw(Fixed32.Atan2Fast(y.Raw, x.Raw)); }
        public static F32 Atan2Fastest(F32 y, F32 x) { return FromRaw(Fixed32.Atan2Fastest(y.Raw, x.Raw)); }
        public static F32 Pow(F32 a, F32 b) { return FromRaw(Fixed32.Pow(a.Raw, b.Raw)); }
        public static F32 PowFast(F32 a, F32 b) { return FromRaw(Fixed32.PowFast(a.Raw, b.Raw)); }
        public static F32 PowFastest(F32 a, F32 b) { return FromRaw(Fixed32.PowFastest(a.Raw, b.Raw)); }

        public static F32 Min(F32 a, F32 b) { return FromRaw(Fixed32.Min(a.Raw, b.Raw)); }
        public static F32 Max(F32 a, F32 b) { return FromRaw(Fixed32.Max(a.Raw, b.Raw)); }
        public static F32 Clamp(F32 a, F32 min, F32 max) { return FromRaw(Fixed32.Clamp(a.Raw, min.Raw, max.Raw)); }
        public static F32 Clamp01(F32 a) { return FromRaw(Fixed32.Clamp(a.Raw, Fixed32.Zero, Fixed32.One)); }

        public static F32 Lerp(F32 a, F32 b, F32 t)
        {
            int tb = t.Raw;
            int ta = Fixed32.One - tb;
            return FromRaw(Fixed32.Mul(a.Raw, ta) + Fixed32.Mul(b.Raw, tb));
        }

        public readonly bool Equals(F32 other)
        {
            return (Raw == other.Raw);
        }

        public override readonly bool Equals(object obj)
        {
            if (!(obj is F32))
                return false;
            return ((F32)obj).Raw == Raw;
        }

        public readonly int CompareTo(F32 other)
        {
            if (Raw < other.Raw) return -1;
            if (Raw > other.Raw) return +1;
            return 0;
        }

        static readonly int[] _intPow10Table = new int[]
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
        };

        // \note System char.IsDigit() also returns true for some non-ASCII Unicode chars which we don't want!
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool IsDigit(char c) => c >= '0' && c <= '9';

#if METAPLAY_USE_LEGACY_FIXED_POINT_PARSING
        static int IntPow(int a, int b)
        {
            int res = 1;
            for (int i = 0; i < b; i++)
                res *= a;
            return res;
        }

        /// <summary>
        /// Support for matching fixed-point parsing exactly with the pre-R25 version.
        /// In R25, the parsing and ToString() methods were improved to behave much better,
        /// but this is here in case the change is too big for an existing project.
        /// </summary>
        public static F32 Parse(string str)
        {
            // Old, naive implementation of fixed-point parsing, tends to round values down compared to accurate results
            str = str.Trim();
            string[] parts = str.Split(new char[] { '.', ',' });
            if (parts.Length > 2)
                throw new InvalidOperationException($"F32.Parse(): invalid format '{str}'");
            int integer = int.Parse(parts[0], CultureInfo.InvariantCulture);

            if (parts.Length == 2)
            {
                // cap frac length, to avoid overflows
                string frac = (parts[1].Length > 8) ? parts[1].Substring(0, 8) : parts[1];
                int fracLen = frac.Length;
                int fracInt = int.Parse(frac, CultureInfo.InvariantCulture);
                int divisor = IntPow(10, fracLen);
                int sign = (parts[0][0] == '-') ? -1 : +1;
                return FromInt(integer) + sign * F32.Ratio(fracInt, divisor);
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
        /// <exception cref="OverflowException">Thrown if the input value is too big to fit into an F32.</exception>
        public static F32 Parse(ReadOnlySpan<char> str)
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

            // Parse integer part digits
            int intStartOffset = offset;
            while (offset < str.Length && IsDigit(str[offset]))
                offset++;
            int numIntDigits = offset - intStartOffset;
            int integer = 0;
            if (numIntDigits > 0)
            {
                integer = sign * int.Parse(str.Slice(intStartOffset, offset - intStartOffset), NumberStyles.Integer, CultureInfo.InvariantCulture); // \note throws OverflowException for large values
                if (integer < -32768 || integer > 32767)
                    throw new OverflowException($"Input '{new string(str)}' does not fit into F32");
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

                return FromInt(integer);
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
            int fracLen = System.Math.Min(offset - fracStartOffset, 8);
            int fracEnd = fracStartOffset + fracLen;
            int fracInt = int.Parse(str[fracStartOffset..fracEnd], NumberStyles.Integer, CultureInfo.InvariantCulture);
            int divisor = _intPow10Table[fracLen];
            long fraction = sign * (((long)fracInt << 16) + (divisor >> 1)) / divisor;
            if (integer == -32768 && fraction < 0)
                throw new OverflowException($"Input '{new string(str)}' does not fit into F32");
            return FromRaw((integer << 16) + (int)fraction);
        }

        public static F32 Parse(string str)
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
            return Fixed32.ToString(Raw);
        }

        public override readonly int GetHashCode()
        {
            // Make sure integer values affect the low bits, otherwise integers values with
            // power-of-two hash tables (eg, OrderedDictionary) cause lots of conflicts. This
            // is equivalent to what long.GetHashCode() does.
            return Raw ^ (Raw >> 16);
        }

        int IComparable.CompareTo(object obj)
        {
            if (obj is F32 other)
                return CompareTo(other);
            else if (obj is null)
                return 1;
            // don't allow comparisons with other numeric or non-numeric types.
            throw new ArgumentException("F32 can only be compared against another F32.");
        }
    }
}
