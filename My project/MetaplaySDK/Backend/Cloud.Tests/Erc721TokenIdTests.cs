// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Web3;
using Metaplay.Core;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace Cloud.Tests
{
    class Erc721TokenIdTests
    {
        [Test]
        public void TestDefaultErc721TokenId()
        {
            Erc721TokenId token = default;
            Assert.AreEqual("0", token.GetTokenIdString());
        }

        [TestCase("0")]
        [TestCase("1")]
        [TestCase("2")]
        [TestCase("123123123")]
        [TestCase("1231231231231231231231415159090")]
        [TestCase("1157920892373161954235709850086879078532699846656")]
        [TestCase("115792089237316195423570985008687907853269984665640564039457584007913129639930")]
        [TestCase("115792089237316195423570985008687907853269984665640564039457584007913129639935")]
        [TestCase("115792089237316195423570985008687907853269984665640564039457584007913129639934")]
        public void TestFromDecimalString(string input)
        {
            Erc721TokenId tokenId = Erc721TokenId.FromDecimalString(input);
            Assert.AreEqual(input, tokenId.GetTokenIdString());
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("-1")]
        [TestCase("00")]
        [TestCase("01")]
        [TestCase("0xAAA")]
        [TestCase("FFFF")]
        [TestCase("115792089237316195423570985008687907853269984665640564039457584007913129639936")]
        [TestCase("1157920892373161954235709850086879078532699846656405640394575840079131296399350")]
        [TestCase("123 ")]
        [TestCase(" 123")]
        public void TestFromDecimalStringInvalid(string input)
        {
            Exception ex = Assert.Catch(() => Erc721TokenId.FromDecimalString(input));
            if (input == null)
                Assert.IsTrue(ex is ArgumentNullException);
            else
                Assert.IsTrue(ex is FormatException);
        }

        [Test]
        public void TestOrder()
        {
            List<Erc721TokenId> ordered = new List<Erc721TokenId>()
            {
                Erc721TokenId.FromDecimalString("0"),
                Erc721TokenId.FromDecimalString("1"),
                Erc721TokenId.FromDecimalString("9"),
                Erc721TokenId.FromDecimalString("10"),
                Erc721TokenId.FromDecimalString("100"),
                Erc721TokenId.FromDecimalString("2000"),
                Erc721TokenId.FromDecimalString("115792089237316195423570985008687907853269984665640564039457584007913129639935"),
            };

            List<Erc721TokenId> tested = new List<Erc721TokenId>(ordered);
            RandomPCG.CreateFromSeed(123).ShuffleInPlace(tested);
            tested.Sort();

            Assert.AreEqual(ordered, tested);
        }
    }
}
