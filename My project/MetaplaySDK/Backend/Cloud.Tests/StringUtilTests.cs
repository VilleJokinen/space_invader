// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using NUnit.Framework;
using System.Collections.Generic;

namespace Cloud.Tests
{
    class StringUtilTests
    {
        [TestCase("", new string[] { })]
        [TestCase("abba", new string[] { "abba", "bba", "ba", "a" })]
        [TestCase("asd_", new string[] { "asd_", "sd_", "d_", "_" })]
        [TestCase("あ", new string[] { "あ" })]
        [TestCase("ああ", new string[] { "ああ", "あ" })]
        [TestCase("🤔", new string[] { "🤔" })]
        [TestCase("あ%🤔", new string[] { "あ%🤔", "%🤔", "🤔" })]
        public void TestStringSearchSuffixes(string input, string[] expected)
        {
            List<string> suffixes = Util.ComputeStringSearchSuffixes(input, 100, 1, 100);
            Assert.AreEqual(expected, suffixes.ToArray());
        }

        [TestCase("abcde", 16, 3, 16, new string[] { "abcde", "bcde", "cde" })]
        [TestCase("abcde", 16, 2, 3, new string[] { "abc", "bcd", "cde", "de" })]
        [TestCase("abcde", 1, 1, 16, new string[] { "abcde" })]
        [TestCase("abcde", 2, 1, 16, new string[] { "abcde", "bcde" })]
        [TestCase("abcde", 1, 2, 3, new string[] { "abc" })]
        [TestCase("🌀🏯🤔", 2, 1, 2, new string[] { "🌀🏯", "🏯🤔" })]
        public void TestStringSearchSuffixLimits(string input, int maxSuffixes, int minLength, int maxLength, string[] expected)
        {
            List<string> suffixes = Util.ComputeStringSearchSuffixes(input, maxSuffixes, minLength, maxLength);
            Assert.AreEqual(expected, suffixes.ToArray());
        }
    }
}
