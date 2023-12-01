// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.IO;
using Metaplay.Core.Memory;
using Metaplay.Core.Serialization;
using Metaplay.Cloud.Web3;
using NUnit.Framework;
using System;

namespace Cloud.Tests
{
    class EthereumAddressTests
    {
        [Test]
        public void TestDefaultEthereumAddress()
        {
            EthereumAddress addr = default;
            Assert.AreEqual("0x0000000000000000000000000000000000000000", addr.GetAddressString());
        }

        [TestCase("0x0000000000000000000000000000000000000000")]
        [TestCase("0xcDA67b3936A21a8EAe84A3BF8B7Ec7123c4C3234")]
        [TestCase("0x52908400098527886E0F7030069857D2E4169EE7")]
        [TestCase("0x8617E340B3D01FA5F11F306F4090FD50E238070D")]
        [TestCase("0xde709f2102306220921060314715629080e2fb77")]
        [TestCase("0x27b1fdb04752bbc536007a920d24acb045561c26")]
        [TestCase("0x5aAeb6053F3E94C9b9A09f33669435E7Ef1BeAed")]
        [TestCase("0xfB6916095ca1df60bB79Ce92cE3Ea74c37c5d359")]
        [TestCase("0xdbF03B407c01E7cD3CBea99509d93f8DDDC8C6FB")]
        [TestCase("0xD1220A0cf47c7B9Be7A2E6BA89F429762e7b9aDb")]
        public void TestFromString(string input)
        {
            EthereumAddress addr = EthereumAddress.FromString(input);
            Assert.AreEqual(input, addr.GetAddressString());

            EthereumAddress addr2 = EthereumAddress.FromStringWithoutChecksumCasing(input);
            Assert.AreEqual(input, addr2.GetAddressString());
        }

        [TestCase("0xCDA67B3936a21a8eae84a3bf8b7ec7123C4C3234")]
        [TestCase("0x52908400098527886e0f7030069857d2E4169EE7")]
        [TestCase("0x8617E340b3d01fa5f11f306f4090fd50E238070D")]
        [TestCase("0xDE709F2102306220921060314715629080E2FB77")]
        [TestCase("0x27B1FDb04752bbc536007a920d24acb045561C26")]
        [TestCase("0x5AAEB6053f3e94c9b9a09f33669435e7EF1BEAED")]
        [TestCase("0xFB6916095ca1df60bb79ce92ce3ea74c37C5D359")]
        [TestCase("0xDBF03B407c01e7cd3cbea99509d93f8dDDC8C6FB")]
        [TestCase("0xD1220A0cf47c7b9be7a2e6ba89f429762E7B9ADB")]
        public void TestFromStringWithoutChecksumCasing(string input)
        {
            EthereumAddress addr = EthereumAddress.FromStringWithoutChecksumCasing(input);
            Assert.AreEqual(input.ToLowerInvariant(), addr.GetAddressString().ToLowerInvariant());
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("0x8617E340B3D01FA5F11f306F4090FD50E238070D")]
        [TestCase("0xdE709f2102306220921060314715629080e2fb77")]
        [TestCase("0x27b1fdb04752bbc536007A920d24acb045561c26")]
        [TestCase("0X27B1FDB04752BBC536007A920D24ACB045561C26")]
        [TestCase("0x27b1fdb04752bbc536007a920d24acb045561C26")]
        [TestCase("0x27b1fdb04752bbc536007A920d24acb045561c2 ")]
        [TestCase("0x27b1fdb04752bbc536007A920d24acb045561c2_")]
        [TestCase("0x27b1fdb04752bbc536007a920d24acb045561g26")]
        [TestCase("0XdE709f2102306220921060314715629080e2fb77")]
        [TestCase("dE709f2102306220921060314715629080e2fb77")]
        [TestCase("0x8617E340B3D01FA5F11f306F4090FD50E23807")]
        [TestCase("0x8617E340B3D01FA5F11f306F4090FD50E238070201")]
        public void TestFromStringInvalid(string input)
        {
            Exception ex = Assert.Catch(() => EthereumAddress.FromString(input));
            if (input == null)
                Assert.IsTrue(ex is ArgumentNullException);
            else
                Assert.IsTrue(ex is FormatException);
        }

        [TestCase("192fc18156078d75bf4f564bdfa3de9ca909fdcd912dc8586a52f9a008ff06a0f1c65286b1466aaec85c9b0b46a5851e98a04d4137adf874612e794530145d4e", "0xF246Fa41aC41D909dee70B209932676D6121661a")]
        [TestCase("79e0310cac794097e11a15a92d46c9f4581b774af3eb00be504e3768caa39d06ecdd8b2e6ca8ddc7a7c36b4745e58b9286546ca894ff3b79808bdfaafe674f5e", "0xfdAdD8b38CB116ee6dB919Db6c315E4Ee6A73e40")]
        [TestCase("aa931f5ee58735270821b3722866d8882d1948909532cf8ac2b3ef144ae8043363d1d3728b49f10c7cd78c38289c8012477473879f3b53169f2a677b7fbed0c7", "0xe16C1623c1AA7D919cd2241d8b36d9E79C1Be2A2")]
        public void TestFromPublicKey(string input, string expected)
        {
            EthereumAddress addr = EthereumAddress.FromPublicKey(Convert.FromHexString(input));
            Assert.AreEqual(expected, addr.GetAddressString());
        }

        [TestCase("0x0000000000000000000000000000000000000000")]
        [TestCase("0xcda67b3936a21a8eae84a3bf8b7ec7123c4c3234")]
        [TestCase("0x52908400098527886e0f7030069857d2e4169ee7")]
        [TestCase("0xffffffffffffffffffffffffffffffffffffffff")]
        public void TestSerializationRoundtrip(string input)
        {
            EthereumAddress address = EthereumAddress.FromStringWithoutChecksumCasing(input);
            EthereumAddress deserialized;
            using (FlatIOBuffer buffer = new FlatIOBuffer())
            {
                using (IOWriter writer = new IOWriter(buffer))
                {
                    MetaSerialization.SerializeTagged(writer, address, MetaSerializationFlags.IncludeAll, null);
                }
                using (IOReader reader = new IOReader(buffer))
                {
                    deserialized = MetaSerialization.DeserializeTagged<EthereumAddress>(reader, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);
                }
            }
            Assert.AreEqual(address, deserialized);
        }
    }
}
