// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Persistence;
using NUnit.Framework;

namespace Cloud.Tests
{
    class SqlUtilTests
    {
        [TestCase("", "")]
        [TestCase("abba", "abba")]
        [TestCase("asd_", "asd/_")]
        [TestCase("%a_sd_", "/%a/_sd/_")]
        [TestCase("%%", "/%/%")]
        [TestCase("%/%", "/%///%")]
        [TestCase("%//あ%", "/%////あ/%")]
        [TestCase("あ%/%あ", "あ/%///%あ")]
        [TestCase("あ%🤔", "あ/%🤔")]
        [TestCase("あ%🤔_", "あ/%🤔/_")]
        public void TestEscapeSqlLike(string input, string expected)
        {
            Assert.AreEqual(expected, SqlUtil.EscapeSqlLike(input, '/'));
        }
    }
}
