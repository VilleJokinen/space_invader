// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Utility;
using NUnit.Framework;
using System;
using Metaplay.Core;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cloud.Tests
{
    class PooledListTests
    {
        const int ListDefaultCapacity = 4;
        class TestItemClass
        {
            public int Identifier;

            /// <inheritdoc />
            public override string ToString()
            {
                return $"{nameof(Identifier)}: {Identifier.ToString(CultureInfo.InvariantCulture)}";
            }
        }

        struct TestItemStruct : IComparable<TestItemStruct>
        {
            public int Identifier;

            public TestItemStruct(int identifier)
            {
                Identifier = identifier;
            }

            /// <inheritdoc />
            public int CompareTo(TestItemStruct other)
            {
                return Identifier.CompareTo(other.Identifier);
            }

            /// <inheritdoc />
            public override string ToString()
            {
                return $"{nameof(Identifier)}: {Identifier.ToString(CultureInfo.InvariantCulture)}";
            }
        }

        [Test]
        public void TestCreateEmpty()
        {
            using PooledList<TestItemClass> list = PooledList<TestItemClass>.Create(0);
            Assert.AreEqual(0, list.Count);
            Assert.AreEqual(0, list.Capacity);
            Assert.AreEqual(0, list.Span.Length);
        }

        [Test]
        public void TestCreateEmptyAddOne()
        {
            using PooledList<TestItemClass> list      = PooledList<TestItemClass>.Create(0);
            TestItemClass             testItem0 = new TestItemClass() {Identifier = 1};

            list.Add(testItem0);

            Assert.GreaterOrEqual(list.Capacity, ListDefaultCapacity);

            Assert.AreEqual(1, list.Count);
            Assert.AreEqual(1, list.Span.Length);
            Assert.AreEqual(testItem0, list[0]);
            Assert.AreEqual(testItem0, list.Span[0]);
            Assert.AreEqual(testItem0, list.First());
        }

        [Test]
        public void TestCreateMinCapacity()
        {
            using PooledList<TestItemClass> list = PooledList<TestItemClass>.Create();

            Assert.GreaterOrEqual(list.Capacity, ListDefaultCapacity);
        }

        [Test]
        public void TestCreateDefaultAddItems()
        {
            using PooledList<TestItemClass> list = PooledList<TestItemClass>.Create();

            for (int i = 0; i < ListDefaultCapacity; i++)
                list.Add(new TestItemClass {Identifier = i});

            Assert.GreaterOrEqual(list.Capacity, ListDefaultCapacity);
            Assert.AreEqual(ListDefaultCapacity, list.Count);
            Assert.AreEqual(ListDefaultCapacity, list.Span.Length);

            for (int i = 0; i < ListDefaultCapacity; i++)
            {
                Assert.AreEqual(i, list[i].Identifier);
                Assert.AreEqual(i, list.Span[i].Identifier);
            }

            int i2 = 0;
            foreach (TestItemClass item in list)
                Assert.AreEqual(i2++, item.Identifier);
        }

        [Test]
        public void TestCreateDefaultAddGrow()
        {
            using PooledList<TestItemClass> list = PooledList<TestItemClass>.Create();

            int oldCapacity = list.Capacity;

            for (int i = 0; i < oldCapacity + 1; i++)
                list.Add(new TestItemClass { Identifier = i });

            Assert.Greater(list.Capacity, oldCapacity);

            Assert.AreEqual(oldCapacity + 1, list.Count);
            Assert.AreEqual(oldCapacity + 1, list.Span.Length);

            for (int i = 0; i < list.Count; i++)
            {
                Assert.AreEqual(i, list[i].Identifier);
                Assert.AreEqual(i, list.Span[i].Identifier);
            }

            int i2 = 0;
            foreach (TestItemClass item in list)
                Assert.AreEqual(i2++, item.Identifier);
        }

        [Test]
        public void TestCreateDefaultInsertToBeginning()
        {
            using PooledList<TestItemClass> list = PooledList<TestItemClass>.Create();

            int oldCapacity = list.Capacity;

            for (int i = 0; i < oldCapacity + 1; i++)
                list.Insert(0, new TestItemClass { Identifier = i });

            Assert.Greater(list.Capacity, oldCapacity);

            Assert.AreEqual(oldCapacity + 1, list.Count);
            Assert.AreEqual(oldCapacity + 1, list.Span.Length);

            for (int i = 0; i < list.Count; i++)
            {
                int expected = i == 0 ? list.Count - 1 : i - 1;
                Assert.AreEqual(expected, list[i].Identifier);
            }
        }

        [Test]
        public void TestCreateDefaultInsertToMiddle()
        {
            using PooledList<TestItemClass> list = PooledList<TestItemClass>.Create();

            int oldCapacity = list.Capacity;
            int halfIndex   = oldCapacity / 2;
            for (int i = 0; i < oldCapacity; i++)
                list.Add(new TestItemClass { Identifier = i });

            TestItemClass oldHalfIndex = list[halfIndex];

            TestItemClass testItem     = new TestItemClass {Identifier = 123};
            list.Insert(halfIndex, testItem);

            Assert.AreEqual(testItem, list[halfIndex]);

            Assert.AreEqual(oldHalfIndex, list[^1]);
            Assert.AreEqual(oldHalfIndex, list.Last());

            Assert.Greater(list.Capacity, oldCapacity);

            Assert.AreEqual(oldCapacity + 1, list.Count);
            Assert.AreEqual(oldCapacity + 1, list.Span.Length);
        }

        [Test]
        public void TestCreateDefaultRemoveFromMiddle()
        {
            using PooledList<TestItemClass> list = PooledList<TestItemClass>.Create();

            int oldCapacity = list.Capacity;
            int halfIndex   = oldCapacity / 2;
            for (int i = 0; i < oldCapacity; i++)
                list.Add(new TestItemClass { Identifier = i });

            int           oldCount          = list.Count;

            TestItemClass expectedMovedItem = list[^1];

            TestItemClass testItem = list[halfIndex];

            Assert.True(list.Contains(testItem));
            Assert.True(list.Contains(expectedMovedItem));

            list.RemoveAt(halfIndex);

            Assert.False(list.Contains(testItem));
            Assert.True(list.Contains(expectedMovedItem));

            Assert.AreEqual(expectedMovedItem, list[halfIndex]);

            Assert.Less(list.Count, oldCount);
            Assert.AreEqual(list.Capacity, oldCapacity);
        }

        [Test]
        public void TestGetAndSetThrow()
        {
            using PooledList<TestItemClass> list = PooledList<TestItemClass>.Create();

            for (int i = 0; i < list.Capacity; i++)
                list.Add(new TestItemClass { Identifier = i });

            int oldCapacity = list.Capacity;
            int oldCount    = list.Count;

            Assert.Throws<IndexOutOfRangeException>(
                () =>
                {
                    TestItemClass testItem = list[list.Capacity];
                    _ = testItem;
                });
            Assert.Throws<IndexOutOfRangeException>(
                () =>
                {
                    list[list.Capacity] = new TestItemClass();
                });

            Assert.AreEqual(list.Capacity, oldCapacity);
            Assert.AreEqual(list.Count, oldCount);
        }

        [Test]
        public void TestOperationsOnEmpty()
        {
            using PooledList<TestItemClass> list = PooledList<TestItemClass>.Create(0);

            TestItemClass testItem = new TestItemClass {Identifier = 0};

            Assert.AreEqual(0, list.Count);
            Assert.False(list.Contains(testItem));
            Assert.AreEqual(-1, list.IndexOf(testItem));
            Assert.False(list.Remove(testItem));
            Assert.Throws<ArgumentOutOfRangeException>(() => list.RemoveAt(0));

            foreach(TestItemClass cl in list)
                Assert.Fail("Iterator should not iterate any when empty.");

            Assert.AreEqual(0, list.Span.Length);
        }

        [Test]
        public void TestRemoveByReference()
        {
            using PooledList<TestItemClass> list = PooledList<TestItemClass>.Create();

            TestItemClass testItem = new TestItemClass { Identifier = 0 };

            list.Add(testItem);

            Assert.AreEqual(1, list.Count);
            Assert.AreEqual(0, list.IndexOf(testItem));
            Assert.True(list.Contains(testItem));

            Assert.True(list.Remove(testItem));

            Assert.AreEqual(0, list.Count);
            Assert.AreEqual(-1, list.IndexOf(testItem));
            Assert.False(list.Contains(testItem));
        }

        [Test]
        public void TestRemoveByValue()
        {
            using PooledList<TestItemStruct> list = PooledList<TestItemStruct>.Create();

            TestItemStruct testItem  = new TestItemStruct { Identifier = 0 };
            TestItemStruct testItem2 = new TestItemStruct { Identifier = 0 };
            TestItemStruct testItem3 = new TestItemStruct { Identifier = 0 };

            Assert.AreEqual(testItem, testItem2);
            Assert.AreEqual(testItem2, testItem3);

            list.Add(testItem);
            list.Add(testItem2);

            Assert.AreEqual(2, list.Count);
            Assert.AreEqual(0, list.IndexOf(testItem));
            Assert.True(list.Contains(testItem));
            Assert.AreEqual(0, list.IndexOf(testItem2));
            Assert.True(list.Contains(testItem2));

            Assert.True(list.Remove(testItem3));

            Assert.AreEqual(1, list.Count);
            Assert.AreEqual(0, list.IndexOf(testItem));
            Assert.True(list.Contains(testItem));
            Assert.AreEqual(0, list.IndexOf(testItem2));
            Assert.True(list.Contains(testItem2));

            Assert.True(list.Remove(testItem3));

            Assert.AreEqual(0, list.Count);
            Assert.AreEqual(-1, list.IndexOf(testItem));
            Assert.False(list.Contains(testItem));
        }

        [Test]
        public void TestSort()
        {
            using PooledList<TestItemStruct> list = PooledList<TestItemStruct>.Create();

            for (int i = 0; i < 100; i++)
                list.Add(new TestItemStruct(i));

            RandomPCG rnd = RandomPCG.CreateFromSeed(123);
            rnd.ShuffleInPlace(list);

            Assert.That(list, Is.Not.Ordered.Ascending);
            Assert.That(list, Is.Not.Ordered.Descending);

            Assert.AreEqual(100, list.Count);
            Assert.GreaterOrEqual(list.Capacity, 100);

            list.Sort();

            Assert.That(list, Is.Ordered.Ascending);
        }


        [Test]
        public void TestSortByDescending()
        {
            using PooledList<TestItemStruct> list = PooledList<TestItemStruct>.Create();

            for (int i = 0; i < 100; i++)
                list.Add(new TestItemStruct(i));

            RandomPCG rnd = RandomPCG.CreateFromSeed(123);
            rnd.ShuffleInPlace(list);

            Assert.That(list, Is.Not.Ordered.Ascending);
            Assert.That(list, Is.Not.Ordered.Descending);

            Assert.AreEqual(100, list.Count);
            Assert.GreaterOrEqual(list.Capacity, 100);

            list.Sort((t1, t2) => -t1.CompareTo(t2));

            Assert.That(list, Is.Ordered.Descending);
        }

        [Test]
        public void TestCopyToExactArray()
        {
            using PooledList<TestItemStruct> list = PooledList<TestItemStruct>.Create();

            for (int i = 0; i < 100; i++)
            {
                list.Add(new TestItemStruct(i));
            }

            TestItemStruct[] array = new TestItemStruct[100];

            list.CopyTo(array, 0);

            Assert.That(list, Is.EqualTo(array));
        }

        [Test]
        public void TestCopyToSmallerArray()
        {
            using PooledList<TestItemStruct> list = PooledList<TestItemStruct>.Create();

            for (int i = 0; i < 100; i++)
            {
                list.Add(new TestItemStruct(i));
            }

            TestItemStruct[] array = new TestItemStruct[50];

            Assert.Throws<ArgumentException>(() => list.CopyTo(array, 0));
        }

        [Test]
        public void TestCopyTwiceToLargerArray()
        {
            using PooledList<TestItemStruct> list = PooledList<TestItemStruct>.Create();

            for (int i = 0; i < 100; i++)
            {
                list.Add(new TestItemStruct(i));
            }

            TestItemStruct[] array = new TestItemStruct[200];

            list.CopyTo(array, 0);
            list.CopyTo(array, 100);

            for (int i = 0; i < 100; i++)
            {
                Assert.AreEqual(list[i], array[i]);
                Assert.AreEqual(list[i], array[i + 100]);
            }
        }

        [Test]
        public void TestIListInterface()
        {
            using PooledList<TestItemStruct> list = PooledList<TestItemStruct>.Create();

            IList          iList     = (IList)list;
            TestItemStruct testItem  = new TestItemStruct { Identifier = 1 };
            TestItemStruct testItem2 = new TestItemStruct { Identifier = 2 };
            TestItemClass  testItem3 = new TestItemClass { Identifier  = 1 };

            Assert.False(iList.IsReadOnly);
            Assert.False(iList.IsFixedSize);
            Assert.False(iList.IsSynchronized);

            Assert.AreNotEqual(testItem, testItem2);
            Assert.AreNotEqual(testItem2, testItem3);

            Assert.AreEqual(0, iList.Add(testItem));
            Assert.AreEqual(1, iList.Add(testItem2));
            Assert.Throws<InvalidCastException>(() => iList.Add(testItem3));

            Assert.AreEqual(2, iList.Count);

            Assert.True(iList.Contains(testItem));
            Assert.AreEqual(0, iList.IndexOf(testItem));

            Assert.True(iList.Contains(testItem2));
            Assert.AreEqual(1, iList.IndexOf(testItem2));

            iList.Remove(testItem);

            Assert.AreEqual(1, iList.Count);

            Assert.False(iList.Contains(testItem));
            Assert.AreEqual(-1, iList.IndexOf(testItem));

            Assert.True(iList.Contains(testItem2));
            Assert.AreEqual(0, iList.IndexOf(testItem2));

            iList.RemoveAt(0);

            Assert.AreEqual(0, iList.Count);

            Assert.False(iList.Contains(testItem2));
            Assert.AreEqual(-1, iList.IndexOf(testItem2));

            Assert.Throws<InvalidCastException>(() => iList.Remove(testItem3));

            iList.Add(testItem);
            iList[0] = testItem2;

            Assert.AreEqual(1, iList.Count);

            Assert.False(iList.Contains(testItem));
            Assert.AreEqual(-1, iList.IndexOf(testItem));

            Assert.True(iList.Contains(testItem2));
            Assert.AreEqual(0, iList.IndexOf(testItem2));

            Assert.AreEqual(testItem2, iList[0]);
        }
    }
}
