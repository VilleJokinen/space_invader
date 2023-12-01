// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using NUnit.Framework;
using System;
using Metaplay.Core;
using System.Globalization;

namespace Cloud.Tests
{
    [TestFixture]
    public class LogFormattingTests
    {
        [Test]
        public void TestMessageTemplateToFlatString()
        {
            Assert.AreEqual("String is foo, int is 1", LogTemplateFormatter.ToFlatString("String is {string}, int is {int}", new object[] { "foo", 1 }));
            Assert.AreEqual("String is foo, int is 1", LogTemplateFormatter.ToFlatString("String is {0}, int is {1}", new object[] { "foo", 1 }));
            Assert.AreEqual("pname 1", LogTemplateFormatter.ToFlatString("pname {_}", new object[] { 1 }));
            Assert.AreEqual("pname 1", LogTemplateFormatter.ToFlatString("pname {_a}", new object[] { 1 }));
            Assert.AreEqual("pname 1", LogTemplateFormatter.ToFlatString("pname {_a01Z_}", new object[] { 1 }));

            // Indexing
            Assert.AreEqual("", LogTemplateFormatter.ToFlatString("", new object[] { }));
            Assert.AreEqual("{", LogTemplateFormatter.ToFlatString("{{", new object[] { }));
            Assert.AreEqual("{0}", LogTemplateFormatter.ToFlatString("{{0}}", new object[] { }));
            Assert.AreEqual("{1}", LogTemplateFormatter.ToFlatString("{{{0}}}", new object[] { 1 }));
            Assert.AreEqual("{{0}}", LogTemplateFormatter.ToFlatString("{{{{0}}}}", new object[] { }));
            Assert.AreEqual("1", LogTemplateFormatter.ToFlatString("{0}", new object[] { 1 }));
            Assert.AreEqual("{0", LogTemplateFormatter.ToFlatString("{{0", new object[] { 1 }));

            // Named
            foreach (var holeName in new string[] { "x", "iden", "_iden", "_1", "_1x_" })
            {
                Assert.AreEqual("{" + holeName, LogTemplateFormatter.ToFlatString("{{" + holeName, new object[] { 1 }));
                Assert.AreEqual("1", LogTemplateFormatter.ToFlatString("{" + holeName + "}", new object[] { 1 }));
                Assert.AreEqual("{" + holeName + "}", LogTemplateFormatter.ToFlatString("{{" + holeName + "}}", new object[] { 1 }));
                Assert.AreEqual("{1}", LogTemplateFormatter.ToFlatString("{{{" + holeName + "}}}", new object[] { 1 }));
                Assert.AreEqual("{1", LogTemplateFormatter.ToFlatString("{{{" + holeName + "}", new object[] { 1 }));
                Assert.AreEqual("--{1--", LogTemplateFormatter.ToFlatString("--{{{" + holeName + "}--", new object[] { 1 }));
            }

            // Align & format
            foreach (var specifier in new string[] { ",3:X", ":X", ",3" })
            {
                object[] args = new object[] { 18 };
                Assert.AreEqual(String.Format(CultureInfo.InvariantCulture, "Val is {0" + specifier + "}", args), LogTemplateFormatter.ToFlatString("Val is {name" + specifier + "}", args));
                Assert.AreEqual(String.Format(CultureInfo.InvariantCulture, "Val is {0" + specifier + "}!", args), LogTemplateFormatter.ToFlatString("Val is {name" + specifier + "}!", args));
            }
        }

        [Test]
        public void TestMessageTemplateToFlatStringInvalid()
        {
            // Negative
            Assert.DoesNotThrow(() => LogTemplateFormatter.ToFlatString("{", new object[] { 1 }));
            Assert.DoesNotThrow(() => LogTemplateFormatter.ToFlatString("{0", new object[] { 1 }));
            Assert.DoesNotThrow(() => LogTemplateFormatter.ToFlatString("{iden", new object[] { 1 }));
            Assert.DoesNotThrow(() => LogTemplateFormatter.ToFlatString("{_", new object[] { 1 }));
            Assert.DoesNotThrow(() => LogTemplateFormatter.ToFlatString("{}", new object[] { 1 }));
            Assert.DoesNotThrow(() => LogTemplateFormatter.ToFlatString("{A.B}", new object[] { 1 }));
            Assert.DoesNotThrow(() => LogTemplateFormatter.ToFlatString("{A[0]}", new object[] { 1 }));
        }
    }
}
