// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.RuntimeOptions;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Cloud.Tests
{
    [RuntimeOptions("Test", isStatic: false)]
    class TestOptions : RuntimeOptionsBase
    {
        public class NestedType
        {
            public int NestedIntValue { get; private set; }
        }

        public int IntValue { get; private set; }
        public DateTime DateTimeValue { get; private set; }
        public NestedType NestedValue { get; private set; }

        [ComputedValue]
        public int ComputedInt { get; private set; }

        public int[] IntArr { get; private set; } = new int[] { 10 };
        public List<int> IntList { get; private set; } = new List<int>() { 10 };
        public Dictionary<string, int> StringIntDict { get; private set; } = new Dictionary<string, int>() { { "Init", 100 } };

        public class DictValue
        {
            public int IntValueA { get; private set; } = 10;
            public int IntValueB { get; private set; } = 10;
        }
        public Dictionary<string, DictValue> StringObjDict { get; private set; } = new Dictionary<string, DictValue>();

        public int? NullableInt { get; private set; } = 100;
        public string NullableString { get; private set; } = "init";
    }

    public class YamlRuntimeOptionsTestUtil
    {
        public static (T Options, RuntimeOptionsBinder.BindingResults Results) BindYaml<T>(string value, bool strictBindingChecks = true, bool throwOnError = true) where T : RuntimeOptionsBase
        {
            List<RuntimeOptionsBinder.RuntimeOptionDefinition> definitions = new List<RuntimeOptionsBinder.RuntimeOptionDefinition>()
            {
                new RuntimeOptionsBinder.RuntimeOptionDefinition(typeof(T), typeof(T).GetCustomAttribute<RuntimeOptionsAttribute>().SectionName)
            };
            RuntimeOptionsSourceSet sources = new RuntimeOptionsSourceSet();
            foreach (string part in value.Split("----"))
                sources.AddSource(new RuntimeOptionsSourceSet.SourceDeclaration("<inline>", "Yaml source", strictBindingChecks, (builder) => builder.AddYaml(part)));

            return BindYamlFromSources<T>(definitions, sources, throwOnError);
        }

        public static (T Options, RuntimeOptionsBinder.BindingResults Results) BindYamlFromSources<T>(List<RuntimeOptionsBinder.RuntimeOptionDefinition> definitions, RuntimeOptionsSourceSet sources, bool throwOnError = true) where T : RuntimeOptionsBase
        {
            RuntimeOptionsBinder.BindingResults results = RuntimeOptionsBinder.BindToRuntimeOptions(definitions, sources);

            if (results.Errors.Length > 0 && throwOnError)
            {
                foreach (var w in results.Warnings)
                    Console.WriteLine(w);

                throw new AggregateException(results.Errors);
            }

            foreach (Exception e in results.Errors)
                Console.WriteLine(e);
            foreach (RuntimeOptionsBinder.Warning w in results.Warnings)
                Console.WriteLine(w);

            return ((T)results.Sections[typeof(T)].Options, results);
        }
    }

    class YamlRuntimeOptionsTests
    {
        static (TestOptions Options, RuntimeOptionsBinder.BindingResults Results) BindYaml(string value, bool strictBindingChecks = true, bool throwOnError = true)
        {
            return YamlRuntimeOptionsTestUtil.BindYaml<TestOptions>(value, strictBindingChecks, throwOnError);
        }

        [TestCase(@"
Test:
    IntValue: abc
")]
        [TestCase(@"
Test:
    MissingField: abc
")]
[TestCase(@"
Test: bad-value
")]
[TestCase(@"
TestNoSuchEntry:
    MissingField: abc
")]
        [TestCase(@"
Test:
    NestedValue:
        NestedIntValue: abc
")]
        [TestCase(@"
Test:
    NestedValue:
        - 55
")]
        [TestCase(@"
Test:
    ComputedInt: 10
")]
        public static void TestInvalid(string yaml)
        {
            RuntimeOptionsBinder.BindingResults results = BindYaml(yaml, throwOnError: false).Results;
            Assert.IsTrue(results.Errors.Length > 0);
        }

        [Test]
        public static void TestScalar()
        {
            TestOptions options;

            options = BindYaml(@"
Test:
    IntValue: 12
").Options;
            Assert.AreEqual(12, options.IntValue);

            options = BindYaml(@"
Test:
    DateTimeValue: 2020-02-23T12:00:11Z
").Options;
            Assert.AreEqual(new DateTime(2020, 02, 23, 12, 00, 11, 0, DateTimeKind.Utc), options.DateTimeValue);

            options = BindYaml(@"
Test:
    NestedValue:
        NestedIntValue: 123
").Options;
            Assert.AreEqual(123, options.NestedValue.NestedIntValue);
        }

        [Test]
        public static void TestCollection()
        {
            TestOptions options = BindYaml(@"
Test:
    IntList:
        - 20
        - 21
        - 22
    IntArr:
        - 20
        - 21
        - 22
    StringIntDict:
        Foo: 1
    StringObjDict:
        Foo:
            IntValueA: 1
            IntValueB: 2
").Options;
            Assert.AreEqual(new int[] {20, 21, 22}, options.IntList.ToArray());
            Assert.AreEqual(new int[] {20, 21, 22}, options.IntArr);
            Assert.IsTrue(options.StringIntDict["Foo"] == 1);
            Assert.IsTrue(options.StringIntDict["Init"] == 100);
            Assert.IsTrue(options.StringObjDict["Foo"].IntValueA == 1);
            Assert.IsTrue(options.StringObjDict["Foo"].IntValueB == 2);

            options = BindYaml(@"
Test:
    IntList: [20, 21, 22]
    IntArr: [20, 21, 22]
    StringIntDict: { ""Foo"": 1 }
").Options;
            Assert.AreEqual(new int[] {20, 21, 22}, options.IntList.ToArray());
            Assert.AreEqual(new int[] {20, 21, 22}, options.IntArr);
            Assert.IsTrue(options.StringIntDict["Foo"] == 1);
            Assert.IsTrue(options.StringIntDict["Init"] == 100);
        }

        [Test]
        public static void TestCombinedScalar()
        {
            TestOptions options = BindYaml(@"
Test:
    IntValue: 1
----
Test:
    IntValue: 2
").Options;
            Assert.AreEqual(2, options.IntValue);
        }

        [Test]
        public static void TestCombinedArray()
        {
            TestOptions options = options = BindYaml(@"
Test:
    IntList: [20, 21, 22]
    IntArr: [20, 21, 22]
----
Test:
    IntList: [30, 31]
    IntArr: [30, 31]
").Options;
            Assert.AreEqual(new int[] {30, 31}, options.IntList.ToArray());
            Assert.AreEqual(new int[] {30, 31}, options.IntArr);
        }

        [Test]
        public static void TestCombinedDictionary()
        {
            TestOptions options = BindYaml(@"
Test:
    StringObjDict:
        Foo:
            IntValueA: 1
----
Test:
    StringObjDict:
        Foo:
            IntValueB: 2
").Options;
            Assert.IsTrue(options.StringObjDict["Foo"].IntValueA == 1);
            Assert.IsTrue(options.StringObjDict["Foo"].IntValueB == 2);
        }

        [Test]
        public static void TestCombinedNullTopLevelEntry()
        {
            (TestOptions options, RuntimeOptionsBinder.BindingResults results) = BindYaml(@"
Test:
    IntValue: 1
----
Test:
", strictBindingChecks: false);
            Assert.IsTrue(options.IntValue == 1);
            Assert.IsTrue(results.Warnings.Length > 0);

            (options, results) = BindYaml(@"
Test:
----
Test:
    IntValue: 1
", strictBindingChecks: false);
            Assert.IsTrue(options.IntValue == 1);
            Assert.IsTrue(results.Warnings.Length > 0);
        }

        [Test]
        public static void TestCombinedNullNestedEntry()
        {
            (TestOptions options, RuntimeOptionsBinder.BindingResults results) = BindYaml(@"
Test:
    NestedValue:
        NestedIntValue: 123
----
Test:
    NestedValue:
", strictBindingChecks: false);
            Assert.IsTrue(options.NestedValue.NestedIntValue == 123);
            Assert.IsTrue(results.Warnings.Length > 0);

            (options, results) = BindYaml(@"
Test:
    NestedValue:
----
Test:
    NestedValue:
        NestedIntValue: 123
", strictBindingChecks: false);
            Assert.IsTrue(options.NestedValue.NestedIntValue == 123);
            Assert.IsTrue(results.Warnings.Length > 0);
        }

        [Test]
        public static void TestCombinedNullList()
        {
            (TestOptions options, RuntimeOptionsBinder.BindingResults results) = BindYaml(@"
Test:
    IntList:
        - 20
        - 21
        - 22
----
Test:
    IntList:
", strictBindingChecks: false);
            Assert.AreEqual(new int[] {20, 21, 22}, options.IntList.ToArray());

            (options, results) = BindYaml(@"
Test:
    IntList:
----
Test:
    IntList:
        - 20
        - 21
        - 22
", strictBindingChecks: false);
            Assert.AreEqual(new int[] {20, 21, 22}, options.IntList.ToArray());
        }

        [Test]
        public static void TestCombinedIgnoreNullEntryDict()
        {
            (TestOptions options, RuntimeOptionsBinder.BindingResults results) = BindYaml(@"
Test:
    StringObjDict:
        Foo:
            IntValueA: 1
----
Test:
    StringObjDict:
        Foo:
", strictBindingChecks: false);
            Assert.IsTrue(options.StringObjDict["Foo"].IntValueA == 1);
            Assert.IsTrue(results.Warnings.Length > 0);

            (options, results) = BindYaml(@"
Test:
    StringObjDict:
        Foo:
----
Test:
    StringObjDict:
        Foo:
            IntValueA: 1
", strictBindingChecks: false);
            Assert.IsTrue(options.StringObjDict["Foo"].IntValueA == 1);
            Assert.IsTrue(results.Warnings.Length > 0);
        }

        [Test]
        public static void TestNullable()
        {
            TestOptions options;

            options = BindYaml(@"
Test:
    NullableInt:
").Options;
            Assert.IsTrue(options.NullableInt.HasValue == false);

            options = BindYaml(@"
Test:
    NullableInt: null
").Options;
            Assert.IsTrue(options.NullableInt.HasValue == false);

            options = BindYaml(@"
Test:
    NullableInt: ~
").Options;
            Assert.IsTrue(options.NullableInt.HasValue == false);

            options = BindYaml(@"
Test:
    NullableInt: 123
").Options;
            Assert.IsTrue(options.NullableInt == 123);

            options = BindYaml(@"
Test:
    NullableString:
").Options;
            Assert.IsTrue(options.NullableString == "");

            options = BindYaml(@"
Test:
    NullableString: null
").Options;
            Assert.IsTrue(options.NullableString == null);

            options = BindYaml(@"
Test:
    NullableString: ~
").Options;
            Assert.IsTrue(options.NullableString == null);

            options = BindYaml(@"
Test:
    NullableString: ""null""
").Options;
            Assert.IsTrue(options.NullableString == "null");

            options = BindYaml(@"
Test:
    StringObjDict:
        Foo: ~
").Options;
            Assert.IsTrue(options.StringObjDict["Foo"] == null);

            options = BindYaml(@"
Test:
    StringObjDict:
        Foo:
            IntValueA: 1
----
Test:
    StringObjDict:
        Foo: ~
").Options;
            Assert.IsTrue(options.StringObjDict["Foo"] == null);
        }


        [Test]
        public static void TestMismatchCase()
        {
            TestOptions options;
            RuntimeOptionsBinder.BindingResults results;

            (options, results) = BindYaml(@"
Test:
    INTVALUE: 12
");
            Assert.AreEqual(12, options.IntValue);
            Assert.IsTrue(results.Warnings.Length > 0);

            (options, results) = BindYaml(@"
TEST:
    IntValue: 12
");
            Assert.AreEqual(12, options.IntValue);
            Assert.IsTrue(results.Warnings.Length > 0);
        }
    }
}
