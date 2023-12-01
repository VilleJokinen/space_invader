// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Message;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Cloud.Tests
{
    class TypeScannerTests
    {
        public class CaseA_BaseClass
        {
        }
        public class CaseA_SubClass : CaseA_BaseClass
        {
        }

        public abstract class CaseB_BaseClass
        {
        }
        public class CaseB_SubClass : CaseB_BaseClass
        {
        }

        public interface CaseC_BaseInterface
        {
        }
        public interface CaseC_SubInterface : CaseC_BaseInterface
        {
        }
        public class CaseC_Implementation : CaseC_SubInterface
        {
        }
        public abstract class CaseC_AbstractImplementation : CaseC_SubInterface
        {
        }

        [Test]
        public void TestGetDerivedTypes()
        {
            Assert.AreEqual(new HashSet<Type> { typeof(CaseA_SubClass) }, TypeScanner.GetDerivedTypes<CaseA_BaseClass>().ToHashSet());
            Assert.AreEqual(new HashSet<Type> { typeof(CaseB_SubClass) }, TypeScanner.GetDerivedTypes<CaseB_BaseClass>().ToHashSet());
            Assert.AreEqual(new HashSet<Type> { }, TypeScanner.GetDerivedTypes<CaseC_BaseInterface>().ToHashSet());
            Assert.AreEqual(new HashSet<Type> { }, TypeScanner.GetDerivedTypes<CaseC_SubInterface>().ToHashSet());
        }

        [Test]
        public void TestGetDerivedTypesAndSelf()
        {
            Assert.AreEqual(new HashSet<Type> { typeof(CaseA_BaseClass), typeof(CaseA_SubClass) }, TypeScanner.GetDerivedTypesAndSelf<CaseA_BaseClass>().ToHashSet());
            Assert.AreEqual(new HashSet<Type> { typeof(CaseB_BaseClass), typeof(CaseB_SubClass) }, TypeScanner.GetDerivedTypesAndSelf<CaseB_BaseClass>().ToHashSet());
            Assert.AreEqual(new HashSet<Type> { typeof(CaseC_BaseInterface) }, TypeScanner.GetDerivedTypesAndSelf<CaseC_BaseInterface>().ToHashSet());
            Assert.AreEqual(new HashSet<Type> { typeof(CaseC_SubInterface) }, TypeScanner.GetDerivedTypesAndSelf<CaseC_SubInterface>().ToHashSet());
        }

        [Test]
        public void TestGetConcreteDerivedTypes()
        {
            Assert.AreEqual(new HashSet<Type> { typeof(CaseA_SubClass) }, TypeScanner.GetConcreteDerivedTypes<CaseA_BaseClass>().ToHashSet());
            Assert.AreEqual(new HashSet<Type> { typeof(CaseB_SubClass) }, TypeScanner.GetConcreteDerivedTypes<CaseB_BaseClass>().ToHashSet());
            Assert.AreEqual(new HashSet<Type> { }, TypeScanner.GetConcreteDerivedTypes<CaseC_BaseInterface>().ToHashSet());
            Assert.AreEqual(new HashSet<Type> { }, TypeScanner.GetConcreteDerivedTypes<CaseC_SubInterface>().ToHashSet());
        }

        [Test]
        public void TestGetInterfaceImplementations()
        {
            Assert.AreEqual(new HashSet<Type> { typeof(CaseC_Implementation), typeof(CaseC_AbstractImplementation) }, TypeScanner.GetInterfaceImplementations<CaseC_BaseInterface>().ToHashSet());
            Assert.AreEqual(new HashSet<Type> { typeof(CaseC_Implementation), typeof(CaseC_AbstractImplementation) }, TypeScanner.GetInterfaceImplementations<CaseC_SubInterface>().ToHashSet());
        }

        [AttributeUsage(AttributeTargets.Class)]
        class TestAttribute1 : Attribute { }
        [TestAttribute1]
        class AttrCaseAClass { }

        [AttributeUsage(AttributeTargets.Class, Inherited = false)]
        class TestAttribute2 : Attribute { }
        [TestAttribute2]
        abstract class AttrCaseBBaseClass { }
        class AttrCaseBSubClass : AttrCaseBBaseClass { }

        [AttributeUsage(AttributeTargets.Class, Inherited = true)]
        class TestAttribute3 : Attribute { }
        [TestAttribute3]
        abstract class AttrCaseCBaseClass { }
        class AttrCaseCSubClass : AttrCaseCBaseClass { }

        [Test]
        public void TestGetClassesWithAttribute()
        {
            Assert.AreEqual(new HashSet<Type> { typeof(AttrCaseAClass) }, TypeScanner.GetClassesWithAttribute<TestAttribute1>().ToHashSet());
            Assert.AreEqual(new HashSet<Type> { typeof(AttrCaseBBaseClass) }, TypeScanner.GetClassesWithAttribute<TestAttribute2>().ToHashSet());
            Assert.AreEqual(new HashSet<Type> { typeof(AttrCaseCBaseClass), typeof(AttrCaseCSubClass) }, TypeScanner.GetClassesWithAttribute<TestAttribute3>().ToHashSet());
        }

        [AttributeUsage(AttributeTargets.Class)]
        class TestAttribute4 : Attribute { }
        [TestAttribute4]
        abstract class AttrCaseDAbstractBaseClass { }
        class AttrCaseDSubClass : AttrCaseDAbstractBaseClass { }
        [TestAttribute4]
        class AttrCaseEAbstractBaseClass { }
        abstract class AttrCaseESubClass : AttrCaseEAbstractBaseClass { }

        [Test]
        public void TestGetConcreteClassesWithAttribute()
        {
            Assert.AreEqual(new HashSet<Type> { typeof(AttrCaseDSubClass), typeof(AttrCaseEAbstractBaseClass) }, TypeScanner.GetConcreteClassesWithAttribute<TestAttribute4>().ToHashSet());
        }
    }
}
