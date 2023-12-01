// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.CloudCoreTestsIntegrationTypes;
using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Localization;
using Metaplay.Core.Math;
using Metaplay.Core.Model;
using Metaplay.Core.Serialization;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using TestUser.CloudCoreTestsIntegrationTypes;
using static System.FormattableString;

[MetaSerializable]
#pragma warning disable CA1050 // Declare types in namespaces
public class TestCustomTypeWithNoNS
#pragma warning restore CA1050 // Declare types in namespaces
{
    [MetaMember(1)] public int Foo = 1;

    public override bool Equals(object obj) => obj is TestCustomTypeWithNoNS other && Foo == other.Foo;
    public override int GetHashCode() => throw new NotImplementedException();
};

namespace Cloud.Tests
{
    // NameQualificationTestNamespace0 and NameQualificationTestNamespace1
    // contain different types with the same name, which should be OK as
    // the generated serializer should use namespace-qualified names.
    namespace NameQualificationTestNamespace0
    {
        [MetaSerializable]
        public class NameQualificationTestType
        {
            [MetaMember(1)] public int SameNameField;
            [MetaMember(2)] public int DifferentNameField0;
        }
    }
    namespace NameQualificationTestNamespace1
    {
        [MetaSerializable]
        public class NameQualificationTestType
        {
            [MetaMember(1)] public string SameNameField;
            [MetaMember(2)] public string DifferentNameField1;
        }
    }

    [MetaSerializable]
    public class TestSortedSet
    {
        [MetaMember(1)] public SortedSet<int> SortedSet { get; set; }
    }

    [MetaSerializable]
    public class TestGlobalState
    {
        [MetaMember(2)] public int                                          RunningBroadcastMessageId   { get; set; } = 1;
        [MetaMember(6), Transient] public Dictionary<string, ContentHash>   GameConfigVersions          { get; set; } = new Dictionary<string, ContentHash>();
    }

    [MetaSerializable]
    public class TestGlobalStateOverrideCollectionSize
    {
        [MetaMember(2)]                                      public int                             RunningBroadcastMessageId { get; set; } = 1;
        [MetaMember(6), Transient, MaxCollectionSize(18000)] public Dictionary<string, ContentHash> GameConfigVersions        { get; set; } = new Dictionary<string, ContentHash>();
        [MetaMember(7), Transient, MaxCollectionSize(100)] public List<List<int>>              Nested                    { get; set; } = new List<List<int>>();
    }

    [MetaSerializable]
    public class TestVersionedState
    {
        [MetaMember(1), AddedInVersion(1)]
        public int AddedMember;

        [MetaMember(2), RemovedInVersion(2)]
        public int RemovedMember;

        [MetaMember(3), AddedInVersion(1), RemovedInVersion(2)]
        public int AddedAndRemoved;
    }

    [MetaSerializable]
    public class TestCustomType
    {
        [MetaMember(1)] public int Foo;

        public override bool Equals(object obj) => obj is TestCustomType other && Foo == other.Foo;
        public override int GetHashCode() => throw new NotImplementedException();
    };

    public interface ITestInterfaceNonSerializable
    {
        int Bar();
    }

    public abstract class TestAbstractNonSerializable
    {
        public int Dummy;
    }

    [MetaSerializable]
    public abstract class TestAbstract
    {
        [MetaMember(1)] public int FieldInBase;
    }

    [MetaSerializableDerived(1)]
    public class TestDerivedA : TestAbstract, ITestInterfaceNonSerializable // \note Test that having additional non-serializable interface is OK
    {
        [MetaMember(2)] public int FieldInDerived;

        public int Bar() => 1;

        public TestDerivedA()
        {
        }

        public TestDerivedA(int fieldInBase, int fieldInDerived)
        {
            FieldInBase    = fieldInBase;
            FieldInDerived = fieldInDerived;
        }

        public override bool Equals(object obj)
        {
            if (!(obj is TestDerivedA other))
                return false;

            return FieldInBase == other.FieldInBase
                && FieldInDerived == other.FieldInDerived;
        }

        public override string ToString()
        {
            return Invariant($"({FieldInBase}, {FieldInDerived})");
        }

        public override int GetHashCode() => (FieldInBase + FieldInDerived).GetHashCode();
    }

    public abstract class TestAbstractIntermediateInBaseChain : TestAbstract
    {
    };

    [MetaSerializableDerived(2)]
    public class TestDerivedB : TestAbstractIntermediateInBaseChain
    {
        [MetaMember(2)] public int FieldInDerived;

        public TestDerivedB()
        {
        }

        public TestDerivedB(int fieldInBase, int fieldInDerived)
        {
            FieldInBase    = fieldInBase;
            FieldInDerived = fieldInDerived;
        }

        public override bool Equals(object obj)
        {
            if (!(obj is TestDerivedB other))
                return false;

            return FieldInBase == other.FieldInBase
                && FieldInDerived == other.FieldInDerived;
        }

        public override string ToString()
        {
            return Invariant($"({FieldInBase}, {FieldInDerived})");
        }

        public override int GetHashCode() => (FieldInBase + FieldInDerived).GetHashCode();
    }

    [MetaSerializable]
    public interface ITestInterface
    {
        int Func();
    }

    [MetaSerializableDerived(1)]
    public class TestInterfaceImplementationA : TestAbstractNonSerializable, ITestInterface, ITestInterfaceNonSerializable // \note Test that having additional non-serializable base and interface is OK
    {
        [MetaMember(1)] public int Field;

        public int Func() => throw new NotImplementedException();
        public int Bar() => throw new NotImplementedException();

        TestInterfaceImplementationA() { }
        public TestInterfaceImplementationA (int x) { Field = x; }

        public override bool Equals(object obj) => obj is TestInterfaceImplementationA other && Field == other.Field;
        public override int GetHashCode() => Field.GetHashCode();
    }

    [MetaSerializableDerived(2)]
    public class TestInterfaceImplementationB : ITestInterface
    {
        [MetaMember(1)] public int Field;

        public int Func() => throw new NotImplementedException();

        TestInterfaceImplementationB() { }
        public TestInterfaceImplementationB (int x) { Field = x; }

        public override bool Equals(object obj) => obj is TestInterfaceImplementationB other && Field == other.Field;
        public override int GetHashCode() => Field.GetHashCode();
    }

    public abstract class TestInterfaceIntermediateClass : ITestInterface
    {
        public abstract int Func();
    }

    [MetaSerializableDerived(3)]
    public class TestInterfaceImplementationC : TestInterfaceIntermediateClass
    {
        [MetaMember(1)] public int Field;

        public override int Func() => throw new NotImplementedException();

        TestInterfaceImplementationC() { }
        public TestInterfaceImplementationC (int x) { Field = x; }

        public override bool Equals(object obj) => obj is TestInterfaceImplementationC other && Field == other.Field;
        public override int GetHashCode() => Field.GetHashCode();
    }

    public interface ITestInterfaceIntermediateInterface : ITestInterface
    {
    }

    [MetaSerializableDerived(4)]
    public class TestInterfaceImplementationD : ITestInterfaceIntermediateInterface
    {
        [MetaMember(1)] public int Field;

        public int Func() => throw new NotImplementedException();

        TestInterfaceImplementationD() { }
        public TestInterfaceImplementationD (int x) { Field = x; }

        public override bool Equals(object obj) => obj is TestInterfaceImplementationD other && Field == other.Field;
        public override int GetHashCode() => Field.GetHashCode();
    }

    public abstract class TestModelActionBase : ModelAction
    {
        public TestModelActionBase() { }
    }

    [MetaSerializable]
    public class TestModelActionPayload
    {
        [MetaMember(100)] public int        Id      { get; set; }
        [MetaMember(101)] public MetaTime   Time    { get; set; }

        public TestModelActionPayload() { }
        public TestModelActionPayload(MetaTime time) { Time = time; }
    };

    [ModelActionExecuteFlags(ModelActionExecuteFlags.LeaderSynchronized)]
    [ModelAction(0x7F000001)]
    public class TestModelAction : TestModelActionBase
    {
        public TestModelActionPayload Payload { get; private set; }

        public TestModelAction() { }
        public TestModelAction(TestModelActionPayload payload) { Payload = payload; }

        public override MetaActionResult InvokeExecute(IModel model, bool commit)
        {
            throw new NotImplementedException();
        }
    }

    [MetaSerializable]
    public struct TestValueStruct
    {
        [MetaMember(100)] public int Member { get; set; }
    }

    [MetaSerializable]
    public class TestSimpleWithAccessModifiers
    {
        [MetaMember(1)] public int PublicField;
        [MetaMember(2)] protected int ProtectedField;
        [MetaMember(3)] private int PrivateField;
        [MetaMember(4)] internal int InternalField;
        [MetaMember(5)] public int PublicProperty { get; set; }
        [MetaMember(6)] protected int ProtectedProperty { get; set; }
        [MetaMember(7)] private int PrivateProperty { get; set; }
        [MetaMember(8)] internal int InternalProperty { get; set; }
        [MetaMember(9)] public int PublicProperty_ProtectedGetter { protected get; set; }
        [MetaMember(10)] public int PublicProperty_PrivateGetter { private get; set; }
        [MetaMember(11)] public int PublicProperty_InternalGetter { internal get; set; }
        [MetaMember(12)] protected int ProtectedProperty_PrivateGetter { private get; set; }
        [MetaMember(13)] internal int InternalProperty_PrivateGetter { private get; set; }
        [MetaMember(14)] public int PublicProperty_ProtectedSetter { get; protected set; }
        [MetaMember(15)] public int PublicProperty_PrivateSetter { get; private set; }
        [MetaMember(16)] public int PublicProperty_InternalSetter { get; internal set; }
        [MetaMember(17)] protected int ProtectedProperty_PrivateSetter { get; private set; }
        [MetaMember(18)] internal int InternalProperty_PrivateSetter { get; private set; }

        public TestSimpleWithAccessModifiers(){ }
        public TestSimpleWithAccessModifiers(bool _)
        {
            PublicField = 10;
            ProtectedField = 20;
            PrivateField = 30;
            InternalField = 40;
            PublicProperty = 50;
            ProtectedProperty = 60;
            PrivateProperty = 70;
            InternalProperty = 80;
            PublicProperty_ProtectedGetter = 90;
            PublicProperty_PrivateGetter = 100;
            PublicProperty_InternalGetter = 110;
            ProtectedProperty_PrivateGetter = 120;
            InternalProperty_PrivateGetter = 130;
            PublicProperty_ProtectedSetter = 140;
            PublicProperty_PrivateSetter = 150;
            PublicProperty_InternalSetter = 160;
            ProtectedProperty_PrivateSetter = 170;
            InternalProperty_PrivateSetter = 180;
        }

        public void AssertEqual(TestSimpleWithAccessModifiers expected)
        {
            Assert.AreEqual(PublicField, expected.PublicField);
            Assert.AreEqual(ProtectedField, expected.ProtectedField);
            Assert.AreEqual(PrivateField, expected.PrivateField);
            Assert.AreEqual(InternalField, expected.InternalField);
            Assert.AreEqual(PublicProperty, expected.PublicProperty);
            Assert.AreEqual(ProtectedProperty, expected.ProtectedProperty);
            Assert.AreEqual(PrivateProperty, expected.PrivateProperty);
            Assert.AreEqual(InternalProperty, expected.InternalProperty);
            Assert.AreEqual(PublicProperty_ProtectedGetter, expected.PublicProperty_ProtectedGetter);
            Assert.AreEqual(PublicProperty_PrivateGetter, expected.PublicProperty_PrivateGetter);
            Assert.AreEqual(PublicProperty_InternalGetter, expected.PublicProperty_InternalGetter);
            Assert.AreEqual(ProtectedProperty_PrivateGetter, expected.ProtectedProperty_PrivateGetter);
            Assert.AreEqual(InternalProperty_PrivateGetter, expected.InternalProperty_PrivateGetter);
            Assert.AreEqual(PublicProperty_ProtectedSetter, expected.PublicProperty_ProtectedSetter);
            Assert.AreEqual(PublicProperty_PrivateSetter, expected.PublicProperty_PrivateSetter);
            Assert.AreEqual(PublicProperty_InternalSetter, expected.PublicProperty_InternalSetter);
            Assert.AreEqual(ProtectedProperty_PrivateSetter, expected.ProtectedProperty_PrivateSetter);
            Assert.AreEqual(InternalProperty_PrivateSetter, expected.InternalProperty_PrivateSetter);
        }
    }

    [MetaSerializable]
    public abstract class TestBaseWithAccessModifiers
    {
        [MetaMember(1)] public int PublicField;
        [MetaMember(2)] protected int ProtectedField;
        [MetaMember(3)] private int PrivateField;
        [MetaMember(4)] internal int InternalField;
        [MetaMember(5)] public int PublicProperty { get; set; }
        [MetaMember(6)] protected int ProtectedProperty { get; set; }
        [MetaMember(7)] private int PrivateProperty { get; set; }
        [MetaMember(8)] internal int InternalProperty { get; set; }
        [MetaMember(9)] public int PublicProperty_ProtectedGetter { protected get; set; }
        [MetaMember(10)] public int PublicProperty_PrivateGetter { private get; set; }
        [MetaMember(11)] public int PublicProperty_InternalGetter { internal get; set; }
        [MetaMember(12)] protected int ProtectedProperty_PrivateGetter { private get; set; }
        [MetaMember(13)] internal int InternalProperty_PrivateGetter { private get; set; }
        [MetaMember(14)] public int PublicProperty_ProtectedSetter { get; protected set; }
        [MetaMember(15)] public int PublicProperty_PrivateSetter { get; private set; }
        [MetaMember(16)] public int PublicProperty_InternalSetter { get; internal set; }
        [MetaMember(17)] protected int ProtectedProperty_PrivateSetter { get; private set; }
        [MetaMember(18)] internal int InternalProperty_PrivateSetter { get; private set; }

        public TestBaseWithAccessModifiers(){ }
        public TestBaseWithAccessModifiers(bool _)
        {
            PublicField = 10;
            ProtectedField = 20;
            PrivateField = 30;
            InternalField = 40;
            PublicProperty = 50;
            ProtectedProperty = 60;
            PrivateProperty = 70;
            InternalProperty = 80;
            PublicProperty_ProtectedGetter = 90;
            PublicProperty_PrivateGetter = 100;
            PublicProperty_InternalGetter = 110;
            ProtectedProperty_PrivateGetter = 120;
            InternalProperty_PrivateGetter = 130;
            PublicProperty_ProtectedSetter = 140;
            PublicProperty_PrivateSetter = 150;
            PublicProperty_InternalSetter = 160;
            ProtectedProperty_PrivateSetter = 170;
            InternalProperty_PrivateSetter = 180;
        }

        public void AssertEqual(TestBaseWithAccessModifiers expected)
        {
            Assert.AreEqual(PublicField, expected.PublicField);
            Assert.AreEqual(ProtectedField, expected.ProtectedField);
            Assert.AreEqual(PrivateField, expected.PrivateField);
            Assert.AreEqual(InternalField, expected.InternalField);
            Assert.AreEqual(PublicProperty, expected.PublicProperty);
            Assert.AreEqual(ProtectedProperty, expected.ProtectedProperty);
            Assert.AreEqual(PrivateProperty, expected.PrivateProperty);
            Assert.AreEqual(InternalProperty, expected.InternalProperty);
            Assert.AreEqual(PublicProperty_ProtectedGetter, expected.PublicProperty_ProtectedGetter);
            Assert.AreEqual(PublicProperty_PrivateGetter, expected.PublicProperty_PrivateGetter);
            Assert.AreEqual(PublicProperty_InternalGetter, expected.PublicProperty_InternalGetter);
            Assert.AreEqual(ProtectedProperty_PrivateGetter, expected.ProtectedProperty_PrivateGetter);
            Assert.AreEqual(InternalProperty_PrivateGetter, expected.InternalProperty_PrivateGetter);
            Assert.AreEqual(PublicProperty_ProtectedSetter, expected.PublicProperty_ProtectedSetter);
            Assert.AreEqual(PublicProperty_PrivateSetter, expected.PublicProperty_PrivateSetter);
            Assert.AreEqual(PublicProperty_InternalSetter, expected.PublicProperty_InternalSetter);
            Assert.AreEqual(ProtectedProperty_PrivateSetter, expected.ProtectedProperty_PrivateSetter);
            Assert.AreEqual(InternalProperty_PrivateSetter, expected.InternalProperty_PrivateSetter);
        }
    }

    [MetaSerializable]
    public class TestDerivedNonPolymorphicWithAccessModifiers : TestBaseWithAccessModifiers
    {
        public TestDerivedNonPolymorphicWithAccessModifiers(){ }
        public TestDerivedNonPolymorphicWithAccessModifiers(bool dummy) : base(dummy) { }
    }

    [MetaSerializableDerived(1)]
    public class TestDerivedPolymorphicWithAccessModifiers : TestBaseWithAccessModifiers
    {
        public TestDerivedPolymorphicWithAccessModifiers(){ }
        public TestDerivedPolymorphicWithAccessModifiers(bool dummy) : base(dummy) { }
    }

    [MetaSerializable] public enum TestEnumType                { Zero = 0, TestValue = int.MaxValue }
    [MetaSerializable] public enum TestEnumTypeSByte : sbyte   { Zero = 0, TestValue = sbyte.MaxValue }
    [MetaSerializable] public enum TestEnumTypeByte : byte     { Zero = 0, TestValue = byte.MaxValue }
    [MetaSerializable] public enum TestEnumTypeShort : short   { Zero = 0, TestValue = short.MaxValue }
    [MetaSerializable] public enum TestEnumTypeUShort : ushort { Zero = 0, TestValue = ushort.MaxValue }
    [MetaSerializable] public enum TestEnumTypeInt : int       { Zero = 0, TestValue = int.MaxValue }
    [MetaSerializable] public enum TestEnumTypeUInt : uint     { Zero = 0, TestValue = uint.MaxValue }
    [MetaSerializable] public enum TestEnumTypeLong : long     { Zero = 0, TestValue = long.MaxValue }
    [MetaSerializable] public enum TestEnumTypeULong : ulong   { Zero = 0, TestValue = ulong.MaxValue }

    [MetaSerializable]
    public class TestObjectTypes
    {
        [MetaSerializable]
        public class NestedPublicType
        {
            [MetaMember(1)] public int Foo;

            public override bool Equals(object obj) => obj is NestedPublicType other && Foo == other.Foo;
            public override int GetHashCode() => throw new NotImplementedException();
        };

        #if SUPPORT_PRIVATE_ACCESSORS
        [MetaSerializable]
        private class NestedPrivateType
        {
            [MetaMember(1)] public int Foo;

            public override bool Equals(object obj) => obj is NestedPrivateType other && Foo == other.Foo;
            public override int GetHashCode() => throw new NotImplementedException();
        };
        #endif

        [MetaSerializable]
        public enum NestedEnumType { TestValue = int.MaxValue }

        [MetaSerializable]
        public class TestNullableValueTypes
        {
            [MetaMember(1)]  public bool?           Bool           { get; set; }
            [MetaMember(2)]  public sbyte?          SByte          { get; set; }
            [MetaMember(3)]  public byte?           Byte           { get; set; }
            [MetaMember(4)]  public short?          Short          { get; set; }
            [MetaMember(5)]  public ushort?         UShort         { get; set; }
            [MetaMember(20)] public char?           Char           { get; set; }
            [MetaMember(6)]  public int?            Int            { get; set; }
            [MetaMember(7)]  public uint?           UInt           { get; set; }
            [MetaMember(8)]  public long?           Long           { get; set; }
            [MetaMember(9)]  public ulong?          ULong          { get; set; }
            [MetaMember(10)] public MetaUInt128?    UInt128        { get; set; }
            [MetaMember(12)] public F32?            F32            { get; set; }
            [MetaMember(13)] public F32Vec2?        F32Vec2        { get; set; }
            [MetaMember(14)] public F32Vec3?        F32Vec3        { get; set; }
            [MetaMember(15)] public F64?            F64            { get; set; }
            [MetaMember(16)] public F64Vec2?        F64Vec2        { get; set; }
            [MetaMember(17)] public F64Vec3?        F64Vec3        { get; set; }
            [MetaMember(18)] public float?          Float          { get; set; }
            [MetaMember(19)] public double?         Double         { get; set; }
            [MetaMember(21)] public MetaGuid?       Guid           { get; set; }
            [MetaMember(22)] public EntityKind?     EntityKind     { get; set; }
            [MetaMember(25)] public DateTimeOffset? DateTimeOffset { get; set; }
            [MetaMember(26)] public Guid?           SystemGuid        { get; set; }
            [MetaMember(27)] public TimeSpan?       TimeSpan        { get; set; }

            public void AssertEqual(TestNullableValueTypes expected)
            {
                Assert.AreEqual(expected.Bool, Bool);
                Assert.AreEqual(expected.SByte, SByte);
                Assert.AreEqual(expected.Byte, Byte);
                Assert.AreEqual(expected.Short, Short);
                Assert.AreEqual(expected.UShort, UShort);
                Assert.AreEqual(expected.Char, Char);
                Assert.AreEqual(expected.Int, Int);
                Assert.AreEqual(expected.UInt, UInt);
                Assert.AreEqual(expected.Long, Long);
                Assert.AreEqual(expected.ULong, ULong);
                Assert.AreEqual(expected.UInt128, UInt128);
                Assert.AreEqual(expected.F32, F32);
                Assert.AreEqual(expected.F32Vec2, F32Vec2);
                Assert.AreEqual(expected.F32Vec3, F32Vec3);
                Assert.AreEqual(expected.F64, F64);
                Assert.AreEqual(expected.F64Vec2, F64Vec2);
                Assert.AreEqual(expected.F64Vec3, F64Vec3);
                Assert.AreEqual(expected.Float, Float);
                Assert.AreEqual(expected.Double, Double);
                Assert.AreEqual(expected.Guid, Guid);
                Assert.AreEqual(expected.EntityKind, EntityKind);
                Assert.AreEqual(expected.DateTimeOffset, DateTimeOffset);
                Assert.AreEqual(expected.SystemGuid, SystemGuid);
                Assert.AreEqual(expected.TimeSpan, TimeSpan);
            }
        }

        [MetaSerializable]
        public class TestNullableEnums
        {
            [MetaMember(1)] public TestEnumType?        TestEnum            { get; set; }
            [MetaMember(2)] public TestEnumTypeSByte?   TestEnumSByte       { get; set; }
            [MetaMember(3)] public TestEnumTypeByte?    TestEnumByte        { get; set; }
            [MetaMember(4)] public TestEnumTypeShort?   TestEnumShort       { get; set; }
            [MetaMember(5)] public TestEnumTypeUShort?  TestEnumUShort      { get; set; }
            [MetaMember(6)] public TestEnumTypeInt?     TestEnumInt         { get; set; }
            [MetaMember(7)] public TestEnumTypeUInt?    TestEnumUInt        { get; set; }
            [MetaMember(8)] public TestEnumTypeLong?    TestEnumLong        { get; set; }
            [MetaMember(9)] public TestEnumTypeULong?   TestEnumULong       { get; set; }

            public void AssertEqual(TestNullableEnums expected)
            {
                Assert.AreEqual(expected.TestEnum, TestEnum);
                Assert.AreEqual(expected.TestEnumSByte, TestEnumSByte);
                Assert.AreEqual(expected.TestEnumByte, TestEnumByte);
                Assert.AreEqual(expected.TestEnumShort, TestEnumShort);
                Assert.AreEqual(expected.TestEnumUShort, TestEnumUShort);
                Assert.AreEqual(expected.TestEnumInt, TestEnumInt);
                Assert.AreEqual(expected.TestEnumUInt, TestEnumUInt);
                Assert.AreEqual(expected.TestEnumLong, TestEnumLong);
                Assert.AreEqual(expected.TestEnumULong, TestEnumULong);
            }
        }

        [MetaMember(4)] public string                                   EmptyString         { get; set; }
        [MetaMember(5)] public string                                   String              { get; set; }
        [MetaMember(6)] public string                                   NullString          { get; set; }
        [MetaMember(11)] public bool                                    Bool                { get; set; }
        [MetaMember(7)] public sbyte                                    SByte               { get; set; }
        [MetaMember(8)] public byte                                     Byte                { get; set; }
        [MetaMember(9)] public short                                    Short               { get; set; }
        [MetaMember(10)] public ushort                                  UShort              { get; set; }
        [MetaMember(12)] public char                                    Char                { get; set; }
        [MetaMember(2)] public int                                      Int                 { get; set; }
        [MetaMember(30)] public uint                                    UInt                { get; set; }
        [MetaMember(3)] public long                                     Long                { get; set; }
        [MetaMember(31)] public ulong                                   ULong               { get; set; }
        [MetaMember(32)] public MetaUInt128                             UInt128             { get; set; }
        [MetaMember(33)] public F32                                     F32                 { get; set; }
        [MetaMember(34)] public F32Vec2                                 F32Vec2             { get; set; }
        [MetaMember(35)] public F32Vec3                                 F32Vec3             { get; set; }
        [MetaMember(36)] public F64                                     F64                 { get; set; }
        [MetaMember(37)] public F64Vec2                                 F64Vec2             { get; set; }
        [MetaMember(38)] public F64Vec3                                 F64Vec3             { get; set; }
        [MetaMember(20)] public float                                   Float               { get; set; }
        [MetaMember(21)] public double                                  Double              { get; set; }
        [MetaMember(26)] public MetaGuid                                Guid                { get; set; }
        [MetaMember(29)] public EntityKind                              EntityKind          { get; set; }
        [MetaMember(22)] public TestNullableValueTypes                  NTWithValues        { get; set; }
        [MetaMember(23)] public TestNullableValueTypes                  NTWithNulls         { get; set; }
        [MetaMember(24)] public TestStringId                            TestStringId        { get; set; }
        [MetaMember(25)] public TestStringId                            EmptyStringId       { get; set; }
        [MetaMember(50)] public List<int>                               IntList             { get; set; }
        [MetaMember(51)] public List<int>                               EmptyList           { get; set; }
        [MetaMember(52)] public List<int>                               NullList            { get; set; }
        [MetaMember(60)] public int[]                                   IntArray            { get; set; }
        [MetaMember(61)] public int[]                                   EmptyIntArray       { get; set; }
        [MetaMember(62)] public int[]                                   NullIntArray        { get; set; }
        [MetaMember(63)] public byte[]                                  ByteArray           { get; set; }
        [MetaMember(64)] public byte[]                                  EmptyByteArray      { get; set; }
        [MetaMember(65)] public byte[]                                  NullByteArray       { get; set; }
        [MetaMember(66)] public TestCustomType[]                        ObjectArray         { get; set; }
        [MetaMember(70)] public SortedDictionary<int, string>           Dict                { get; set; }
        [MetaMember(71)] public SortedDictionary<int, string>           EmptyDict           { get; set; }
        [MetaMember(72)] public SortedDictionary<int, string>           NullDict            { get; set; }
        [MetaMember(73)] public SortedDictionary<string, ContentHash>   VersionDict         { get; set; }
        [MetaMember(123)] public TestModelAction                        Action              { get; set; }
        [MetaMember(124)] public TestModelAction                        NullAction          { get; set; }
        [MetaMember(990)] public SortedSet<int>                         NullIntSet          { get; set; }
        [MetaMember(991)] public SortedSet<int>                         IntSet              { get; set; }
        [MetaMember(992)] public SortedSet<string>                      StringSet           { get; set; }
        [MetaMember(995)] public SortedSet<LanguageId>                  EmptyLanguageSet    { get; set; }
        [MetaMember(996)] public SortedSet<LanguageId>                  LanguageSet         { get; set; }
        [MetaMember(1000)] public OrderedSet<int>                       OrderedIntSet       { get; set; }
        [MetaMember(1001)] public OrderedSet<string>                    OrderedStringSet    { get; set; }
        [MetaMember(1002)] public OrderedDictionary<int,int>            OrderedIntDict      { get; set; }
        [MetaMember(1003)] public OrderedDictionary<string,string>      OrderedStringDict   { get; set; }
        [MetaMember(1013)] private NestedPublicType                     _nestedPublicType   { get; set; }
        [MetaMember(1014)] private NestedPublicType[]                   _nestedPublicTypeArray  { get; set; }
        #if SUPPORT_PRIVATE_ACCESSORS
        [MetaMember(1014)] private NestedPrivateType                    _nestedPrivateType  { get; set; }
        [MetaMember(1016)] private NestedPrivateType[]                  _nestedPrivateTypeArray { get; set; }
        #endif
        [MetaMember(1017)] private TestCustomTypeWithNoNS[]            NoNamespaceTypeArray                  { get; set; }
        [MetaMember(1020)] public  TestAbstract                        TestAbstractA                         { get; set; }
        [MetaMember(1021)] public  TestAbstract                        TestAbstractB                         { get; set; }
        [MetaMember(1022)] public  TestAbstractIntermediateInBaseChain TestAbstractBAsIntermediate           { get; set; }
        [MetaMember(1030)] public  ITestInterface                      TestInterfaceA                        { get; set; }
        [MetaMember(1031)] public  ITestInterface                      TestInterfaceB                        { get; set; }
        [MetaMember(1032)] public  ITestInterface                      TestInterfaceC                        { get; set; }
        [MetaMember(1033)] public  ITestInterface                      TestInterfaceD                        { get; set; }
        [MetaMember(1034)] public  TestInterfaceIntermediateClass      TestInterfaceCAsIntermediateClass     { get; set; }
        [MetaMember(1035)] public  ITestInterfaceIntermediateInterface TestInterfaceDAsIntermediateInterface { get; set; }
        [MetaMember(1041)] public  Nullable<TestValueStruct>           NullNullable                          { get; set; }
        [MetaMember(1042)] public  Nullable<TestValueStruct>           NonnullNullable                       { get; set; }
        [MetaMember(1050)] public  NestedEnumType                      NestedEnum                            { get; set; }
        [MetaMember(1051)] public  TestEnumType                        TestEnum                              { get; set; }
        [MetaMember(1052)] public  TestEnumTypeSByte                   TestEnumSByte                         { get; set; }
        [MetaMember(1053)] public  TestEnumTypeByte                    TestEnumByte                          { get; set; }
        [MetaMember(1054)] public  TestEnumTypeShort                   TestEnumShort                         { get; set; }
        [MetaMember(1055)] public  TestEnumTypeUShort                  TestEnumUShort                        { get; set; }
        [MetaMember(1056)] public  TestEnumTypeInt                     TestEnumInt                           { get; set; }
        [MetaMember(1057)] public  TestEnumTypeUInt                    TestEnumUInt                          { get; set; }
        [MetaMember(1058)] public  TestEnumTypeLong                    TestEnumLong                          { get; set; }
        [MetaMember(1059)] public  TestEnumTypeULong                   TestEnumULong                         { get; set; }
        [MetaMember(1060)] public  TestNullableEnums                   NEWithValues                          { get; set; }
        [MetaMember(1061)] public  TestNullableEnums                   NEWithNulls                           { get; set; }
        [MetaMember(1100)] public  MetaSerialized<TestCustomType>      MetaSerialized                        { get; set; }
        [MetaMember(1101)] public  int[][]                             NestedIntArray                        { get; set; }
        [MetaMember(1102)] public  List<int[][]>[][]                   NestedIntListArray                    { get; set; }
        [MetaMember(1103)] public  TupleSerialization                  Tuples                                { get; set; }
        [MetaMember(1104)] public  Guid                                SystemGuid                            { get; set; }
        [MetaMember(1105)] public  TimeSpan                            TimeSpan                              { get; set; }
        [MetaMember(1106)] public  DateTimeOffset                      DateTimeOffset                        { get; set; }
        [MetaMember(1107)] public  Version                             Version                               { get; set; }
        [MetaMember(1108)] public  ReadOnlyCollection<int>             ReadOnlyCollection                    { get; set; }
        [MetaMember(1109)] public  ReadOnlyDictionary<int, string>     ReadOnlyDictionary                    { get; set; }

        public TestObjectTypes() { }
        public TestObjectTypes(bool init)
        {
            EmptyString         = "";
            String              = "str";
            Bool                = true;
            SByte               = -17;
            Byte                = 42;
            Short               = -12345;
            UShort              = 54321;
            Char                = 'あ';
            Int                 = 15;
            UInt                = 3456789123;
            Long                = 15_000_000_000_000L;
            ULong               = 12345678901345252342UL;
            UInt128             = new MetaUInt128(14328762348762347372UL, 17982734934908345798UL);
            F32                 = F32.Pi;
            F32Vec2             = new F32Vec2(F32.Pi, F32.Pi2);
            F32Vec3             = new F32Vec3(F32.Pi, F32.Pi2, F32.PiHalf);
            F64                 = F64.Pi;
            F64Vec2             = new F64Vec2(F64.Pi, F64.Pi2);
            F64Vec3             = new F64Vec3(F64.Pi, F64.Pi2, F64.PiHalf);
            Float               = 16.5f;
            Double              = 32.5;
            Guid                = new MetaGuid(new MetaUInt128(0x1234567890abcde0UL, 17982734934908345798UL));
            EntityKind          = EntityKindCore.Player;
            NTWithValues        = new TestNullableValueTypes
            {
                Bool       = true,
                SByte      = -17,
                Byte       = 42,
                Short      = -12345,
                UShort     = 54321,
                Char       = 'あ',
                Int        = 15,
                UInt       = 3456789123,
                Long       = 15_000_000_000_000L,
                ULong      = 12345678901345252342UL,
                UInt128    = new MetaUInt128(14328762348762347372UL, 17982734934908345798UL),
                F32        = F32.Pi,
                F32Vec2    = new F32Vec2(F32.Pi, F32.Pi2),
                F32Vec3    = new F32Vec3(F32.Pi, F32.Pi2, F32.PiHalf),
                F64        = F64.Pi,
                F64Vec2    = new F64Vec2(F64.Pi, F64.Pi2),
                F64Vec3    = new F64Vec3(F64.Pi, F64.Pi2, F64.PiHalf),
                Float      = 16.5f,
                Double     = 32.5,
                Guid       = new MetaGuid(new MetaUInt128(0x1234567890abcde0UL, 17982734934908345798UL)),
                EntityKind = EntityKindCore.Player,
                SystemGuid         = new Guid(1,2,3,4, 5, 6, 7, 8, 9, 10, 11),
                TimeSpan           = new TimeSpan(123456789),
                DateTimeOffset     = new DateTimeOffset(2023, 9, 25, 15, 6, 30, new TimeSpan(0, 30, 0)),
            };
            NTWithNulls         = new TestNullableValueTypes{ };
            TestStringId        = TestStringId.FromString("Foobar");
            EmptyStringId       = null;
            IntList             = new List<int> { -5, -5 };
            EmptyList           = new List<int> { };
            IntArray            = new int[] { -1, 0, 1_999_999_999 };
            EmptyIntArray       = new int[] { };
            ByteArray           = new byte[] { 42, 123, 255 };
            EmptyByteArray      = new byte[] { };
            ObjectArray         = new TestCustomType[] { null, new TestCustomType() };
            Dict                = new SortedDictionary<int, string>() { { 5, "foo" }, { -13, "Bar" } };
            EmptyDict           = new SortedDictionary<int, string>() { };
            VersionDict         = new SortedDictionary<string, ContentHash>(StringComparer.InvariantCulture) { { "abba", ContentHash.ComputeFromBytes(new byte[] { 1, 2, 3 }) } };
            Action              = new TestModelAction(new TestModelActionPayload(MetaTime.Epoch));
            IntSet              = new SortedSet<int> { 5, 3, 1, -9999 };
            StringSet           = new SortedSet<string>(StringComparer.InvariantCulture) { "", null, "fuf" };
            EmptyLanguageSet    = new SortedSet<LanguageId> { };
            LanguageSet         = new SortedSet<LanguageId> { LanguageId.FromString(null), LanguageId.FromString("en_us") };
            OrderedIntSet       = new OrderedSet<int>(1024) { 1, 2, 6, 9, 10 }; // uses wonky capacity
            OrderedStringSet    = new OrderedSet<string>(512) { "a", "b", null, "c", "d", "e" };
            OrderedIntDict      = new OrderedDictionary<int, int>(123) { [0] = 1, [1] = 2, [-3] = -4, [8] = 8, [10] = 0 };
            OrderedStringDict   = new OrderedDictionary<string, string>(123) { ["0"] = "1", ["1"] = null, [null] = "2", [""] = "f", ["foo"] = "bar" };
            _nestedPublicType       = new NestedPublicType{ Foo = 1234 };
            _nestedPublicTypeArray  = new NestedPublicType[]{ new NestedPublicType{ Foo = 12 }, null, new NestedPublicType{ Foo = 34 } };
            #if SUPPORT_PRIVATE_ACCESSORS
            _nestedPrivateType      = new NestedPrivateType{ Foo = 5678 };
            _nestedPrivateTypeArray = new NestedPrivateType[]{ new NestedPrivateType{ Foo = 56 }, null, new NestedPrivateType{ Foo = 78 } };
            #endif
            NoNamespaceTypeArray    = new TestCustomTypeWithNoNS[]{ new TestCustomTypeWithNoNS{ Foo = 123 }, null, new TestCustomTypeWithNoNS{ Foo = 456 } };
            TestAbstractA       = new TestDerivedA(12, 34);
            TestAbstractB       = new TestDerivedB(56, 78);
            TestAbstractBAsIntermediate = new TestDerivedB(123, 456);
            TestInterfaceA      = new TestInterfaceImplementationA(123);
            TestInterfaceB      = new TestInterfaceImplementationB(456);
            TestInterfaceC      = new TestInterfaceImplementationC(789);
            TestInterfaceD      = new TestInterfaceImplementationD(987);
            TestInterfaceCAsIntermediateClass = new TestInterfaceImplementationC(123);
            TestInterfaceDAsIntermediateInterface = new TestInterfaceImplementationD(456);
            NullNullable        = null;
            NonnullNullable     = new TestValueStruct() { Member = 123 };
            NestedEnum          = NestedEnumType.TestValue;
            TestEnum            = TestEnumType.TestValue;
            TestEnumSByte       = TestEnumTypeSByte.TestValue;
            TestEnumByte        = TestEnumTypeByte.TestValue;
            TestEnumShort       = TestEnumTypeShort.TestValue;
            TestEnumUShort      = TestEnumTypeUShort.TestValue;
            TestEnumInt         = TestEnumTypeInt.TestValue;
            TestEnumUInt        = TestEnumTypeUInt.TestValue;
            TestEnumLong        = TestEnumTypeLong.TestValue;
            TestEnumULong       = TestEnumTypeULong.TestValue;
            NEWithValues        = new TestNullableEnums
            {
                TestEnum        = TestEnumType.TestValue,
                TestEnumSByte   = TestEnumTypeSByte.TestValue,
                TestEnumByte    = TestEnumTypeByte.TestValue,
                TestEnumShort   = TestEnumTypeShort.TestValue,
                TestEnumUShort  = TestEnumTypeUShort.TestValue,
                TestEnumInt     = TestEnumTypeInt.TestValue,
                TestEnumUInt    = TestEnumTypeUInt.TestValue,
                TestEnumLong    = TestEnumTypeLong.TestValue,
                TestEnumULong   = TestEnumTypeULong.TestValue,
            };
            NEWithNulls         = new TestNullableEnums{ };
            MetaSerialized      = new MetaSerialized<TestCustomType>(new TestCustomType(), MetaSerializationFlags.IncludeAll, logicVersion: null);
            NestedIntArray      = new int[4][] { null, new int[0], new int[1] { -1 }, new int[3] { 1, 2, 3 } };
            NestedIntListArray  = new List<int[][]>[2][]
            {
                null,
                new List<int[][]>[2]
                {
                    null,
                    new List<int[][]>
                    {
                        new int[2][] { null, new int[2] { 1, 2 } }
                    },
                },
            };
            Tuples = new TupleSerialization();
            Tuples.Init();
            SystemGuid         = new Guid(1,2,3,4, 5, 6, 7, 8, 9, 10, 11);
            TimeSpan           = new TimeSpan(123456789);
            DateTimeOffset     = new DateTimeOffset(2023, 9, 25, 15, 6, 30, new TimeSpan(0, 30, 0));
            Version            = new Version(1, 2, 3, 4);
            ReadOnlyCollection = new ReadOnlyCollection<int>(new List<int> { -5, -5 });
            ReadOnlyDictionary = new ReadOnlyDictionary<int, string>(new OrderedDictionary<int, string>() { { 5, "foo" }, { -13, "Bar" } });
        }

        public void AssertEqual(TestObjectTypes expected)
        {
            Assert.AreEqual(expected.EmptyString, EmptyString);
            Assert.AreEqual(expected.String, String);
            Assert.AreEqual(expected.NullString, NullString);
            Assert.AreEqual(expected.Bool, Bool);
            Assert.AreEqual(expected.SByte, SByte);
            Assert.AreEqual(expected.Byte, Byte);
            Assert.AreEqual(expected.Short, Short);
            Assert.AreEqual(expected.UShort, UShort);
            Assert.AreEqual(expected.Char, Char);
            Assert.AreEqual(expected.Int, Int);
            Assert.AreEqual(expected.UInt, UInt);
            Assert.AreEqual(expected.Long, Long);
            Assert.AreEqual(expected.ULong, ULong);
            Assert.AreEqual(expected.UInt128, UInt128);
            Assert.AreEqual(expected.F32, F32);
            Assert.AreEqual(expected.F32Vec2, F32Vec2);
            Assert.AreEqual(expected.F32Vec3, F32Vec3);
            Assert.AreEqual(expected.F64, F64);
            Assert.AreEqual(expected.F64Vec2, F64Vec2);
            Assert.AreEqual(expected.F64Vec3, F64Vec3);
            Assert.AreEqual(expected.Float, Float);
            Assert.AreEqual(expected.Double, Double);
            Assert.AreEqual(expected.Guid, Guid);
            Assert.AreEqual(expected.EntityKind, EntityKind);
            Assert.AreEqual(expected.NTWithValues == null, NTWithValues == null);
            expected.NTWithValues?.AssertEqual(NTWithValues);
            Assert.AreEqual(expected.NTWithNulls == null, NTWithNulls == null);
            expected.NTWithNulls?.AssertEqual(NTWithNulls);
            Assert.AreEqual(expected.TestStringId, TestStringId);
            Assert.AreEqual(expected.EmptyStringId, EmptyStringId);
            Assert.AreEqual(expected.IntList, IntList);
            Assert.AreEqual(expected.EmptyList, EmptyList);
            Assert.AreEqual(expected.NullList, NullList);
            Assert.AreEqual(expected.IntArray, IntArray);
            Assert.AreEqual(expected.EmptyIntArray, EmptyIntArray);
            Assert.AreEqual(expected.NullIntArray, NullIntArray);
            Assert.AreEqual(expected.ByteArray, ByteArray);
            Assert.AreEqual(expected.EmptyByteArray, EmptyByteArray);
            Assert.AreEqual(expected.NullByteArray, NullByteArray);
            Assert.AreEqual(expected.ObjectArray, ObjectArray);
            Assert.AreEqual(expected.Dict, Dict);
            Assert.AreEqual(expected.EmptyDict, EmptyDict);
            Assert.AreEqual(expected.NullDict, NullDict);
            Assert.AreEqual(expected.VersionDict, VersionDict);
            Assert.AreEqual(PrettyPrinter.Compact(expected.Action), PrettyPrinter.Compact(Action));
            Assert.AreEqual(expected.NullAction, NullAction);
            Assert.AreEqual(expected.NullIntSet, NullIntSet);
            Assert.AreEqual(expected.IntSet, IntSet);
            Assert.AreEqual(expected.StringSet, StringSet);
            Assert.AreEqual(expected.EmptyLanguageSet, EmptyLanguageSet);
            Assert.AreEqual(expected.LanguageSet, LanguageSet);
            Assert.AreEqual(expected.OrderedIntSet, OrderedIntSet);
            Assert.AreEqual(expected.OrderedStringSet, OrderedStringSet);
            Assert.AreEqual(expected.OrderedIntDict, OrderedIntDict);
            Assert.AreEqual(expected.OrderedStringDict, OrderedStringDict);
            Assert.AreEqual(expected._nestedPublicType, _nestedPublicType);
            Assert.AreEqual(expected._nestedPublicTypeArray, _nestedPublicTypeArray);
            #if SUPPORT_PRIVATE_ACCESSORS
            Assert.AreEqual(expected._nestedPrivateType, _nestedPrivateType);
            Assert.AreEqual(expected._nestedPrivateTypeArray, _nestedPrivateTypeArray);
            #endif
            Assert.AreEqual(expected.NoNamespaceTypeArray, NoNamespaceTypeArray);
            Assert.AreEqual(expected.TestAbstractA, TestAbstractA);
            Assert.AreEqual(expected.TestAbstractB, TestAbstractB);
            Assert.AreEqual(expected.TestAbstractBAsIntermediate, TestAbstractBAsIntermediate);
            Assert.AreEqual(expected.TestInterfaceA, TestInterfaceA);
            Assert.AreEqual(expected.TestInterfaceB, TestInterfaceB);
            Assert.AreEqual(expected.TestInterfaceC, TestInterfaceC);
            Assert.AreEqual(expected.TestInterfaceD, TestInterfaceD);
            Assert.AreEqual(expected.TestInterfaceCAsIntermediateClass, TestInterfaceCAsIntermediateClass);
            Assert.AreEqual(expected.TestInterfaceDAsIntermediateInterface, TestInterfaceDAsIntermediateInterface);
            Assert.AreEqual(expected.NullNullable, expected.NullNullable);
            Assert.AreEqual(expected.NonnullNullable, expected.NonnullNullable);
            Assert.AreEqual(expected.NestedEnum, NestedEnum);
            Assert.AreEqual(expected.TestEnum, TestEnum);
            Assert.AreEqual(expected.TestEnumSByte, TestEnumSByte);
            Assert.AreEqual(expected.TestEnumByte, TestEnumByte);
            Assert.AreEqual(expected.TestEnumShort, TestEnumShort);
            Assert.AreEqual(expected.TestEnumUShort, TestEnumUShort);
            Assert.AreEqual(expected.TestEnumInt, TestEnumInt);
            Assert.AreEqual(expected.TestEnumUInt, TestEnumUInt);
            Assert.AreEqual(expected.TestEnumLong, TestEnumLong);
            Assert.AreEqual(expected.TestEnumULong, TestEnumULong);
            Assert.AreEqual(expected.NEWithValues == null, NEWithValues == null);
            expected.NEWithValues?.AssertEqual(NEWithValues);
            Assert.AreEqual(expected.NEWithNulls == null, NEWithNulls == null);
            expected.NEWithNulls?.AssertEqual(NEWithNulls);
            Assert.AreEqual(expected.MetaSerialized.Bytes, MetaSerialized.Bytes);
            Assert.AreEqual(expected.MetaSerialized.Flags, MetaSerialized.Flags);
            Assert.AreEqual(expected.NestedIntArray, NestedIntArray);
            Assert.AreEqual(expected.NestedIntListArray, NestedIntListArray);
            Tuples.AssertValues(expected.Tuples);
            Assert.AreEqual(expected.SystemGuid, SystemGuid);
            Assert.AreEqual(expected.TimeSpan, TimeSpan);
            Assert.AreEqual(expected.DateTimeOffset, DateTimeOffset);
            Assert.AreEqual(expected.Version, Version);
            Assert.AreEqual(expected.ReadOnlyCollection, ReadOnlyCollection);
            Assert.AreEqual(expected.ReadOnlyDictionary, ReadOnlyDictionary);
        }
    }

    [MetaSerializable]
    public class TestObjectWithFlags
    {
        [MetaMember(1)] public int              Persisted   { get; set; }
        [MetaMember(2), Transient] public int   Transient   { get; set; }
        [MetaMember(3), ServerOnly] public int  ServerOnly  { get; set; }
    }

    // Same as TestObjectWithFlags but with [MetaOnMemberDeserializationFailure] handlers,
    // because that interacts a bit with the flag checks in the generated deserializer.
    [MetaSerializable]
    public class TestObjectWithFlagsAndDeserializationFailureHandlers
    {
        [MetaOnMemberDeserializationFailure("Dummy")]
        [MetaMember(1)] public int              Persisted   { get; set; }
        [MetaOnMemberDeserializationFailure("Dummy")]
        [MetaMember(2), Transient] public int   Transient   { get; set; }
        [MetaOnMemberDeserializationFailure("Dummy")]
        [MetaMember(3), ServerOnly] public int  ServerOnly  { get; set; }

        public static int Dummy(MetaMemberDeserializationFailureParams _) => 0;
    }

    [MetaSerializable]
    public abstract class TestObjectWithInheritedFlagsBase
    {
        public abstract int                 Persisted   { get; set; }
        [Transient] public abstract int     Transient   { get; set; }
        [ServerOnly] public abstract int    ServerOnly  { get; set; }
    }

    [MetaSerializable]
    public class TestObjectWithInheritedFlags : TestObjectWithInheritedFlagsBase
    {
        [MetaMember(1)] public sealed override int Persisted   { get; set; }
        [MetaMember(2)] public sealed override int Transient   { get; set; }
        [MetaMember(3)] public sealed override int ServerOnly  { get; set; }
    }

    [MetaSerializableDerived(1)]
    public class TestObjectWithInheritedFlagsDerived : TestObjectWithInheritedFlagsBase
    {
        [MetaMember(1)] public sealed override int Persisted   { get; set; }
        [MetaMember(2)] public sealed override int Transient   { get; set; }
        [MetaMember(3)] public sealed override int ServerOnly  { get; set; }
    }

    // Same as TestObjectWithFlags except flags removed (to test serialization)
    [MetaSerializable]
    public class TestObjectNoFlags
    {
        [MetaMember(1)] public int  Persisted   { get; set; }
        [MetaMember(2)] public int  Transient   { get; set; }
        [MetaMember(3)] public int  ServerOnly  { get; set; }
    }

    // Test direct serialization with [MetaSerializable] and [MetaSerializableDerived] should be interchangeable (assuming no base classes used)
    [MetaSerializable]
    public class WithOnlySerializable
    {
        [MetaMember(1)] public string Value { get; set; }
    }

    [MetaSerializable]
    public interface IDummyInterface
    {
    }

    [MetaSerializableDerived(1)]
    public class WithSerializableDerived : IDummyInterface
    {
        [MetaMember(1)] public string Value { get; set; }
    }

    [MetaSerializable]
    public class WithOnlySerializableContainer
    {
        [MetaMember(1)] public WithOnlySerializable                     Direct      { get; set; }
        [MetaMember(2)] public MetaSerialized<WithOnlySerializable>     Serialized  { get; set; }
    }

    [MetaSerializable]
    public class WithSerializableDerivedContainer
    {
        [MetaMember(1)] public WithSerializableDerived                  Direct      { get; set; }
        [MetaMember(2)] public MetaSerialized<WithSerializableDerived>  Serialized  { get; set; }
    }

    [MetaSerializable(MetaSerializableFlags.AutomaticConstructorDetection)]
    public class ConstructorSerialization
    {
        [MetaMember(1)] public int  A { get; set; }
        [MetaMember(2)] public bool B { get; set; }

        public bool ConstructorInvoked { get; init; }

        public ConstructorSerialization(int a, bool b)
        {
            A = a;
            B = b;

            ConstructorInvoked = true;
        }
    }

    [MetaSerializable]
    public class MultipleConstructorSerialization
    {
        [MetaMember(1)] public int  A { get; set; }
        [MetaMember(2)] public bool B { get; set; }

        public bool ConstructorInvoked { get; init; }

        [MetaDeserializationConstructor]
        public MultipleConstructorSerialization(int a, bool b)
        {
            A = a;
            B = b;

            ConstructorInvoked = true;
        }

        public MultipleConstructorSerialization(bool b, int a)
        {
            A = a;
            B = b;
        }
    }

    [MetaSerializable(MetaSerializableFlags.AutomaticConstructorDetection)]
    public readonly struct ReadOnlyStructConstructorSerialization
    {
        [MetaMember(1)] public readonly int  A ;
        [MetaMember(2)] public readonly bool B ;

        public bool ConstructorInvoked { get; init; }

        public ReadOnlyStructConstructorSerialization(int a, bool b)
        {
            A = a;
            B = b;

            ConstructorInvoked = true;
        }
    }

    [MetaSerializable]
    public class ConstructorExplicitConstructorSerialization
    {
        [MetaMember(1)] public int  A { get; set; }
        [MetaMember(2)] public bool B { get; set; }

        public bool ConstructorInvoked { get; init; }

        [MetaDeserializationConstructor]
        public ConstructorExplicitConstructorSerialization(bool b, int a)
        {
            A = a;
            B = b;

            ConstructorInvoked = true;
        }
    }

    [MetaSerializable]
    public class ConstructorPrivateConstructorSerialization
    {
        [MetaMember(1)] public int  A { get; set; }
        [MetaMember(2)] public bool B { get; set; }

        public bool ConstructorInvoked { get; init; }

        [MetaDeserializationConstructor]
        private ConstructorPrivateConstructorSerialization(bool b, int a)
        {
            A = a;
            B = b;

            ConstructorInvoked = true;
        }

        public static ConstructorPrivateConstructorSerialization Create(bool b, int a)
        {
            return new ConstructorPrivateConstructorSerialization(b, a);
        }
    }

    [MetaSerializable(MetaSerializableFlags.ImplicitMembers | MetaSerializableFlags.AutomaticConstructorDetection)]
    [MetaImplicitMembersRange(1, 100)]
    public record RecordImplicitMembersSerialization(int A, bool B)
    {
    }

    [MetaSerializable(MetaSerializableFlags.AutomaticConstructorDetection)]
    public record RecordSerialization([property: MetaMember(1)]int A, [property: MetaMember(2)]bool B)
    {
    }

    [MetaSerializable(MetaSerializableFlags.ImplicitMembers), MetaImplicitMembersRange(1, 100)]
    public class TupleSerialization
    {
        public (int, bool)                            Tuple2         { get; set; }
        public (int, bool, int)                       Tuple3         { get; set; }
        public (int, bool, int, bool)                 Tuple4         { get; set; }
        public (int, bool, int, bool, int, bool, int) Tuple7         { get; set; }
        public (List<int>, bool)                      ReferenceTuple { get; set; }

        public Tuple<int, bool> NullableTuple { get; set; }
        public Tuple<int, bool> NullableTuple2 { get; set; }

        public void Init()
        {
            Tuple2         = (100, true);
            Tuple3         = (100, true, 1234);
            Tuple4         = (100, true, 1234, false);
            Tuple7         = (100, true, 1234, false, 12, true, 24);
            ReferenceTuple = (Enumerable.Range(1, 100).ToList(), true);
            NullableTuple  = new Tuple<int, bool>(100, true);
            NullableTuple2 = null;
        }

        public void AssertValues(TupleSerialization source)
        {
            Assert.AreEqual(source.Tuple2, Tuple2);
            Assert.AreEqual(source.Tuple3, Tuple3);
            Assert.AreEqual(source.Tuple4, Tuple4);
            Assert.AreEqual(source.Tuple7, Tuple7);

            Assert.AreEqual(source.ReferenceTuple.Item1, ReferenceTuple.Item1);
            Assert.AreEqual(source.ReferenceTuple.Item2, ReferenceTuple.Item2);

            Assert.AreEqual(source.NullableTuple.Item1, NullableTuple.Item1);
            Assert.AreEqual(source.NullableTuple.Item2, NullableTuple.Item2);
            Assert.AreEqual(source.NullableTuple2, NullableTuple2);
        }
    }

    [MetaSerializable]
    public class TuplePreStructSerialization
    {
        [MetaMember(1)]
        public (int, bool) Tuple2ToStruct { get; set; }
        [MetaMember(2)]
        public Tuple<int, bool> Tuple2ToClass { get; set; }

        public TuplePreStructSerialization()
        {
        }
    }

    [MetaSerializable]
    public class TuplePostStructSerialization
    {
        [MetaMember(1)]
        public ValueTupleToStructTest Tuple2ToStruct { get; set; }
        [MetaMember(2)]
        public TupleToClassTest Tuple2ToClass { get; set; }

        public TuplePostStructSerialization()
        {
        }
    }

    [MetaSerializable]
    public struct ValueTupleToStructTest
    {
        [MetaMember(1)]
        public int Test1 { get; set; }
        [MetaMember(2)]
        public bool Test2 { get; set; }
    }

    [MetaSerializable]
    public class TupleToClassTest
    {
        [MetaMember(1)]
        public int Test1 { get; set; }
        [MetaMember(2)]
        public bool Test2 { get; set; }

        public TupleToClassTest()
        {
        }
    }

    [TestFixture]
    public class SerializationTests
    {
        [Test]
        public void TestDeserializeValueTupleAsObject()
        {
            TuplePreStructSerialization input = new TuplePreStructSerialization();
            input.Tuple2ToStruct = (100, true);
            input.Tuple2ToClass = new Tuple<int, bool>(100, true);

            byte[]                       preSerialized  = MetaSerialization.SerializeTagged(input, MetaSerializationFlags.Persisted, logicVersion: null);
            TuplePostStructSerialization clone          = MetaSerialization.DeserializeTagged<TuplePostStructSerialization>(preSerialized, MetaSerializationFlags.Persisted, resolver: null, logicVersion: null);
            byte[]                       postSerialized = MetaSerialization.SerializeTagged(clone, MetaSerializationFlags.Persisted, logicVersion: null);

            // Test whether the serialized bytes are exactly the same, to ensure compatibility.
            Assert.AreEqual(preSerialized, postSerialized);

            Assert.AreEqual(100, clone.Tuple2ToStruct.Test1);
            Assert.AreEqual(true, clone.Tuple2ToStruct.Test2);

            Assert.AreEqual(100, clone.Tuple2ToClass.Test1);
            Assert.AreEqual(true, clone.Tuple2ToClass.Test2);
        }

        [Test]
        public void TestTupleSerialization()
        {
            TupleSerialization input = new TupleSerialization();
            input.Init();

            byte[]             persisted = MetaSerialization.SerializeTagged(input, MetaSerializationFlags.Persisted, logicVersion: null);
            TupleSerialization clone     = MetaSerialization.DeserializeTagged<TupleSerialization>(persisted, MetaSerializationFlags.Persisted, resolver: null, logicVersion: null);

            clone.AssertValues(input);
        }

        [Test]
        public void TestReadOnlyStructConstructorDeserialization()
        {
            ReadOnlyStructConstructorSerialization input = new ReadOnlyStructConstructorSerialization(100, true);

            byte[]                                 persisted = MetaSerialization.SerializeTagged(input, MetaSerializationFlags.Persisted, logicVersion: null);
            ReadOnlyStructConstructorSerialization clone     = MetaSerialization.DeserializeTagged<ReadOnlyStructConstructorSerialization>(persisted, MetaSerializationFlags.Persisted, resolver: null, logicVersion: null);

            Assert.AreEqual(true, clone.ConstructorInvoked);
            Assert.AreEqual(100, clone.A);
            Assert.AreEqual(true, clone.B);
        }

        [Test]
        public void TestConstructorDeserialization()
        {
            ConstructorSerialization input = new ConstructorSerialization(100, true);

            byte[]                   persisted = MetaSerialization.SerializeTagged(input, MetaSerializationFlags.Persisted, logicVersion: null);
            ConstructorSerialization clone     = MetaSerialization.DeserializeTagged<ConstructorSerialization>(persisted, MetaSerializationFlags.Persisted, resolver: null, logicVersion: null);

            Assert.AreEqual(true, clone.ConstructorInvoked);
            Assert.AreEqual(100, clone.A);
            Assert.AreEqual(true, clone.B);
        }

        [Test]
        public void TestMultipleValidConstructorDeserialization()
        {
            MultipleConstructorSerialization input = new MultipleConstructorSerialization(100, true);

            byte[]                           persisted = MetaSerialization.SerializeTagged(input, MetaSerializationFlags.Persisted, logicVersion: null);
            MultipleConstructorSerialization clone     = MetaSerialization.DeserializeTagged<MultipleConstructorSerialization>(persisted, MetaSerializationFlags.Persisted, resolver: null, logicVersion: null);

            Assert.AreEqual(true, clone.ConstructorInvoked);
            Assert.AreEqual(100, clone.A);
            Assert.AreEqual(true, clone.B);
        }

        [Test]
        public void TestExplicitConstructorDeserialization()
        {
            ConstructorExplicitConstructorSerialization input = new ConstructorExplicitConstructorSerialization(true, 100);

            byte[]                                      persisted = MetaSerialization.SerializeTagged(input, MetaSerializationFlags.Persisted, logicVersion: null);
            ConstructorExplicitConstructorSerialization clone     = MetaSerialization.DeserializeTagged<ConstructorExplicitConstructorSerialization>(persisted, MetaSerializationFlags.Persisted, resolver: null, logicVersion: null);

            Assert.AreEqual(true, clone.ConstructorInvoked);
            Assert.AreEqual(100, clone.A);
            Assert.AreEqual(true, clone.B);
        }

        [Test]
        public void TestPrivateConstructorDeserialization()
        {
            ConstructorPrivateConstructorSerialization input = ConstructorPrivateConstructorSerialization.Create(true, 100);

            byte[]                                     persisted = MetaSerialization.SerializeTagged(input, MetaSerializationFlags.Persisted, logicVersion: null);
            ConstructorPrivateConstructorSerialization clone     = MetaSerialization.DeserializeTagged<ConstructorPrivateConstructorSerialization>(persisted, MetaSerializationFlags.Persisted, resolver: null, logicVersion: null);

            Assert.AreEqual(true, clone.ConstructorInvoked);
            Assert.AreEqual(100, clone.A);
            Assert.AreEqual(true, clone.B);
        }

        [Test]
        public void TestRecordImplicitMembersSerialization()
        {
            RecordImplicitMembersSerialization input = new RecordImplicitMembersSerialization(100, true);

            byte[]              persisted = MetaSerialization.SerializeTagged(input, MetaSerializationFlags.Persisted, logicVersion: null);
            RecordImplicitMembersSerialization clone     = MetaSerialization.DeserializeTagged<RecordImplicitMembersSerialization>(persisted, MetaSerializationFlags.Persisted, resolver: null, logicVersion: null);

            Assert.AreEqual(100, clone.A);
            Assert.AreEqual(true, clone.B);
        }

        [Test]
        public void TestRecordSerialization()
        {
            RecordSerialization input = new RecordSerialization(100, true);

            byte[]              persisted = MetaSerialization.SerializeTagged(input, MetaSerializationFlags.Persisted, logicVersion: null);
            RecordSerialization clone     = MetaSerialization.DeserializeTagged<RecordSerialization>(persisted, MetaSerializationFlags.Persisted, resolver: null, logicVersion: null);

            Assert.AreEqual(100, clone.A);
            Assert.AreEqual(true, clone.B);
        }


        //[Test]
        //public void TestCompactSerializer()
        //{
        //    TestObjectTypes input = new TestObjectTypes(init: true);
        //    byte[] serialized = MetaSerialization.SerializeCompact(input, MetaSerializationFlags.IncludeAll);
        //    TestObjectTypes clone = MetaSerialization.DeserializeCompact<TestObjectTypes>(serialized, MetaSerializationFlags.IncludeAll, null);
        //
        //    clone.AssertEqual(input);
        //}

        [Test]
        public void TestSortedSetSerialization()
        {
            TestSortedSet input = new TestSortedSet
            {
                SortedSet = new SortedSet<int> { 1 }
            };

            byte[] persisted = MetaSerialization.SerializeTagged(input, MetaSerializationFlags.Persisted, logicVersion: null);
            TestSortedSet clone = MetaSerialization.DeserializeTagged<TestSortedSet>(persisted, MetaSerializationFlags.Persisted, resolver: null, logicVersion: null);

            Assert.AreEqual(new SortedSet<int> { 1 }, clone.SortedSet);
        }

        [Test]
        public void TestCollectionSize()
        {
            Assert.That(
                () =>
                {
                    TestGlobalState globalState = new TestGlobalState();
                    ContentHash     hash        = ContentHash.ComputeFromBytes(new byte[] { 1, 2, 3 });
                    for (int i = 0; i < 20000; i++)
                        globalState.GameConfigVersions.Add("ExampleGameConfigName" + i.ToString(CultureInfo.InvariantCulture), hash);
                    MetaSerialized<TestGlobalState> _ = new MetaSerialized<TestGlobalState>(globalState, MetaSerializationFlags.IncludeAll, logicVersion: null);
                }, Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("Invalid value collection size 20000 (maximum allowed is 16384)"));

            Assert.That(
                () =>
                {
                    TestGlobalStateOverrideCollectionSize globalState = new TestGlobalStateOverrideCollectionSize();
                    ContentHash                           hash        = ContentHash.ComputeFromBytes(new byte[] { 1, 2, 3 });
                    for (int i = 0; i < 20000; i++)
                        globalState.GameConfigVersions.Add("ExampleGameConfigName" + i.ToString(CultureInfo.InvariantCulture), hash);
                    MetaSerialized<TestGlobalStateOverrideCollectionSize> _ = new MetaSerialized<TestGlobalStateOverrideCollectionSize>(globalState, MetaSerializationFlags.IncludeAll, logicVersion: null);
                }, Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("Invalid value collection size 20000 (maximum allowed is 18000)"));

            Assert.That(
                () =>
                {
                    TestGlobalStateOverrideCollectionSize globalState = new TestGlobalStateOverrideCollectionSize();
                    for (int i = 0; i < 110; i++)
                        globalState.Nested.Add(Enumerable.Repeat(i, 110).ToList());
                    MetaSerialized<TestGlobalStateOverrideCollectionSize> _ = new MetaSerialized<TestGlobalStateOverrideCollectionSize>(globalState, MetaSerializationFlags.IncludeAll, logicVersion: null);
                }, Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("Invalid value collection size 110 (maximum allowed is 100)"));

            Assert.That(
                () =>
                {
                    TestGlobalStateOverrideCollectionSize globalState = new TestGlobalStateOverrideCollectionSize();
                    for (int i = 0; i < 90; i++)
                        globalState.Nested.Add(Enumerable.Repeat(i, 110).ToList());
                    MetaSerialized<TestGlobalStateOverrideCollectionSize> _ = new MetaSerialized<TestGlobalStateOverrideCollectionSize>(globalState, MetaSerializationFlags.IncludeAll, logicVersion: null);
                }, Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("Invalid value collection size 110 (maximum allowed is 100)"));

            TestGlobalStateOverrideCollectionSize globalState = new TestGlobalStateOverrideCollectionSize();
            ContentHash                           hash        = ContentHash.ComputeFromBytes(new byte[] { 1, 2, 3 });
            for (int i = 0; i < 18000; i++)
                globalState.GameConfigVersions.Add("ExampleGameConfigName" + i.ToString(CultureInfo.InvariantCulture), hash);
            MetaSerialized<TestGlobalStateOverrideCollectionSize> taggedSerialized = new MetaSerialized<TestGlobalStateOverrideCollectionSize>(globalState, MetaSerializationFlags.IncludeAll, logicVersion: null);
            TestGlobalStateOverrideCollectionSize                 taggedClone      = taggedSerialized.Deserialize(resolver: null, logicVersion: null);
            Assert.AreEqual(globalState.GameConfigVersions, taggedClone.GameConfigVersions);
        }

        [Test]
        public void TestGlobalStateSerialization()
        {
            TestGlobalState globalState = new TestGlobalState();
            globalState.GameConfigVersions.Add("ExampleGameConfigName", ContentHash.ComputeFromBytes(new byte[] { 1, 2, 3 }));

            //var compactSerialized = new CompactSerialized<TestGlobalState>(globalState, MetaSerializationFlags.IncludeAll);
            //var compactClone = compactSerialized.Deserialize(null);
            //Assert.AreEqual(globalState.GameConfigVersions, compactClone.GameConfigVersions);

            MetaSerialized<TestGlobalState> taggedSerialized = new MetaSerialized<TestGlobalState>(globalState, MetaSerializationFlags.IncludeAll, logicVersion: null);
            TestGlobalState taggedClone = taggedSerialized.Deserialize(resolver: null, logicVersion: null);
            Assert.AreEqual(globalState.GameConfigVersions, taggedClone.GameConfigVersions);
        }

        [Test]
        public void TestSortedSetDiff()
        {
            SortedSet<int> a = new SortedSet<int> { 3, 2, 1 };
            SortedSet<int> b = new SortedSet<int> { 2 };

            Assert.AreEqual("", PrettyPrinter.Difference(a, a));

            string diff = PrettyPrinter.Difference(a, b);
            Assert.AreNotEqual("", diff);
        }

        [Test]
        public void TestTaggedSerializerSkip()
        {
            TestObjectTypes input = new TestObjectTypes(init: true);
            byte[] serialized = MetaSerialization.SerializeTagged(input, MetaSerializationFlags.IncludeAll, logicVersion: null);

            TaggedWireSerializer.TestSkip(serialized);
        }

        [Test]
        public void TestTaggedSerializerToString()
        {
            TestObjectTypes input = new TestObjectTypes(init: true);
            byte[] serialized = MetaSerialization.SerializeTagged(input, MetaSerializationFlags.IncludeAll, logicVersion: null);

            string str = TaggedWireSerializer.ToString(serialized);
            // \todo [nuutti] better asserts

            // \todo [nuutti] This is not a really robust way of signaling/checking failure of TaggedWireSerializer.ToString
            if (str.Contains("FAILED"))
                Assert.Fail("TaggedWireSerializer.ToString result contains \"FAILED\"");
        }

        [Test]
        public void TestTaggedSerializerToStringFailure()
        {
            TestObjectTypes input = new TestObjectTypes(init: true);
            byte[] serialized = MetaSerialization.SerializeTagged(input, MetaSerializationFlags.IncludeAll, logicVersion: null);
            byte[] brokenSerialized = serialized.Take(serialized.Length / 2).ToArray();

            string failureStr = TaggedWireSerializer.ToString(brokenSerialized);

            // \todo [nuutti] This is not a really robust way of signaling/checking failure of TaggedWireSerializer.ToString
            if (!failureStr.Contains("FAILED"))
                Assert.Fail("TaggedWireSerializer.ToString result should contain \"FAILED\", but doesn't");
        }

        [Test]
        public void TestTaggedSerializer()
        {
            TestObjectTypes input = new TestObjectTypes(init: true);
            byte[] serialized = MetaSerialization.SerializeTagged(input, MetaSerializationFlags.IncludeAll, logicVersion: null);
            TestObjectTypes clone = MetaSerialization.DeserializeTagged<TestObjectTypes>(serialized, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);

            clone.AssertEqual(input);
        }

        //[Test]
        //public void TestCompactSerialized()
        //{
        //    TestObjectTypes input = new TestObjectTypes(init: true);
        //    CompactSerialized<TestObjectTypes> serialized = new CompactSerialized<TestObjectTypes>(input, MetaSerializationFlags.IncludeAll);
        //    TestObjectTypes clone = serialized.Deserialize(null);
        //
        //    clone.AssertEqual(input);
        //}

        [Test]
        public void TestTaggedSerialized()
        {
            TestObjectTypes input = new TestObjectTypes(init: true);
            MetaSerialized<TestObjectTypes> serialized = new MetaSerialized<TestObjectTypes>(input, MetaSerializationFlags.IncludeAll, logicVersion: null);
            TestObjectTypes clone = serialized.Deserialize(resolver: null, logicVersion: null);

            clone.AssertEqual(input);
        }

        [Test]
        public void TestTaggedClone()
        {
            TestObjectTypes input = new TestObjectTypes(init: true);
            TestObjectTypes clone = MetaSerialization.CloneTagged(input, MetaSerializationFlags.IncludeAll, null, null);

            Assert.False(ReferenceEquals(clone, input));
            clone.AssertEqual(input);
        }

        [Test]
        public void TestTaggedSerializerFlags()
        {
            TestObjectWithFlags input = new TestObjectWithFlags();
            input.Persisted = 20;
            input.Transient = 10;
            input.ServerOnly = 20;

            byte[] persisted = MetaSerialization.SerializeTagged(input, MetaSerializationFlags.Persisted, logicVersion: null);
            TestObjectWithFlags clone = MetaSerialization.DeserializeTagged<TestObjectWithFlags>(persisted, MetaSerializationFlags.Persisted, resolver: null, logicVersion: null);

            Assert.AreEqual(20, clone.Persisted);
            Assert.AreEqual(0, clone.Transient);
            Assert.AreEqual(20, clone.ServerOnly);
        }

        [Test]
        public void TestTaggedFlagsAndDeserializationFailureHandlers()
        {
            TestObjectWithFlagsAndDeserializationFailureHandlers input = new TestObjectWithFlagsAndDeserializationFailureHandlers();
            input.Persisted = 20;
            input.Transient = 10;
            input.ServerOnly = 20;

            byte[] persisted = MetaSerialization.SerializeTagged(input, MetaSerializationFlags.Persisted, logicVersion: null);
            TestObjectWithFlagsAndDeserializationFailureHandlers clone = MetaSerialization.DeserializeTagged<TestObjectWithFlagsAndDeserializationFailureHandlers>(persisted, MetaSerializationFlags.Persisted, resolver: null, logicVersion: null);

            Assert.AreEqual(20, clone.Persisted);
            Assert.AreEqual(0, clone.Transient);
            Assert.AreEqual(20, clone.ServerOnly);
        }

        [Test]
        public void TestTaggedSerializerInheritedFlags()
        {
            TestObjectWithInheritedFlags input = new TestObjectWithInheritedFlags();
            input.Persisted = 20;
            input.Transient = 10;
            input.ServerOnly = 20;

            byte[] persisted = MetaSerialization.SerializeTagged(input, MetaSerializationFlags.Persisted, logicVersion: null);
            TestObjectWithInheritedFlags clone = MetaSerialization.DeserializeTagged<TestObjectWithInheritedFlags>(persisted, MetaSerializationFlags.Persisted, resolver: null, logicVersion: null);

            Assert.AreEqual(20, clone.Persisted);
            Assert.AreEqual(0, clone.Transient);
            Assert.AreEqual(20, clone.ServerOnly);
        }

        [Test]
        public void TestTaggedSerializerInheritedFlagsDerived()
        {
            TestObjectWithInheritedFlagsDerived input = new TestObjectWithInheritedFlagsDerived();
            input.Persisted = 20;
            input.Transient = 10;
            input.ServerOnly = 20;

            byte[] persisted = MetaSerialization.SerializeTagged((TestObjectWithInheritedFlagsBase)input, MetaSerializationFlags.Persisted, logicVersion: null);
            TestObjectWithInheritedFlagsBase clone = MetaSerialization.DeserializeTagged<TestObjectWithInheritedFlagsBase>(persisted, MetaSerializationFlags.Persisted, resolver: null, logicVersion: null);

            Assert.AreEqual(20, clone.Persisted);
            Assert.AreEqual(0, clone.Transient);
            Assert.AreEqual(20, clone.ServerOnly);
        }

        [Test]
        public void TestDeserializeDropTransient()
        {
            TestObjectNoFlags input = new TestObjectNoFlags();
            input.Persisted = 20;
            input.Transient = 10;
            input.ServerOnly = 30;

            byte[] persisted = MetaSerialization.SerializeTagged(input, MetaSerializationFlags.Persisted, logicVersion: null);
            TestObjectWithFlags clone = MetaSerialization.DeserializeTagged<TestObjectWithFlags>(persisted, MetaSerializationFlags.Persisted, resolver: null, logicVersion: null);

            Assert.AreEqual(20, clone.Persisted);
            Assert.AreEqual(0, clone.Transient);
            Assert.AreEqual(30, clone.ServerOnly);
        }

        [Test]
        public void TestAddedMemberSerialization()
        {
            TestVersionedState input = new TestVersionedState
            {
                AddedMember = 100,
            };

            // If serializing with logicVersion==0, AddedMember should be discarded
            byte[] serializedV0 = MetaSerialization.SerializeTagged(input, MetaSerializationFlags.IncludeAll, logicVersion: 0);
            TestVersionedState cloneV0 = MetaSerialization.DeserializeTagged<TestVersionedState>(serializedV0, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);
            Assert.AreEqual(0, cloneV0.AddedMember);

            // If serializing with logicVersion==1, AddedMember should be retained
            byte[] serializedV1 = MetaSerialization.SerializeTagged(input, MetaSerializationFlags.IncludeAll, logicVersion: 1);
            TestVersionedState cloneV1 = MetaSerialization.DeserializeTagged<TestVersionedState>(serializedV1, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);
            Assert.AreEqual(100, cloneV1.AddedMember);
        }

        [Test]
        public void TestRemovedMemberSerialization()
        {
            TestVersionedState input = new TestVersionedState
            {
                AddedAndRemoved = 100,
            };

            // If serializing with logicVersion==0, AddedAndRemoved should be discarded
            byte[] serializedV0 = MetaSerialization.SerializeTagged(input, MetaSerializationFlags.IncludeAll, logicVersion: 0);
            TestVersionedState cloneV0 = MetaSerialization.DeserializeTagged<TestVersionedState>(serializedV0, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);
            Assert.AreEqual(0, cloneV0.AddedAndRemoved);

            // If serializing with logicVersion==1, AddedAndRemoved should be retained
            byte[] serializedV1 = MetaSerialization.SerializeTagged(input, MetaSerializationFlags.IncludeAll, logicVersion: 1);
            TestVersionedState cloneV1 = MetaSerialization.DeserializeTagged<TestVersionedState>(serializedV1, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);
            Assert.AreEqual(100, cloneV1.AddedAndRemoved);

            // If serializing with logicVersion==2, AddedAndRemoved should be discarded
            byte[] serializedV2 = MetaSerialization.SerializeTagged(input, MetaSerializationFlags.IncludeAll, logicVersion: 2);
            TestVersionedState cloneV2 = MetaSerialization.DeserializeTagged<TestVersionedState>(serializedV2, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);
            Assert.AreEqual(0, cloneV2.AddedAndRemoved);
        }

        [Test]
        public void TestAddedAndRemovedMemberSerialization()
        {
            TestVersionedState input = new TestVersionedState
            {
                AddedAndRemoved = 100,
            };

            // If serializing with logicVersion==0, AddedMember should be discarded
            byte[] serializedV0 = MetaSerialization.SerializeTagged(input, MetaSerializationFlags.IncludeAll, logicVersion: 0);
            TestVersionedState cloneV0 = MetaSerialization.DeserializeTagged<TestVersionedState>(serializedV0, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);
            Assert.AreEqual(0, cloneV0.AddedAndRemoved);

            // If serializing with logicVersion==1, AddedMember should be retained
            byte[] serializedV1 = MetaSerialization.SerializeTagged(input, MetaSerializationFlags.IncludeAll, logicVersion: 1);
            TestVersionedState cloneV1 = MetaSerialization.DeserializeTagged<TestVersionedState>(serializedV1, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);
            Assert.AreEqual(100, cloneV1.AddedAndRemoved);
        }

        //[Test]
        //public void TestAuthClaimSerialization()
        //{
        //    SocialAuthenticationClaimDevelopment claim = new SocialAuthenticationClaimDevelopment("XX", "YY");
        //    byte[] tagged = MetaSerialization.SerializeTagged<SocialAuthenticationClaimBase>(claim, MetaSerializationFlags.IncludeAll);
        //    SocialAuthenticationClaimDevelopment taggedClone = (SocialAuthenticationClaimDevelopment)MetaSerialization.DeserializeTagged<SocialAuthenticationClaimBase>(tagged, MetaSerializationFlags.IncludeAll, null);
        //
        //    byte[] compact = MetaSerialization.SerializeCompact<SocialAuthenticationClaimBase>(claim, MetaSerializationFlags.IncludeAll);
        //    SocialAuthenticationClaimDevelopment compactClone = (SocialAuthenticationClaimDevelopment)MetaSerialization.DeserializeCompact<SocialAuthenticationClaimBase>(compact, MetaSerializationFlags.IncludeAll, null);
        //
        //    string taggedResult = PrettyPrinter.Verbose(taggedClone);
        //    string compactResult = PrettyPrinter.Verbose(compactClone);
        //    Assert.AreEqual(taggedResult, compactResult);
        //}

        [Test]
        public void TestStringIdComparisons()
        {
            TestStringId a = TestStringId.FromString("abba");
            TestStringId b = TestStringId.FromString("ba");
            TestStringId c = TestStringId.FromString("ba");
            TestStringId n = null;

            Assert.AreEqual(a, a);
            Assert.AreEqual(b, c);
            Assert.AreNotEqual(a, b);
            Assert.AreNotEqual(a, b);
            Assert.AreNotEqual(a, n);
            Assert.AreNotEqual(n, a);
            Assert.AreEqual(n, n);
        }

        [Test]
        public void TestSerializableOnlyToSerializableDerived()
        {
            // Test that serialized class with [MetaSerializable] can be deserialized to class with [MetaSerializableDerived()] when not using base types
            WithOnlySerializable orig = new WithOnlySerializable { Value = "VALUE" };
            byte[] serialized = MetaSerialization.SerializeTagged(orig, MetaSerializationFlags.IncludeAll, null);

            WithSerializableDerived clone = MetaSerialization.DeserializeTagged<WithSerializableDerived>(serialized, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);
            Assert.AreEqual(orig.Value, clone.Value);
        }

        [Test]
        public void TestSerializableDerivedToSerializableOnly()
        {
            // Test that serialized class with [MetaSerializableDerived()] can be deserialized to class with [MetaSerializable] when not using base types
            WithSerializableDerived orig = new WithSerializableDerived { Value = "VALUE" };
            byte[] serialized = MetaSerialization.SerializeTagged(orig, MetaSerializationFlags.IncludeAll, null);

            WithOnlySerializable clone = MetaSerialization.DeserializeTagged<WithOnlySerializable>(serialized, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);
            Assert.AreEqual(orig.Value, clone.Value);
        }

        [Test]
        public void TestSerializableOnlyToSerializableDerivedInContainer()
        {
            // Test that serialized class with [MetaSerializable] can be deserialized to class with [MetaSerializableDerived()] when stored as direct or MetaSerialized member in another class
            WithOnlySerializableContainer orig = new WithOnlySerializableContainer
            {
                Direct      = new WithOnlySerializable { Value = "Direct" },
                Serialized  = new MetaSerialized<WithOnlySerializable>(new WithOnlySerializable { Value = "Serialized" }, MetaSerializationFlags.IncludeAll, logicVersion: null)
            };
            byte[] serialized = MetaSerialization.SerializeTagged(orig, MetaSerializationFlags.IncludeAll, null);

            WithSerializableDerivedContainer clone = MetaSerialization.DeserializeTagged<WithSerializableDerivedContainer>(serialized, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);
            Assert.AreEqual(orig.Direct.Value, clone.Direct.Value);

            Assert.AreEqual("Serialized", clone.Serialized.Deserialize(resolver: null, logicVersion: null).Value);
        }

        [Test]
        public void TestSerializableDerivedToSerializableOnlyInContainer()
        {
            // Test that serialized class with [MetaSerializableDerived()] can be deserialized to class with [MetaSerializable] when stored as direct or MetaSerialized member in another class
            WithSerializableDerivedContainer orig = new WithSerializableDerivedContainer
            {
                Direct      = new WithSerializableDerived { Value = "Direct" },
                Serialized  = new MetaSerialized<WithSerializableDerived>(new WithSerializableDerived { Value = "Serialized" }, MetaSerializationFlags.IncludeAll, logicVersion: null)
            };
            byte[] serialized = MetaSerialization.SerializeTagged(orig, MetaSerializationFlags.IncludeAll, null);

            WithOnlySerializableContainer clone = MetaSerialization.DeserializeTagged<WithOnlySerializableContainer>(serialized, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);
            Assert.AreEqual(orig.Direct.Value, clone.Direct.Value);

            Assert.AreEqual("Serialized", clone.Serialized.Deserialize(resolver: null, logicVersion: null).Value);
        }

        [Test]
        public void TestAccessModifiers()
        {
            {
                TestSimpleWithAccessModifiers original = new TestSimpleWithAccessModifiers(true);
                MetaSerialization.CloneTagged(original, MetaSerializationFlags.IncludeAll, null, null)
                    .AssertEqual(original);
            }
            {
                TestDerivedNonPolymorphicWithAccessModifiers original = new TestDerivedNonPolymorphicWithAccessModifiers(true);
                MetaSerialization.CloneTagged(original, MetaSerializationFlags.IncludeAll, null, null)
                    .AssertEqual(original);
            }
            {
                TestDerivedPolymorphicWithAccessModifiers original = new TestDerivedPolymorphicWithAccessModifiers(true);
                MetaSerialization.CloneTagged(original, MetaSerializationFlags.IncludeAll, null, null)
                    .AssertEqual(original);
            }
        }

        /// <summary>
        /// Test that tagged-serialized <see cref="TestObjectTypes"/> is equal to a reference blob that's assumed to be correct.
        /// This is intended to caught accidental format changes in the serializer. Sometimes the serializer and/or <see cref="TestObjectTypes"/>
        /// are intentionally changed (e.g. extended), in which case the reference here should be updated (when you're confident your change
        /// is correct).
        /// </summary>
        [Test]
        public void TestEqualToAssumedGoodReference()
        {
            // \note Update assumedGoodReference when serializer and/or TestObjectTypes is intentionally changed

            TestObjectTypes input = new TestObjectTypes(init: true);
            byte[] result = MetaSerialization.SerializeTagged(input, MetaSerializationFlags.IncludeAll, logicVersion: null);

            // For convenience when reference needs to be updated
            string resultBytesStr = string.Concat(ToChunks(result, 32).Select(chunk => Util.BytesToString(chunk.ToArray()) + ",\n"));
            _ = resultBytesStr;

            byte[] reference = new byte[]
            {
                15, 2, 2, 4, 30, 2, 6, 128, 192, 223, 218, 142, 233, 6, 12, 8, 0, 12, 10, 6, 115, 116, 114, 12, 12, 1, 2, 14, 33, 2, 16, 42,
                2, 18, 241, 192, 1, 2, 20, 177, 168, 3, 2, 22, 1, 2, 24, 194, 96, 10, 40, 0, 0, 132, 65, 11, 42, 0, 0, 0, 0, 0, 64, 64,
                64, 15, 44, 2, 21, 2, 2, 1, 21, 4, 2, 33, 21, 6, 2, 42, 21, 8, 2, 241, 192, 1, 21, 10, 2, 177, 168, 3, 21, 12, 2, 30,
                21, 14, 2, 131, 213, 169, 240, 12, 21, 16, 2, 128, 192, 223, 218, 142, 233, 6, 21, 18, 2, 246, 231, 223, 141, 207, 177, 170, 170, 171, 1, 22,
                20, 2, 198, 187, 224, 233, 216, 196, 224, 199, 249, 217, 221, 197, 207, 141, 220, 254, 217, 141, 3, 23, 24, 2, 0, 3, 36, 63, 24, 26, 2, 0,
                3, 36, 63, 0, 6, 72, 126, 25, 28, 2, 0, 3, 36, 63, 0, 6, 72, 126, 0, 1, 146, 31, 26, 30, 2, 0, 0, 0, 3, 36, 63, 106,
                137, 27, 32, 2, 0, 0, 0, 3, 36, 63, 106, 137, 0, 0, 0, 6, 72, 126, 213, 17, 28, 34, 2, 0, 0, 0, 3, 36, 63, 106, 137, 0,
                0, 0, 6, 72, 126, 213, 17, 0, 0, 0, 1, 146, 31, 181, 68, 29, 36, 2, 0, 0, 132, 65, 30, 38, 2, 0, 0, 0, 0, 0, 64, 64,
                64, 21, 40, 2, 194, 96, 32, 42, 2, 18, 52, 86, 120, 144, 171, 205, 224, 249, 143, 130, 37, 141, 56, 29, 198, 21, 44, 2, 2, 15, 50, 2,
                2, 2, 128, 252, 228, 247, 159, 246, 222, 219, 17, 2, 4, 128, 208, 145, 142, 134, 1, 17, 13, 52, 32, 1, 0, 0, 0, 2, 0, 3, 0, 4,
                5, 6, 7, 8, 9, 10, 11, 21, 54, 2, 170, 180, 222, 117, 17, 15, 46, 2, 21, 2, 0, 21, 4, 0, 21, 6, 0, 21, 8, 0, 21, 10,
                0, 21, 12, 0, 21, 14, 0, 21, 16, 0, 21, 18, 0, 22, 20, 0, 23, 24, 0, 24, 26, 0, 25, 28, 0, 26, 30, 0, 27, 32, 0, 28,
                34, 0, 29, 36, 0, 30, 38, 0, 21, 40, 0, 32, 42, 0, 21, 44, 0, 15, 50, 0, 13, 52, 1, 21, 54, 0, 17, 12, 48, 12, 70, 111,
                111, 98, 97, 114, 12, 50, 1, 31, 52, 18, 52, 86, 120, 144, 171, 205, 224, 249, 143, 130, 37, 141, 56, 29, 198, 2, 58, 2, 2, 60, 131, 213,
                169, 240, 12, 2, 62, 246, 231, 223, 141, 207, 177, 170, 170, 171, 1, 3, 64, 198, 187, 224, 233, 216, 196, 224, 199, 249, 217, 221, 197, 207, 141, 220,
                254, 217, 141, 3, 4, 66, 0, 3, 36, 63, 5, 68, 0, 3, 36, 63, 0, 6, 72, 126, 6, 70, 0, 3, 36, 63, 0, 6, 72, 126, 0, 1,
                146, 31, 7, 72, 0, 0, 0, 3, 36, 63, 106, 137, 8, 74, 0, 0, 0, 3, 36, 63, 106, 137, 0, 0, 0, 6, 72, 126, 213, 17, 9, 76,
                0, 0, 0, 3, 36, 63, 106, 137, 0, 0, 0, 6, 72, 126, 213, 17, 0, 0, 0, 1, 146, 31, 181, 68, 18, 100, 4, 2, 9, 9, 18, 102,
                0, 2, 18, 104, 1, 18, 120, 6, 2, 1, 0, 254, 207, 172, 243, 14, 18, 122, 0, 2, 18, 124, 1, 13, 126, 6, 42, 123, 255, 13, 128, 1,
                0, 13, 130, 1, 1, 18, 132, 1, 4, 15, 0, 2, 2, 2, 0, 17, 19, 140, 1, 4, 2, 12, 25, 6, 66, 97, 114, 10, 6, 102, 111, 111,
                19, 142, 1, 0, 2, 12, 19, 144, 1, 1, 19, 146, 1, 2, 12, 16, 8, 97, 98, 98, 97, 3, 2, 171, 144, 156, 184, 211, 195, 221, 155, 249,
                224, 221, 129, 152, 142, 166, 225, 170, 250, 1, 17, 15, 246, 1, 2, 15, 2, 2, 2, 200, 1, 0, 16, 202, 1, 2, 2, 0, 17, 17, 17, 15,
                248, 1, 0, 18, 188, 15, 1, 18, 190, 15, 8, 2, 157, 156, 1, 2, 6, 10, 18, 192, 15, 6, 12, 1, 0, 6, 102, 117, 102, 18, 198, 15,
                0, 12, 18, 200, 15, 4, 12, 1, 10, 101, 110, 95, 117, 115, 18, 208, 15, 10, 2, 2, 4, 12, 18, 20, 18, 210, 15, 12, 12, 2, 97, 2,
                98, 1, 2, 99, 2, 100, 2, 101, 19, 212, 15, 10, 2, 2, 0, 2, 2, 4, 5, 7, 16, 16, 20, 0, 19, 214, 15, 10, 12, 12, 2, 48,
                2, 49, 2, 49, 1, 1, 2, 50, 0, 2, 102, 6, 102, 111, 111, 6, 98, 97, 114, 15, 234, 15, 2, 2, 2, 164, 19, 17, 18, 236, 15, 6,
                15, 2, 2, 2, 24, 17, 0, 2, 2, 2, 68, 17, 18, 242, 15, 6, 15, 2, 2, 2, 246, 1, 17, 0, 2, 2, 2, 144, 7, 17, 14, 248,
                15, 2, 2, 2, 24, 2, 4, 68, 17, 14, 250, 15, 4, 2, 2, 112, 2, 4, 156, 1, 17, 14, 252, 15, 4, 2, 2, 246, 1, 2, 4, 144,
                7, 17, 14, 140, 16, 2, 2, 2, 246, 1, 17, 14, 142, 16, 4, 2, 2, 144, 7, 17, 14, 144, 16, 6, 2, 2, 170, 12, 17, 14, 146, 16,
                8, 2, 2, 182, 15, 17, 14, 148, 16, 6, 2, 2, 246, 1, 17, 14, 150, 16, 8, 2, 2, 144, 7, 17, 15, 162, 16, 0, 15, 164, 16, 2,
                2, 200, 1, 246, 1, 17, 2, 180, 16, 254, 255, 255, 255, 15, 2, 182, 16, 254, 255, 255, 255, 15, 2, 184, 16, 254, 1, 2, 186, 16, 255, 1,
                2, 188, 16, 254, 255, 3, 2, 190, 16, 255, 255, 3, 2, 192, 16, 254, 255, 255, 255, 15, 2, 194, 16, 255, 255, 255, 255, 15, 2, 196, 16, 254,
                255, 255, 255, 255, 255, 255, 255, 255, 1, 2, 198, 16, 255, 255, 255, 255, 255, 255, 255, 255, 255, 1, 15, 200, 16, 2, 21, 2, 2, 254, 255, 255,
                255, 15, 21, 4, 2, 254, 1, 21, 6, 2, 255, 1, 21, 8, 2, 254, 255, 3, 21, 10, 2, 255, 255, 3, 21, 12, 2, 254, 255, 255, 255, 15,
                21, 14, 2, 255, 255, 255, 255, 15, 21, 16, 2, 254, 255, 255, 255, 255, 255, 255, 255, 255, 1, 21, 18, 2, 255, 255, 255, 255, 255, 255, 255, 255,
                255, 1, 17, 15, 202, 16, 2, 21, 2, 0, 21, 4, 0, 21, 6, 0, 21, 8, 0, 21, 10, 0, 21, 12, 0, 21, 14, 0, 21, 16, 0, 21,
                18, 0, 17, 16, 152, 17, 13, 2, 12, 15, 2, 2, 2, 0, 17, 2, 4, 0, 17, 18, 154, 17, 8, 18, 1, 0, 2, 2, 2, 1, 6, 2,
                2, 4, 6, 18, 156, 17, 4, 18, 1, 4, 18, 1, 2, 18, 4, 18, 1, 4, 2, 2, 4, 15, 158, 17, 2, 16, 2, 2, 2, 200, 1, 2,
                4, 1, 17, 16, 4, 2, 2, 200, 1, 2, 4, 1, 2, 6, 164, 19, 17, 16, 6, 2, 2, 200, 1, 2, 4, 1, 2, 6, 164, 19, 2, 8,
                0, 17, 16, 8, 2, 2, 200, 1, 2, 4, 1, 2, 6, 164, 19, 2, 8, 0, 2, 10, 24, 2, 12, 1, 2, 14, 48, 17, 16, 10, 18, 2,
                200, 1, 2, 2, 4, 6, 8, 10, 12, 14, 16, 18, 20, 22, 24, 26, 28, 30, 32, 34, 36, 38, 40, 42, 44, 46, 48, 50, 52, 54, 56, 58,
                60, 62, 64, 66, 68, 70, 72, 74, 76, 78, 80, 82, 84, 86, 88, 90, 92, 94, 96, 98, 100, 102, 104, 106, 108, 110, 112, 114, 116, 118, 120, 122,
                124, 126, 128, 1, 130, 1, 132, 1, 134, 1, 136, 1, 138, 1, 140, 1, 142, 1, 144, 1, 146, 1, 148, 1, 150, 1, 152, 1, 154, 1, 156, 1,
                158, 1, 160, 1, 162, 1, 164, 1, 166, 1, 168, 1, 170, 1, 172, 1, 174, 1, 176, 1, 178, 1, 180, 1, 182, 1, 184, 1, 186, 1, 188, 1,
                190, 1, 192, 1, 194, 1, 196, 1, 198, 1, 200, 1, 2, 4, 1, 17, 15, 12, 2, 2, 2, 200, 1, 2, 4, 1, 17, 15, 14, 0, 17, 13,
                160, 17, 32, 1, 0, 0, 0, 2, 0, 3, 0, 4, 5, 6, 7, 8, 9, 10, 11, 2, 162, 17, 170, 180, 222, 117, 16, 164, 17, 2, 2, 128,
                252, 228, 247, 159, 246, 222, 219, 17, 2, 4, 128, 208, 145, 142, 134, 1, 17, 15, 166, 17, 2, 2, 2, 2, 2, 4, 4, 2, 6, 6, 2, 8,
                8, 17, 18, 168, 17, 4, 2, 9, 9, 19, 170, 17, 4, 2, 12, 10, 6, 102, 111, 111, 25, 6, 66, 97, 114, 17,
            };

            // For convenience when debugging
            string referenceStr = TaggedWireSerializer.ToString(reference);
            string resultStr    = TaggedWireSerializer.ToString(result);
            _ = referenceStr;
            _ = resultStr;

            Assert.AreEqual(reference, result);
        }

        static IEnumerable<IEnumerable<T>> ToChunks<T>(IEnumerable<T> source, int chunkSize)
        {
            for (IEnumerable<T> remaining = source; remaining.Any(); remaining = remaining.Skip(chunkSize))
                yield return remaining.Take(chunkSize);
        }

        [MetaAllowNoSerializedMembers]
        [MetaSerializable]
        public abstract class MetaOnDeserializedTestBase
        {
            public int          Counter;
            public List<string> Infos = new List<string>();

            public void AddInfo(string info)
            {
                Counter++;
                Infos.Add(info);
            }

            [MetaOnDeserialized] public void BasePublic()       => AddInfo("BasePublic");
            [MetaOnDeserialized] protected void BaseProtected() => AddInfo("BaseProtected");
            [MetaOnDeserialized] private void BasePrivate()     => AddInfo("BasePrivate");
            [MetaOnDeserialized] internal void BaseInternal()   => AddInfo("BaseInternal");

            protected abstract void BaseAbstract();

            protected virtual void BaseVirtual() => AddInfo("BaseVirtual_BaseImpl");
        }

        [MetaSerializableDerived(1)]
        public class MetaOnDeserializedTestDerived : MetaOnDeserializedTestBase
        {
            [MetaOnDeserialized] public void DerivedPublic()       => AddInfo("DerivedPublic");
            [MetaOnDeserialized] protected void DerivedProtected() => AddInfo("DerivedProtected");
            [MetaOnDeserialized] private void DerivedPrivate()     => AddInfo("DerivedPrivate");
            [MetaOnDeserialized] internal void DerivedInternal()   => AddInfo("DerivedInternal");

            [MetaOnDeserialized] protected sealed override void BaseAbstract() => AddInfo("BaseAbstract");

            [MetaOnDeserialized] protected sealed override void BaseVirtual() => AddInfo("BaseVirtual_DerivedImpl");
        }

        [MetaAllowNoSerializedMembers]
        [MetaSerializable]
        public struct MetaOnDeserializedTestStruct
        {
            public int          Counter;
            public List<string> Infos;

            public void AddInfo(string info)
            {
                Counter++;

                if (Infos == null)
                    Infos = new List<string>();
                Infos.Add(info);
            }

            [MetaOnDeserialized] public void Public()       => AddInfo("Public");
            [MetaOnDeserialized] private void Private()     => AddInfo("Private");
            [MetaOnDeserialized] internal void Internal()   => AddInfo("Internal");
        }

        [MetaSerializable]
        public class MetaOnDeserializedWrapper
        {
            [MetaMember(1)] public MetaOnDeserializedTestDerived    Derived;
            [MetaMember(2)] public MetaOnDeserializedTestBase       Base;
            [MetaMember(3)] public MetaOnDeserializedTestStruct     Struct;

            [MetaMember(4)] public List<MetaOnDeserializedTestDerived>  DerivedList;
            [MetaMember(5)] public List<MetaOnDeserializedTestBase>     BaseList;
            [MetaMember(6)] public List<MetaOnDeserializedTestStruct>   StructList;

            MetaOnDeserializedWrapper(){ }
            public static MetaOnDeserializedWrapper Create()
            {
                return new MetaOnDeserializedWrapper
                {
                    Derived = new MetaOnDeserializedTestDerived(),
                    Base    = new MetaOnDeserializedTestDerived(),
                    Struct  = new MetaOnDeserializedTestStruct(),

                    DerivedList = new List<MetaOnDeserializedTestDerived>(){ new MetaOnDeserializedTestDerived(), new MetaOnDeserializedTestDerived() },
                    BaseList    = new List<MetaOnDeserializedTestBase>(){ new MetaOnDeserializedTestDerived(), new MetaOnDeserializedTestDerived() },
                    StructList  = new List<MetaOnDeserializedTestStruct>(){ new MetaOnDeserializedTestStruct(), new MetaOnDeserializedTestStruct() },
                };
            }
        }

        [Test]
        public void TestMetaOnDeserialized()
        {
            List<string> ClassReferenceInfos = new List<string>
            {
                "BasePublic",
                "BaseProtected",
                "BasePrivate",
                "BaseInternal",

                "DerivedPublic",
                "DerivedProtected",
                "DerivedPrivate",
                "DerivedInternal",

                "BaseAbstract",

                "BaseVirtual_DerivedImpl",
            };

            List<string> StructReferenceInfos = new List<string>
            {
                "Public",
                "Private",
                "Internal",
            };

            void CheckTestClass(MetaOnDeserializedTestDerived result)
            {
                Assert.AreEqual(ClassReferenceInfos.Count, result.Counter);
                Assert.AreEqual(ClassReferenceInfos, result.Infos);
            }

            void CheckTestStruct(MetaOnDeserializedTestStruct result)
            {
                Assert.AreEqual(StructReferenceInfos.Count, result.Counter);
                Assert.AreEqual(StructReferenceInfos, result.Infos);
            }

            // Serializing as derived
            {
                MetaOnDeserializedTestDerived original = new MetaOnDeserializedTestDerived();
                byte[] serialized = MetaSerialization.SerializeTagged(original, MetaSerializationFlags.IncludeAll, logicVersion: null);
                MetaOnDeserializedTestDerived deserialized = MetaSerialization.DeserializeTagged<MetaOnDeserializedTestDerived>(serialized, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);

                CheckTestClass(deserialized);
            }

            // Serializing as base
            {
                MetaOnDeserializedTestDerived original = new MetaOnDeserializedTestDerived();
                byte[] serialized = MetaSerialization.SerializeTagged<MetaOnDeserializedTestBase>(original, MetaSerializationFlags.IncludeAll, logicVersion: null);
                MetaOnDeserializedTestBase deserializedBase = MetaSerialization.DeserializeTagged<MetaOnDeserializedTestBase>(serialized, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);

                CheckTestClass((MetaOnDeserializedTestDerived)deserializedBase);
            }

            // Struct
            {
                MetaOnDeserializedTestStruct original = new MetaOnDeserializedTestStruct();
                byte[] serialized = MetaSerialization.SerializeTagged(original, MetaSerializationFlags.IncludeAll, logicVersion: null);
                MetaOnDeserializedTestStruct deserialized = MetaSerialization.DeserializeTagged<MetaOnDeserializedTestStruct>(serialized, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);

                CheckTestStruct(deserialized);
            }

            // Wrapper containing Derived, Base, Struct, and also List<> of each of those
            {
                MetaOnDeserializedWrapper original = MetaOnDeserializedWrapper.Create();
                byte[] serialized = MetaSerialization.SerializeTagged(original, MetaSerializationFlags.IncludeAll, logicVersion: null);
                MetaOnDeserializedWrapper deserialized = MetaSerialization.DeserializeTagged<MetaOnDeserializedWrapper>(serialized, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);

                CheckTestClass(deserialized.Derived);
                CheckTestClass((MetaOnDeserializedTestDerived)deserialized.Base);
                CheckTestStruct(deserialized.Struct);

                Assert.AreEqual(2, deserialized.DerivedList.Count);
                Assert.AreEqual(2, deserialized.BaseList.Count);
                Assert.AreEqual(2, deserialized.StructList.Count);
                foreach (MetaOnDeserializedTestDerived derived in deserialized.DerivedList)
                    CheckTestClass(derived);
                foreach (MetaOnDeserializedTestBase testBase in deserialized.BaseList)
                    CheckTestClass((MetaOnDeserializedTestDerived)testBase);
                foreach (MetaOnDeserializedTestStruct testStruct in deserialized.StructList)
                    CheckTestStruct(testStruct);
            }
        }

        [MetaAllowNoSerializedMembers]
        [MetaSerializable]
        public class MetaOnDeserializedParamsTest
        {
            public string ResolvedString;
            public int LogicVersion;

            [MetaOnDeserialized]
            public void OnDeserialized(MetaOnDeserializedParams onDeserializedParams)
            {
                ResolvedString = (string)onDeserializedParams.Resolver.ResolveReference(typeof(string), 42);
                LogicVersion = onDeserializedParams.LogicVersion.Value;
            }
        }

        public class MetaOnDeserializedTestResolver : IGameConfigDataResolver
        {
            public object TryResolveReference(Type type, object configKey)
            {
                if (type == typeof(string) && configKey.Equals(42))
                    return "dummy resolve result";
                else
                    return null;
            }
        }

        [Test]
        public void TestMetaOnDeserializedWithParams()
        {
            MetaOnDeserializedParamsTest original = new MetaOnDeserializedParamsTest();
            IGameConfigDataResolver resolver = new MetaOnDeserializedTestResolver();
            MetaOnDeserializedParamsTest clone = MetaSerialization.CloneTagged(original, MetaSerializationFlags.IncludeAll, logicVersion: 123, resolver: resolver);

            Assert.AreEqual(null, original.ResolvedString);
            Assert.AreEqual(0, original.LogicVersion);

            Assert.AreEqual("dummy resolve result", clone.ResolvedString);
            Assert.AreEqual(123, clone.LogicVersion);
        }

        #pragma warning disable CS0414 // The field <...> is assigned but its value is never used

        // MemberOrderTestClassA and MemberOrderTestClassB should serialize to the same, despite differences in member order etc. declaration.

        [MetaSerializable]
        public class MemberOrderTestClassA
        {
            [MetaMember(1)] public string       A { get; set; } = "magic0";
            [MetaMember(2)] protected string    B { get; set; } = "magic1";
            [MetaMember(3)] private string      C { get; set; } = "magic2";
            [MetaMember(4)] public string       D = "magic3";
            [MetaMember(5)] protected string    E = "magic4";
            [MetaMember(6)] private string      F = "magic5";
        }

        [MetaSerializable]
        public class MemberOrderTestClassB
        {
            [MetaMember(4)] public string       D = "magic3";
            [MetaMember(3)] private string      C { get; set; } = "magic2";
            [MetaMember(2)] protected string    B { get; set; } = "magic1";
            [MetaMember(6)] private string      F = "magic5";
            [MetaMember(5)] protected string    E = "magic4";
            [MetaMember(1)] public string       A { get; set; } = "magic0";
        }

        // MemberOrderTestClassDerivedA and MemberOrderTestClassDerivedB should serialize to the same, despite differences in member order etc. declaration.

        [MetaSerializable]
        public abstract class MemberOrderTestClassBaseA
        {
            [MetaMember(1)] public string       A { get; set; } = "magic0";
            [MetaMember(2)] protected string    B { get; set; } = "magic1";
            [MetaMember(3)] private string      C { get; set; } = "magic2";
        }

        [MetaSerializableDerived(1)]
        public class MemberOrderTestClassDerivedA : MemberOrderTestClassBaseA
        {
            [MetaMember(4)] public string       D = "magic3";
            [MetaMember(5)] protected string    E = "magic4";
            [MetaMember(6)] private string      F = "magic5";
        }

        [MetaSerializable]
        public abstract class MemberOrderTestClassBaseB
        {
            [MetaMember(4)] public string       D = "magic3";
            [MetaMember(3)] private string      C { get; set; } = "magic2";
            [MetaMember(5)] protected string    E = "magic4";
        }

        [MetaSerializableDerived(1)]
        public class MemberOrderTestClassDerivedB : MemberOrderTestClassBaseB
        {
            [MetaMember(2)] protected string  B { get; set; } = "magic1";
            [MetaMember(6)] private string    F = "magic5";
            [MetaMember(1)] public string     A { get; set; } = "magic0";
        }

        #pragma warning restore CS0414

        [Test]
        public void TestMembersOrderedByTagId()
        {
            void CheckExpectedMemberOrder(byte[] serialized)
            {
                // \note Kludgily treating serialized bytes as UTF-16 values just so we can easily use regex on it.
                //       Kludge but harmless for this test case.
                string serializedPseudoString = new string(serialized.Select(b => (char)b).ToArray());
                Assert.AreEqual(
                    new List<string>
                    {
                        "magic0",
                        "magic1",
                        "magic2",
                        "magic3",
                        "magic4",
                        "magic5",
                    },
                    new Regex("magic[0-9]").Matches(serializedPseudoString).Select(match => match.Value));
            }

            {
                byte[] serializedA = MetaSerialization.SerializeTagged(new MemberOrderTestClassA(), MetaSerializationFlags.IncludeAll, logicVersion: null);
                byte[] serializedB = MetaSerialization.SerializeTagged(new MemberOrderTestClassB(), MetaSerializationFlags.IncludeAll, logicVersion: null);

                Assert.AreEqual(serializedA, serializedB);
                CheckExpectedMemberOrder(serializedA);
            }

            {
                byte[] serializedA = MetaSerialization.SerializeTagged(new MemberOrderTestClassDerivedA(), MetaSerializationFlags.IncludeAll, logicVersion: null);
                byte[] serializedB = MetaSerialization.SerializeTagged(new MemberOrderTestClassDerivedB(), MetaSerializationFlags.IncludeAll, logicVersion: null);

                Assert.AreEqual(serializedA, serializedB);
                CheckExpectedMemberOrder(serializedA);
            }

            {
                byte[] serializedA = MetaSerialization.SerializeTagged<MemberOrderTestClassBaseA>(new MemberOrderTestClassDerivedA(), MetaSerializationFlags.IncludeAll, logicVersion: null);
                byte[] serializedB = MetaSerialization.SerializeTagged<MemberOrderTestClassBaseB>(new MemberOrderTestClassDerivedB(), MetaSerializationFlags.IncludeAll, logicVersion: null);

                Assert.AreEqual(serializedA, serializedB);
                CheckExpectedMemberOrder(serializedA);
            }
        }

        #pragma warning disable CS0414 // The field <...> is assigned but its value is never used

        [MetaSerializable(MetaSerializableFlags.ImplicitMembers)]
        [MetaImplicitMembersRange(1, 100)]
        public class ImplicitMembersTestClass
        {
            public string PublicProp0 { get; private set; } = "publicprop0";
            public string PublicField0 = "publicfield0";

            private string PrivateProp { get; set; } = "privateprop";
            private string PrivateField = "privatefield";

            protected string ProtectedProp { get; private set; } = "protectedprop";
            protected string ProtectedField = "protectedfield";

            public string PublicProp1 { get; private set; } = "publicprop1";
            public string PublicField1 = "publicfield1";
        }

        [MetaSerializable]
        public class ExplicitReferenceForImplicitMembersTestClass
        {
            [MetaMember(1)] public string PublicProp0 { get; private set; } = "publicprop0";
            [MetaMember(5)] public string PublicField0 = "publicfield0";

            [MetaMember(2)] private string PrivateProp { get; set; } = "privateprop";
            [MetaMember(6)] private string PrivateField = "privatefield";

            [MetaMember(3)] protected string ProtectedProp { get; private set; } = "protectedprop";
            [MetaMember(7)] protected string ProtectedField = "protectedfield";

            [MetaMember(4)] public string PublicProp1 { get; private set; } = "publicprop1";
            [MetaMember(8)] public string PublicField1 = "publicfield1";
        }

        [MetaSerializable(MetaSerializableFlags.ImplicitMembers)]
        [MetaImplicitMembersDefaultRangeForMostDerivedClass(1, 100)]
        [MetaImplicitMembersRange(100, 200)]
        public abstract class ImplicitMembersTestBase
        {
            public string PublicProp0Base { get; private set; } = "publicprop0base";
            public string PublicField0Base = "publicfield0base";

            private string PrivatePropBase { get; set; } = "privatepropbase";
            private string PrivateFieldBase = "privatefieldbase";

            protected string ProtectedPropBase { get; private set; } = "protectedpropbase";
            protected string ProtectedFieldBase = "protectedfieldbase";

            public string PublicProp1Base { get; private set; } = "publicprop1base";
            public string PublicField1Base = "publicfield1base";
        }

        [MetaSerializableDerived(1)]
        public class ImplicitMembersTestDerived : ImplicitMembersTestBase
        {
            public string PublicProp0Derived { get; private set; } = "publicprop0derived";
            public string PublicField0Derived = "publicfield0derived";

            private string PrivatePropDerived { get; set; } = "privatepropderived";
            private string PrivateFieldDerived = "privatefieldderived";

            protected string ProtectedPropDerived { get; private set; } = "protectedpropderived";
            protected string ProtectedFieldDerived = "protectedfieldderived";

            public string PublicProp1Derived { get; private set; } = "publicprop1derived";
            public string PublicField1Derived = "publicfield1derived";
        }

        [MetaSerializable]
        public class ExplicitReferenceForImplicitMembersTestBaseAndDerived
        {
            [MetaMember(100)] public string PublicProp0Base { get; private set; } = "publicprop0base";
            [MetaMember(104)] public string PublicField0Base = "publicfield0base";

            [MetaMember(101)] private string PrivatePropBase { get; set; } = "privatepropbase";
            [MetaMember(105)] private string PrivateFieldBase = "privatefieldbase";

            [MetaMember(102)] protected string ProtectedPropBase { get; private set; } = "protectedpropbase";
            [MetaMember(106)] protected string ProtectedFieldBase = "protectedfieldbase";

            [MetaMember(103)] public string PublicProp1Base { get; private set; } = "publicprop1base";
            [MetaMember(107)] public string PublicField1Base = "publicfield1base";

            [MetaMember(1)] public string PublicProp0Derived { get; private set; } = "publicprop0derived";
            [MetaMember(5)] public string PublicField0Derived = "publicfield0derived";

            [MetaMember(2)] private string PrivatePropDerived { get; set; } = "privatepropderived";
            [MetaMember(6)] private string PrivateFieldDerived = "privatefieldderived";

            [MetaMember(3)] protected string ProtectedPropDerived { get; private set; } = "protectedpropderived";
            [MetaMember(7)] protected string ProtectedFieldDerived = "protectedfieldderived";

            [MetaMember(4)] public string PublicProp1Derived { get; private set; } = "publicprop1derived";
            [MetaMember(8)] public string PublicField1Derived = "publicfield1derived";
        }

        [MetaSerializableDerived(2)]
        [MetaImplicitMembersRange(400, 500)]
        public class ImplicitMembersTestDerivedWithCustomRange : ImplicitMembersTestBase
        {
            public string PublicProp0Derived { get; private set; } = "publicprop0derived";
            public string PublicField0Derived = "publicfield0derived";

            private string PrivatePropDerived { get; set; } = "privatepropderived";
            private string PrivateFieldDerived = "privatefieldderived";

            protected string ProtectedPropDerived { get; private set; } = "protectedpropderived";
            protected string ProtectedFieldDerived = "protectedfieldderived";

            public string PublicProp1Derived { get; private set; } = "publicprop1derived";
            public string PublicField1Derived = "publicfield1derived";
        }

        [MetaSerializable]
        public class ExplicitReferenceForImplicitMembersTestBaseAndDerivedWithCustomRange
        {
            [MetaMember(100)] public string PublicProp0Base { get; private set; } = "publicprop0base";
            [MetaMember(104)] public string PublicField0Base = "publicfield0base";

            [MetaMember(101)] private string PrivatePropBase { get; set; } = "privatepropbase";
            [MetaMember(105)] private string PrivateFieldBase = "privatefieldbase";

            [MetaMember(102)] protected string ProtectedPropBase { get; private set; } = "protectedpropbase";
            [MetaMember(106)] protected string ProtectedFieldBase = "protectedfieldbase";

            [MetaMember(103)] public string PublicProp1Base { get; private set; } = "publicprop1base";
            [MetaMember(107)] public string PublicField1Base = "publicfield1base";

            [MetaMember(400)] public string PublicProp0Derived { get; private set; } = "publicprop0derived";
            [MetaMember(404)] public string PublicField0Derived = "publicfield0derived";

            [MetaMember(401)] private string PrivatePropDerived { get; set; } = "privatepropderived";
            [MetaMember(405)] private string PrivateFieldDerived = "privatefieldderived";

            [MetaMember(402)] protected string ProtectedPropDerived { get; private set; } = "protectedpropderived";
            [MetaMember(406)] protected string ProtectedFieldDerived = "protectedfieldderived";

            [MetaMember(403)] public string PublicProp1Derived { get; private set; } = "publicprop1derived";
            [MetaMember(407)] public string PublicField1Derived = "publicfield1derived";
        }

        #pragma warning restore CS0414

        [Test]
        public void TestImplicitMembers()
        {
            {
                byte[] serialized = MetaSerialization.SerializeTagged(new ImplicitMembersTestClass(), MetaSerializationFlags.IncludeAll, logicVersion: null);
                byte[] reference = MetaSerialization.SerializeTagged(new ExplicitReferenceForImplicitMembersTestClass(), MetaSerializationFlags.IncludeAll, logicVersion: null);
                Assert.AreEqual(reference, serialized);
            }

            {
                byte[] serialized = MetaSerialization.SerializeTagged(new ImplicitMembersTestDerived(), MetaSerializationFlags.IncludeAll, logicVersion: null);
                byte[] reference = MetaSerialization.SerializeTagged(new ExplicitReferenceForImplicitMembersTestBaseAndDerived(), MetaSerializationFlags.IncludeAll, logicVersion: null);
                Assert.AreEqual(reference, serialized);
            }

            {
                byte[] serialized = MetaSerialization.SerializeTagged(new ImplicitMembersTestDerivedWithCustomRange(), MetaSerializationFlags.IncludeAll, logicVersion: null);
                byte[] reference = MetaSerialization.SerializeTagged(new ExplicitReferenceForImplicitMembersTestBaseAndDerivedWithCustomRange(), MetaSerializationFlags.IncludeAll, logicVersion: null);
                Assert.AreEqual(reference, serialized);
            }
        }

        // Deserialization failure hook test: original types to serialize

        [MetaSerializable]
        public abstract class FailureTestOriginalItemBase
        {
        }

        [MetaSerializableDerived(1)]
        public class FailureTestOriginalItemDerivedA : FailureTestOriginalItemBase
        {
            [MetaMember(1)] public TestObjectTypes Value;
            FailureTestOriginalItemDerivedA(){ }
            public FailureTestOriginalItemDerivedA(string str) { Value = new TestObjectTypes(){ String = str }; }
        }

        [MetaSerializableDerived(2)]
        public class FailureTestOriginalItemDerivedB : FailureTestOriginalItemBase
        {
            [MetaMember(1)] public TestObjectTypes Value;
            FailureTestOriginalItemDerivedB(){ }
            public FailureTestOriginalItemDerivedB(string str) { Value = new TestObjectTypes(){ String = str }; }
        }

        [MetaSerializable]
        public class FailureTestOriginalWrapper
        {
            [MetaMember(1)] public FailureTestOriginalItemBase Item;
            FailureTestOriginalWrapper(){ }
            public FailureTestOriginalWrapper(FailureTestOriginalItemBase item){ Item = item; }
        }

        [MetaSerializable]
        public class FailureTestOriginalListWrapper
        {
            [MetaMember(1)] public List<FailureTestOriginalWrapper> List;
            FailureTestOriginalListWrapper(){ }
            public FailureTestOriginalListWrapper(List<FailureTestOriginalWrapper> list) { List = list; }
        }

        // Deserialization failure hook test: types compatible with the original types, except that one derived type is missing

        [MetaSerializable]
        public abstract class FailureTestUnknownDerivedItemBase
        {
        }

        [MetaSerializableDerived(1)]
        public class FailureTestUnknownDerivedItemDerivedA : FailureTestUnknownDerivedItemBase
        {
            [MetaMember(1)] public TestObjectTypes Value;
        }

        [MetaSerializableDerived(999)]
        public class FailureTestUnknownDerivedItemDerivedSubstitute : FailureTestUnknownDerivedItemBase
        {
            [MetaMember(1)] public byte[] Payload;
            public Exception Exception;
            FailureTestUnknownDerivedItemDerivedSubstitute(){ }
            public FailureTestUnknownDerivedItemDerivedSubstitute(byte[] payload, Exception exception)
            {
                Payload = payload;
                Exception = exception;
            }
        }

        [MetaSerializable]
        public class FailureTestUnknownDerivedItemWrapper
        {
            [MetaOnMemberDeserializationFailure("CreateSubstituteItem")]
            [MetaMember(1)] public FailureTestUnknownDerivedItemBase Item;

            public static FailureTestUnknownDerivedItemBase CreateSubstituteItem(MetaMemberDeserializationFailureParams failureParams)
            {
                return new FailureTestUnknownDerivedItemDerivedSubstitute(failureParams.MemberPayload, failureParams.Exception);
            }
        }

        [MetaSerializable]
        public class FailureTestUnknownDerivedListWrapper
        {
            [MetaMember(1)] public List<FailureTestUnknownDerivedItemWrapper> List;
        }

        // Deserialization failure hook test: types compatible with the original types, except that a member has incompatible type

        [MetaSerializable]
        public abstract class FailureTestWrongTypeItemBase
        {
        }

        [MetaSerializableDerived(1)]
        public class FailureTestWrongTypeItemDerivedA : FailureTestWrongTypeItemBase
        {
            [MetaMember(1)] public TestObjectTypes Value;
        }

        [MetaSerializableDerived(2)]
        public class FailureTestWrongTypeItemDerivedB : FailureTestWrongTypeItemBase
        {
            [MetaMember(1)] public string Value;
        }

        [MetaSerializableDerived(999)]
        public class FailureTestWrongTypeItemDerivedSubstitute : FailureTestWrongTypeItemBase
        {
            [MetaMember(1)] public byte[] Payload;
            public Exception Exception;
            FailureTestWrongTypeItemDerivedSubstitute(){ }
            public FailureTestWrongTypeItemDerivedSubstitute(byte[] payload, Exception exception)
            {
                Payload = payload;
                Exception = exception;
            }
        }

        [MetaSerializable]
        public class FailureTestWrongTypeItemWrapper
        {
            [MetaOnMemberDeserializationFailure("CreateSubstituteItem")]
            [MetaMember(1)] public FailureTestWrongTypeItemBase Item;

            public static FailureTestWrongTypeItemBase CreateSubstituteItem(MetaMemberDeserializationFailureParams failureParams)
            {
                return new FailureTestWrongTypeItemDerivedSubstitute(failureParams.MemberPayload, failureParams.Exception);
            }
        }

        [MetaSerializable]
        public class FailureTestWrongTypeListWrapper
        {
            [MetaMember(1)] public List<FailureTestWrongTypeItemWrapper> List;
        }

        [Test]
        public void TestOnMemberDeserializationFailure()
        {
            FailureTestOriginalListWrapper originalList = new FailureTestOriginalListWrapper(new List<FailureTestOriginalWrapper>
            {
                new FailureTestOriginalWrapper(new FailureTestOriginalItemDerivedA("derivedA_x")),
                new FailureTestOriginalWrapper(new FailureTestOriginalItemDerivedA("derivedA_xx")),
                new FailureTestOriginalWrapper(new FailureTestOriginalItemDerivedB("derivedB_xxx")),
                new FailureTestOriginalWrapper(new FailureTestOriginalItemDerivedA("derivedA_xxxx")),
                new FailureTestOriginalWrapper(new FailureTestOriginalItemDerivedB("derivedB_xxxxx")),
                new FailureTestOriginalWrapper(new FailureTestOriginalItemDerivedA("derivedA_xxxxxx")),
            });

            byte[] serializedOriginal = MetaSerialization.SerializeTagged(originalList, MetaSerializationFlags.IncludeAll, logicVersion: null);

            // Test with "unknown derived type" failure
            {
                FailureTestUnknownDerivedListWrapper listWithUnknownDerived = MetaSerialization.DeserializeTagged<FailureTestUnknownDerivedListWrapper>(serializedOriginal, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);
                Assert.AreEqual(originalList.List.Count, listWithUnknownDerived.List.Count);
                // Valid items
                Assert.AreEqual("derivedA_x", ((FailureTestUnknownDerivedItemDerivedA)listWithUnknownDerived.List[0].Item).Value.String);
                Assert.AreEqual("derivedA_xx", ((FailureTestUnknownDerivedItemDerivedA)listWithUnknownDerived.List[1].Item).Value.String);
                Assert.AreEqual("derivedA_xxxx", ((FailureTestUnknownDerivedItemDerivedA)listWithUnknownDerived.List[3].Item).Value.String);
                Assert.AreEqual("derivedA_xxxxxx", ((FailureTestUnknownDerivedItemDerivedA)listWithUnknownDerived.List[5].Item).Value.String);
                // Failed items. Check that the payload and the exception stored in the substitute are as expected.
                {
                    FailureTestUnknownDerivedItemDerivedSubstitute substitute = (FailureTestUnknownDerivedItemDerivedSubstitute)listWithUnknownDerived.List[2].Item;
                    Assert.AreEqual(
                        MetaSerialization.SerializeTagged<FailureTestOriginalItemBase>(originalList.List[2].Item, MetaSerializationFlags.IncludeAll, logicVersion: null),
                        Util.ConcatBytes(new byte[]{ (byte)TaggedWireSerializer.GetWireType(typeof(FailureTestOriginalItemBase)) }, substitute.Payload));

                    MetaUnknownDerivedTypeDeserializationException exception = (MetaUnknownDerivedTypeDeserializationException)substitute.Exception;
                    Assert.AreEqual(typeof(FailureTestUnknownDerivedItemBase), exception.AttemptedType);
                    Assert.AreEqual(2, exception.EncounteredTypeCode);
                }
                {
                    FailureTestUnknownDerivedItemDerivedSubstitute substitute = (FailureTestUnknownDerivedItemDerivedSubstitute)listWithUnknownDerived.List[4].Item;
                    Assert.AreEqual(
                        MetaSerialization.SerializeTagged<FailureTestOriginalItemBase>(originalList.List[4].Item, MetaSerializationFlags.IncludeAll, logicVersion: null),
                        Util.ConcatBytes(new byte[]{ (byte)TaggedWireSerializer.GetWireType(typeof(FailureTestOriginalItemBase)) }, substitute.Payload));

                    MetaUnknownDerivedTypeDeserializationException exception = (MetaUnknownDerivedTypeDeserializationException)substitute.Exception;
                    Assert.AreEqual(typeof(FailureTestUnknownDerivedItemBase), exception.AttemptedType);
                    Assert.AreEqual(2, exception.EncounteredTypeCode);
                }
            }

            // Test with "wire data type mismatch" failure
            {
                FailureTestWrongTypeListWrapper listWithWrongType = MetaSerialization.DeserializeTagged<FailureTestWrongTypeListWrapper>(serializedOriginal, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);
                Assert.AreEqual(originalList.List.Count, listWithWrongType.List.Count);
                // Valid items
                Assert.AreEqual("derivedA_x", ((FailureTestWrongTypeItemDerivedA)listWithWrongType.List[0].Item).Value.String);
                Assert.AreEqual("derivedA_xx", ((FailureTestWrongTypeItemDerivedA)listWithWrongType.List[1].Item).Value.String);
                Assert.AreEqual("derivedA_xxxx", ((FailureTestWrongTypeItemDerivedA)listWithWrongType.List[3].Item).Value.String);
                Assert.AreEqual("derivedA_xxxxxx", ((FailureTestWrongTypeItemDerivedA)listWithWrongType.List[5].Item).Value.String);
                // Failed items. Check that the payload and the exception stored in the substitute are as expected.
                {
                    FailureTestWrongTypeItemDerivedSubstitute substitute = (FailureTestWrongTypeItemDerivedSubstitute)listWithWrongType.List[2].Item;
                    byte[] payloadPrefixedWithWireType = Util.ConcatBytes(
                        new byte[]{ (byte)TaggedWireSerializer.GetWireType(typeof(FailureTestOriginalItemBase)) },
                        substitute.Payload);
                    FailureTestOriginalItemBase originalItem = MetaSerialization.DeserializeTagged<FailureTestOriginalItemBase>(payloadPrefixedWithWireType, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);
                    Assert.AreEqual("derivedB_xxx", ((FailureTestOriginalItemDerivedB)originalItem).Value.String);

                    MetaWireDataTypeMismatchDeserializationException exception = (MetaWireDataTypeMismatchDeserializationException)substitute.Exception;
                    Assert.AreEqual(typeof(string), exception.AttemptedType);
                    Assert.AreEqual(WireDataType.String, exception.ExpectedWireDataType);
                    Assert.AreEqual(WireDataType.NullableStruct, exception.EncounteredWireDataType);
                }
                {
                    FailureTestWrongTypeItemDerivedSubstitute substitute = (FailureTestWrongTypeItemDerivedSubstitute)listWithWrongType.List[4].Item;
                    byte[] payloadPrefixedWithWireType = Util.ConcatBytes(
                        new byte[]{ (byte)TaggedWireSerializer.GetWireType(typeof(FailureTestOriginalItemBase)) },
                        substitute.Payload);
                    FailureTestOriginalItemBase originalItem = MetaSerialization.DeserializeTagged<FailureTestOriginalItemBase>(payloadPrefixedWithWireType, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);
                    Assert.AreEqual("derivedB_xxxxx", ((FailureTestOriginalItemDerivedB)originalItem).Value.String);

                    MetaWireDataTypeMismatchDeserializationException exception = (MetaWireDataTypeMismatchDeserializationException)substitute.Exception;
                    Assert.AreEqual(typeof(string), exception.AttemptedType);
                    Assert.AreEqual(WireDataType.String, exception.ExpectedWireDataType);
                    Assert.AreEqual(WireDataType.NullableStruct, exception.EncounteredWireDataType);
                }
            }
        }

        #region Game config reference and content serialization

        // Key types

        [MetaSerializable]
        public class TestGameConfigId : StringId<TestGameConfigId> { }

        [MetaSerializable]
        public struct TestGameConfigCustomValueKey
        {
            [MetaMember(1)] public int X;

            public override bool Equals(object obj) => obj is TestGameConfigCustomValueKey key && X == key.X;
            public override int GetHashCode() => X.GetHashCode();
            public override string ToString() => Invariant($"{nameof(TestGameConfigCustomValueKey)}({X})");
        }

        [MetaSerializable]
        public class TestGameConfigCustomReferenceKey
        {
            [MetaMember(1)] public int X;

            public override bool Equals(object obj) => obj is TestGameConfigCustomReferenceKey key && X == key.X;
            public override int GetHashCode() => X.GetHashCode();
            public override string ToString() => Invariant($"{nameof(TestGameConfigCustomReferenceKey)}({X})");
        }

        // Common use case: StringId key.
        [MetaSerializable]
        public class TestGameConfigInfo : IGameConfigData<TestGameConfigId>
        {
            [MetaMember(1)] public TestGameConfigId Id;
            [MetaMember(2)] public string           Value;

            public TestGameConfigId ConfigKey => Id;
        }

        // Identical contents as TestGameConfigInfo but without being an IGameConfigData.
        // Used for testing serialization of GameConfigDataContent.
        [MetaSerializable]
        public class TestGameConfigInfoContentMimic
        {
            [MetaMember(1)] public TestGameConfigId Id;
            [MetaMember(2)] public string           Value;
        }

        // Non-nullable primitive value-typed key.
        [MetaSerializable]
        public class TestGameConfigIntInfo : IGameConfigData<int>
        {
            [MetaMember(1)] public int              Id;
            [MetaMember(2)] public string           Value;

            public int ConfigKey => Id;
        }

        // Nullable primitive value-typed key.
        [MetaSerializable]
        public class TestGameConfigNIntInfo : IGameConfigData<int?>
        {
            [MetaMember(1)] public int              Id;
            [MetaMember(2)] public string           Value;

            public int? ConfigKey => Id;
        }

        // Non-nullable primitive value-typed key, but using a sentinel key for null.
        [MetaSerializable]
        public class TestGameConfigSIntInfo : IGameConfigData<int>
        {
            [MetaMember(1)] public int              Id;
            [MetaMember(2)] public string           Value;

            const int ConfigNullSentinelKey = 42;
            public int ConfigKey => Id;
        }

        // Identical contents as TestGameConfigIntInfo/TestGameConfigNIntInfo/TestGameConfigSIntInfo but without being an IGameConfigData.
        [MetaSerializable]
        public class TestGameConfigIntInfoContentMimic
        {
            [MetaMember(1)] public int              Id;
            [MetaMember(2)] public string           Value;
        }

        // Non-nullable custom value-typed key.
        [MetaSerializable]
        public class TestGameConfigCVKInfo : IGameConfigData<TestGameConfigCustomValueKey>
        {
            [MetaMember(1)] public TestGameConfigCustomValueKey Id;
            [MetaMember(2)] public string                       Value;

            public TestGameConfigCustomValueKey ConfigKey => Id;
        }

        // Nullable custom value-typed key.
        [MetaSerializable]
        public class TestGameConfigNCVKInfo : IGameConfigData<TestGameConfigCustomValueKey?>
        {
            [MetaMember(1)] public TestGameConfigCustomValueKey Id;
            [MetaMember(2)] public string                       Value;

            public TestGameConfigCustomValueKey? ConfigKey => Id;
        }

        // Non-nullable custom value-typed key, but using a sentinel key for null.
        [MetaSerializable]
        public class TestGameConfigSCVKInfo : IGameConfigData<TestGameConfigCustomValueKey>
        {
            [MetaMember(1)] public TestGameConfigCustomValueKey Id;
            [MetaMember(2)] public string                       Value;

            static TestGameConfigCustomValueKey ConfigNullSentinelKey = new TestGameConfigCustomValueKey { X = 42 };
            public TestGameConfigCustomValueKey ConfigKey => Id;
        }

        // Identical contents as TestGameConfigCVKInfo/TestGameConfigNCVKInfo/TestGameConfigSCVKInfo but without being an IGameConfigData.
        [MetaSerializable]
        public class TestGameConfigCVKInfoContentMimic
        {
            [MetaMember(1)] public TestGameConfigCustomValueKey     Id;
            [MetaMember(2)] public string                           Value;
        }

        // Custom reference-typed key.
        [MetaSerializable]
        public class TestGameConfigCRKInfo : IGameConfigData<TestGameConfigCustomReferenceKey>
        {
            [MetaMember(1)] public TestGameConfigCustomReferenceKey Id;
            [MetaMember(2)] public string                           Value;

            public TestGameConfigCustomReferenceKey ConfigKey => Id;
        }

        // Identical contents as TestGameConfigCRKInfo but without being an IGameConfigData.
        [MetaSerializable]
        public class TestGameConfigCRKInfoContentMimic
        {
            [MetaMember(1)] public TestGameConfigCustomReferenceKey Id;
            [MetaMember(2)] public string                           Value;
        }

        [MetaSerializable]
        public class TestGameConfigInfoContainer
        {
            // Common use case: StringId key.
            [MetaMember(1)] public TestGameConfigInfo                               Reference;
            [MetaMember(2)] public TestGameConfigInfo                               NullReference;
            [MetaMember(3)] public MetaRef<TestGameConfigInfo>                      MetaRefReference;
            [MetaMember(4)] public MetaRef<TestGameConfigInfo>                      MetaRefNullReference;
            [MetaMember(5)] public GameConfigDataContent<TestGameConfigInfo>        Content;
            [MetaMember(6)] public GameConfigDataContent<TestGameConfigInfo>        NullContent;

            // Non-nullable primitive value-typed key.
            [MetaMember(11)] public TestGameConfigIntInfo                           IntReference;
            [MetaMember(13)] public MetaRef<TestGameConfigIntInfo>                  IntMetaRefReference;
            [MetaMember(15)] public GameConfigDataContent<TestGameConfigIntInfo>    IntContent;
            [MetaMember(16)] public GameConfigDataContent<TestGameConfigIntInfo>    IntNullContent;

            // Nullable primitive value-typed key.
            [MetaMember(21)] public TestGameConfigNIntInfo                          NIntReference;
            [MetaMember(22)] public TestGameConfigNIntInfo                          NIntNullReference;
            [MetaMember(23)] public MetaRef<TestGameConfigNIntInfo>                 NIntMetaRefReference;
            [MetaMember(24)] public MetaRef<TestGameConfigNIntInfo>                 NIntMetaRefNullReference;
            [MetaMember(25)] public GameConfigDataContent<TestGameConfigNIntInfo>   NIntContent;
            [MetaMember(26)] public GameConfigDataContent<TestGameConfigNIntInfo>   NIntNullContent;

            // Non-nullable primitive value-typed key, but using a sentinel key for null.
            [MetaMember(61)] public TestGameConfigSIntInfo                          SIntReference;
            [MetaMember(62)] public TestGameConfigSIntInfo                          SIntNullReference;
            [MetaMember(63)] public MetaRef<TestGameConfigSIntInfo>                 SIntMetaRefReference;
            [MetaMember(64)] public MetaRef<TestGameConfigSIntInfo>                 SIntMetaRefNullReference;
            [MetaMember(65)] public GameConfigDataContent<TestGameConfigSIntInfo>   SIntContent;
            [MetaMember(66)] public GameConfigDataContent<TestGameConfigSIntInfo>   SIntNullContent;

            // Non-nullable custom value-typed key.
            [MetaMember(31)] public TestGameConfigCVKInfo                           CVKReference;
            [MetaMember(33)] public MetaRef<TestGameConfigCVKInfo>                  CVKMetaRefReference;
            [MetaMember(35)] public GameConfigDataContent<TestGameConfigCVKInfo>    CVKContent;
            [MetaMember(36)] public GameConfigDataContent<TestGameConfigCVKInfo>    CVKNullContent;

            // Nullable custom value-typed key.
            [MetaMember(41)] public TestGameConfigNCVKInfo                          NCVKReference;
            [MetaMember(42)] public TestGameConfigNCVKInfo                          NCVKNullReference;
            [MetaMember(43)] public MetaRef<TestGameConfigNCVKInfo>                 NCVKMetaRefReference;
            [MetaMember(44)] public MetaRef<TestGameConfigNCVKInfo>                 NCVKMetaRefNullReference;
            [MetaMember(45)] public GameConfigDataContent<TestGameConfigNCVKInfo>   NCVKContent;
            [MetaMember(46)] public GameConfigDataContent<TestGameConfigNCVKInfo>   NCVKNullContent;

            // Non-nullable custom value-typed key, but using a sentinel key for null.
            [MetaMember(71)] public TestGameConfigSCVKInfo                          SCVKReference;
            [MetaMember(72)] public TestGameConfigSCVKInfo                          SCVKNullReference;
            [MetaMember(73)] public MetaRef<TestGameConfigSCVKInfo>                 SCVKMetaRefReference;
            [MetaMember(74)] public MetaRef<TestGameConfigSCVKInfo>                 SCVKMetaRefNullReference;
            [MetaMember(75)] public GameConfigDataContent<TestGameConfigSCVKInfo>   SCVKContent;
            [MetaMember(76)] public GameConfigDataContent<TestGameConfigSCVKInfo>   SCVKNullContent;

            // Custom reference-typed key.
            [MetaMember(51)] public TestGameConfigCRKInfo                           CRKReference;
            [MetaMember(52)] public TestGameConfigCRKInfo                           CRKNullReference;
            [MetaMember(53)] public MetaRef<TestGameConfigCRKInfo>                  CRKMetaRefReference;
            [MetaMember(54)] public MetaRef<TestGameConfigCRKInfo>                  CRKMetaRefNullReference;
            [MetaMember(55)] public GameConfigDataContent<TestGameConfigCRKInfo>    CRKContent;
            [MetaMember(56)] public GameConfigDataContent<TestGameConfigCRKInfo>    CRKNullContent;
        }

        // Serialization-compatible with TestGameConfigInfoContainer, but without containing
        // any config references. Contains just the keys in place of config references, and
        // the "ContentMimics" in place of GameConfigDataContents.
        [MetaSerializable]
        public class TestGameConfigInfoContainerMimic
        {
            [MetaMember(1)] public TestGameConfigId                     Reference;
            [MetaMember(2)] public TestGameConfigId                     NullReference;
            [MetaMember(3)] public TestGameConfigId                     MetaRefReference;
            [MetaMember(4)] public TestGameConfigId                     MetaRefNullReference;
            [MetaMember(5)] public TestGameConfigInfoContentMimic       Content;
            [MetaMember(6)] public TestGameConfigInfoContentMimic       NullContent;

            [MetaMember(11)] public int                                 IntReference;
            [MetaMember(13)] public int                                 IntMetaRefReference;
            [MetaMember(15)] public TestGameConfigIntInfoContentMimic   IntContent;
            [MetaMember(16)] public TestGameConfigIntInfoContentMimic   IntNullContent;

            [MetaMember(21)] public int?                                NIntReference;
            [MetaMember(22)] public int?                                NIntNullReference;
            [MetaMember(23)] public int?                                NIntMetaRefReference;
            [MetaMember(24)] public int?                                NIntMetaRefNullReference;
            [MetaMember(25)] public TestGameConfigIntInfoContentMimic   NIntContent;
            [MetaMember(26)] public TestGameConfigIntInfoContentMimic   NIntNullContent;

            [MetaMember(61)] public int                                 SIntReference;
            [MetaMember(62)] public int                                 SIntNullReference;
            [MetaMember(63)] public int                                 SIntMetaRefReference;
            [MetaMember(64)] public int                                 SIntMetaRefNullReference;
            [MetaMember(65)] public TestGameConfigIntInfoContentMimic   SIntContent;
            [MetaMember(66)] public TestGameConfigIntInfoContentMimic   SIntNullContent;

            [MetaMember(31)] public TestGameConfigCustomValueKey        CVKReference;
            [MetaMember(33)] public TestGameConfigCustomValueKey        CVKMetaRefReference;
            [MetaMember(35)] public TestGameConfigCVKInfoContentMimic   CVKContent;
            [MetaMember(36)] public TestGameConfigCVKInfoContentMimic   CVKNullContent;

            [MetaMember(41)] public TestGameConfigCustomValueKey?       NCVKReference;
            [MetaMember(42)] public TestGameConfigCustomValueKey?       NCVKNullReference;
            [MetaMember(43)] public TestGameConfigCustomValueKey?       NCVKMetaRefReference;
            [MetaMember(44)] public TestGameConfigCustomValueKey?       NCVKMetaRefNullReference;
            [MetaMember(45)] public TestGameConfigCVKInfoContentMimic   NCVKContent;
            [MetaMember(46)] public TestGameConfigCVKInfoContentMimic   NCVKNullContent;

            [MetaMember(71)] public TestGameConfigCustomValueKey        SCVKReference;
            [MetaMember(72)] public TestGameConfigCustomValueKey        SCVKNullReference;
            [MetaMember(73)] public TestGameConfigCustomValueKey        SCVKMetaRefReference;
            [MetaMember(74)] public TestGameConfigCustomValueKey        SCVKMetaRefNullReference;
            [MetaMember(75)] public TestGameConfigCVKInfoContentMimic   SCVKContent;
            [MetaMember(76)] public TestGameConfigCVKInfoContentMimic   SCVKNullContent;

            [MetaMember(51)] public TestGameConfigCustomReferenceKey    CRKReference;
            [MetaMember(52)] public TestGameConfigCustomReferenceKey    CRKNullReference;
            [MetaMember(53)] public TestGameConfigCustomReferenceKey    CRKMetaRefReference;
            [MetaMember(54)] public TestGameConfigCustomReferenceKey    CRKMetaRefNullReference;
            [MetaMember(55)] public TestGameConfigCRKInfoContentMimic   CRKContent;
            [MetaMember(56)] public TestGameConfigCRKInfoContentMimic   CRKNullContent;
        }

        // Subset of TestGameConfigInfoContainer that can be deserialized without a resolver.
        // Namely, doesn't contain non-null "plain" (i.e. non-MetaRef) references.
        // Note that when deserialized without a resolver, non-null MetaRefs remain in
        // "unresolved" state, where only the key is present but not the actual reference.
        [MetaSerializable]
        public class TestGameConfigResolverlessInfoContainer
        {
            [MetaMember(2)] public TestGameConfigInfo                               NullReference;
            [MetaMember(3)] public MetaRef<TestGameConfigInfo>                      MetaRefReference;
            [MetaMember(4)] public MetaRef<TestGameConfigInfo>                      MetaRefNullReference;
            [MetaMember(5)] public GameConfigDataContent<TestGameConfigInfo>        Content;
            [MetaMember(6)] public GameConfigDataContent<TestGameConfigInfo>        NullContent;

            [MetaMember(13)] public MetaRef<TestGameConfigIntInfo>                  IntMetaRefReference;
            [MetaMember(15)] public GameConfigDataContent<TestGameConfigIntInfo>    IntContent;
            [MetaMember(16)] public GameConfigDataContent<TestGameConfigIntInfo>    IntNullContent;

            [MetaMember(22)] public TestGameConfigNIntInfo                          NIntNullReference;
            [MetaMember(23)] public MetaRef<TestGameConfigNIntInfo>                 NIntMetaRefReference;
            [MetaMember(24)] public MetaRef<TestGameConfigNIntInfo>                 NIntMetaRefNullReference;
            [MetaMember(25)] public GameConfigDataContent<TestGameConfigNIntInfo>   NIntContent;
            [MetaMember(26)] public GameConfigDataContent<TestGameConfigNIntInfo>   NIntNullContent;

            [MetaMember(62)] public TestGameConfigSIntInfo                          SIntNullReference;
            [MetaMember(63)] public MetaRef<TestGameConfigSIntInfo>                 SIntMetaRefReference;
            [MetaMember(64)] public MetaRef<TestGameConfigSIntInfo>                 SIntMetaRefNullReference;
            [MetaMember(65)] public GameConfigDataContent<TestGameConfigSIntInfo>   SIntContent;
            [MetaMember(66)] public GameConfigDataContent<TestGameConfigSIntInfo>   SIntNullContent;

            [MetaMember(33)] public MetaRef<TestGameConfigCVKInfo>                  CVKMetaRefReference;
            [MetaMember(35)] public GameConfigDataContent<TestGameConfigCVKInfo>    CVKContent;
            [MetaMember(36)] public GameConfigDataContent<TestGameConfigCVKInfo>    CVKNullContent;

            [MetaMember(42)] public TestGameConfigNCVKInfo                          NCVKNullReference;
            [MetaMember(43)] public MetaRef<TestGameConfigNCVKInfo>                 NCVKMetaRefReference;
            [MetaMember(44)] public MetaRef<TestGameConfigNCVKInfo>                 NCVKMetaRefNullReference;
            [MetaMember(45)] public GameConfigDataContent<TestGameConfigNCVKInfo>   NCVKContent;
            [MetaMember(46)] public GameConfigDataContent<TestGameConfigNCVKInfo>   NCVKNullContent;

            [MetaMember(72)] public TestGameConfigSCVKInfo                          SCVKNullReference;
            [MetaMember(73)] public MetaRef<TestGameConfigSCVKInfo>                 SCVKMetaRefReference;
            [MetaMember(74)] public MetaRef<TestGameConfigSCVKInfo>                 SCVKMetaRefNullReference;
            [MetaMember(75)] public GameConfigDataContent<TestGameConfigSCVKInfo>   SCVKContent;
            [MetaMember(76)] public GameConfigDataContent<TestGameConfigSCVKInfo>   SCVKNullContent;

            [MetaMember(52)] public TestGameConfigCRKInfo                           CRKNullReference;
            [MetaMember(53)] public MetaRef<TestGameConfigCRKInfo>                  CRKMetaRefReference;
            [MetaMember(54)] public MetaRef<TestGameConfigCRKInfo>                  CRKMetaRefNullReference;
            [MetaMember(55)] public GameConfigDataContent<TestGameConfigCRKInfo>    CRKContent;
            [MetaMember(56)] public GameConfigDataContent<TestGameConfigCRKInfo>    CRKNullContent;
        }

        public class TestGameConfigDataResolver : IGameConfigDataResolver
        {
            public Dictionary<(Type, object), object> TypeAndKeyToInfo = new Dictionary<(Type, object), object>();

            public object TryResolveReference(Type type, object configKey) => TypeAndKeyToInfo[(type, configKey)];
        }

        [Test]
        public void TestGameConfigData()
        {
            TestGameConfigInfo originalInfo0 = new TestGameConfigInfo{ Id = TestGameConfigId.FromString("TestId0"), Value = "TestValue0" };
            TestGameConfigInfo originalInfo1 = new TestGameConfigInfo{ Id = TestGameConfigId.FromString("TestId1"), Value = "TestValue1" };

            TestGameConfigIntInfo originalIntInfo0 = new TestGameConfigIntInfo{ Id = 123, Value = "TestIntValue0" };
            TestGameConfigNIntInfo originalNIntInfo0 = new TestGameConfigNIntInfo{ Id = 321, Value = "TestNIntValue0" };
            TestGameConfigSIntInfo originalSIntInfo0 = new TestGameConfigSIntInfo{ Id = 123_321, Value = "TestSIntValue0" };

            TestGameConfigCVKInfo originalCVKInfo0 = new TestGameConfigCVKInfo{ Id = new TestGameConfigCustomValueKey{ X = 456 }, Value = "TestCVKValue0" };
            TestGameConfigNCVKInfo originalNCVKInfo0 = new TestGameConfigNCVKInfo{ Id = new TestGameConfigCustomValueKey{ X = 654 }, Value = "TestNCVKValue0" };
            TestGameConfigSCVKInfo originalSCVKInfo0 = new TestGameConfigSCVKInfo{ Id = new TestGameConfigCustomValueKey{ X = 456_654 }, Value = "TestSCVKValue0" };

            TestGameConfigCRKInfo originalCRKInfo0 = new TestGameConfigCRKInfo{ Id = new TestGameConfigCustomReferenceKey{ X = 789 }, Value = "TestCRKValue0" };

            // Test a IGameConfigData-containing object and a "mimic" that doesn't contain IGameConfigDatas but should serialize to identical data.
            {
                TestGameConfigInfoContainer         originalContainer       = new TestGameConfigInfoContainer
                {
                    Reference               = originalInfo0,
                    NullReference           = null,
                    MetaRefReference        = MetaRef<TestGameConfigInfo>.FromItem(originalInfo0),
                    MetaRefNullReference    = null,
                    Content                 = new GameConfigDataContent<TestGameConfigInfo>(originalInfo1),
                    NullContent             = new GameConfigDataContent<TestGameConfigInfo>(null),

                    IntReference            = originalIntInfo0,
                    IntMetaRefReference     = MetaRef<TestGameConfigIntInfo>.FromItem(originalIntInfo0),
                    IntContent              = new GameConfigDataContent<TestGameConfigIntInfo>(originalIntInfo0),
                    IntNullContent          = new GameConfigDataContent<TestGameConfigIntInfo>(null),

                    NIntReference           = originalNIntInfo0,
                    NIntNullReference       = null,
                    NIntMetaRefReference    = MetaRef<TestGameConfigNIntInfo>.FromItem(originalNIntInfo0),
                    NIntMetaRefNullReference= null,
                    NIntContent             = new GameConfigDataContent<TestGameConfigNIntInfo>(originalNIntInfo0),
                    NIntNullContent         = new GameConfigDataContent<TestGameConfigNIntInfo>(null),

                    SIntReference           = originalSIntInfo0,
                    SIntNullReference       = null,
                    SIntMetaRefReference    = MetaRef<TestGameConfigSIntInfo>.FromItem(originalSIntInfo0),
                    SIntMetaRefNullReference= null,
                    SIntContent             = new GameConfigDataContent<TestGameConfigSIntInfo>(originalSIntInfo0),
                    SIntNullContent         = new GameConfigDataContent<TestGameConfigSIntInfo>(null),

                    CVKReference            = originalCVKInfo0,
                    CVKMetaRefReference     = MetaRef<TestGameConfigCVKInfo>.FromItem(originalCVKInfo0),
                    CVKContent              = new GameConfigDataContent<TestGameConfigCVKInfo>(originalCVKInfo0),
                    CVKNullContent          = new GameConfigDataContent<TestGameConfigCVKInfo>(null),

                    NCVKReference           = originalNCVKInfo0,
                    NCVKNullReference       = null,
                    NCVKMetaRefReference    = MetaRef<TestGameConfigNCVKInfo>.FromItem(originalNCVKInfo0),
                    NCVKMetaRefNullReference= null,
                    NCVKContent             = new GameConfigDataContent<TestGameConfigNCVKInfo>(originalNCVKInfo0),
                    NCVKNullContent         = new GameConfigDataContent<TestGameConfigNCVKInfo>(null),

                    SCVKReference           = originalSCVKInfo0,
                    SCVKNullReference       = null,
                    SCVKMetaRefReference    = MetaRef<TestGameConfigSCVKInfo>.FromItem(originalSCVKInfo0),
                    SCVKMetaRefNullReference= null,
                    SCVKContent             = new GameConfigDataContent<TestGameConfigSCVKInfo>(originalSCVKInfo0),
                    SCVKNullContent         = new GameConfigDataContent<TestGameConfigSCVKInfo>(null),

                    CRKReference            = originalCRKInfo0,
                    CRKNullReference        = null,
                    CRKMetaRefReference     = MetaRef<TestGameConfigCRKInfo>.FromItem(originalCRKInfo0),
                    CRKMetaRefNullReference = null,
                    CRKContent              = new GameConfigDataContent<TestGameConfigCRKInfo>(originalCRKInfo0),
                    CRKNullContent          = new GameConfigDataContent<TestGameConfigCRKInfo>(null),
                };
                TestGameConfigInfoContainerMimic    originalContainerMimic  = new TestGameConfigInfoContainerMimic
                {
                    Reference               = TestGameConfigId.FromString("TestId0"),
                    NullReference           = null,
                    MetaRefReference        = TestGameConfigId.FromString("TestId0"),
                    MetaRefNullReference    = null,
                    Content                 = new TestGameConfigInfoContentMimic{ Id = TestGameConfigId.FromString("TestId1"), Value = "TestValue1" },
                    NullContent             = null,

                    IntReference            = 123,
                    IntMetaRefReference     = 123,
                    IntContent              = new TestGameConfigIntInfoContentMimic{ Id = 123, Value = "TestIntValue0" },
                    IntNullContent          = null,

                    NIntReference           = 321,
                    NIntNullReference       = null,
                    NIntMetaRefReference    = 321,
                    NIntMetaRefNullReference= null,
                    NIntContent             = new TestGameConfigIntInfoContentMimic{ Id = 321, Value = "TestNIntValue0" },
                    NIntNullContent         = null,

                    SIntReference           = 123_321,
                    SIntNullReference       = 42,
                    SIntMetaRefReference    = 123_321,
                    SIntMetaRefNullReference= 42,
                    SIntContent             = new TestGameConfigIntInfoContentMimic{ Id = 123_321, Value = "TestSIntValue0" },
                    SIntNullContent         = null,

                    CVKReference            = new TestGameConfigCustomValueKey{ X = 456 },
                    CVKMetaRefReference     = new TestGameConfigCustomValueKey{ X = 456 },
                    CVKContent              = new TestGameConfigCVKInfoContentMimic{ Id = new TestGameConfigCustomValueKey{ X = 456 }, Value = "TestCVKValue0" },
                    CVKNullContent          = null,

                    NCVKReference           = new TestGameConfigCustomValueKey{ X = 654 },
                    NCVKNullReference       = null,
                    NCVKMetaRefReference    = new TestGameConfigCustomValueKey{ X = 654 },
                    NCVKMetaRefNullReference= null,
                    NCVKContent             = new TestGameConfigCVKInfoContentMimic{ Id = new TestGameConfigCustomValueKey{ X = 654 }, Value = "TestNCVKValue0" },
                    NCVKNullContent         = null,

                    SCVKReference           = new TestGameConfigCustomValueKey{ X = 456_654 },
                    SCVKNullReference       = new TestGameConfigCustomValueKey{ X = 42 },
                    SCVKMetaRefReference    = new TestGameConfigCustomValueKey{ X = 456_654 },
                    SCVKMetaRefNullReference= new TestGameConfigCustomValueKey{ X = 42 },
                    SCVKContent             = new TestGameConfigCVKInfoContentMimic{ Id = new TestGameConfigCustomValueKey{ X = 456_654 }, Value = "TestSCVKValue0" },
                    SCVKNullContent         = null,

                    CRKReference            = new TestGameConfigCustomReferenceKey{ X = 789 },
                    CRKNullReference        = null,
                    CRKMetaRefReference     = new TestGameConfigCustomReferenceKey{ X = 789 },
                    CRKMetaRefNullReference = null,
                    CRKContent              = new TestGameConfigCRKInfoContentMimic { Id = new TestGameConfigCustomReferenceKey{ X = 789 }, Value = "TestCRKValue0" },
                    CRKNullContent          = null,
                };

                byte[] serializedContainer         = MetaSerialization.SerializeTagged(originalContainer, MetaSerializationFlags.IncludeAll, logicVersion: null);
                byte[] serializedContainerMimic    = MetaSerialization.SerializeTagged(originalContainerMimic, MetaSerializationFlags.IncludeAll, logicVersion: null);

                Assert.AreEqual(serializedContainerMimic, serializedContainer);

                // Validate mimic (not really the purpose of this test, but for sanity)
                {
                    TestGameConfigInfoContainerMimic deserializedContainerMimic = MetaSerialization.DeserializeTagged<TestGameConfigInfoContainerMimic>(serializedContainer, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);

                    Assert.AreEqual(TestGameConfigId.FromString("TestId0"), deserializedContainerMimic.Reference);
                    Assert.IsNull(deserializedContainerMimic.NullReference);
                    Assert.AreEqual(TestGameConfigId.FromString("TestId0"), deserializedContainerMimic.MetaRefReference);
                    Assert.IsNull(deserializedContainerMimic.MetaRefNullReference);
                    Assert.AreEqual(TestGameConfigId.FromString("TestId1"), deserializedContainerMimic.Content.Id);
                    Assert.AreEqual("TestValue1", deserializedContainerMimic.Content.Value);
                    Assert.IsNull(deserializedContainerMimic.NullContent);

                    Assert.AreEqual(123, deserializedContainerMimic.IntReference);
                    Assert.AreEqual(123, deserializedContainerMimic.IntMetaRefReference);
                    Assert.AreEqual(123, deserializedContainerMimic.IntContent.Id);
                    Assert.AreEqual("TestIntValue0", deserializedContainerMimic.IntContent.Value);
                    Assert.IsNull(deserializedContainerMimic.IntNullContent);

                    Assert.AreEqual(321, deserializedContainerMimic.NIntReference);
                    Assert.IsNull(deserializedContainerMimic.NIntNullReference);
                    Assert.AreEqual(321, deserializedContainerMimic.NIntMetaRefReference);
                    Assert.IsNull(deserializedContainerMimic.NIntMetaRefNullReference);
                    Assert.AreEqual(321, deserializedContainerMimic.NIntContent.Id);
                    Assert.AreEqual("TestNIntValue0", deserializedContainerMimic.NIntContent.Value);
                    Assert.IsNull(deserializedContainerMimic.NIntNullContent);

                    Assert.AreEqual(123_321, deserializedContainerMimic.SIntReference);
                    Assert.AreEqual(42, deserializedContainerMimic.SIntNullReference);
                    Assert.AreEqual(123_321, deserializedContainerMimic.SIntMetaRefReference);
                    Assert.AreEqual(42, deserializedContainerMimic.SIntMetaRefNullReference);
                    Assert.AreEqual(123_321, deserializedContainerMimic.SIntContent.Id);
                    Assert.AreEqual("TestSIntValue0", deserializedContainerMimic.SIntContent.Value);
                    Assert.IsNull(deserializedContainerMimic.SIntNullContent);

                    Assert.AreEqual(new TestGameConfigCustomValueKey{ X = 456 }, deserializedContainerMimic.CVKReference);
                    Assert.AreEqual(new TestGameConfigCustomValueKey{ X = 456 }, deserializedContainerMimic.CVKMetaRefReference);
                    Assert.AreEqual(new TestGameConfigCustomValueKey{ X = 456 }, deserializedContainerMimic.CVKContent.Id);
                    Assert.AreEqual("TestCVKValue0", deserializedContainerMimic.CVKContent.Value);
                    Assert.IsNull(deserializedContainerMimic.CVKNullContent);

                    Assert.AreEqual(new TestGameConfigCustomValueKey{ X = 654 }, deserializedContainerMimic.NCVKReference);
                    Assert.IsNull(deserializedContainerMimic.NCVKNullReference);
                    Assert.AreEqual(new TestGameConfigCustomValueKey{ X = 654 }, deserializedContainerMimic.NCVKMetaRefReference);
                    Assert.IsNull(deserializedContainerMimic.NCVKMetaRefNullReference);
                    Assert.AreEqual(new TestGameConfigCustomValueKey{ X = 654 }, deserializedContainerMimic.NCVKContent.Id);
                    Assert.AreEqual("TestNCVKValue0", deserializedContainerMimic.NCVKContent.Value);
                    Assert.IsNull(deserializedContainerMimic.NCVKNullContent);

                    Assert.AreEqual(new TestGameConfigCustomValueKey{ X = 456_654 }, deserializedContainerMimic.SCVKReference);
                    Assert.AreEqual(new TestGameConfigCustomValueKey{ X = 42 }, deserializedContainerMimic.SCVKNullReference);
                    Assert.AreEqual(new TestGameConfigCustomValueKey{ X = 456_654 }, deserializedContainerMimic.SCVKMetaRefReference);
                    Assert.AreEqual(new TestGameConfigCustomValueKey{ X = 42 }, deserializedContainerMimic.SCVKMetaRefNullReference);
                    Assert.AreEqual(new TestGameConfigCustomValueKey{ X = 456_654 }, deserializedContainerMimic.SCVKContent.Id);
                    Assert.AreEqual("TestSCVKValue0", deserializedContainerMimic.SCVKContent.Value);
                    Assert.IsNull(deserializedContainerMimic.SCVKNullContent);

                    Assert.AreEqual(new TestGameConfigCustomReferenceKey{ X = 789 }, deserializedContainerMimic.CRKReference);
                    Assert.IsNull(deserializedContainerMimic.CRKNullReference);
                    Assert.AreEqual(new TestGameConfigCustomReferenceKey{ X = 789 }, deserializedContainerMimic.CRKMetaRefReference);
                    Assert.IsNull(deserializedContainerMimic.CRKMetaRefNullReference);
                    Assert.AreEqual(new TestGameConfigCustomReferenceKey{ X = 789 }, deserializedContainerMimic.CRKContent.Id);
                    Assert.AreEqual("TestCRKValue0", deserializedContainerMimic.CRKContent.Value);
                    Assert.IsNull(deserializedContainerMimic.CRKNullContent);
                }

                // Validate proper
                {
                    TestGameConfigDataResolver resolver = new TestGameConfigDataResolver();
                    resolver.TypeAndKeyToInfo.Add(
                        (typeof(TestGameConfigInfo), TestGameConfigId.FromString("TestId0")),
                        originalInfo0);
                    resolver.TypeAndKeyToInfo.Add(
                        (typeof(TestGameConfigIntInfo), 123),
                        originalIntInfo0);
                    resolver.TypeAndKeyToInfo.Add(
                        (typeof(TestGameConfigNIntInfo), 321),
                        originalNIntInfo0);
                    resolver.TypeAndKeyToInfo.Add(
                        (typeof(TestGameConfigSIntInfo), 123_321),
                        originalSIntInfo0);
                    resolver.TypeAndKeyToInfo.Add(
                        (typeof(TestGameConfigCVKInfo), new TestGameConfigCustomValueKey{ X = 456 }),
                        originalCVKInfo0);
                    resolver.TypeAndKeyToInfo.Add(
                        (typeof(TestGameConfigNCVKInfo), new TestGameConfigCustomValueKey{ X = 654 }),
                        originalNCVKInfo0);
                    resolver.TypeAndKeyToInfo.Add(
                        (typeof(TestGameConfigSCVKInfo), new TestGameConfigCustomValueKey{ X = 456_654 }),
                        originalSCVKInfo0);
                    resolver.TypeAndKeyToInfo.Add(
                        (typeof(TestGameConfigCRKInfo), new TestGameConfigCustomReferenceKey{ X = 789 }),
                        originalCRKInfo0);

                    TestGameConfigInfoContainer deserializedContainer = MetaSerialization.DeserializeTagged<TestGameConfigInfoContainer>(serializedContainer, MetaSerializationFlags.IncludeAll, resolver, logicVersion: null);

                    Assert.True(ReferenceEquals(originalInfo0, deserializedContainer.Reference));
                    Assert.IsNull(deserializedContainer.NullReference);
                    Assert.AreEqual(TestGameConfigId.FromString("TestId0"), deserializedContainer.MetaRefReference.KeyObject);
                    Assert.IsNull(deserializedContainer.MetaRefNullReference);
                    Assert.AreEqual(TestGameConfigId.FromString("TestId1"), deserializedContainer.Content.ConfigData.Id);
                    Assert.AreEqual("TestValue1", deserializedContainer.Content.ConfigData.Value);
                    Assert.IsNull(deserializedContainer.NullContent.ConfigData);

                    Assert.True(ReferenceEquals(originalIntInfo0, deserializedContainer.IntReference));
                    Assert.AreEqual(123, deserializedContainer.IntMetaRefReference.KeyObject);
                    Assert.True(ReferenceEquals(originalIntInfo0, deserializedContainer.IntMetaRefReference.Ref));
                    Assert.AreEqual(123, deserializedContainer.IntContent.ConfigData.Id);
                    Assert.AreEqual("TestIntValue0", deserializedContainer.IntContent.ConfigData.Value);
                    Assert.IsNull(deserializedContainer.IntNullContent.ConfigData);

                    Assert.True(ReferenceEquals(originalNIntInfo0, deserializedContainer.NIntReference));
                    Assert.IsNull(deserializedContainer.NIntNullReference);
                    Assert.AreEqual(321, deserializedContainer.NIntMetaRefReference.KeyObject);
                    Assert.IsNull(deserializedContainer.NIntMetaRefNullReference);
                    Assert.AreEqual(321, deserializedContainer.NIntContent.ConfigData.Id);
                    Assert.AreEqual("TestNIntValue0", deserializedContainer.NIntContent.ConfigData.Value);
                    Assert.IsNull(deserializedContainer.NIntNullContent.ConfigData);

                    Assert.True(ReferenceEquals(originalSIntInfo0, deserializedContainer.SIntReference));
                    Assert.IsNull(deserializedContainer.SIntNullReference);
                    Assert.AreEqual(123_321, deserializedContainer.SIntMetaRefReference.KeyObject);
                    Assert.IsNull(deserializedContainer.SIntMetaRefNullReference);
                    Assert.AreEqual(123_321, deserializedContainer.SIntContent.ConfigData.Id);
                    Assert.AreEqual("TestSIntValue0", deserializedContainer.SIntContent.ConfigData.Value);
                    Assert.IsNull(deserializedContainer.SIntNullContent.ConfigData);

                    Assert.True(ReferenceEquals(originalCVKInfo0, deserializedContainer.CVKReference));
                    Assert.AreEqual(new TestGameConfigCustomValueKey{ X = 456 }, deserializedContainer.CVKMetaRefReference.KeyObject);
                    Assert.True(ReferenceEquals(originalCVKInfo0, deserializedContainer.CVKMetaRefReference.Ref));
                    Assert.AreEqual(new TestGameConfigCustomValueKey{ X = 456 }, deserializedContainer.CVKContent.ConfigData.Id);
                    Assert.AreEqual("TestCVKValue0", deserializedContainer.CVKContent.ConfigData.Value);
                    Assert.IsNull(deserializedContainer.CVKNullContent.ConfigData);

                    Assert.True(ReferenceEquals(originalNCVKInfo0, deserializedContainer.NCVKReference));
                    Assert.IsNull(deserializedContainer.NCVKNullReference);
                    Assert.AreEqual(new TestGameConfigCustomValueKey{ X = 654 }, deserializedContainer.NCVKMetaRefReference.KeyObject);
                    Assert.IsNull(deserializedContainer.NCVKMetaRefNullReference);
                    Assert.AreEqual(new TestGameConfigCustomValueKey{ X = 654 }, deserializedContainer.NCVKContent.ConfigData.Id);
                    Assert.AreEqual("TestNCVKValue0", deserializedContainer.NCVKContent.ConfigData.Value);
                    Assert.IsNull(deserializedContainer.NCVKNullContent.ConfigData);

                    Assert.True(ReferenceEquals(originalSCVKInfo0, deserializedContainer.SCVKReference));
                    Assert.IsNull(deserializedContainer.SCVKNullReference);
                    Assert.AreEqual(new TestGameConfigCustomValueKey{ X = 456_654 }, deserializedContainer.SCVKMetaRefReference.KeyObject);
                    Assert.IsNull(deserializedContainer.SCVKMetaRefNullReference);
                    Assert.AreEqual(new TestGameConfigCustomValueKey{ X = 456_654 }, deserializedContainer.SCVKContent.ConfigData.Id);
                    Assert.AreEqual("TestSCVKValue0", deserializedContainer.SCVKContent.ConfigData.Value);
                    Assert.IsNull(deserializedContainer.SCVKNullContent.ConfigData);

                    Assert.True(ReferenceEquals(originalCRKInfo0, deserializedContainer.CRKReference));
                    Assert.IsNull(deserializedContainer.CRKNullReference);
                    Assert.AreEqual(new TestGameConfigCustomReferenceKey{ X = 789 }, deserializedContainer.CRKMetaRefReference.KeyObject);
                    Assert.IsNull(deserializedContainer.CRKMetaRefNullReference);
                    Assert.AreEqual(new TestGameConfigCustomReferenceKey{ X = 789 }, deserializedContainer.CRKContent.ConfigData.Id);
                    Assert.AreEqual("TestCRKValue0", deserializedContainer.CRKContent.ConfigData.Value);
                    Assert.IsNull(deserializedContainer.CRKNullContent.ConfigData);
                }

                // Check that trying to deserialize the reference-containing object without a resolver fails
                Assert.Catch(() => MetaSerialization.DeserializeTagged<TestGameConfigInfoContainer>(serializedContainer, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null));
            }

            // Check that deserializing an object without using a resolver succeeds,
            // as long as the object doesn't contain non-null plain references (MetaRefs are ok).
            {
                TestGameConfigResolverlessInfoContainer container = new TestGameConfigResolverlessInfoContainer
                {
                    NullReference           = null,
                    MetaRefReference        = MetaRef<TestGameConfigInfo>.FromItem(originalInfo1),
                    MetaRefNullReference    = null,
                    Content                 = new GameConfigDataContent<TestGameConfigInfo>(originalInfo1),
                    NullContent             = new GameConfigDataContent<TestGameConfigInfo>(null),

                    IntMetaRefReference     = MetaRef<TestGameConfigIntInfo>.FromItem(originalIntInfo0),
                    IntContent              = new GameConfigDataContent<TestGameConfigIntInfo>(originalIntInfo0),
                    IntNullContent          = new GameConfigDataContent<TestGameConfigIntInfo>(null),

                    NIntNullReference       = null,
                    NIntMetaRefReference    = MetaRef<TestGameConfigNIntInfo>.FromItem(originalNIntInfo0),
                    NIntMetaRefNullReference= null,
                    NIntContent             = new GameConfigDataContent<TestGameConfigNIntInfo>(originalNIntInfo0),
                    NIntNullContent         = new GameConfigDataContent<TestGameConfigNIntInfo>(null),

                    SIntNullReference       = null,
                    SIntMetaRefReference    = MetaRef<TestGameConfigSIntInfo>.FromItem(originalSIntInfo0),
                    SIntMetaRefNullReference= null,
                    SIntContent             = new GameConfigDataContent<TestGameConfigSIntInfo>(originalSIntInfo0),
                    SIntNullContent         = new GameConfigDataContent<TestGameConfigSIntInfo>(null),

                    CVKMetaRefReference     = MetaRef<TestGameConfigCVKInfo>.FromItem(originalCVKInfo0),
                    CVKContent              = new GameConfigDataContent<TestGameConfigCVKInfo>(originalCVKInfo0),
                    CVKNullContent          = new GameConfigDataContent<TestGameConfigCVKInfo>(null),

                    NCVKNullReference       = null,
                    NCVKMetaRefReference    = MetaRef<TestGameConfigNCVKInfo>.FromItem(originalNCVKInfo0),
                    NCVKMetaRefNullReference= null,
                    NCVKContent             = new GameConfigDataContent<TestGameConfigNCVKInfo>(originalNCVKInfo0),
                    NCVKNullContent         = new GameConfigDataContent<TestGameConfigNCVKInfo>(null),

                    SCVKNullReference       = null,
                    SCVKMetaRefReference    = MetaRef<TestGameConfigSCVKInfo>.FromItem(originalSCVKInfo0),
                    SCVKMetaRefNullReference= null,
                    SCVKContent             = new GameConfigDataContent<TestGameConfigSCVKInfo>(originalSCVKInfo0),
                    SCVKNullContent         = new GameConfigDataContent<TestGameConfigSCVKInfo>(null),

                    CRKNullReference        = null,
                    CRKMetaRefReference     = MetaRef<TestGameConfigCRKInfo>.FromItem(originalCRKInfo0),
                    CRKMetaRefNullReference = null,
                    CRKContent              = new GameConfigDataContent<TestGameConfigCRKInfo>(originalCRKInfo0),
                    CRKNullContent          = new GameConfigDataContent<TestGameConfigCRKInfo>(null),
                };

                byte[] serialized = MetaSerialization.SerializeTagged(container, MetaSerializationFlags.IncludeAll, logicVersion: null);
                TestGameConfigResolverlessInfoContainer deserialized = MetaSerialization.DeserializeTagged<TestGameConfigResolverlessInfoContainer>(serialized, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);

                Assert.IsNull(deserialized.NullReference);
                Assert.False(deserialized.MetaRefReference.IsResolved);
                Assert.IsNull(deserialized.MetaRefReference.MaybeRef);
                Assert.AreEqual(TestGameConfigId.FromString("TestId1"), deserialized.MetaRefReference.KeyObject);
                Assert.IsNull(deserialized.MetaRefNullReference);
                Assert.AreEqual(TestGameConfigId.FromString("TestId1"), deserialized.Content.ConfigData.Id);
                Assert.AreEqual("TestValue1", deserialized.Content.ConfigData.Value);
                Assert.IsNull(deserialized.NullContent.ConfigData);

                Assert.AreEqual(123, deserialized.IntMetaRefReference.KeyObject);
                Assert.False(deserialized.IntMetaRefReference.IsResolved);
                Assert.IsNull(deserialized.IntMetaRefReference.MaybeRef);
                Assert.AreEqual(123, deserialized.IntContent.ConfigData.Id);
                Assert.AreEqual("TestIntValue0", deserialized.IntContent.ConfigData.Value);
                Assert.IsNull(deserialized.IntNullContent.ConfigData);

                Assert.IsNull(deserialized.NIntNullReference);
                Assert.False(deserialized.NIntMetaRefReference.IsResolved);
                Assert.IsNull(deserialized.NIntMetaRefReference.MaybeRef);
                Assert.AreEqual(321, deserialized.NIntMetaRefReference.KeyObject);
                Assert.IsNull(deserialized.NIntMetaRefNullReference);
                Assert.AreEqual(321, deserialized.NIntContent.ConfigData.Id);
                Assert.AreEqual("TestNIntValue0", deserialized.NIntContent.ConfigData.Value);
                Assert.IsNull(deserialized.NIntNullContent.ConfigData);

                Assert.IsNull(deserialized.SIntNullReference);
                Assert.False(deserialized.SIntMetaRefReference.IsResolved);
                Assert.IsNull(deserialized.SIntMetaRefReference.MaybeRef);
                Assert.AreEqual(123_321, deserialized.SIntMetaRefReference.KeyObject);
                Assert.IsNull(deserialized.SIntMetaRefNullReference);
                Assert.AreEqual(123_321, deserialized.SIntContent.ConfigData.Id);
                Assert.AreEqual("TestSIntValue0", deserialized.SIntContent.ConfigData.Value);
                Assert.IsNull(deserialized.SIntNullContent.ConfigData);

                Assert.AreEqual(new TestGameConfigCustomValueKey{ X = 456 }, deserialized.CVKMetaRefReference.KeyObject);
                Assert.False(deserialized.CVKMetaRefReference.IsResolved);
                Assert.IsNull(deserialized.CVKMetaRefReference.MaybeRef);
                Assert.AreEqual(new TestGameConfigCustomValueKey{ X = 456 }, deserialized.CVKContent.ConfigData.Id);
                Assert.AreEqual("TestCVKValue0", deserialized.CVKContent.ConfigData.Value);
                Assert.IsNull(deserialized.CVKNullContent.ConfigData);

                Assert.IsNull(deserialized.NCVKNullReference);
                Assert.False(deserialized.NCVKMetaRefReference.IsResolved);
                Assert.IsNull(deserialized.NCVKMetaRefReference.MaybeRef);
                Assert.AreEqual(new TestGameConfigCustomValueKey{ X = 654 }, deserialized.NCVKMetaRefReference.KeyObject);
                Assert.IsNull(deserialized.NCVKMetaRefNullReference);
                Assert.AreEqual(new TestGameConfigCustomValueKey{ X = 654 }, deserialized.NCVKContent.ConfigData.Id);
                Assert.AreEqual("TestNCVKValue0", deserialized.NCVKContent.ConfigData.Value);
                Assert.IsNull(deserialized.NCVKNullContent.ConfigData);

                Assert.IsNull(deserialized.SCVKNullReference);
                Assert.False(deserialized.SCVKMetaRefReference.IsResolved);
                Assert.IsNull(deserialized.SCVKMetaRefReference.MaybeRef);
                Assert.AreEqual(new TestGameConfigCustomValueKey{ X = 456_654 }, deserialized.SCVKMetaRefReference.KeyObject);
                Assert.IsNull(deserialized.SCVKMetaRefNullReference);
                Assert.AreEqual(new TestGameConfigCustomValueKey{ X = 456_654 }, deserialized.SCVKContent.ConfigData.Id);
                Assert.AreEqual("TestSCVKValue0", deserialized.SCVKContent.ConfigData.Value);
                Assert.IsNull(deserialized.SCVKNullContent.ConfigData);

                Assert.IsNull(deserialized.CRKNullReference);
                Assert.False(deserialized.CRKMetaRefReference.IsResolved);
                Assert.IsNull(deserialized.CRKMetaRefReference.MaybeRef);
                Assert.AreEqual(new TestGameConfigCustomReferenceKey{ X = 789 }, deserialized.CRKMetaRefReference.KeyObject);
                Assert.IsNull(deserialized.CRKMetaRefNullReference);
                Assert.AreEqual(new TestGameConfigCustomReferenceKey{ X = 789 }, deserialized.CRKContent.ConfigData.Id);
                Assert.AreEqual("TestCRKValue0", deserialized.CRKContent.ConfigData.Value);
                Assert.IsNull(deserialized.CRKNullContent.ConfigData);
            }
        }

        #endregion

        [MetaSerializable]
        public class TestTableEntry : IGameConfigData<TestStringId>, IEquatable<TestTableEntry>
        {
            [MetaMember(1)] public TestStringId ConfigKey { get; private set; }
            [MetaMember(2)] public int Payload { get; set; }

            TestTableEntry() { }
            public TestTableEntry(TestStringId configKey, int payload)
            {
                ConfigKey = configKey;
                Payload = payload;
            }

            public bool Equals(TestTableEntry other)
            {
                return other != null && ConfigKey == other.ConfigKey && Payload == other.Payload;
            }

            public override bool Equals(object obj)
            {
                return Equals(obj as TestTableEntry);
            }

            public override int GetHashCode()
            {
                throw new NotSupportedException("not supported, declared to silence a warning");
            }
        }

        [Test]
        public void TestTableSerialization()
        {
            static void TestSequence(List<TestTableEntry> entries, int overrideMaxCollectionSize = 16384)
            {
                byte[] serialized = MetaSerialization.SerializeTableTagged(entries, MetaSerializationFlags.IncludeAll, logicVersion: null, maxCollectionSizeOverride: overrideMaxCollectionSize);
                IReadOnlyList<TestTableEntry> deserializedEntries = MetaSerialization.DeserializeTableTagged<TestTableEntry>(serialized, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null, maxCollectionSizeOverride: overrideMaxCollectionSize);
                Assert.AreEqual(entries, deserializedEntries);
            }

            TestSequence(null);
            TestSequence(new List<TestTableEntry>());
            TestSequence(new List<TestTableEntry>()
            {
                new TestTableEntry(TestStringId.FromString("1"), 1),
            });
            TestSequence(new List<TestTableEntry>()
            {
                new TestTableEntry(TestStringId.FromString("1"), 1),
                new TestTableEntry(TestStringId.FromString("2"), 2),
            });
            TestSequence(new List<TestTableEntry>()
            {
                new TestTableEntry(null, 123),
                new TestTableEntry(TestStringId.FromString("2"), 2),
            });

            TestSequence(Enumerable.Repeat(new TestTableEntry(TestStringId.FromString("2"), 2), 18000).ToList(), 18000);

            Assert.That(
                () =>
                {
                    TestSequence(Enumerable.Repeat(new TestTableEntry(TestStringId.FromString("2"), 2), 18000).ToList());
                }, Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("Invalid value collection size 18000 (maximum allowed is 16384)"));

            Assert.That(
                () =>
                {
                    TestSequence(Enumerable.Repeat(new TestTableEntry(TestStringId.FromString("2"), 2), 20000).ToList(), 18000);
                }, Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("Invalid value collection size 20000 (maximum allowed is 18000)"));
        }

        // Test that values serialized as DynamicEnums (old EntityKind) deserialize as new built-in EntityKind

        [MetaSerializable]
        public class EntityKindEnum : DynamicEnum<EntityKindEnum>
        {
            public static readonly EntityKindEnum TestValue = new EntityKindEnum(2, "TestValue");
            public EntityKindEnum(int value, string name) : base(value, name, isValid: true) { }
        }

        [MetaSerializable] public class EntityKindEnum_DynamicEnum { [MetaMember(1)] public EntityKindEnum Value = EntityKindEnum.TestValue; }
        [MetaSerializable] public class EntityKindEnum_EntityKind { [MetaMember(1)] public EntityKind EntityKind; }

        [Test]
        public void TestDynamicEnumToEntityKind()
        {
            EntityKindEnum_DynamicEnum source = new EntityKindEnum_DynamicEnum();
            byte[] serialized = MetaSerialization.SerializeTagged(source, MetaSerializationFlags.IncludeAll, logicVersion: null);
            EntityKindEnum_EntityKind clone = MetaSerialization.DeserializeTagged<EntityKindEnum_EntityKind>(serialized, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);
            Assert.AreEqual(EntityKindEnum.TestValue.Id, clone.EntityKind.Value);
        }

        // Test that using the same name for different types in different namespaces is OK

        [Test]
        public void NameQualificationTest()
        {
            NameQualificationTestNamespace0.NameQualificationTestType value0 = new NameQualificationTestNamespace0.NameQualificationTestType
            {
                SameNameField = 123,
                DifferentNameField0 = 456,
            };
            NameQualificationTestNamespace1.NameQualificationTestType value1 = new NameQualificationTestNamespace1.NameQualificationTestType
            {
                SameNameField = "abc",
                DifferentNameField1 = "def",
            };

            NameQualificationTestNamespace0.NameQualificationTestType clonedValue0 = MetaSerialization.CloneTagged(value0, MetaSerializationFlags.IncludeAll, logicVersion: null, resolver: null);
            NameQualificationTestNamespace1.NameQualificationTestType clonedValue1 = MetaSerialization.CloneTagged(value1, MetaSerializationFlags.IncludeAll, logicVersion: null, resolver: null);

            Assert.AreEqual(123, clonedValue0.SameNameField);
            Assert.AreEqual(456, clonedValue0.DifferentNameField0);

            Assert.AreEqual("abc", clonedValue1.SameNameField);
            Assert.AreEqual("def", clonedValue1.DifferentNameField1);
        }

        #region Cyclic types

        [MetaSerializable]
        public class CyclicTypesSimple
        {
            [MetaMember(1)] public int Value;
            [MetaMember(2)] public CyclicTypesSimple X;
        }

        // \note CyclicTypesTestMutual* use a slightly nontrivial structure
        //       containing a separate "Root" type and also List<>s,
        //       to test a case where MetaSerializerTypeRegistry used to
        //       accidentally attempt to register a type multiple times and
        //       would throw, at init time. That bug didn't trigger with the
        //       most trivial kind of multi-type cycle.

        [MetaSerializable]
        public class CyclicTypesMutualRoot
        {
            [MetaMember(1)] public int Value;
            [MetaMember(2)] public List<CyclicTypesMutualA> X;
        }

        [MetaSerializable]
        public class CyclicTypesMutualA
        {
            [MetaMember(1)] public int Value;
            [MetaMember(2)] public CyclicTypesMutualB X;
        }

        [MetaSerializable]
        public class CyclicTypesMutualB
        {
            [MetaMember(1)] public int Value;
            [MetaMember(2)] public List<CyclicTypesMutualA> X;
        }

        [Test]
        public void CyclicTypesTestSimple()
        {
            CyclicTypesSimple original = new CyclicTypesSimple
            {
                Value = 123,
                X = new CyclicTypesSimple
                {
                    Value = 456,
                    X = new CyclicTypesSimple
                    {
                        Value = 789,
                    }
                }
            };

            byte[] serialized = MetaSerialization.SerializeTagged(original, MetaSerializationFlags.IncludeAll, logicVersion: null);
            CyclicTypesSimple cloned = MetaSerialization.DeserializeTagged<CyclicTypesSimple>(serialized, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);

            Assert.AreEqual(123, cloned.Value);
            Assert.AreEqual(456, cloned.X.Value);
            Assert.AreEqual(789, cloned.X.X.Value);
            Assert.IsNull(cloned.X.X.X);
        }

        [Test]
        public void CyclicTypesTestMutual()
        {
            CyclicTypesMutualRoot original = new CyclicTypesMutualRoot
            {
                Value = 1,
                X = new List<CyclicTypesMutualA>
                {
                    new CyclicTypesMutualA
                    {
                        Value = 2,
                        X = new CyclicTypesMutualB
                        {
                            Value = 3,
                            X = new List<CyclicTypesMutualA>
                            {
                                new CyclicTypesMutualA
                                {
                                    Value = 4,
                                    X = new CyclicTypesMutualB
                                    {
                                        Value = 5,
                                    },
                                },
                                new CyclicTypesMutualA
                                {
                                    Value = 6,
                                    X = new CyclicTypesMutualB
                                    {
                                        Value = 7,
                                    },
                                }
                            }
                        }
                    },
                    new CyclicTypesMutualA
                    {
                        Value = 8,
                        X = new CyclicTypesMutualB
                        {
                            Value = 9,
                            X = new List<CyclicTypesMutualA>
                            {
                                new CyclicTypesMutualA
                                {
                                    Value = 10,
                                    X = new CyclicTypesMutualB
                                    {
                                        Value = 11,
                                    },
                                },
                                new CyclicTypesMutualA
                                {
                                    Value = 12,
                                    X = new CyclicTypesMutualB
                                    {
                                        Value = 13,
                                    },
                                },
                            },
                        },
                    },
                },
            };

            byte[] serialized = MetaSerialization.SerializeTagged(original, MetaSerializationFlags.IncludeAll, logicVersion: null);
            CyclicTypesMutualRoot cloned = MetaSerialization.DeserializeTagged<CyclicTypesMutualRoot>(serialized, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);

            Assert.AreEqual(1, cloned.Value);
            Assert.AreEqual(2, cloned.X[0].Value);
            Assert.AreEqual(3, cloned.X[0].X.Value);
            Assert.AreEqual(4, cloned.X[0].X.X[0].Value);
            Assert.AreEqual(5, cloned.X[0].X.X[0].X.Value);
            Assert.AreEqual(6, cloned.X[0].X.X[1].Value);
            Assert.AreEqual(7, cloned.X[0].X.X[1].X.Value);
            Assert.AreEqual(8, cloned.X[1].Value);
            Assert.AreEqual(9, cloned.X[1].X.Value);
            Assert.AreEqual(10, cloned.X[1].X.X[0].Value);
            Assert.AreEqual(11, cloned.X[1].X.X[0].X.Value);
            Assert.AreEqual(12, cloned.X[1].X.X[1].Value);
            Assert.AreEqual(13, cloned.X[1].X.X[1].X.Value);
        }

        // Some other cyclic type structures. Actual serialization tests not implemented,
        // these are here just to test that serializer init doesn't break.

        [MetaSerializable] public class CyclicTypesMutualPlainRefRoot                                   { [MetaMember(1)] public CyclicTypesMutualPlainRefA X; }
        [MetaSerializable] public class CyclicTypesMutualPlainRefA : IGameConfigData<int>               { [MetaMember(1)] public CyclicTypesMutualPlainRefB X; public int ConfigKey => throw new NotImplementedException(); }
        [MetaSerializable] public class CyclicTypesMutualPlainRefB                                      { [MetaMember(1)] public CyclicTypesMutualPlainRefA X; }

        [MetaSerializable] public class CyclicTypesMutualMetaRefRoot                                    { [MetaMember(1)] public MetaRef<CyclicTypesMutualMetaRefA> X; }
        [MetaSerializable] public class CyclicTypesMutualMetaRefA : IGameConfigData<int>                { [MetaMember(1)] public CyclicTypesMutualMetaRefB X; public int ConfigKey => throw new NotImplementedException(); }
        [MetaSerializable] public class CyclicTypesMutualMetaRefB                                       { [MetaMember(1)] public MetaRef<CyclicTypesMutualMetaRefA> X; }

        [MetaSerializable] public class CyclicTypesMutualNullableRoot                                   { [MetaMember(1)] public Nullable<CyclicTypesMutualNullableA> X; }
        [MetaSerializable] public struct CyclicTypesMutualNullableA                                     { [MetaMember(1)] public CyclicTypesMutualNullableB X; public int ConfigKey => throw new NotImplementedException(); }
        [MetaSerializable] public class CyclicTypesMutualNullableB                                      { [MetaMember(1)] public Nullable<CyclicTypesMutualNullableA> X; }

        [MetaSerializable] public class CyclicTypesMutualGameConfigDataContentRoot                      { [MetaMember(1)] public GameConfigDataContent<CyclicTypesMutualGameConfigDataContentA> X; }
        [MetaSerializable] public class CyclicTypesMutualGameConfigDataContentA : IGameConfigData<int>  { [MetaMember(1)] public CyclicTypesMutualGameConfigDataContentB X; public int ConfigKey => throw new NotImplementedException(); }
        [MetaSerializable] public class CyclicTypesMutualGameConfigDataContentB                         { [MetaMember(1)] public GameConfigDataContent<CyclicTypesMutualGameConfigDataContentA> X; }

        [MetaSerializable] public class CyclicTypesMutualMetaSerializedRoot                             { [MetaMember(1)] public MetaSerialized<CyclicTypesMutualMetaSerializedA> X; }
        [MetaSerializable] public class CyclicTypesMutualMetaSerializedA                                { [MetaMember(1)] public CyclicTypesMutualMetaSerializedB X; public int ConfigKey => throw new NotImplementedException(); }
        [MetaSerializable] public class CyclicTypesMutualMetaSerializedB                                { [MetaMember(1)] public MetaSerialized<CyclicTypesMutualMetaSerializedA> X; }

        #endregion

        #region Deserialization conversion

        // \note Several kinds of conversions are tested here; namely the different
        //       converters defined in DeserializationConverters. But to reduce verbosity,
        //       only one kind of conversion (struct-to-class, as an arbitrary choice)
        //       tests also collections and value-collections containing the test subject
        //       types; the rest only test the plain single-object case. This should be OK
        //       because it is likely that the collection cases will either work for all
        //       conversions or be broken for all conversions.

        #region Struct to class

        [MetaSerializable]
        public struct ConvertStructToClass_Struct
        {
            [MetaMember(1)] public string X;
        }

        [MetaSerializable]
        [MetaDeserializationConvertFromStruct]
        public class ConvertStructToClass_Class
        {
            [MetaMember(1)] public string X;
        }

        [MetaSerializable]
        public class ConvertStructToClass_StructContainer
        {
            [MetaMember(1)] public ConvertStructToClass_Struct                                                  Struct;

            [MetaMember(2)] public List<ConvertStructToClass_Struct>                                            StructList;

            [MetaMember(3)] public OrderedDictionary<string, ConvertStructToClass_Struct>                       StructValuedDict;
            [MetaMember(4)] public OrderedDictionary<ConvertStructToClass_Struct, string>                       StructKeyedDict;
            [MetaMember(5)] public OrderedDictionary<ConvertStructToClass_Struct, ConvertStructToClass_Struct>  StructKeyedAndValuedDict;
        }

        [MetaSerializable]
        public class ConvertStructToClass_ClassContainer
        {
            [MetaMember(1)] public ConvertStructToClass_Class                                                   Class;

            [MetaMember(2)] public List<ConvertStructToClass_Class>                                             ClassList;

            [MetaMember(3)] public OrderedDictionary<string, ConvertStructToClass_Class>                        ClassValuedDict;
            [MetaMember(4)] public OrderedDictionary<ConvertStructToClass_Class, string>                        ClassKeyedDict;
            [MetaMember(5)] public OrderedDictionary<ConvertStructToClass_Class, ConvertStructToClass_Class>    ClassKeyedAndValuedDict;

            [MetaMember(6)] public ConvertStructToClass_Class                                                   Null;
        }

        [Test]
        public void DeserializationConversionStructToClass()
        {
            ConvertStructToClass_StructContainer structContainer = new ConvertStructToClass_StructContainer
            {
                Struct = new ConvertStructToClass_Struct { X = "X" },

                StructList = new List<ConvertStructToClass_Struct>
                {
                    new ConvertStructToClass_Struct { X = "Elem0X"  },
                    new ConvertStructToClass_Struct { X = "Elem1X"  },
                },

                StructValuedDict = new OrderedDictionary<string, ConvertStructToClass_Struct>
                {
                    { "Key0", new ConvertStructToClass_Struct { X = "Value0X" } },
                    { "Key1", new ConvertStructToClass_Struct { X = "Value1X" } },
                },

                StructKeyedDict = new OrderedDictionary<ConvertStructToClass_Struct, string>
                {
                    { new ConvertStructToClass_Struct { X = "Key0X" }, "Value0" },
                    { new ConvertStructToClass_Struct { X = "Key1X" }, "Value1" },
                },

                StructKeyedAndValuedDict = new OrderedDictionary<ConvertStructToClass_Struct, ConvertStructToClass_Struct>
                {
                    { new ConvertStructToClass_Struct { X = "Key0X" }, new ConvertStructToClass_Struct { X = "Value0X" } },
                    { new ConvertStructToClass_Struct { X = "Key1X" }, new ConvertStructToClass_Struct { X = "Value1X" } },
                },
            };

            byte[] structContainerSerialized = MetaSerialization.SerializeTagged(structContainer, MetaSerializationFlags.IncludeAll, logicVersion: null);

            ConvertStructToClass_ClassContainer deserializedClassContainer = MetaSerialization.DeserializeTagged<ConvertStructToClass_ClassContainer>(structContainerSerialized, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);
            Check(deserializedClassContainer);

            // Test also non-converting deserialization, by cloning the deserialized object
            ConvertStructToClass_ClassContainer clonedClassContainer = MetaSerialization.CloneTagged(deserializedClassContainer, MetaSerializationFlags.IncludeAll, logicVersion: null, resolver: null);
            Check(clonedClassContainer);

            static void Check(ConvertStructToClass_ClassContainer classContainer)
            {
                Assert.AreEqual("X", classContainer.Class.X);

                Assert.AreEqual(2, classContainer.ClassList.Count);
                Assert.AreEqual("Elem0X", classContainer.ClassList[0].X);
                Assert.AreEqual("Elem1X", classContainer.ClassList[1].X);

                Assert.AreEqual(2, classContainer.ClassValuedDict.Count);
                Assert.AreEqual("Key0", classContainer.ClassValuedDict.ElementAt(0).Key);
                Assert.AreEqual("Value0X", classContainer.ClassValuedDict.ElementAt(0).Value.X);
                Assert.AreEqual("Key1", classContainer.ClassValuedDict.ElementAt(1).Key);
                Assert.AreEqual("Value1X", classContainer.ClassValuedDict.ElementAt(1).Value.X);

                Assert.AreEqual(2, classContainer.ClassKeyedDict.Count);
                Assert.AreEqual("Key0X", classContainer.ClassKeyedDict.ElementAt(0).Key.X);
                Assert.AreEqual("Value0", classContainer.ClassKeyedDict.ElementAt(0).Value);
                Assert.AreEqual("Key1X", classContainer.ClassKeyedDict.ElementAt(1).Key.X);
                Assert.AreEqual("Value1", classContainer.ClassKeyedDict.ElementAt(1).Value);

                Assert.AreEqual(2, classContainer.ClassKeyedAndValuedDict.Count);
                Assert.AreEqual("Key0X", classContainer.ClassKeyedAndValuedDict.ElementAt(0).Key.X);
                Assert.AreEqual("Value0X", classContainer.ClassKeyedAndValuedDict.ElementAt(0).Value.X);
                Assert.AreEqual("Key1X", classContainer.ClassKeyedAndValuedDict.ElementAt(1).Key.X);
                Assert.AreEqual("Value1X", classContainer.ClassKeyedAndValuedDict.ElementAt(1).Value.X);

                Assert.IsNull(classContainer.Null);
            }
        }

        #endregion

        #region Concrete class to abstract

        [MetaSerializable]
        public class ConvertConcreteToBase_Class
        {
            [MetaMember(1)] public string X;
            [MetaMember(2)] public string Y;
        }

        [MetaSerializable]
        [MetaDeserializationConvertFromConcreteDerivedType(typeof(ConvertConcreteToBase_Derived0))]
        public abstract class ConvertConcreteToBase_Base
        {
            [MetaMember(1)] public string X;
        }

        [MetaSerializableDerived(123)]
        public class ConvertConcreteToBase_Derived0 : ConvertConcreteToBase_Base
        {
            [MetaMember(2)] public string Y;
        }

        [MetaSerializableDerived(456)]
        public class ConvertConcreteToBase_Derived1 : ConvertConcreteToBase_Base
        {
            [MetaMember(3)] public string Z;
        }

        [Test]
        public void DeserializationConversionConcreteClassToBase()
        {
            // Non-null
            {
                ConvertConcreteToBase_Class original = new ConvertConcreteToBase_Class { X = "X", Y = "Y" };
                byte[] serialized = MetaSerialization.SerializeTagged(original, MetaSerializationFlags.IncludeAll, logicVersion: null);

                ConvertConcreteToBase_Base deserialized = MetaSerialization.DeserializeTagged<ConvertConcreteToBase_Base>(serialized, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);

                Assert.AreEqual("X", ((ConvertConcreteToBase_Derived0)deserialized).X);
                Assert.AreEqual("Y", ((ConvertConcreteToBase_Derived0)deserialized).Y);

                // Test also non-converting deserialization, by cloning the deserialized object

                ConvertConcreteToBase_Base cloned = MetaSerialization.CloneTagged(deserialized, MetaSerializationFlags.IncludeAll, logicVersion: null, resolver: null);

                Assert.AreEqual("X", ((ConvertConcreteToBase_Derived0)cloned).X);
                Assert.AreEqual("Y", ((ConvertConcreteToBase_Derived0)cloned).Y);
            }

            // Null
            {
                ConvertConcreteToBase_Class original = null;
                byte[] serialized = MetaSerialization.SerializeTagged(original, MetaSerializationFlags.IncludeAll, logicVersion: null);

                ConvertConcreteToBase_Base deserialized = MetaSerialization.DeserializeTagged<ConvertConcreteToBase_Base>(serialized, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);

                Assert.IsNull(deserialized);

                // Test also non-converting deserialization, by cloning the deserialized object

                ConvertConcreteToBase_Base cloned = MetaSerialization.CloneTagged(deserialized, MetaSerializationFlags.IncludeAll, logicVersion: null, resolver: null);

                Assert.IsNull(cloned);
            }
        }

        #endregion

        #region Concrete class to interface

        [MetaSerializable]
        public class ConvertConcreteToInterface_Class
        {
            [MetaMember(1)] public string X;
            [MetaMember(2)] public string Y;
        }

        [MetaSerializable]
        [MetaDeserializationConvertFromConcreteDerivedType(typeof(ConvertConcreteToInterface_Derived0))]
        public interface IConvertConcreteToInterface_Interface
        {
            string X { get; }
        }

        [MetaSerializableDerived(123)]
        public class ConvertConcreteToInterface_Derived0 : IConvertConcreteToInterface_Interface
        {
            [MetaMember(1)] public string X { get; private set; }
            [MetaMember(2)] public string Y;
        }

        [MetaSerializableDerived(456)]
        public class ConvertConcreteToInterface_Derived1 : IConvertConcreteToInterface_Interface
        {
            [MetaMember(1)] public string X { get; private set; }
            [MetaMember(3)] public string Z;
        }

        [Test]
        public void DeserializationConversionConcreteClassToInterface()
        {
            // Non-null
            {
                ConvertConcreteToInterface_Class original = new ConvertConcreteToInterface_Class { X = "X", Y = "Y" };
                byte[] serialized = MetaSerialization.SerializeTagged(original, MetaSerializationFlags.IncludeAll, logicVersion: null);

                IConvertConcreteToInterface_Interface deserialized = MetaSerialization.DeserializeTagged<IConvertConcreteToInterface_Interface>(serialized, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);

                Assert.AreEqual("X", ((ConvertConcreteToInterface_Derived0)deserialized).X);
                Assert.AreEqual("Y", ((ConvertConcreteToInterface_Derived0)deserialized).Y);

                // Test also non-converting deserialization, by cloning the deserialized object

                IConvertConcreteToInterface_Interface cloned = MetaSerialization.CloneTagged(deserialized, MetaSerializationFlags.IncludeAll, logicVersion: null, resolver: null);

                Assert.AreEqual("X", ((ConvertConcreteToInterface_Derived0)cloned).X);
                Assert.AreEqual("Y", ((ConvertConcreteToInterface_Derived0)cloned).Y);
            }

            // Null
            {
                ConvertConcreteToBase_Class original = null;
                byte[] serialized = MetaSerialization.SerializeTagged(original, MetaSerializationFlags.IncludeAll, logicVersion: null);

                ConvertConcreteToBase_Base deserialized = MetaSerialization.DeserializeTagged<ConvertConcreteToBase_Base>(serialized, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);

                Assert.IsNull(deserialized);

                // Test also non-converting deserialization, by cloning the deserialized object

                ConvertConcreteToBase_Base cloned = MetaSerialization.CloneTagged(deserialized, MetaSerializationFlags.IncludeAll, logicVersion: null, resolver: null);

                Assert.IsNull(cloned);
            }
        }

        #endregion

        #region Struct to abstract

        [MetaSerializable]
        public struct ConvertStructToBase_Struct
        {
            [MetaMember(1)] public string X;
            [MetaMember(2)] public string Y;
        }

        [MetaSerializable]
        [MetaDeserializationConvertFromConcreteDerivedTypeStruct(typeof(ConvertStructToBase_Derived0))]
        public abstract class ConvertStructToBase_Base
        {
            [MetaMember(1)] public string X;
        }

        [MetaSerializableDerived(123)]
        public class ConvertStructToBase_Derived0 : ConvertStructToBase_Base
        {
            [MetaMember(2)] public string Y;
        }

        [MetaSerializableDerived(456)]
        public class ConvertStructToBase_Derived1 : ConvertStructToBase_Base
        {
            [MetaMember(3)] public string Z;
        }

        [Test]
        public void DeserializationConversionConcreteStructToBase()
        {
            ConvertStructToBase_Struct original = new ConvertStructToBase_Struct { X = "X", Y = "Y" };
            byte[] serialized = MetaSerialization.SerializeTagged(original, MetaSerializationFlags.IncludeAll, logicVersion: null);

            ConvertStructToBase_Base deserialized = MetaSerialization.DeserializeTagged<ConvertStructToBase_Base>(serialized, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);

            Assert.AreEqual("X", ((ConvertStructToBase_Derived0)deserialized).X);
            Assert.AreEqual("Y", ((ConvertStructToBase_Derived0)deserialized).Y);

            // Test also non-converting deserialization, by cloning the deserialized object

            ConvertStructToBase_Base cloned = MetaSerialization.CloneTagged(deserialized, MetaSerializationFlags.IncludeAll, logicVersion: null, resolver: null);

            Assert.AreEqual("X", ((ConvertStructToBase_Derived0)cloned).X);
            Assert.AreEqual("Y", ((ConvertStructToBase_Derived0)cloned).Y);
        }

        #endregion

        #region Concrete to abstract using IMetaIntegration

        // \note Some types are elsewhere in this file, in namespaces
        //       Metaplay.CloudCoreTestsIntegrationTypes and TestUser.CloudCoreTestsIntegrationTypes.
        //       We need to do that because integration types are filtered based on namespace:
        //       - Cloud.Tests is ignored, so can't put there
        //       - Integration types must be in a namespace beginning with "Metaplay."
        //       - Testing a "user" override (ConvertIntegrationTypeToBase_User) so it must be
        //         in a namespace not beginning with "Metaplay."

        [MetaSerializable]
        public class ConvertIntegrationTypeToBase_Class
        {
            [MetaMember(1)] public string X;
            [MetaMember(2)] public string Y;
        }

        [Test]
        public void DeserializationConversionIntegrationTypeToBase()
        {
            ConvertIntegrationTypeToBase_Class original = new ConvertIntegrationTypeToBase_Class { X = "X", Y = "Y" };
            byte[] serialized = MetaSerialization.SerializeTagged(original, MetaSerializationFlags.IncludeAll, logicVersion: null);

            ConvertIntegrationTypeToBase_Base deserialized = MetaSerialization.DeserializeTagged<ConvertIntegrationTypeToBase_Base>(serialized, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);

            Assert.AreEqual("X", ((ConvertIntegrationTypeToBase_User)deserialized).X);
            Assert.AreEqual("Y", ((ConvertIntegrationTypeToBase_User)deserialized).Y);
        }

        #endregion

        #endregion

        [Test]
        public void CompileSerializerWithTrampolines()
        {
            string dir = Path.Join(TestContext.CurrentContext.WorkDirectory, nameof(CompileSerializerWithTrampolines));

            Assert.DoesNotThrow(() => RoslynSerializerCompileCache.CompileAssembly(
                outputDir:                  dir,
                dllFileName:                "Metaplay.Generated-WithTrampolines.Test.dll",
                errorDir:                   dir,
                useMemberAccessTrampolines: true,
                generateRuntimeTypeInfo:    true));
        }
    }
}

namespace Metaplay.CloudCoreTestsIntegrationTypes
{
    [MetaSerializable]
    [MetaDeserializationConvertFromIntegrationImplementation]
    public abstract class ConvertIntegrationTypeToBase_Base : IMetaIntegrationConstructible<ConvertIntegrationTypeToBase_Base>
    {
        [MetaMember(1)] public string X;
    }

    [MetaSerializableDerived(1)]
    public class ConvertIntegrationTypeToBase_Default : ConvertIntegrationTypeToBase_Base
    {
        [MetaMember(3)] public string Z;
    }
}

namespace TestUser.CloudCoreTestsIntegrationTypes
{
    [MetaSerializableDerived(2)]
    public class ConvertIntegrationTypeToBase_User : ConvertIntegrationTypeToBase_Base
    {
        [MetaMember(2)] public string Y;
    }
}
