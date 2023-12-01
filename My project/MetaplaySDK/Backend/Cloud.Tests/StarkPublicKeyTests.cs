// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Web3;
using NUnit.Framework;
using System;

namespace Cloud.Tests
{
    class StarkPublicKeyTests
    {
        [Test]
        public void TestDefaultStarkPublicKey()
        {
            StarkPublicKey addr = default;
            Assert.AreEqual("0x0000000000000000000000000000000000000000000000000000000000000000", addr.GetPublicKeyString());
        }

        [TestCase("0x0000000000000000000000000000000000000000000000000000000000000000")]
        [TestCase("0x04ca6e59a29db2e76941e96d82d3bed3f3e5f4fa3812ca64a59833c4c6196bc0")]
        [TestCase("0x04ca6e59a29db2e76941e96d82D3BED3F3E5F4FA3812CA64A59833C4C6196BC0")]
        [TestCase("0x04CA6E59A29DB2E76941E96D82D3BED3F3E5F4FA3812CA64A59833C4C6196BC0")]
        public void TestParseStarkPublicKey(string input)
        {
            StarkPublicKey addr = StarkPublicKey.FromString(input);
            Assert.AreEqual(input.ToLowerInvariant(), addr.GetPublicKeyString());
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("0x04ca6e59a29db2e76941e96d82d3bed3f3e5f4fa3812ca64a59833c4c6196bc0 ")]
        [TestCase("0x04ca6e59a29db2e76941e96d82d3bed3f3e5f4fa3812ca64a59833c4c6196bc0_")]
        [TestCase("0x04ca6e59a29db2e76941e96d82d3bed3f3e5f4fa3812ca64a59833c4c6196bx0")]
        [TestCase("04ca6e59a29db2e76941e96d82d3bed3f3e5f4fa3812ca64a59833c4c6196bc0")]
        [TestCase("0X04CA6E59A29DB2E76941E96D82D3BED3F3E5F4FA3812CA64A59833C4C6196BX0")]
        public void TestParseInvalidStarkPublicKey(string input)
        {
            Exception ex = Assert.Catch(() => StarkPublicKey.FromString(input));
            if (input == null)
                Assert.IsTrue(ex is ArgumentNullException);
            else
                Assert.IsTrue(ex is FormatException);
        }
    }
}
