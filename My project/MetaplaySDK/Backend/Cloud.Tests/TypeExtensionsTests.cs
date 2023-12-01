// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using NUnit.Framework;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace Cloud.Tests
{
    [TestFixture]
    public class TypeExtensionsTests
    {
        public class NestingClass
        {
            public class NestedClass
            {
                public int Field;
                public int Method() => 1;
                public int Property => 2;
            }
            public class NestedClassG<T1, T2>
            {
                public int Field;
                public int Method() => 1;
                public int Property => 2;
            }

            public int Field;
            public int Method() => 1;
            public int Property => 2;
        }
        public class NestingGClass<T>
        {
            public class NestedClassInG { }
            public class NestedGClassInG<Tinner> { }
        }
        public class NestingG2Class<T1, T2>
        {
            public class NestedG2Class<T3,T4> { }
        }

        [TestCase(typeof(int), "Int32")]
        [TestCase(typeof(string), "String")]
        [TestCase(typeof(System.InvalidOperationException), "InvalidOperationException")]
        [TestCase(typeof(NestingClass.NestedClass), "TypeExtensionsTests.NestingClass.NestedClass", TestName = "GetNestedClassName(NestingClass.NestedClass)")] // naming bug, see below
        [TestCase(typeof(NestingClass.NestedClass[]), "TypeExtensionsTests.NestingClass.NestedClass[]", TestName = "GetNestedClassName(NestingClass.NestedClass[])")]
        [TestCase(typeof(NestingClass.NestedClassG<,>), "TypeExtensionsTests.NestingClass.NestedClassG<,>", TestName = "GetNestedClassName(NestingClass.NestedClassG<,>)")]
        [TestCase(typeof(NestingClass.NestedClassG<int, float[]>), "TypeExtensionsTests.NestingClass.NestedClassG<Int32,Single[]>", TestName = "GetNestedClassName(NestingClass.NestedClassG<int, float[]>)")]
        [TestCase(typeof(NestingClass.NestedClassG<int, float[]>[]), "TypeExtensionsTests.NestingClass.NestedClassG<Int32,Single[]>[]", TestName = "GetNestedClassName(NestingClass.NestedClassG<int, float[]>[])")]
        [TestCase(typeof(NestingGClass<>.NestedClassInG), "TypeExtensionsTests.NestingGClass<>.NestedClassInG", TestName = "GetNestedClassName(NestingClass.NestingGClass<>.NestedClassInG)")]
        [TestCase(typeof(NestingGClass<Exception>.NestedClassInG), "TypeExtensionsTests.NestingGClass<Exception>.NestedClassInG", TestName = "GetNestedClassName(NestingGClass<Exception>.NestedClassInG)")]
        [TestCase(typeof(NestingGClass<Exception>.NestedGClassInG<long>), "TypeExtensionsTests.NestingGClass<Exception>.NestedGClassInG<Int64>", TestName = "GetNestedClassName(NestingGClass<Exception>.NestedGClassInG<long>)")]
        [TestCase(typeof(NestingGClass<>.NestedGClassInG<>), "TypeExtensionsTests.NestingGClass<>.NestedGClassInG<>", TestName = "GetNestedClassName(NestingGClass<>.NestedGClassInG<>)")]
        [TestCase(typeof(NestingG2Class<,>.NestedG2Class<,>), "TypeExtensionsTests.NestingG2Class<,>.NestedG2Class<,>", TestName = "GetNestedClassName(NestingG2Class<,>.NestedG2Class<,>)")]
        [TestCase(typeof(NestingG2Class<int,string>.NestedG2Class<long,float>), "TypeExtensionsTests.NestingG2Class<Int32,String>.NestedG2Class<Int64,Single>", TestName = "GetNestedClassName(NestingG2Class<int,string>.NestedG2Class<long,float>)")]
        [TestCase(typeof(NestingG2Class<int,NestingClass.NestedClassG<NestingClass.NestedClass[], NestingGClass<int[]>>>.NestedG2Class<int,float>), "TypeExtensionsTests.NestingG2Class<Int32,TypeExtensionsTests.NestingClass.NestedClassG<TypeExtensionsTests.NestingClass.NestedClass[],TypeExtensionsTests.NestingGClass<Int32[]>>>.NestedG2Class<Int32,Single>", TestName = "GetNestedClassName(NestingG2Class<int,NestingClass.NestedClassG<NestingClass.NestedClass[], NestingGClass<int[]>>>.NestedG2Class<int,float>)")]
        [TestCase(typeof(int[]), "Int32[]")]
        [TestCase(typeof(List<>), "List<>")]
        [TestCase(typeof(List<int>), "List<Int32>", TestName = "GetNestedClassName(List<int>)")]
        [TestCase(typeof(List<List<float>>), "List<List<Single>>", TestName = "GetNestedClassName(List<List<float>>)")]
        [TestCase(typeof(int[][]), "Int32[][]")]
        [TestCase(typeof(int[,][,]), "Int32[,][,]")]
        [TestCase(typeof(int[][,]), "Int32[][,]")]
        [TestCase(typeof(int[,][]), "Int32[,][]")]
        [TestCase(typeof(List<int[][]>[][]), "List<Int32[][]>[][]")]
        public void GetNestedClassName(Type type, string expected)
        {
            Assert.AreEqual(expected, TypeExtensions.GetNestedClassName(type));
        }

        [TestCase(typeof(int), "Int32")]
        [TestCase(typeof(string), "String")]
        [TestCase(typeof(System.InvalidOperationException), "InvalidOperationException")]
        [TestCase(typeof(NestingClass.NestedClass), "TypeExtensionsTests.NestingClass.NestedClass", TestName = "ToGenericTypeString(NestingClass.NestedClass)")] // naming bug, see below
        [TestCase(typeof(int[]), "Int32[]")]
        [TestCase(typeof(List<float>), "List<Single>")]
        [TestCase(typeof(List<List<float>>), "List<List<Single>>")]
        public void ToGenericTypeString(Type type, string expected)
        {
            Assert.AreEqual(expected, TypeExtensions.ToGenericTypeString(type));
        }

        [TestCase(typeof(int), "System.Int32")]
        [TestCase(typeof(string), "System.String")]
        [TestCase(typeof(System.InvalidOperationException), "System.InvalidOperationException")]
        [TestCase(typeof(NestingClass.NestedClass), "Cloud.Tests.TypeExtensionsTests.NestingClass.NestedClass", TestName = "ToNamespaceQualifiedTypeString(NestingClass.NestedClass)")] // naming bug, see below
        [TestCase(typeof(NestingClass.NestedClass[]), "Cloud.Tests.TypeExtensionsTests.NestingClass.NestedClass[]", TestName = "ToNamespaceQualifiedTypeString(NestingClass.NestedClass[])")]
        [TestCase(typeof(NestingClass.NestedClassG<,>), "Cloud.Tests.TypeExtensionsTests.NestingClass.NestedClassG<,>", TestName = "ToNamespaceQualifiedTypeString(NestingClass.NestedClassG<,>)")]
        [TestCase(typeof(NestingClass.NestedClassG<int, float[]>), "Cloud.Tests.TypeExtensionsTests.NestingClass.NestedClassG<System.Int32,System.Single[]>", TestName = "ToNamespaceQualifiedTypeString(NestingClass.NestedClassG<int, float[]>)")]
        [TestCase(typeof(NestingClass.NestedClassG<int, float[]>[]), "Cloud.Tests.TypeExtensionsTests.NestingClass.NestedClassG<System.Int32,System.Single[]>[]", TestName = "ToNamespaceQualifiedTypeString(NestingClass.NestedClassG<int, float[]>[])")]
        [TestCase(typeof(NestingGClass<>.NestedClassInG), "Cloud.Tests.TypeExtensionsTests.NestingGClass<>.NestedClassInG", TestName = "ToNamespaceQualifiedTypeString(NestingClass.NestingGClass<>.NestedClassInG)")]
        [TestCase(typeof(NestingGClass<Exception>.NestedClassInG), "Cloud.Tests.TypeExtensionsTests.NestingGClass<System.Exception>.NestedClassInG", TestName = "ToNamespaceQualifiedTypeString(NestingGClass<Exception>.NestedClassInG)")]
        [TestCase(typeof(NestingGClass<Exception>.NestedGClassInG<long>), "Cloud.Tests.TypeExtensionsTests.NestingGClass<System.Exception>.NestedGClassInG<System.Int64>", TestName = "ToNamespaceQualifiedTypeString(NestingGClass<Exception>.NestedGClassInG<long>)")]
        [TestCase(typeof(NestingGClass<>.NestedGClassInG<>), "Cloud.Tests.TypeExtensionsTests.NestingGClass<>.NestedGClassInG<>", TestName = "ToNamespaceQualifiedTypeString(NestingGClass<>.NestedGClassInG<>)")]
        [TestCase(typeof(NestingG2Class<,>.NestedG2Class<,>), "Cloud.Tests.TypeExtensionsTests.NestingG2Class<,>.NestedG2Class<,>", TestName = "ToNamespaceQualifiedTypeString(NestingG2Class<,>.NestedG2Class<,>)")]
        [TestCase(typeof(NestingG2Class<int,string>.NestedG2Class<long,float>), "Cloud.Tests.TypeExtensionsTests.NestingG2Class<System.Int32,System.String>.NestedG2Class<System.Int64,System.Single>", TestName = "ToNamespaceQualifiedTypeString(NestingG2Class<int,string>.NestedG2Class<long,float>)")]
        [TestCase(typeof(NestingG2Class<int,NestingClass.NestedClassG<NestingClass.NestedClass[], NestingGClass<int[]>>>.NestedG2Class<int,float>), "Cloud.Tests.TypeExtensionsTests.NestingG2Class<System.Int32,Cloud.Tests.TypeExtensionsTests.NestingClass.NestedClassG<Cloud.Tests.TypeExtensionsTests.NestingClass.NestedClass[],Cloud.Tests.TypeExtensionsTests.NestingGClass<System.Int32[]>>>.NestedG2Class<System.Int32,System.Single>", TestName = "ToNamespaceQualifiedTypeString(NestingG2Class<int,NestingClass.NestedClassG<NestingClass.NestedClass[], NestingGClass<int[]>>>.NestedG2Class<int,float>)")]
        [TestCase(typeof(int[]), "System.Int32[]")]
        [TestCase(typeof(List<>), "System.Collections.Generic.List<>")]
        [TestCase(typeof(List<int>), "System.Collections.Generic.List<System.Int32>", TestName = "ToNamespaceQualifiedTypeString(List<int>)")]
        [TestCase(typeof(List<List<float>>), "System.Collections.Generic.List<System.Collections.Generic.List<System.Single>>", TestName = "ToNamespaceQualifiedTypeString(List<List<float>>)")]
        public void ToNamespaceQualifiedTypeString(Type type, string expected)
        {
            Assert.AreEqual(expected, TypeExtensions.ToNamespaceQualifiedTypeString(type));
        }

        [TestCase(typeof(int), "global::System.Int32")]
        [TestCase(typeof(List<int>), "global::System.Collections.Generic.List<global::System.Int32>", TestName = "ToGlobalNamespaceQualifiedTypeString(List<int>)")]
        public void ToGlobalNamespaceQualifiedTypeString(Type type, string expected)
        {
            Assert.AreEqual(expected, TypeExtensions.ToGlobalNamespaceQualifiedTypeString(type));
        }

        [Test]
        public void ToNamespaceQualifiedTypeStringNegative()
        {
            Type genericDefinition = typeof(NestingGClass<>.NestedClassInG);
            Type genericParam = genericDefinition.GetGenericArguments()[0];
            Assert.Throws<ArgumentException>(() => TypeExtensions.ToNamespaceQualifiedTypeString(genericParam));
        }

        [TestCase(false, typeof(int), typeof(int))]
        [TestCase(false, typeof(int), typeof(float))]
        [TestCase(false, typeof(System.InvalidOperationException), typeof(System.Exception))]
        [TestCase(false, typeof(List<float>), typeof(List<float>))]
        [TestCase(false, typeof(float[]), typeof(Array))]
        [TestCase(false, typeof(ValueTuple<float, int>), typeof(ValueTuple<>), TestName = "IsGenericTypeOf(vtuple´1 with float and int)")] // Nunit test naming gets confused. Tuples get formatted as "ValueTuple`N[...." and as (T1, T2)
        [TestCase(true, typeof(List<float>), typeof(List<>))]
        [TestCase(true, typeof(List<List<float>>), typeof(List<>))]
        [TestCase(true, typeof(List<List<float>>), typeof(List<>))]
        [TestCase(true, typeof(ValueTuple<float>), typeof(ValueTuple<>))]
        [TestCase(true, typeof(ValueTuple<float, int>), typeof(ValueTuple<,>), TestName = "IsGenericTypeOf(vtuple´2 with float and int)")] // see above
        public void IsGenericTypeOf(bool expected, Type type, Type typeOf)
        {
            if (expected)
                Assert.IsTrue(TypeExtensions.IsGenericTypeOf(type, typeOf));
            else
                Assert.IsFalse(TypeExtensions.IsGenericTypeOf(type, typeOf));
        }

        interface IInParentClass { }
        class InterfaceTestClassBase : IInParentClass
        {
        }
        interface IInterfaceTestClassBase { }
        interface IInterfaceTestClass : IInterfaceTestClassBase { }
        class InterfaceTestClass : InterfaceTestClassBase, IInterfaceTestClass
        {
        }

        [TestCase(false, typeof(int), typeof(int))]
        [TestCase(false, typeof(int), typeof(float))]
        [TestCase(false, typeof(List<int>), typeof(ICollection<float>))]
        [TestCase(false, typeof(List<int>), typeof(IList<float>))]
        [TestCase(false, typeof(System.InvalidOperationException), typeof(System.Exception))]
        [TestCase(false, typeof(System.InvalidOperationException), typeof(IReadOnlyCollection<int>))]
        [TestCase(false, typeof(System.InvalidOperationException), typeof(IEnumerable))]
        [TestCase(true, typeof(List<int>), typeof(ICollection<int>))]
        [TestCase(true, typeof(List<int>), typeof(IList<int>))]
        [TestCase(true, typeof(OrderedDictionary<int, string>), typeof(IEnumerable))]
        [TestCase(true, typeof(System.InvalidOperationException), typeof(ISerializable))]
        [TestCase(true, typeof(InterfaceTestClass), typeof(IInterfaceTestClassBase))]
        [TestCase(true, typeof(InterfaceTestClass), typeof(IInParentClass))]
        public void ImplementsInterface(bool expected, Type type, Type interfaceType)
        {
            // static reference to find tests easier
            _ = TypeExtensions.ImplementsInterface<int>(typeof(int));

            // Test generic method: ImplementsInterface<TInterface>()
            bool genericResult = (bool)(typeof(TypeExtensions).GetMethod("ImplementsInterface", new Type[] { typeof(Type) }).MakeGenericMethod(interfaceType).Invoke(obj: null, parameters: new object[] { type }));
            if (expected)
                Assert.IsTrue(genericResult);
            else
                Assert.IsFalse(genericResult);

            // Test method: ImplementsInterface(Type interfaceType)
            bool dynamicResult = (bool)(typeof(TypeExtensions).GetMethod("ImplementsInterface", new Type[] { typeof(Type), typeof(Type) }).Invoke(obj: null, parameters: new object[] { type, interfaceType }));
            if (expected)
                Assert.IsTrue(dynamicResult);
            else
                Assert.IsFalse(dynamicResult);
        }

        interface IGenericInterface<T>
        {
        }
        class ClassWithGenericInterface : IGenericInterface<int>
        {
        }
        class ClassWithParentWithGenericInterface : ClassWithGenericInterface
        {
        }

        [TestCase(new Type[]{ typeof(float) }, typeof(List<float>), typeof(IList<>))]
        [TestCase(new Type[]{ typeof(int) }, typeof(List<int>), typeof(IEnumerable<>))]
        [TestCase(new Type[]{ typeof(int), typeof(string) }, typeof(Dictionary<int, string>), typeof(IDictionary<,>))]
        [TestCase(new Type[]{ typeof(List<int>) }, typeof(List<List<int>>), typeof(IList<>))]
        [TestCase(new Type[]{ typeof(int) }, typeof(ClassWithGenericInterface), typeof(IGenericInterface<>))]
        [TestCase(new Type[]{ typeof(int) }, typeof(ClassWithParentWithGenericInterface), typeof(IGenericInterface<>))]
        public void GetGenericInterfaceTypeArguments(Type[] expected, Type type, Type genericInterfaceDefinition)
        {
            Assert.AreEqual(expected, TypeExtensions.GetGenericInterfaceTypeArguments(type, genericInterfaceDefinition));
        }

        [TestCase(false, typeof(List<float>), typeof(IGenericInterface<>))]
        [TestCase(false, typeof(List<float>), typeof(IGenericInterface<>))]
        [TestCase(true, typeof(List<float>), typeof(IList<>))]
        [TestCase(true, typeof(List<int>), typeof(IEnumerable<>))]
        [TestCase(true, typeof(Dictionary<int, string>), typeof(IDictionary<,>))]
        [TestCase(true, typeof(List<List<int>>), typeof(IList<>))]
        [TestCase(true, typeof(ClassWithGenericInterface), typeof(IGenericInterface<>))]
        [TestCase(true, typeof(ClassWithParentWithGenericInterface), typeof(IGenericInterface<>))]
        public void ImplementsGenericInterface(bool expected, Type type, Type genericInterfaceDefinition)
        {
            if (expected)
                Assert.IsTrue(TypeExtensions.ImplementsGenericInterface(type, genericInterfaceDefinition));
            else
                Assert.IsFalse(TypeExtensions.ImplementsGenericInterface(type, genericInterfaceDefinition));
        }

        [TestCase(typeof(IList<float>), typeof(List<float>), typeof(IList<>))]
        [TestCase(typeof(IEnumerable<int>), typeof(List<int>), typeof(IEnumerable<>))]
        [TestCase(typeof(IDictionary<int, string>), typeof(Dictionary<int, string>), typeof(IDictionary<,>))]
        [TestCase(typeof(IList<List<int>>), typeof(List<List<int>>), typeof(IList<>))]
        [TestCase(typeof(IGenericInterface<int>), typeof(ClassWithGenericInterface), typeof(IGenericInterface<>))]
        [TestCase(typeof(IGenericInterface<int>), typeof(ClassWithParentWithGenericInterface), typeof(IGenericInterface<>))]
        public void GetGenericInterface(Type expected, Type type, Type genericInterfaceDefinition)
        {
            Assert.AreEqual(expected, TypeExtensions.GetGenericInterface(type, genericInterfaceDefinition));
        }

        public class ClassWithParentIntList : List<int>{ }
        public class ClassWithParentList<T> : List<T>{ }
        public class ClassWithParentIntStringDictionary : Dictionary<int, string>{ }
        public class ClassWithParentDictionary<TKey, TValue> : Dictionary<TKey, TValue>{ }
        public class ClassWithParentWithParentIntStringDictionary : ClassWithParentIntStringDictionary{ }

        [TestCase(new Type[]{ typeof(float) }, typeof(List<float>), typeof(List<>))]
        [TestCase(new Type[]{ typeof(int) }, typeof(List<int>), typeof(List<>))]
        [TestCase(new Type[]{ typeof(int) }, typeof(ClassWithParentIntList), typeof(List<>))]
        [TestCase(new Type[]{ typeof(int) }, typeof(ClassWithParentList<int>), typeof(List<>))]
        [TestCase(new Type[]{ typeof(int), typeof(string) }, typeof(Dictionary<int, string>), typeof(Dictionary<,>))]
        [TestCase(new Type[]{ typeof(float), typeof(double) }, typeof(Dictionary<float, double>), typeof(Dictionary<,>))]
        [TestCase(new Type[]{ typeof(int), typeof(string) }, typeof(ClassWithParentIntStringDictionary), typeof(Dictionary<,>))]
        [TestCase(new Type[]{ typeof(int), typeof(string) }, typeof(ClassWithParentDictionary<int, string>), typeof(Dictionary<,>))]
        [TestCase(new Type[]{ typeof(int), typeof(string) }, typeof(ClassWithParentWithParentIntStringDictionary), typeof(Dictionary<,>))]
        public void GetGenericAncestorTypeArguments(Type[] expected, Type type, Type genericAncestorDefinition)
        {
            Assert.AreEqual(expected, TypeExtensions.GetGenericAncestorTypeArguments(type, genericAncestorDefinition));
        }

        [TestCase(false, typeof(List<float>), typeof(Dictionary<,>))]
        [TestCase(false, typeof(ClassWithParentIntList), typeof(Dictionary<,>))]
        [TestCase(false, typeof(ClassWithParentIntStringDictionary), typeof(List<>))]
        [TestCase(false, typeof(ClassWithParentDictionary<float, double>), typeof(List<>))]
        [TestCase(true, typeof(List<float>), typeof(List<>))]
        [TestCase(true, typeof(List<int>), typeof(List<>))]
        [TestCase(true, typeof(ClassWithParentIntList), typeof(List<>))]
        [TestCase(true, typeof(ClassWithParentList<int>), typeof(List<>))]
        [TestCase(true, typeof(Dictionary<int, string>), typeof(Dictionary<,>))]
        [TestCase(true, typeof(Dictionary<float, double>), typeof(Dictionary<,>))]
        [TestCase(true, typeof(ClassWithParentIntStringDictionary), typeof(Dictionary<,>))]
        [TestCase(true, typeof(ClassWithParentDictionary<int, string>), typeof(Dictionary<,>))]
        [TestCase(true, typeof(ClassWithParentWithParentIntStringDictionary), typeof(Dictionary<,>))]
        public void HasGenericAncestor(bool expected, Type type, Type genericAncestorDefinition)
        {
            Assert.AreEqual(expected, TypeExtensions.HasGenericAncestor(type, genericAncestorDefinition));
        }

        [TestCase(typeof(List<float>), typeof(List<float>), typeof(List<>))]
        [TestCase(typeof(List<int>), typeof(List<int>), typeof(List<>))]
        [TestCase(typeof(List<int>), typeof(ClassWithParentIntList), typeof(List<>))]
        [TestCase(typeof(List<int>), typeof(ClassWithParentList<int>), typeof(List<>))]
        [TestCase(typeof(Dictionary<int, string>), typeof(Dictionary<int, string>), typeof(Dictionary<,>))]
        [TestCase(typeof(Dictionary<float, double>), typeof(Dictionary<float, double>), typeof(Dictionary<,>))]
        [TestCase(typeof(Dictionary<int, string>), typeof(ClassWithParentIntStringDictionary), typeof(Dictionary<,>))]
        [TestCase(typeof(Dictionary<int, string>), typeof(ClassWithParentDictionary<int, string>), typeof(Dictionary<,>))]
        [TestCase(typeof(Dictionary<int, string>), typeof(ClassWithParentWithParentIntStringDictionary), typeof(Dictionary<,>))]
        public void GetGenericAncestor(Type expected, Type type, Type genericAncestorDefinition)
        {
            Assert.AreEqual(expected, TypeExtensions.GetGenericAncestor(type, genericAncestorDefinition));
            Assert.AreEqual(expected, TypeExtensions.TryGetGenericAncestor(type, genericAncestorDefinition));
        }

        [TestCase(typeof(List<float>), typeof(HashSet<>))]
        [TestCase(typeof(List<int>), typeof(HashSet<>))]
        [TestCase(typeof(ClassWithParentIntList), typeof(HashSet<>))]
        [TestCase(typeof(ClassWithParentList<int>), typeof(HashSet<>))]
        [TestCase(typeof(Dictionary<int, string>), typeof(OrderedDictionary<,>))]
        [TestCase(typeof(Dictionary<float, double>), typeof(OrderedDictionary<,>))]
        [TestCase(typeof(ClassWithParentIntStringDictionary), typeof(OrderedDictionary<,>))]
        [TestCase(typeof(ClassWithParentDictionary<int, string>), typeof(OrderedDictionary<,>))]
        [TestCase(typeof(ClassWithParentWithParentIntStringDictionary), typeof(OrderedDictionary<,>))]
        public void GetGenericAncestorNonMatch(Type type, Type genericAncestorDefinition)
        {
            Assert.AreEqual(null, TypeExtensions.TryGetGenericAncestor(type, genericAncestorDefinition));
            Assert.Throws<InvalidOperationException>(() => TypeExtensions.GetGenericAncestor(type, genericAncestorDefinition));
        }

        class InheritanceTestClass { }
        class InheritanceTestSubClass : InheritanceTestClass { }
        class InheritanceTestSubSubClass : InheritanceTestSubClass
        {
            public override string ToString() => "Zyzzy";
        }

        [TestCase(false, typeof(List<float>), typeof(IInterfaceTestClassBase))]
        [TestCase(false, typeof(object), typeof(IInterfaceTestClassBase))]
        [TestCase(true, typeof(List<float>), typeof(IList<float>))]
        [TestCase(true, typeof(InterfaceTestClass), typeof(IInterfaceTestClassBase))]
        [TestCase(true, typeof(InterfaceTestClass), typeof(IInParentClass))]
        [TestCase(true, typeof(System.InvalidOperationException), typeof(System.Exception))]
        [TestCase(true, typeof(NestingClass), typeof(object))]
        [TestCase(true, typeof(IInterfaceTestClass), typeof(IInterfaceTestClassBase))]
        [TestCase(true, typeof(InheritanceTestSubSubClass), typeof(InheritanceTestSubClass))]
        [TestCase(true, typeof(InheritanceTestSubSubClass), typeof(InheritanceTestClass))]
        public void IsDerivedFrom(bool expected, Type type, Type baseType)
        {
            // static reference to find tests easier
            _ = TypeExtensions.IsDerivedFrom<object>(typeof(object));

            bool result = (bool)(typeof(TypeExtensions).GetMethod("IsDerivedFrom").MakeGenericMethod(baseType).Invoke(obj: null, parameters: new object[] { type }));
            if (expected)
                Assert.IsTrue(result);
            else
                Assert.IsFalse(result);
        }

        public class GenericBase<T>{ }
        public class NonGenericLeaf : GenericBase<int>{ }
        public class GenericLeafA<T> : GenericBase<int>{ }
        public class GenericLeafB<T> : GenericBase<T>{ }
        public struct TestStruct{ }

        [TestCase(typeof(object), typeof(object))]
        [TestCase(typeof(ValueType), typeof(ValueType), typeof(object))]
        [TestCase(typeof(InheritanceTestClass), typeof(InheritanceTestClass), typeof(object))]
        [TestCase(typeof(InheritanceTestSubClass), typeof(InheritanceTestSubClass), typeof(InheritanceTestClass), typeof(object))]
        [TestCase(typeof(InheritanceTestSubSubClass), typeof(InheritanceTestSubSubClass), typeof(InheritanceTestSubClass), typeof(InheritanceTestClass), typeof(object))]
        [TestCase(typeof(InterfaceTestClass), typeof(InterfaceTestClass), typeof(InterfaceTestClassBase), typeof(object))]
        [TestCase(typeof(IInterfaceTestClassBase), typeof(IInterfaceTestClassBase))]
        [TestCase(typeof(GenericBase<>), typeof(GenericBase<>), typeof(object))]
        [TestCase(typeof(GenericBase<int>), typeof(GenericBase<int>), typeof(object))]
        [TestCase(typeof(NonGenericLeaf), typeof(NonGenericLeaf), typeof(GenericBase<int>), typeof(object))]
        [TestCase(typeof(GenericLeafA<>), typeof(GenericLeafA<>), typeof(GenericBase<int>), typeof(object))]
        [TestCase(typeof(GenericLeafA<float>), typeof(GenericLeafA<float>), typeof(GenericBase<int>), typeof(object))]
        [TestCase(typeof(GenericLeafB<float>), typeof(GenericLeafB<float>), typeof(GenericBase<float>), typeof(object))]
        [TestCase(typeof(TestStruct), typeof(TestStruct), typeof(ValueType), typeof(object))]
        public void EnumerateTypeAndBases(Type type, params Type[] expected)
        {
            Assert.AreEqual(expected, TypeExtensions.EnumerateTypeAndBases(type));
        }

        class StaticFieldsNoFieldsClass
        {
        }

        class StaticFieldHasFieldsClass
        {
            public static readonly int s_1 = 1;
            public static readonly float s_2 = 2.5f;
            public static readonly string s_3 = "ASD";
            public static readonly DateTime s_4 = new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            public static readonly InheritanceTestSubSubClass s_5 = new InheritanceTestSubSubClass();
        }
        class StaticFieldSubClass : StaticFieldHasFieldsClass
        {
            public static readonly string t_1 = "BAX";
            public static readonly int t_2 = 3;
        }

        [TestCase("", typeof(int), typeof(StaticFieldsNoFieldsClass))]
        [TestCase("1", typeof(int), typeof(StaticFieldHasFieldsClass))]
        [TestCase("2.5", typeof(float), typeof(StaticFieldHasFieldsClass))]
        [TestCase("ASD", typeof(string), typeof(StaticFieldHasFieldsClass))]
        [TestCase("01/01/2000 12:00:00", typeof(DateTime), typeof(StaticFieldHasFieldsClass))]
        [TestCase("Zyzzy", typeof(InheritanceTestSubSubClass), typeof(StaticFieldHasFieldsClass))]
        [TestCase("Zyzzy", typeof(InheritanceTestSubClass), typeof(StaticFieldHasFieldsClass))]
        [TestCase("Zyzzy", typeof(InheritanceTestClass), typeof(StaticFieldHasFieldsClass))]
        [TestCase("1;2.5;ASD;01/01/2000 12:00:00;Zyzzy", typeof(object), typeof(StaticFieldHasFieldsClass))]
        [TestCase("BAX", typeof(string), typeof(StaticFieldSubClass))]
        [TestCase("3", typeof(int), typeof(StaticFieldSubClass))]
        [TestCase("", typeof(float), typeof(StaticFieldSubClass))]
        [TestCase("BAX;3", typeof(object), typeof(StaticFieldSubClass))]
        public void GetStaticFieldsOfType(string expected, Type fieldType, Type type)
        {
            // static reference to find tests easier
            _ = TypeExtensions.GetStaticFieldsOfType<object>(typeof(object));

            IList fields = (IList)(typeof(TypeExtensions).GetMethod("GetStaticFieldsOfType").MakeGenericMethod(fieldType).Invoke(obj: null, parameters: new object[] { type }));
            string result = "";
            foreach (object o in fields)
            {
                if (!string.IsNullOrEmpty(result))
                    result += ";";

                result += Util.ObjectToStringInvariant(o);
            }
            Assert.AreEqual(expected, result);
        }

        public class PublicType
        {
            public static Type PrivateNestedType = typeof(PublicType.PrivateNested);
            private class PrivateNested { }
            public class PublicNested { }
        }
        internal class InternalType
        {
        }
        private class PrivateType
        {
            public class PublicNested { }
        }
        public struct PublicStruct
        {
        }
        private struct PrivateStruct
        {
        }
        class PlaceHolderForPublicTypePrivateNestedType { }

        public class PublicGType<T>
        {
        }
        internal class InternalGType<T>
        {
        }
        private class PrivateGType<T>
        {
        }

        [TestCase(true, typeof(object))]
        [TestCase(true, typeof(string))]
        [TestCase(true, typeof(List<string>))]
        [TestCase(true, typeof(PublicType))]
        [TestCase(true, typeof(PublicType.PublicNested))]
        [TestCase(true, typeof(PublicStruct))]
        [TestCase(true, typeof(PublicStruct?))]
        [TestCase(true, typeof(PublicStruct[]))]
        [TestCase(true, typeof(Dictionary<PublicType, PublicStruct?>))]
        [TestCase(true, typeof(PublicGType<int>))]
        [TestCase(true, typeof(PublicGType<>))]
        [TestCase(false, typeof(PrivateType))]
        [TestCase(false, typeof(InternalType))]
        [TestCase(false, typeof(List<InternalType>))]
        [TestCase(false, typeof(Dictionary<PublicType, PrivateType>))]
        [TestCase(false, typeof(Dictionary<PublicType, PrivateType[]>))]
        [TestCase(false, typeof(Dictionary<PublicType, PrivateStruct>))]
        [TestCase(false, typeof(Dictionary<PublicType, PrivateStruct?>))]
        [TestCase(false, typeof(ValueTuple<PublicType, PrivateType.PublicNested>))]
        [TestCase(false, typeof(PlaceHolderForPublicTypePrivateNestedType))]
        [TestCase(false, typeof(PrivateStruct))]
        [TestCase(false, typeof(PrivateStruct?))]
        [TestCase(false, typeof(PrivateStruct[]))]
        [TestCase(false, typeof(InternalGType<int>))]
        [TestCase(false, typeof(InternalGType<>))]
        [TestCase(false, typeof(PrivateGType<int>))]
        [TestCase(false, typeof(PrivateGType<>))]
        [TestCase(false, typeof(PublicGType<PrivateType>))]
        [TestCase(false, typeof(InternalGType<PublicType.PublicNested>))]
        [TestCase(false, typeof(PrivateGType<PublicType>))]
        public void GetRecursiveVisibility(bool expected, Type type)
        {
            if (type == typeof(PlaceHolderForPublicTypePrivateNestedType))
                type = PublicType.PrivateNestedType;

            if (expected)
                Assert.IsTrue(TypeExtensions.GetRecursiveVisibility(type));
            else
                Assert.IsFalse(TypeExtensions.GetRecursiveVisibility(type));
        }

        public class CustomClassType{ }
        public interface CustomInterfaceType{ }
        public struct CustomStructType{ }

        // Reference types - can be null
        [TestCase(true, typeof(object))]
        [TestCase(true, typeof(string))]
        [TestCase(true, typeof(int[]))]
        [TestCase(true, typeof(IList))]
        [TestCase(true, typeof(IEquatable<int>))]
        [TestCase(true, typeof(List<string>))]
        [TestCase(true, typeof(ValueType))]
        [TestCase(true, typeof(CustomInterfaceType))]
        [TestCase(true, typeof(CustomClassType))]
        // Value types - cannot be null
        [TestCase(false, typeof(int))]
        [TestCase(false, typeof(Guid))]
        [TestCase(false, typeof(DateTime))]
        [TestCase(false, typeof(CustomStructType))]
        // Nullables of value types - can be null
        [TestCase(true, typeof(int?))]
        [TestCase(true, typeof(Guid?))]
        [TestCase(true, typeof(DateTime?))]
        [TestCase(true, typeof(CustomStructType?))]
        public void CanBeNull(bool expected, Type type)
        {
            if (expected)
                Assert.IsTrue(TypeExtensions.CanBeNull(type));
            else
                Assert.IsFalse(TypeExtensions.CanBeNull(type));
        }

        [TestCase("", typeof(object))]
        [TestCase("", typeof(string))]
        [TestCase("", typeof(List<string>))]
        [TestCase("", typeof(PublicType))]
        [TestCase("", typeof(PublicType.PublicNested))]
        [TestCase("", typeof(PublicStruct))]
        [TestCase("", typeof(PublicStruct?))]
        [TestCase("", typeof(PublicStruct[]))]
        [TestCase("", typeof(Dictionary<PublicType, PublicStruct?>))]
        [TestCase("", typeof(PublicGType<int>))]
        [TestCase("", typeof(PublicGType<>))]
        [TestCase("TypeExtensionsTests.PrivateType", typeof(PrivateType))]
        [TestCase("TypeExtensionsTests.PrivateType", typeof(List<PrivateType>))]
        [TestCase("TypeExtensionsTests.PrivateType", typeof(Dictionary<PublicType, PrivateType>))]
        [TestCase("TypeExtensionsTests.PrivateType", typeof(Dictionary<PublicType, PrivateType[]>))]
        [TestCase("TypeExtensionsTests.PrivateStruct", typeof(Dictionary<PublicType, PrivateStruct>))]
        [TestCase("TypeExtensionsTests.PrivateStruct", typeof(Dictionary<PublicType, PrivateStruct?>))]
        [TestCase("TypeExtensionsTests.PrivateType", typeof(ValueTuple<PublicType, PrivateType.PublicNested>))]
        [TestCase("TypeExtensionsTests.PublicType.PrivateNested", typeof(PlaceHolderForPublicTypePrivateNestedType))]
        [TestCase("TypeExtensionsTests.PrivateStruct", typeof(PrivateStruct))]
        [TestCase("TypeExtensionsTests.PrivateStruct", typeof(PrivateStruct?))]
        [TestCase("TypeExtensionsTests.PrivateStruct", typeof(PrivateStruct[]))]
        [TestCase("TypeExtensionsTests.PrivateGType<Int32>", typeof(PrivateGType<int>))]
        [TestCase("TypeExtensionsTests.PrivateGType<>", typeof(PrivateGType<>))]
        [TestCase("TypeExtensionsTests.PrivateType", typeof(PublicGType<PrivateType>))]
        [TestCase("TypeExtensionsTests.PrivateGType<TypeExtensionsTests.PublicType>", typeof(PrivateGType<PublicType>))]
        [TestCase("", typeof(InternalType))] // internals are public in IL
        [TestCase("", typeof(List<InternalType>))]
        [TestCase("", typeof(InternalGType<int>))]
        [TestCase("", typeof(InternalGType<>))]
        [TestCase("", typeof(InternalGType<PublicType.PublicNested>))]
        public void FindNonPublicComponent(string expected, Type type)
        {
            if (type == typeof(PlaceHolderForPublicTypePrivateNestedType))
                type = PublicType.PrivateNestedType;

            Type got = TypeExtensions.FindNonPublicComponent(type);
            if (got == null)
                Assert.IsEmpty(expected);
            else
                Assert.AreEqual(expected, got.ToGenericTypeString());
        }

        public class ClassWithAProperty
        {
            public int PropertyWithPublicSetter { get; set; } = -1;
            public int PropertyWithPrivateSetter { get; private set; } = -1;

            public int _PrivateProperty = -1;
            private int PrivateProperty{ get => _PrivateProperty; set => _PrivateProperty = value; }

            public void SetPropertyWithPrivateSetter(int value)
            {
                PropertyWithPrivateSetter = value;
            }
            public void SetPrivateProperty(int value)
            {
                _PrivateProperty = value;
            }
        }
        public class SubClassWithAProperty : ClassWithAProperty
        {
        }

        public interface IInterfaceWithAPropery
        {
            int GetSetProperty { get; set; }
            int SetProperty { set; }
            int GetProperty { get; }
        }
        public class ClassImplementingIInterfaceWithAPropery : IInterfaceWithAPropery
        {
            public int GetSetProperty { get; set; }

            public int _SetProperty = -1;
            public int _SetPropertyAddedGetterValue = -1;
            public int SetProperty { set => _SetProperty = value; get { return _SetPropertyAddedGetterValue; } }

            public int _GetProperty = -1;
            public int _GetPropertyAddedSetterValue = -1;
            public int GetProperty { get { return _GetProperty; } set { _GetPropertyAddedSetterValue = value; } }
        }

        public class ClassWithHiddenPropertyBase
        {
            public int GetSetProperty { get; set; } = 1;
        }

        public class ClassWithHiddenProperty : ClassWithHiddenPropertyBase
        {
            public new string GetSetProperty { get; set; } = "init";
        }

        [Test]
        public void GetSetMethodOnDeclaringType()
        {
            {
                var mi = TypeExtensions.GetSetMethodOnDeclaringType(typeof(ClassWithAProperty).GetProperty("PropertyWithPublicSetter"));
                ClassWithAProperty obj = new ClassWithAProperty();
                mi.Invoke(obj, parameters: new object[] { (int)2 });
                Assert.AreEqual(2, obj.PropertyWithPublicSetter);
            }
            {
                var mi = TypeExtensions.GetSetMethodOnDeclaringType(typeof(ClassWithAProperty).GetProperty("PropertyWithPrivateSetter"));
                ClassWithAProperty obj = new ClassWithAProperty();
                mi.Invoke(obj, parameters: new object[] { (int)4 });
                Assert.AreEqual(4, obj.PropertyWithPrivateSetter);
            }
            {
                var mi = TypeExtensions.GetSetMethodOnDeclaringType(typeof(ClassWithAProperty).GetProperty("PrivateProperty", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic));
                ClassWithAProperty obj = new ClassWithAProperty();
                mi.Invoke(obj, parameters: new object[] { (int)8 });
                Assert.AreEqual(8, obj._PrivateProperty);
            }

            {
                var mi = TypeExtensions.GetSetMethodOnDeclaringType(typeof(SubClassWithAProperty).GetProperty("PropertyWithPublicSetter"));
                var obj = new SubClassWithAProperty();
                mi.Invoke(obj, parameters: new object[] { (int)2 });
                Assert.AreEqual(2, obj.PropertyWithPublicSetter);
            }
            {
                var mi = TypeExtensions.GetSetMethodOnDeclaringType(typeof(SubClassWithAProperty).GetProperty("PropertyWithPrivateSetter"));
                var obj = new SubClassWithAProperty();
                mi.Invoke(obj, parameters: new object[] { (int)4 });
                Assert.AreEqual(4, obj.PropertyWithPrivateSetter);
            }
            {
                // \note: need to look up in parent class
                var mi = TypeExtensions.GetSetMethodOnDeclaringType(typeof(ClassWithAProperty).GetProperty("PrivateProperty", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic));
                var obj = new SubClassWithAProperty();
                mi.Invoke(obj, parameters: new object[] { (int)8 });
                Assert.AreEqual(8, obj._PrivateProperty);
            }

            {
                var mi = TypeExtensions.GetSetMethodOnDeclaringType(typeof(IInterfaceWithAPropery).GetProperty("GetSetProperty"));
                var obj = new ClassImplementingIInterfaceWithAPropery();
                mi.Invoke(obj, parameters: new object[] { (int)2 });
                Assert.AreEqual(2, obj.GetSetProperty);
            }
            {
                var mi = TypeExtensions.GetSetMethodOnDeclaringType(typeof(ClassImplementingIInterfaceWithAPropery).GetProperty("GetSetProperty"));
                var obj = new ClassImplementingIInterfaceWithAPropery();
                mi.Invoke(obj, parameters: new object[] { (int)2 });
                Assert.AreEqual(2, obj.GetSetProperty);
            }

            {
                var mi = TypeExtensions.GetSetMethodOnDeclaringType(typeof(IInterfaceWithAPropery).GetProperty("SetProperty"));
                var obj = new ClassImplementingIInterfaceWithAPropery();
                mi.Invoke(obj, parameters: new object[] { (int)3 });
                Assert.AreEqual(3, obj._SetProperty);
            }
            {
                var mi = TypeExtensions.GetSetMethodOnDeclaringType(typeof(ClassImplementingIInterfaceWithAPropery).GetProperty("SetProperty"));
                var obj = new ClassImplementingIInterfaceWithAPropery();
                mi.Invoke(obj, parameters: new object[] { (int)3 });
                Assert.AreEqual(3, obj._SetProperty);
            }

            {
                var mi = TypeExtensions.GetSetMethodOnDeclaringType(typeof(IInterfaceWithAPropery).GetProperty("GetProperty"));
                Assert.IsNull(mi);
            }
            {
                var mi = TypeExtensions.GetSetMethodOnDeclaringType(typeof(ClassImplementingIInterfaceWithAPropery).GetProperty("GetProperty"));
                var obj = new ClassImplementingIInterfaceWithAPropery();
                mi.Invoke(obj, parameters: new object[] { (int)4 });
                Assert.AreEqual(4, obj._GetPropertyAddedSetterValue);
            }

            {
                var mi = TypeExtensions.GetSetMethodOnDeclaringType(typeof(ClassWithHiddenProperty).GetProperty("GetSetProperty", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly));
                var obj = new ClassWithHiddenProperty();
                mi.Invoke(obj, parameters: new object[] { "2" });
                Assert.AreEqual("2", obj.GetSetProperty);
            }
            {
                var mi = TypeExtensions.GetSetMethodOnDeclaringType(typeof(ClassWithHiddenPropertyBase).GetProperty("GetSetProperty", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly));
                var obj = new ClassWithHiddenProperty();
                mi.Invoke(obj, parameters: new object[] { (int)2 });
                Assert.AreEqual(2, ((ClassWithHiddenPropertyBase)obj).GetSetProperty);
            }
        }

        [Test]
        public void GetGetMethodOnDeclaringType()
        {
            {
                var mi = TypeExtensions.GetGetMethodOnDeclaringType(typeof(ClassWithAProperty).GetProperty("PropertyWithPublicSetter"));
                ClassWithAProperty obj = new ClassWithAProperty();
                obj.PropertyWithPublicSetter = 2;
                int result = (int)(mi.Invoke(obj, parameters: null));
                Assert.AreEqual(2, result);
            }
            {
                var mi = TypeExtensions.GetGetMethodOnDeclaringType(typeof(ClassWithAProperty).GetProperty("PropertyWithPrivateSetter"));
                ClassWithAProperty obj = new ClassWithAProperty();
                obj.SetPropertyWithPrivateSetter(4);
                int result = (int)(mi.Invoke(obj, parameters: null));
                Assert.AreEqual(4, result);
            }
            {
                var mi = TypeExtensions.GetGetMethodOnDeclaringType(typeof(ClassWithAProperty).GetProperty("PrivateProperty", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic));
                ClassWithAProperty obj = new ClassWithAProperty();
                obj.SetPrivateProperty(8);
                int result = (int)(mi.Invoke(obj, parameters: null));
                Assert.AreEqual(8, result);
            }

            {
                var mi = TypeExtensions.GetGetMethodOnDeclaringType(typeof(SubClassWithAProperty).GetProperty("PropertyWithPublicSetter"));
                var obj = new SubClassWithAProperty();
                obj.PropertyWithPublicSetter = 2;
                int result = (int)(mi.Invoke(obj, parameters: null));
                Assert.AreEqual(2, result);
            }
            {
                var mi = TypeExtensions.GetGetMethodOnDeclaringType(typeof(SubClassWithAProperty).GetProperty("PropertyWithPrivateSetter"));
                var obj = new SubClassWithAProperty();
                obj.SetPropertyWithPrivateSetter(4);
                int result = (int)(mi.Invoke(obj, parameters: null));
                Assert.AreEqual(4, result);
            }
            {
                // \note: need to look up in parent class
                var mi = TypeExtensions.GetGetMethodOnDeclaringType(typeof(ClassWithAProperty).GetProperty("PrivateProperty", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic));
                var obj = new SubClassWithAProperty();
                obj.SetPrivateProperty(8);
                int result = (int)(mi.Invoke(obj, parameters: null));
                Assert.AreEqual(8, result);
            }

            {
                var mi = TypeExtensions.GetGetMethodOnDeclaringType(typeof(IInterfaceWithAPropery).GetProperty("GetSetProperty"));
                var obj = new ClassImplementingIInterfaceWithAPropery();
                obj.GetSetProperty = 2;
                int result = (int)(mi.Invoke(obj, parameters: null));
                Assert.AreEqual(2, result);
            }
            {
                var mi = TypeExtensions.GetGetMethodOnDeclaringType(typeof(ClassImplementingIInterfaceWithAPropery).GetProperty("GetSetProperty"));
                var obj = new ClassImplementingIInterfaceWithAPropery();
                obj.GetSetProperty = 2;
                int result = (int)(mi.Invoke(obj, parameters: null));
                Assert.AreEqual(2, result);
            }

            {
                var mi = TypeExtensions.GetGetMethodOnDeclaringType(typeof(IInterfaceWithAPropery).GetProperty("SetProperty"));
                Assert.IsNull(mi);
            }
            {
                var mi = TypeExtensions.GetGetMethodOnDeclaringType(typeof(ClassImplementingIInterfaceWithAPropery).GetProperty("SetProperty"));
                var obj = new ClassImplementingIInterfaceWithAPropery();
                obj._SetPropertyAddedGetterValue = 3;
                int result = (int)(mi.Invoke(obj, parameters: null));
                Assert.AreEqual(3, result);
            }

            {
                var mi = TypeExtensions.GetGetMethodOnDeclaringType(typeof(IInterfaceWithAPropery).GetProperty("GetProperty"));
                var obj = new ClassImplementingIInterfaceWithAPropery();
                obj._GetProperty = 4;
                int result = (int)(mi.Invoke(obj, parameters: null));
                Assert.AreEqual(4, result);
            }
            {
                var mi = TypeExtensions.GetGetMethodOnDeclaringType(typeof(ClassImplementingIInterfaceWithAPropery).GetProperty("GetProperty"));
                var obj = new ClassImplementingIInterfaceWithAPropery();
                obj._GetProperty = 4;
                int result = (int)(mi.Invoke(obj, parameters: null));
                Assert.AreEqual(4, result);
            }

            {
                var mi = TypeExtensions.GetGetMethodOnDeclaringType(typeof(ClassWithHiddenProperty).GetProperty("GetSetProperty", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly));
                var obj = new ClassWithHiddenProperty();
                obj.GetSetProperty = "4";
                string result = (string)(mi.Invoke(obj, parameters: null));
                Assert.AreEqual("4", result);
            }
            {
                var mi = TypeExtensions.GetGetMethodOnDeclaringType(typeof(ClassWithHiddenPropertyBase).GetProperty("GetSetProperty", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly));
                var obj = new ClassWithHiddenProperty();
                ((ClassWithHiddenPropertyBase)obj).GetSetProperty = 4;
                int result = (int)(mi.Invoke(obj, parameters: null));
                Assert.AreEqual(4, result);
            }
        }

        [Test]
        public void GetSetValueOnDeclaringType()
        {
            RunPropertyTestsWithGetSetValueOnDeclaringType(TypeExtensions.GetSetValueOnDeclaringType);

            // dummy call to detect coverage statically
            _ = TypeExtensions.GetSetValueOnDeclaringType(typeof(ClassWithAProperty).GetProperty("PropertyWithPublicSetter"));
        }

        [Test]
        public void GetGetValueOnDeclaringType()
        {
            RunPropertyTestsWithGetGetValueOnDeclaringType(TypeExtensions.GetGetValueOnDeclaringType);

            // dummy call to detect coverage statically
            _ = TypeExtensions.GetGetValueOnDeclaringType(typeof(ClassWithAProperty).GetProperty("PropertyWithPublicSetter"));
        }

        void RunPropertyTestsWithGetSetValueOnDeclaringType(Func<System.Reflection.PropertyInfo, Action<object,object>> setFuncGetFn)
        {
            {
                var setter = setFuncGetFn(typeof(ClassWithAProperty).GetProperty("PropertyWithPublicSetter"));
                ClassWithAProperty obj = new ClassWithAProperty();
                setter(obj, (int)2 );
                Assert.AreEqual(2, obj.PropertyWithPublicSetter);
            }
            {
                var setter = setFuncGetFn(typeof(ClassWithAProperty).GetProperty("PropertyWithPrivateSetter"));
                ClassWithAProperty obj = new ClassWithAProperty();
                setter(obj, (int)4 );
                Assert.AreEqual(4, obj.PropertyWithPrivateSetter);
            }
            {
                var setter = setFuncGetFn(typeof(ClassWithAProperty).GetProperty("PrivateProperty", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic));
                ClassWithAProperty obj = new ClassWithAProperty();
                setter(obj, (int)8 );
                Assert.AreEqual(8, obj._PrivateProperty);
            }

            {
                var setter = setFuncGetFn(typeof(SubClassWithAProperty).GetProperty("PropertyWithPublicSetter"));
                var obj = new SubClassWithAProperty();
                setter(obj, (int)2 );
                Assert.AreEqual(2, obj.PropertyWithPublicSetter);
            }
            {
                var setter = setFuncGetFn(typeof(SubClassWithAProperty).GetProperty("PropertyWithPrivateSetter"));
                var obj = new SubClassWithAProperty();
                setter(obj, (int)4 );
                Assert.AreEqual(4, obj.PropertyWithPrivateSetter);
            }
            {
                // \note: need to look up in parent class
                var setter = setFuncGetFn(typeof(ClassWithAProperty).GetProperty("PrivateProperty", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic));
                var obj = new SubClassWithAProperty();
                setter(obj, (int)8 );
                Assert.AreEqual(8, obj._PrivateProperty);
            }

            {
                var setter = setFuncGetFn(typeof(IInterfaceWithAPropery).GetProperty("GetSetProperty"));
                var obj = new ClassImplementingIInterfaceWithAPropery();
                setter(obj, (int)2);
                Assert.AreEqual(2, obj.GetSetProperty);
            }
            {
                var setter = setFuncGetFn(typeof(ClassImplementingIInterfaceWithAPropery).GetProperty("GetSetProperty"));
                var obj = new ClassImplementingIInterfaceWithAPropery();
                setter(obj, (int)2);
                Assert.AreEqual(2, obj.GetSetProperty);
            }

            {
                var setter = setFuncGetFn(typeof(IInterfaceWithAPropery).GetProperty("SetProperty"));
                var obj = new ClassImplementingIInterfaceWithAPropery();
                setter(obj, (int)3);
                Assert.AreEqual(3, obj._SetProperty);
            }
            {
                var setter = setFuncGetFn(typeof(ClassImplementingIInterfaceWithAPropery).GetProperty("SetProperty"));
                var obj = new ClassImplementingIInterfaceWithAPropery();
                setter(obj, (int)3);
                Assert.AreEqual(3, obj._SetProperty);
            }

            {
                var setter = setFuncGetFn(typeof(IInterfaceWithAPropery).GetProperty("GetProperty"));
                Assert.IsNull(setter);
            }
            {
                var setter = setFuncGetFn(typeof(ClassImplementingIInterfaceWithAPropery).GetProperty("GetProperty"));
                var obj = new ClassImplementingIInterfaceWithAPropery();
                setter(obj, (int)4);
                Assert.AreEqual(4, obj._GetPropertyAddedSetterValue);
            }

            {
                var setter = setFuncGetFn(typeof(ClassWithHiddenProperty).GetProperty("GetSetProperty", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly));
                var obj = new ClassWithHiddenProperty();
                setter(obj, "2");
                Assert.AreEqual("2", obj.GetSetProperty);
            }
            {
                var setter = setFuncGetFn(typeof(ClassWithHiddenPropertyBase).GetProperty("GetSetProperty", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly));
                var obj = new ClassWithHiddenProperty();
                setter(obj, (int)2);
                Assert.AreEqual(2, ((ClassWithHiddenPropertyBase)obj).GetSetProperty);
            }
        }

        void RunPropertyTestsWithGetGetValueOnDeclaringType(Func<System.Reflection.PropertyInfo, Func<object,object>> getFuncGetFn)
        {
            {
                var getter = getFuncGetFn(typeof(ClassWithAProperty).GetProperty("PropertyWithPublicSetter"));
                ClassWithAProperty obj = new ClassWithAProperty();
                obj.PropertyWithPublicSetter = 2;
                int result = (int)getter(obj);
                Assert.AreEqual(2, result);
            }
            {
                var getter = getFuncGetFn(typeof(ClassWithAProperty).GetProperty("PropertyWithPrivateSetter"));
                ClassWithAProperty obj = new ClassWithAProperty();
                obj.SetPropertyWithPrivateSetter(4);
                int result = (int)getter(obj);
                Assert.AreEqual(4, result);
            }
            {
                var getter = getFuncGetFn(typeof(ClassWithAProperty).GetProperty("PrivateProperty", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic));
                ClassWithAProperty obj = new ClassWithAProperty();
                obj.SetPrivateProperty(8);
                int result = (int)getter(obj);
                Assert.AreEqual(8, result);
            }

            {
                var getter = getFuncGetFn(typeof(SubClassWithAProperty).GetProperty("PropertyWithPublicSetter"));
                var obj = new SubClassWithAProperty();
                obj.PropertyWithPublicSetter = 2;
                int result = (int)getter(obj);
                Assert.AreEqual(2, result);
            }
            {
                var getter = getFuncGetFn(typeof(SubClassWithAProperty).GetProperty("PropertyWithPrivateSetter"));
                var obj = new SubClassWithAProperty();
                obj.SetPropertyWithPrivateSetter(4);
                int result = (int)getter(obj);
                Assert.AreEqual(4, result);
            }
            {
                // \note: need to look up in parent class
                var getter = getFuncGetFn(typeof(ClassWithAProperty).GetProperty("PrivateProperty", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic));
                var obj = new SubClassWithAProperty();
                obj.SetPrivateProperty(8);
                int result = (int)getter(obj);
                Assert.AreEqual(8, result);
            }

            {
                var getter = getFuncGetFn(typeof(IInterfaceWithAPropery).GetProperty("GetSetProperty"));
                var obj = new ClassImplementingIInterfaceWithAPropery();
                obj.GetSetProperty = 2;
                int result = (int)getter(obj);
                Assert.AreEqual(2, result);
            }
            {
                var getter = getFuncGetFn(typeof(ClassImplementingIInterfaceWithAPropery).GetProperty("GetSetProperty"));
                var obj = new ClassImplementingIInterfaceWithAPropery();
                obj.GetSetProperty = 2;
                int result = (int)getter(obj);
                Assert.AreEqual(2, result);
            }

            {
                var getter = getFuncGetFn(typeof(IInterfaceWithAPropery).GetProperty("SetProperty"));
                Assert.IsNull(getter);
            }
            {
                var getter = getFuncGetFn(typeof(ClassImplementingIInterfaceWithAPropery).GetProperty("SetProperty"));
                var obj = new ClassImplementingIInterfaceWithAPropery();
                obj._SetPropertyAddedGetterValue = 3;
                int result = (int)getter(obj);
                Assert.AreEqual(3, result);
            }

            {
                var getter = getFuncGetFn(typeof(IInterfaceWithAPropery).GetProperty("GetProperty"));
                var obj = new ClassImplementingIInterfaceWithAPropery();
                obj._GetProperty = 4;
                int result = (int)getter(obj);
                Assert.AreEqual(4, result);
            }
            {
                var getter = getFuncGetFn(typeof(ClassImplementingIInterfaceWithAPropery).GetProperty("GetProperty"));
                var obj = new ClassImplementingIInterfaceWithAPropery();
                obj._GetProperty = 4;
                int result = (int)getter(obj);
                Assert.AreEqual(4, result);
            }

            {
                var getter = getFuncGetFn(typeof(ClassWithHiddenProperty).GetProperty("GetSetProperty", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly));
                var obj = new ClassWithHiddenProperty();
                obj.GetSetProperty = "4";
                string result = (string)getter(obj);
                Assert.AreEqual("4", result);
            }
            {
                var getter = getFuncGetFn(typeof(ClassWithHiddenPropertyBase).GetProperty("GetSetProperty", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly));
                var obj = new ClassWithHiddenProperty();
                ((ClassWithHiddenPropertyBase)obj).GetSetProperty = 4;
                int result = (int)getter(obj);
                Assert.AreEqual(4, result);
            }
        }

        class ClassWithFields
        {
            public int PublicField = 1;
            protected int ProtectedField = 2;
            private int PrivateField = 3;

            public int GetProtectedField() => ProtectedField;
            public int GetPrivateField() => PrivateField;
        }

        [Test]
        public void GetDataMemberGetValueOnDeclaringType()
        {
            // for properies
            RunPropertyTestsWithGetGetValueOnDeclaringType(TypeExtensions.GetDataMemberGetValueOnDeclaringType);

            // for fields
            {
                var getter = TypeExtensions.GetDataMemberGetValueOnDeclaringType(typeof(ClassWithFields).GetField("PublicField"));
                ClassWithFields obj = new ClassWithFields();
                Assert.AreEqual(1, getter(obj));
            }
            {
                var getter = TypeExtensions.GetDataMemberGetValueOnDeclaringType(typeof(ClassWithFields).GetField("ProtectedField", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));
                ClassWithFields obj = new ClassWithFields();
                Assert.AreEqual(2, getter(obj));
            }
            {
                var getter = TypeExtensions.GetDataMemberGetValueOnDeclaringType(typeof(ClassWithFields).GetField("PrivateField", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));
                ClassWithFields obj = new ClassWithFields();
                Assert.AreEqual(3, getter(obj));
            }
        }

        [Test]
        public void GetDataMemberSetValueOnDeclaringType()
        {
            // for properies
            RunPropertyTestsWithGetSetValueOnDeclaringType(TypeExtensions.GetDataMemberSetValueOnDeclaringType);

            // for fields
            {
                var setter = TypeExtensions.GetDataMemberSetValueOnDeclaringType(typeof(ClassWithFields).GetField("PublicField"));
                ClassWithFields obj = new ClassWithFields();
                setter(obj, (object)5);
                Assert.AreEqual(5, obj.PublicField);
            }
            {
                var setter = TypeExtensions.GetDataMemberSetValueOnDeclaringType(typeof(ClassWithFields).GetField("ProtectedField", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));
                ClassWithFields obj = new ClassWithFields();
                setter(obj, (object)5);
                Assert.AreEqual(5, obj.GetProtectedField());
            }
            {
                var setter = TypeExtensions.GetDataMemberSetValueOnDeclaringType(typeof(ClassWithFields).GetField("PrivateField", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));
                ClassWithFields obj = new ClassWithFields();
                setter(obj, (object)5);
                Assert.AreEqual(5, obj.GetPrivateField());
            }
        }

        #pragma warning disable CS0649, CS0169
        abstract class EnumeratedBaseClass
        {
            public int BasePublicGetSet { get; set; }
            public int BasePublicGet { get; }
            private int BasePrivateGet { get; }
            public int BasePublicSet { set => _ = value; }
            private int BasePrivateSet { set => _ = value; }

            private int BasePrivateGetSet { get; set; }
            protected int BaseProtectedGetSet { get; set; }
            public int BaseGetPrivateSet { get; private set; }
            public int BaseSetPrivateGet { private get; set; }
            public int BaseSetProtectedGet { protected get; set; }

            protected abstract int BaseAbstractGet { get; }
            protected abstract int BaseAbstractSet { set; }

            private int BasePrivateField;
            protected int BaseProtectedField;
            public int BasePublicField;

        }
        sealed class EnumeratedLeafClass : EnumeratedBaseClass
        {
            protected override int BaseAbstractGet => 123;
            protected override int BaseAbstractSet { set => _ = value; }

            public int LeafPublicGetSet { get; set; }
            public int LeafPublicGet { get; }
            private int LeafPrivateGet { get; }
            public int LeafPublicSet { set => _ = value; }
            private int LeafPrivateSet { set => _ = value; }

            public int LeafPublicField;
            private int LeafPrivateField;
        }
        abstract class EnumeratedMidClass : EnumeratedBaseClass
        {
            protected override int BaseAbstractGet => 123;

            public int MidPublicGetSet { get; set; }
            public int MidPublicGet { get; }
            private int MidPrivateGet { get; }
            public int MidPublicSet { set => _ = value; }
            private int MidPrivateSet { set => _ = value; }

            public int MidPublicField;
            private int MidPrivateField;
        }
        class EnumeratedTrdClass : EnumeratedMidClass
        {
            protected override int BaseAbstractSet { set => _ = value; }

            public int TrdPublicGetSet { get; set; }
            public int TrdPublicGet { get; }
            private int TrdPrivateGet { get; }
            public int TrdPublicSet { set => _ = value; }
            private int TrdPrivateSet { set => _ = value; }

            public int TrdPublicField;
            private int TrdPrivateField;
        }

        [Test]
        public void EnumerateInstancePropertiesInUnspecifiedOrder()
        {
            string PropsToString(IEnumerable<System.Reflection.PropertyInfo> props)
            {
                return string.Join(";", props.Select(prop => prop.Name));
            }

            // \note This expects a specific order for simplicity, but the order returned by EnumerateInstancePropertiesInUnspecifiedOrder
            //       is so peculiar (and theoretically it really is not guaranteed) that callers might as well consider it unspecified.

            {
                IEnumerable<System.Reflection.PropertyInfo> props = TypeExtensions.EnumerateInstancePropertiesInUnspecifiedOrder(typeof(EnumeratedBaseClass));
                Assert.AreEqual("BasePublicGetSet;BasePublicGet;BasePrivateGet;BasePublicSet;BasePrivateSet;BasePrivateGetSet;BaseProtectedGetSet;BaseGetPrivateSet;BaseSetPrivateGet;BaseSetProtectedGet;BaseAbstractGet;BaseAbstractSet", PropsToString(props));
            }
            {
                IEnumerable<System.Reflection.PropertyInfo> props = TypeExtensions.EnumerateInstancePropertiesInUnspecifiedOrder(typeof(EnumeratedLeafClass));
                Assert.AreEqual("BaseAbstractGet;BaseAbstractSet;LeafPublicGetSet;LeafPublicGet;LeafPrivateGet;LeafPublicSet;LeafPrivateSet;BasePublicGetSet;BasePublicGet;BasePublicSet;BaseProtectedGetSet;BaseGetPrivateSet;BaseSetPrivateGet;BaseSetProtectedGet;BasePrivateGet;BasePrivateSet;BasePrivateGetSet", PropsToString(props));
            }
            {
                IEnumerable<System.Reflection.PropertyInfo> props = TypeExtensions.EnumerateInstancePropertiesInUnspecifiedOrder(typeof(EnumeratedMidClass));
                Assert.AreEqual("BaseAbstractGet;MidPublicGetSet;MidPublicGet;MidPrivateGet;MidPublicSet;MidPrivateSet;BasePublicGetSet;BasePublicGet;BasePublicSet;BaseProtectedGetSet;BaseGetPrivateSet;BaseSetPrivateGet;BaseSetProtectedGet;BaseAbstractSet;BasePrivateGet;BasePrivateSet;BasePrivateGetSet", PropsToString(props));
            }
            {
                IEnumerable<System.Reflection.PropertyInfo> props = TypeExtensions.EnumerateInstancePropertiesInUnspecifiedOrder(typeof(EnumeratedTrdClass));
                Assert.AreEqual("BaseAbstractSet;TrdPublicGetSet;TrdPublicGet;TrdPrivateGet;TrdPublicSet;TrdPrivateSet;BaseAbstractGet;MidPublicGetSet;MidPublicGet;MidPublicSet;BasePublicGetSet;BasePublicGet;BasePublicSet;BaseProtectedGetSet;BaseGetPrivateSet;BaseSetPrivateGet;BaseSetProtectedGet;MidPrivateGet;MidPrivateSet;BasePrivateGet;BasePrivateSet;BasePrivateGetSet", PropsToString(props));
            }
        }

        [Test]
        public void EnumerateInstanceFieldsInUnspecifiedOrder()
        {
            string FieldsToString(IEnumerable<System.Reflection.FieldInfo> fields)
            {
                return string.Join(";", fields.Select(field => field.Name));
            }

            // \note This expects a specific order for simplicity, but the order returned by EnumerateInstanceFieldsInUnspecifiedOrder
            //       is so peculiar (and theoretically it really is not guaranteed) that callers might as well consider it unspecified.

            {
                IEnumerable<System.Reflection.FieldInfo> fields = TypeExtensions.EnumerateInstanceFieldsInUnspecifiedOrder(typeof(EnumeratedBaseClass));
                Assert.AreEqual("BasePrivateField;BaseProtectedField;BasePublicField", FieldsToString(fields));
            }
            {
                IEnumerable<System.Reflection.FieldInfo> fields = TypeExtensions.EnumerateInstanceFieldsInUnspecifiedOrder(typeof(EnumeratedLeafClass));
                Assert.AreEqual("LeafPublicField;LeafPrivateField;BaseProtectedField;BasePublicField;BasePrivateField", FieldsToString(fields));
            }
            {
                IEnumerable<System.Reflection.FieldInfo> fields = TypeExtensions.EnumerateInstanceFieldsInUnspecifiedOrder(typeof(EnumeratedMidClass));
                Assert.AreEqual("MidPublicField;MidPrivateField;BaseProtectedField;BasePublicField;BasePrivateField", FieldsToString(fields));
            }
            {
                IEnumerable<System.Reflection.FieldInfo> fields = TypeExtensions.EnumerateInstanceFieldsInUnspecifiedOrder(typeof(EnumeratedTrdClass));
                Assert.AreEqual("TrdPublicField;TrdPrivateField;MidPublicField;BaseProtectedField;BasePublicField;MidPrivateField;BasePrivateField", FieldsToString(fields));
            }
        }

        [Test]
        public void EnumerateInstanceDataMembersInUnspecifiedOrder()
        {
            string FieldsToString(IEnumerable<System.Reflection.MemberInfo> fields)
            {
                return string.Join(";", fields
                    .Select(field =>
                    {
                        if (field is System.Reflection.FieldInfo)
                            return "F" + field.Name;
                        else if (field is System.Reflection.MemberInfo)
                            return "M" + field.Name;
                        else
                            return "";
                    })
                    .OrderBy(field => field, StringComparer.Ordinal));
            }

            // \note We sort the results as the order is unspecified

            {
                IEnumerable<System.Reflection.MemberInfo> fields = TypeExtensions.EnumerateInstanceDataMembersInUnspecifiedOrder(typeof(EnumeratedBaseClass));
                Assert.AreEqual("FBasePrivateField;FBaseProtectedField;FBasePublicField;MBaseAbstractGet;MBaseAbstractSet;MBaseGetPrivateSet;MBasePrivateGet;MBasePrivateGetSet;MBasePrivateSet;MBaseProtectedGetSet;MBasePublicGet;MBasePublicGetSet;MBasePublicSet;MBaseSetPrivateGet;MBaseSetProtectedGet", FieldsToString(fields));
            }
            {
                IEnumerable<System.Reflection.MemberInfo> fields = TypeExtensions.EnumerateInstanceDataMembersInUnspecifiedOrder(typeof(EnumeratedLeafClass));
                Assert.AreEqual("FBasePrivateField;FBaseProtectedField;FBasePublicField;FLeafPrivateField;FLeafPublicField;MBaseAbstractGet;MBaseAbstractSet;MBaseGetPrivateSet;MBasePrivateGet;MBasePrivateGetSet;MBasePrivateSet;MBaseProtectedGetSet;MBasePublicGet;MBasePublicGetSet;MBasePublicSet;MBaseSetPrivateGet;MBaseSetProtectedGet;MLeafPrivateGet;MLeafPrivateSet;MLeafPublicGet;MLeafPublicGetSet;MLeafPublicSet", FieldsToString(fields));
            }
            {
                IEnumerable<System.Reflection.MemberInfo> fields = TypeExtensions.EnumerateInstanceDataMembersInUnspecifiedOrder(typeof(EnumeratedMidClass));
                Assert.AreEqual("FBasePrivateField;FBaseProtectedField;FBasePublicField;FMidPrivateField;FMidPublicField;MBaseAbstractGet;MBaseAbstractSet;MBaseGetPrivateSet;MBasePrivateGet;MBasePrivateGetSet;MBasePrivateSet;MBaseProtectedGetSet;MBasePublicGet;MBasePublicGetSet;MBasePublicSet;MBaseSetPrivateGet;MBaseSetProtectedGet;MMidPrivateGet;MMidPrivateSet;MMidPublicGet;MMidPublicGetSet;MMidPublicSet", FieldsToString(fields));
            }
            {
                IEnumerable<System.Reflection.MemberInfo> fields = TypeExtensions.EnumerateInstanceDataMembersInUnspecifiedOrder(typeof(EnumeratedTrdClass));
                Assert.AreEqual("FBasePrivateField;FBaseProtectedField;FBasePublicField;FMidPrivateField;FMidPublicField;FTrdPrivateField;FTrdPublicField;MBaseAbstractGet;MBaseAbstractSet;MBaseGetPrivateSet;MBasePrivateGet;MBasePrivateGetSet;MBasePrivateSet;MBaseProtectedGetSet;MBasePublicGet;MBasePublicGetSet;MBasePublicSet;MBaseSetPrivateGet;MBaseSetProtectedGet;MMidPrivateGet;MMidPrivateSet;MMidPublicGet;MMidPublicGetSet;MMidPublicSet;MTrdPrivateGet;MTrdPrivateSet;MTrdPublicGet;MTrdPublicGetSet;MTrdPublicSet", FieldsToString(fields));
            }
        }

        [Test]
        public void ToMemberWithGenericDeclaringTypeString()
        {
            Assert.AreEqual("TypeExtensionsTests.NestingClass.Field", typeof(NestingClass).GetField("Field").ToMemberWithGenericDeclaringTypeString());
            Assert.AreEqual("TypeExtensionsTests.NestingClass.Method", typeof(NestingClass).GetMethod("Method").ToMemberWithGenericDeclaringTypeString());
            Assert.AreEqual("TypeExtensionsTests.NestingClass.Property", typeof(NestingClass).GetProperty("Property").ToMemberWithGenericDeclaringTypeString());

            Assert.AreEqual("TypeExtensionsTests.NestingClass.NestedClass.Field", typeof(NestingClass.NestedClass).GetField("Field").ToMemberWithGenericDeclaringTypeString());
            Assert.AreEqual("TypeExtensionsTests.NestingClass.NestedClass.Method", typeof(NestingClass.NestedClass).GetMethod("Method").ToMemberWithGenericDeclaringTypeString());
            Assert.AreEqual("TypeExtensionsTests.NestingClass.NestedClass.Property", typeof(NestingClass.NestedClass).GetProperty("Property").ToMemberWithGenericDeclaringTypeString());

            Assert.AreEqual("TypeExtensionsTests.NestingClass.NestedClassG<Object,Object>.Field",typeof(NestingClass.NestedClassG<object, object>).GetField("Field").ToMemberWithGenericDeclaringTypeString());
            Assert.AreEqual("TypeExtensionsTests.NestingClass.NestedClassG<Object,Object>.Method", typeof(NestingClass.NestedClassG<object, object>).GetMethod("Method").ToMemberWithGenericDeclaringTypeString());
            Assert.AreEqual("TypeExtensionsTests.NestingClass.NestedClassG<Object,Object>.Property", typeof(NestingClass.NestedClassG<object, object>).GetProperty("Property").ToMemberWithGenericDeclaringTypeString());

            Assert.AreEqual("TypeExtensionsTests.NestingClass.NestedClassG<,>.Field", typeof(NestingClass.NestedClassG<,>).GetField("Field").ToMemberWithGenericDeclaringTypeString());
            Assert.AreEqual("TypeExtensionsTests.NestingClass.NestedClassG<,>.Method", typeof(NestingClass.NestedClassG<,>).GetMethod("Method").ToMemberWithGenericDeclaringTypeString());
            Assert.AreEqual("TypeExtensionsTests.NestingClass.NestedClassG<,>.Property", typeof(NestingClass.NestedClassG<,>).GetProperty("Property").ToMemberWithGenericDeclaringTypeString());
        }

        [Test]
        public void ToMemberWithNamespaceQualifiedTypeString()
        {
            Assert.AreEqual("Cloud.Tests.TypeExtensionsTests.NestingClass.Field", typeof(NestingClass).GetField("Field").ToMemberWithNamespaceQualifiedTypeString());
            Assert.AreEqual("Cloud.Tests.TypeExtensionsTests.NestingClass.Method", typeof(NestingClass).GetMethod("Method").ToMemberWithNamespaceQualifiedTypeString());
            Assert.AreEqual("Cloud.Tests.TypeExtensionsTests.NestingClass.Property", typeof(NestingClass).GetProperty("Property").ToMemberWithNamespaceQualifiedTypeString());

            Assert.AreEqual("Cloud.Tests.TypeExtensionsTests.NestingClass.NestedClass.Field", typeof(NestingClass.NestedClass).GetField("Field").ToMemberWithNamespaceQualifiedTypeString());
            Assert.AreEqual("Cloud.Tests.TypeExtensionsTests.NestingClass.NestedClass.Method", typeof(NestingClass.NestedClass).GetMethod("Method").ToMemberWithNamespaceQualifiedTypeString());
            Assert.AreEqual("Cloud.Tests.TypeExtensionsTests.NestingClass.NestedClass.Property", typeof(NestingClass.NestedClass).GetProperty("Property").ToMemberWithNamespaceQualifiedTypeString());

            Assert.AreEqual("Cloud.Tests.TypeExtensionsTests.NestingClass.NestedClassG<System.Object,System.Object>.Field",typeof(NestingClass.NestedClassG<object, object>).GetField("Field").ToMemberWithNamespaceQualifiedTypeString());
            Assert.AreEqual("Cloud.Tests.TypeExtensionsTests.NestingClass.NestedClassG<System.Object,System.Object>.Method", typeof(NestingClass.NestedClassG<object, object>).GetMethod("Method").ToMemberWithNamespaceQualifiedTypeString());
            Assert.AreEqual("Cloud.Tests.TypeExtensionsTests.NestingClass.NestedClassG<System.Object,System.Object>.Property", typeof(NestingClass.NestedClassG<object, object>).GetProperty("Property").ToMemberWithNamespaceQualifiedTypeString());

            Assert.AreEqual("Cloud.Tests.TypeExtensionsTests.NestingClass.NestedClassG<,>.Field", typeof(NestingClass.NestedClassG<,>).GetField("Field").ToMemberWithNamespaceQualifiedTypeString());
            Assert.AreEqual("Cloud.Tests.TypeExtensionsTests.NestingClass.NestedClassG<,>.Method", typeof(NestingClass.NestedClassG<,>).GetMethod("Method").ToMemberWithNamespaceQualifiedTypeString());
            Assert.AreEqual("Cloud.Tests.TypeExtensionsTests.NestingClass.NestedClassG<,>.Property", typeof(NestingClass.NestedClassG<,>).GetProperty("Property").ToMemberWithNamespaceQualifiedTypeString());
        }

        [Test]
        public void ToMemberWithGlobalNamespaceQualifiedTypeString()
        {
            Assert.AreEqual("global::Cloud.Tests.TypeExtensionsTests.NestingClass.Field", typeof(NestingClass).GetField("Field").ToMemberWithGlobalNamespaceQualifiedTypeString());
            Assert.AreEqual("global::Cloud.Tests.TypeExtensionsTests.NestingClass.Method", typeof(NestingClass).GetMethod("Method").ToMemberWithGlobalNamespaceQualifiedTypeString());
            Assert.AreEqual("global::Cloud.Tests.TypeExtensionsTests.NestingClass.Property", typeof(NestingClass).GetProperty("Property").ToMemberWithGlobalNamespaceQualifiedTypeString());

            Assert.AreEqual("global::Cloud.Tests.TypeExtensionsTests.NestingClass.NestedClass.Field", typeof(NestingClass.NestedClass).GetField("Field").ToMemberWithGlobalNamespaceQualifiedTypeString());
            Assert.AreEqual("global::Cloud.Tests.TypeExtensionsTests.NestingClass.NestedClass.Method", typeof(NestingClass.NestedClass).GetMethod("Method").ToMemberWithGlobalNamespaceQualifiedTypeString());
            Assert.AreEqual("global::Cloud.Tests.TypeExtensionsTests.NestingClass.NestedClass.Property", typeof(NestingClass.NestedClass).GetProperty("Property").ToMemberWithGlobalNamespaceQualifiedTypeString());

            Assert.AreEqual("global::Cloud.Tests.TypeExtensionsTests.NestingClass.NestedClassG<global::System.Object,global::System.Object>.Field",typeof(NestingClass.NestedClassG<object, object>).GetField("Field").ToMemberWithGlobalNamespaceQualifiedTypeString());
            Assert.AreEqual("global::Cloud.Tests.TypeExtensionsTests.NestingClass.NestedClassG<global::System.Object,global::System.Object>.Method", typeof(NestingClass.NestedClassG<object, object>).GetMethod("Method").ToMemberWithGlobalNamespaceQualifiedTypeString());
            Assert.AreEqual("global::Cloud.Tests.TypeExtensionsTests.NestingClass.NestedClassG<global::System.Object,global::System.Object>.Property", typeof(NestingClass.NestedClassG<object, object>).GetProperty("Property").ToMemberWithGlobalNamespaceQualifiedTypeString());

            Assert.AreEqual("global::Cloud.Tests.TypeExtensionsTests.NestingClass.NestedClassG<,>.Field", typeof(NestingClass.NestedClassG<,>).GetField("Field").ToMemberWithGlobalNamespaceQualifiedTypeString());
            Assert.AreEqual("global::Cloud.Tests.TypeExtensionsTests.NestingClass.NestedClassG<,>.Method", typeof(NestingClass.NestedClassG<,>).GetMethod("Method").ToMemberWithGlobalNamespaceQualifiedTypeString());
            Assert.AreEqual("global::Cloud.Tests.TypeExtensionsTests.NestingClass.NestedClassG<,>.Property", typeof(NestingClass.NestedClassG<,>).GetProperty("Property").ToMemberWithGlobalNamespaceQualifiedTypeString());
        }

        interface InterfaceWithMethodsAndProperies
        {
            public int Method();

            public int Property { get; }
        }
        abstract class ClassWithOverridablesBase
        {
            public virtual int SealedMethod() => 1;
            public virtual int OverridenMethod() => 1;

            public virtual int SealedProperty { get; }
            public virtual int OverridenProperty { get; }
        }
        abstract class ClassWithOverridables : ClassWithOverridablesBase
        {
            public int Method() => 1;
            public override sealed int SealedMethod() => 1;
            public override int OverridenMethod() => 1;
            public virtual int VirtualMethod() => 1;
            public abstract int AbstractMethod();

            public int Property { get; }
            public override sealed int SealedProperty { get; }
            public override int OverridenProperty { get; }
            public virtual int VirtualProperty { get; }
            public abstract int AbstractProperty { get; }

            public int Field = 0;
        }

        [Test]
        public void MethodIsOverridable()
        {
            Assert.IsTrue(typeof(ClassWithOverridablesBase).GetMethod("SealedMethod").MethodIsOverridable());
            Assert.IsTrue(typeof(ClassWithOverridablesBase).GetMethod("OverridenMethod").MethodIsOverridable());
            Assert.IsFalse(typeof(ClassWithOverridables).GetMethod("Method").MethodIsOverridable());
            Assert.IsFalse(typeof(ClassWithOverridables).GetMethod("SealedMethod").MethodIsOverridable());
            Assert.IsTrue(typeof(ClassWithOverridables).GetMethod("OverridenMethod").MethodIsOverridable());
            Assert.IsTrue(typeof(ClassWithOverridables).GetMethod("VirtualMethod").MethodIsOverridable());
            Assert.IsTrue(typeof(ClassWithOverridables).GetMethod("AbstractMethod").MethodIsOverridable());
            Assert.IsTrue(typeof(InterfaceWithMethodsAndProperies).GetMethod("Method").MethodIsOverridable());
        }

        [Test]
        public void PropertyIsOverridable()
        {
            Assert.IsTrue(typeof(ClassWithOverridablesBase).GetProperty("SealedProperty").PropertyIsOverridable());
            Assert.IsTrue(typeof(ClassWithOverridablesBase).GetProperty("OverridenProperty").PropertyIsOverridable());
            Assert.IsFalse(typeof(ClassWithOverridables).GetProperty("Property").PropertyIsOverridable());
            Assert.IsFalse(typeof(ClassWithOverridables).GetProperty("SealedProperty").PropertyIsOverridable());
            Assert.IsTrue(typeof(ClassWithOverridables).GetProperty("OverridenProperty").PropertyIsOverridable());
            Assert.IsTrue(typeof(ClassWithOverridables).GetProperty("VirtualProperty").PropertyIsOverridable());
            Assert.IsTrue(typeof(ClassWithOverridables).GetProperty("AbstractProperty").PropertyIsOverridable());
            Assert.IsTrue(typeof(InterfaceWithMethodsAndProperies).GetProperty("Property").PropertyIsOverridable());
        }

        [Test]
        public void DataMemberIsOverridable()
        {
            Assert.IsTrue(typeof(ClassWithOverridablesBase).GetProperty("SealedProperty").DataMemberIsOverridable());
            Assert.IsTrue(typeof(ClassWithOverridablesBase).GetProperty("OverridenProperty").DataMemberIsOverridable());
            Assert.IsFalse(typeof(ClassWithOverridables).GetProperty("Property").DataMemberIsOverridable());
            Assert.IsFalse(typeof(ClassWithOverridables).GetProperty("SealedProperty").DataMemberIsOverridable());
            Assert.IsTrue(typeof(ClassWithOverridables).GetProperty("OverridenProperty").DataMemberIsOverridable());
            Assert.IsTrue(typeof(ClassWithOverridables).GetProperty("VirtualProperty").DataMemberIsOverridable());
            Assert.IsTrue(typeof(ClassWithOverridables).GetProperty("AbstractProperty").DataMemberIsOverridable());
            Assert.IsTrue(typeof(InterfaceWithMethodsAndProperies).GetProperty("Property").DataMemberIsOverridable());

            Assert.IsFalse(typeof(ClassWithOverridables).GetField("Field").DataMemberIsOverridable());
        }

        class PropertyTestBase
        {
            public int PropertyInBase { get; set; }
            public virtual int VirtualPropertyInBase { get; }
            public virtual int VirtualPropertyInBaseOverridenInMid { get; }
            public virtual int VirtualPropertyInBaseOverridenInLeaf { set { } }
            public virtual int VirtualPropertyInBaseOverridenInBoth { get; }

            public int Field;
        }
        class PropertyTestMid : PropertyTestBase
        {
            public int PropertyInMid { set { } }
            public override int VirtualPropertyInBaseOverridenInMid { get; }
            public override int VirtualPropertyInBaseOverridenInBoth { get; }
            public virtual int VirtualPropertyInMidOverridenInLeaf { get; }

            public new int Field;
        }
        class PropertyTestLeaf : PropertyTestMid
        {
            public override int VirtualPropertyInBaseOverridenInLeaf { set { } }
            public override int VirtualPropertyInBaseOverridenInBoth { get; }
            public override int VirtualPropertyInMidOverridenInLeaf { get; }

            public new int Field;
        }

        [Test]
        public void GetPropertyBaseDefinition()
        {
            Assert.AreEqual(typeof(PropertyTestBase), typeof(PropertyTestBase).GetProperty("PropertyInBase").GetPropertyBaseDefinition().DeclaringType);
            Assert.AreEqual(typeof(PropertyTestBase), typeof(PropertyTestMid).GetProperty("PropertyInBase").GetPropertyBaseDefinition().DeclaringType);
            Assert.AreEqual(typeof(PropertyTestBase), typeof(PropertyTestLeaf).GetProperty("PropertyInBase").GetPropertyBaseDefinition().DeclaringType);

            Assert.AreEqual(typeof(PropertyTestBase), typeof(PropertyTestBase).GetProperty("VirtualPropertyInBase").GetPropertyBaseDefinition().DeclaringType);
            Assert.AreEqual(typeof(PropertyTestBase), typeof(PropertyTestMid).GetProperty("VirtualPropertyInBase").GetPropertyBaseDefinition().DeclaringType);
            Assert.AreEqual(typeof(PropertyTestBase), typeof(PropertyTestLeaf).GetProperty("VirtualPropertyInBase").GetPropertyBaseDefinition().DeclaringType);

            Assert.AreEqual(typeof(PropertyTestBase), typeof(PropertyTestBase).GetProperty("VirtualPropertyInBaseOverridenInLeaf").GetPropertyBaseDefinition().DeclaringType);
            Assert.AreEqual(typeof(PropertyTestBase), typeof(PropertyTestMid).GetProperty("VirtualPropertyInBaseOverridenInLeaf").GetPropertyBaseDefinition().DeclaringType);
            Assert.AreEqual(typeof(PropertyTestBase), typeof(PropertyTestLeaf).GetProperty("VirtualPropertyInBaseOverridenInLeaf").GetPropertyBaseDefinition().DeclaringType);

            Assert.AreEqual(typeof(PropertyTestBase), typeof(PropertyTestBase).GetProperty("VirtualPropertyInBaseOverridenInBoth").GetPropertyBaseDefinition().DeclaringType);
            Assert.AreEqual(typeof(PropertyTestBase), typeof(PropertyTestMid).GetProperty("VirtualPropertyInBaseOverridenInBoth").GetPropertyBaseDefinition().DeclaringType);
            Assert.AreEqual(typeof(PropertyTestBase), typeof(PropertyTestLeaf).GetProperty("VirtualPropertyInBaseOverridenInBoth").GetPropertyBaseDefinition().DeclaringType);

            Assert.AreEqual(typeof(PropertyTestBase), typeof(PropertyTestBase).GetProperty("VirtualPropertyInBaseOverridenInMid").GetPropertyBaseDefinition().DeclaringType);
            Assert.AreEqual(typeof(PropertyTestBase), typeof(PropertyTestMid).GetProperty("VirtualPropertyInBaseOverridenInMid").GetPropertyBaseDefinition().DeclaringType);
            Assert.AreEqual(typeof(PropertyTestBase), typeof(PropertyTestLeaf).GetProperty("VirtualPropertyInBaseOverridenInMid").GetPropertyBaseDefinition().DeclaringType);

            Assert.AreEqual(typeof(PropertyTestMid), typeof(PropertyTestMid).GetProperty("PropertyInMid").GetPropertyBaseDefinition().DeclaringType);
            Assert.AreEqual(typeof(PropertyTestMid), typeof(PropertyTestLeaf).GetProperty("PropertyInMid").GetPropertyBaseDefinition().DeclaringType);

            Assert.AreEqual(typeof(PropertyTestMid), typeof(PropertyTestMid).GetProperty("VirtualPropertyInMidOverridenInLeaf").GetPropertyBaseDefinition().DeclaringType);
            Assert.AreEqual(typeof(PropertyTestMid), typeof(PropertyTestLeaf).GetProperty("VirtualPropertyInMidOverridenInLeaf").GetPropertyBaseDefinition().DeclaringType);
        }

        [Test]
        public void GetDataMemberBaseDefinition()
        {
            Assert.AreEqual(typeof(PropertyTestBase), typeof(PropertyTestBase).GetProperty("PropertyInBase").GetDataMemberBaseDefinition().DeclaringType);
            Assert.AreEqual(typeof(PropertyTestBase), typeof(PropertyTestMid).GetProperty("PropertyInBase").GetDataMemberBaseDefinition().DeclaringType);
            Assert.AreEqual(typeof(PropertyTestBase), typeof(PropertyTestLeaf).GetProperty("PropertyInBase").GetDataMemberBaseDefinition().DeclaringType);

            Assert.AreEqual(typeof(PropertyTestBase), typeof(PropertyTestBase).GetProperty("VirtualPropertyInBase").GetDataMemberBaseDefinition().DeclaringType);
            Assert.AreEqual(typeof(PropertyTestBase), typeof(PropertyTestMid).GetProperty("VirtualPropertyInBase").GetDataMemberBaseDefinition().DeclaringType);
            Assert.AreEqual(typeof(PropertyTestBase), typeof(PropertyTestLeaf).GetProperty("VirtualPropertyInBase").GetDataMemberBaseDefinition().DeclaringType);

            Assert.AreEqual(typeof(PropertyTestBase), typeof(PropertyTestBase).GetProperty("VirtualPropertyInBaseOverridenInLeaf").GetDataMemberBaseDefinition().DeclaringType);
            Assert.AreEqual(typeof(PropertyTestBase), typeof(PropertyTestMid).GetProperty("VirtualPropertyInBaseOverridenInLeaf").GetDataMemberBaseDefinition().DeclaringType);
            Assert.AreEqual(typeof(PropertyTestBase), typeof(PropertyTestLeaf).GetProperty("VirtualPropertyInBaseOverridenInLeaf").GetDataMemberBaseDefinition().DeclaringType);

            Assert.AreEqual(typeof(PropertyTestBase), typeof(PropertyTestBase).GetProperty("VirtualPropertyInBaseOverridenInBoth").GetDataMemberBaseDefinition().DeclaringType);
            Assert.AreEqual(typeof(PropertyTestBase), typeof(PropertyTestMid).GetProperty("VirtualPropertyInBaseOverridenInBoth").GetDataMemberBaseDefinition().DeclaringType);
            Assert.AreEqual(typeof(PropertyTestBase), typeof(PropertyTestLeaf).GetProperty("VirtualPropertyInBaseOverridenInBoth").GetDataMemberBaseDefinition().DeclaringType);

            Assert.AreEqual(typeof(PropertyTestBase), typeof(PropertyTestBase).GetProperty("VirtualPropertyInBaseOverridenInMid").GetDataMemberBaseDefinition().DeclaringType);
            Assert.AreEqual(typeof(PropertyTestBase), typeof(PropertyTestMid).GetProperty("VirtualPropertyInBaseOverridenInMid").GetDataMemberBaseDefinition().DeclaringType);
            Assert.AreEqual(typeof(PropertyTestBase), typeof(PropertyTestLeaf).GetProperty("VirtualPropertyInBaseOverridenInMid").GetDataMemberBaseDefinition().DeclaringType);

            Assert.AreEqual(typeof(PropertyTestMid), typeof(PropertyTestMid).GetProperty("PropertyInMid").GetDataMemberBaseDefinition().DeclaringType);
            Assert.AreEqual(typeof(PropertyTestMid), typeof(PropertyTestLeaf).GetProperty("PropertyInMid").GetDataMemberBaseDefinition().DeclaringType);

            Assert.AreEqual(typeof(PropertyTestMid), typeof(PropertyTestMid).GetProperty("VirtualPropertyInMidOverridenInLeaf").GetDataMemberBaseDefinition().DeclaringType);
            Assert.AreEqual(typeof(PropertyTestMid), typeof(PropertyTestLeaf).GetProperty("VirtualPropertyInMidOverridenInLeaf").GetDataMemberBaseDefinition().DeclaringType);

            Assert.AreEqual(typeof(PropertyTestBase), typeof(PropertyTestBase).GetField("Field").GetDataMemberBaseDefinition().DeclaringType);
            Assert.AreEqual(typeof(PropertyTestMid), typeof(PropertyTestMid).GetField("Field").GetDataMemberBaseDefinition().DeclaringType);
            Assert.AreEqual(typeof(PropertyTestLeaf), typeof(PropertyTestLeaf).GetField("Field").GetDataMemberBaseDefinition().DeclaringType);
        }

        class ClassForMemberTypeTests
        {
            public int IntField;
            public string StringProperty { get; }

            public class GenericInner<TInner>
            {
                public TInner TInnerField;
                public TInner TInnerProperty { get; }
            }
        }

        [Test]
        public void GetDataMemberType()
        {
            Assert.AreEqual(typeof(int), typeof(ClassForMemberTypeTests).GetField("IntField").GetDataMemberType());
            Assert.AreEqual(typeof(string), typeof(ClassForMemberTypeTests).GetProperty("StringProperty").GetDataMemberType());
            Assert.AreEqual(typeof(float), typeof(ClassForMemberTypeTests.GenericInner<float>).GetField("TInnerField").GetDataMemberType());
            Assert.AreEqual(typeof(float), typeof(ClassForMemberTypeTests.GenericInner<float>).GetProperty("TInnerProperty").GetDataMemberType());
            Assert.AreEqual("TInner", typeof(ClassForMemberTypeTests.GenericInner<>).GetField("TInnerField").GetDataMemberType().Name);
            Assert.AreEqual("TInner", typeof(ClassForMemberTypeTests.GenericInner<>).GetProperty("TInnerProperty").GetDataMemberType().Name);
        }

        #pragma warning restore CS0649, CS0169

        [Test]
        public void IsCollection()
        {
            Assert.IsTrue(typeof(int[]).IsCollection());
            Assert.IsTrue(typeof(List<int>).IsCollection());
            Assert.IsTrue(typeof(Queue<int>).IsCollection());
            Assert.IsTrue(typeof(HashSet<int>).IsCollection());
            Assert.IsTrue(typeof(Dictionary<int, string>).IsCollection());

            Assert.IsFalse(typeof(Nullable<int>).IsCollection());
            Assert.IsFalse(typeof(int).IsCollection());
            Assert.IsFalse(typeof(string).IsCollection());
        }
        [Test]
        public void GetCollectionElementType()
        {
            Assert.AreEqual(typeof(int), typeof(int[]).GetCollectionElementType());
            Assert.AreEqual(typeof(int), typeof(List<int>).GetCollectionElementType());
            Assert.AreEqual(typeof(int), typeof(Queue<int>).GetCollectionElementType());
            Assert.AreEqual(typeof(int), typeof(HashSet<int>).GetCollectionElementType());
            Assert.AreEqual(typeof(KeyValuePair<int, string>), typeof(Dictionary<int, string>).GetCollectionElementType());
        }
        [Test]
        public void IsDictionary()
        {
            Assert.IsTrue(typeof(Dictionary<int, string>).IsDictionary());
            Assert.IsTrue(typeof(OrderedDictionary<int, string>).IsDictionary());

            Assert.IsFalse(typeof(int[]).IsDictionary());
            Assert.IsFalse(typeof(List<int>).IsDictionary());
            Assert.IsFalse(typeof(Queue<int>).IsDictionary());
            Assert.IsFalse(typeof(HashSet<int>).IsDictionary());
            Assert.IsFalse(typeof(Nullable<int>).IsDictionary());
            Assert.IsFalse(typeof(int).IsDictionary());
            Assert.IsFalse(typeof(string).IsDictionary());
        }
        [Test]
        public void GetDictionaryKeyAndValueTypes()
        {
            Assert.AreEqual((typeof(int), typeof(string)), typeof(Dictionary<int, string>).GetDictionaryKeyAndValueTypes());
            Assert.AreEqual((typeof(int), typeof(string)), typeof(OrderedDictionary<int, string>).GetDictionaryKeyAndValueTypes());
        }
        [Test]
        public void GetSystemNullableElementType()
        {
            Assert.AreEqual(typeof(int), typeof(int?).GetSystemNullableElementType());
            Assert.AreEqual(typeof(TestEnumType), typeof(TestEnumType?).GetSystemNullableElementType());
        }
    }
}
