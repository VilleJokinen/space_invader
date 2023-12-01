// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud;
using Metaplay.Cloud.Utility;
using NUnit.Framework;
using System.Security.Cryptography;
using System.Text;

namespace Cloud.Tests
{
    class MiniMD5Tests
    {
        static uint ReferenceMiniMD5(string value)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(value));
                return ((uint)hash[0] << 24) + ((uint)hash[1] << 16) + ((uint)hash[2] << 8) + (uint)hash[3];
            }
        }

        [TestCase("")]
        [TestCase("a")]
        [TestCase("foo")]
        [TestCase("0, 255")]
        [TestCase("Player:987159873")]
        [TestCase("äöäåöäåöäåö")]
        [TestCase("01234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789")]
        public void BasicTest(string value)
        {
            uint expected = ReferenceMiniMD5(value);
            uint result = MiniMD5.ComputeMiniMD5(value);
            Assert.AreEqual(expected, result);
        }

        [Test]
        public void TestLengths()
        {
            const int MaxLen = 1024;
            for (int len = 0; len < MaxLen; len++)
            {
                string value = new string('a', len);
                uint expected = ReferenceMiniMD5(value);
                uint result = MiniMD5.ComputeMiniMD5(value);
                Assert.AreEqual(expected, result);
            }
        }

        [Test]
        public void TestCharacters()
        {
            const int MaxChar = 16384;
            for (int ch = 0; ch < MaxChar; ch++)
            {
                string value = new string((char)ch, 16);
                uint expected = ReferenceMiniMD5(value);
                uint result = MiniMD5.ComputeMiniMD5(value);
                Assert.AreEqual(expected, result);
            }
        }
    }
}
