// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using NUnit.Framework;
using System;
using System.Reflection;

namespace Cloud.Tests
{
    class MethodInfoExtensionTests
    {
        class MyException : Exception { }
        class MethodTestClass
        {
            public int Result;
            public void SuccessMethod(int input)
            {
                Result = input;
            }
            public void ThrowSomething()
            {
                throw new MyException();
            }
        }

        [Test]
        public void TestSuccess()
        {
            MethodTestClass instance = new MethodTestClass();
            MethodInfo mi = instance.GetType().GetMethod(nameof(MethodTestClass.SuccessMethod));
            mi.InvokeWithoutWrappingError(instance, new object[1] { (int) 1 });
            Assert.AreEqual(1, instance.Result);
        }

        [Test]
        public void TestThrow()
        {
            MethodTestClass instance = new MethodTestClass();
            MethodInfo mi = instance.GetType().GetMethod(nameof(MethodTestClass.ThrowSomething));
            Assert.Throws<MyException>(() => mi.InvokeWithoutWrappingError(instance, new object[0] { }));
        }
    }
}
