// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.RuntimeOptions;
using NUnit.Framework;
using System.Collections.Generic;

namespace Cloud.Tests
{
    class CommandLineRuntimeOptionsTests
    {
        [Test]
        public void Empty()
        {
            _ = CommandLineRuntimeOptions.Parse(new string[] { }, new Dictionary<string, string>());
        }

        [TestCase("--SectionName:FieldName=Value")]
        [TestCase("--SectionName:FieldName|Value")]
        public void LongFormats(string input)
        {
            CommandLineRuntimeOptions options = CommandLineRuntimeOptions.Parse(input.Split('|'), new Dictionary<string, string>());
            Assert.AreEqual(options.Definitions.Count, 1);
            Assert.AreEqual(options.Definitions["SectionName:FieldName"], "Value");
        }

        [TestCase("-")]
        [TestCase("-=")]
        [TestCase("-=Value")]
        [TestCase("-UnknownValue=1")]
        [TestCase("-UnknownValue|1")]
        [TestCase("--")]
        [TestCase("--=")]
        [TestCase("--=Value")]
        [TestCase("--:=Value")]
        [TestCase("--:FieldName=Value")]
        [TestCase("--SectionName=Value")]
        [TestCase("--SectionName|Value")]
        [TestCase("--SectionName:=Value")]
        [TestCase("filename")]
        [TestCase("--SectionName:FieldName=Value|filename")]
        [TestCase("--SectionName:FieldName|Value|filename")]
        [TestCase("--SectionName:FieldName|Value|--|filename")]
        [TestCase("--SectionName:FieldName")]
        [TestCase("--SectionName:Duplicate=1|--SectionName:Duplicate=1")]
        [TestCase("--SectionName:Duplicate|1|--SectionName:Duplicate|1")]
        [TestCase("--SectionName:DuplicateMixed=1|--SectionName:DuplicateMIXED=2")]
        [TestCase("--MixedSection:Field1=1|--MiXEDSection:Field2=1")]
        [TestCase("-ShortKey")]
        [TestCase("-ShortKey|123|-AnotherKey")]
        [TestCase("-ShortKey=123|-AnotherKey")]
        [TestCase("-ShortKey=123|-ShortKey|123")]
        [TestCase("-ShortKey=123|--Section:LongKey=123")]
        public void IllegalFormats(string input)
        {
            Dictionary<string, string> mappings = new Dictionary<string, string>();
            mappings["ShortKey"] = "Section:LongKey";
            mappings["AnotherKey"] = "Section:AnotherLongKey";

            Assert.Catch<CommandLineRuntimeOptions.ParseError>(() => CommandLineRuntimeOptions.Parse(input.Split('|'), mappings));
        }

        [TestCase("-ShortKey=shortValue")]
        [TestCase("-ShortKey|shortValue")]
        public void ShortForms(string input)
        {
            Dictionary<string, string> mappings = new Dictionary<string, string>();
            mappings["ShortKey"] = "Section:LongKey";
            CommandLineRuntimeOptions options = CommandLineRuntimeOptions.Parse(input.Split('|'), mappings);

            Assert.AreEqual(options.Definitions.Count, 1);
            Assert.AreEqual(options.Definitions["Section:LongKey"], "shortValue");
        }
    }
}
