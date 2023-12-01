// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using NUnit.Framework;
using System;

namespace Cloud.Tests
{
    class FileUtilTests
    {
        [TestCase("", "")]
        [TestCase("foo", "foo")]
        [TestCase("foo/", "foo")]
        [TestCase("foo/.", "foo")]
        [TestCase("foo/./.", "foo")]
        [TestCase("./foo/.", "foo")]
        [TestCase("././foo/.", "foo")]
        [TestCase("/", "/")]
        [TestCase("/foo", "/foo")]
        [TestCase("/foo/", "/foo")]
        [TestCase("/foo/..", "/")]
        [TestCase("C:/foo", "C:/foo")]
        [TestCase("C:/foo/", "C:/foo")]
        [TestCase("C:/foo/..", "C:/")]
        [TestCase("C:\\foo\\..", "C:/", Ignore="Disabled due to platform-dependency of backslash handling.")] // \todo Figure out what to do about backslashes.
        [TestCase("foo/bar", "foo/bar")]
        [TestCase("foo\\bar", "foo/bar", Ignore="Disabled due to platform-dependency of backslash handling.")] // \todo Figure out what to do about backslashes.
        [TestCase("foo\\bar/../bie", "foo/bie", Ignore="Disabled due to platform-dependency of backslash handling.")] // \todo Figure out what to do about backslashes.
        [TestCase("/foo", "/foo")]
        [TestCase("foo/..", "")]
        [TestCase("foo/../.", "")]
        [TestCase("foo/bar/../..", "")]
        [TestCase("foo/bar/../../", "")]
        [TestCase("./foo/./../bar/../.", "")]
        [TestCase("..", "..")]
        [TestCase("../..", "../..")]
        [TestCase("../../foo", "../../foo")]
        [TestCase("../../foo/..", "../..")]
        [TestCase("../foo/../..", "../..")]
        [TestCase("foo/../..", "..")]
        [TestCase("foo/../../bar", "../bar")]
        public void TestNormalizePath(string input, string expected)
        {
            string output = FileUtil.NormalizePath(input);
            Assert.AreEqual(expected, output);
        }

        [TestCase("/..")]
        [TestCase("/bar/../..")]
        [TestCase("//bar")]
        [TestCase("foo//bar")]
        [TestCase("C:/..")]
        [TestCase("C:/foo/../..")]
        public void TestNormalizePathInvalid(string input)
        {
            Assert.Throws<ArgumentException>(() => FileUtil.NormalizePath(input));
        }
    }
}
