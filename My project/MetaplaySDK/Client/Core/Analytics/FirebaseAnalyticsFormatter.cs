// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Math;
using Metaplay.Core.Session;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;

namespace Metaplay.Core.Analytics
{
    /// <summary>
    /// Specifies the Field, Property or a Type should not be written into Firebase Analytics. This can be
    /// used to annotate and disable unsupported types.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Class | AttributeTargets.Struct)]
    public class FirebaseAnalyticsIgnoreAttribute : Attribute
    {
    }

    /// <summary>
    /// Specifies the name of an analytics event, or a parameter in an analytics event,
    /// for Firebase. Without this, the default name is used (C# class name for events,
    /// or member name for parameters).
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Field)]
    public class FirebaseAnalyticsNameAttribute : Attribute
    {
        public string Name { get; private set; }

        public FirebaseAnalyticsNameAttribute(string name)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
        }
    }

    // \todo [petri] The implementation is very basic and only handles basic types of flat events, generalize it to more events
    //               by splitting the BigQueryFormatter into a general EventFlattener + separate converters to Firebase & BigQuery
    public class FirebaseAnalyticsFormatter
    {
        static FirebaseAnalyticsFormatter _instance = null;
        public static FirebaseAnalyticsFormatter Instance => _instance ?? throw new InvalidOperationException($"FirebaseAnalyticsFormatter has not been initialized");

        public static void Initialize()
        {
            if (_instance != null)
                throw new InvalidOperationException($"FirebaseAnalyticsFormatter already initialized!");

            IEnumerable<AnalyticsEventSpec> events;
            #if METAPLAY_ENABLE_FIREBASE_ANALYTICS
            events = AnalyticsEventRegistry.EventSpecs.Values;
            #else
            events = Array.Empty<AnalyticsEventSpec>();
            #endif
            _instance = new FirebaseAnalyticsFormatter(events);
        }

        /// <summary>
        /// An (integer|double|string) union type representing the Firebase Analytics Event parameter value, within the value bounds. At most one value is set.
        /// </summary>
        public struct ParameterValue
        {
            public readonly long?   IntegerValue;
            public readonly double? DoubleValue;
            public readonly string  StringValue;

            public ParameterValue(long value)
            {
                IntegerValue = value;
                DoubleValue = null;
                StringValue = null;
            }
            public ParameterValue(double value)
            {
                IntegerValue = null;
                DoubleValue = value;
                StringValue = null;
            }

            /// <summary>
            /// Creates a new value from a string or null. String is clamped to the maximum length.
            /// </summary>
            public ParameterValue(string value)
            {
                IntegerValue = null;
                DoubleValue = null;
                if (value == null)
                    StringValue = null;
                else
                {
                    // Clamp string to length.
                    StringValue = Util.ClampStringToLengthCodepoints(value, maxCodepoints: 100);
                }
            }
        }
        public delegate ParameterValue ConverterDelegate(object obj);
        public class ParameterFormatter
        {
            public readonly string                              Name;
            public readonly Func<AnalyticsEventBase, object>    Getter;
            public readonly ConverterDelegate                   Converter;

            public ParameterFormatter(string name, Func<AnalyticsEventBase, object> getter, ConverterDelegate converter)
            {
                Name = name;
                Getter = getter;
                Converter = converter;
            }
        }
        public class EventFormatter
        {
            public readonly string                  EventName;
            public readonly ParameterFormatter[]    Parameters;

            public EventFormatter(string eventName, ParameterFormatter[] parameters)
            {
                EventName = eventName;
                Parameters = parameters;
            }
            public List<(string, ParameterValue)> GetEventParameters(AnalyticsEventBase ev)
            {
                List<(string, ParameterValue)> events = new List<(string, ParameterValue)>(capacity: Parameters.Length);
                foreach (ParameterFormatter paramFormatter in Parameters)
                {
                    string name = paramFormatter.Name;
                    ParameterValue value = paramFormatter.Converter(paramFormatter.Getter(ev));
                    events.Add((name, value));
                }
                return events;
            }
        }

        OrderedDictionary<Type, EventFormatter> _eventFormatters;

        FirebaseAnalyticsFormatter(IEnumerable<AnalyticsEventSpec> events)
        {
            _eventFormatters = new OrderedDictionary<Type, EventFormatter>();

            Dictionary<Type, ConverterDelegate> converterMemo = new Dictionary<Type, ConverterDelegate>();
            foreach (AnalyticsEventSpec eventSpec in events)
            {
                EventFormatter formatter = TryCreateFormatterForEvent(eventSpec, converterMemo);
                if (formatter != null)
                    _eventFormatters.Add(eventSpec.Type, formatter);
            }
        }

        static EventFormatter TryCreateFormatterForEvent(AnalyticsEventSpec spec, Dictionary<Type, ConverterDelegate> converterMemo)
        {
            // Non-analytics events are skipped
            if (!spec.SendToAnalytics)
                return null;
            if (spec.Type.GetCustomAttribute<FirebaseAnalyticsIgnoreAttribute>() != null)
                return null;

            List<ParameterFormatter> parameters = new List<ParameterFormatter>();
            foreach (MemberInfo memberInfo in spec.Type.EnumerateInstanceDataMembersInUnspecifiedOrder())
            {
                // If [IgnoreDataMember], skip
                if (memberInfo.GetCustomAttribute<IgnoreDataMemberAttribute>() != null)
                    continue;
                // If [FirebaseAnalyticsIgnore] on member, skip
                if (memberInfo.GetCustomAttribute<FirebaseAnalyticsIgnoreAttribute>() != null)
                    continue;
                // If property has no setter, skip. It must be a computed value.
                // \todo: It might be useful to allow opting-in certain Properties with some attribute
                if (memberInfo is PropertyInfo propertyInfo)
                {
                    if (propertyInfo.GetSetValueOnDeclaringType() == null)
                        continue;
                }

                Type valueType = memberInfo.GetDataMemberType();

                // If [FirebaseAnalyticsIgnore] on value type itself, skip.
                if (valueType.GetCustomAttribute<FirebaseAnalyticsIgnoreAttribute>() != null)
                    continue;

                Func<AnalyticsEventBase, object> getter = memberInfo.GetDataMemberGetValueOnDeclaringType();
                if (getter == null)
                    throw new InvalidOperationException($"AnalyticsEvent {memberInfo.ToMemberWithGenericDeclaringTypeString()} has no getter");

                ConverterDelegate converter = TryGetConverter(valueType, converterMemo);
                if (converter == null)
                {
                    throw new InvalidOperationException(
                        $"AnalyticsEvent {memberInfo.ToMemberWithGenericDeclaringTypeString()} has unsupported type {valueType.ToGenericTypeString()} for FirebaseAnalytics. "
                        + "If the value is not to be to emitted to Firebase Analytics, the field or the whole event can be marked with [FirebaseAnalyticsIgnore].");
                }

                // Use name specified in [FirebaseAnalyticsName] if present, or C# member's name by default.
                string parameterName = memberInfo.GetCustomAttribute<FirebaseAnalyticsNameAttribute>()?.Name
                                       ?? memberInfo.Name;

                parameters.Add(new ParameterFormatter(parameterName, getter, converter));
            }

            if (parameters.Count > 25)
                throw new InvalidOperationException(
                    $"AnalyticsEvent {spec.Type.ToGenericTypeString()} has too many parameters ({parameters.Count}). Firebase only allows for 25. "
                    + "You may reduce event parameters by disabling them with [FirebaseAnalyticsIgnore], or disable the whole event with [FirebaseAnalyticsIgnore].");

            string eventName = spec.Type.GetCustomAttribute<FirebaseAnalyticsNameAttribute>()?.Name
                               ?? spec.EventType;

            EventFormatter formatter = new EventFormatter(eventName, parameters.ToArray());

            #if UNITY_EDITOR
            // Test formatter once.
            // \todo: Debug only?
            AnalyticsEventBase testEvent = (AnalyticsEventBase)spec.Type.GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, binder: null, Type.EmptyTypes, modifiers: null).Invoke(null);
            _ = formatter.GetEventParameters(testEvent);
            #endif

            return formatter;
        }

        static ConverterDelegate TryGetConverter(Type valueType, Dictionary<Type, ConverterDelegate> memo)
        {
            if (memo.TryGetValue(valueType, out ConverterDelegate memoConverter))
                return memoConverter;

            ConverterDelegate newDelegate = InternalTryGetConverter(valueType, memo);
            if (newDelegate != null)
                memo.Add(valueType, newDelegate);
            return newDelegate;
        }

        static ConverterDelegate InternalTryGetConverter(Type valueType, Dictionary<Type, ConverterDelegate> memo)
        {
            // Trivial conversions
            if (valueType == typeof(sbyte))                     { return (object value) => new ParameterValue((sbyte)value); }
            else if (valueType == typeof(byte))                 { return (object value) => new ParameterValue((byte)value); }
            else if (valueType == typeof(short))                { return (object value) => new ParameterValue((short)value); }
            else if (valueType == typeof(ushort))               { return (object value) => new ParameterValue((ushort)value); }
            else if (valueType == typeof(int))                  { return (object value) => new ParameterValue((int)value); }
            else if (valueType == typeof(uint))                 { return (object value) => new ParameterValue((uint)value); }
            else if (valueType == typeof(long))                 { return (object value) => new ParameterValue((long)value); }
            else if (valueType == typeof(float))                { return (object value) => new ParameterValue((float)value); }
            else if (valueType == typeof(double))               { return (object value) => new ParameterValue((double)value); }
            else if (valueType == typeof(string))               { return (object value) => new ParameterValue((string)value); }

            // Non-trivial conversions
            // \todo: figure out a simpler pattern for the ToString transforms
            else if (valueType.IsEnum)                          { return (object value) => new ParameterValue(FormattableString.Invariant($"{value}")); }
            else if (valueType == typeof(bool))                 { return (object value) => new ParameterValue(((bool)value) ? 1 : 0); }
            else if (valueType.ImplementsInterface<IStringId>()){ return (object value) => new ParameterValue(((IStringId)value)?.Value); }
            else if (valueType == typeof(AuthenticationKey))    { return (object value) => new ParameterValue(((AuthenticationKey)value)?.ToString()); }
            else if (valueType == typeof(EntityId))             { return (object value) => new ParameterValue(((EntityId)value).ToString()); }
            else if (valueType == typeof(SessionToken))         { return (object value) => new ParameterValue($"{((SessionToken)value).Value:X16}"); }
            else if (valueType == typeof(F64))                  { return (object value) => new ParameterValue(((F64)value).Double); }
            else if (valueType == typeof(F32))                  { return (object value) => new ParameterValue(((F32)value).Double); }
            else if (valueType == typeof(MetaTime))             { return (object value) => new ParameterValue(((MetaTime)value).MillisecondsSinceEpoch); }
            else if (valueType == typeof(MetaDuration))         { return (object value) => new ParameterValue(((MetaDuration)value).Milliseconds); }
            else if (valueType == typeof(MetaGuid))             { return (object value) => new ParameterValue(((MetaGuid)value).ToString()); }
            else if (valueType == typeof(byte[]))
            {
                return (object value) =>
                {
                    byte[] bytes = (byte[])value;
                    if (bytes == null)
                        return new ParameterValue(value: null);
                    return new ParameterValue(Convert.ToBase64String(bytes));
                };
            }

            if (valueType.IsGenericTypeOf(typeof(Nullable<>)))
            {
                Type nonNullableType = valueType.GetGenericArguments()[0];
                ConverterDelegate nonNullableConverter = TryGetConverter(nonNullableType, memo);

                if (nonNullableConverter != null)
                {
                    return (object value) =>
                    {
                        if (value == null)
                            return new ParameterValue(value: null);
                        return nonNullableConverter(value);
                    };
                }
            }

            return null;
        }

        /// <summary>
        /// Returns the formatter for the Firebase destined event. If no such event exists or the events is not to be
        /// sent to Firebase, returns null.
        /// </summary>
        public EventFormatter TryGetFormatterForEvent(Type eventType)
        {
            return _eventFormatters.GetValueOrDefault(eventType);
        }
    }

}
