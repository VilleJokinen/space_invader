// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using static System.FormattableString;

namespace Cloud.Tests
{
    class UtilTests
    {
        [TestCase("", new byte[] { })]
        [TestCase("0, 255", new byte[] { 0, 255 })]
        public void TestBytesToString(string expected, byte[] bytes)
        {
            Assert.AreEqual(expected, Util.BytesToString(bytes));
        }

        [TestCase(new byte[] { }, "")]
        [TestCase(new byte[] { 0 }, "0x00")]
        [TestCase(new byte[] { 255 }, "0XFF")]
        [TestCase(new byte[] { 1, 255, 128 }, "0x01FF80")]
        public void TestParseHexString(byte[] expected, string str)
        {
            Assert.AreEqual(expected, Util.ParseHexString(str));
        }

        [TestCase("", new byte[] { })]
        [TestCase("ff", new byte[] { 0xff })]
        [TestCase("00ff", new byte[] { 0, 0xff })]
        [TestCase("000102030405060708090a0b0c0d0e", new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14 })]
        public void TestToHexString(string expected, byte[] bytes)
        {
            Assert.AreEqual(expected, Util.ToHexString(bytes));
        }

        [TestCase("", "")]
        [TestCase("abba", "abba")]
        [TestCase("012345678901234567890123", "012345678901234567890123")]
        [TestCase("012345678901234567890...", "0123456789012345678901234")]
        [TestCase("012345678901234567890...", "0123456789012345678901234567890123456789")]
        public void TestShortenString(string expected, string input)
        {
            Assert.AreEqual(expected, Util.ShortenString(input, maxLength: 24));
        }

        [TestCase("/api/abcd1234EFGH", "/api/abcd1234EFGH")]
        [TestCase("/api/abcd._-%\\", "/api/abcd._-%\\")]
        [TestCase("/api/abcd??????", "/api/abcd:?&`!#")]
        [TestCase("/api/abcd??", "/api/abcd☠️")] // \note: There's a variation selector after the skull
        [TestCase("/api/abcd?", "/api/abcdä")]
        public void TestSanitizePathForDisplay(string expected, string input)
        {
            Assert.AreEqual(expected, Util.SanitizePathForDisplay(input));
        }

        [TestCase(true, "")]
        [TestCase(false, "!")]
        [TestCase(false, "aaa")]
        [TestCase(true, "Zg==")]
        [TestCase(true, "Zm8=")]
        [TestCase(true, "Zm9v")]
        [TestCase(false, "Zg=")]
        [TestCase(false, "Zm8")]
        [TestCase(false, "Zm9v=")]
        [TestCase(true, "ZiANCg==")]
        [TestCase(true, "Zm8gDQo=")]
        [TestCase(true, "Zm9vIA0K")]
        [TestCase(false, "Zm!vIA0K")]
        public void TestIsBase64Encoded(bool expected, string input)
        {
            Assert.AreEqual(expected, Util.IsBase64Encoded(input));
        }

        // \note Some of the TestCases here have explicit dummy-ish TestNames because otherwise
        //       they don't get run. Looks like NUnit gets confused when the test name
        //       would have tricky contents, like I guess they would with some of these?
        [TestCase(0, "")]
        [TestCase(1, "a")]
        [TestCase(2, "ab")]
        [TestCase(5, " ~[]|")]
        [TestCase(16, "abc\r\ndef\rghi\njkl")]
        [TestCase(9, "\u0000\u0001abc\u00fe\u00ff\uaaaa\uffff", TestName=nameof(TestGetNumUnicodeCodePointsPermissive)+"(a)")]
        [TestCase(1, "🤔")]
        [TestCase(6, "あ%/%あ🤔")]
        [TestCase(1, new char[]{ '\ud800', '\udc00' }, TestName=nameof(TestGetNumUnicodeCodePointsPermissive)+"(b)")]
        [TestCase(1, new char[]{ '\ud800', '\udfff' }, TestName=nameof(TestGetNumUnicodeCodePointsPermissive)+"(c)")]
        [TestCase(1, new char[]{ '\udbff', '\udc00' }, TestName=nameof(TestGetNumUnicodeCodePointsPermissive)+"(d)")]
        [TestCase(1, new char[]{ '\udbff', '\udfff' }, TestName=nameof(TestGetNumUnicodeCodePointsPermissive)+"(e)")]
        [TestCase(1, new char[]{ '\ud83e', '\udd14' }, TestName=nameof(TestGetNumUnicodeCodePointsPermissive)+"(A)")] // 🤔 thinking emoji but expressed as UTF-16 code units
        [TestCase(2, new char[]{ '\udd14', '\ud83e' }, TestName=nameof(TestGetNumUnicodeCodePointsPermissive)+"(B)")] // same as above except reversed code points i.e. stray surrogates. Util.GetNumUnicodeCodePointsPermissive counts stray a surrogate as one code point
        [TestCase(2, new char[]{ '\ud83e', '\udd14', '\udd14' }, TestName=nameof(TestGetNumUnicodeCodePointsPermissive)+"(C)")] // high+low, stray low
        [TestCase(2, new char[]{ '\ud83e', '\ud83e', '\udd14' }, TestName=nameof(TestGetNumUnicodeCodePointsPermissive)+"(D)")] // stray high, high+low
        [TestCase(3, new char[]{ '\ud83e', '\ud83e', '\udd14', '\udd14' }, TestName=nameof(TestGetNumUnicodeCodePointsPermissive)+"(E)")] // stray high, high+low, stray low
        [TestCase(3, new char[]{ '\udd14', '\ud83e', '\udd14', '\ud83e' }, TestName=nameof(TestGetNumUnicodeCodePointsPermissive)+"(F)")] // stray low, high+low, stray high
        [TestCase(3, new char[]{ '\udd14', '\ud83e', '\udd14', '\ud83e', '\udd14' }, TestName=nameof(TestGetNumUnicodeCodePointsPermissive)+"(G)")] // stray low, high+low, high+low
        [TestCase(1, new char[]{ '\ud83e', }, TestName=nameof(TestGetNumUnicodeCodePointsPermissive)+"(H)")] // stray high
        [TestCase(2, new char[]{ '\ud83e', '\ud83e' }, TestName=nameof(TestGetNumUnicodeCodePointsPermissive)+"(I)")] // 2x stray high
        [TestCase(1, new char[]{ '\udd14' }, TestName=nameof(TestGetNumUnicodeCodePointsPermissive)+"(J)")] // stray low
        [TestCase(2, new char[]{ '\udd14', '\udd14' }, TestName=nameof(TestGetNumUnicodeCodePointsPermissive)+"(K)")] // 2x stray low
        [TestCase(3, new char[]{ '\ud83e', ' ', '\udd14' }, TestName=nameof(TestGetNumUnicodeCodePointsPermissive)+"(L)")] // misc strays
        [TestCase(3, new char[]{ '\udd14', ' ', '\ud83e' }, TestName=nameof(TestGetNumUnicodeCodePointsPermissive)+"(M)")] // misc strays
        public void TestGetNumUnicodeCodePointsPermissive(int expected, object inputParam)
        {
            // Support input param as either direct string, or a char[] from which to construct the string.
            // char[] is used for testing stray surrogates.
            string str;
            if (inputParam is string strParam)
                str = strParam;
            else if (inputParam is char[] charArrayParam)
                str = new string(charArrayParam);
            else
                throw new ArgumentException("string or char[] expected", nameof(inputParam));

            Assert.AreEqual(expected, Util.GetNumUnicodeCodePointsPermissive(str));
            // Compare also to reference implementation to assert our understanding of what's actually going on.
            Assert.AreEqual(ReferenceGetNumUnicodeCodePointsPermissive(str), Util.GetNumUnicodeCodePointsPermissive(str));
        }

        int ReferenceGetNumUnicodeCodePointsPermissive(string str)
        {
            int numCodePoints = 0;
            int i = 0;
            while (i < str.Length)
            {
                // If there's a valid surrogate pair here, skip the whole pair while counting just one code point.
                // Otherwise (including on stray surrogates), each 'char' (i.e. UTF-16 code unit) is counted as one code point.
                if (char.IsHighSurrogate(str[i])
                 && i+1 < str.Length
                 && char.IsLowSurrogate(str[i + 1]))
                {
                    i++;
                }

                i++;
                numCodePoints++;
            }

            return numCodePoints;
        }

        [Test]
        public void TestIsPowerOfTwo()
        {
            Assert.IsFalse(Util.IsPowerOfTwo(0));
            Assert.IsTrue(Util.IsPowerOfTwo(1));
            Assert.IsTrue(Util.IsPowerOfTwo(2));
            Assert.IsFalse(Util.IsPowerOfTwo(3));

            int val = 1 << 2;
            for (int i = 2; i < 31; ++i)
            {
                Assert.IsTrue(Util.IsPowerOfTwo(val));
                Assert.IsFalse(Util.IsPowerOfTwo(val + 1));
                Assert.IsFalse(Util.IsPowerOfTwo(val - 1));
                Assert.IsFalse(Util.IsPowerOfTwo(val + 13));
                val <<= 1;
            }
            Assert.IsFalse(Util.IsPowerOfTwo(-1));
            Assert.IsFalse(Util.IsPowerOfTwo(-13154));
            Assert.IsFalse(Util.IsPowerOfTwo(1 << 31));
            Assert.IsFalse(Util.IsPowerOfTwo((1 << 30) + 1));
            Assert.IsFalse(Util.IsPowerOfTwo(Int32.MaxValue));
            Assert.IsFalse(Util.IsPowerOfTwo((int)((1u << 31) - 1)));
        }

        [Test]
        public void TestCeilToPowerOfTwo()
        {
            Assert.AreEqual(1, Util.CeilToPowerOfTwo(0));
            Assert.AreEqual(1, Util.CeilToPowerOfTwo(1));
            Assert.AreEqual(2, Util.CeilToPowerOfTwo(2));
            Assert.AreEqual(4, Util.CeilToPowerOfTwo(3));
            Assert.AreEqual(1 << 30, Util.CeilToPowerOfTwo(1 << 30));

            int val = 1 << 3;
            for (int i = 3; i < 31; ++i)
            {
                Assert.AreEqual(val, Util.CeilToPowerOfTwo(val));
                Assert.AreEqual(val, Util.CeilToPowerOfTwo(val - 1));
                Assert.AreEqual(val, Util.CeilToPowerOfTwo(val - 2));
                if (i < 30)
                    Assert.AreEqual(val*2, Util.CeilToPowerOfTwo(val + 1));
                val <<= 1;
            }

            Assert.Catch<ArgumentOutOfRangeException>(() => Util.CeilToPowerOfTwo(-1));
            Assert.Catch<ArgumentOutOfRangeException>(() => Util.CeilToPowerOfTwo(-13154));
            Assert.Catch<ArgumentOutOfRangeException>(() => Util.CeilToPowerOfTwo(1 << 31));

            Assert.Catch<OverflowException>(() => Util.CeilToPowerOfTwo((1 << 30) + 1));
            Assert.Catch<OverflowException>(() => Util.CeilToPowerOfTwo(Int32.MaxValue));
            Assert.Catch<OverflowException>(() => Util.CeilToPowerOfTwo((int)((1u << 31) - 1)));
        }

        [Test]
        public void TestObjectToStringInvariant()
        {
            // Remember culture
            CultureInfo oldCultureInfo = System.Threading.Thread.CurrentThread.CurrentCulture;

            // Set up a culture that differs from InvariantCulture in relevant ways
            System.Threading.Thread.CurrentThread.CurrentCulture = new CultureInfo("fi-FI");

            object formattableObj       = -1.23;
            object nonFormattableObj    = EntityId.ParseFromString("Player:23456abcde");

            // Check prerequisite for test: double.ToString() should differ from InvariantCulture results, due to the differing CurrentCulture set above
            #pragma warning disable MP_STR_02
            Assert.AreNotEqual("-1.23", formattableObj.ToString());
            #pragma warning restore MP_STR_02

            // Test: double should be formatted according to InvariantCulture, despite the differing current culture
            Assert.AreEqual("-1.23", Util.ObjectToStringInvariant(formattableObj));

            // Sanity test: culture shouldn't matter for this type
            Assert.AreEqual("Player:23456abcde", Util.ObjectToStringInvariant(nonFormattableObj));

            // Restore culture
            System.Threading.Thread.CurrentThread.CurrentCulture = oldCultureInfo;
        }

        [Test]
        public void TestMinMax()
        {
            int a = 1; // smallest
            int b = 2;
            int c = 3;
            int d = 4; // biggest

            // Min

            Assert.AreEqual(a, Util.Min(a, d));
            Assert.AreEqual(a, Util.Min(d, a));

            Assert.AreEqual(a, Util.Min(a, b, d));
            Assert.AreEqual(a, Util.Min(a, d, b));
            Assert.AreEqual(a, Util.Min(b, a, d));
            Assert.AreEqual(a, Util.Min(d, a, b));
            Assert.AreEqual(a, Util.Min(b, d, a));
            Assert.AreEqual(a, Util.Min(d, b, a));

            // \todo Doesn't test all permutations
            Assert.AreEqual(a, Util.Min(a, b, c, d));
            Assert.AreEqual(a, Util.Min(b, a, c, d));
            Assert.AreEqual(a, Util.Min(b, c, a, d));
            Assert.AreEqual(a, Util.Min(b, c, d, a));

            // Max

            Assert.AreEqual(d, Util.Max(a, d));
            Assert.AreEqual(d, Util.Max(d, a));

            Assert.AreEqual(d, Util.Max(a, b, d));
            Assert.AreEqual(d, Util.Max(a, d, b));
            Assert.AreEqual(d, Util.Max(b, a, d));
            Assert.AreEqual(d, Util.Max(d, a, b));
            Assert.AreEqual(d, Util.Max(b, d, a));
            Assert.AreEqual(d, Util.Max(d, b, a));

            // \todo Doesn't test all permutations
            Assert.AreEqual(d, Util.Max(a, b, c, d));
            Assert.AreEqual(d, Util.Max(b, a, c, d));
            Assert.AreEqual(d, Util.Max(b, c, a, d));
            Assert.AreEqual(d, Util.Max(b, c, d, a));
        }

        class DuplicateTestElement
        {
            public int Id;
            public string X;

            public DuplicateTestElement(int id, string x)
            {
                Id = id;
                X = x;
            }

            public override string ToString() => Invariant($"DuplicateTestElement({Id}, {X})");
        }

        [Test]
        public void TestCheckPropertyDuplicates()
        {
            {
                DuplicateTestElement[] empty = new DuplicateTestElement[] { };

                List<string> dupeProps = new List<string>();
                Util.CheckPropertyDuplicates(empty, elem => elem.X, (e0, e1, prop) => dupeProps.Add(prop));
                Assert.AreEqual(0, dupeProps.Count);
            }

            {
                DuplicateTestElement[] allUnique = new DuplicateTestElement[]
                {
                    new DuplicateTestElement(0, "foo"),
                    new DuplicateTestElement(1, "bar"),
                    new DuplicateTestElement(2, "test"),
                };

                List<string> dupeProps = new List<string>();
                Util.CheckPropertyDuplicates(allUnique, elem => elem.X, (e0, e1, prop) => dupeProps.Add(prop));
                Assert.AreEqual(0, dupeProps.Count);
            }

            {
                DuplicateTestElement[] containsDupes = new DuplicateTestElement[]
                {
                    new DuplicateTestElement(0, "foo"),
                    new DuplicateTestElement(1, "bar"),
                    new DuplicateTestElement(2, "foo"),
                    new DuplicateTestElement(3, "test"),
                    new DuplicateTestElement(4, "bar"),
                    new DuplicateTestElement(5, "foo"),
                };

                List<(DuplicateTestElement E0, DuplicateTestElement E1, string Prop)> dupes = new();
                Util.CheckPropertyDuplicates(containsDupes, elem => elem.X, (e0, e1, prop) => dupes.Add((e0, e1, prop)));

                Assert.AreEqual(3, dupes.Count);

                Assert.AreEqual(0, dupes[0].E0.Id);
                Assert.AreEqual(2, dupes[0].E1.Id);
                Assert.AreEqual("foo", dupes[0].Prop);

                Assert.AreEqual(1, dupes[1].E0.Id);
                Assert.AreEqual(4, dupes[1].E1.Id);
                Assert.AreEqual("bar", dupes[1].Prop);

                Assert.AreEqual(0, dupes[2].E0.Id);
                Assert.AreEqual(5, dupes[2].E1.Id);
                Assert.AreEqual("foo", dupes[2].Prop);
            }

            {
                DuplicateTestElement[] containsDupes = new DuplicateTestElement[]
                {
                    new DuplicateTestElement(0, "foo"),
                    new DuplicateTestElement(1, "bar"),
                    new DuplicateTestElement(2, "foo"),
                    new DuplicateTestElement(3, "test"),
                    new DuplicateTestElement(4, "bar"),
                    new DuplicateTestElement(5, "foo"),
                };

                InvalidOperationException ex = Assert.Catch<InvalidOperationException>(
                    () =>
                        Util.CheckPropertyDuplicates(
                        containsDupes,
                        elem => elem.X,
                        (e0, e1, prop) => throw new InvalidOperationException($"First dupe ids {e0.Id} and {e1.Id}, value {prop}")));

                Assert.AreEqual("First dupe ids 0 and 2, value foo", ex.Message);
            }
        }

        [Test]
        public void TestComputeReachableNodes()
        {
            AssertSetsAreEqual(
                new OrderedSet<string> { },
                Util.ComputeReachableNodes(
                    startNodes: new OrderedSet<string> { },
                    nodeNeighbors: new Dictionary<string, OrderedSet<string>>
                    {
                    }));

            AssertSetsAreEqual(
                new OrderedSet<string> { "A", "B" },
                Util.ComputeReachableNodes(
                    startNodes: new OrderedSet<string> { "A", "B" },
                    nodeNeighbors: new Dictionary<string, OrderedSet<string>>
                    {
                    }));

            // A <-> B
            // start at A & B
            AssertSetsAreEqual(
                new OrderedSet<string> { "A", "B" },
                Util.ComputeReachableNodes(
                    startNodes: new OrderedSet<string> { "A", "B" },
                    nodeNeighbors: new Dictionary<string, OrderedSet<string>>
                    {
                        { "A", new OrderedSet<string> { "B" } },
                        { "B", new OrderedSet<string> { "A" } },
                    }));

            // A -> B -> C
            // start at B
            AssertSetsAreEqual(
                new OrderedSet<string> { "B", "C" },
                Util.ComputeReachableNodes(
                    startNodes: new OrderedSet<string> { "B" },
                    nodeNeighbors: new Dictionary<string, OrderedSet<string>>
                    {
                        { "A", new OrderedSet<string> { "B" } },
                        { "B", new OrderedSet<string> { "C" } },
                    }));

            /*
                     X    Y    Z    W
                     ^    |    ^    |
                     |    v    |    v
                0 -> 1 -> 2 -> 3 -> 4
                     ^    |    ^    |
                     |    v    |    v
                     A    B    C    D

                start at 0
            */
            AssertSetsAreEqual(
                new OrderedSet<string> { "0", "1", "2", "3", "4", "X", "B", "Z", "D" },
                Util.ComputeReachableNodes(
                    startNodes: new OrderedSet<string> { "0" },
                    nodeNeighbors: new Dictionary<string, OrderedSet<string>>
                    {
                        { "0", new OrderedSet<string> { "1" } },
                        { "1", new OrderedSet<string> { "X", "2" } },
                        { "2", new OrderedSet<string> { "3", "B" } },
                        { "3", new OrderedSet<string> { "Z", "4" } },
                        { "4", new OrderedSet<string> { "D" } },
                        { "A", new OrderedSet<string> { "1" } },
                        { "Y", new OrderedSet<string> { "2" } },
                        { "C", new OrderedSet<string> { "3" } },
                        { "W", new OrderedSet<string> { "4" } },
                    }));

            /*
                --> X -> Y -> Z --
                |                |
                |                v
                0 -> W           1
                |                ^
                |                |
                --> A -> B -> C --

                start at 0
            */
            AssertSetsAreEqual(
                new OrderedSet<string> { "0", "W", "A", "B", "C", "X", "Y", "Z", "1" },
                Util.ComputeReachableNodes(
                    startNodes: new OrderedSet<string> { "0" },
                    nodeNeighbors: new Dictionary<string, OrderedSet<string>>
                    {
                        { "0", new OrderedSet<string> { "X", "A", "W" } },
                        { "X", new OrderedSet<string> { "Y" } },
                        { "Y", new OrderedSet<string> { "Z" } },
                        { "Z", new OrderedSet<string> { "1" } },
                        { "A", new OrderedSet<string> { "B" } },
                        { "B", new OrderedSet<string> { "C" } },
                        { "C", new OrderedSet<string> { "1" } },
                    }));

            /*
                A -> B   X -> Y
                ^    |   ^    |
                |    |   |    |
                - C <-   - Z <-

                start at A
            */
            AssertSetsAreEqual(
                new OrderedSet<string> { "A", "B", "C" },
                Util.ComputeReachableNodes(
                    startNodes: new OrderedSet<string> { "A" },
                    nodeNeighbors: new Dictionary<string, OrderedSet<string>>
                    {
                        { "A", new OrderedSet<string> { "B" } },
                        { "B", new OrderedSet<string> { "C" } },
                        { "C", new OrderedSet<string> { "A" } },
                        { "X", new OrderedSet<string> { "Y" } },
                        { "Y", new OrderedSet<string> { "Z" } },
                        { "Z", new OrderedSet<string> { "X" } },
                    }));

            /*
                A -> B   X -> Y
                ^    |   ^    |
                |    |   |    |
                - C <-   - Z <-

                start at A and Y
            */
            AssertSetsAreEqual(
                new OrderedSet<string> { "A", "B", "C", "X", "Y", "Z" },
                Util.ComputeReachableNodes(
                    startNodes: new OrderedSet<string> { "A", "Y" },
                    nodeNeighbors: new Dictionary<string, OrderedSet<string>>
                    {
                        { "A", new OrderedSet<string> { "B" } },
                        { "B", new OrderedSet<string> { "C" } },
                        { "C", new OrderedSet<string> { "A" } },
                        { "X", new OrderedSet<string> { "Y" } },
                        { "Y", new OrderedSet<string> { "Z" } },
                        { "Z", new OrderedSet<string> { "X" } },
                    }));
        }

        void AssertSetsAreEqual<T>(OrderedSet<T> expected, OrderedSet<T> actual)
        {
            foreach (T x in expected)
                Assert.IsTrue(actual.Contains(x));

            foreach (T x in actual)
                Assert.IsTrue(expected.Contains(x));
        }
    }
}
