// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Config;
using Metaplay.Core.Serialization;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using static System.FormattableString;

namespace Metaplay.Core
{
    // \todo [nuutti] Consider generalizing to more than just config references.
    //                Otherwise, move to Metaplay.Core.Config.
    //                #generalized-reference

    public interface IMetaRef
    {
        IMetaRef CreateResolved(IGameConfigDataResolver resolver);

        Type ItemType { get; }
        object KeyObject { get; }
        object MaybeRefObject { get; }
        bool IsResolved { get; }
    }

    /// <summary>
    /// Holds a reference to an item identified by a key.
    /// A MetaRef instance always represents a non-null reference;
    /// a null reference is instead represented by the MetaRef-
    /// typed variable itself being null.
    /// <para>
    /// Each MetaRef object is either non-resolved or resolved.
    /// A non-resolved MetaRef holds only the key.
    /// A resolved MetaRef holds a concrete reference to an item.
    /// Given a non-resolved MetaRef and a reference resolver,
    /// <see cref="CreateResolved(IGameConfigDataResolver)"/>
    /// can be used to create a corresponding resolved MetaRef.
    /// Note that due to config aliases, the resulting resolved
    /// MetaRef's <see cref="KeyObject"/> is not necessarily equal
    /// to that of the non-resolved MetaRef.
    /// </para>
    /// </summary>
    /// <remarks>
    /// <para>
    /// The motivating use case for MetaRefs involves inter-config
    /// references. Config references within config data are
    /// parsed/deserialized as non-resolved, and are only resolved
    /// (using <see cref="MetaSerialization.ResolveMetaRefs(Type, ref object, IGameConfigDataResolver)"/>
    /// or its variants) after all config libraries have been imported.
    /// This way arbitrary inter-config references (including forward
    /// references) are supported.
    /// </para>
    /// <para>
    /// The serialized representation of a MetaRef is the same as that
    /// of its <see cref="KeyObject"/>. When deserializing, whether
    /// a resolved or non-resolved MetaRef is produced depends on
    /// whether a reference resolved is available.
    /// \note The serialization/deserialization of null MetaRefs is
    ///       currently only supported when the key type is nullable
    ///       (either a reference type, or a <see cref="Nullable"/>).
    ///       Trying to serialize a MetaRef with a non-nullable key type
    ///       throws at serialization time.
    ///       #null-config-ref-serialization
    /// </para>
    /// </remarks>
    public class MetaRef<TItem> : IMetaRef, IEquatable<MetaRef<TItem>>
        where TItem : class, IGameConfigData // \todo #generalized-reference
    {
        readonly object _key;
        readonly TItem _item;

        /// <summary>
        /// The key of this MetaRef. This is never null, and is present
        /// for both non-resolved and resolved MetaRefs.
        /// </summary>
        /// <remarks>
        /// This has static type 'object' because we don't have the key type
        /// statically available here. To have it statically available, it
        /// would need to be an additional type parameter to MetaRef, which
        /// would make MetaRef more verbose to use.
        /// </remarks>
        public object KeyObject => _key;

        /// <summary>
        /// The reference to the item, if this MetaRef is resolved, and null otherwise.
        /// </summary>
        public TItem MaybeRef => _item;
        /// <summary>
        /// The reference to the item, if this MetaRef is resolved. Throws otherwise.
        /// </summary>
        public TItem Ref => _item ?? throw new InvalidOperationException($"Tried to get reference to '{_key}' from MetaRef<{typeof(TItem).Name}> but the reference hasn't yet been resolved");

        public bool IsResolved => _item != null;

        Type IMetaRef.ItemType => typeof(TItem);
        object IMetaRef.MaybeRefObject => _item;

        static readonly Type KeyType =
            typeof(TItem)
            .GetGenericInterfaceTypeArguments(typeof(IGameConfigKey<>))[0];

        static readonly PropertyInfo KeyProperty =
            typeof(TItem)
            .GetGenericInterface(typeof(IGameConfigKey<>))
            .GetProperty(nameof(IGameConfigKey<int>.ConfigKey));

        MetaRef(object key, TItem item)
        {
            CheckKeyValidity(key);
            _key = key;
            _item = item;
        }

        /// <summary>
        /// Create a non-resolved MetaRef with the specified key. The key must not be null,
        /// and must be of an appropriate type for TItem.
        /// </summary>
        public static MetaRef<TItem> FromKey(object key)
        {
            return new MetaRef<TItem>(
                key: key,
                item: null);
        }

        /// <summary>
        /// Create a resolved MetaRef referring to the given item. The item must not be null.
        /// </summary>
        public static MetaRef<TItem> FromItem(TItem item)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item), $"MetaRef<{typeof(TItem).Name}>: cannot be created from a null item reference. Perhaps you meant to use a null MetaRef reference instead?");

            return new MetaRef<TItem>(
                key: KeyProperty.GetValue(item),
                item: item);
        }

        /// <summary>
        /// Using the given resolver, create a resolved MetaRef by resolving the key of this
        /// MetaRef. Note that the resulting resolved MetaRef might have a different KeyObject
        /// than this one, due to config aliases. The resolved MetaRef will have the
        /// "canonical" key, i.e. the actual key of the item.
        /// </summary>
        public MetaRef<TItem> CreateResolved(IGameConfigDataResolver resolver) // \todo #generalized-reference
        {
            TItem item = (TItem)resolver.ResolveReference(typeof(TItem), _key);
            return FromItem(item);
        }
        IMetaRef IMetaRef.CreateResolved(IGameConfigDataResolver resolver)
            => CreateResolved(resolver);

        public MetaRef<TOtherItem> CastItem<TOtherItem>()
            where TOtherItem : class, IGameConfigData
        {
            return new MetaRef<TOtherItem>(
                _key,
                (TOtherItem)(object)_item);
        }

        static void CheckKeyValidity(object key)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key), $"MetaRef<{typeof(TItem).Name}>: cannot have null key");

            if (!KeyType.IsAssignableFrom(key.GetType()))
            {
                throw new InvalidOperationException(
                    $"Key '{key}' of type {key.GetType().ToGenericTypeString()} cannot be used as a key in MetaRef<{typeof(TItem).Name}>, "
                    + $"because {typeof(TItem).Name} is a {typeof(TItem).GetGenericInterface(typeof(IGameConfigData<>)).ToGenericTypeString()} which has key type {KeyType.ToGenericTypeString()}");
            }
        }

        public override string ToString()
        {
            if (IsResolved)
                return Invariant($"(resolved: {KeyObject})");
            else
                return Invariant($"(non-resolved: {KeyObject})");
        }

        /// <summary>
        /// Two MetaRefs are equal if either:
        /// - both are resolved, and refer to the same item
        /// - neither is resolved, their keys are equal, and their item types are compatible
        ///   (i.e. assignable to each other)
        ///
        /// \todo [nuutti] These are pretty complex equality semantics. Is there a simpler
        ///                acceptable definition?
        /// </summary>
        bool EqualsImpl(IMetaRef other)
        {
            if (other is null)
                return false;

            // If either is resolved, then both must be resolved and refer to the same item.
            object otherItemMaybe = other.MaybeRefObject;
            if (!(_item is null) || !(otherItemMaybe is null))
                return ReferenceEquals(_item, otherItemMaybe);

            // Neither is resolved.
            // One item type be assignable to the other, and keys must be equal.
            Type otherItemType = other.ItemType;
            return (typeof(TItem).IsAssignableFrom(otherItemType) || otherItemType.IsAssignableFrom(typeof(TItem)))
                && _key.Equals(other.KeyObject);
        }

        /// <inheritdoc cref="EqualsImpl"/>
        public override bool Equals(object obj)
            => obj is IMetaRef other && EqualsImpl(other);

        /// <inheritdoc cref="EqualsImpl"/>
        public bool Equals(MetaRef<TItem> other)
            => EqualsImpl(other);

        public override int GetHashCode() => _key.GetHashCode();

        public static bool operator ==(MetaRef<TItem> a, MetaRef<TItem> b)
        {
            if (a is null)
                return b is null;
            return a.Equals(b);
        }

        public static bool operator !=(MetaRef<TItem> a, MetaRef<TItem> b)
        {
            return !(a == b);
        }
    }

    public static class MetaRefUtil
    {
        // Dummy type, only used to statically assert the type and name of MetaRef<>.FromKey .
        // See DummyFromKey below. This type is not actually used at runtime.
        class DummyGameConfigData : IGameConfigData<int>
        {
            public int ConfigKey => throw new NotImplementedException();
        }

        // This, too, only exists to assert the static type and name of MetaRef<>.FromKey .
        // If FromKey's type is changed, you should update the usage of fromKeyMethod.Invoke
        // in the CreateFromKey method below.
        static readonly Func<object, MetaRef<DummyGameConfigData>> DummyFromKey = MetaRef<DummyGameConfigData>.FromKey;

        /// <summary>
        /// Dynamic-typed helper for <see cref="MetaRef{TItem}.FromKey(object)"/>
        /// </summary>
        public static IMetaRef CreateFromKey(Type metaRefType, object key)
        {
            MethodInfo fromKeyMethod = metaRefType.GetMethod(DummyFromKey.Method.Name, BindingFlags.Public | BindingFlags.Static)
                                       ?? throw new InvalidOperationException($"No public static {DummyFromKey.Method.Name} method found from {metaRefType.ToGenericTypeString()}!");
            return (IMetaRef)fromKeyMethod.Invoke(null, new object[]{ key });
        }

        /// <summary>
        /// Traverse the object tree and check that it doesn't contain
        /// non-resolved MetaRefs. This can be used to debug-validate
        /// that <see cref="MetaSerialization.ResolveMetaRefs(Type, ref object, IGameConfigDataResolver)"/>
        /// did not accidentally leave any MetaRefs uresolved (if it did, that's
        /// a bug).
        ///
        /// Since this debug check uses reflection to traverse the object
        /// tree, it is quite inefficient for large amounts of data, and
        /// is thus only meant to be used ad-hoc at development time.
        /// </summary>
        public static void DebugValidateMetaRefsAreResolved(object obj, string objectName)
        {
            DebugValidateMetaRefsAreResolvedImpl(
                obj,
                depth: 0,
                accessPath: new string[]{ objectName },
                processedObjects: new HashSet<ObjectIdentity>());
        }

        public class MetaRefResolvedDebugValidationError : Exception
        {
            public MetaRefResolvedDebugValidationError(string message, Exception innerException)
                : base(message, innerException)
            {
            }
        }

        static void DebugValidateMetaRefsAreResolvedImpl(object obj, int depth, IEnumerable<string> accessPath, HashSet<ObjectIdentity> processedObjects)
        {
            try
            {
                if (depth > 50)
                    throw new InvalidOperationException($"Recursion depth too big ({depth}), probably infinite recursion.");

                if (obj is null)
                    return;

                Type type = obj.GetType();

                // Skip objects already seen. Only for non-value types. Value-types cannot contain themselves transitively.
                ObjectIdentity objId = new ObjectIdentity(obj);
                if (!type.IsValueType && !processedObjects.Add(objId))
                    return;

                if (obj is Type
                 || TaggedWireSerializer.IsBuiltinType(obj.GetType())
                 || (obj is IGameConfigData && depth != 0)
                 || obj is MemberInfo
                 || obj is Delegate)
                {
                }
                else if (obj is IMetaRef metaRef)
                {
                    if (!metaRef.IsResolved)
                        throw new InvalidOperationException($"Encountered non-resolved {metaRef.GetType().ToGenericTypeString()}: {metaRef} .");
                }
                else if (obj is IDictionary dictionary)
                {
                    IDictionaryEnumerator enumerator = dictionary.GetEnumerator();
                    while (enumerator.MoveNext())
                    {
                        DictionaryEntry entry = enumerator.Entry;
                        DebugValidateMetaRefsAreResolvedImpl(entry.Key, depth+1, accessPath.Append(":Key"), processedObjects);
                        DebugValidateMetaRefsAreResolvedImpl(entry.Value, depth+1, accessPath.Append(":Value"), processedObjects);
                    }
                }
                else if (obj is IEnumerable enumerable)
                {
                    foreach (object element in enumerable)
                    {
                        DebugValidateMetaRefsAreResolvedImpl(element, depth+1, accessPath.Append(":Element"), processedObjects);
                    }
                }
                else
                {
                    foreach (MemberInfo memberInfo in type.EnumerateInstanceDataMembersInUnspecifiedOrder())
                    {
                        // Skip indexers.
                        if (memberInfo is PropertyInfo propertyInfo && propertyInfo.GetIndexParameters().Length != 0)
                            continue;

                        Func<object, object> getter = memberInfo.GetDataMemberGetValueOnDeclaringType();
                        // Skip non-gettable properties.
                        if (getter == null)
                            continue;

                        object memberObj = getter(obj);
                        DebugValidateMetaRefsAreResolvedImpl(memberObj, depth+1, accessPath.Append($".{memberInfo.Name}"), processedObjects);
                    }
                }
            }
            catch(Exception ex)
            {
                if (ex is MetaRefResolvedDebugValidationError)
                    throw;
                else
                {
                    throw new MetaRefResolvedDebugValidationError(
                        "Error when debug-validating that all MetaRefs are resolved."
                        + $" This is probably an internal error in the Metaplay SDK. Access path: {string.Concat(accessPath)}",
                        ex);
                }
            }
        }
    }
}
