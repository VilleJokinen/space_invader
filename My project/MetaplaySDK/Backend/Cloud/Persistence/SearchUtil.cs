// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Metaplay.Cloud.Persistence
{
    public static class SearchUtil
    {
        /// <summary>
        /// Check if character is "special" for the purposes of name searching. All names are
        /// searchable with and without the special characters, meaning that names containing
        /// emojis and similar can be found (more accurately) by including said special character
        /// in the search string, but they can also be found by only including the non-special
        /// characters.
        /// </summary>
        /// <param name="codepoint">Unicode codepoint to test whether it's special</param>
        /// <returns></returns>
        static bool IsSpecialCharacter(int codepoint)
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(codepoint);
            return (category != UnicodeCategory.UppercaseLetter)
                && (category != UnicodeCategory.LowercaseLetter)
                && (category != UnicodeCategory.TitlecaseLetter)
                && (category != UnicodeCategory.OtherLetter)
                && (category != UnicodeCategory.DecimalDigitNumber)
                && (category != UnicodeCategory.LetterNumber)
                && (category != UnicodeCategory.OtherNumber)
                && (category != UnicodeCategory.OpenPunctuation)
                && (category != UnicodeCategory.ClosePunctuation)
                && (category != UnicodeCategory.SpaceSeparator)
                && (category != UnicodeCategory.LineSeparator)
                && (category != UnicodeCategory.ParagraphSeparator);
        }

        /// <summary>
        /// Filter out any "special" characters from a string.
        /// </summary>
        /// <param name="src"></param>
        /// <returns></returns>
        static string FilterSpecialCharacters(string src)
        {
            char[] dst = new char[src.Length];

            int srcOffset = 0;
            int dstOffset = 0;
            while (srcOffset < src.Length)
            {
                bool isSurrogatePair = char.IsSurrogatePair(src, srcOffset);
                int cp = char.ConvertToUtf32(src, srcOffset);

                // Copy all non-special characters to destination
                if (!IsSpecialCharacter(cp))
                {
                    dst[dstOffset++] = src[srcOffset];
                    if (isSurrogatePair)
                        dst[dstOffset++] = src[srcOffset + 1];
                }

                srcOffset += isSurrogatePair ? 2 : 1;
            }

            return new string(dst, 0, dstOffset);
        }

        static string[] SplitToWords(string str)
        {
            // Split to words on whitespace boundaries (understands unicode whitespaces)
            return str.Split(Array.Empty<char>(), StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        static List<string> ComputeWordSuffixes(string str)
        {
            // Simplify all whitespace such that words are separated by single space
            string simplified = string.Join(' ', SplitToWords(str));

            // Find all substrings which start at a word boundary (beginning of string, or after a space)
            List<string> result = new List<string>() { simplified };
            for (int ndx = 0; ndx < simplified.Length; ndx++)
            {
                // Add whole suffix starting after each whitespace
                if (simplified[ndx] == ' ')
                    result.Add(simplified.Substring(ndx + 1));
            }
            return result;
        }

        /// <summary>
        /// Return a list of parts based on the input name that should be persisted in the database search table, and used when
        /// searching the database.
        /// The process for computing is as follows:
        /// - Use original input and version of string where all special characters have been removed
        /// - Trim input & canonize multiple whitespace to a single space character
        /// - Compute suffixes of the inputs, from the start of each word to the end of the input
        /// - Order inputs by length, from longest to shortest
        /// - Clamp all inputs to <paramref name="maxLengthCodepoints"/> codepoints (and remove trim trailing spaces)
        /// - Filter out any words that are shorter than <paramref name="minLengthCodepoints"/> codepoints
        /// - Only take the first <paramref name="maxParts"/> parts
        /// </summary>
        /// <param name="str"></param>
        /// <param name="minLengthCodepoints"></param>
        /// <param name="maxLengthCodepoints"></param>
        /// <param name="maxParts"></param>
        /// <returns></returns>
        public static List<string> ComputeSearchablePartsFromName(string str, int minLengthCodepoints, int maxLengthCodepoints, int maxParts)
        {
            // Compute all word-suffixes for a) original string, and b) simplified string with special characters removed
            IEnumerable<string> suffixes = ComputeWordSuffixes(str).Concat(ComputeWordSuffixes(FilterSpecialCharacters(str)));

            // Resolve unique results, clamped to maxLength, at least minLength long, ordered longest-to-shortest, and cap to maxWords
            return suffixes
                .OrderByDescending(word => word.Length)
                .Select(word => Util.ClampStringToLengthCodepoints(word, maxLengthCodepoints).TrimEnd())
                .Where(word => Util.StringLengthCodepoints(word) >= minLengthCodepoints)
                .Distinct()
                .Take(maxParts)
                .ToList();
        }

        /// <summary>
        /// Compute a list of search strings to query from the database when searching Players/Guilds by name.
        /// Includes both the results from <see cref="ComputeSearchablePartsFromName(string, int, int, int)"/>
        /// as well as all individual words.
        /// </summary>
        /// <param name="query"></param>
        /// <param name="maxLengthCodepoints"></param>
        /// <param name="limit"></param>
        /// <returns></returns>
        public static List<string> ComputeSearchStringsForQuery(string query, int maxLengthCodepoints, int limit)
        {
            if (string.IsNullOrEmpty(query))
                return new List<string>();

            // Get all words in the input string (with and without special characters)
            IEnumerable<string> words = SplitToWords(query).Concat(SplitToWords(FilterSpecialCharacters(query)));

            // Compute all searchable parts (as inserted into database)
            List<string> searchableParts = ComputeSearchablePartsFromName(query, minLengthCodepoints: 1, maxLengthCodepoints, maxParts: limit);

            // From the combination of the above, resolve all unique entries, fulfilling the conditions, in longest-to-shortest order
            // Concat words first, so we get the individual words in original order
            return words.Concat(searchableParts)
                .OrderByDescending(word => word.Length)
                .Select(word => Util.ClampStringToLengthCodepoints(word, maxLengthCodepoints).TrimEnd())
                .Distinct()
                .Take(limit)
                .ToList();
        }
    }
}
