using Metaplay.Core;
using Metaplay.Core.Config;
using NUnit.Framework;
using System.Collections.Generic;

namespace Cloud.Tests
{
    public class ObjectGraphDumpTests
    {
        public class TopLevel
        {
            public int Integer;
            public MyStruct Struct;
            public MyClass Class;
            public object Object;
            public List<MyClass> List;
            public OrderedDictionary<string, MyClass> Dict0;
            public OrderedDictionary<MyClass, string> Dict1;
            public GameConfigLibrary<string, DummyConfigInfo> Library;
        }

        public class DummyConfigInfo : IGameConfigData<string>
        {
            public string ConfigKey { get; set; }
        }

        public class MyClass
        {
            public string Str;
            public MyClass Other;

            public override string ToString() => $"({Str}, Other = ...)";
        }

        public struct MyStruct
        {
            public string Str;
        }

        TopLevel NewMiscTestObject()
        {
            return new TopLevel
            {
                Integer = 123,
                Struct = new MyStruct { Str = "s0" },
                Class = new MyClass { Str = "c0" },
                Object = "string as object",
                List = new List<MyClass>
                {
                    new MyClass { Str = "lc0" },
                    new MyClass { Str = null },
                    new MyClass { Str = "lc2" },
                },
                Dict0 = new OrderedDictionary<string, MyClass>
                {
                    { "d0key0", new MyClass { Str = "d0c0" } },
                    { "d0key1", new MyClass { Str = null } },
                    { "d0key2", new MyClass { Str = "d0c2" } },
                },
                Dict1 = new OrderedDictionary<MyClass, string>
                {
                    { new MyClass { Str = "d1c0" }, "d1value0" },
                    { new MyClass { Str = null }, "value1" },
                    { new MyClass { Str = "d1c2" }, "d1value2" },
                },
                Library = GameConfigLibrary<string, DummyConfigInfo>.CreateSolo(new OrderedDictionary<string, DummyConfigInfo>
                {
                    { "item0", new DummyConfigInfo{ ConfigKey = "item0" } },
                    { "item1", new DummyConfigInfo{ ConfigKey = "item1" } },
                    { "item2", new DummyConfigInfo{ ConfigKey = "item2" } },
                }),
            };
        }

        ObjectGraphDump.DumpOptions CreateDefaultDumpOptions()
        {
            return new ObjectGraphDump.DumpOptions(
                objectCountSafetyLimit: 1000,
                objectCollectionInitialCapacity: 0);
        }

        [Test]
        public void StringDump_Basic()
        {
            TopLevel obj = NewMiscTestObject();

            ObjectGraphDump.DumpResult dump = ObjectGraphDump.Dump(obj, CreateDefaultDumpOptions());
            string dumpString = ObjectGraphDump.DumpToString(dump, includeIdentityHashes: false).Replace("\r\n", "\n");

            Assert.AreEqual(
@"root <#0>
<#0>:
    type Cloud.Tests.ObjectGraphDumpTests.TopLevel
    field Integer: <#1>
    field Struct: <#2>
    field Class: <#3>
    field Object: <#4>
    field List: <#5>
    field Dict0: <#6>
    field Dict1: <#7>
    field Library: <#8>
<#1>:
    type System.Int32
    scalar 123
<#2>:
    type Cloud.Tests.ObjectGraphDumpTests.MyStruct
    field Str: <#9>
<#3>:
    type Cloud.Tests.ObjectGraphDumpTests.MyClass
    field Str: <#10>
    field Other: <#11>
<#4>:
    type System.String
    scalar string as object
<#5>:
    type System.Collections.Generic.List<Cloud.Tests.ObjectGraphDumpTests.MyClass>
    element at index 0: <#12>
    element at index 1: <#13>
    element at index 2: <#14>
<#6>:
    type Metaplay.Core.OrderedDictionary<System.String,Cloud.Tests.ObjectGraphDumpTests.MyClass>
    key at index 0: <#15>
    value at index 0: <#16>
    key at index 1: <#17>
    value at index 1: <#18>
    key at index 2: <#19>
    value at index 2: <#20>
<#7>:
    type Metaplay.Core.OrderedDictionary<Cloud.Tests.ObjectGraphDumpTests.MyClass,System.String>
    key at index 0: <#21>
    value at index 0: <#22>
    key at index 1: <#23>
    value at index 1: <#24>
    key at index 2: <#25>
    value at index 2: <#26>
<#8>:
    type Metaplay.Core.Config.GameConfigLibrary<System.String,Cloud.Tests.ObjectGraphDumpTests.DummyConfigInfo>
    key at index 0: <#27>
    value at index 0: <#28>
    key at index 1: <#29>
    value at index 1: <#30>
    key at index 2: <#31>
    value at index 2: <#32>
<#9>:
    type System.String
    scalar s0
<#10>:
    type System.String
    scalar c0
<#11>:
    null
<#12>:
    type Cloud.Tests.ObjectGraphDumpTests.MyClass
    field Str: <#33>
    field Other: <#11>
<#13>:
    type Cloud.Tests.ObjectGraphDumpTests.MyClass
    field Str: <#11>
    field Other: <#11>
<#14>:
    type Cloud.Tests.ObjectGraphDumpTests.MyClass
    field Str: <#34>
    field Other: <#11>
<#15>:
    type System.String
    scalar d0key0
<#16>:
    type Cloud.Tests.ObjectGraphDumpTests.MyClass
    field Str: <#35>
    field Other: <#11>
<#17>:
    type System.String
    scalar d0key1
<#18>:
    type Cloud.Tests.ObjectGraphDumpTests.MyClass
    field Str: <#11>
    field Other: <#11>
<#19>:
    type System.String
    scalar d0key2
<#20>:
    type Cloud.Tests.ObjectGraphDumpTests.MyClass
    field Str: <#36>
    field Other: <#11>
<#21>:
    type Cloud.Tests.ObjectGraphDumpTests.MyClass
    field Str: <#37>
    field Other: <#11>
<#22>:
    type System.String
    scalar d1value0
<#23>:
    type Cloud.Tests.ObjectGraphDumpTests.MyClass
    field Str: <#11>
    field Other: <#11>
<#24>:
    type System.String
    scalar value1
<#25>:
    type Cloud.Tests.ObjectGraphDumpTests.MyClass
    field Str: <#38>
    field Other: <#11>
<#26>:
    type System.String
    scalar d1value2
<#27>:
    type System.String
    scalar item0
<#28>:
    type Cloud.Tests.ObjectGraphDumpTests.DummyConfigInfo
    field ConfigKey: <#27>
<#29>:
    type System.String
    scalar item1
<#30>:
    type Cloud.Tests.ObjectGraphDumpTests.DummyConfigInfo
    field ConfigKey: <#29>
<#31>:
    type System.String
    scalar item2
<#32>:
    type Cloud.Tests.ObjectGraphDumpTests.DummyConfigInfo
    field ConfigKey: <#31>
<#33>:
    type System.String
    scalar lc0
<#34>:
    type System.String
    scalar lc2
<#35>:
    type System.String
    scalar d0c0
<#36>:
    type System.String
    scalar d0c2
<#37>:
    type System.String
    scalar d1c0
<#38>:
    type System.String
    scalar d1c2
",
                dumpString);
        }

        [Test]
        public void StringDump_Cyclic()
        {
            MyClass c0 = new MyClass { Str = "c0" };
            MyClass c1 = new MyClass { Str = "c1" };
            MyClass c2 = new MyClass { Str = "c2" };

            c0.Other = c1;
            c1.Other = c2;
            c2.Other = c1;

            TopLevel obj = new TopLevel
            {
                Class = c0,
            };

            ObjectGraphDump.DumpResult dump = ObjectGraphDump.Dump(obj, CreateDefaultDumpOptions());
            string dumpString = ObjectGraphDump.DumpToString(dump, includeIdentityHashes: false).Replace("\r\n", "\n");

            Assert.AreEqual(
@"root <#0>
<#0>:
    type Cloud.Tests.ObjectGraphDumpTests.TopLevel
    field Integer: <#1>
    field Struct: <#2>
    field Class: <#3>
    field Object: <#4>
    field List: <#4>
    field Dict0: <#4>
    field Dict1: <#4>
    field Library: <#4>
<#1>:
    type System.Int32
    scalar 0
<#2>:
    type Cloud.Tests.ObjectGraphDumpTests.MyStruct
    field Str: <#4>
<#3>:
    type Cloud.Tests.ObjectGraphDumpTests.MyClass
    field Str: <#5>
    field Other: <#6>
<#4>:
    null
<#5>:
    type System.String
    scalar c0
<#6>:
    type Cloud.Tests.ObjectGraphDumpTests.MyClass
    field Str: <#7>
    field Other: <#8>
<#7>:
    type System.String
    scalar c1
<#8>:
    type Cloud.Tests.ObjectGraphDumpTests.MyClass
    field Str: <#9>
    field Other: <#6>
<#9>:
    type System.String
    scalar c2
",
                dumpString);
        }

        public class MyClassWithProperty
        {
            public string MyProperty { get; set; } = "abc";
            string MyPrivateProperty { get; set; } = "def";
        }

        [Test]
        public void StringDump_PropertyBackingField()
        {
            MyClassWithProperty obj = new MyClassWithProperty();

            ObjectGraphDump.DumpResult dump = ObjectGraphDump.Dump(obj, CreateDefaultDumpOptions());
            string dumpString = ObjectGraphDump.DumpToString(dump, includeIdentityHashes: false).Replace("\r\n", "\n");

            Assert.AreEqual(
@"root <#0>
<#0>:
    type Cloud.Tests.ObjectGraphDumpTests.MyClassWithProperty
    field MyProperty: <#1>
    field MyPrivateProperty: <#2>
<#1>:
    type System.String
    scalar abc
<#2>:
    type System.String
    scalar def
",
                dumpString);

        }

        [Test]
        public void Compare_Basic_Equal()
        {
            TopLevel obj = NewMiscTestObject();

            ObjectGraphDump.DumpResult dumpBefore = ObjectGraphDump.Dump(obj, CreateDefaultDumpOptions());
            ObjectGraphDump.DumpResult dumpAfter = ObjectGraphDump.Dump(obj, CreateDefaultDumpOptions());

            ObjectGraphDump.ComparisonResult result = ObjectGraphDump.CompareDumpResults(dumpBefore, dumpAfter);

            Assert.AreEqual(true, result.DumpsAreEqual);
            Assert.AreEqual("Dumps are equal", result.Description);
        }

        [Test]
        public void Compare_Basic_DifferentInteger()
        {
            TopLevel obj = NewMiscTestObject();

            ObjectGraphDump.DumpResult dumpBefore = ObjectGraphDump.Dump(obj, CreateDefaultDumpOptions());

            obj.Integer = 456;

            ObjectGraphDump.DumpResult dumpAfter = ObjectGraphDump.Dump(obj, CreateDefaultDumpOptions());

            ObjectGraphDump.ComparisonResult result = ObjectGraphDump.CompareDumpResults(dumpBefore, dumpAfter);

            Assert.AreEqual(false, result.DumpsAreEqual);
            Assert.AreEqual("Values differ: 123 vs 456. Path: .Integer (ObjectGraphDump handle path: 0 -> 1)", result.Description);
        }

        [Test]
        public void Compare_Basic_DifferentStructMember()
        {
            TopLevel obj = NewMiscTestObject();

            ObjectGraphDump.DumpResult dumpBefore = ObjectGraphDump.Dump(obj, CreateDefaultDumpOptions());

            obj.Struct.Str = "changed";

            ObjectGraphDump.DumpResult dumpAfter = ObjectGraphDump.Dump(obj, CreateDefaultDumpOptions());

            ObjectGraphDump.ComparisonResult result = ObjectGraphDump.CompareDumpResults(dumpBefore, dumpAfter);

            Assert.AreEqual(false, result.DumpsAreEqual);
            Assert.AreEqual("Values differ: s0 vs changed. Path: .Struct.Str (ObjectGraphDump handle path: 0 -> 2 -> 9)", result.Description);
        }

        [Test]
        public void Compare_Basic_DifferentClassMember()
        {
            TopLevel obj = NewMiscTestObject();

            ObjectGraphDump.DumpResult dumpBefore = ObjectGraphDump.Dump(obj, CreateDefaultDumpOptions());

            obj.Class.Str = "changed";

            ObjectGraphDump.DumpResult dumpAfter = ObjectGraphDump.Dump(obj, CreateDefaultDumpOptions());

            ObjectGraphDump.ComparisonResult result = ObjectGraphDump.CompareDumpResults(dumpBefore, dumpAfter);

            Assert.AreEqual(false, result.DumpsAreEqual);
            Assert.AreEqual("Values differ: c0 vs changed. Path: .Class.Str (ObjectGraphDump handle path: 0 -> 3 -> 10)", result.Description);
        }

        [Test]
        public void Compare_Basic_DifferentObjectType()
        {
            TopLevel obj = NewMiscTestObject();

            ObjectGraphDump.DumpResult dumpBefore = ObjectGraphDump.Dump(obj, CreateDefaultDumpOptions());

            obj.Object = 42;

            ObjectGraphDump.DumpResult dumpAfter = ObjectGraphDump.Dump(obj, CreateDefaultDumpOptions());

            ObjectGraphDump.ComparisonResult result = ObjectGraphDump.CompareDumpResults(dumpBefore, dumpAfter);

            Assert.AreEqual(false, result.DumpsAreEqual);
            Assert.AreEqual("Types differ: System.String vs System.Int32. Path: .Object (ObjectGraphDump handle path: 0 -> 4)", result.Description);
        }

        [Test]
        public void Compare_Basic_ObjectChangedToNull()
        {
            MyClass obj = new MyClass { Str = "abc", Other = new MyClass{ } };

            ObjectGraphDump.DumpResult dumpBefore = ObjectGraphDump.Dump(obj, CreateDefaultDumpOptions());

            obj.Str = null;

            ObjectGraphDump.DumpResult dumpAfter = ObjectGraphDump.Dump(obj, CreateDefaultDumpOptions());

            ObjectGraphDump.ComparisonResult result = ObjectGraphDump.CompareDumpResults(dumpBefore, dumpAfter);

            Assert.AreEqual(false, result.DumpsAreEqual);
            Assert.AreEqual("Types differ: System.String vs <null>. Path: .Str (ObjectGraphDump handle path: 0 -> 1)", result.Description);
        }

        [Test]
        public void Compare_Basic_ObjectChangedFromNull()
        {
            MyClass obj = new MyClass { Str = null, Other = new MyClass{ } };

            ObjectGraphDump.DumpResult dumpBefore = ObjectGraphDump.Dump(obj, CreateDefaultDumpOptions());

            obj.Str = "abc";

            ObjectGraphDump.DumpResult dumpAfter = ObjectGraphDump.Dump(obj, CreateDefaultDumpOptions());

            ObjectGraphDump.ComparisonResult result = ObjectGraphDump.CompareDumpResults(dumpBefore, dumpAfter);

            Assert.AreEqual(false, result.DumpsAreEqual);
            Assert.AreEqual("Types differ: <null> vs System.String. Path: .Str (ObjectGraphDump handle path: 0 -> 1)", result.Description);
        }

        [Test]
        public void Compare_Basic_DifferentClassIdentity()
        {
            TopLevel obj = NewMiscTestObject();

            ObjectGraphDump.DumpResult dumpBefore = ObjectGraphDump.Dump(obj, CreateDefaultDumpOptions());

            obj.Class = new MyClass { Str = obj.Class.Str, Other = obj.Class.Other };

            ObjectGraphDump.DumpResult dumpAfter = ObjectGraphDump.Dump(obj, CreateDefaultDumpOptions());

            ObjectGraphDump.ComparisonResult result = ObjectGraphDump.CompareDumpResults(dumpBefore, dumpAfter);

            Assert.AreEqual(false, result.DumpsAreEqual);
            Assert.AreEqual("Object identities differ. Path: .Class (ObjectGraphDump handle path: 0 -> 3)", result.Description);
        }

        [Test]
        public void Compare_Basic_DifferentListElement()
        {
            TopLevel obj = NewMiscTestObject();

            ObjectGraphDump.DumpResult dumpBefore = ObjectGraphDump.Dump(obj, CreateDefaultDumpOptions());

            obj.List[2].Str = "changed";

            ObjectGraphDump.DumpResult dumpAfter = ObjectGraphDump.Dump(obj, CreateDefaultDumpOptions());

            ObjectGraphDump.ComparisonResult result = ObjectGraphDump.CompareDumpResults(dumpBefore, dumpAfter);

            Assert.AreEqual(false, result.DumpsAreEqual);
            Assert.AreEqual("Values differ: lc2 vs changed. Path: .List[2].Str (ObjectGraphDump handle path: 0 -> 5 -> 14 -> 34)", result.Description);
        }

        [Test]
        public void Compare_Basic_DifferentDictKeyMember()
        {
            TopLevel obj = NewMiscTestObject();

            ObjectGraphDump.DumpResult dumpBefore = ObjectGraphDump.Dump(obj, CreateDefaultDumpOptions());

            obj.Dict1.Keys.ElementAt(2).Str = "changed";

            ObjectGraphDump.DumpResult dumpAfter = ObjectGraphDump.Dump(obj, CreateDefaultDumpOptions());

            ObjectGraphDump.ComparisonResult result = ObjectGraphDump.CompareDumpResults(dumpBefore, dumpAfter);

            Assert.AreEqual(false, result.DumpsAreEqual);
            Assert.AreEqual("Values differ: d1c2 vs changed. Path: .Dict1.Keys[2].Str (ObjectGraphDump handle path: 0 -> 7 -> 25 -> 38)", result.Description);
        }

        [Test]
        public void Compare_Basic_DifferentDictValue()
        {
            TopLevel obj = NewMiscTestObject();

            ObjectGraphDump.DumpResult dumpBefore = ObjectGraphDump.Dump(obj, CreateDefaultDumpOptions());

            obj.Dict1[obj.Dict1.Keys.ElementAt(2)] = "changed";

            ObjectGraphDump.DumpResult dumpAfter = ObjectGraphDump.Dump(obj, CreateDefaultDumpOptions());

            ObjectGraphDump.ComparisonResult result = ObjectGraphDump.CompareDumpResults(dumpBefore, dumpAfter);

            Assert.AreEqual(false, result.DumpsAreEqual);
            Assert.AreEqual("Values differ: d1value2 vs changed. Path: .Dict1[(d1c2, Other = ...)] (ObjectGraphDump handle path: 0 -> 7 -> 26)", result.Description);
        }

        [Test]
        public void Compare_Basic_DifferentListCount_Increased()
        {
            TopLevel obj = NewMiscTestObject();

            ObjectGraphDump.DumpResult dumpBefore = ObjectGraphDump.Dump(obj, CreateDefaultDumpOptions());

            obj.List.Add(new MyClass { Str = "added" });

            ObjectGraphDump.DumpResult dumpAfter = ObjectGraphDump.Dump(obj, CreateDefaultDumpOptions());

            ObjectGraphDump.ComparisonResult result = ObjectGraphDump.CompareDumpResults(dumpBefore, dumpAfter);

            Assert.AreEqual(false, result.DumpsAreEqual);
            Assert.AreEqual("Child counts differ: 3 vs 4. Path: .List (ObjectGraphDump handle path: 0 -> 5)", result.Description);
        }

        [Test]
        public void Compare_Basic_DifferentListCount_Decreased()
        {
            TopLevel obj = NewMiscTestObject();

            ObjectGraphDump.DumpResult dumpBefore = ObjectGraphDump.Dump(obj, CreateDefaultDumpOptions());

            obj.List.RemoveAt(obj.List.Count-1);

            ObjectGraphDump.DumpResult dumpAfter = ObjectGraphDump.Dump(obj, CreateDefaultDumpOptions());

            ObjectGraphDump.ComparisonResult result = ObjectGraphDump.CompareDumpResults(dumpBefore, dumpAfter);

            Assert.AreEqual(false, result.DumpsAreEqual);
            Assert.AreEqual("Child counts differ: 3 vs 2. Path: .List (ObjectGraphDump handle path: 0 -> 5)", result.Description);
        }

        [Test]
        public void Compare_Basic_DifferentChildHandles()
        {
            TopLevel obj = NewMiscTestObject();

            ObjectGraphDump.DumpResult dumpBefore = ObjectGraphDump.Dump(obj, CreateDefaultDumpOptions());

            obj.List[0] = obj.Class;

            ObjectGraphDump.DumpResult dumpAfter = ObjectGraphDump.Dump(obj, CreateDefaultDumpOptions());

            ObjectGraphDump.ComparisonResult result = ObjectGraphDump.CompareDumpResults(dumpBefore, dumpAfter);

            Assert.AreEqual(false, result.DumpsAreEqual);
            Assert.AreEqual("Child [element at index 0] object handles differ: 12 vs 3. Path: .List (ObjectGraphDump handle path: 0 -> 5)", result.Description);
        }

        [Test]
        public void Compare_Property()
        {
            MyClassWithProperty obj = new MyClassWithProperty();

            ObjectGraphDump.DumpResult dumpBefore = ObjectGraphDump.Dump(obj, CreateDefaultDumpOptions());

            obj.MyProperty = "changed";

            ObjectGraphDump.DumpResult dumpAfter = ObjectGraphDump.Dump(obj, CreateDefaultDumpOptions());

            ObjectGraphDump.ComparisonResult result = ObjectGraphDump.CompareDumpResults(dumpBefore, dumpAfter);

            Assert.AreEqual(false, result.DumpsAreEqual);
            Assert.AreEqual("Values differ: abc vs changed. Path: .MyProperty (ObjectGraphDump handle path: 0 -> 1)", result.Description);
        }
    }
}
