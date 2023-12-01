// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Runtime.Serialization.Formatters.Binary;
using System.IO;
using Metaplay.Core;
using System.Globalization;
using static System.FormattableString;

namespace Cloud.Tests
{
    [TestFixture]
    public class OrderedContainerTests
    {
        class BadHashKey : IEquatable<BadHashKey>
        {
            string key;
            public BadHashKey(string key_)
            {
                key = key_;
            }
            public override int GetHashCode()
            {
                return key.Length;
            }
            public override bool Equals(object obj)
            {
                if (obj is BadHashKey other)
                {
                    return String.Equals(key, other.key);
                }
                return false;
            }

            bool IEquatable<BadHashKey>.Equals(BadHashKey other)
            {
                return String.Equals(key, other.key);
            }
        };
        abstract class SetOp<T>
        {
            public class Add : SetOp<T> { public T v; }
            public class Rem : SetOp<T> { public T v; }
            public class Clr : SetOp<T> { }
            public class Tst : SetOp<T> { public T v; }
        };
        abstract class DictOp<K,V>
        {
            public class Add : DictOp<K,V> { public K k; public V v; }
            public class Rem : DictOp<K, V> { public K k; }
            public class Clr : DictOp<K, V> { }
            public class Tst : DictOp<K, V> { public K k; }
        };

        void ExecuteSetOperations<T>(SetOp<T>[] ops) where T : IEquatable<T>
        {
            HashSet<T> refSet = new HashSet<T>();
            List<T> refList = new List<T>();
            OrderedSet<T> orderedSet = new OrderedSet<T>();

            Assert.AreEqual(refList, new List<T>(orderedSet));

            foreach (var op in ops)
            {
                switch (op)
                {
                    case SetOp<T>.Add add:
                        Assert.AreEqual(refSet.Add(add.v), orderedSet.Add(add.v));

                        if (!refList.Contains(add.v))
                            refList.Add(add.v);
                        break;

                    case SetOp<T>.Rem rem:
                        Assert.AreEqual(refSet.Remove(rem.v), orderedSet.Remove(rem.v));
                        refList.Remove(rem.v);
                        break;

                    case SetOp<T>.Clr clr:
                        orderedSet.Clear();
                        refSet.Clear();
                        refList.Clear();
                        break;

                    case SetOp<T>.Tst tst:
                        Assert.AreEqual(refSet.Contains(tst.v), orderedSet.Contains(tst.v));
                        break;
                }

                Assert.AreEqual(refList, new List<T>(orderedSet));
                foreach (var v in refList)
                    Assert.IsTrue(orderedSet.Contains(v));
            }
        }

        void ExecuteDictOperations<K, V>(DictOp<K, V>[] ops) where K : IEquatable<K>
        {
            ExecuteDictOperations(ops, false, false);
            ExecuteDictOperations(ops, true, false);
            ExecuteDictOperations(ops, false, true);
        }
        void ExecuteDictOperations<K, V>(DictOp<K, V>[] ops, bool addOverwrites, bool removeExtractsValue) where K : IEquatable<K>
        {
            LinkedList<KeyValuePair<K, V>> refList = new LinkedList<KeyValuePair<K, V>>();
            OrderedDictionary<K, V> dict = new OrderedDictionary<K,V>();
            Assert.AreEqual(refList, new List<KeyValuePair<K, V>>(dict));

            bool RefAdd(K key, V value)
            {
                LinkedListNode<KeyValuePair<K, V>> node;
                for (node = refList.First; node != null; node = node.Next)
                {
                    if ((node.Value.Key == null && key == null)
                        || (node.Value.Key != null && key != null && node.Value.Key.Equals(key)))
                    {
                        if (addOverwrites)
                        {
                            node.Value = new KeyValuePair<K, V>(key, value);
                        }
                        return false;
                    }
                    else
                        continue;
                }
                refList.AddLast(new KeyValuePair<K, V>(key, value));
                return true;
            }
            bool RefRemove(K key, out V extracted)
            {
                LinkedListNode<KeyValuePair<K, V>> node;
                for (node = refList.First; node != null; node = node.Next)
                {
                    if ((node.Value.Key == null && key == null)
                        || (node.Value.Key != null && key != null && node.Value.Key.Equals(key)))
                    {
                        extracted = node.Value.Value;
                        refList.Remove(node);
                        return true;
                    }
                    else
                        continue;
                }
                extracted = default;
                return false;
            }
            bool RefContains(K key)
            {
                LinkedListNode<KeyValuePair<K, V>> node;
                for (node = refList.First; node != null; node = node.Next)
                {
                    if (node.Value.Key == null && key == null)
                        return true;
                    if (node.Value.Key != null && key != null && node.Value.Key.Equals(key))
                        return true;
                }
                return false;
            }
            V RefGetValueOrDefault(K key)
            {
                LinkedListNode<KeyValuePair<K, V>> node;
                for (node = refList.First; node != null; node = node.Next)
                {
                    if (node.Value.Key == null && key == null)
                        return node.Value.Value;
                    if (node.Value.Key != null && key != null && node.Value.Key.Equals(key))
                        return node.Value.Value;
                }
                return default;
            }

            foreach (var op in ops)
            {
                switch (op)
                {
                    case DictOp<K, V>.Add add:
                        Assert.AreEqual(RefAdd(add.k, add.v), addOverwrites ? dict.AddOrReplace(add.k, add.v) : dict.AddIfAbsent(add.k, add.v));
                        break;

                    case DictOp<K, V>.Rem rem:
                        if (removeExtractsValue)
                        {
                            bool refRemoved = RefRemove(rem.k, out V refExtract);
                            bool odRemoved = dict.Remove(rem.k, out V odExtract);
                            Assert.AreEqual(refRemoved, odRemoved);
                            if (refRemoved)
                                Assert.AreEqual(refExtract, odExtract);
                        }
                        else
                            Assert.AreEqual(RefRemove(rem.k, out _), dict.Remove(rem.k));
                        break;

                    case DictOp<K, V>.Clr clr:
                        refList.Clear();
                        dict.Clear();
                        break;

                    case DictOp<K, V>.Tst tst:
                        Assert.AreEqual(RefContains(tst.k), dict.ContainsKey(tst.k));
                        Assert.AreEqual(RefGetValueOrDefault(tst.k), dict.GetValueOrDefault(tst.k));
                        break;
                }

                Assert.AreEqual(refList, new List<KeyValuePair<K, V>>(dict));
                foreach (var v in refList)
                {
                    Assert.IsTrue(dict.ContainsKey(v.Key));
                    Assert.IsTrue(dict.TryGetValue(v.Key, out V outVal));
                    Assert.IsTrue(dict[v.Key].Equals(v.Value));
                    Assert.IsTrue(outVal.Equals(v.Value));
                }
            }
        }

        [Test]
        public void TestOrderedSetWithValueTypes()
        {
            ExecuteSetOperations<int>(new SetOp<int>[] { new SetOp<int>.Add { v = 1 } });
            ExecuteSetOperations<int>(new SetOp<int>[] { new SetOp<int>.Add { v = 1 }, new SetOp<int>.Add { v = 1 } });
            ExecuteSetOperations<int>(new SetOp<int>[] { new SetOp<int>.Add { v = 1 }, new SetOp<int>.Add { v = 2 } });
            ExecuteSetOperations<int>(new SetOp<int>[] { new SetOp<int>.Add { v = 1 }, new SetOp<int>.Add { v = 2 }, new SetOp<int>.Add { v = 3 }, new SetOp<int>.Add { v = 4 } });
            ExecuteSetOperations<int>(new SetOp<int>[] { new SetOp<int>.Add { v = 4 }, new SetOp<int>.Add { v = 3 }, new SetOp<int>.Add { v = 2 }, new SetOp<int>.Add { v = 1 }, new SetOp<int>.Add { v = 8 } });

            ExecuteSetOperations<int>(new SetOp<int>[] { new SetOp<int>.Rem { v = 1 } });
            ExecuteSetOperations<int>(new SetOp<int>[] { new SetOp<int>.Add { v = 1 }, new SetOp<int>.Rem { v = 1 }, new SetOp<int>.Add { v = 1 } });
            ExecuteSetOperations<int>(new SetOp<int>[] { new SetOp<int>.Add { v = 1 }, new SetOp<int>.Rem { v = 1 }, new SetOp<int>.Rem { v = 1 } });
            ExecuteSetOperations<int>(new SetOp<int>[] { new SetOp<int>.Add { v = 1 }, new SetOp<int>.Rem { v = 2 } });
            ExecuteSetOperations<int>(new SetOp<int>[] { new SetOp<int>.Add { v = 1 }, new SetOp<int>.Add { v = 2 }, new SetOp<int>.Rem { v = 1 }, new SetOp<int>.Rem { v = 2 } });
            ExecuteSetOperations<int>(new SetOp<int>[] { new SetOp<int>.Add { v = 1 }, new SetOp<int>.Add { v = 2 }, new SetOp<int>.Rem { v = 2 }, new SetOp<int>.Rem { v = 1 } });

            ExecuteSetOperations<int>(new SetOp<int>[] { new SetOp<int>.Clr { } });
            ExecuteSetOperations<int>(new SetOp<int>[] { new SetOp<int>.Add { v = 1 }, new SetOp<int>.Clr { }, new SetOp<int>.Clr { } });
            ExecuteSetOperations<int>(new SetOp<int>[] { new SetOp<int>.Clr { }, new SetOp<int>.Clr { }, new SetOp<int>.Add { v = 1 } });
            ExecuteSetOperations<int>(new SetOp<int>[] { new SetOp<int>.Clr { }, new SetOp<int>.Clr { }, new SetOp<int>.Rem { v = 1 } });

            for (int testNdx = 0; testNdx < 1000; ++testNdx)
            {
                System.Random rnd = new System.Random(1234 + testNdx);

                SetOp<int>[] ops = new SetOp<int>[rnd.Next(3, 50)];
                for (int ndx = 0; ndx < ops.Length; ++ndx)
                {
                    int roll = rnd.Next(0, 100);

                    if (roll >= 0 && roll <= 29)
                        ops[ndx] = new SetOp<int>.Add { v = rnd.Next(0, 64) };
                    else if (roll >= 30 && roll <= 59)
                        ops[ndx] = new SetOp<int>.Rem { v = rnd.Next(0, 64) };
                    else if (roll >= 60 && roll <= 89)
                        ops[ndx] = new SetOp<int>.Tst { v = rnd.Next(0, 64) };
                    else
                        ops[ndx] = new SetOp<int>.Clr { };
                }
                ExecuteSetOperations<int>(ops);
            }
        }
        [Test]
        public void TestOrderedSetWithReferenceTypes()
        {
            ExecuteSetOperations<string>(new SetOp<string>[] { new SetOp<string>.Add { v = "1" } });
            ExecuteSetOperations<string>(new SetOp<string>[] { new SetOp<string>.Add { v = "1" }, new SetOp<string>.Add { v = "1" } });
            ExecuteSetOperations<string>(new SetOp<string>[] { new SetOp<string>.Add { v = "1" }, new SetOp<string>.Add { v = "2" } });
            ExecuteSetOperations<string>(new SetOp<string>[] { new SetOp<string>.Add { v = "1" }, new SetOp<string>.Add { v = "2" }, new SetOp<string>.Add { v = "3" }, new SetOp<string>.Add { v = "4" } });
            ExecuteSetOperations<string>(new SetOp<string>[] { new SetOp<string>.Add { v = "4" }, new SetOp<string>.Add { v = "3" }, new SetOp<string>.Add { v = "2" }, new SetOp<string>.Add { v = "1" }, new SetOp<string>.Add { v = "8" } });

            ExecuteSetOperations<string>(new SetOp<string>[] { new SetOp<string>.Rem { v = "1" } });
            ExecuteSetOperations<string>(new SetOp<string>[] { new SetOp<string>.Add { v = "1" }, new SetOp<string>.Rem { v = "1" }, new SetOp<string>.Add { v = "1" } });
            ExecuteSetOperations<string>(new SetOp<string>[] { new SetOp<string>.Add { v = "1" }, new SetOp<string>.Rem { v = "1" }, new SetOp<string>.Rem { v = "1" } });
            ExecuteSetOperations<string>(new SetOp<string>[] { new SetOp<string>.Add { v = "1" }, new SetOp<string>.Rem { v = "2" } });
            ExecuteSetOperations<string>(new SetOp<string>[] { new SetOp<string>.Add { v = "1" }, new SetOp<string>.Add { v = "2" }, new SetOp<string>.Rem { v = "1" }, new SetOp<string>.Rem { v = "2" } });
            ExecuteSetOperations<string>(new SetOp<string>[] { new SetOp<string>.Add { v = "1" }, new SetOp<string>.Add { v = "2" }, new SetOp<string>.Rem { v = "2" }, new SetOp<string>.Rem { v = "1" } });

            ExecuteSetOperations<string>(new SetOp<string>[] { new SetOp<string>.Clr { } });
            ExecuteSetOperations<string>(new SetOp<string>[] { new SetOp<string>.Add { v = "1" }, new SetOp<string>.Clr { }, new SetOp<string>.Clr { } });
            ExecuteSetOperations<string>(new SetOp<string>[] { new SetOp<string>.Clr { }, new SetOp<string>.Clr { }, new SetOp<string>.Add { v = "1" } });
            ExecuteSetOperations<string>(new SetOp<string>[] { new SetOp<string>.Clr { }, new SetOp<string>.Clr { }, new SetOp<string>.Rem { v = "1" } });

            for (int testNdx = 0; testNdx < 1000; ++testNdx)
            {
                System.Random rnd = new System.Random(1234 + testNdx);

                SetOp<string>[] ops = new SetOp<string>[rnd.Next(3, 50)];
                for (int ndx = 0; ndx < ops.Length; ++ndx)
                {
                    int roll = rnd.Next(0, 100);

                    if (roll >= 0 && roll <= 29)
                        ops[ndx] = new SetOp<string>.Add { v = Invariant($"v{rnd.Next(0, 64)}") };
                    else if (roll >= 30 && roll <= 59)
                        ops[ndx] = new SetOp<string>.Rem { v = Invariant($"v{rnd.Next(0, 64)}") };
                    else if (roll >= 60 && roll <= 89)
                        ops[ndx] = new SetOp<string>.Tst { v = Invariant($"v{rnd.Next(0, 64)}") };
                    else
                        ops[ndx] = new SetOp<string>.Clr { };
                }
                ExecuteSetOperations<string>(ops);
            }
        }
        [Test]
        public void TestOrderedSetWithNulls()
        {
            ExecuteSetOperations<string>(new SetOp<string>[] { new SetOp<string>.Add { v = null } });
            ExecuteSetOperations<string>(new SetOp<string>[] { new SetOp<string>.Add { v = null }, new SetOp<string>.Add { v = null } });
            ExecuteSetOperations<string>(new SetOp<string>[] { new SetOp<string>.Add { v = null }, new SetOp<string>.Rem { v = null } });
        }
        [Test]
        public void TestOrderedSetCapacity()
        {
            // allocs at least the desired amount
            foreach (int capacity in new int[] { 0, 1, 2, 3, 4, 5, 123, 1023, 1024, 1025, 1239 })
            {
                OrderedSet<int> orderedSet = new OrderedSet<int>(capacity);
                Assert.LessOrEqual(capacity, orderedSet.Capacity);
            }
        }
        [Test]
        public void TestOrderedSetCtors()
        {
            Assert.AreEqual(new List<int>(), new List<int>(new OrderedSet<int>()));
            Assert.AreEqual(new List<int>(), new List<int>(new OrderedSet<int>(1)));
            Assert.AreEqual(new List<int>() { 1, 2 }, new List<int>(new OrderedSet<int>(new int[] { 1, 2, 1 })));
        }
        [Test]
        public void TestOrderedSetWithConflicts()
        {
            List<SetOp<BadHashKey>> ops = new List<SetOp<BadHashKey>>();
            int val;

            val = 0;
            while (true)
            {
                ops.Add(new SetOp<BadHashKey>.Add { v = new BadHashKey(val.ToString(CultureInfo.InvariantCulture)) });
                val = (val + 13) % 500;
                if (val == 0)
                    break;
            }
            val = 0;
            while (true)
            {
                ops.Add(new SetOp<BadHashKey>.Rem { v = new BadHashKey(val.ToString(CultureInfo.InvariantCulture)) });
                val = (val + 57) % 500;
                if (val == 0)
                    break;
            }
            ExecuteSetOperations<BadHashKey>(ops.ToArray());
        }
        [Test]
        public void TestOrderedSetConcurrentModification()
        {
            Assert.Catch<InvalidOperationException>(() =>
            {
                // attempt to in-place filter
                OrderedSet<int> set = new OrderedSet<int>() { 1, 2 };
                foreach (var v in set)
                {
                    if ((v % 2) == 0)
                        set.Remove(v);
                }
            });

            Assert.Catch<InvalidOperationException>(() =>
            {
                // attempt to in-place extend
                OrderedSet<int> set = new OrderedSet<int>() { 1, 2, 3 };
                foreach (var v in set)
                {
                    if (v < 4)
                        set.Add(v + 1);
                }
            });

            Assert.Catch<InvalidOperationException>(() =>
            {
                // attempt to clear
                OrderedSet<int> set = new OrderedSet<int>() { 1, 2, 3 };
                foreach (var v in set)
                {
                    if (v == 2)
                        set.Clear();
                }
            });

            Assert.Catch<InvalidOperationException>(() =>
            {
                // attempt to remove
                OrderedSet<int> set = new OrderedSet<int>() { 1, 2, 3 };
                foreach (var v in set)
                {
                    set.RemoveWhere(x => x == 2);
                }
            });
        }
        [Test]
        public void TestOrderedSetRemoveWhere()
        {
            // Resulting set
            {
                OrderedSet<int> set = new OrderedSet<int>() { 1, 2, 3 };
                Assert.AreEqual(3, set.RemoveWhere(v => v < 4));
                Assert.AreEqual(new List<int>(), new List<int>(set));
            }
            {
                OrderedSet<int> set = new OrderedSet<int>() { 1, 2, 3 };
                Assert.AreEqual(0, set.RemoveWhere(v => v > 4));
                Assert.AreEqual(new List<int>() { 1, 2, 3 }, new List<int>(set));
            }
            {
                OrderedSet<int> set = new OrderedSet<int>() { 1, 2, 3 };
                Assert.AreEqual(1, set.RemoveWhere(v => (v % 2) == 0));
                Assert.AreEqual(new List<int>() { 1, 3 }, new List<int>(set));
            }
            {
                OrderedSet<int> set = new OrderedSet<int>() { 1, 2, 3 };
                Assert.AreEqual(2, set.RemoveWhere(v => (v % 2) != 0));
                Assert.AreEqual(new List<int>() { 2 }, new List<int>(set));
            }
            // Iteration order
            {
                OrderedSet<int> set = new OrderedSet<int>();
                for (int i = 0; i < 1000; ++i)
                    set.Add(i);

                List<int> desiredOrder = new List<int>();
                for (int i = 0; i < 1000; ++i)
                    desiredOrder.Add(i);

                List<int> observedOrder = new List<int>();
                int numRemoved = set.RemoveWhere((int v) =>
                {
                    observedOrder.Add(v);
                    return (v % 3) != 0;
                });

                Assert.AreEqual(666, numRemoved);
                Assert.AreEqual(desiredOrder, observedOrder);
            }

        }
        [Test]
        public void TestOrderedSetISerializable()
        {
            foreach (int initialCapacity in new int[] { 0, 1, 4, 5 })
            {
                OrderedSet<string> original = new OrderedSet<string>(initialCapacity) { "1", "A", null, "foo", "Bar" };

                // Using BinaryFormatter is Dangerous, but if somebody wants to use it, it better work.
                #pragma warning disable SYSLIB0011

                BinaryFormatter fmt = new BinaryFormatter();
                MemoryStream stream = new MemoryStream();
                fmt.Serialize(stream, original);
                stream.Seek(0, SeekOrigin.Begin);
                OrderedSet<string> deserialized = (OrderedSet<string>)fmt.Deserialize(stream);

                Assert.AreEqual(original, deserialized);

                #pragma warning restore SYSLIB0011
            }
        }
        [Test]
        public void TestOrderedSetPrettyPrintable()
        {
            Assert.AreEqual(PrettyPrint.Compact(new SortedSet<int>()).ToString(), PrettyPrint.Compact(new OrderedSet<int>()).ToString());
            Assert.AreEqual(PrettyPrint.Compact(new SortedSet<int>() { 1 }).ToString(), PrettyPrint.Compact(new OrderedSet<int>() { 1 }).ToString());
            Assert.AreEqual(PrettyPrint.Compact(new SortedSet<int>() { 1, 2 }).ToString(), PrettyPrint.Compact(new OrderedSet<int>() { 1, 2, 1 }).ToString());

            Assert.AreEqual(PrettyPrint.Compact(new SortedSet<string>(StringComparer.InvariantCulture)).ToString(), PrettyPrint.Compact(new OrderedSet<string>()).ToString());
            Assert.AreEqual(PrettyPrint.Compact(new SortedSet<string>(StringComparer.InvariantCulture) { "1" }).ToString(), PrettyPrint.Compact(new OrderedSet<string>() { "1" }).ToString());
            Assert.AreEqual(PrettyPrint.Compact(new SortedSet<string>(StringComparer.InvariantCulture) { "1", "2" }).ToString(), PrettyPrint.Compact(new OrderedSet<string>() { "1", "2", "1" }).ToString());
            Assert.AreEqual(PrettyPrint.Compact(new SortedSet<string>(StringComparer.InvariantCulture) { null, "2" }).ToString(), PrettyPrint.Compact(new OrderedSet<string>() { null, "2" }).ToString());
        }

        enum TestEnum
        {
            Zero,
            One,
            Two,
            Twenty = 20
        }

        [Test]
        [Retry(100)] // First runs provoke some internal allocs.
        public void TestOrderedSetAllocations()
        {
            void NoAllocationsIn(Action action)
            {
                long initial = GC.GetAllocatedBytesForCurrentThread();
                action.Invoke();
                Assert.IsTrue(initial == GC.GetAllocatedBytesForCurrentThread());
            }
            Predicate<int> IsOdd = v => v % 2 == 0;
            Predicate<string> IsEvenLength = v => (v?.Length ?? 0) % 2 != 0;
            Predicate<TestEnum> IsOne = v => v == TestEnum.One;
            OrderedSet<int> intSet = new OrderedSet<int>() { 1, 2, 3, 4, 5, 6 };
            OrderedSet<string> boxSet = new OrderedSet<string>() { "1", "2", null, "fo", "bar" };
            OrderedSet<TestEnum> enumSet = new OrderedSet<TestEnum>() { TestEnum.One, TestEnum.Two, TestEnum.Zero };

            NoAllocationsIn(() => intSet.Contains(2));
            NoAllocationsIn(() => intSet.Contains(20));
            NoAllocationsIn(() => intSet.Remove(2));
            NoAllocationsIn(() => intSet.Remove(20));
            NoAllocationsIn(() => intSet.Add(1));
            NoAllocationsIn(() => intSet.RemoveWhere(IsOdd));
            NoAllocationsIn(() => { foreach (int v in intSet); } );
            int[] intArr = new int[16];
            NoAllocationsIn(() => intSet.CopyTo(intArr, 0));
            NoAllocationsIn(() => intSet.First());
            NoAllocationsIn(() => intSet.ElementAt(0));
            NoAllocationsIn(() => intSet.Clear());

            string str1 = "1";
            string str2 = "2";
            string str20 = "20";
            NoAllocationsIn(() => boxSet.Contains(str2));
            NoAllocationsIn(() => boxSet.Contains(str20));
            NoAllocationsIn(() => boxSet.Remove(str2));
            NoAllocationsIn(() => boxSet.Remove(str20));
            NoAllocationsIn(() => boxSet.Add(str1));
            NoAllocationsIn(() => boxSet.RemoveWhere(IsEvenLength));
            NoAllocationsIn(() => { foreach (string v in boxSet) ; });
            string[] boxArr = new string[16];
            NoAllocationsIn(() => boxSet.CopyTo(boxArr, 0));
            NoAllocationsIn(() => boxSet.Clear());

            TestEnum enum1 = TestEnum.One;
            TestEnum enum2 = TestEnum.Two;
            TestEnum enum20 = TestEnum.Twenty;
            NoAllocationsIn(() => enumSet.Contains(enum2));
            NoAllocationsIn(() => enumSet.Contains(enum20));
            NoAllocationsIn(() => enumSet.Remove(enum2));
            NoAllocationsIn(() => enumSet.Remove(enum20));
            NoAllocationsIn(() => enumSet.Add(enum1));
            NoAllocationsIn(() => enumSet.RemoveWhere(IsOne));
            NoAllocationsIn(() => { foreach (TestEnum v in enumSet) ; });
            TestEnum[] enumArr = new TestEnum[16];
            NoAllocationsIn(() => enumSet.CopyTo(enumArr, 0));
            NoAllocationsIn(() => enumSet.Clear());
        }

        [Test]
        public void TestOrderedSetFirst()
        {
            {
                OrderedSet<int> set = new OrderedSet<int>() { 1, 2, 3 };
                Assert.AreEqual(1, set.First());
                set.Remove(1);
                Assert.AreEqual(2, set.First());
            }
            {
                OrderedSet<int> set = new OrderedSet<int>();
                Assert.Throws<InvalidOperationException>(() => _ = set.First());
            }
        }
        [Test]
        public void TestOrderedSetElementAt()
        {
            {
                OrderedSet<int> set = new OrderedSet<int>() { 1, 2, 3 };
                Assert.AreEqual(1, set.ElementAt(0));
                Assert.AreEqual(2, set.ElementAt(1));
                Assert.AreEqual(3, set.ElementAt(2));
            }
            {
                OrderedSet<int> set = new OrderedSet<int>();
                Assert.Throws<ArgumentOutOfRangeException>(() => _ = set.ElementAt(0));
                set.Add(1);
                Assert.Throws<ArgumentOutOfRangeException>(() => _ = set.ElementAt(1));
                Assert.Throws<ArgumentOutOfRangeException>(() => _ = set.ElementAt(-1));
                Assert.Throws<ArgumentOutOfRangeException>(() => _ = set.ElementAt(int.MaxValue));
            }
        }

        [Test]
        public void TestOrderedDictWithValueKeys()
        {
            ExecuteDictOperations<int, string>(new DictOp<int, string>[]
            {
                new DictOp<int, string>.Add { k = 1, v = "a" }
            });
            ExecuteDictOperations<int, string>(new DictOp<int, string>[]
            {
                new DictOp<int, string>.Add { k = 1, v = "b" },
                new DictOp<int, string>.Add { k = 1, v = "f" }
            });
            ExecuteDictOperations<int, string>(new DictOp<int, string>[]
            {
                new DictOp<int, string>.Add { k = 1, v = "c" },
                new DictOp<int, string>.Add { k = 2, v = "g" }
            });
            ExecuteDictOperations<int, string>(new DictOp<int, string>[]
            {
                new DictOp<int, string>.Add { k = 1, v = "d" },
                new DictOp<int, string>.Add { k = 2, v = "h" },
                new DictOp<int, string>.Add { k = 3, v = "i" },
                new DictOp<int, string>.Add { k = 4, v = "j" }
            });
            ExecuteDictOperations<int, string>(new DictOp<int, string>[]
            {
                new DictOp<int, string>.Add { k = 4, v = "e" },
                new DictOp<int, string>.Add { k = 3, v = "o" },
                new DictOp<int, string>.Add { k = 2, v = "n" },
                new DictOp<int, string>.Add { k = 1, v = "m" },
                new DictOp<int, string>.Add { k = 8, v = "e" }
            });

            ExecuteDictOperations<int, string>(new DictOp<int, string>[]
            {
                new DictOp<int, string>.Rem { k = 1 }
            });
            ExecuteDictOperations<int, string>(new DictOp<int, string>[]
            {
                new DictOp<int, string>.Add { k = 1, v = "o" },
                new DictOp<int, string>.Rem { k = 1 },
                new DictOp<int, string>.Add { k = 1, v = "m" }
            });
            ExecuteDictOperations<int, string>(new DictOp<int, string>[]
            {
                new DictOp<int, string>.Add { k = 1, v = "m" },
                new DictOp<int, string>.Rem { k = 1 },
                new DictOp<int, string>.Rem { k = 1 }
            });
            ExecuteDictOperations<int, string>(new DictOp<int, string>[]
            {
                new DictOp<int, string>.Add { k = 1, v = "m" },
                new DictOp<int, string>.Rem { k = 2 }
            });
            ExecuteDictOperations<int, string>(new DictOp<int, string>[]
            {
                new DictOp<int, string>.Add { k = 1, v = "m" },
                new DictOp<int, string>.Add { k = 2, v = "n" },
                new DictOp<int, string>.Rem { k = 1 },
                new DictOp<int, string>.Rem { k = 2 }
            });
            ExecuteDictOperations<int, string>(new DictOp<int, string>[]
            {
                new DictOp<int, string>.Add { k = 1, v = "m" },
                new DictOp<int, string>.Add { k = 2, v = "n" },
                new DictOp<int, string>.Rem { k = 2 },
                new DictOp<int, string>.Rem { k = 1 }
            });

            ExecuteDictOperations<int, string>(new DictOp<int, string>[]
            {
                new DictOp<int, string>.Clr { }
            });
            ExecuteDictOperations<int, string>(new DictOp<int, string>[]
            {
                new DictOp<int, string>.Add { k = 1, v = "v" },
                new DictOp<int, string>.Clr { },
                new DictOp<int, string>.Clr { }
            });
            ExecuteDictOperations<int, string>(new DictOp<int, string>[]
            {
                new DictOp<int, string>.Clr { },
                new DictOp<int, string>.Clr { },
                new DictOp<int, string>.Add { k = 1, v = "v" }
            });
            ExecuteDictOperations<int, string>(new DictOp<int, string>[]
            {
                new DictOp<int, string>.Clr { },
                new DictOp<int, string>.Clr { },
                new DictOp<int, string>.Rem { k = 1 }
            });

            for (int testNdx = 0; testNdx < 1000; ++testNdx)
            {
                System.Random rnd = new System.Random(1234 + testNdx);

                DictOp<int, string>[] ops = new DictOp<int, string>[rnd.Next(3, 50)];
                for (int ndx = 0; ndx < ops.Length; ++ndx)
                {
                    int roll = rnd.Next(0, 100);

                    if (roll >= 0 && roll <= 29)
                        ops[ndx] = new DictOp<int, string>.Add { k = rnd.Next(0, 64), v = rnd.Next().ToString(CultureInfo.InvariantCulture) };
                    else if (roll >= 30 && roll <= 59)
                        ops[ndx] = new DictOp<int, string>.Rem { k = rnd.Next(0, 64) };
                    else if (roll >= 60 && roll <= 89)
                        ops[ndx] = new DictOp<int, string>.Tst { k = rnd.Next(0, 64) };
                    else
                        ops[ndx] = new DictOp<int, string>.Clr { };
                }
                ExecuteDictOperations<int, string>(ops);
            }
        }
        [Test]
        public void TestOrderedDictWithReferenceKeys()
        {
            ExecuteDictOperations<string, string>(new DictOp<string, string>[]
            {
                new DictOp<string, string>.Add { k = "1", v = "a" }
            });
            ExecuteDictOperations<string, string>(new DictOp<string, string>[]
            {
                new DictOp<string, string>.Add { k = "1", v = "b" },
                new DictOp<string, string>.Add { k = "1", v = "f" }
            });
            ExecuteDictOperations<string, string>(new DictOp<string, string>[]
            {
                new DictOp<string, string>.Add { k = "1", v = "c" },
                new DictOp<string, string>.Add { k = "2", v = "g" }
            });
            ExecuteDictOperations<string, string>(new DictOp<string, string>[]
            {
                new DictOp<string, string>.Add { k = "1", v = "d" },
                new DictOp<string, string>.Add { k = "2", v = "h" },
                new DictOp<string, string>.Add { k = "3", v = "i" },
                new DictOp<string, string>.Add { k = "4", v = "j" }
            });
            ExecuteDictOperations<string, string>(new DictOp<string, string>[]
            {
                new DictOp<string, string>.Add { k = "4", v = "e" },
                new DictOp<string, string>.Add { k = "3", v = "o" },
                new DictOp<string, string>.Add { k = "2", v = "n" },
                new DictOp<string, string>.Add { k = "1", v = "m" },
                new DictOp<string, string>.Add { k = "8", v = "e" }
            });

            ExecuteDictOperations<string, string>(new DictOp<string, string>[]
            {
                new DictOp<string, string>.Rem { k = "1" }
            });
            ExecuteDictOperations<string, string>(new DictOp<string, string>[]
            {
                new DictOp<string, string>.Add { k = "1", v = "o" },
                new DictOp<string, string>.Rem { k = "1" },
                new DictOp<string, string>.Add { k = "1", v = "m" }
            });
            ExecuteDictOperations<string, string>(new DictOp<string, string>[]
            {
                new DictOp<string, string>.Add { k = "1", v = "m" },
                new DictOp<string, string>.Rem { k = "1" },
                new DictOp<string, string>.Rem { k = "1" }
            });
            ExecuteDictOperations<string, string>(new DictOp<string, string>[]
            {
                new DictOp<string, string>.Add { k = "1", v = "m" },
                new DictOp<string, string>.Rem { k = "2" }
            });
            ExecuteDictOperations<string, string>(new DictOp<string, string>[]
            {
                new DictOp<string, string>.Add { k = "1", v = "m" },
                new DictOp<string, string>.Add { k = "2", v = "n" },
                new DictOp<string, string>.Rem { k = "1" },
                new DictOp<string, string>.Rem { k = "2" }
            });
            ExecuteDictOperations<string, string>(new DictOp<string, string>[]
            {
                new DictOp<string, string>.Add { k = "1", v = "m" },
                new DictOp<string, string>.Add { k = "2", v = "n" },
                new DictOp<string, string>.Rem { k = "2" },
                new DictOp<string, string>.Rem { k = "1" }
            });

            ExecuteDictOperations<string, string>(new DictOp<string, string>[]
            {
                new DictOp<string, string>.Clr { }
            });
            ExecuteDictOperations<string, string>(new DictOp<string, string>[]
            {
                new DictOp<string, string>.Add { k = "1", v = "v" },
                new DictOp<string, string>.Clr { },
                new DictOp<string, string>.Clr { }
            });
            ExecuteDictOperations<string, string>(new DictOp<string, string>[]
            {
                new DictOp<string, string>.Clr { },
                new DictOp<string, string>.Clr { },
                new DictOp<string, string>.Add { k = "1", v = "v" }
            });
            ExecuteDictOperations<string, string>(new DictOp<string, string>[]
            {
                new DictOp<string, string>.Clr { },
                new DictOp<string, string>.Clr { },
                new DictOp<string, string>.Rem { k = "1" }
            });

            for (int testNdx = 0; testNdx < 1000; ++testNdx)
            {
                System.Random rnd = new System.Random(1234 + testNdx);

                DictOp<string, string>[] ops = new DictOp<string, string>[rnd.Next(3, 50)];
                for (int ndx = 0; ndx < ops.Length; ++ndx)
                {
                    int roll = rnd.Next(0, 100);

                    if (roll >= 0 && roll <= 29)
                        ops[ndx] = new DictOp<string, string>.Add { k = Invariant($"v{rnd.Next(0, 64)}"), v = rnd.Next().ToString(CultureInfo.InvariantCulture) };
                    else if (roll >= 30 && roll <= 59)
                        ops[ndx] = new DictOp<string, string>.Rem { k = Invariant($"v{rnd.Next(0, 64)}") };
                    else if (roll >= 60 && roll <= 89)
                        ops[ndx] = new DictOp<string, string>.Tst { k = Invariant($"v{rnd.Next(0, 64)}") };
                    else
                        ops[ndx] = new DictOp<string, string>.Clr { };
                }
                ExecuteDictOperations(ops);
            }
        }
        [Test]
        public void TestOrderedDictWithNulls()
        {
            ExecuteDictOperations(new DictOp<string, string>[] { new DictOp<string, string>.Add { k = null, v = "v" } });
            ExecuteDictOperations(new DictOp<string, string>[] { new DictOp<string, string>.Add { k = null, v = "v" }, new DictOp<string, string>.Add { k = null, v = "w" } });
            ExecuteDictOperations(new DictOp<string, string>[] { new DictOp<string, string>.Add { k = null, v = "v" }, new DictOp<string, string>.Rem { k = null } });
        }
        [Test]
        public void TestOrderedDictCapacity()
        {
            // allocs at least the desired amount
            foreach (int capacity in new int[] { 0, 1, 2, 3, 4, 5, 123, 1023, 1024, 1025, 1239 })
            {
                OrderedDictionary<int, int> orderedDict = new OrderedDictionary<int, int>(capacity);
                Assert.LessOrEqual(capacity, orderedDict.Capacity);
            }
        }
        [Test]
        public void TestOrderedDictCtors()
        {
            Assert.AreEqual(new List<KeyValuePair<int, int>>(), new List<KeyValuePair<int, int>>(new OrderedDictionary<int, int>()));
            Assert.AreEqual(new List<KeyValuePair<int, int>>(), new List<KeyValuePair<int, int>>(new OrderedDictionary<int, int>(1)));
            Assert.Throws<ArgumentException>(() =>
                new List<KeyValuePair<int, int>>(new OrderedDictionary<int, int>(new KeyValuePair<int, int>[]
                {
                    new KeyValuePair<int, int>(1, 2),
                    new KeyValuePair<int, int>(2, 3),
                    new KeyValuePair<int, int>(1, 2),
                })));
            Assert.AreEqual(
                new List<KeyValuePair<int, int>>()
                {
                    new KeyValuePair<int, int>(1, 2),
                    new KeyValuePair<int, int>(2, 3)
                },
                new List<KeyValuePair<int, int>>(new OrderedDictionary<int, int>()
                {
                    [1] = 2,
                    [2] = 3,
                }));
            Assert.AreEqual(
                new List<KeyValuePair<int, int>>()
                {
                    new KeyValuePair<int, int>(1, 2),
                    new KeyValuePair<int, int>(2, 3)
                },
                new List<KeyValuePair<int, int>>(new OrderedDictionary<int, int>()
                {
                    { 1, 2 },
                    { 2, 3 }
                }));
        }
        [Test]
        public void TestOrderedDictWithConflicts()
        {
            List<DictOp<BadHashKey, string>> ops = new List<DictOp<BadHashKey, string>>();
            int val;

            val = 0;
            while (true)
            {
                ops.Add(new DictOp<BadHashKey, string>.Add { k = new BadHashKey(val.ToString(CultureInfo.InvariantCulture)), v = val.ToString(CultureInfo.InvariantCulture) });
                val = (val + 13) % 100;
                if (val == 0)
                    break;
            }
            val = 0;
            while (true)
            {
                ops.Add(new DictOp<BadHashKey, string>.Rem { k = new BadHashKey(val.ToString(CultureInfo.InvariantCulture)) });
                val = (val + 57) % 100;
                if (val == 0)
                    break;
            }
            ExecuteDictOperations(ops.ToArray());
        }
        [Test]
        public void TestOrderedDictConcurrentModification()
        {
            Assert.Catch<InvalidOperationException>(() =>
            {
                // attempt to in-place filter
                OrderedDictionary<int, int> dict = new OrderedDictionary<int, int>() { [1] = 2, [0] = 2 };
                foreach (var v in dict)
                {
                    if ((v.Key % 2) == 0)
                        dict.Remove(v.Key);
                }
            });

            Assert.Catch<InvalidOperationException>(() =>
            {
                // attempt to in-place extend
                OrderedDictionary<int, int> dict = new OrderedDictionary<int, int>() { [1] = 2, [0] = 2 };
                foreach (var v in dict)
                {
                    if (v.Key < 4)
                        dict.AddIfAbsent(v.Key + 1, 5);
                }
            });

            Assert.Catch<InvalidOperationException>(() =>
            {
                // attempt to clear
                OrderedDictionary<int, int> dict = new OrderedDictionary<int, int>() { [1] = 2, [0] = 2 };
                foreach (var v in dict)
                {
                    if (v.Key == 0)
                        dict.Clear();
                }
            });

            Assert.Catch<InvalidOperationException>(() =>
            {
                // attempt to remove
                OrderedDictionary<int, int> dict = new OrderedDictionary<int, int>() { [1] = 2, [0] = 2 };
                foreach (var v in dict)
                {
                    dict.RemoveWhere(kv => kv.Key == 2);
                }
            });

            Assert.Catch<InvalidOperationException>(() =>
            {
                // attempt to use keys
                OrderedDictionary<int, int> dict = new OrderedDictionary<int, int>() { [1] = 2, [0] = 3 };
                foreach (var v in dict.Keys)
                {
                    if (v % 2 != 0)
                        dict.Remove(v);
                }
            });

            Assert.Catch<InvalidOperationException>(() =>
            {
                // unsafe pattern: dict[] will modify container if key is not already present. Prevent any write access.
                OrderedDictionary<int, int> dict = new OrderedDictionary<int, int>() { [1] = 2, [0] = 3 };
                foreach (var kv in dict)
                {
                    if (kv.Value % 2 != 0)
                        dict[kv.Key] = -1;
                }
            });

            {
                OrderedDictionary<int, int> dict = new OrderedDictionary<int, int>() { [1] = 2, [0] = 3 };
                var keys = dict.Keys;
                dict.Remove(0);

                Assert.Catch<InvalidOperationException>(() =>
                {
                    var _ = keys.Count;
                });
                Assert.Catch<InvalidOperationException>(() =>
                {
                    foreach (var v in keys) { }
                });
                Assert.Catch<InvalidOperationException>(() =>
                {
                    keys.Contains(4);
                });
            }

            Assert.Catch<InvalidOperationException>(() =>
            {
                // attempt to use values
                OrderedDictionary<int, int> dict = new OrderedDictionary<int, int>() { [1] = 2, [0] = 3 };
                foreach (var v in dict.Values)
                {
                    if (v % 2 != 0)
                        dict.Remove(v);
                }
            });

            {
                OrderedDictionary<int, int> dict = new OrderedDictionary<int, int>() { [1] = 2, [0] = 3 };
                var values = dict.Values;
                dict.Remove(0);

                Assert.Catch<InvalidOperationException>(() =>
                {
                    var _ = values.Count;
                });
                Assert.Catch<InvalidOperationException>(() =>
                {
                    foreach (var v in values) { }
                });
                Assert.Catch<InvalidOperationException>(() =>
                {
                    values.Contains(4);
                });
            }
        }
        [Test]
        public void TestOrderedDictRemoveWhere()
        {
            // result
            {
                OrderedDictionary<int, int> dict = new OrderedDictionary<int, int>() { { 1, 1 }, { 2, 2 }, { 3, 3 } };
                Assert.AreEqual(3, dict.RemoveWhere(kv => kv.Key < 4));
                Assert.AreEqual(new List<KeyValuePair<int, int>>(), new List<KeyValuePair<int, int>>(dict));
            }
            {
                OrderedDictionary<int, int> dict = new OrderedDictionary<int, int>() { { 1, 1 }, { 2, 2 }, { 3, 3 } };
                Assert.AreEqual(0, dict.RemoveWhere(kv => kv.Key > 4));
                Assert.AreEqual(new List<int>() { 1, 2, 3 }, new List<int>(dict.Keys));
            }
            {
                OrderedDictionary<int, int> dict = new OrderedDictionary<int, int>() { { 1, 1 }, { 2, 2 }, { 3, 3 } };
                Assert.AreEqual(1, dict.RemoveWhere(kv => (kv.Key % 2) == 0));
                Assert.AreEqual(new List<int>() { 1, 3 }, new List<int>(dict.Values));
            }
            {
                OrderedDictionary<int, int> dict = new OrderedDictionary<int, int>() { { 1, 1 }, { 2, 2 }, { 3, 3 } };
                Assert.AreEqual(2, dict.RemoveWhere(kv => (kv.Key % 2) != 0));
                Assert.AreEqual(new List<int>() { 2 }, new List<int>(dict.Keys));
            }
            // Iteration order
            {
                OrderedDictionary<int, int> dict = new OrderedDictionary<int, int>();
                for (int i = 0; i < 1000; ++i)
                    dict.Add(i, i);

                List<int> desiredOrder = new List<int>();
                for (int i = 0; i < 1000; ++i)
                    desiredOrder.Add(i);

                List<int> observedOrder = new List<int>();
                int numRemoved = dict.RemoveWhere(kv =>
                {
                    observedOrder.Add(kv.Key);
                    return (kv.Key % 3) != 0;
                });

                Assert.AreEqual(666, numRemoved);
                Assert.AreEqual(desiredOrder, observedOrder);
            }

        }
        [Test]
        public void TestOrderedDictISerializable()
        {
            foreach (int initialCapacity in new int[] { 0, 1, 4, 5 })
            {
                OrderedDictionary<string, string> original = new OrderedDictionary<string, string>(initialCapacity)
                {
                    [null] = "1",
                    ["A"] = "2",
                    ["foo"] = null,
                    ["bar"] = "",
                    [""] = "Bar"
                };

                // Using BinaryFormatter is Dangerous, but if somebody wants to use it, it better work.
                #pragma warning disable SYSLIB0011

                BinaryFormatter fmt = new BinaryFormatter();
                MemoryStream stream = new MemoryStream();
                fmt.Serialize(stream, original);
                stream.Seek(0, SeekOrigin.Begin);
                OrderedDictionary<string, string> deserialized = (OrderedDictionary<string, string>)fmt.Deserialize(stream);

                Assert.AreEqual(original, deserialized);

                #pragma warning restore SYSLIB0011
            }
        }
        [Test]
        public void TestOrderedDictKeysValues()
        {
            OrderedDictionary<string, string> dict = new OrderedDictionary<string, string>()
            {
                [null] = "1",
                ["A"] = "2",
                ["foo"] = null,
                ["bar"] = "",
                [""] = "Bar"
            };
            Assert.IsTrue(dict.Keys.Contains(null));
            Assert.IsTrue(dict.Keys.Contains(""));
            Assert.IsTrue(dict.Keys.Contains("A"));
            Assert.IsFalse(dict.Keys.Contains("Bar"));
            Assert.AreEqual(new List<string>(){ null, "A", "foo", "bar", "" }, new List<string>(dict.Keys));

            Assert.IsTrue(dict.Values.Contains(null));
            Assert.IsTrue(dict.Values.Contains(""));
            Assert.IsFalse(dict.Values.Contains("A"));
            Assert.IsTrue(dict.Values.Contains("Bar"));
            Assert.AreEqual(new List<string>() { "1", "2", null, "", "Bar" }, new List<string>(dict.Values));
        }

        struct IntStruct { public int Int; }

        [Test]
        public void TestOrderedDictMutateValueValuesWithForeachRef()
        {
            OrderedDictionary<int, int> intDict = new OrderedDictionary<int, int>()
            {
                [1] = 1,
                [2] = 2,
                [50] = 50,
            };
            foreach (ref int value in intDict.Values)
                value += 2;
            foreach ((int key, int value) in intDict)
                Assert.AreEqual(key + 2, value);

            OrderedDictionary<int, IntStruct> structDict = new OrderedDictionary<int, IntStruct>()
            {
                [1] = new IntStruct() { Int = 1 },
                [2] = new IntStruct() { Int = 2 },
                [50] = new IntStruct() { Int = 50 },
            };
            foreach (ref IntStruct value in structDict.Values)
                value.Int += 2;
            foreach ((int key, IntStruct value) in structDict)
                Assert.AreEqual(key + 2, value.Int);
        }
        [Test]
        public void TestOrderedDictPrettyPrintable()
        {
            // Use PrettyPrint as a proxy to determine if we look internally indentical to
            // a "proper" container.
            Assert.AreEqual(
                PrettyPrint.Compact(new SortedDictionary<int, int>()).ToString(),
                PrettyPrint.Compact(new OrderedDictionary<int, int>()).ToString());
            Assert.AreEqual(
                PrettyPrint.Compact(new SortedDictionary<int, int>() { { 1, 2 } }).ToString(),
                PrettyPrint.Compact(new OrderedDictionary<int, int>() { { 1, 2 } }).ToString());
            Assert.AreEqual(
                PrettyPrint.Compact(new SortedDictionary<int, int>() { { 1, 2 }, { 2, 3 } }).ToString(),
                PrettyPrint.Compact(new OrderedDictionary<int, int>() { { 1, 2 }, { 2, 3 } }).ToString());

            Assert.AreEqual(
                PrettyPrint.Compact(new SortedDictionary<string, string>(StringComparer.InvariantCulture)).ToString(),
                PrettyPrint.Compact(new OrderedDictionary<string, string>()).ToString());
            Assert.AreEqual(
                PrettyPrint.Compact(new SortedDictionary<string, string>(StringComparer.InvariantCulture) { { "1", "2" } }).ToString(),
                PrettyPrint.Compact(new OrderedDictionary<string, string>() { { "1", "2" } }).ToString());
            Assert.AreEqual(
                PrettyPrint.Compact(new SortedDictionary<string, string>(StringComparer.InvariantCulture) { { "1", "2" }, { "3", "4" } }).ToString(),
                PrettyPrint.Compact(new OrderedDictionary<string, string>() { { "1", "2" }, { "3", "4" } }).ToString());

            // SortedDictionary does not support null. We do. Hack the reference to look what we expect.
            Assert.AreEqual(
                PrettyPrint.Compact(new SortedDictionary<string, string>(StringComparer.InvariantCulture) { { "1", null }, { "null", "2" } }).ToString(),
                PrettyPrint.Compact(new OrderedDictionary<string, string>() { { "1", null }, { null, "2" } }).ToString());
        }
        [Test]
        [Retry(100)] // First runs provoke some internal allocs.
        public void TestOrderedDictAllocations()
        {
            void NoAllocationsIn(Action action)
            {
                long initial = GC.GetAllocatedBytesForCurrentThread();
                action.Invoke();
                Assert.IsTrue(initial == GC.GetAllocatedBytesForCurrentThread());
            }
            Predicate<OrderedDictionary<int, int>.KeyValue> IsOdd = kv => kv.Key % 2 == 0;
            Predicate<OrderedDictionary<string, string>.KeyValue> IsEvenLength = kv => (kv.Value?.Length ?? 0) % 2 != 0;
            Predicate<OrderedDictionary<TestEnum, TestEnum>.KeyValue> IsOne = kv => kv.Key == TestEnum.One;
            OrderedDictionary<int, int> intDict = new OrderedDictionary<int, int>() { { 0, 1 }, { 1, 2 }, { 5, 3 }, { -3, 4 }, { 8, 5 }, { 3, 6 } };
            OrderedDictionary<string, string> boxDict = new OrderedDictionary<string, string>() { { "1", "foo" }, { "2", "Bar" }, { null, "nully" }, { "fo", "fu" }, { "bar", null } };
            OrderedDictionary<TestEnum, TestEnum> enumDict = new OrderedDictionary<TestEnum, TestEnum>() { { TestEnum.One, TestEnum.Twenty }, { TestEnum.Two, TestEnum.Twenty }, { TestEnum.Zero, TestEnum.Twenty  } };

            NoAllocationsIn(() => intDict.ContainsKey(2));
            NoAllocationsIn(() => intDict.ContainsKey(20));
            NoAllocationsIn(() => intDict.Remove(2));
            NoAllocationsIn(() => intDict.Remove(20));
            NoAllocationsIn(() => intDict.AddIfAbsent(0, 5));
            NoAllocationsIn(() => intDict.AddOrReplace(0, 5));
            NoAllocationsIn(() => intDict.RemoveWhere(IsOdd));
            NoAllocationsIn(() => { foreach (var kv in intDict) ; });
            NoAllocationsIn(() => { foreach (ref var kv in intDict) ; });
            NoAllocationsIn(() => { foreach (var k in intDict.Keys) ; });
            NoAllocationsIn(() => { foreach (var v in intDict.Values) ; });
            NoAllocationsIn(() => { foreach (ref var v in intDict.Values) ; });
            int[] intArr = new int[16];
            NoAllocationsIn(() => intDict.CopyKeysTo(intArr, 0));
            NoAllocationsIn(() => intDict.CopyValuesTo(intArr, 0));
            NoAllocationsIn(() => intDict.First());
            NoAllocationsIn(() => intDict.ElementAt(0));
            NoAllocationsIn(() => intDict.Keys.First());
            NoAllocationsIn(() => intDict.Keys.ElementAt(0));
            NoAllocationsIn(() => intDict.Values.First());
            NoAllocationsIn(() => intDict.Values.ElementAt(0));
            NoAllocationsIn(() => intDict.Clear());

            string str1 = "1";
            string str2 = "2";
            string str20 = "20";
            NoAllocationsIn(() => boxDict.ContainsKey(str2));
            NoAllocationsIn(() => boxDict.ContainsKey(str20));
            NoAllocationsIn(() => boxDict.Remove(str2));
            NoAllocationsIn(() => boxDict.Remove(str20));
            NoAllocationsIn(() => boxDict.AddIfAbsent(str1, str20));
            NoAllocationsIn(() => boxDict.AddOrReplace(str1, str20));
            NoAllocationsIn(() => boxDict.RemoveWhere(IsEvenLength));
            NoAllocationsIn(() => { foreach (var kv in boxDict) ; });
            NoAllocationsIn(() => { foreach (ref var kv in boxDict) ; });
            NoAllocationsIn(() => { foreach (var k in boxDict.Keys) ; });
            NoAllocationsIn(() => { foreach (var v in boxDict.Values) ; });
            NoAllocationsIn(() => { foreach (ref var v in boxDict.Values) ; });
            string[] boxArr = new string[16];
            NoAllocationsIn(() => boxDict.CopyKeysTo(boxArr, 0));
            NoAllocationsIn(() => boxDict.CopyValuesTo(boxArr, 0));
            NoAllocationsIn(() => boxDict.Clear());

            TestEnum enum1 = TestEnum.One;
            TestEnum enum2 = TestEnum.Two;
            TestEnum enum20 = TestEnum.Twenty;
            NoAllocationsIn(() => enumDict.ContainsKey(enum2));
            NoAllocationsIn(() => enumDict.ContainsKey(enum20));
            NoAllocationsIn(() => enumDict.Remove(enum2));
            NoAllocationsIn(() => enumDict.Remove(enum20));
            NoAllocationsIn(() => enumDict.AddIfAbsent(enum1, enum20));
            NoAllocationsIn(() => enumDict.AddOrReplace(enum1, enum20));
            NoAllocationsIn(() => enumDict.RemoveWhere(IsOne));
            NoAllocationsIn(() => { foreach (var kv in enumDict) ; });
            NoAllocationsIn(() => { foreach (ref var kv in enumDict) ; });
            NoAllocationsIn(() => { foreach (var k in enumDict.Keys) ; });
            NoAllocationsIn(() => { foreach (var v in enumDict.Values) ; });
            TestEnum[] enumArr = new TestEnum[16];
            NoAllocationsIn(() => enumDict.CopyKeysTo(enumArr, 0));
            NoAllocationsIn(() => enumDict.CopyValuesTo(enumArr, 0));
            NoAllocationsIn(() => enumDict.Clear());
        }
        [Test]
        public void TestOrderedDictForeachRef()
        {
            OrderedDictionary<int, int> idict = new OrderedDictionary<int, int>() { [1] = 2, [0] = 3 };
            foreach (ref var kv in idict)
            {
                if (kv.Value % 2 == 0)
                    kv.Value = 0;
            }
            Assert.AreEqual(new List<KeyValuePair<int, int>>() { new KeyValuePair<int, int>(1, 0), new KeyValuePair<int, int>(0, 3) }, new List<KeyValuePair<int, int>>(idict));

            OrderedDictionary<string, string> sdict = new OrderedDictionary<string, string>() { ["A"] = "1", ["BC"] = "2" };
            foreach (ref var kv in sdict)
            {
                if (kv.Key.Length > 1)
                    kv.Value = null;
                else
                    kv.Value = "X";
            }
            Assert.AreEqual(new List<KeyValuePair<string, string>>() { new KeyValuePair<string, string>("A", "X"), new KeyValuePair<string, string>("BC", null) }, new List<KeyValuePair<string, string>>(sdict));
        }
        [Test]
        public void TestOrderedDictGetValueOrDefault()
        {
            {
                OrderedDictionary<int, int> dict = new OrderedDictionary<int, int>() { { 1, 1 }, { 2, 2 }, { 3, 3 } };
                Assert.AreEqual(3, dict.GetValueOrDefault(3, defaultValue: 5));
                Assert.AreEqual(5, dict.GetValueOrDefault(4, defaultValue: 5));
            }
            {
                OrderedDictionary<int, string> dict = new OrderedDictionary<int, string>() { { 1, "1" }, { 2, "2" }, { 3, "3" } };
                Assert.AreEqual("3", dict.GetValueOrDefault(3, defaultValue: "5"));
                Assert.AreEqual("5", dict.GetValueOrDefault(4, defaultValue: "5"));
            }
        }
        [Test]
        public void TestOrderedDictFirst()
        {
            {
                OrderedDictionary<int, int> dict = new OrderedDictionary<int, int>() { { 1, 1 }, { 2, 2 }, { 3, 3 } };
                Assert.AreEqual(1, dict.First().Key);
                Assert.AreEqual(1, dict.Keys.First());
                Assert.AreEqual(1, dict.Values.First());
                dict.Remove(1);
                Assert.AreEqual(2, dict.First().Key);
                Assert.AreEqual(2, dict.Keys.First());
                Assert.AreEqual(2, dict.Values.First());
            }
            {
                OrderedDictionary<int, int> dict = new OrderedDictionary<int, int>();
                Assert.Throws<InvalidOperationException>(() => _ = dict.First());
                Assert.Throws<InvalidOperationException>(() => _ = dict.Keys.First());
                Assert.Throws<InvalidOperationException>(() => _ = dict.Values.First());
            }
        }
        [Test]
        public void TestOrderedDictElementAt()
        {
            {
                OrderedDictionary<int, int> dict = new OrderedDictionary<int, int>() { { 1, 1 }, { 2, 2 }, { 3, 3 } };
                Assert.AreEqual(1, dict.ElementAt(0).Key);
                Assert.AreEqual(1, dict.Keys.ElementAt(0));
                Assert.AreEqual(1, dict.Values.ElementAt(0));
                Assert.AreEqual(2, dict.ElementAt(1).Key);
                Assert.AreEqual(2, dict.Keys.ElementAt(1));
                Assert.AreEqual(2, dict.Values.ElementAt(1));
                Assert.AreEqual(3, dict.ElementAt(2).Key);
                Assert.AreEqual(3, dict.Keys.ElementAt(2));
                Assert.AreEqual(3, dict.Values.ElementAt(2));
            }
            {
                OrderedDictionary<int, int> dict = new OrderedDictionary<int, int>();
                Assert.Throws<ArgumentOutOfRangeException>(() => _ = dict.ElementAt(0));
                Assert.Throws<ArgumentOutOfRangeException>(() => _ = dict.Keys.ElementAt(0));
                Assert.Throws<ArgumentOutOfRangeException>(() => _ = dict.Values.ElementAt(0));
                dict.Add(1, 1);
                Assert.Throws<ArgumentOutOfRangeException>(() => _ = dict.ElementAt(1));
                Assert.Throws<ArgumentOutOfRangeException>(() => _ = dict.Keys.ElementAt(1));
                Assert.Throws<ArgumentOutOfRangeException>(() => _ = dict.Values.ElementAt(1));
                Assert.Throws<ArgumentOutOfRangeException>(() => _ = dict.ElementAt(-1));
                Assert.Throws<ArgumentOutOfRangeException>(() => _ = dict.Keys.ElementAt(-1));
                Assert.Throws<ArgumentOutOfRangeException>(() => _ = dict.Values.ElementAt(-1));
                Assert.Throws<ArgumentOutOfRangeException>(() => _ = dict.ElementAt(int.MaxValue));
            }
        }
    }
}
