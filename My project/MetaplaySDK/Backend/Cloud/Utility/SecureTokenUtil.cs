// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Security.Cryptography;
using System.Text;

namespace Metaplay.Cloud.Utility
{
    public static class SecureTokenUtil
    {
        // \note Any changes to ValidChars must be reflected in IsValidChar().
        public static readonly string ValidChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

        public static bool IsValidChar(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
        }

        public static bool IsValidToken(string claimedTokenId, int expectedLength)
        {
            if (claimedTokenId == null || claimedTokenId.Length != expectedLength)
                return false;

            for (int ndx = 0; ndx < claimedTokenId.Length; ndx++)
                if (!IsValidChar(claimedTokenId[ndx]))
                    return false;

            return true;
        }

        /// <summary>
        /// Generate a random alphanumeric string of length <paramref name="length"/> using cryptographic random source.
        /// </summary>
        /// <param name="rnd">Random number generator to use</param>
        /// <param name="length">length of the generated token</param>
        /// <returns>Generated random string</returns>
        public static string GenerateRandomStringToken(int length)
        {
            byte[] bytes = RandomNumberGenerator.GetBytes(4 * length);

            char[] str = new char[length];
            for (int ndx = 0; ndx < length; ndx++)
                str[ndx] = ValidChars[(int)(BitConverter.ToUInt32(bytes, 4 * ndx) % ValidChars.Length)];
            return new string(str);
        }

        /// <summary>
        /// Generate a random alphanumeric string of length <paramref name="length"/>.
        /// Warning: this uses the system random, which is not cryptographically safe! Do not use in production!
        /// </summary>
        /// <param name="rnd">Random number generator to use</param>
        /// <param name="length">length of the generated token</param>
        /// <returns>Generated random string</returns>
        public static string GenerateRandomStringTokenUnsafe(Random rnd, int length)
        {
            char[] str = new char[length];
            for (int ndx = 0; ndx < length; ndx++)
                str[ndx] = ValidChars[rnd.Next(ValidChars.Length)];
            return new string(str);
        }
    }
}
