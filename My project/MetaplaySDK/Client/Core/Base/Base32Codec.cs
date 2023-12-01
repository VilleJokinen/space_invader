// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;

namespace Metaplay.Core
{
    /// <summary>
    /// Implements RFC 4648 Base32 encoding and decoding.
    /// </summary>
    public static class Base32Codec
    {
        public static readonly string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        /// <summary>
        /// Decodes the input string from base32 into bytes. If format is invalid, returns null.
        /// </summary>
        public static byte[] TryDecodeToBytes(string encodedString, bool padding)
        {
            if (encodedString == null)
                throw new ArgumentNullException(nameof(encodedString));

            int charCursor = 0;
            int charCursorEnd;

            if (padding)
            {
                if ((encodedString.Length % 8) != 0)
                    return null;

                charCursorEnd = encodedString.Length;

                for (int padLen = 1; padLen <= 6; ++padLen)
                {
                    int ndx = charCursorEnd - 1;
                    if (ndx < 0)
                        break;
                    if (encodedString[ndx] != '=')
                        break;
                    charCursorEnd = ndx;
                }
            }
            else
                charCursorEnd = encodedString.Length;

            ushort pool = 0;
            int poolSize = 0;
            byte[] bytes = new byte[((charCursorEnd - charCursor) * 5) / 8]; // Non-padding quintets to bits to octests. Rounded down, the overflow bits are 0s.
            int bytesCursor = 0;

            for (;;)
            {
                // read to pool
                do
                {
                    if (charCursor >= charCursorEnd)
                        goto eof;
                    char sample = encodedString[charCursor];
                    charCursor++;

                    int converted = Alphabet.IndexOf(sample);
                    if (converted == -1)
                        return null;
                    pool = (ushort)((pool << 5) | converted);
                    poolSize += 5;
                } while (poolSize < 8);

                // write to data
                bytes[bytesCursor] = (byte)(pool >> (poolSize - 8));
                poolSize -= 8;
                bytesCursor++;
            }

        eof:
            // partial quintet bits must be 0
            if ((pool & ((1 << poolSize) - 1)) != 0)
                return null;

            return bytes;
        }

        /// <summary>
        /// Encodes the input byte into base32 string.
        /// </summary>
        public static string EncodeToString(byte[] bytes, bool padding)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));

            int byteCursor = 0;
            int byteCursorEnd = bytes.Length;
            ushort pool = 0;
            int poolSize = 0;
            int numInputBytes = byteCursorEnd - byteCursor;
            int numInputBits = numInputBytes * 8;
            int numInputQuintets = (numInputBits + 4) / 5; // round up
            int numOutputChars = (numInputQuintets + 7) / 8 * 8; // round up to next multiple of 8 (base32 works in 8-char blocks, each quintet takes one char)
            char[] chars = new char[numOutputChars];
            int charsCursor = 0;

            for (;;)
            {
                // read to pool
                if (byteCursor >= byteCursorEnd)
                    break;
                pool = (ushort)((pool << 8) | bytes[byteCursor]);
                poolSize += 8;
                byteCursor++;

                // consume from pool
                do
                {
                    byte sample = (byte)((pool >> (poolSize - 5)) & 31);
                    poolSize -= 5;
                    chars[charsCursor++] = Alphabet[sample];
                } while(poolSize >= 5);
            }

            // flush
            if (poolSize != 0)
            {
                byte sample = (byte)((pool << (5 - poolSize)) & 31);
                chars[charsCursor++] = Alphabet[sample];
            }

            while (padding && (charsCursor % 8) != 0)
                chars[charsCursor++] = '=';

            return new string(chars, 0, charsCursor);
        }
    }
}
