// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Model;
using Metaplay.Core.Serialization;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using static System.FormattableString;

namespace Cloud.Tests
{
    /// <summary>
    /// Note: serialization-related tests for MetaRefs are in SerializationTests.
    /// </summary>
    [TestFixture]
    public class MetaRefTests
    {
        public class TestIntItem : IGameConfigData<int>
        {
            public int ConfigKey { get; set; }
            public override string ToString() => Invariant($"TestIntItem({ConfigKey})");
        }

        public class TestStringItem : IGameConfigData<string>
        {
            public string ConfigKey { get; set; }
            public override string ToString() => $"TestStringItem({ConfigKey})";
        }

        public class TestDerivedStringItem : TestStringItem
        {
            public override string ToString() => $"TestDerivedStringItem({ConfigKey})";
        }

        [Test]
        public void Invalid()
        {
            // Null key

            Assert.Catch(() => MetaRef<TestIntItem>.FromKey(null));
            Assert.Catch(() => MetaRef<TestIntItem>.FromItem(null));
            Assert.Catch(() => MetaRefUtil.CreateFromKey(typeof(MetaRef<TestIntItem>), null));

            Assert.Catch(() => MetaRef<TestStringItem>.FromKey(null));
            Assert.Catch(() => MetaRef<TestStringItem>.FromItem(null));
            Assert.Catch(() => MetaRefUtil.CreateFromKey(typeof(MetaRef<TestStringItem>), null));

            // Wrong key type

            Assert.Catch(() => MetaRef<TestIntItem>.FromKey("test"));
            Assert.Catch(() => MetaRefUtil.CreateFromKey(typeof(MetaRef<TestIntItem>), "test"));

            Assert.Catch(() => MetaRef<TestStringItem>.FromKey(123));
            Assert.Catch(() => MetaRefUtil.CreateFromKey(typeof(MetaRef<TestStringItem>), 123));

            // Misc

            MetaRef<TestIntItem>    intRef      = MetaRef<TestIntItem>.FromKey(123);
            MetaRef<TestStringItem> stringRef   = MetaRef<TestStringItem>.FromKey("test");

            Assert.Catch(() => _ = intRef.Ref);
            Assert.Catch(() => intRef.CastItem<TestStringItem>());
            Assert.Catch(() => intRef.CreateResolved(resolver: null));
            Assert.Catch(() => intRef.CreateResolved(resolver: new Resolver()));

            Assert.Catch(() => _ = stringRef.Ref);
            Assert.Catch(() => stringRef.CastItem<TestIntItem>());
            Assert.Catch(() => stringRef.CreateResolved(resolver: null));
            Assert.Catch(() => stringRef.CreateResolved(resolver: new Resolver()));

            MetaRef<TestStringItem> resolvedStringRef = MetaRef<TestStringItem>.FromItem(new TestStringItem{ ConfigKey = "test" });
            Assert.Catch(() => resolvedStringRef.CastItem<TestDerivedStringItem>());
        }

        [Test]
        public void Misc()
        {
            // \todo [nuutti] This is repetitive, clean it up?

            TestIntItem                     intItem                         = new TestIntItem{ ConfigKey = 123 };
            TestStringItem                  stringItem                      = new TestStringItem{ ConfigKey = "test" };
            TestDerivedStringItem           derivedStringItem               = new TestDerivedStringItem{ ConfigKey = "testDerived" };
            TestIntItem                     intItem2                        = new TestIntItem{ ConfigKey = 1232 };
            TestStringItem                  stringItem2                     = new TestStringItem{ ConfigKey = "test2" };
            TestDerivedStringItem           derivedStringItem2              = new TestDerivedStringItem{ ConfigKey = "testDerived2" };
            Resolver                        resolver                        = new Resolver(intItem, stringItem, derivedStringItem, intItem2, stringItem2, derivedStringItem2);

            MetaRef<TestIntItem>            intRef                          = MetaRef<TestIntItem>.FromKey(123);
            MetaRef<TestStringItem>         stringRef                       = MetaRef<TestStringItem>.FromKey("test");
            MetaRef<TestDerivedStringItem>  derivedStringRef                = MetaRef<TestDerivedStringItem>.FromKey("testDerived");
            MetaRef<TestIntItem>            intRef2                         = MetaRef<TestIntItem>.FromKey(1232);
            MetaRef<TestStringItem>         stringRef2                      = MetaRef<TestStringItem>.FromKey("test2");
            MetaRef<TestDerivedStringItem>  derivedStringRef2               = MetaRef<TestDerivedStringItem>.FromKey("testDerived2");

            MetaRef<TestIntItem>            otherIntRef                     = MetaRef<TestIntItem>.FromKey(123);
            MetaRef<TestStringItem>         otherStringRef                  = MetaRef<TestStringItem>.FromKey("test");
            MetaRef<TestDerivedStringItem>  otherDerivedStringRef           = MetaRef<TestDerivedStringItem>.FromKey("testDerived");

            MetaRef<TestIntItem>            resolvedIntRef                  = MetaRef<TestIntItem>.FromItem(intItem);
            MetaRef<TestIntItem>            otherResolvedIntRef             = MetaRef<TestIntItem>.FromKey(123).CreateResolved(resolver);
            MetaRef<TestStringItem>         resolvedStringRef               = MetaRef<TestStringItem>.FromItem(stringItem);
            MetaRef<TestStringItem>         otherResolvedStringRef          = MetaRef<TestStringItem>.FromKey("test").CreateResolved(resolver);
            MetaRef<TestDerivedStringItem>  resolvedDerivedStringRef        = MetaRef<TestDerivedStringItem>.FromItem(derivedStringItem);
            MetaRef<TestDerivedStringItem>  otherResolvedDerivedStringRef   = MetaRef<TestDerivedStringItem>.FromKey("testDerived").CreateResolved(resolver);

            MetaRef<TestIntItem>            resolvedIntRef2                 = MetaRef<TestIntItem>.FromItem(intItem2);
            MetaRef<TestIntItem>            otherResolvedIntRef2            = MetaRef<TestIntItem>.FromKey(1232).CreateResolved(resolver);
            MetaRef<TestStringItem>         resolvedStringRef2              = MetaRef<TestStringItem>.FromItem(stringItem2);
            MetaRef<TestStringItem>         otherResolvedStringRef2         = MetaRef<TestStringItem>.FromKey("test2").CreateResolved(resolver);
            MetaRef<TestDerivedStringItem>  resolvedDerivedStringRef2       = MetaRef<TestDerivedStringItem>.FromItem(derivedStringItem2);
            MetaRef<TestDerivedStringItem>  otherResolvedDerivedStringRef2  = MetaRef<TestDerivedStringItem>.FromKey("testDerived2").CreateResolved(resolver);

            // Check keys and references

            CheckNonResolved(intRef, 123);
            CheckNonResolved(stringRef, "test");
            CheckNonResolved(derivedStringRef, "testDerived");
            CheckNonResolved(derivedStringRef.CastItem<TestStringItem>(), "testDerived");
            CheckNonResolved(intRef2, 1232);
            CheckNonResolved(stringRef2, "test2");
            CheckNonResolved(derivedStringRef2, "testDerived2");
            CheckNonResolved(derivedStringRef2.CastItem<TestStringItem>(), "testDerived2");

            CheckRefersTo(resolvedIntRef, intItem);
            CheckRefersTo(resolvedStringRef, stringItem);
            CheckRefersTo(resolvedDerivedStringRef, derivedStringItem);
            CheckRefersTo(resolvedDerivedStringRef.CastItem<TestStringItem>(), derivedStringItem);
            CheckRefersTo(resolvedIntRef2, intItem2);
            CheckRefersTo(resolvedStringRef2, stringItem2);
            CheckRefersTo(resolvedDerivedStringRef2, derivedStringItem2);
            CheckRefersTo(resolvedDerivedStringRef2.CastItem<TestStringItem>(), derivedStringItem2);

            CheckRefersTo(otherResolvedIntRef, intItem);
            CheckRefersTo(otherResolvedStringRef, stringItem);
            CheckRefersTo(otherResolvedDerivedStringRef, derivedStringItem);
            CheckRefersTo(otherResolvedDerivedStringRef.CastItem<TestStringItem>(), derivedStringItem);
            CheckRefersTo(otherResolvedIntRef2, intItem2);
            CheckRefersTo(otherResolvedStringRef2, stringItem2);
            CheckRefersTo(otherResolvedDerivedStringRef2, derivedStringItem2);
            CheckRefersTo(otherResolvedDerivedStringRef2.CastItem<TestStringItem>(), derivedStringItem2);

            // Equality between two non-resolveds

            CheckEquals(true, intRef, intRef);
            CheckEquals(true, stringRef, stringRef);
            CheckEquals(true, derivedStringRef, derivedStringRef);
            CheckEquals(true, derivedStringRef.CastItem<TestStringItem>(), derivedStringRef.CastItem<TestStringItem>());
            CheckObjectEquals(true, derivedStringRef.CastItem<TestStringItem>(), derivedStringRef);

            CheckEquals(true, intRef.CastItem<TestIntItem>(), intRef.CastItem<TestIntItem>());
            CheckEquals(true, stringRef.CastItem<TestStringItem>(), stringRef.CastItem<TestStringItem>());
            CheckEquals(true, derivedStringRef.CastItem<TestDerivedStringItem>(), derivedStringRef.CastItem<TestDerivedStringItem>());
            CheckEquals(true, intRef.CastItem<TestIntItem>(), intRef);
            CheckEquals(true, stringRef.CastItem<TestStringItem>(), stringRef);
            CheckEquals(true, derivedStringRef.CastItem<TestDerivedStringItem>(), derivedStringRef);

            Assert.AreNotSame(intRef, otherIntRef);
            Assert.AreNotSame(stringRef, otherStringRef);
            Assert.AreNotSame(derivedStringRef, otherDerivedStringRef);
            CheckEquals(true, intRef, otherIntRef);
            CheckEquals(true, stringRef, otherStringRef);
            CheckEquals(true, derivedStringRef, otherDerivedStringRef);
            CheckEquals(true, derivedStringRef.CastItem<TestStringItem>(), otherDerivedStringRef.CastItem<TestStringItem>());
            CheckObjectEquals(true, derivedStringRef.CastItem<TestStringItem>(), otherDerivedStringRef);
            CheckObjectEquals(true, derivedStringRef, otherDerivedStringRef.CastItem<TestStringItem>());

            CheckEquals(false, intRef, intRef2);
            CheckEquals(false, stringRef, stringRef2);
            CheckEquals(false, derivedStringRef, derivedStringRef2);
            CheckEquals(false, derivedStringRef.CastItem<TestStringItem>(), derivedStringRef2.CastItem<TestStringItem>());
            CheckObjectEquals(false, derivedStringRef.CastItem<TestStringItem>(), derivedStringRef2);
            CheckObjectEquals(false, derivedStringRef, derivedStringRef2.CastItem<TestStringItem>());

            CheckObjectEquals(false, intRef, stringRef);
            CheckObjectEquals(false, stringRef, derivedStringRef);
            CheckObjectEquals(false, derivedStringRef, intRef);
            CheckObjectEquals(false, derivedStringRef.CastItem<TestStringItem>(), stringRef);

            // Equality between two resolveds

            CheckEquals(true, resolvedIntRef, resolvedIntRef);
            CheckEquals(true, resolvedStringRef, resolvedStringRef);
            CheckEquals(true, resolvedDerivedStringRef, resolvedDerivedStringRef);
            CheckEquals(true, resolvedDerivedStringRef.CastItem<TestStringItem>(), resolvedDerivedStringRef.CastItem<TestStringItem>());
            CheckObjectEquals(true, resolvedDerivedStringRef.CastItem<TestStringItem>(), resolvedDerivedStringRef);

            CheckEquals(true, resolvedIntRef.CastItem<TestIntItem>(), resolvedIntRef.CastItem<TestIntItem>());
            CheckEquals(true, resolvedStringRef.CastItem<TestStringItem>(), resolvedStringRef.CastItem<TestStringItem>());
            CheckEquals(true, resolvedDerivedStringRef.CastItem<TestDerivedStringItem>(), resolvedDerivedStringRef.CastItem<TestDerivedStringItem>());
            CheckEquals(true, resolvedIntRef.CastItem<TestIntItem>(), resolvedIntRef);
            CheckEquals(true, resolvedStringRef.CastItem<TestStringItem>(), resolvedStringRef);
            CheckEquals(true, resolvedDerivedStringRef.CastItem<TestDerivedStringItem>(), resolvedDerivedStringRef);

            Assert.AreNotSame(resolvedIntRef, otherResolvedIntRef);
            Assert.AreNotSame(resolvedStringRef, otherResolvedStringRef);
            Assert.AreNotSame(resolvedDerivedStringRef, otherResolvedDerivedStringRef);
            CheckEquals(true, resolvedIntRef, otherResolvedIntRef);
            CheckEquals(true, resolvedStringRef, otherResolvedStringRef);
            CheckEquals(true, resolvedDerivedStringRef, otherResolvedDerivedStringRef);
            CheckEquals(true, resolvedDerivedStringRef.CastItem<TestStringItem>(), otherResolvedDerivedStringRef.CastItem<TestStringItem>());
            CheckObjectEquals(true, resolvedDerivedStringRef.CastItem<TestStringItem>(), otherResolvedDerivedStringRef);
            CheckObjectEquals(true, resolvedDerivedStringRef, otherResolvedDerivedStringRef.CastItem<TestStringItem>());

            CheckEquals(false, resolvedIntRef, resolvedIntRef2);
            CheckEquals(false, resolvedIntRef, otherResolvedIntRef2);
            CheckEquals(false, resolvedStringRef, resolvedStringRef2);
            CheckEquals(false, resolvedStringRef, otherResolvedStringRef2);
            CheckEquals(false, resolvedDerivedStringRef, resolvedDerivedStringRef2);
            CheckEquals(false, resolvedDerivedStringRef, otherResolvedDerivedStringRef2);
            CheckEquals(false, resolvedDerivedStringRef.CastItem<TestStringItem>(), resolvedDerivedStringRef2.CastItem<TestStringItem>());
            CheckEquals(false, resolvedDerivedStringRef.CastItem<TestStringItem>(), otherResolvedDerivedStringRef2.CastItem<TestStringItem>());
            CheckObjectEquals(false, resolvedDerivedStringRef.CastItem<TestStringItem>(), resolvedDerivedStringRef2);
            CheckObjectEquals(false, resolvedDerivedStringRef.CastItem<TestStringItem>(), otherResolvedDerivedStringRef2);
            CheckObjectEquals(false, resolvedDerivedStringRef, resolvedDerivedStringRef2.CastItem<TestStringItem>());
            CheckObjectEquals(false, resolvedDerivedStringRef, otherResolvedDerivedStringRef2.CastItem<TestStringItem>());

            CheckObjectEquals(false, resolvedIntRef, resolvedStringRef);
            CheckObjectEquals(false, resolvedStringRef, resolvedDerivedStringRef);
            CheckObjectEquals(false, resolvedDerivedStringRef, resolvedIntRef);
            CheckObjectEquals(false, resolvedDerivedStringRef, resolvedStringRef.CastItem<TestStringItem>());

            // Equality between non-resolved and resolved

            Assert.AreEqual(intRef.KeyObject, resolvedIntRef.KeyObject);
            Assert.AreEqual(intRef.KeyObject, otherResolvedIntRef.KeyObject);
            Assert.AreEqual(stringRef.KeyObject, resolvedStringRef.KeyObject);
            Assert.AreEqual(stringRef.KeyObject, otherResolvedStringRef.KeyObject);
            Assert.AreEqual(derivedStringRef.KeyObject, resolvedDerivedStringRef.KeyObject);
            Assert.AreEqual(derivedStringRef.KeyObject, otherResolvedDerivedStringRef.KeyObject);
            Assert.AreEqual(derivedStringRef.CastItem<TestStringItem>().KeyObject, resolvedDerivedStringRef.CastItem<TestStringItem>().KeyObject);
            Assert.AreEqual(derivedStringRef.CastItem<TestStringItem>().KeyObject, otherResolvedDerivedStringRef.CastItem<TestStringItem>().KeyObject);
            Assert.AreEqual(derivedStringRef.KeyObject, resolvedDerivedStringRef.CastItem<TestStringItem>().KeyObject);
            Assert.AreEqual(derivedStringRef.KeyObject, otherResolvedDerivedStringRef.CastItem<TestStringItem>().KeyObject);
            Assert.AreEqual(derivedStringRef.CastItem<TestStringItem>().KeyObject, resolvedDerivedStringRef.KeyObject);
            Assert.AreEqual(derivedStringRef.CastItem<TestStringItem>().KeyObject, otherResolvedDerivedStringRef.KeyObject);
            CheckEquals(false, intRef, resolvedIntRef);
            CheckEquals(false, intRef, otherResolvedIntRef);
            CheckEquals(false, stringRef, resolvedStringRef);
            CheckEquals(false, stringRef, otherResolvedStringRef);
            CheckEquals(false, derivedStringRef, resolvedDerivedStringRef);
            CheckEquals(false, derivedStringRef, otherResolvedDerivedStringRef);

            CheckEquals(false, derivedStringRef.CastItem<TestStringItem>(), resolvedDerivedStringRef.CastItem<TestStringItem>());
            CheckEquals(false, derivedStringRef.CastItem<TestStringItem>(), otherResolvedDerivedStringRef.CastItem<TestStringItem>());
            CheckObjectEquals(false, derivedStringRef, resolvedDerivedStringRef.CastItem<TestStringItem>());
            CheckObjectEquals(false, derivedStringRef, otherResolvedDerivedStringRef.CastItem<TestStringItem>());
            CheckObjectEquals(false, derivedStringRef.CastItem<TestStringItem>(), resolvedDerivedStringRef);
            CheckObjectEquals(false, derivedStringRef.CastItem<TestStringItem>(), otherResolvedDerivedStringRef);

            // Equality involving null

            CheckEquals(true, (MetaRef<TestIntItem>)null, (MetaRef<TestIntItem>)null);
            CheckEquals(false, intRef, (MetaRef<TestIntItem>)null);
            CheckEquals(false, resolvedIntRef, (MetaRef<TestIntItem>)null);

            CheckEquals(true, (MetaRef<TestStringItem>)null, (MetaRef<TestStringItem>)null);
            CheckEquals(false, stringRef, (MetaRef<TestStringItem>)null);
            CheckEquals(false, resolvedStringRef, (MetaRef<TestStringItem>)null);

            CheckEquals(true, (MetaRef<TestDerivedStringItem>)null, (MetaRef<TestDerivedStringItem>)null);
            CheckEquals(false, derivedStringRef, (MetaRef<TestDerivedStringItem>)null);
            CheckEquals(false, resolvedDerivedStringRef, (MetaRef<TestDerivedStringItem>)null);

            // ToString

            Assert.AreEqual("(non-resolved: 123)", intRef.ToString());
            Assert.AreEqual("(non-resolved: test)", stringRef.ToString());
            Assert.AreEqual("(non-resolved: testDerived)", derivedStringRef.ToString());
            Assert.AreEqual("(non-resolved: testDerived)", derivedStringRef.CastItem<TestStringItem>().ToString());

            Assert.AreEqual("(resolved: 123)", resolvedIntRef.ToString());
            Assert.AreEqual("(resolved: test)", resolvedStringRef.ToString());
            Assert.AreEqual("(resolved: testDerived)", resolvedDerivedStringRef.ToString());
            Assert.AreEqual("(resolved: testDerived)", resolvedDerivedStringRef.CastItem<TestStringItem>().ToString());

            // GetHashCode

            object[] refs = new object[]
            {
                intRef,
                stringRef,
                derivedStringRef,
                intRef2,
                stringRef2,
                derivedStringRef2,

                otherIntRef,
                otherStringRef,
                otherDerivedStringRef,

                resolvedIntRef,
                otherResolvedIntRef,
                resolvedStringRef,
                otherResolvedStringRef,
                resolvedDerivedStringRef,
                otherResolvedDerivedStringRef,

                resolvedIntRef2,
                otherResolvedIntRef2,
                resolvedStringRef2,
                otherResolvedStringRef2,
                resolvedDerivedStringRef2,
                otherResolvedDerivedStringRef2,
            };

            foreach (object a in refs)
            {
                foreach (object b in refs)
                {
                    if (a.Equals(b))
                        Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
                }
            }
        }

        [Test]
        public void Alias()
        {
            TestStringItem item = new TestStringItem{ ConfigKey = "test" };

            Resolver resolver = new Resolver();
            resolver.Add(item);
            resolver.Add("alias", item);

            MetaRef<TestStringItem> canonicalRef    = MetaRef<TestStringItem>.FromKey("test");
            MetaRef<TestStringItem> aliasRef        = MetaRef<TestStringItem>.FromKey("alias");

            CheckNonResolved(canonicalRef, item.ConfigKey);
            CheckNonResolved(aliasRef, "alias");

            CheckEquals(false, canonicalRef, aliasRef);

            MetaRef<TestStringItem> resolvedRef                 = MetaRef<TestStringItem>.FromItem(item);
            MetaRef<TestStringItem> resolvedFromCanonicalRef    = MetaRef<TestStringItem>.FromKey("test").CreateResolved(resolver);
            MetaRef<TestStringItem> resolvedFromAliasRef        = MetaRef<TestStringItem>.FromKey("alias").CreateResolved(resolver);

            CheckRefersTo(resolvedRef, item);
            CheckRefersTo(resolvedFromCanonicalRef, item);
            CheckRefersTo(resolvedFromAliasRef, item);

            CheckEquals(false, canonicalRef, resolvedRef);
            CheckEquals(false, aliasRef, resolvedRef);

            CheckEquals(true, resolvedRef, resolvedFromCanonicalRef);
            CheckEquals(true, resolvedRef, resolvedFromAliasRef);
            CheckEquals(true, resolvedFromCanonicalRef, resolvedFromAliasRef);
        }

        static void CheckRefersTo<TItem>(MetaRef<TItem> metaRef, TItem item)
            where TItem : class, IGameConfigData
        {
            Assert.AreEqual(GetConfigKey(item), metaRef.KeyObject);
            Assert.IsTrue(metaRef.IsResolved);
            Assert.AreSame(item, metaRef.Ref);
            Assert.AreSame(item, metaRef.MaybeRef);
        }

        static void CheckNonResolved<TItem>(MetaRef<TItem> metaRef, object key)
            where TItem : class, IGameConfigData
        {
            Assert.AreEqual(key, metaRef.KeyObject);
            Assert.IsNull(metaRef.MaybeRef);
            Assert.IsFalse(metaRef.IsResolved);
        }

        static void CheckEquals<TItem>(bool equals, MetaRef<TItem> a, MetaRef<TItem> b)
            where TItem : class, IGameConfigData
        {
            CheckObjectEquals(equals, a, b);
            CheckEquatableEquals(equals, a, b);

            Assert.AreEqual(equals, a == b);
            Assert.AreEqual(equals, b == a);

            Assert.AreEqual(!equals, a != b);
            Assert.AreEqual(!equals, b != a);
        }

        static void CheckObjectEquals(bool equals, object a, object b)
        {
            if (!ReferenceEquals(a, null))
            {
                Assert.AreEqual(equals, a.Equals(b));
                if (equals)
                    Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
            }

            if (!ReferenceEquals(b, null))
            {
                Assert.AreEqual(equals, b.Equals(a));
                if (equals)
                    Assert.AreEqual(b.GetHashCode(), a.GetHashCode());
            }
        }

        static void CheckEquatableEquals<T>(bool equals, T a, T b)
            where T : IEquatable<T>
        {
            if (!ReferenceEquals(a, null))
            {
                Assert.AreEqual(equals, a.Equals(b));
                if (equals)
                    Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
            }

            if (!ReferenceEquals(b, null))
            {
                Assert.AreEqual(equals, b.Equals(a));
                if (equals)
                    Assert.AreEqual(b.GetHashCode(), a.GetHashCode());
            }
        }

        static object GetConfigKey(IGameConfigData item)
        {
            return item
                .GetType()
                .GetGenericInterface(typeof(IGameConfigKey<>))
                .GetProperty("ConfigKey")
                .GetValue(item);
        }

        class Resolver : IGameConfigDataResolver
        {
            Dictionary<(Type, object), IGameConfigData> _items = new Dictionary<(Type, object), IGameConfigData>();

            public Resolver(params IGameConfigData[] items)
            {
                foreach (IGameConfigData item in items)
                    Add(item);
            }

            public void Add(IGameConfigData item)
            {
                Add(GetConfigKey(item), item);
            }

            public void Add(object configKey, IGameConfigData item)
            {
                _items.Add(
                    (item.GetType(), configKey),
                    item);
            }

            public object TryResolveReference(Type type, object configKey)
                => _items.GetValueOrDefault((type, configKey), null);
        }

        [MetaSerializable(MetaSerializableFlags.ImplicitMembers)]
        [MetaImplicitMembersRange(1, 100)]
        public class TestInnerClass
        {
            public MetaRef<TestStringItem> RefField;
            public MetaRef<TestStringItem> NullRefField;
            public MetaRef<TestStringItem> RefProp { get; set; }
            public MetaRef<TestStringItem> NullRefProp { get; set; }
        }

        [MetaSerializable(MetaSerializableFlags.ImplicitMembers)]
        [MetaImplicitMembersRange(1, 100)]
        public struct TestInnerStruct
        {
            public MetaRef<TestStringItem> RefField;
            public MetaRef<TestStringItem> NullRefField;
            public MetaRef<TestStringItem> RefProp { get; set; }
            public MetaRef<TestStringItem> NullRefProp { get; set; }
        }

        [MetaSerializable(MetaSerializableFlags.ImplicitMembers)]
        [MetaImplicitMembersRange(100, 200)]
        public abstract class TestInnerBaseClass
        {
            public MetaRef<TestStringItem> RefFieldInBase;
            public MetaRef<TestStringItem> NullRefFieldInBase;
            public MetaRef<TestStringItem> RefPropInBase { get; set; }
            public MetaRef<TestStringItem> NullRefPropInBase { get; set; }
        }

        [MetaSerializableDerived(1)]
        [MetaImplicitMembersRange(1, 100)]
        public class TestInnerDerived0Class : TestInnerBaseClass
        {
            public MetaRef<TestStringItem> RefFieldInDerived0;
            public MetaRef<TestStringItem> NullRefFieldInDerived0;
            public MetaRef<TestStringItem> RefPropInDerived0 { get; set; }
            public MetaRef<TestStringItem> NullRefPropInDerived0 { get; set; }
        }

        [MetaSerializableDerived(2)]
        [MetaImplicitMembersRange(1, 100)]
        public class TestInnerDerived1Class : TestInnerBaseClass
        {
            public MetaRef<TestStringItem> RefFieldInDerived1;
            public MetaRef<TestStringItem> NullRefFieldInDerived1;
            public MetaRef<TestStringItem> RefPropInDerived1 { get; set; }
            public MetaRef<TestStringItem> NullRefPropInDerived1 { get; set; }
        }

        [MetaSerializable(MetaSerializableFlags.ImplicitMembers)]
        [MetaImplicitMembersRange(1, 100)]
        public class TestOuterClass
        {
            public MetaRef<TestStringItem>                      RefField;
            public MetaRef<TestStringItem>                      NullRefField;
            public MetaRef<TestStringItem>                      RefProp { get; set; }
            public MetaRef<TestStringItem>                      NullRefProp { get; set; }

            public TestInnerClass                               InnerClassField;
            public TestInnerClass                               InnerNullClassField;
            public TestInnerStruct                              InnerStructField;
            public TestInnerStruct?                             InnerNullableStructField;
            public TestInnerStruct?                             InnerNullNullableStructField;
            public TestInnerBaseClass                           InnerDerived0ClassAsBaseField;
            public TestInnerBaseClass                           InnerDerived1ClassAsBaseField;
            public TestInnerBaseClass                           InnerNullBaseClassField;

            public TestInnerClass                               InnerClassProp { get; set; }
            public TestInnerClass                               InnerNullClassProp { get; set; }
            public TestInnerStruct                              InnerStructProp { get; set; }
            public TestInnerStruct?                             InnerNullableStructProp { get; set; }
            public TestInnerStruct?                             InnerNullNullableStructProp { get; set; }
            public TestInnerBaseClass                           InnerDerived0ClassAsBaseProp { get; set; }
            public TestInnerBaseClass                           InnerDerived1ClassAsBaseProp { get; set; }
            public TestInnerBaseClass                           InnerNullBaseClassProp { get; set; }

            public List<TestInnerClass>                         InnerClassList;
            public List<TestInnerClass>                         NullInnerClassList;
            public List<TestInnerStruct>                        InnerStructList;
            public List<TestInnerStruct?>                       InnerNullableStructList;
            public List<MetaRef<TestStringItem>>                RefList;

            public TestInnerClass[]                             InnerClassArray;
            public TestInnerClass[]                             NullInnerClassArray;
            public TestInnerStruct[]                            InnerStructArray;
            public TestInnerStruct?[]                           InnerNullableStructArray;
            public MetaRef<TestStringItem>[]                    RefArray;

            public Queue<TestInnerClass>                        InnerClassQueue;
            public Queue<TestInnerClass>                        NullInnerClassQueue;
            public Queue<TestInnerStruct>                       InnerStructQueue;
            public Queue<TestInnerStruct?>                      InnerNullableStructQueue;
            public Queue<MetaRef<TestStringItem>>               RefQueue;

            public Dictionary<string, TestInnerClass>           InnerClassDict;
            public Dictionary<string, TestInnerClass>           NullInnerClassDict;
            public Dictionary<string, TestInnerStruct>          InnerStructDict;
            public Dictionary<string, TestInnerStruct?>         InnerNullableStructDict;
            public Dictionary<string, MetaRef<TestStringItem>>  RefDict;

            public OrderedDictionary<MetaRef<TestStringItem>, MetaRef<TestStringItem>>  RefKeyedOrderedDict;
            public OrderedSet<MetaRef<TestStringItem>>                                  RefOrderedSet;
        }

        [MetaSerializable(MetaSerializableFlags.ImplicitMembers)]
        [MetaImplicitMembersRange(1, 100)]
        public class TestOuterStruct
        {
            public MetaRef<TestStringItem>                      RefField;
            public MetaRef<TestStringItem>                      NullRefField;
            public MetaRef<TestStringItem>                      RefProp { get; set; }
            public MetaRef<TestStringItem>                      NullRefProp { get; set; }

            public TestInnerClass                               InnerClassField;
            public TestInnerClass                               InnerNullClassField;
            public TestInnerStruct                              InnerStructField;
            public TestInnerStruct?                             InnerNullableStructField;
            public TestInnerStruct?                             InnerNullNullableStructField;
            public TestInnerBaseClass                           InnerDerived0ClassAsBaseField;
            public TestInnerBaseClass                           InnerDerived1ClassAsBaseField;
            public TestInnerBaseClass                           InnerNullBaseClassField;

            public TestInnerClass                               InnerClassProp { get; set; }
            public TestInnerClass                               InnerNullClassProp { get; set; }
            public TestInnerStruct                              InnerStructProp { get; set; }
            public TestInnerStruct?                             InnerNullableStructProp { get; set; }
            public TestInnerStruct?                             InnerNullNullableStructProp { get; set; }
            public TestInnerBaseClass                           InnerDerived0ClassAsBaseProp { get; set; }
            public TestInnerBaseClass                           InnerDerived1ClassAsBaseProp { get; set; }
            public TestInnerBaseClass                           InnerNullBaseClassProp { get; set; }

            public List<TestInnerClass>                         InnerClassList;
            public List<TestInnerClass>                         NullInnerClassList;
            public List<TestInnerStruct>                        InnerStructList;
            public List<TestInnerStruct?>                       InnerNullableStructList;
            public List<MetaRef<TestStringItem>>                RefList;

            public TestInnerClass[]                             InnerClassArray;
            public TestInnerClass[]                             NullInnerClassArray;
            public TestInnerStruct[]                            InnerStructArray;
            public TestInnerStruct?[]                           InnerNullableStructArray;
            public MetaRef<TestStringItem>[]                    RefArray;

            public Queue<TestInnerClass>                        InnerClassQueue;
            public Queue<TestInnerClass>                        NullInnerClassQueue;
            public Queue<TestInnerStruct>                       InnerStructQueue;
            public Queue<TestInnerStruct?>                      InnerNullableStructQueue;
            public Queue<MetaRef<TestStringItem>>               RefQueue;

            public Dictionary<string, TestInnerClass>           InnerClassDict;
            public Dictionary<string, TestInnerClass>           NullInnerClassDict;
            public Dictionary<string, TestInnerStruct>          InnerStructDict;
            public Dictionary<string, TestInnerStruct?>         InnerNullableStructDict;
            public Dictionary<string, MetaRef<TestStringItem>>  RefDict;

            public OrderedDictionary<MetaRef<TestStringItem>, MetaRef<TestStringItem>>  RefKeyedOrderedDict;
            public OrderedSet<MetaRef<TestStringItem>>                                  RefOrderedSet;
        }

        [MetaSerializable(MetaSerializableFlags.ImplicitMembers)]
        [MetaImplicitMembersRange(1, 100)]
        public class TopClass
        {
            public TestOuterClass Class;
            public TestOuterStruct Struct;
        }

        [Test]
        public void ObjectGraphResolve()
        {
            List<string> keys = new List<string>();
            string CreateKey()
            {
                string key = Invariant($"key{keys.Count}");
                keys.Add(key);
                return key;
            }
            MetaRef<TestStringItem> CreateRef()
            {
                return MetaRef<TestStringItem>.FromKey(CreateKey());
            }
            TestInnerClass CreateInnerClass()
            {
                return new TestInnerClass
                {
                    RefField = CreateRef(),
                    NullRefField = null,
                    RefProp = CreateRef(),
                    NullRefProp = null,
                };
            }
            TestInnerStruct CreateInnerStruct()
            {
                return new TestInnerStruct
                {
                    RefField = CreateRef(),
                    NullRefField = null,
                    RefProp = CreateRef(),
                    NullRefProp = null,
                };
            }
            TestInnerDerived0Class CreateInnerDerived0Class()
            {
                return new TestInnerDerived0Class
                {
                    RefFieldInBase = CreateRef(),
                    NullRefFieldInBase = null,
                    RefPropInBase = CreateRef(),
                    NullRefPropInBase = null,

                    RefFieldInDerived0 = CreateRef(),
                    NullRefFieldInDerived0 = null,
                    RefPropInDerived0 = CreateRef(),
                    NullRefPropInDerived0 = null,
                };
            }
            TestInnerDerived1Class CreateInnerDerived1Class()
            {
                return new TestInnerDerived1Class
                {
                    RefFieldInBase = CreateRef(),
                    NullRefFieldInBase = null,
                    RefPropInBase = CreateRef(),
                    NullRefPropInBase = null,

                    RefFieldInDerived1 = CreateRef(),
                    NullRefFieldInDerived1 = null,
                    RefPropInDerived1 = CreateRef(),
                    NullRefPropInDerived1 = null,
                };
            }
            List<TestInnerClass> CreateInnerClassList()
            {
                return new List<TestInnerClass>
                {
                    CreateInnerClass(),
                    null,
                    CreateInnerClass(),
                };
            }
            List<TestInnerStruct> CreateInnerStructList()
            {
                return new List<TestInnerStruct>
                {
                    CreateInnerStruct(),
                    CreateInnerStruct(),
                };
            }
            List<TestInnerStruct?> CreateInnerNullableStructList()
            {
                return new List<TestInnerStruct?>
                {
                    CreateInnerStruct(),
                    null,
                    CreateInnerStruct(),
                };
            }
            List<MetaRef<TestStringItem>> CreateRefList()
            {
                return new List<MetaRef<TestStringItem>>
                {
                    CreateRef(),
                    null,
                    CreateRef(),
                };
            }
            Dictionary<string, TestInnerClass> CreateInnerClassDict()
            {
                return new Dictionary<string, TestInnerClass>
                {
                    { "a", CreateInnerClass() },
                    { "b", null },
                    { "c", CreateInnerClass() },
                };
            }
            Dictionary<string, TestInnerStruct> CreateInnerStructDict()
            {
                return new Dictionary<string, TestInnerStruct>
                {
                    { "a", CreateInnerStruct() },
                    { "b", CreateInnerStruct() },
                };
            }
            Dictionary<string, TestInnerStruct?> CreateInnerNullableStructDict()
            {
                return new Dictionary<string, TestInnerStruct?>
                {
                    { "a", CreateInnerStruct() },
                    { "b", null },
                    { "c", CreateInnerStruct() },
                };
            }
            Dictionary<string, MetaRef<TestStringItem>> CreateRefDict()
            {
                return new Dictionary<string, MetaRef<TestStringItem>>
                {
                    { "a", CreateRef() },
                    { "b", null },
                    { "c", CreateRef() },
                };
            }
            OrderedDictionary<MetaRef<TestStringItem>, MetaRef<TestStringItem>> CreateRefKeyedOrderedDict()
            {
                return new OrderedDictionary<MetaRef<TestStringItem>, MetaRef<TestStringItem>>
                {
                    { CreateRef(), CreateRef() },
                    { CreateRef(), null },
                    { null, CreateRef() },
                };
            }
            OrderedSet<MetaRef<TestStringItem>> CreateRefOrderedSet()
            {
            return new OrderedSet<MetaRef<TestStringItem>>
                {
                    CreateRef(),
                    null,
                    CreateRef(),
                };
            }

            TopClass top = new TopClass
            {
                Class = new TestOuterClass
                {
                    RefField = CreateRef(),
                    NullRefField = null,
                    RefProp = CreateRef(),
                    NullRefProp = null,

                    InnerClassField = CreateInnerClass(),
                    InnerNullClassField = null,
                    InnerStructField = CreateInnerStruct(),
                    InnerNullableStructField = CreateInnerStruct(),
                    InnerNullNullableStructField = null,
                    InnerDerived0ClassAsBaseField = CreateInnerDerived0Class(),
                    InnerDerived1ClassAsBaseField = CreateInnerDerived1Class(),
                    InnerNullBaseClassField = null,

                    InnerClassProp = CreateInnerClass(),
                    InnerNullClassProp = null,
                    InnerStructProp = CreateInnerStruct(),
                    InnerNullableStructProp = CreateInnerStruct(),
                    InnerNullNullableStructProp = null,
                    InnerDerived0ClassAsBaseProp = CreateInnerDerived0Class(),
                    InnerDerived1ClassAsBaseProp = CreateInnerDerived1Class(),
                    InnerNullBaseClassProp = null,

                    InnerClassList = CreateInnerClassList(),
                    NullInnerClassList = null,
                    InnerStructList = CreateInnerStructList(),
                    InnerNullableStructList = CreateInnerNullableStructList(),
                    RefList = CreateRefList(),

                    InnerClassArray = CreateInnerClassList().ToArray(),
                    NullInnerClassArray = null,
                    InnerStructArray = CreateInnerStructList().ToArray(),
                    InnerNullableStructArray = CreateInnerNullableStructList().ToArray(),
                    RefArray = CreateRefList().ToArray(),

                    InnerClassQueue = CreateInnerClassList().ToQueue(),
                    NullInnerClassQueue = null,
                    InnerStructQueue = CreateInnerStructList().ToQueue(),
                    InnerNullableStructQueue = CreateInnerNullableStructList().ToQueue(),
                    RefQueue = CreateRefList().ToQueue(),

                    InnerClassDict = CreateInnerClassDict(),
                    NullInnerClassDict = null,
                    InnerStructDict = CreateInnerStructDict(),
                    InnerNullableStructDict = CreateInnerNullableStructDict(),
                    RefDict = CreateRefDict(),

                    RefKeyedOrderedDict = CreateRefKeyedOrderedDict(),
                    RefOrderedSet = CreateRefOrderedSet(),
                },
                Struct = new TestOuterStruct
                {
                    RefField = CreateRef(),
                    NullRefField = null,
                    RefProp = CreateRef(),
                    NullRefProp = null,

                    InnerClassField = CreateInnerClass(),
                    InnerNullClassField = null,
                    InnerStructField = CreateInnerStruct(),
                    InnerNullableStructField = CreateInnerStruct(),
                    InnerNullNullableStructField = null,
                    InnerDerived0ClassAsBaseField = CreateInnerDerived0Class(),
                    InnerDerived1ClassAsBaseField = CreateInnerDerived1Class(),
                    InnerNullBaseClassField = null,

                    InnerClassProp = CreateInnerClass(),
                    InnerNullClassProp = null,
                    InnerStructProp = CreateInnerStruct(),
                    InnerNullableStructProp = CreateInnerStruct(),
                    InnerNullNullableStructProp = null,
                    InnerDerived0ClassAsBaseProp = CreateInnerDerived0Class(),
                    InnerDerived1ClassAsBaseProp = CreateInnerDerived1Class(),
                    InnerNullBaseClassProp = null,

                    InnerClassList = CreateInnerClassList(),
                    NullInnerClassList = null,
                    InnerStructList = CreateInnerStructList(),
                    InnerNullableStructList = CreateInnerNullableStructList(),
                    RefList = CreateRefList(),

                    InnerClassArray = CreateInnerClassList().ToArray(),
                    NullInnerClassArray = null,
                    InnerStructArray = CreateInnerStructList().ToArray(),
                    InnerNullableStructArray = CreateInnerNullableStructList().ToArray(),
                    RefArray = CreateRefList().ToArray(),

                    InnerClassQueue = CreateInnerClassList().ToQueue(),
                    NullInnerClassQueue = null,
                    InnerStructQueue = CreateInnerStructList().ToQueue(),
                    InnerNullableStructQueue = CreateInnerNullableStructList().ToQueue(),
                    RefQueue = CreateRefList().ToQueue(),

                    InnerClassDict = CreateInnerClassDict(),
                    NullInnerClassDict = null,
                    InnerStructDict = CreateInnerStructDict(),
                    InnerNullableStructDict = CreateInnerNullableStructDict(),
                    RefDict = CreateRefDict(),

                    RefKeyedOrderedDict = CreateRefKeyedOrderedDict(),
                    RefOrderedSet = CreateRefOrderedSet(),
                },
            };

            TestStringItem[] items = keys.Select(key => new TestStringItem { ConfigKey = key }).ToArray();
            TopClass oldTop = top;
            MetaSerialization.ResolveMetaRefs(ref top, new Resolver(items));
            MetaRefUtil.DebugValidateMetaRefsAreResolved(top, objectName: "");
            // For by-members reference types (as opposed to e.g. collections or structs)
            // the destructive resolve is guaranteed to not shallow-copy the top level object.
            Assert.AreSame(oldTop, top);

            MetaRef<TestStringItem>[] metaRefs = new MetaRef<TestStringItem>[]
            {
                top.Class.RefField,
                top.Class.RefProp,
                top.Class.InnerClassField.RefField,
                top.Class.InnerClassField.RefProp,
                top.Class.InnerStructField.RefField,
                top.Class.InnerStructField.RefProp,
                top.Class.InnerNullableStructField.Value.RefField,
                top.Class.InnerNullableStructField.Value.RefProp,
                top.Class.InnerDerived0ClassAsBaseField.RefFieldInBase,
                top.Class.InnerDerived0ClassAsBaseField.RefPropInBase,
                ((TestInnerDerived0Class)top.Class.InnerDerived0ClassAsBaseField).RefFieldInDerived0,
                ((TestInnerDerived0Class)top.Class.InnerDerived0ClassAsBaseField).RefPropInDerived0,
                top.Class.InnerDerived1ClassAsBaseField.RefFieldInBase,
                top.Class.InnerDerived1ClassAsBaseField.RefPropInBase,
                ((TestInnerDerived1Class)top.Class.InnerDerived1ClassAsBaseField).RefFieldInDerived1,
                ((TestInnerDerived1Class)top.Class.InnerDerived1ClassAsBaseField).RefPropInDerived1,
                top.Class.InnerClassProp.RefField,
                top.Class.InnerClassProp.RefProp,
                top.Class.InnerStructProp.RefField,
                top.Class.InnerStructProp.RefProp,
                top.Class.InnerNullableStructProp.Value.RefField,
                top.Class.InnerNullableStructProp.Value.RefProp,
                top.Class.InnerDerived0ClassAsBaseProp.RefFieldInBase,
                top.Class.InnerDerived0ClassAsBaseProp.RefPropInBase,
                ((TestInnerDerived0Class)top.Class.InnerDerived0ClassAsBaseProp).RefFieldInDerived0,
                ((TestInnerDerived0Class)top.Class.InnerDerived0ClassAsBaseProp).RefPropInDerived0,
                top.Class.InnerDerived1ClassAsBaseProp.RefFieldInBase,
                top.Class.InnerDerived1ClassAsBaseProp.RefPropInBase,
                ((TestInnerDerived1Class)top.Class.InnerDerived1ClassAsBaseProp).RefFieldInDerived1,
                ((TestInnerDerived1Class)top.Class.InnerDerived1ClassAsBaseProp).RefPropInDerived1,
                top.Class.InnerClassList[0].RefField,
                top.Class.InnerClassList[0].RefProp,
                top.Class.InnerClassList[2].RefField,
                top.Class.InnerClassList[2].RefProp,
                top.Class.InnerStructList[0].RefField,
                top.Class.InnerStructList[0].RefProp,
                top.Class.InnerStructList[1].RefField,
                top.Class.InnerStructList[1].RefProp,
                top.Class.InnerNullableStructList[0].Value.RefField,
                top.Class.InnerNullableStructList[0].Value.RefProp,
                top.Class.InnerNullableStructList[2].Value.RefField,
                top.Class.InnerNullableStructList[2].Value.RefProp,
                top.Class.RefList[0],
                top.Class.RefList[2],
                top.Class.InnerClassArray[0].RefField,
                top.Class.InnerClassArray[0].RefProp,
                top.Class.InnerClassArray[2].RefField,
                top.Class.InnerClassArray[2].RefProp,
                top.Class.InnerStructArray[0].RefField,
                top.Class.InnerStructArray[0].RefProp,
                top.Class.InnerStructArray[1].RefField,
                top.Class.InnerStructArray[1].RefProp,
                top.Class.InnerNullableStructArray[0].Value.RefField,
                top.Class.InnerNullableStructArray[0].Value.RefProp,
                top.Class.InnerNullableStructArray[2].Value.RefField,
                top.Class.InnerNullableStructArray[2].Value.RefProp,
                top.Class.RefArray[0],
                top.Class.RefArray[2],
                top.Class.InnerClassQueue.ElementAt(0).RefField,
                top.Class.InnerClassQueue.ElementAt(0).RefProp,
                top.Class.InnerClassQueue.ElementAt(2).RefField,
                top.Class.InnerClassQueue.ElementAt(2).RefProp,
                top.Class.InnerStructQueue.ElementAt(0).RefField,
                top.Class.InnerStructQueue.ElementAt(0).RefProp,
                top.Class.InnerStructQueue.ElementAt(1).RefField,
                top.Class.InnerStructQueue.ElementAt(1).RefProp,
                top.Class.InnerNullableStructQueue.ElementAt(0).Value.RefField,
                top.Class.InnerNullableStructQueue.ElementAt(0).Value.RefProp,
                top.Class.InnerNullableStructQueue.ElementAt(2).Value.RefField,
                top.Class.InnerNullableStructQueue.ElementAt(2).Value.RefProp,
                top.Class.RefQueue.ElementAt(0),
                top.Class.RefQueue.ElementAt(2),
                top.Class.InnerClassDict["a"].RefField,
                top.Class.InnerClassDict["a"].RefProp,
                top.Class.InnerClassDict["c"].RefField,
                top.Class.InnerClassDict["c"].RefProp,
                top.Class.InnerStructDict["a"].RefField,
                top.Class.InnerStructDict["a"].RefProp,
                top.Class.InnerStructDict["b"].RefField,
                top.Class.InnerStructDict["b"].RefProp,
                top.Class.InnerNullableStructDict["a"].Value.RefField,
                top.Class.InnerNullableStructDict["a"].Value.RefProp,
                top.Class.InnerNullableStructDict["c"].Value.RefField,
                top.Class.InnerNullableStructDict["c"].Value.RefProp,
                top.Class.RefDict["a"],
                top.Class.RefDict["c"],
                top.Class.RefKeyedOrderedDict.ElementAt(0).Key,
                top.Class.RefKeyedOrderedDict.ElementAt(0).Value,
                top.Class.RefKeyedOrderedDict.ElementAt(1).Key,
                top.Class.RefKeyedOrderedDict.ElementAt(2).Value,
                top.Class.RefOrderedSet.ElementAt(0),
                top.Class.RefOrderedSet.ElementAt(2),

                top.Struct.RefField,
                top.Struct.RefProp,
                top.Struct.InnerClassField.RefField,
                top.Struct.InnerClassField.RefProp,
                top.Struct.InnerStructField.RefField,
                top.Struct.InnerStructField.RefProp,
                top.Struct.InnerNullableStructField.Value.RefField,
                top.Struct.InnerNullableStructField.Value.RefProp,
                top.Struct.InnerDerived0ClassAsBaseField.RefFieldInBase,
                top.Struct.InnerDerived0ClassAsBaseField.RefPropInBase,
                ((TestInnerDerived0Class)top.Struct.InnerDerived0ClassAsBaseField).RefFieldInDerived0,
                ((TestInnerDerived0Class)top.Struct.InnerDerived0ClassAsBaseField).RefPropInDerived0,
                top.Struct.InnerDerived1ClassAsBaseField.RefFieldInBase,
                top.Struct.InnerDerived1ClassAsBaseField.RefPropInBase,
                ((TestInnerDerived1Class)top.Struct.InnerDerived1ClassAsBaseField).RefFieldInDerived1,
                ((TestInnerDerived1Class)top.Struct.InnerDerived1ClassAsBaseField).RefPropInDerived1,
                top.Struct.InnerClassProp.RefField,
                top.Struct.InnerClassProp.RefProp,
                top.Struct.InnerStructProp.RefField,
                top.Struct.InnerStructProp.RefProp,
                top.Struct.InnerNullableStructProp.Value.RefField,
                top.Struct.InnerNullableStructProp.Value.RefProp,
                top.Struct.InnerDerived0ClassAsBaseProp.RefFieldInBase,
                top.Struct.InnerDerived0ClassAsBaseProp.RefPropInBase,
                ((TestInnerDerived0Class)top.Struct.InnerDerived0ClassAsBaseProp).RefFieldInDerived0,
                ((TestInnerDerived0Class)top.Struct.InnerDerived0ClassAsBaseProp).RefPropInDerived0,
                top.Struct.InnerDerived1ClassAsBaseProp.RefFieldInBase,
                top.Struct.InnerDerived1ClassAsBaseProp.RefPropInBase,
                ((TestInnerDerived1Class)top.Struct.InnerDerived1ClassAsBaseProp).RefFieldInDerived1,
                ((TestInnerDerived1Class)top.Struct.InnerDerived1ClassAsBaseProp).RefPropInDerived1,
                top.Struct.InnerClassList[0].RefField,
                top.Struct.InnerClassList[0].RefProp,
                top.Struct.InnerClassList[2].RefField,
                top.Struct.InnerClassList[2].RefProp,
                top.Struct.InnerStructList[0].RefField,
                top.Struct.InnerStructList[0].RefProp,
                top.Struct.InnerStructList[1].RefField,
                top.Struct.InnerStructList[1].RefProp,
                top.Struct.InnerNullableStructList[0].Value.RefField,
                top.Struct.InnerNullableStructList[0].Value.RefProp,
                top.Struct.InnerNullableStructList[2].Value.RefField,
                top.Struct.InnerNullableStructList[2].Value.RefProp,
                top.Struct.RefList[0],
                top.Struct.RefList[2],
                top.Struct.InnerClassArray[0].RefField,
                top.Struct.InnerClassArray[0].RefProp,
                top.Struct.InnerClassArray[2].RefField,
                top.Struct.InnerClassArray[2].RefProp,
                top.Struct.InnerStructArray[0].RefField,
                top.Struct.InnerStructArray[0].RefProp,
                top.Struct.InnerStructArray[1].RefField,
                top.Struct.InnerStructArray[1].RefProp,
                top.Struct.InnerNullableStructArray[0].Value.RefField,
                top.Struct.InnerNullableStructArray[0].Value.RefProp,
                top.Struct.InnerNullableStructArray[2].Value.RefField,
                top.Struct.InnerNullableStructArray[2].Value.RefProp,
                top.Struct.RefArray[0],
                top.Struct.RefArray[2],
                top.Struct.InnerClassQueue.ElementAt(0).RefField,
                top.Struct.InnerClassQueue.ElementAt(0).RefProp,
                top.Struct.InnerClassQueue.ElementAt(2).RefField,
                top.Struct.InnerClassQueue.ElementAt(2).RefProp,
                top.Struct.InnerStructQueue.ElementAt(0).RefField,
                top.Struct.InnerStructQueue.ElementAt(0).RefProp,
                top.Struct.InnerStructQueue.ElementAt(1).RefField,
                top.Struct.InnerStructQueue.ElementAt(1).RefProp,
                top.Struct.InnerNullableStructQueue.ElementAt(0).Value.RefField,
                top.Struct.InnerNullableStructQueue.ElementAt(0).Value.RefProp,
                top.Struct.InnerNullableStructQueue.ElementAt(2).Value.RefField,
                top.Struct.InnerNullableStructQueue.ElementAt(2).Value.RefProp,
                top.Struct.RefQueue.ElementAt(0),
                top.Struct.RefQueue.ElementAt(2),
                top.Struct.InnerClassDict["a"].RefField,
                top.Struct.InnerClassDict["a"].RefProp,
                top.Struct.InnerClassDict["c"].RefField,
                top.Struct.InnerClassDict["c"].RefProp,
                top.Struct.InnerStructDict["a"].RefField,
                top.Struct.InnerStructDict["a"].RefProp,
                top.Struct.InnerStructDict["b"].RefField,
                top.Struct.InnerStructDict["b"].RefProp,
                top.Struct.InnerNullableStructDict["a"].Value.RefField,
                top.Struct.InnerNullableStructDict["a"].Value.RefProp,
                top.Struct.InnerNullableStructDict["c"].Value.RefField,
                top.Struct.InnerNullableStructDict["c"].Value.RefProp,
                top.Struct.RefDict["a"],
                top.Struct.RefDict["c"],
                top.Struct.RefKeyedOrderedDict.ElementAt(0).Key,
                top.Struct.RefKeyedOrderedDict.ElementAt(0).Value,
                top.Struct.RefKeyedOrderedDict.ElementAt(1).Key,
                top.Struct.RefKeyedOrderedDict.ElementAt(2).Value,
                top.Struct.RefOrderedSet.ElementAt(0),
                top.Struct.RefOrderedSet.ElementAt(2),
            };

            Assert.AreEqual(items, metaRefs.Select(metaRef => metaRef.Ref).ToArray());
        }

        [MetaSerializable]
        public class RefKeyedDictWrapper
        {
            [MetaMember(1)] public Dictionary<MetaRef<TestStringItem>, string> Dict;
        }

        [Test]
        public void AliasDictKeyResolve()
        {
            TestStringItem item0 = new TestStringItem{ ConfigKey = "test0" };
            TestStringItem item1 = new TestStringItem{ ConfigKey = "test1" };

            Dictionary<MetaRef<TestStringItem>, string> dict = new Dictionary<MetaRef<TestStringItem>, string>();
            dict.Add(MetaRef<TestStringItem>.FromKey("test0"), "value_test0");
            dict.Add(MetaRef<TestStringItem>.FromKey("alias1"), "value_alias1");

            Resolver resolver = new Resolver();
            resolver.Add(item0);
            resolver.Add("alias1", item1);

            RefKeyedDictWrapper wrapper = new RefKeyedDictWrapper{ Dict = dict };
            MetaSerialization.ResolveMetaRefs(ref wrapper, resolver);
            MetaRefUtil.DebugValidateMetaRefsAreResolved(wrapper, objectName: "");
            dict = wrapper.Dict;
            Assert.AreEqual(2, dict.Count);
            Assert.AreEqual("value_test0", dict[MetaRef<TestStringItem>.FromItem(item0)]);
            Assert.AreEqual("value_alias1", dict[MetaRef<TestStringItem>.FromItem(item1)]);
        }

        [Test]
        public void AliasDictKeyResolveInvalid()
        {
            TestStringItem item = new TestStringItem{ ConfigKey = "test" };

            Dictionary<MetaRef<TestStringItem>, string> dict = new Dictionary<MetaRef<TestStringItem>, string>();
            dict.Add(MetaRef<TestStringItem>.FromKey("test"), "value_test");
            dict.Add(MetaRef<TestStringItem>.FromKey("alias"), "value_alias");

            Resolver resolver = new Resolver();
            resolver.Add(item);
            resolver.Add("alias", item);

            RefKeyedDictWrapper wrapper = new RefKeyedDictWrapper{ Dict = dict };
            // This will throw because the resolve would produce duplicate dictionary keys due to the alias.
            Assert.Catch<ArgumentException>(() => MetaSerialization.ResolveMetaRefs(ref wrapper, resolver));
        }

        [MetaSerializable]
        public class RefOrderedSetWrapper
        {
            [MetaMember(1)] public OrderedSet<MetaRef<TestStringItem>> Set;
        }

        [Test]
        public void AliasSetResolve()
        {
            TestStringItem item0 = new TestStringItem{ ConfigKey = "test0" };
            TestStringItem item1 = new TestStringItem{ ConfigKey = "test1" };

            OrderedSet<MetaRef<TestStringItem>> set = new OrderedSet<MetaRef<TestStringItem>>();
            set.Add(MetaRef<TestStringItem>.FromKey("test0"));
            set.Add(MetaRef<TestStringItem>.FromKey("alias0"));
            set.Add(MetaRef<TestStringItem>.FromKey("test1"));
            set.Add(MetaRef<TestStringItem>.FromKey("alias1"));

            Assert.AreEqual(4, set.Count);
            Assert.Contains(MetaRef<TestStringItem>.FromKey("test0"), set);
            Assert.Contains(MetaRef<TestStringItem>.FromKey("alias0"), set);
            Assert.Contains(MetaRef<TestStringItem>.FromKey("test1"), set);
            Assert.Contains(MetaRef<TestStringItem>.FromKey("alias1"), set);

            Resolver resolver = new Resolver();
            resolver.Add(item0);
            resolver.Add("alias0", item0);
            resolver.Add(item1);
            resolver.Add("alias1", item1);

            RefOrderedSetWrapper wrapper = new RefOrderedSetWrapper{ Set = set };
            MetaSerialization.ResolveMetaRefs(ref wrapper, resolver);
            MetaRefUtil.DebugValidateMetaRefsAreResolved(wrapper, objectName: "");
            set = wrapper.Set;
            Assert.AreEqual(2, set.Count);
            Assert.Contains(MetaRef<TestStringItem>.FromItem(item0), set);
            Assert.Contains(MetaRef<TestStringItem>.FromItem(item1), set);
        }

        #region SerializerGenerationBugPreR24

        // Tests a scenario that used to cause the serializer generator to produce non-compiling code
        // (trying to call a nonexistent method) due to a bug in the generator.
        // Bug: ConcreteClassOrStructMembersInfo.AppendMembersMetaRefResolveCode was accidentally being
        //      given the metaRefByMembersContainingTypes, not metaRefContainingTypes.
        //      This meant it would try to call the MetaRef resolving function for some types for which
        //      it was not generated (because the type contains MetaRefs only when serialized by-members,
        //      not normally).
        // In this particular example: *ConfigB contains MetaRefs when serialized by-members, but not when
        // serialized "by-key". The "resolve MetaRefs members in *Class" function ends up trying to call
        // the "resolve MetaRefs in *ConfigB (when serialized by-key)" function even though it does not exist.
        // (*Class contains an actual MetaRef (to the otherwise irrelevant *ConfigA) so that the aforementioned
        //  "resolve MetaRefs members in *Class" function gets generated in the first place.)
        //
        // This was fixed in R24.

        [MetaSerializable]
        public class SerializerGenerationBugPreR24Class
        {
            [MetaMember(1)] public MetaRef<SerializerGenerationBugPreR24ConfigA> A;
            [MetaMember(2)] public SerializerGenerationBugPreR24ConfigB B;
        }

        [MetaSerializable]
        public class SerializerGenerationBugPreR24ConfigA : IGameConfigData<string>
        {
            [MetaMember(1)] public string ConfigKey { get; private set; }
        }

        [MetaSerializable]
        public class SerializerGenerationBugPreR24ConfigB : IGameConfigData<string>
        {
            [MetaMember(1)] public string ConfigKey { get; private set; }
            [MetaMember(2)] public MetaRef<SerializerGenerationBugPreR24ConfigC> C { get; private set; }
        }

        [MetaSerializable]
        public class SerializerGenerationBugPreR24ConfigC : IGameConfigData<string>
        {
            [MetaMember(1)] public string ConfigKey { get; private set; }
        }

        #endregion
    }

    static class MetaRefTestsExtensionHelpers
    {
        public static Queue<T> ToQueue<T>(this IEnumerable<T> enumerable)
            => new Queue<T>(enumerable);
    }
}
