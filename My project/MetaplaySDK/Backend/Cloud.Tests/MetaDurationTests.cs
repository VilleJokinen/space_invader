// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using NUnit.Framework;
using System;
using Metaplay.Core;
using Metaplay.Core.Math;

namespace Cloud.Tests
{
    class MetaDurationTests
    {
        [Test]
        public void TestToString()
        {
            Assert.AreEqual("0.00:00:00.0000000", MetaDuration.Zero.ToJsonString());

            Assert.AreEqual("0.00:00:00.0010000", MetaDuration.FromMilliseconds(1).ToJsonString());
            Assert.AreEqual("0.00:00:01.0000000", MetaDuration.FromMilliseconds(1000).ToJsonString());
            Assert.AreEqual("0.00:00:01.0010000", MetaDuration.FromMilliseconds(1001).ToJsonString());
            Assert.AreEqual("0.00:01:00.0000000", MetaDuration.FromSeconds(60).ToJsonString());
            Assert.AreEqual("0.00:01:01.0000000", MetaDuration.FromSeconds(61).ToJsonString());
            Assert.AreEqual("0.00:01:01.0010000", (MetaDuration.FromSeconds(61) + MetaDuration.FromMilliseconds(1)).ToJsonString());
            Assert.AreEqual("0.01:00:00.0000000", MetaDuration.FromMinutes(60).ToJsonString());
            Assert.AreEqual("0.01:01:00.0000000", MetaDuration.FromMinutes(61).ToJsonString());
            Assert.AreEqual("0.01:01:01.0000000", (MetaDuration.FromMinutes(61) + MetaDuration.FromSeconds(1)).ToJsonString());
            Assert.AreEqual("0.01:01:00.0010000", (MetaDuration.FromMinutes(61) + MetaDuration.FromMilliseconds(1)).ToJsonString());
            Assert.AreEqual("1.00:00:00.0000000", MetaDuration.FromHours(24).ToJsonString());
            Assert.AreEqual("1.01:00:00.0000000", MetaDuration.FromHours(25).ToJsonString());
            Assert.AreEqual("1.01:01:00.0000000", (MetaDuration.FromHours(25) + MetaDuration.FromMinutes(1)).ToJsonString());
            Assert.AreEqual("1.01:01:01.0000000", (MetaDuration.FromHours(25) + MetaDuration.FromMinutes(1) + MetaDuration.FromSeconds(1)).ToJsonString());
            Assert.AreEqual("1.01:01:01.0010000", (MetaDuration.FromHours(25) + MetaDuration.FromMinutes(1) + MetaDuration.FromSeconds(1) + MetaDuration.FromMilliseconds(1)).ToJsonString());
            Assert.AreEqual("111.01:01:01.0010000", (MetaDuration.FromDays(111) + MetaDuration.FromHours(1) + MetaDuration.FromMinutes(1) + MetaDuration.FromSeconds(1) + MetaDuration.FromMilliseconds(1)).ToJsonString());

            Assert.AreEqual("-0.00:00:00.0010000", (-MetaDuration.FromMilliseconds(1)).ToJsonString());
            Assert.AreEqual("-0.00:00:01.0000000", (-MetaDuration.FromMilliseconds(1000)).ToJsonString());
            Assert.AreEqual("-0.00:00:01.0010000", (-MetaDuration.FromMilliseconds(1001)).ToJsonString());
            Assert.AreEqual("-0.00:01:00.0000000", (-MetaDuration.FromSeconds(60)).ToJsonString());
            Assert.AreEqual("-0.00:01:01.0000000", (-MetaDuration.FromSeconds(61)).ToJsonString());
            Assert.AreEqual("-0.00:01:01.0010000", (-(MetaDuration.FromSeconds(61) + MetaDuration.FromMilliseconds(1))).ToJsonString());
            Assert.AreEqual("-0.01:00:00.0000000", (-MetaDuration.FromMinutes(60)).ToJsonString());
            Assert.AreEqual("-0.01:01:00.0000000", (-MetaDuration.FromMinutes(61)).ToJsonString());
            Assert.AreEqual("-0.01:01:01.0000000", (-(MetaDuration.FromMinutes(61) + MetaDuration.FromSeconds(1))).ToJsonString());
            Assert.AreEqual("-0.01:01:00.0010000", (-(MetaDuration.FromMinutes(61) + MetaDuration.FromMilliseconds(1))).ToJsonString());
            Assert.AreEqual("-1.00:00:00.0000000", (-MetaDuration.FromHours(24)).ToJsonString());
            Assert.AreEqual("-1.01:00:00.0000000", (-MetaDuration.FromHours(25)).ToJsonString());
            Assert.AreEqual("-1.01:01:00.0000000", (-(MetaDuration.FromHours(25) + MetaDuration.FromMinutes(1))).ToJsonString());
            Assert.AreEqual("-1.01:01:01.0000000", (-(MetaDuration.FromHours(25) + MetaDuration.FromMinutes(1) + MetaDuration.FromSeconds(1))).ToJsonString());
            Assert.AreEqual("-1.01:01:01.0010000", (-(MetaDuration.FromHours(25) + MetaDuration.FromMinutes(1) + MetaDuration.FromSeconds(1) + MetaDuration.FromMilliseconds(1))).ToJsonString());
            Assert.AreEqual("-111.01:01:01.0010000", (-(MetaDuration.FromDays(111) + MetaDuration.FromHours(1) + MetaDuration.FromMinutes(1) + MetaDuration.FromSeconds(1) + MetaDuration.FromMilliseconds(1))).ToJsonString());
        }

        [Test]
        public void TestParseExactFromJson()
        {
            Assert.AreEqual(MetaDuration.Zero, MetaDuration.ParseExactFromJson("0.00:00:00.0000000"));

            Assert.AreEqual(MetaDuration.FromMilliseconds(1), MetaDuration.ParseExactFromJson("0.00:00:00.0010000"));
            Assert.AreEqual(MetaDuration.FromMilliseconds(1000), MetaDuration.ParseExactFromJson("0.00:00:01.0000000"));
            Assert.AreEqual(MetaDuration.FromMilliseconds(1001), MetaDuration.ParseExactFromJson("0.00:00:01.0010000"));
            Assert.AreEqual(MetaDuration.FromSeconds(60), MetaDuration.ParseExactFromJson("0.00:01:00.0000000"));
            Assert.AreEqual(MetaDuration.FromSeconds(61), MetaDuration.ParseExactFromJson("0.00:01:01.0000000"));
            Assert.AreEqual((MetaDuration.FromSeconds(61) + MetaDuration.FromMilliseconds(1)), MetaDuration.ParseExactFromJson("0.00:01:01.0010000"));
            Assert.AreEqual(MetaDuration.FromMinutes(60), MetaDuration.ParseExactFromJson("0.01:00:00.0000000"));
            Assert.AreEqual(MetaDuration.FromMinutes(61), MetaDuration.ParseExactFromJson("0.01:01:00.0000000"));
            Assert.AreEqual((MetaDuration.FromMinutes(61) + MetaDuration.FromSeconds(1)), MetaDuration.ParseExactFromJson("0.01:01:01.0000000"));
            Assert.AreEqual((MetaDuration.FromMinutes(61) + MetaDuration.FromMilliseconds(1)), MetaDuration.ParseExactFromJson("0.01:01:00.0010000"));
            Assert.AreEqual(MetaDuration.FromHours(24), MetaDuration.ParseExactFromJson("1.00:00:00.0000000"));
            Assert.AreEqual(MetaDuration.FromHours(25), MetaDuration.ParseExactFromJson("1.01:00:00.0000000"));
            Assert.AreEqual((MetaDuration.FromHours(25) + MetaDuration.FromMinutes(1)), MetaDuration.ParseExactFromJson("1.01:01:00.0000000"));
            Assert.AreEqual((MetaDuration.FromHours(25) + MetaDuration.FromMinutes(1) + MetaDuration.FromSeconds(1)), MetaDuration.ParseExactFromJson("1.01:01:01.0000000"));
            Assert.AreEqual((MetaDuration.FromHours(25) + MetaDuration.FromMinutes(1) + MetaDuration.FromSeconds(1) + MetaDuration.FromMilliseconds(1)), MetaDuration.ParseExactFromJson("1.01:01:01.0010000"));
            Assert.AreEqual((MetaDuration.FromDays(111) + MetaDuration.FromHours(1) + MetaDuration.FromMinutes(1) + MetaDuration.FromSeconds(1) + MetaDuration.FromMilliseconds(1)), MetaDuration.ParseExactFromJson("111.01:01:01.0010000"));

            Assert.AreEqual((-MetaDuration.FromMilliseconds(1)), MetaDuration.ParseExactFromJson("-0.00:00:00.0010000"));
            Assert.AreEqual((-MetaDuration.FromMilliseconds(1000)), MetaDuration.ParseExactFromJson("-0.00:00:01.0000000"));
            Assert.AreEqual((-MetaDuration.FromMilliseconds(1001)), MetaDuration.ParseExactFromJson("-0.00:00:01.0010000"));
            Assert.AreEqual((-MetaDuration.FromSeconds(60)), MetaDuration.ParseExactFromJson("-0.00:01:00.0000000"));
            Assert.AreEqual((-MetaDuration.FromSeconds(61)), MetaDuration.ParseExactFromJson("-0.00:01:01.0000000"));
            Assert.AreEqual((-(MetaDuration.FromSeconds(61) + MetaDuration.FromMilliseconds(1))), MetaDuration.ParseExactFromJson("-0.00:01:01.0010000"));
            Assert.AreEqual((-MetaDuration.FromMinutes(60)), MetaDuration.ParseExactFromJson("-0.01:00:00.0000000"));
            Assert.AreEqual((-MetaDuration.FromMinutes(61)), MetaDuration.ParseExactFromJson("-0.01:01:00.0000000"));
            Assert.AreEqual((-(MetaDuration.FromMinutes(61) + MetaDuration.FromSeconds(1))), MetaDuration.ParseExactFromJson("-0.01:01:01.0000000"));
            Assert.AreEqual((-(MetaDuration.FromMinutes(61) + MetaDuration.FromMilliseconds(1))), MetaDuration.ParseExactFromJson("-0.01:01:00.0010000"));
            Assert.AreEqual((-MetaDuration.FromHours(24)), MetaDuration.ParseExactFromJson("-1.00:00:00.0000000"));
            Assert.AreEqual((-MetaDuration.FromHours(25)), MetaDuration.ParseExactFromJson("-1.01:00:00.0000000"));
            Assert.AreEqual((-(MetaDuration.FromHours(25) + MetaDuration.FromMinutes(1))), MetaDuration.ParseExactFromJson("-1.01:01:00.0000000"));
            Assert.AreEqual((-(MetaDuration.FromHours(25) + MetaDuration.FromMinutes(1) + MetaDuration.FromSeconds(1))), MetaDuration.ParseExactFromJson("-1.01:01:01.0000000"));
            Assert.AreEqual((-(MetaDuration.FromHours(25) + MetaDuration.FromMinutes(1) + MetaDuration.FromSeconds(1) + MetaDuration.FromMilliseconds(1))), MetaDuration.ParseExactFromJson("-1.01:01:01.0010000"));
            Assert.AreEqual((-(MetaDuration.FromDays(111) + MetaDuration.FromHours(1) + MetaDuration.FromMinutes(1) + MetaDuration.FromSeconds(1) + MetaDuration.FromMilliseconds(1))), MetaDuration.ParseExactFromJson("-111.01:01:01.0010000"));
        }

        [Test]
        public void TestInvalidParseExactFromJson()
        {
            Assert.Catch<FormatException>(() => MetaDuration.ParseExactFromJson("+0.00:00:00.0000000")); // '+' not allowed
            Assert.Catch<FormatException>(() => MetaDuration.ParseExactFromJson("--0.00:00:00.0000000")); // double negated
            Assert.Catch<FormatException>(() => MetaDuration.ParseExactFromJson(".00:00:00.0000000")); // missing days
            Assert.Catch<FormatException>(() => MetaDuration.ParseExactFromJson("00:00:00.0000000")); // missing days
            Assert.Catch<FormatException>(() => MetaDuration.ParseExactFromJson("0.00:00:00.000000")); // 6 fraction digits
            Assert.Catch<FormatException>(() => MetaDuration.ParseExactFromJson("0.00:00:00.00000000")); // 8 fraction digits
            Assert.Catch<FormatException>(() => MetaDuration.ParseExactFromJson("0.24:00:00.0000000")); // invalid hours
            Assert.Catch<FormatException>(() => MetaDuration.ParseExactFromJson("0.-01:00:00.0000000")); // invalid hours
            Assert.Catch<FormatException>(() => MetaDuration.ParseExactFromJson("0.00:60:00.0000000")); // invalid minutes
            Assert.Catch<FormatException>(() => MetaDuration.ParseExactFromJson("0.00:000:00.0000000")); // invalid minutes
            Assert.Catch<FormatException>(() => MetaDuration.ParseExactFromJson("0.00:00:60.0000000")); // invalid seconds
            Assert.Catch<FormatException>(() => MetaDuration.ParseExactFromJson("0.00:00:001.0000000")); // invalid seconds
            Assert.Catch<FormatException>(() => MetaDuration.ParseExactFromJson("0.00:00:00.000000a")); // invalid fraction
            Assert.Catch<FormatException>(() => MetaDuration.ParseExactFromJson("0.00:00:00.-0000000")); // invalid fraction
            Assert.Catch<FormatException>(() => MetaDuration.ParseExactFromJson("0:00:00:00.0000000")); // invalid day/hour separator
            Assert.Catch<FormatException>(() => MetaDuration.ParseExactFromJson("0,00:00:00.0000000")); // invalid day/hour separator
            Assert.Catch<FormatException>(() => MetaDuration.ParseExactFromJson("0.00.00:00.0000000")); // invalid hour/minute separator
            Assert.Catch<FormatException>(() => MetaDuration.ParseExactFromJson("0.00:00.00.0000000")); // invalid minute/second separator
            Assert.Catch<FormatException>(() => MetaDuration.ParseExactFromJson("0.00:00:00:0000000")); // invalid second/fraction separator
        }

        [Test]
        public void TestToHumanString()
        {
            Assert.AreEqual("0.000s", MetaDuration.Zero.ToString());

            Assert.AreEqual("0.001s", MetaDuration.FromMilliseconds(1).ToString());
            Assert.AreEqual("1.000s", MetaDuration.FromMilliseconds(1000).ToString());
            Assert.AreEqual("1.001s", MetaDuration.FromMilliseconds(1001).ToString());
            Assert.AreEqual("1m 0.000s", MetaDuration.FromSeconds(60).ToString());
            Assert.AreEqual("1m 1.000s", MetaDuration.FromSeconds(61).ToString());
            Assert.AreEqual("1m 1.001s", (MetaDuration.FromSeconds(61) + MetaDuration.FromMilliseconds(1)).ToString());
            Assert.AreEqual("1h 0m 0.000s", MetaDuration.FromMinutes(60).ToString());
            Assert.AreEqual("1h 1m 0.000s", MetaDuration.FromMinutes(61).ToString());
            Assert.AreEqual("1h 1m 1.000s", (MetaDuration.FromMinutes(61) + MetaDuration.FromSeconds(1)).ToString());
            Assert.AreEqual("1h 1m 0.001s", (MetaDuration.FromMinutes(61) + MetaDuration.FromMilliseconds(1)).ToString());
            Assert.AreEqual("1d 0h 0m 0.000s", MetaDuration.FromHours(24).ToString());
            Assert.AreEqual("1d 1h 0m 0.000s", MetaDuration.FromHours(25).ToString());
            Assert.AreEqual("1d 1h 1m 0.000s", (MetaDuration.FromHours(25) + MetaDuration.FromMinutes(1)).ToString());
            Assert.AreEqual("1d 1h 1m 1.000s", (MetaDuration.FromHours(25) + MetaDuration.FromMinutes(1) + MetaDuration.FromSeconds(1)).ToString());
            Assert.AreEqual("1d 1h 1m 1.001s", (MetaDuration.FromHours(25) + MetaDuration.FromMinutes(1) + MetaDuration.FromSeconds(1) + MetaDuration.FromMilliseconds(1)).ToString());
            Assert.AreEqual("111d 1h 1m 1.001s", (MetaDuration.FromDays(111) + MetaDuration.FromHours(1) + MetaDuration.FromMinutes(1) + MetaDuration.FromSeconds(1) + MetaDuration.FromMilliseconds(1)).ToString());

            Assert.AreEqual("-0.001s", (-MetaDuration.FromMilliseconds(1)).ToString());
            Assert.AreEqual("-1.000s", (-MetaDuration.FromMilliseconds(1000)).ToString());
            Assert.AreEqual("-1.001s", (-MetaDuration.FromMilliseconds(1001)).ToString());
            Assert.AreEqual("-1m 0.000s", (-MetaDuration.FromSeconds(60)).ToString());
            Assert.AreEqual("-1m 1.000s", (-MetaDuration.FromSeconds(61)).ToString());
            Assert.AreEqual("-1m 1.001s", (-(MetaDuration.FromSeconds(61) + MetaDuration.FromMilliseconds(1))).ToString());
            Assert.AreEqual("-1h 0m 0.000s", (-MetaDuration.FromMinutes(60)).ToString());
            Assert.AreEqual("-1h 1m 0.000s", (-MetaDuration.FromMinutes(61)).ToString());
            Assert.AreEqual("-1h 1m 1.000s", (-(MetaDuration.FromMinutes(61) + MetaDuration.FromSeconds(1))).ToString());
            Assert.AreEqual("-1h 1m 0.001s", (-(MetaDuration.FromMinutes(61) + MetaDuration.FromMilliseconds(1))).ToString());
            Assert.AreEqual("-1d 0h 0m 0.000s", (-MetaDuration.FromHours(24)).ToString());
            Assert.AreEqual("-1d 1h 0m 0.000s", (-MetaDuration.FromHours(25)).ToString());
            Assert.AreEqual("-1d 1h 1m 0.000s", (-(MetaDuration.FromHours(25) + MetaDuration.FromMinutes(1))).ToString());
            Assert.AreEqual("-1d 1h 1m 1.000s", (-(MetaDuration.FromHours(25) + MetaDuration.FromMinutes(1) + MetaDuration.FromSeconds(1))).ToString());
            Assert.AreEqual("-1d 1h 1m 1.001s", (-(MetaDuration.FromHours(25) + MetaDuration.FromMinutes(1) + MetaDuration.FromSeconds(1) + MetaDuration.FromMilliseconds(1))).ToString());
            Assert.AreEqual("-111d 1h 1m 1.001s", (-(MetaDuration.FromDays(111) + MetaDuration.FromHours(1) + MetaDuration.FromMinutes(1) + MetaDuration.FromSeconds(1) + MetaDuration.FromMilliseconds(1))).ToString());
        }

        [Test]
        public void TestFromSecondsF64()
        {
            Assert.AreEqual(0, MetaDuration.FromSeconds(F64.FromDouble(0.0), MetaDuration.RoundingMode.Floor).Milliseconds);

            (int, int, double)[] doubleSamples = new (int, int, double)[]
            {
                (0, 0, 0.0),
                (250, 250, 0.25),
                (500, 500, 0.5),
                (750, 750, 0.75),

                (1000, 1000, 1.0),
                (1250, 1250, 1.25),
                (1500, 1500, 1.5),
                (1750, 1750, 1.75),

                (1000, 1001, 1.0001),
                (1000, 1001, 1.0005),
                (1000, 1001, 1.0009),

                (-1001, -1000, -1.0001),
                (-1001, -1000, -1.0005),
                (-1001, -1000, -1.0009),
            };

            foreach ((int expectedFloor, int _, double f) in doubleSamples)
                Assert.AreEqual(expectedFloor, MetaDuration.FromSeconds(F64.FromDouble(f), MetaDuration.RoundingMode.Floor).Milliseconds);

            foreach ((int _, int expectedCeil, double f) in doubleSamples)
                Assert.AreEqual(expectedCeil, MetaDuration.FromSeconds(F64.FromDouble(f), MetaDuration.RoundingMode.Ceil).Milliseconds);

            int[] wholeSeconds = new int[]
            {
                0,

                1000,
                10000,
                100000,
                1000000,
                10000000,
                100000000,
                1000000000,
                2000000000,
                Int32.MaxValue - 1,
                Int32.MaxValue

                -1000,
                -10000,
                -100000,
                -1000000,
                -10000000,
                -100000000,
                -1000000000,
                -2000000000,
                Int32.MinValue + 1,
                Int32.MinValue
            };
            uint[] q32Samples = new uint[]
            {
                0,
                1,
                1000,
                10000000,
                1000000000,
                2000000000,
                Int32.MaxValue - 1,
                (uint)Int32.MaxValue,
                (uint)Int32.MaxValue + 1,
                UInt32.MaxValue / 4 * 1,
                UInt32.MaxValue / 4 * 3,
                UInt32.MaxValue - 2,
                UInt32.MaxValue - 1,
                UInt32.MaxValue,
            };

            foreach (int wholeSecondPart in wholeSeconds)
                Assert.AreEqual(1000L * wholeSecondPart, MetaDuration.FromSeconds(F64.FromInt(wholeSecondPart), MetaDuration.RoundingMode.Floor).Milliseconds);
            foreach (int wholeSecondPart in wholeSeconds)
                Assert.AreEqual(1000L * wholeSecondPart, MetaDuration.FromSeconds(F64.FromInt(wholeSecondPart), MetaDuration.RoundingMode.Ceil).Milliseconds);

            foreach (int wholeSecondPart in wholeSeconds)
            {
                foreach (uint q in q32Samples)
                {
                    {
                        F64 f = F64.FromInt(wholeSecondPart) + F64.FromRaw(q);
                        uint qMillisecondsFloor = (uint)((q * 1000L) >> 32);
                        Assert.AreEqual(1000L * wholeSecondPart + qMillisecondsFloor, MetaDuration.FromSeconds(f, MetaDuration.RoundingMode.Floor).Milliseconds);
                    }
                    {
                        // Skip cases where computing f would underflow. Can skip the q == 0 case as well, the above takes care of it.
                        if (wholeSecondPart == Int32.MinValue)
                            continue;
                        F64 f = F64.FromInt(wholeSecondPart) - F64.FromRaw(q);
                        uint qMillisecondsFloor = (uint)F64.CeilToInt(F64.FromRaw(q) * F64.FromInt(1000));
                        Assert.AreEqual(1000L * wholeSecondPart - qMillisecondsFloor, MetaDuration.FromSeconds(f, MetaDuration.RoundingMode.Floor).Milliseconds);
                    }
                }
            }

            foreach (int wholeSecondPart in wholeSeconds)
            {
                foreach (uint q in q32Samples)
                {
                    {
                        F64 f = F64.FromInt(wholeSecondPart) + F64.FromRaw(q);
                        uint qMillisecondsCeil = (uint)F64.CeilToInt(F64.FromRaw(q) * F64.FromInt(1000));
                        Assert.AreEqual(1000L * wholeSecondPart + qMillisecondsCeil, MetaDuration.FromSeconds(f, MetaDuration.RoundingMode.Ceil).Milliseconds);
                    }
                    {
                        // Skip cases where computing f would underflow. Can skip the q == 0 case as well, the above takes care of it.
                        if (wholeSecondPart == Int32.MinValue)
                            continue;
                        F64 f = F64.FromInt(wholeSecondPart) - F64.FromRaw(q);
                        uint qMillisecondsCeil = (uint)F64.FloorToInt(F64.FromRaw(q) * F64.FromInt(1000));
                        Assert.AreEqual(1000L * wholeSecondPart - qMillisecondsCeil, MetaDuration.FromSeconds(f, MetaDuration.RoundingMode.Ceil).Milliseconds);
                    }
                }
            }
        }

        [Test]
        public void TestFromTimeSpan()
        {
            for (int value = -100; value < 100; value++)
            {
                Assert.AreEqual(MetaDuration.FromMilliseconds(value), MetaDuration.FromTimeSpan(TimeSpan.FromMilliseconds(value)));
                Assert.AreEqual(MetaDuration.FromSeconds(value), MetaDuration.FromTimeSpan(TimeSpan.FromSeconds(value)));
                Assert.AreEqual(MetaDuration.FromMinutes(value), MetaDuration.FromTimeSpan(TimeSpan.FromMinutes(value)));
                Assert.AreEqual(MetaDuration.FromHours(value), MetaDuration.FromTimeSpan(TimeSpan.FromHours(value)));
                Assert.AreEqual(MetaDuration.FromDays(value), MetaDuration.FromTimeSpan(TimeSpan.FromDays(value)));
            }
        }

        [Test]
        public void TestIComparable()
        {
            Assert.Greater(MetaDuration.FromSeconds(1), MetaDuration.Zero);
            Assert.Less(MetaDuration.Zero, MetaDuration.FromSeconds(1));
            Assert.IsTrue(((IComparable)MetaDuration.FromSeconds(1)).CompareTo(MetaDuration.Zero) > 0);
            Assert.IsTrue(((IComparable)MetaDuration.Zero).CompareTo(MetaDuration.FromSeconds(1)) < 0);
            Assert.IsTrue(MetaDuration.FromSeconds(1).CompareTo(MetaDuration.Zero) > 0);
            Assert.IsTrue(MetaDuration.Zero.CompareTo(MetaDuration.FromSeconds(1)) < 0);
        }
    }
}
