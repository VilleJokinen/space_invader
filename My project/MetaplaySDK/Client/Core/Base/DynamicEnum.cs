// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Metaplay.Core
{
    public interface IDynamicEnum
    {
        int     Id    { get; }
        string  Name  { get; }
    }

    public static class DynamicEnumUtil
    {
        static readonly Lazy<Dictionary<Type, Dictionary<int, IDynamicEnum>>> _dict = new Lazy<Dictionary<Type, Dictionary<int, IDynamicEnum>>>(
            () =>
            {
                Dictionary<Type, Dictionary<int, IDynamicEnum>> result = new Dictionary<Type, Dictionary<int, IDynamicEnum>>();
                foreach (Type enumType in TypeScanner.GetInterfaceImplementations<IDynamicEnum>())
                {
                    Dictionary<int, IDynamicEnum> values = new Dictionary<int, IDynamicEnum>();
                    foreach (IDynamicEnum value in enumType.GetStaticFieldsOfType<IDynamicEnum>())
                        values.Add(value.Id, value);
                    result.Add(enumType, values);
                }

                return result;
            });

        public static IDynamicEnum FromId(Type type, int id)
        {
            return _dict.Value[type][id];
        }
    }

    /// <summary>
    /// DynamicEnum enables enum-like pattern without constraining element declarations to a single assembly. This
    /// provides the flexibility of opaque Ids while still remaining exhaustively Enumerable. Additionally, as each
    /// element is an object instance, they may contain custom data fields much like Enums in Java. Upon app startup,
    /// the enumerations are collected across all app assemblies. New Enumerations cannot be added at runtime.
    /// <para>
    /// Each dynamic enum should inherit this type giving the type itself as the generic argument. (Curiously recurring
    /// template pattern).
    /// </para>
    /// <code>
    /// // Example:                                                                                                                                         <br/>
    /// public class FooOperationType : DynamicEnum&lt;FooOperationType&gt;                                                                                 <br/>
    /// {                                                                                                                                                   <br/>
    ///     public readonly string Description;                                                                                                             <br/>
    ///     protected FooOperationType(int value, string name, string description) : base(value, name, isValid: true)                                       <br/>
    ///     {                                                                                                                                               <br/>
    ///         Description = description;                                                                                                                  <br/>
    ///     }                                                                                                                                               <br/>
    /// }                                                                                                                                                   <br/>
    ///                                                                                                                                                     <br/>
    /// // In DashboardFooControllers                                                                                                                       <br/>
    ///                                                                                                                                                     <br/>
    /// public class DashFooOperationType : FooOperationType                                                                                                <br/>
    /// {                                                                                                                                                   <br/>
    ///     public static readonly DashFooOperationType OpDashBuy = new DashFooOperationType(0, nameof(OpDashBuy), "buy it");                               <br/>
    ///     public static readonly DashFooOperationType OpDashUse = new DashFooOperationType(1, nameof(OpDashUse), "use it");                               <br/>
    ///                                                                                                                                                     <br/>
    ///     DashFooOperationType(int value, string name, string description) : base(value, name, description) { }                                           <br/>
    /// }                                                                                                                                                   <br/>
    ///                                                                                                                                                     <br/>
    /// // In BackgroundFooManager                                                                                                                          <br/>
    ///                                                                                                                                                     <br/>
    /// public class BackgroundFooOperationType : FooOperationType                                                                                          <br/>
    /// {                                                                                                                                                   <br/>
    ///     public static readonly BackgroundFooOperationType OpBkgmBreak = new BackgroundFooOperationType(10, nameof(OpDashBreak), "break it");            <br/>
    ///     public static readonly BackgroundFooOperationType OpBkgmFix = new BackgroundFooOperationType(11, nameof(OpDashFix), "fix it");                  <br/>
    ///                                                                                                                                                     <br/>
    ///     BackgroundFooOperationType(int value, string name, string description) : base(value, name, description) { }                                     <br/>
    /// }                                                                                                                                                   <br/>
    /// </code>
    /// </summary>
    public class DynamicEnum<TEnum> :
        IDynamicEnum,
        IEquatable<DynamicEnum<TEnum>>,
        IComparable<DynamicEnum<TEnum>>,
        IComparable
        where TEnum : DynamicEnum<TEnum>
    {
        static readonly Lazy<List<TEnum>>               _allValues      = new Lazy<List<TEnum>>(() => FindAllValues());
        static readonly Lazy<Dictionary<string, TEnum>> _nameToValue    = new Lazy<Dictionary<string, TEnum>>(() => _allValues.Value.ToDictionary(item => item.Name));
        static readonly Lazy<Dictionary<int, TEnum>>    _idToValue      = new Lazy<Dictionary<int, TEnum>>(() => _allValues.Value.ToDictionary(item => item.Id));

        public static List<TEnum> AllValues => _allValues.Value;

        public int      Id      { get; private set; }
        public string   Name    { get; private set; }
        public bool     IsValid { get; private set; }

        protected DynamicEnum(int id, string name, bool isValid)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentNullException(nameof(name));

            Id = id;
            Name = name;
            IsValid = isValid;
        }

        public override string ToString() => Name;

        public override int GetHashCode() => Id;

        // \note IEquatable<DynamicEnum<TEnum>>.Equals(DynamicEnum<TEnum> other) is the actual equality implementation here,
        //       other equality methods are based on it.

        bool IEquatable<DynamicEnum<TEnum>>.Equals(DynamicEnum<TEnum> other) => !ReferenceEquals(other, null) && Id == other.Id;

        public override bool Equals(object obj)
        {
            if (obj is TEnum other)
                return ((IEquatable<DynamicEnum<TEnum>>)this).Equals(other);
            else
                return false;
        }

        public static bool operator ==(DynamicEnum<TEnum> a, DynamicEnum<TEnum> b)
        {
            if (ReferenceEquals(a, null))
                return ReferenceEquals(b, null);
            return ((IEquatable<DynamicEnum<TEnum>>)a).Equals(b);
        }
        public static bool operator !=(DynamicEnum<TEnum> a, DynamicEnum<TEnum> b) => !(a == b);

        int IComparable<DynamicEnum<TEnum>>.CompareTo(DynamicEnum<TEnum> other) => ReferenceEquals(other, null) ? +1 : Id.CompareTo(other.Id);

        int IComparable.CompareTo(object obj) => (obj is DynamicEnum<TEnum> other) ? Id.CompareTo(other.Id) : 1;

        public static TEnum FromName(string name)
        {
            if (TryFromName(name, out TEnum value))
                return value;
            else
                throw new ArgumentException($"Unknown name for <{typeof(TEnum).Name}> '{name}'");
        }

        public static bool TryFromName(string name, out TEnum value)
        {
            return _nameToValue.Value.TryGetValue(name, out value);
        }

        public static TEnum FromId(int id)
        {
            if (TryFromId(id, out TEnum value))
                return value;
            else
                throw new ArgumentException($"Unknown id for <{typeof(TEnum).Name}> '{id}'");
        }

        public static bool TryFromId(int id, out TEnum value)
        {
            return _idToValue.Value.TryGetValue(id, out value);
        }

        private static List<TEnum> FindAllValues()
        {
            Dictionary<int, TEnum> result = new Dictionary<int, TEnum>();

            foreach (Type enumType in TypeScanner.GetDerivedTypesAndSelf<TEnum>())
            {
                foreach (TEnum value in enumType.GetStaticFieldsOfType<TEnum>())
                {
                    // Skip invalid values
                    if (!value.IsValid)
                        continue;

                    // Check for duplicates
                    if (result.TryGetValue(value.Id, out TEnum existing))
                        throw new InvalidOperationException($"Duplicate {enumType} id #{value.Id}: used by {value.Name} and {existing.Name}");

                    result.Add(value.Id, value);
                }
            }

            return result.Values.ToList();
        }
    }
}
