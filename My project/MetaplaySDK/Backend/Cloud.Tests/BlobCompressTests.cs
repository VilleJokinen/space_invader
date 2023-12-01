// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Utility;
using Metaplay.Core;
using Metaplay.Core.Memory;
using NUnit.Framework;

namespace Cloud.Tests
{
    class BlobCompressTests
    {
        [TestCase(CompressionAlgorithm.LZ4)]
        [TestCase(CompressionAlgorithm.Zstandard)]
        public void TestCompressionAlgorithm(CompressionAlgorithm algorithm)
        {
            // \note Using an arbitrary file from the project for testing -- if changes to it
            // causes the compression ratio to drop below the threshold, feel free to adjust either
            // the file or the required ratio.
            byte[] input = FileUtil.ReadAllBytes("TypeExtensionsTests.cs");
            using FlatIOBuffer inBuffer = FlatIOBuffer.CopyFromSpan(input);
            using FlatIOBuffer outBuffer = BlobCompress.CompressBlob(inBuffer, algorithm);
            float ratio = outBuffer.Count / (float)inBuffer.Count;
            Assert.LessOrEqual(ratio, 0.50f); // output must be less than half the input
        }
    }
}
