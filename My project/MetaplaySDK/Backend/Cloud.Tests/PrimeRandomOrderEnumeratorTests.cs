// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Utility;
using NUnit.Framework;
using System;
using Metaplay.Core;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cloud.Tests
{
    class PrimeRandomOrderEnumeratorTests
    {
        [Test]
        public void TestEmptyList()
        {
            List<int> list = new List<int>();

            foreach (int _ in new PrimeRandomOrderEnumerable<int>(list))
                Assert.Fail("Empty list should not enumerate");
        }

        [Test]
        public void TestCount()
        {
            List<int> list = Enumerable.Range(0, 100).ToList();

            PrimeRandomOrderEnumerable<int> enumerable = new PrimeRandomOrderEnumerable<int>(list);

            Assert.AreEqual(100, enumerable.Count());
        }

        [Test]
        public void TestWhere()
        {
            Func<int, bool> predicate = x => x % 2 == 0;
            List<int>       list      = Enumerable.Range(0, 100).ToList();
            List<int>       expected  = list.Where(predicate).ToList();

            PrimeRandomOrderEnumerable<int> enumerable = new PrimeRandomOrderEnumerable<int>(list);

            Assert.AreEqual(50, enumerable.Where(predicate).Count());
            Assert.That(enumerable.Where(predicate), Is.EquivalentTo(expected));
        }

        [Test]
        public void TestArray()
        {
            int[] array = Enumerable.Range(0, 100).ToArray();

            Assert.That(array, Is.Ordered.Ascending);

            PrimeRandomOrderEnumerable<int> enumerable = new PrimeRandomOrderEnumerable<int>(array);

            Assert.That(enumerable, Is.EquivalentTo(array));
        }

        [Test]
        public void TestList()
        {
            List<int> list = Enumerable.Range(0, 100).ToList();

            Assert.That(list, Is.Ordered.Ascending);

            PrimeRandomOrderEnumerable<int> enumerable = new PrimeRandomOrderEnumerable<int>(list);

            Assert.That(enumerable, Is.EquivalentTo(list));
        }

        [TestCase(6)]
        [TestCase(7)]
        [TestCase(10)]
        [TestCase(13)]
        [TestCase(20)]
        [TestCase(50)]
        [TestCase(100)]
        public void TestNotIterateSameTwice(int listSize)
        {
            List<int> list = Enumerable.Range(0, listSize).ToList();

            Assert.That(list, Is.Ordered.Ascending);

            OrderedSet<int>                 iterated   = new OrderedSet<int>();
            PrimeRandomOrderEnumerable<int> enumerable = new PrimeRandomOrderEnumerable<int>(list);

            foreach (int i in enumerable)
            {
                Assert.False(iterated.Contains(i));
                Assert.True(iterated.Add(i));
            }
        }
    }
}
