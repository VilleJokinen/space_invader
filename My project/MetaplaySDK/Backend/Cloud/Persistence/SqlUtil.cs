// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Text;

namespace Metaplay.Cloud.Persistence
{
    public static class SqlUtil
    {
        /// <summary>
        /// Returns a LIKE query that will match only and exactly the given <paramref name="exactMatch"/> string.
        /// </summary>
        /// <remarks>
        /// The returned string is properly escaped for LIKE argument, but IS NOT ESCAPED FOR TO BE A LITERAL CONSTANT.
        /// DO NOT FORMAT THIS INTO A QUERY. YOU MUST USE PARAMETERIZED QUERY FOR THE LIKE ARGUMENT.
        /// </remarks>
        public static string EscapeSqlLike(string exactMatch, char escapeChar)
        {
            if (escapeChar >= 128)
                throw new ArgumentException("escape char must be ASCII", nameof(escapeChar));

            // \note: If SQL Server, need to escape [ as well

            ReadOnlySpan<char>  exactMatchSpan  = exactMatch.AsSpan();
            ReadOnlySpan<char>  dangerousChars  = stackalloc [] { '%', '_', escapeChar };
            int                 firstDangerous;

            firstDangerous = exactMatchSpan.IndexOfAny(dangerousChars);
            if (firstDangerous == -1)
                return exactMatch;

            StringBuilder       builder;
            ReadOnlySpan<char>  remainingSpan;

            builder = new StringBuilder();
            builder.Append(exactMatchSpan[0..firstDangerous]);
            remainingSpan = exactMatchSpan[firstDangerous..];

            for (;;)
            {
                // remainingSpan[0] points to a char that needs escaping
                builder.Append(escapeChar);
                builder.Append(remainingSpan[0]);
                remainingSpan = remainingSpan[1..];

                // copy up to the next to be escaped
                int runLength = remainingSpan.IndexOfAny(dangerousChars);
                if (runLength == -1)
                    break;

                builder.Append(remainingSpan[0..runLength]);
                remainingSpan = remainingSpan[runLength..];
            }

            builder.Append(remainingSpan);
            return builder.ToString();
        }

        /// <summary>
        /// Returns a Like query that matches all strings containing <paramref name="substringToSearch"/> as a substring.
        /// </summary>
        /// <remarks>
        /// The returned string is properly escaped for LIKE argument, but IS NOT ESCAPED FOR TO BE A LITERAL CONSTANT.
        /// DO NOT FORMAT THIS INTO A QUERY. YOU MUST USE PARAMETERIZED QUERY FOR THE LIKE ARGUMENT.
        /// </remarks>
        public static string LikeQueryForSubstringSearch(string substringToSearch, char escapeChar)
        {
             return $"%{EscapeSqlLike(substringToSearch, escapeChar)}%";
        }

        /// <summary>
        /// Returns a Like query that matches all strings starting with the prefix <paramref name="prefixToSearch"/>.
        /// </summary>
        /// <remarks>
        /// The returned string is properly escaped for LIKE argument, but IS NOT ESCAPED FOR TO BE A LITERAL CONSTANT.
        /// DO NOT FORMAT THIS INTO A QUERY. YOU MUST USE PARAMETERIZED QUERY FOR THE LIKE ARGUMENT.
        /// </remarks>
        public static string LikeQueryForPrefixSearch(string prefixToSearch, char escapeChar)
        {
            return $"{EscapeSqlLike(prefixToSearch, escapeChar)}%";
        }
    }
}
