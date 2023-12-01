// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using NUnit.Framework;

namespace Cloud.Tests
{
    class Base32CodecTests
    {
        static (byte[], string)[] PaddingTestVectors = new (byte[], string)[]
        {
            (new byte[]{ }, ""),
            (new byte[]{ 0x00 }, "AA======"),
            (new byte[]{ 0xff }, "74======"),
            (new byte[]{ 0x12, 0x34 }, "CI2A===="),
            (new byte[]{ 0x12, 0x34, 0x56 }, "CI2FM==="),
            (new byte[]{ 0x12, 0x34, 0x56, 0x78 }, "CI2FM6A="),
            (new byte[]{ 0x12, 0x34, 0x56, 0x78, 0x9f }, "CI2FM6E7"),
            (new byte[]{ 0x12, 0x34, 0x56, 0x78, 0x9f, 0xAB }, "CI2FM6E7VM======"),
        };
        static (byte[], string)[] PadlessTestVectors = new (byte[], string)[]
        {
            (new byte[]{ }, ""),
            (new byte[]{ 0x00 }, "AA"),
            (new byte[]{ 0xff }, "74"),
            (new byte[]{ 0x12, 0x34 }, "CI2A"),
            (new byte[]{ 0x12, 0x34, 0x56 }, "CI2FM"),
            (new byte[]{ 0x12, 0x34, 0x56, 0x78 }, "CI2FM6A"),
            (new byte[]{ 0x12, 0x34, 0x56, 0x78, 0x9f }, "CI2FM6E7"),
            (new byte[]{ 0x12, 0x34, 0x56, 0x78, 0x9f, 0xAB }, "CI2FM6E7VM"),
        };

        [Test]
        public void TestEncodeWithPadding()
        {
            foreach ((byte[] bytes, string encoded) in PaddingTestVectors)
                Assert.AreEqual(encoded, Base32Codec.EncodeToString(bytes, padding: true));
        }

        [Test]
        public void TestEncodeWithoutPadding()
        {
            foreach ((byte[] bytes, string encoded) in PadlessTestVectors)
                Assert.AreEqual(encoded, Base32Codec.EncodeToString(bytes, padding: false));
        }

        [Test]
        public void TestDecodeWithPadding()
        {
            foreach ((byte[] bytes, string encoded) in PaddingTestVectors)
                Assert.AreEqual(bytes, Base32Codec.TryDecodeToBytes(encoded, padding: true));
        }

        [Test]
        public void TestDecodeWithoutPadding()
        {
            foreach ((byte[] bytes, string encoded) in PadlessTestVectors)
                Assert.AreEqual(bytes, Base32Codec.TryDecodeToBytes(encoded, padding: false));
        }

        [Test]
        public void TestRoundtrip()
        {
            RandomPCG rng = RandomPCG.CreateFromSeed(0x12345);
            for (int repeat = 0; repeat < 100; ++repeat)
            {
                byte[] data = new byte[rng.NextInt(100) + 1];
                for (int ndx = 0; ndx < data.Length; ++ndx)
                    data[ndx] = (byte)rng.NextUInt();
                bool padding = rng.NextBool();

                string encoded = Base32Codec.EncodeToString(data, padding);
                byte[] decoded = Base32Codec.TryDecodeToBytes(encoded, padding);

                Assert.AreEqual(data, decoded);
            }
        }

        [Test]
        public void TestInvalidFormats()
        {
            // missing padding
            foreach ((byte[] bytes, string encoded) in PaddingTestVectors)
            {
                int dropped = 1;
                for (;;)
                {
                    if (encoded.Length < dropped)
                        break;
                    if (encoded[encoded.Length - dropped] != '=')
                        break;
                    Assert.IsNull(Base32Codec.TryDecodeToBytes(encoded.Substring(0, encoded.Length - dropped), padding: true));
                    dropped++;
                }
            }

            // extra pad
            for (int i = 1; i <= 8; ++i)
                Assert.IsNull(Base32Codec.TryDecodeToBytes("74======" + new string('=', i), padding: true));

            // random char
            Assert.IsNull(Base32Codec.TryDecodeToBytes("CI9A====", padding: true));
            Assert.IsNull(Base32Codec.TryDecodeToBytes("CI9A", padding: false));
            Assert.IsNull(Base32Codec.TryDecodeToBytes("CI=A====", padding: true));
            Assert.IsNull(Base32Codec.TryDecodeToBytes("CI=A", padding: false));

            // exra bits
            Assert.IsNull(Base32Codec.TryDecodeToBytes("AB======", padding: true));
            Assert.IsNull(Base32Codec.TryDecodeToBytes("AB", padding: false));
        }
    }
}
