// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using NUnit.Framework;
using System;
using Metaplay.Core;

namespace Cloud.Tests
{
    class MetaTimeTests
    {
        [Test]
        public void TestToString()
        {
            Assert.AreEqual("1970-01-01 00:00:00.000 Z", MetaTime.Epoch.ToString());
            Assert.AreEqual("2021-12-31 23:59:59.999 Z", MetaTime.FromDateTime(new DateTime(2021, 12, 31, 23, 59, 59, 999, DateTimeKind.Utc)).ToString());
        }

        [Test]
        public void TestToISO8601()
        {
            Assert.AreEqual("1970-01-01T00:00:00.0000000Z", MetaTime.Epoch.ToISO8601());
            Assert.AreEqual("2021-12-31T23:59:59.9990000Z", MetaTime.FromDateTime(new DateTime(2021, 12, 31, 23, 59, 59, 999, DateTimeKind.Utc)).ToISO8601());
        }

        [Test]
        public void TestIComparable()
        {
            Assert.Greater(MetaTime.Now, MetaTime.Epoch);
            Assert.Less(MetaTime.Epoch, MetaTime.Now);
            Assert.IsTrue(((IComparable)MetaTime.Now).CompareTo(MetaTime.Epoch) > 0);
            Assert.IsTrue(((IComparable)MetaTime.Epoch).CompareTo(MetaTime.Now) < 0);
            Assert.IsTrue(MetaTime.Now.CompareTo(MetaTime.Epoch) > 0);
            Assert.IsTrue(MetaTime.Epoch.CompareTo(MetaTime.Now) < 0);
        }

        [TestCase("2022-12-25 12:34:56.789", 2022, 12, 25, 12, 34, 56, 789)]
        [TestCase("2022-12-25 12:34:56.78", 2022, 12, 25, 12, 34, 56, 780)]
        [TestCase("2022-12-25 12:34:56.7", 2022, 12, 25, 12, 34, 56, 700)]
        [TestCase("2022-12-25 12:34:56.07", 2022, 12, 25, 12, 34, 56, 70)]
        [TestCase("2022-12-25 12:34:56.007", 2022, 12, 25, 12, 34, 56, 7)]
        [TestCase("2022-12-25 12:34:56.0", 2022, 12, 25, 12, 34, 56, 0)]
        [TestCase("2022-12-25 12:34:56", 2022, 12, 25, 12, 34, 56, 0)]
        [TestCase("2022-12-25 12:34", 2022, 12, 25, 12, 34, 0, 0)]
        [TestCase("2022-1-2 1:2:3.4", 2022, 1, 2, 1, 2, 3, 400)]
        [TestCase("2022-1-2 1:2:3", 2022, 1, 2, 1, 2, 3, 0)]
        [TestCase("2022-1-2 1:2", 2022, 1, 2, 1, 2, 0, 0)]
        [TestCase("2022-01-02 01:02:03", 2022, 1, 2, 1, 2, 3, 0)]
        [TestCase("2022-01-02 0:0:0", 2022, 1, 2, 0, 0, 0, 0)]
        [TestCase("2022-01-02 00:00:00", 2022, 1, 2, 0, 0, 0, 0)]
        [TestCase("2022-01-02 0:0", 2022, 1, 2, 0, 0, 0, 0)]
        [TestCase("2022-01-02 00:00", 2022, 1, 2, 0, 0, 0, 0)]
        [TestCase("2024-2-29 00:00:00", 2024, 2, 29, 0, 0, 0, 0)]
        [TestCase("2025-2-28 00:00:00", 2025, 2, 28, 0, 0, 0, 0)]
        [TestCase("   2022-12-25    12:34:56.789  ", 2022, 12, 25, 12, 34, 56, 789)]
        [TestCase("1671971696", 2022, 12, 25, 12, 34, 56, 0)] // Legacy seconds-since-epoch syntax.
        [TestCase("  1671971696  ", 2022, 12, 25, 12, 34, 56, 0)] // Ditto.
        [TestCase("0", 1970, 1, 1, 0, 0, 0, 0)] // Ditto.
        public void TestConfigParsing(string input, int year, int month, int day, int hour, int minute, int second, int millisecond)
        {
            MetaTime expected = MetaTime.FromDateTime(new DateTime(year, month, day, hour, minute, second, millisecond, DateTimeKind.Utc));
            ConfigLexer lexer = new ConfigLexer(input);
            MetaTime result = MetaTime.ConfigParse(lexer);
            Assert.AreEqual(expected, result);
            Assert.True(lexer.IsAtEnd);
        }

        [TestCase("2022-12-25 12:34:56.789 abc", 2022, 12, 25, 12, 34, 56, 789, "abc")]
        public void ConfigParsingWithRemainingInput(string input, int year, int month, int day, int hour, int minute, int second, int millisecond, string expectedRemainingInput)
        {
            MetaTime expected = MetaTime.FromDateTime(new DateTime(year, month, day, hour, minute, second, millisecond, DateTimeKind.Utc));
            ConfigLexer lexer = new ConfigLexer(input);
            MetaTime result = MetaTime.ConfigParse(lexer);
            Assert.AreEqual(expected, result);
            Assert.False(lexer.IsAtEnd);
            Assert.AreEqual(expectedRemainingInput, lexer.Input.Substring(lexer.CurrentToken.StartOffset));
        }

        // Ill-formed time-of-day.
        [TestCase("2022-12-25 12:34:56:0")]
        [TestCase("2022-12-25 12:34:56:")]
        [TestCase("2022-12-25 12:34:")]
        [TestCase("2022-12-25 12:")]
        [TestCase("2022-12-25 12")]
        [TestCase("2022-12-25 ::")]
        [TestCase("2022-12-25 :")]
        [TestCase("2022-12-25 012:34:56")]
        [TestCase("2022-12-25 12:034:56")]
        [TestCase("2022-12-25 12:34:056")]
        [TestCase("2022-12-25 -12:34:56")]
        [TestCase("2022-12-25 abc")]
        [TestCase("2022-12-25 12:34.56")]
        [TestCase("2022-12-25 12.34:56")]
        [TestCase("2022-12-25 12.34.56")]
        [TestCase("2022-12-25 12:34:56.0007")] // Too many fractional digits (MetaTime has millisecond resolution)
        [TestCase("2022-12-25 12:34:56.1234")] // Ditto.
        [TestCase("2022-12-25 12:34:56.0000")] // Ditto.
        [TestCase("2022-12-25 12:34:56.")] // Forbid period when there are no digits after it.
        [TestCase("2022-12-25 12:34.789")]
        // Missing time-of-day.
        [TestCase("2022-12-25")]
        // Out-of-range time-of-day components but otherwise well-formed.
        [TestCase("2022-12-25 25:00:00")]
        [TestCase("2022-12-25 00:60:00")]
        [TestCase("2022-12-25 00:00:60")]
        // Ill-formed date.
        [TestCase("2022-12-25-12 00:00:00")]
        [TestCase("2022-12-25-1 00:00:00")]
        [TestCase("2022-12-25- 00:00:00")]
        [TestCase("2022-12- 00:00:00")]
        [TestCase("2022-12 00:00:00")]
        [TestCase("2022- 00:00:00")]
        [TestCase("-2022 00:00:00")]
        [TestCase("-2022-12-25 00:00:00")]
        [TestCase("-- 00:00:00")]
        [TestCase("- 00:00:00")]
        [TestCase("2022-12-025 00:00:00")]
        [TestCase("2022-012-25 00:00:00")]
        [TestCase("02022-12-25 00:00:00")]
        [TestCase("abc 00:00:00")]
        // Missing date.
        [TestCase("00:00:00")]
        // Out-of-range date components but otherwise well-formed.
        [TestCase("2022-12-32 00:00:00")]
        [TestCase("2022-12-0 00:00:00")]
        [TestCase("2022-4-31 00:00:00")]
        [TestCase("2025-2-29 00:00:00")]
        [TestCase("2022-13-01 00:00:00")]
        [TestCase("2022-0-01 00:00:00")]
        [TestCase("20222-01-01 00:00:00")]
        [TestCase("202-01-01 00:00:00")]
        [TestCase("20-01-01 00:00:00")]
        [TestCase("2-01-01 00:00:00")]
        [TestCase("0-01-01 00:00:00")]
        // Misc.
        [TestCase("")]
        [TestCase("123.0")]
        [TestCase("123.4")]
        [TestCase("0 0")] // Legacy integer syntax expects end-of-input after the integer, so this is rejected.
        [TestCase("0 00:00:00")] // Ditto.
        [TestCase("2022 00:00:00")] // Ditto.
        public void TestConfigParsingInvalid(string input)
        {
            Assert.Throws<ParseError>(() =>
            {
                ConfigLexer lexer = new ConfigLexer(input);
                MetaTime.ConfigParse(lexer);
            });
        }

        [Test]
        public void TestParse()
        {
            // "Z" timezone specifier: Utc.
            Assert.AreEqual(MetaTime.Epoch, MetaTime.Parse("1970-01-01T00:00:00.0000000Z"));
            Assert.AreEqual(MetaTime.FromDateTime(new DateTime(2021, 12, 31, 23, 59, 59, 999, DateTimeKind.Utc)), MetaTime.Parse("2021-12-31T23:59:59.9990000Z"));

            // "+Number" time offset specifier.
            // When parsed with DateTime.Parse, it produces a DateTime with Local kind.
            // At one point, this used to be mishandled when converting to MetaTime.
            Assert.AreEqual(MetaTime.FromDateTime(new DateTime(2021, 12, 31, 23, 59, 59, 999, DateTimeKind.Utc)), MetaTime.Parse("2021-12-31T23:59:59.9990000+0"));
            Assert.AreEqual(MetaTime.FromDateTime(new DateTime(2021, 12, 31, 23, 59, 59, 999, DateTimeKind.Utc)), MetaTime.Parse("2022-01-01T04:59:59.9990000+5"));
            Assert.AreEqual(MetaTime.FromDateTime(new DateTime(2021, 12, 31, 23, 59, 59, 999, DateTimeKind.Utc)), MetaTime.Parse("2021-12-31T18:59:59.9990000-5"));

            // No time offset specifier.
            // When parsed with DateTime.Parse, it produces a DateTime with Unspecified kind.
            // MetaTime.Parse is specifically a bit permissive here by allowing such inputs.
            // See comment on MetaTime.Parse.
            Assert.AreEqual(MetaTime.FromDateTime(new DateTime(2021, 12, 31, 23, 59, 59, 999, DateTimeKind.Utc)), MetaTime.Parse("2021-12-31T23:59:59.9990000"));
        }
    }
}
