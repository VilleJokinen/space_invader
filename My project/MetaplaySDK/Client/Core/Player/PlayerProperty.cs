// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Math;
using Metaplay.Core.Model;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Metaplay.Core.Player
{
    /// <summary>
    /// A PlayerPropertyId identifies a specific property of a player,
    /// for example: number of gems owned by the player; player account age;
    /// player's last known country.
    /// </summary>
    /// <remarks>
    /// You'll probably want to use <see cref="TypedPlayerPropertyId{TValue}"/> instead.
    /// </remarks>
    [MetaAllowNoSerializedMembers]
    [MetaSerializable]
    public abstract class PlayerPropertyId
    {
        /// <summary>
        /// The type of the property identified by this PlayerPropertyId.
        /// This should be the type of objects returned by GetValueForPlayer.
        /// </summary>
        public abstract Type PropertyType { get; }

        /// <summary>
        /// Extract the value of the property from the given player.
        /// The returned object should be of type PropertyType.
        /// </summary>
        public abstract object GetValueForPlayer(IPlayerModelBase player);

        /// <summary>
        /// Display name for dashboard
        /// </summary>
        public abstract string DisplayName { get; }
    }

    /// <summary>
    /// Static typing helper for PlayerPropertyId.
    /// </summary>
    public abstract class TypedPlayerPropertyId<TValue> : PlayerPropertyId
    {
        public override Type PropertyType => typeof(TValue);

        public sealed override object GetValueForPlayer(IPlayerModelBase player)
        {
            return GetTypedValueForPlayer(player);
        }

        public abstract TValue GetTypedValueForPlayer(IPlayerModelBase player);
    }

    /// <summary>
    /// Represents a requirement (a condition) on a specific property of a player.
    ///
    /// For comparable (ordered) properties (like integers or times), represents
    /// a condition of the form "Min <= propValue && propValue <= Max", except
    /// that either Min or Max can be omitted.
    ///
    /// For non-comparable (non-ordered) properties (like bool or string), represents
    /// a condition of the form "propValue == RequiredValue". In this case, Min represents
    /// RequiredValue.
    /// </summary>
    [MetaSerializable]
    public class PlayerPropertyRequirement
    {
        [MetaMember(1)] public PlayerPropertyId         Id  { get; private set; }
        [MetaMember(2)] public PlayerPropertyConstant   Min { get; private set; }
        [MetaMember(3)] public PlayerPropertyConstant   Max { get; private set; }

        /// <summary>
        /// Parse the property requirement from the given min and max strings, for the given PlayerPropertyId.
        /// The syntax of the strings depends on id.PropertyType.
        /// </summary>
        public static PlayerPropertyRequirement ParseFromStrings(PlayerPropertyId id, string minStr, string maxStr)
        {
            PlayerPropertyKind      propertyKind    = PlayerPropertyKindUtil.GetPropertyKindForType(id.PropertyType);
            PlayerPropertyConstant  min             = string.IsNullOrEmpty(minStr) ? (PlayerPropertyConstant)null : PlayerPropertyConstant.Parse(propertyKind, minStr);
            PlayerPropertyConstant  max             = string.IsNullOrEmpty(maxStr) ? (PlayerPropertyConstant)null : PlayerPropertyConstant.Parse(propertyKind, maxStr);

            return new PlayerPropertyRequirement(id, min, max);
        }

        /// <summary>
        /// Test whether the property condition is fulfilled for the given player.
        /// </summary>
        public bool MatchesPlayer(IPlayerModelBase player)
        {
            PlayerPropertyKind  propertyKind    = PlayerPropertyKindUtil.GetPropertyKindForType(Id.PropertyType);
            object              value           = Id.GetValueForPlayer(player);

            if (PlayerPropertyKindUtil.IsComparablePropertyKind(propertyKind))
            {
                return (Min == null || Compare(propertyKind, value, Min.ConstantValue) >= 0)
                    && (Max == null || Compare(propertyKind, value, Max.ConstantValue) <= 0);
            }
            else
                return NonComparableEquals(propertyKind, value, Min.ConstantValue);
        }

        PlayerPropertyRequirement(){ }
        public PlayerPropertyRequirement(PlayerPropertyId id, PlayerPropertyConstant min, PlayerPropertyConstant max)
        {
            PlayerPropertyKind propertyKind = PlayerPropertyKindUtil.GetPropertyKindForType(id.PropertyType);

            if (min != null)
                MetaDebug.Assert(PlayerPropertyKindUtil.GetPropertyKindForType(min.ConstantValue.GetType()) == propertyKind, $"Expected {nameof(min)} to have kind {propertyKind} when used with property {id}; it has kind {PlayerPropertyKindUtil.GetPropertyKindForType(min.ConstantValue.GetType())} instead");
            if (max != null)
                MetaDebug.Assert(PlayerPropertyKindUtil.GetPropertyKindForType(max.ConstantValue.GetType()) == propertyKind, $"Expected {nameof(max)} to have kind {propertyKind} when used with property {id}; it has kind {PlayerPropertyKindUtil.GetPropertyKindForType(max.ConstantValue.GetType())} instead");

            if (min == null && max == null)
                throw new InvalidOperationException($"{id}: Minimum and maximum shouldn't both be omitted");

            if (PlayerPropertyKindUtil.IsComparablePropertyKind(propertyKind))
            {
                if (min != null && max != null)
                {
                    if (Compare(propertyKind, min.ConstantValue, max.ConstantValue) > 0)
                        throw new InvalidOperationException($"{id}: {nameof(min)} ({min}) cannot be greater than {nameof(max)} ({max})");
                }
            }
            else
            {
                if (max != null)
                    throw new InvalidOperationException($"For property {id} with non-comparable kind {propertyKind}, {nameof(max)} shouldn't be specified");
            }

            Id = id ?? throw new ArgumentNullException(nameof(id));
            Min = min;
            Max = max;
        }

        static int Compare(PlayerPropertyKind kind, object a, object b)
        {
            if (!PlayerPropertyKindUtil.IsComparablePropertyKind(kind))
                MetaDebug.AssertFail($"Non-comparable property kind {kind} given to {nameof(Compare)}");

            switch (kind)
            {
                case PlayerPropertyKind.Integer:        return Convert.ToInt64(a, CultureInfo.InvariantCulture).CompareTo(Convert.ToInt64(b, CultureInfo.InvariantCulture));
                case PlayerPropertyKind.FixedPoint:     return PlayerPropertyKindUtil.ConvertToF64(a).CompareTo(PlayerPropertyKindUtil.ConvertToF64(b));
                case PlayerPropertyKind.Moment:         return ((MetaTime)a).CompareTo((MetaTime)b);
                case PlayerPropertyKind.Duration:       return ((MetaDuration)a).CompareTo((MetaDuration)b);
                default:
                    throw new ArgumentException($"Unknown comparable {nameof(PlayerPropertyKind)}: {kind}", nameof(kind));
            }
        }

        static bool NonComparableEquals(PlayerPropertyKind kind, object a, object b)
        {
            if (PlayerPropertyKindUtil.IsComparablePropertyKind(kind))
                MetaDebug.AssertFail($"Comparable property kind {kind} given to {nameof(NonComparableEquals)}");

            switch (kind)
            {
                case PlayerPropertyKind.Boolean:    return (bool)a == (bool)b;
                case PlayerPropertyKind.String:     return PlayerPropertyKindUtil.ConvertToString(a) == PlayerPropertyKindUtil.ConvertToString(b);
                default:
                    throw new ArgumentException($"Unknown non-comparable {nameof(PlayerPropertyKind)}: {kind}", nameof(kind));
            }
        }
    }

    /// <summary>
    /// Represents a constant in a game config, meant to be compared to a player property value.
    /// </summary>
    [MetaSerializable]
    public abstract class PlayerPropertyConstant
    {
        public abstract object ConstantValue { get; }

        public override string ToString() => Util.ObjectToStringInvariant(ConstantValue);

        /// <summary>
        /// Parse a PlayerPropertyConstant from the string, with syntax according to the given property kind.
        /// </summary>
        internal static PlayerPropertyConstant Parse(PlayerPropertyKind propertyKind, string valueString)
        {
            if (string.IsNullOrEmpty(valueString))
                throw new ArgumentException("Property value must be represented by a non-null, non-empty string", nameof(valueString));

            switch (propertyKind)
            {
                case PlayerPropertyKind.Integer:        return new LongConstant(ParseLong(valueString));
                case PlayerPropertyKind.FixedPoint:     return new F64Constant(F64.Parse(valueString));
                case PlayerPropertyKind.Moment:         return new MetaTimeConstant(ParseMetaTime(valueString));
                case PlayerPropertyKind.Duration:       return new MetaDurationConstant(ParseMetaDuration(valueString));
                case PlayerPropertyKind.Boolean:        return new BoolConstant(ParseBool(valueString));
                case PlayerPropertyKind.String:         return new StringConstant(valueString);
                default:
                    throw new InvalidOperationException($"Unknown {nameof(PlayerPropertyKind)} for {nameof(PlayerPropertyConstant)}: {propertyKind}");
            }
        }

        #region Parsing helpers

        static long ParseLong(string str)
        {
            if (!long.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out long result))
                throw new FormatException($"Failed to parse integer '{str}'");

            return result;
        }

        static readonly string[] s_dateTimeFormats = new string[]
        {
            "yyyy-M-d H:m:s",
            "yyyy-M-d H:m",
            "yyyy-M-d",
        };

        static MetaTime ParseMetaTime(string str)
        {
            if (!DateTime.TryParseExact(str, s_dateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime dateTime))
                throw new InvalidOperationException($"Failed to parse datetime '{str}'. Must be of one of these formats, without extra spaces: {string.Join(", ", s_dateTimeFormats.Select(f => $"'{f}'"))}");

            return MetaTime.FromDateTime(dateTime);
        }

        static MetaDuration ParseMetaDuration(string str)
        {
            return ConfigParser.ParseExact<MetaDuration>(str);
        }

        static bool ParseBool(string str)
        {
            return ConfigParser.ParseExact<bool>(str);
        }

        #endregion

        #region Concrete types for each possible value type

        [MetaSerializable]
        public abstract class TypedConstant<TValue> : PlayerPropertyConstant
        {
            // \note This is protected (not private) only to make it visible to JSON serialization.
            //       Can change back to private if JSON serialization is fixed to include base classes' privates.
            [MetaMember(1)] protected TValue _value;

            public override object ConstantValue => _value;

            protected TypedConstant(){ }
            protected TypedConstant(TValue value){ _value = value; }
        }

        [MetaSerializableDerived(1)]
        public class LongConstant : TypedConstant<long>
        {
            LongConstant() { }
            public LongConstant(long value) : base(value) { }
        }
        [MetaSerializableDerived(2)]
        public class F64Constant : TypedConstant<F64>
        {
            F64Constant(){ }
            public F64Constant(F64 value) : base(value) { }
        }
        [MetaSerializableDerived(3)]
        public class MetaTimeConstant : TypedConstant<MetaTime>
        {
            MetaTimeConstant(){ }
            public MetaTimeConstant(MetaTime value) : base(value) { }
        }
        [MetaSerializableDerived(4)]
        public class MetaDurationConstant : TypedConstant<MetaDuration>
        {
            MetaDurationConstant(){ }
            public MetaDurationConstant(MetaDuration value) : base(value) { }
        }
        [MetaSerializableDerived(5)]
        public class BoolConstant : TypedConstant<bool>
        {
            BoolConstant(){ }
            public BoolConstant(bool value) : base(value) { }
        }
        [MetaSerializableDerived(6)]
        public class StringConstant : TypedConstant<string>
        {
            StringConstant(){ }
            public StringConstant(string value) : base(value) { }
        }

        #endregion
    }

    /// <summary>
    /// The kind of a player property value.
    /// Roughly corresponds to a type, but a kind may comprise several types.
    /// This is used to aid in comparing e.g. integers of different types.
    /// </summary>
    internal enum PlayerPropertyKind
    {
        Integer,    // Integer (e.g. int or long).
        FixedPoint, // Fixed-point value (F32 or F64).
        Moment,     // Specific moment in time (MetaTime).
        Duration,   // Specific duration of time (MetaDuration).
        Boolean,    // Boolean (bool).
        String,     // String-like value (string or an IStringId).
    }

    internal static class PlayerPropertyKindUtil
    {
        static readonly Dictionary<Type, PlayerPropertyKind> s_fixedTypeToPropertyKind = new Dictionary<Type, PlayerPropertyKind>
        {
            { typeof(sbyte),        PlayerPropertyKind.Integer },
            { typeof(byte),         PlayerPropertyKind.Integer },
            { typeof(short),        PlayerPropertyKind.Integer },
            { typeof(ushort),       PlayerPropertyKind.Integer },
            { typeof(int),          PlayerPropertyKind.Integer },
            { typeof(uint),         PlayerPropertyKind.Integer },
            { typeof(long),         PlayerPropertyKind.Integer },

            { typeof(F32),          PlayerPropertyKind.FixedPoint },
            { typeof(F64),          PlayerPropertyKind.FixedPoint },

            { typeof(MetaTime),     PlayerPropertyKind.Moment },

            { typeof(MetaDuration), PlayerPropertyKind.Duration },

            { typeof(bool),         PlayerPropertyKind.Boolean },

            { typeof(string),       PlayerPropertyKind.String },
        };

        internal static PlayerPropertyKind GetPropertyKindForType(Type type)
        {
            if (s_fixedTypeToPropertyKind.TryGetValue(type, out PlayerPropertyKind kind))
                return kind;
            else if (typeof(IStringId).IsAssignableFrom(type))
                return PlayerPropertyKind.String;
            else
                throw new ArgumentException($"Type {type} is unsupported in {nameof(PlayerPropertyRequirement)}");
        }

        internal static bool IsComparablePropertyKind(PlayerPropertyKind kind)
        {
            switch (kind)
            {
                case PlayerPropertyKind.Integer:    return true;
                case PlayerPropertyKind.FixedPoint: return true;
                case PlayerPropertyKind.Moment:     return true;
                case PlayerPropertyKind.Duration:   return true;
                case PlayerPropertyKind.Boolean:    return false;
                case PlayerPropertyKind.String:     return false;
                default:
                    throw new ArgumentException($"Unknown {nameof(PlayerPropertyKind)}: {kind}", nameof(kind));
            }
        }

        internal static F64 ConvertToF64(object value)
        {
            switch (value)
            {
                case F32 f32: return F64.FromF32(f32);
                case F64 f64: return f64;
                default:
                    throw new ArgumentException($"Cannot convert {value} (type {value.GetType()}) to {typeof(F64)}", nameof(value));
            }
        }

        internal static string ConvertToString(object value)
        {
            switch (value)
            {
                case IStringId stringId:    return stringId.Value;
                case string str:            return str;
                case null:                  return null;
                default:
                    throw new ArgumentException($"Cannot convert {value} (type {value.GetType()}) to {typeof(string)}", nameof(value));
            }
        }
    }
}
