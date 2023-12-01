// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Persistence;
using NUnit.Framework;

namespace Cloud.Tests
{
    class SearchUtilTests
    {
        [TestCase("", new string[] { })]
        [TestCase("  ", new string[] { })]
        [TestCase("hello", new string[] { "hello" })]
        [TestCase(" hello ", new string[] { "hello" })] // trim
        [TestCase("hel*lo", new string[] { "hel*lo", "hello" })] // with and without special
        [TestCase("hel🤔lo", new string[] { "hel🤔lo", "hello" })] // with and without special, unicode
        [TestCase("hello world", new string[] { "hello world", "world" })] // two words
        [TestCase("hel🤔lo world🤔", new string[] { "hel🤔lo world🤔", "hello world", "world🤔", "world" })] // two words, special
        [TestCase("\t\thello \n\t world\t\n\r", new string[] { "hello world", "world" })] // whitespaces
        [TestCase(" hello world ", new string[] { "hello world", "world" })] // unicode whitespaces
        [TestCase("abcd abba cd", new string[] { "abcd abba cd", "abba cd", "cd" })] // three words
        public void TestComputeSearchPartsFromName(string input, string[] expected)
        {
            string[] result = SearchUtil.ComputeSearchablePartsFromName(input, minLengthCodepoints: 1, maxLengthCodepoints: 16, maxParts: 20).ToArray();
            Assert.AreEqual(expected, result);
        }

        [TestCase("hello", 1, 3, 20, new string[] { "hel" })] // cap length
        [TestCase("hello world", 1, 3, 20, new string[] { "hel", "wor" })] // cap length
        [TestCase("abcd abba cd", 3, 16, 20, new string[] { "abcd abba cd", "abba cd" })] // filter short strings
        [TestCase("abcd abba cd", 1, 16, 2, new string[] { "abcd abba cd", "abba cd" })] // cap entries
        [TestCase("abcd abba cd", 1, 5, 2, new string[] { "abcd", "abba" })] // trim whitespace
        public void TestComputeSearchPartsFromNameLimits(string input, int minLength, int maxLength, int maxParts, string[] expected)
        {
            string[] result = SearchUtil.ComputeSearchablePartsFromName(input, minLength, maxLength, maxParts).ToArray();
            Assert.AreEqual(expected, result);
        }

        [TestCase("abcd abcd abcd", 1, 4, 20, new string[] { "abcd" })] // all parts should be "abcd"
        [TestCase("abcd abcd abcd", 1, 5, 20, new string[] { "abcd" })] // all parts should be "abcd" after trimming
        public void TestComputeSearchPartsFromNameDuplicates(string input, int minLength, int maxLength, int maxParts, string[] expected)
        {
            string[] result = SearchUtil.ComputeSearchablePartsFromName(input, minLength, maxLength, maxParts).ToArray();
            Assert.AreEqual(expected, result);
        }

        [TestCase(null, new string[] { })]
        [TestCase("", new string[] { })]
        [TestCase("  ", new string[] { })]
        [TestCase("hello", new string[] { "hello" })]
        [TestCase(" hello ", new string[] { "hello" })] // trim
        [TestCase("hel*lo", new string[] { "hel*lo", "hello" })] // with and without special
        [TestCase("hel🤔lo", new string[] { "hel🤔lo", "hello" })] // with and without special, unicode
        [TestCase("hello world", new string[] { "hello world", "hello", "world" })] // two words
        [TestCase("hel🤔lo world🤔", new string[] { "hel🤔lo world🤔", "hello world", "hel🤔lo", "world🤔", "hello", "world" })] // two words, special
        [TestCase("\t\thello \n\t world\t\n\r", new string[] { "hello world", "hello", "world" })] // whitespaces
        [TestCase(" hello world ", new string[] { "hello world", "hello", "world" })] // unicode whitespaces
        [TestCase("abcd abba cd", new string[] { "abcd abba cd", "abba cd", "abcd", "abba", "cd" })] // three words
        public void TestComputeSearchStringsForQuery(string input, string[] expected)
        {
            string[] result = SearchUtil.ComputeSearchStringsForQuery(input, maxLengthCodepoints: 16, limit: 20).ToArray();
            Assert.AreEqual(expected, result);
        }
    }
}
