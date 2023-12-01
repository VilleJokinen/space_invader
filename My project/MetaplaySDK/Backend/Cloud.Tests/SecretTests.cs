// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud;
using Metaplay.Cloud.Utility;
using Metaplay.Core;
using NUnit.Framework;
using System;
using System.Threading.Tasks;

namespace Cloud.Tests
{
    class SecretTests
    {
        static IMetaLogger _log = MetaLogger.ForContext<SecretTests>();

        [Test]
        public async Task TestUnsafeSecret()
        {
            Assert.AreEqual("TestSecret", await SecretUtil.ResolveSecretAsync(_log, "unsafe://TestSecret"));
        }

        [TestCase("test-secret.txt")]
        [TestCase("file://test-secret.txt")]
        public async Task TestFileSecret(string filePath)
        {
            Assert.AreEqual("TestSecret", (await SecretUtil.ResolveSecretAsync(_log, filePath)).Trim());
        }

        [Test]
        public void TestFileNotFound()
        {
            Assert.ThrowsAsync<System.IO.FileNotFoundException>(async () => (await SecretUtil.ResolveSecretAsync(_log, "non-existent-file.txt")).Trim());
        }

        [Test]
        public void TestNoDefault()
        {
            Assert.ThrowsAsync<ArgumentException>(async () => await SecretUtil.ResolveSecretAsync(_log, "test-secret.txt", defaultToFile: false));
        }
    }
}
