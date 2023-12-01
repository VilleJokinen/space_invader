// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if UNITY_WEBGL && !UNITY_EDITOR
#   define UNITY_WEBGL_BUILD
#endif

using Metaplay.Core.Tasks;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Metaplay.Core
{
    public static class Util
    {
        public static string BytesToString(byte[] bytes)
        {
            return string.Join(", ", bytes);
        }

        public static string BytesToString(byte[] bytes, int maxLength)
        {
            return string.Join(", ", bytes.Take(maxLength));
        }

        static int ParseHexChar(char hexChar)
        {
            if (hexChar >= '0' && hexChar <= '9')
                return hexChar - '0';
            else if (hexChar >= 'a' && hexChar <= 'f')
                return hexChar - 'a' + 10;
            else if (hexChar >= 'A' && hexChar <= 'F')
                return hexChar - 'A' + 10;
            else
                throw new ArgumentException($"Invalid hex character: '{hexChar}' (expecting 0..9, a..f, or A..F)");
        }

        /// <summary>
        /// Convert a string of hex characters to a byte array. The hex string may optionally begin with a '0x' prefix.
        /// The method is case-insensitive.
        /// </summary>
        /// <param name="str">String of hex characters to convert</param>
        /// <returns>Returns the converted </returns>
        public static byte[] ParseHexString(string str)
        {
            // Skip 0x prefix, if has one
            bool    has0xPrefix = str.StartsWith("0x", StringComparison.Ordinal) || str.StartsWith("0X", StringComparison.Ordinal);
            int     startNdx    = has0xPrefix ? 2 : 0;

            // Check that number of hex chars is even
            int numChars = str.Length - startNdx;
            if (numChars % 2 != 0)
                throw new ArgumentException("Must have an even number of hex characters");

            // Convert to bytes
            int byteLength = numChars / 2;
            byte[] result = new byte[byteLength];
            for (int ndx = 0; ndx < byteLength; ndx++)
                result[ndx] = (byte)((ParseHexChar(str[startNdx + 2 * ndx]) << 4) + ParseHexChar(str[startNdx + 2 * ndx + 1]));

            return result;
        }

        public static string ShortenString(string str, int maxLength)
        {
            if (str.Length > maxLength)
                return str.Substring(0, maxLength - 3) + "...";
            else
                return str;
        }

        /// <summary>
        /// Clamps string such that the resulting string when encoded with utf8 is at
        /// most <paramref name="maxNumBytes"/> long.
        /// </summary>
        public static string ShortenStringToUtf8ByteLength(string str, int maxNumBytes)
        {
            if (str.Length == 0)
                return str;

            int charNdx = 0;
            int byteNdx = 0;
            for (;;)
            {
                int charStep;
                int cp;
                int byteStep;

                if (char.IsSurrogatePair(str, charNdx))
                {
                    charStep = 2;
                    cp = char.ConvertToUtf32(str[charNdx], str[charNdx+1]);
                }
                else if (char.IsSurrogate(str, charNdx))
                    throw new InvalidOperationException("stray surrogate is illegal and cannot be encoded to utf8");
                else
                {
                    charStep = 1;
                    cp = str[charNdx];
                }

                if (cp <= 0x7F)
                    byteStep = 1;
                else if (cp <= 0x7FF)
                    byteStep = 2;
                else if (cp <= 0xFFFF)
                    byteStep = 3;
                else
                    byteStep = 4;

                byteNdx += byteStep;
                if (byteNdx >= maxNumBytes)
                    return str.Substring(0, charNdx);

                charNdx += charStep;
                if (charNdx >= str.Length)
                    return str;
            }
        }

        /// <summary>
        /// Sanitizes a file path by removing unexpected characters. Allowed characters are alphanumerics and the
        /// special characters `.`, `/`, `\`, `_`, `-` and `%`. All other characters are replaced with the character '?'.
        /// </summary>
        /// <param name="path">Path to sanitize</param>
        /// <returns>Sanitized path</returns>
        public static string SanitizePathForDisplay(string path)
        {
            // Limit processing to a sane length
            if (path.Length > 1024)
                path = path.Substring(0, 1024);

            // Pick only safe characters
            StringBuilder b = new StringBuilder();
            for (int ndx = 0; ndx < path.Length; ++ndx)
            {
                char c = path[ndx];

                if ((c >= 'a' && c <= 'z')
                    || (c >= 'A' && c <= 'Z')
                    || (c >= '0' && c <= '9')
                    || ("./\\_-%".IndexOf(c) != -1))
                {
                    b.Append(c);
                }
                else if (char.IsHighSurrogate(c) && ndx != path.Length - 1 && char.IsLowSurrogate(path[ndx + 1]))
                {
                    ndx++;
                    b.Append('?');
                }
                else
                {
                    b.Append('?');
                }
            }
            return b.ToString();
        }

        static readonly Regex _isBase64Regex = new Regex(@"^\s*[a-zA-Z0-9\+/]*={0,2}\s*$", RegexOptions.Compiled);

        public static bool IsBase64Encoded(string str)
        {
            // Convert.FromBase64String() ignores whitespace
            return (str.Length % 4 == 0) && _isBase64Regex.IsMatch(str);
        }

        public static string StripComments(string code)
        {
            var re = @"(@(?:""[^""]*"")+|""(?:[^""\n\\]+|\\.)*""|'(?:[^'\n\\]+|\\.)*')|//.*|/\*(?s:.*?)\*/";
            return Regex.Replace(code, re, "$1");
        }

        /// <summary>
        /// Get the number of unicode code points in <paramref name="str"/>,
        /// while being "permissive" such that each stray surrogate counts as
        /// one code point instead of being considered an error.
        ///
        /// <para>
        /// Specifically, this is intended to be consistent with the result of
        /// <c>Encoding.UTF8.GetBytes(str)</c>: If <c>GetNumUnicodeCodePointsPermissive(str)</c>
        /// returns N, then <c>Encoding.UTF8.GetBytes(str)</c> returns a utf8 sequence
        /// of bytes that represents N code points.
        /// Note that <c>Encoding.UTF8.GetBytes(str)</c> produces REPLACEMENT CHARACTER (0xFFFD)
        /// on stray surrogates.
        /// </para>
        /// </summary>
        public static int GetNumUnicodeCodePointsPermissive(string str)
        {
            // Number of unicode code points in the string is the same as the number of
            // UTF32 code units in the string, and UTF32 has 4 bytes per code unit.
            return Encoding.UTF32.GetByteCount(str) / 4;
        }

        public static bool ArrayEqual<T>(T[] a, T[] b) where T : IEquatable<T>
        {
            if (a == null && b == null)
                return true;
            else if (a == null || b == null)
                return false;
            else if (a.Length != b.Length)
                return false;
            else
                return a.SequenceEqual(b);
        }

        public static List<T> Repeated<T>(int count, T value)
        {
            List<T> ret = new List<T>(count);
            ret.AddRange(Enumerable.Repeat(value, count));
            return ret;
        }

        public static void ShuffleList<T>(List<T> list, RandomPCG rnd)
        {
            int ndx = list.Count;
            while (ndx > 1)
            {
                ndx--;
                int k = rnd.NextInt(ndx + 1);
                T value = list[k];
                list[k] = list[ndx];
                list[ndx] = value;
            }
        }

        public static int FindMismatchIndex<T>(T[] a, T[] b) where T : IEquatable<T>
        {
            MetaDebug.Assert(a != null && b != null, "Both arrays must be valid");
            MetaDebug.Assert(a.Length == b.Length, "Both arrays must be of same size");

            for (int ndx = 0; ndx < a.Length; ndx++)
            {
                if (!a[ndx].Equals(b[ndx]))
                    return ndx;
            }

            return -1;
        }

        public static T Clamp<T>(T value, T min, T max) where T : IComparable<T>
        {
            if (value.CompareTo(min) <= 0)
                return min;
            else if (value.CompareTo(max) >= 0)
                return max;
            else
                return value;
        }

        public static string ToHexString(byte[] bytes)
        {
            // \todo [petri] use Span for tmp byte buffer
            char[] c = new char[bytes.Length * 2];
            for (int i = 0; i < bytes.Length; ++i)
            {
                int b = bytes[i] >> 4;
                c[i * 2] = (char)(b > 9 ? b + 87 : b + 48);
                b = bytes[i] & 0xF;
                c[i * 2 + 1] = (char)(b > 9 ? b + 87 : b + 48);
            }
            return new string(c);
        }

        public static uint ByteSwap(uint v)
        {
            return (v >> 24) | ((v >> 8) & 0xFF00) | ((v << 8) & 0xFF0000) | (v << 24);
        }

        public static string ComputeSHA1(string str)
        {
            using (SHA1 sha = SHA1.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(str));
                return string.Join("", bytes.Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
            }
        }

        public static string ComputeSHA256(string input)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
                return ToHexString(hash);
            }
        }

        public static string ComputeMD5(string input)
        {
            // \todo [petri] optimize allocations
            using (MD5 md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
                return ToHexString(hash);
            }
        }

        /// <summary>
        /// Returns the current Unix time (seconds since epoch) in UTC timezone.
        /// Returns a 64-bit value to avoid the year 2038 problem (signed 32-bit will overflow in 2038).
        /// </summary>
        /// <returns>Unix timestamp, in seconds</returns>
        public static long GetUtcUnixTimeSeconds()
        {
            return (long)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1, 0, 0, 0)).TotalSeconds;
        }

        /// <summary>
        /// Convert a 64-bit ulong value to bytes in big-endian format.
        /// </summary>
        /// <param name="value">Value to convert to bytes</param>
        /// <returns>Big-endian ordered bytes</returns>
        public static byte[] GetBigEndianBytes(ulong value)
        {
            var buffer = new byte[8];
            for (int i = 0; i < 8; i++)
            {
                buffer[7 - i] = unchecked((byte)(value & 0xff));
                value = value >> 8;
            }
            return buffer;
        }

        public static byte[] ConcatBytes(params byte[][] inputs)
        {
            byte[] result = new byte[inputs.Sum(b => b.Length)];

            int offset = 0;
            foreach (byte[] b in inputs)
            {
                Buffer.BlockCopy(b, 0, result, offset, b.Length);
                offset += b.Length;
            }

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int CombineHashCode(int h0, int h1) => h0 * 1797 + h1;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int CombineHashCode(int h0, int h1, int h2) => (h0 * 1797 + h1) * 7919 + h2;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int CombineHashCode(int h0, int h1, int h2, int h3) => ((h0 * 1797 + h1) * 7919 + h2) * 13 + h3;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int CombineHashCode(int h0, int h1, int h2, int h3, int h4) => (((h0 * 1797 + h1) * 7919 + h2) * 13 + h3) * 2137 + h4;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int CombineHashCode(int h0, int h1, int h2, int h3, int h4, int h5) => ((((h0 * 1797 + h1) * 7919 + h2) * 13 + h3) * 2137 + h4) * 4003 + h5;

        /// <summary>
        /// Tests if given signed integer is a power of two. Notably <c>IsPowerOfTwo(0) = false</c>.
        /// </summary>
        public static bool IsPowerOfTwo(int value)
        {
            if (value < 0)
                return false;
            else if (value == 0)
                return false;
            else
                return (value & (value - 1)) == 0;
        }

        /// <summary>
        /// Returns the smallest integer that is a power of two and that is greater or equal to
        /// the given non-negative <paramref name="value"/>. Notably <c>CeilToPowerOfTwo(0) = 1</c>.
        /// <para>
        /// If the smallest power of two is not representable as Int32, OverflowException is thrown.
        /// If the supplied <paramref name="value"/> is negative, <c>ArgumentOutOfRangeException</c> is thrown.
        /// </para>
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">given value is negative</exception>
        /// <exception cref="OverflowException">return value is not representable</exception>
        public static int CeilToPowerOfTwo(int value)
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value));
            if (value == 0)
                return 1;

            // already POT?
            if ((value & (value - 1)) == 0)
                return value;

            // double and Floor to POT
            int tofloor = value << 1;
            if (tofloor < 0)
                throw new OverflowException();

            while (true)
            {
                tofloor = tofloor & (tofloor - 1);
                if ((tofloor & (tofloor - 1)) == 0)
                    return tofloor;
            }
        }

        /// <summary>
        /// Try to step <paramref name="offset"/> forward in <paramref name="str"/> by one codepoint.
        /// Returns whether the offset was advanced (or false at end-of-string). Offset is incremented
        /// by 1 for non-surrogate (and stray surrogate) characters, and 2 for surrogate pairs.
        /// </summary>
        /// <param name="str">String to step forward in</param>
        /// <param name="offset">Current char offset into the UTF-16 string</param>
        /// <returns>true if stepped forward, false if at end.</returns>
        public static bool TryStepCodepoint(string str, ref int offset)
        {
            // If at end-of-string, return false
            if (offset == str.Length)
                return false;

            // For surrogate pairs, skip 2 characters, otherwise skip 1 character
            bool isSurrogatepair = char.IsSurrogatePair(str, offset);
            offset += isSurrogatepair ? 2 : 1;
            return true;
        }

        /// <summary>
        /// Split an input string into SQL-searchable suffix strings. For example, the string "abcde" gets split into
        /// [ 'abcde', 'bcde', 'cde', 'de', 'e' ]. Further limitations for the number of suffixes and length of the
        /// suffix can be given (in number of codepoints). The suffixes can then be inserted into an indexed column
        /// and searched with an efficient 'LIKE "searchText%"' query.
        /// </summary>
        /// <param name="str">Input string to generate search suffixes for</param>
        /// <param name="maxSuffixes">Maximum number of suffixes to generate</param>
        /// <param name="minLength">Minimum length of each suffix (in codepoints)</param>
        /// <param name="maxLength">Maximum length of each suffix (in codepoints)</param>
        /// <returns></returns>
        public static List<string> ComputeStringSearchSuffixes(string str, int maxSuffixes, int minLength, int maxLength)
        {
            if (str == null)
                throw new ArgumentNullException(nameof(str));
            if (maxSuffixes <= 0)
                throw new ArgumentException("maxSuffixes must be positive", nameof(maxSuffixes));
            if (minLength < 1)
                throw new ArgumentException("minLength must be at least 1", nameof(minLength));
            if (maxLength < minLength)
                throw new ArgumentException("maxLength must not be smaller than minLength", nameof(maxLength));

            // Find endOffset (of current substring) by walking up to maxLength codepoints, and compute codepoint-length of suffix
            int endOffset = 0;
            int suffixCodepoints = 0;
            for (int ndx = 0; ndx < maxLength; ndx++)
            {
                if (TryStepCodepoint(str, ref endOffset))
                    suffixCodepoints++;
                else
                    break;
            }

            // Walk string codepoint-by-codepoint and return each substring (until at end)
            List<string> suffixes = new List<string>();
            int startOffset = 0;
            for (; ;)
            {
                // If suffix length below minimum, we're done
                if (suffixCodepoints < minLength)
                    break;

                // If has maximum suffixes, we're done
                if (suffixes.Count >= maxSuffixes)
                    break;

                // Append current suffix
                suffixes.Add(str.Substring(startOffset, endOffset - startOffset));

                // Step start and end forward by one codepoint (and update suffix length if at end)
                _ = TryStepCodepoint(str, ref startOffset);
                if (!TryStepCodepoint(str, ref endOffset))
                    suffixCodepoints -= 1;
            }

            return suffixes;
        }

        public static int StringLengthCodepoints(string str)
        {
            int offset = 0;
            int numCodepoints = 0;
            while (TryStepCodepoint(str, ref offset))
                numCodepoints++;
            return numCodepoints;
        }

        public static string ClampStringToLengthCodepoints(string str, int maxCodepoints)
        {
            if (str.Length <= maxCodepoints)
                return str;

            int offset = 0;
            int numCodepoints = 0;
            while (offset < str.Length)
            {
                if (TryStepCodepoint(str, ref offset))
                    numCodepoints++;
                else
                    break;

                if (numCodepoints >= maxCodepoints)
                    break;
            }

            return str.Substring(0, offset);
        }

        /// <summary>
        /// Convert <paramref name="obj"/> to a string, with best-effort to
        /// format it according to <see cref="CultureInfo.InvariantCulture"/>.
        ///
        /// <para>
        /// Specifically, this tests if <paramref name="obj"/> implements
        /// <see cref="IFormattable"/>, and if so, uses <see cref="IFormattable.ToString(string?, IFormatProvider?)"/>
        /// with an explicit <see cref="CultureInfo.InvariantCulture"/>.
        /// Otherwise, <see cref="object.ToString"/> is used.
        /// </para>
        /// </summary>
        public static string ObjectToStringInvariant(object obj)
        {
            if (obj is IFormattable formattable)
                return formattable.ToString(null, CultureInfo.InvariantCulture);
            else
            {
                // \note Disabling warning for MP_STR_02: "Default-formatted ToString()".
                //       This method is the "official" fix for that warning.
                #pragma warning disable MP_STR_02
                return obj.ToString();
                #pragma warning restore MP_STR_02
            }
        }

        public static T Min<T>(T a, T b) where T : IComparable<T>           => a.CompareTo(b) <= 0 ? a : b;
        public static T Min<T>(T a, T b, T c) where T : IComparable<T>      => Min(a, Min(b, c));
        public static T Min<T>(T a, T b, T c, T d) where T : IComparable<T> => Min(a, Min(b, c, d));

        public static T Max<T>(T a, T b) where T : IComparable<T>           => a.CompareTo(b) > 0 ? a : b;
        public static T Max<T>(T a, T b, T c) where T : IComparable<T>      => Max(a, Max(b, c));
        public static T Max<T>(T a, T b, T c, T d) where T : IComparable<T> => Max(a, Max(b, c, d));

        /// <summary>
        /// Among the elements in <paramref name="source"/>, find different elements
        /// for which <paramref name="getProperty"/> returns the same value.
        /// Calls <paramref name="onDuplicateFound"/> if a duplicate is found,
        /// passing as arguments the two different elements and the shared property
        /// value.
        /// <para>
        /// If multiple pairs of duplicates exist, this does necessarily not report
        /// all such pairs, but rather, for each distinct property value, all pairs
        /// where one element is the first source element with that property value.
        /// But that does not matter in the typical use case where duplicates are
        /// considered errors and where thus <paramref name="onDuplicateFound"/>
        /// throws an exception.
        /// </para>
        /// </summary>
        /// <remarks>
        /// This uses a dictionary to find duplicates, so <typeparamref name="TProperty"/>
        /// needs to be properly hashable and equatable.
        /// </remarks>
        public static void CheckPropertyDuplicates<TElement, TProperty>(
            IEnumerable<TElement> source,
            Func<TElement, TProperty> getProperty,
            Action<TElement, TElement, TProperty> onDuplicateFound)
        {
            Dictionary<TProperty, TElement> propertyToElement = new Dictionary<TProperty, TElement>();

            foreach (TElement element in source)
            {
                TProperty property = getProperty(element);
                if (propertyToElement.TryGetValue(property, out TElement otherElement))
                    onDuplicateFound(otherElement, element, property);
                else
                    propertyToElement.Add(property, element);
            }
        }

        /// <summary>
        /// Compute the set of nodes reachable in a directed graph.
        /// </summary>
        /// <param name="startNodes">
        /// Starting nodes.
        /// </param>
        /// <param name="tryGetNodeNeighbors">
        /// Represents the edges of the graph:
        /// maps a node to its neighbors reachable from the node by an edge.
        /// </param>
        /// <returns>
        /// Set of reachable nodes, including the starting nodes.
        /// </returns>
        /// <remarks>
        /// <typeparamref name="TNode"/> must be hashable and equality-comparable
        /// (to work in a hash set).
        /// </remarks>
        public static OrderedSet<TNode> ComputeReachableNodes<TNode>(IEnumerable<TNode> startNodes, Func<TNode, IEnumerable<TNode>> tryGetNodeNeighbors)
        {
            // Breadth-first search

            OrderedSet<TNode> reachable = new OrderedSet<TNode>(startNodes);
            Queue<TNode> queue = new Queue<TNode>(startNodes);

            while (queue.TryDequeue(out TNode current))
            {
                IEnumerable<TNode> neighbors = tryGetNodeNeighbors(current);
                if (neighbors != null)
                {
                    foreach (TNode neighbor in neighbors)
                    {
                        bool newlySeen = reachable.Add(neighbor);
                        if (newlySeen)
                            queue.Enqueue(neighbor);
                    }
                }
            }

            return reachable;
        }

        /// <summary>
        /// Helper for <see cref="ComputeReachableNodes{TNode}(IEnumerable{TNode}, Func{TNode, IEnumerable{TNode}})"/>
        /// for when the edges are provided by a dictionary instead of a function.
        /// </summary>
        public static OrderedSet<TNode> ComputeReachableNodes<TNode>(IEnumerable<TNode> startNodes, IReadOnlyDictionary<TNode, OrderedSet<TNode>> nodeNeighbors)
        {
            return ComputeReachableNodes(
                startNodes: startNodes,
                tryGetNodeNeighbors: (TNode node) =>
                {
                    if (nodeNeighbors.TryGetValue(node, out OrderedSet<TNode> neighbors))
                        return neighbors;
                    else
                        return null;
                });
        }
    }

    public static class EnumUtil
    {
        /// <summary>
        /// Get all valid values for an enumeration.
        /// </summary>
        /// <typeparam name="TEnum">Type of enumeration</typeparam>
        /// <returns>Enumerable collection of all valid values for enumerable</returns>
        public static IEnumerable<TEnum> GetValues<TEnum>() => Enum.GetValues(typeof(TEnum)).Cast<TEnum>();

        public static T Parse<T>(string str) where T : Enum
        {
            return (T)Enum.Parse(typeof(T), str);
        }

        public static T ParseCaseInsensitive<T>(string str) where T : Enum
        {
            return (T)Enum.Parse(typeof(T), str, ignoreCase: true);
        }

        /// <summary>
        /// Convert a generic enumeration to 32-bit integer.
        /// </summary>
        /// <typeparam name="TEnum">Type of enumeration to convert</typeparam>
        /// <param name="e">Enum value to convert</param>
        /// <returns></returns>
        public static int ToInt<TEnum>(TEnum e) where TEnum : Enum
        {
#if NETCOREAPP
            return Unsafe.As<TEnum, int>(ref e);
#else
            return (int)(object)e;
#endif
        }

        /// <summary>
        /// Convert a 32-bit integer into a generic enumeration type.
        /// </summary>
        /// <typeparam name="TEnum">Type of enumeration</typeparam>
        /// <param name="value">Converted enum value</param>
        /// <returns></returns>
        public static TEnum FromInt<TEnum>(int value) where TEnum : Enum
        {
#if NETCOREAPP
            return Unsafe.As<int, TEnum>(ref value);
#else
            return (TEnum)(object)value;
#endif
        }
    }

    /// <summary>
    /// Caching enum-to-string converter, to avoid allocations.
    /// </summary>
    internal static class EnumToStringCache<T> where T : Enum
    {
#if UNITY_WEBGL_BUILD
        static readonly WebConcurrentDictionary<T, string> s_cache = new WebConcurrentDictionary<T, string>();
#else
#pragma warning disable MP_WGL_00 // "Feature is poorly supported in WebGL". False positive, this is non-WebGL.
        static readonly ConcurrentDictionary<T, string> s_cache = new ConcurrentDictionary<T, string>();
#pragma warning restore MP_WGL_00
#endif

        public static string ToString(T value) => s_cache.GetOrAdd(value, v => v.ToString());
    }

    public static class TaskUtil
    {
        /// <summary>
        /// Attempts to execute the given async function until it successfully completes or <paramref name="maxNumRetries"/> is reached.
        /// If <paramref name="maxNumRetries"/> is reached, throws the last failure.
        /// If <paramref name="fn"/> is cancelled, it is not retried.
        /// </summary>
        public static async Task<TResult> RetryUntilSuccessAsync<TResult>(int maxNumRetries, Func<Task<TResult>> fn)
        {
            int retryNdx = 0;
            for (;;)
            {
                try
                {
                    return await fn().ConfigureAwaitFalse();
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    retryNdx++;
                    if (retryNdx > maxNumRetries)
                        throw;
                }
            }
        }
    }

    public static class NullableUtil
    {
        /// <summary>
        /// Return the minimum of the non-null arguments,
        /// or null if all of the arguments are null.
        /// </summary>
        public static int? Min(int? a, int? b)
        {
            if (!a.HasValue)
                return b;
            if (!b.HasValue)
                return a;
            return System.Math.Min(a.Value, b.Value);
        }

        /// <inheritdoc cref="Min(int?, int?)"/>
        public static int? Min(int? a, int? b, int? c)
            => Min(a, Min(b, c));
    }
}
