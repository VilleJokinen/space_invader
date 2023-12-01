// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Google.Cloud.BigQuery.V2;
using Metaplay.Core;
using Metaplay.Core.Analytics;
using Metaplay.Core.Config;
using Metaplay.Core.Math;
using Metaplay.Core.Model;
using Metaplay.Core.Serialization;
using Metaplay.Core.Session;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Dynamic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace Metaplay.Cloud.Analytics
{
    public class BigQueryFormatter
    {
        static BigQueryFormatter _instance = null;
        public static BigQueryFormatter Instance => _instance ?? throw new InvalidOperationException($"BigQueryFormatter has not been initialized");
        public static bool IsInitialized => _instance != null;

        public static void Initialize()
        {
            if (_instance != null)
                throw new InvalidOperationException($"BigQueryFormatter already initialized!");
            _instance = new BigQueryFormatter(AnalyticsEventRegistry.EventSpecs.Values);
        }

        class EventFormatter
        {
            public class ParamSpec
            {
                public enum Kind
                {
                    Integer,
                    Float,
                    String,
                    Struct,
                    RuntimeTyped,
                    StringConstant,
                    RuntimeEnumerated,
                }

                public readonly string                              FullName;
                public readonly Kind                                ValueKind;
                public readonly Func<object, object>                GetValue;
                public readonly ParamSpec[]                         StructMembers;
                public readonly OrderedDictionary<Type, ParamSpec>  TypedParams;
                public readonly string                              StringConstant;
                public readonly ParamSpec                           ElementParamSpec;
                public readonly Func<object>                        GetExampleValue;

                public ParamSpec(
                    string fullName,
                    Kind valueKind,
                    Func<object, object> getValue,
                    ParamSpec[] structMembers = null,
                    OrderedDictionary<Type, ParamSpec> typedParams = null,
                    string stringConstant = null,
                    ParamSpec elementParamSpec = null,
                    Func<object> getExampleValue = null)
                {
                    FullName = fullName;
                    ValueKind = valueKind;
                    GetValue = getValue;
                    StructMembers = structMembers;
                    TypedParams = typedParams;
                    StringConstant = stringConstant;
                    ElementParamSpec = elementParamSpec;
                    GetExampleValue = getExampleValue;
                }

                public int ExpectedParamCount
                {
                    get
                    {
                        if (ValueKind == Kind.Struct)
                            return StructMembers.Sum(spec => spec.ExpectedParamCount);
                        else if (ValueKind == Kind.RuntimeTyped)
                            return TypedParams.Values.Max(spec => spec.ExpectedParamCount);
                        else if (ValueKind == Kind.RuntimeEnumerated)
                            return 1 * ElementParamSpec.ExpectedParamCount;
                        else
                            return 1;
                    }
                }
            }

            public readonly string      EventName;
            public readonly ParamSpec[] TopLevelParameterSpecs;
            public readonly int         ExpectedParamCount;

            public EventFormatter(AnalyticsEventSpec spec)
            {
                EventName = spec.Type.GetCustomAttribute<BigQueryAnalyticsNameAttribute>()?.Name
                            ?? spec.EventType;

                TopLevelParameterSpecs = GetTopLevelParamSpecs(spec.Type);
                ExpectedParamCount = TopLevelParameterSpecs.Sum(spec => spec.ExpectedParamCount);
            }

            public List<BigQueryInsertRow> Format(AnalyticsEventBase eventBase)
            {
                List<BigQueryInsertRow> eventParams = new List<BigQueryInsertRow>(ExpectedParamCount);
                foreach (ParamSpec paramSpec in TopLevelParameterSpecs)
                    FlattenAndFormatInto(paramSpec, eventBase, pathPrefix: null, eventParams);
                return eventParams;
            }

            void FlattenAndFormatInto(ParamSpec paramSpec, object container, string pathPrefix, List<BigQueryInsertRow> dst)
            {
                if (paramSpec.ValueKind == ParamSpec.Kind.StringConstant)
                {
                    dst.Add(new BigQueryInsertRow()
                    {
                        { "key", pathPrefix + paramSpec.FullName },
                        { "string_value", paramSpec.StringConstant },
                    });
                    return;
                }

                // Propagate nulls all the way to the leaf-fields. This makes the "schema" of the event
                // stay constant regardless of the contents of the event. This makes the data more readable,
                // but at the cost of emitting null fields.
                // \todo: add configuration switch for emitting / not emitting null fields (if so, we can early exit here)
                object value = container == null ? null : paramSpec.GetValue(container);

                if (paramSpec.ValueKind == ParamSpec.Kind.Struct)
                {
                    foreach (ParamSpec memberParamSpec in paramSpec.StructMembers)
                        FlattenAndFormatInto(memberParamSpec, value, pathPrefix, dst);
                }
                else if (paramSpec.ValueKind == ParamSpec.Kind.RuntimeTyped)
                {
                    if (value == null)
                    {
                        // Runtime typed fields are not handled as if they were null (unlike the others).
                        // To flatten them, we would need to flatten all possible types. While that is possible,
                        // it is probably less readable than not emitting them.
                        return;
                    }
                    FlattenAndFormatInto(paramSpec.TypedParams[value.GetType()], value, pathPrefix, dst);
                }
                else if (paramSpec.ValueKind == ParamSpec.Kind.RuntimeEnumerated)
                {
                    if (value == null)
                    {
                        // Runtime enumerated types are empty.
                        return;
                    }

                    ICollection collection = (ICollection)value;
                    uint index = 0;
                    foreach (object element in collection)
                    {
                        string subRootPath = pathPrefix + $"{paramSpec.FullName}:{index}";
                        FlattenAndFormatInto(paramSpec.ElementParamSpec, element, subRootPath, dst);
                        index++;
                    }
                }
                else
                {
                    string kindName;
                    switch (paramSpec.ValueKind)
                    {
                        case ParamSpec.Kind.Integer:   kindName = "int_value";     break;
                        case ParamSpec.Kind.Float:     kindName = "double_value";  break;
                        case ParamSpec.Kind.String:    kindName = "string_value";  break;
                        default: throw new InvalidOperationException("invalid kind");
                    }

                    dst.Add(new BigQueryInsertRow()
                    {
                        { "key", pathPrefix + paramSpec.FullName },
                        { kindName, value },
                    });
                }
            }

            static ParamSpec[] GetTopLevelParamSpecs(Type type)
            {
                List<ParamSpec> specs = new List<ParamSpec>();
                AddParamFields(type, path: "", debugPath: "", type, specs);
                return specs.ToArray();
            }

            static void AddParamFields(Type eventType, string path, string debugPath, Type type, List<ParamSpec> specs)
            {
                // Note that we choose only serializable members.
                // In particular, we avoid computed and compiler generated fields.

                if (!MetaSerializerTypeRegistry.TryGetTypeSpec(type, out MetaSerializableType serializableTypeSpec))
                    throw new InvalidOperationException($"Non-MetaSerializable type found in analytics event {eventType.ToGenericTypeString()} at {debugPath}: {type.ToGenericTypeString()}");

                foreach (MetaSerializableMember serializableMember in serializableTypeSpec.Members)
                {
                    MemberInfo member = serializableMember.MemberInfo;

                    // Handle attribute on field
                    BigQueryAnalyticsFormatAttribute fieldFormatAttributeMaybe = member.GetCustomAttribute<BigQueryAnalyticsFormatAttribute>();
                    if (fieldFormatAttributeMaybe != null && fieldFormatAttributeMaybe.Mode == BigQueryAnalyticsFormatMode.Ignore)
                        continue;

                    // Use name specified in [BigQueryAnalyticsName] if present, or C# member's name by default.
                    string parameterName = member.GetCustomAttribute<BigQueryAnalyticsNameAttribute>()?.Name
                                           ?? member.Name;

                    string                  fullName        = path + parameterName;
                    string                  fullDebugName   = debugPath + member.Name;
                    Type                    fieldType       = member.GetDataMemberType();
                    Func<object, object>    getter          = member.GetDataMemberGetValueOnDeclaringType();
                    if (!TryExtract(eventType, fullName, fullDebugName, fieldType, getter, out ParamSpec aggregateSpec))
                        throw new InvalidOperationException(
                            $"Unknown analytics event field format in {eventType.ToGenericTypeString()} in {fullDebugName}, Type = {member.GetDataMemberType().ToGenericTypeString()}. " +
                            "Could not register formatter for this type. You may use BigQueryAnalyticsFormatAttribute to disable or customize formatting of a field.");

                    specs.Add(aggregateSpec);
                }
            }

            static bool TryExtract(Type eventType, string fullName, string debugName, Type type, Func<object, object> getter, out ParamSpec spec)
            {
                // Handle attribute set on type itself
                BigQueryAnalyticsFormatAttribute typeFormatAttributeMaybe = type.GetCustomAttribute<BigQueryAnalyticsFormatAttribute>();
                if (typeFormatAttributeMaybe != null && typeFormatAttributeMaybe.Mode == BigQueryAnalyticsFormatMode.Ignore)
                {
                    spec = default;
                    return false;
                }

                if (TryExtractScalar(fullName, type, getter, out spec))
                    return true;
                if (TryExtractAggregate(eventType, fullName, debugName, type, getter, out spec))
                    return true;

                spec = default;
                return false;
            }

            static bool TryExtractScalar(string fullName, Type type, Func<object, object> getter, out ParamSpec spec)
            {
                if (type == typeof(sbyte)
                    || type == typeof(short)
                    || type == typeof(int)
                    || type == typeof(long)
                    || type == typeof(byte)
                    || type == typeof(ushort)
                    || type == typeof(uint))
                {
                    // All integers are stored as int64. Support all that it can fit. Notably ulong is not
                    // supported.
                    spec = new ParamSpec(fullName, ParamSpec.Kind.Integer, getter);
                    return true;
                }
                else if (type == typeof(float)
                    || type == typeof(double))
                {
                    // All floats are stored as doubles.
                    spec = new ParamSpec(fullName, ParamSpec.Kind.Float, getter);
                    return true;
                }
                else if (type == typeof(F32))
                {
                    // convert F32 to double
                    Func<object, object> convertedGetter =
                        (object obj) =>
                        {
                            object value = getter(obj);
                            return ((F32)value).Double;
                        };
                    spec = new ParamSpec(fullName, ParamSpec.Kind.Float, convertedGetter);
                    return true;
                }
                else if (type == typeof(F64))
                {
                    // convert F64 to double
                    Func<object, object> convertedGetter =
                        (object obj) =>
                        {
                            object value = getter(obj);
                            return ((F64)value).Double;
                        };
                    spec = new ParamSpec(fullName, ParamSpec.Kind.Float, convertedGetter);
                    return true;
                }
                else if (type == typeof(string))
                {
                    spec = new ParamSpec(fullName, ParamSpec.Kind.String, getter);
                    return true;
                }
                else if (type.IsEnum)
                {
                    // convert to string
                    Func<object, string> convertedGetter =
                        (object obj) =>
                        {
                            object value = getter(obj);
                            return Enum.GetName(type, value);
                        };

                    string exampleValue = TryGetExampleEnum(type);
                    spec = new ParamSpec(fullName, ParamSpec.Kind.String, convertedGetter, getExampleValue: () => exampleValue);
                    return true;
                }
                else if (type == typeof(bool))
                {
                    // convert to int
                    Func<object, object> convertedGetter =
                        (object obj) =>
                        {
                            object value = getter(obj);
                            return ((bool)value) ? 1 : 0;
                        };
                    spec = new ParamSpec(fullName, ParamSpec.Kind.Integer, convertedGetter, getExampleValue: () => 1);
                    return true;
                }
                else if (type == typeof(EntityId))
                {
                    // convert to string
                    Func<object, string> convertedGetter =
                        (object obj) =>
                        {
                            object value = getter(obj);
                            return ((EntityId)value).ToString();
                        };
                    spec = new ParamSpec(fullName, ParamSpec.Kind.String, convertedGetter, getExampleValue: () => EntityId.Create(EntityKindCore.Player, 1).ToString());
                    return true;
                }
                else if (type.ImplementsInterface<IStringId>())
                {
                    // convert to string
                    Func<object, string> convertedGetter =
                        (object obj) =>
                        {
                            object value = getter(obj);
                            return ((IStringId)value)?.Value;
                        };

                    // StringId example is computed at runtime since IDs get populated at a later phase
                    spec = new ParamSpec(fullName, ParamSpec.Kind.String, convertedGetter, getExampleValue: () => TryGetExampleStringId(type));
                    return true;
                }
                else if (type == typeof(byte[]))
                {
                    // encode as Base64
                    Func<object, string> convertedGetter =
                        (object obj) =>
                        {
                            byte[] value = (byte[])getter(obj);
                            if (value == null)
                                return null;
                            return Convert.ToBase64String(value);
                        };
                    spec = new ParamSpec(fullName, ParamSpec.Kind.String, convertedGetter, getExampleValue: () => "bWV0YQ==");
                    return true;
                }
                else if (type == typeof(SessionToken))
                {
                    // convert to string
                    Func<object, string> convertedGetter =
                        (object obj) =>
                        {
                            object value = getter(obj);
                            return $"{((SessionToken)value).Value:X16}";
                        };
                    spec = new ParamSpec(fullName, ParamSpec.Kind.String, convertedGetter, getExampleValue: () => $"{12345678u:X16}".ToString());
                    return true;
                }
                else if (type == typeof(MetaGuid))
                {
                    // convert to string
                    Func<object, string> convertedGetter =
                        (object obj) =>
                        {
                            object value = getter(obj);
                            return ((MetaGuid)value).ToString();
                        };
                    spec = new ParamSpec(fullName, ParamSpec.Kind.String, convertedGetter, getExampleValue: () => MetaGuid.None.Value.ToString());
                    return true;
                }

                spec = default;
                return false;
            }

            static bool TryExtractAggregate(Type eventType, string fullName, string debugName, Type type, Func<object, object> getter, out ParamSpec spec)
            {
                if (type.IsDictionary())
                {
                    // Cannot flatten dictionaries.
                    spec = default;
                    return false;
                }

                if (type.IsCollection())
                {
                    // Flatten list by repeating. The elements are extracted at runtime, so the getter is an identity function.
                    // The elements are extracted with empty fullname. These are dynamically enumerated specs (due to indexing), and
                    // the dynamic prefix of the name will be inserted during traversal.
                    Type elementType = type.GetCollectionElementType();
                    Func<object, object> elementGetter = (obj) => obj;

                    if (TryExtract(eventType, fullName: "", debugName: debugName + ":[]", elementType, elementGetter, out ParamSpec elementSpec))
                    {
                        spec = new ParamSpec(fullName, ParamSpec.Kind.RuntimeEnumerated, getter, elementParamSpec: elementSpec);
                        return true;
                    }

                    throw new InvalidOperationException(
                        $"Invalid analytics event field type in {eventType.ToGenericTypeString()} in {debugName}, Type = {type.ToGenericTypeString()}. " +
                        "Could not format the Element type. You may use BigQueryAnalyticsFormatAttribute to disable or customize formatting of a field.");
                }

                // Handle nullable
                if (type.IsGenericTypeOf(typeof(Nullable<>)))
                {
                    // Nullable struct. Extract "Value" without altering the path. We do
                    // this by computing the extractor as if the value Nullable<T> was a
                    // plain T and then add Nullable<T> -> T extractor in between..

                    PropertyInfo            valueProp       = type.GetProperty("Value");

                    // \note: Nullables get boxed as non-nullable type. Hence we can just
                    //        take the value directly without having to access "Value"
                    //        property.
                    Func<object, object>    convertedGetter = getter;

                    if (TryExtract(eventType, fullName, debugName: debugName + ":Value", valueProp.PropertyType, convertedGetter, out spec))
                        return true;

                    spec = default;
                    return false;
                }

                // Handle game configs (which are aggregate type, but get collapsed as ConfigKeys)
                if (type.ImplementsGenericInterface(typeof(IGameConfigData<>)))
                {
                    // Collapse as ConfigKey without changing path. Very similar to the Nullable<> path.
                    // Note that we get the ConfigKey from the interface and not the concrete object. The concrete
                    // object might have hidden the propery by explicitly implementing the interface.
                    Type                    configType      = type.GetGenericInterface(typeof(IGameConfigKey<>));
                    PropertyInfo            configKeyProp   = configType.GetProperty("ConfigKey");
                    Func<object, object>    configKeyGetter = configKeyProp.GetDataMemberGetValueOnDeclaringType();

                    // Convert from IGameConfigData<T> into T by getting the ConfigKey
                    Func<object, object> convertedGetter =
                        (object obj) =>
                        {
                            object configData = getter(obj);
                            if (configData == null)
                                return null;
                            return configKeyGetter(configData);
                        };

                    if (TryExtract(eventType, fullName, debugName: debugName + ":ConfigKey", configKeyProp.PropertyType, convertedGetter, out spec))
                        return true;

                    spec = default;
                    return false;
                }

                // "Normal" type:
                // We know the concrete instance in this param is of type T or it inherits T. If there is only one
                // concrete type that fits, just flatten normally. Otherwise we need to add a type switch and handle
                // each case separately.
                // Note that we again piggy-back on MetaSerialized registry.

                if (!MetaSerializerTypeRegistry.TryGetTypeSpec(type, out MetaSerializableType typeSpec))
                    throw new InvalidOperationException(
                        $"Invalid analytics event field type in {eventType.ToGenericTypeString()} in {debugName}, Type = {type.ToGenericTypeString()}. " +
                        "Type not in registry. You may use BigQueryAnalyticsFormatAttribute to disable or customize formatting of a field.");

                List<Type> concreteTypes = new List<Type>();
                if (typeSpec.IsConcreteObject)
                    concreteTypes.Add(typeSpec.Type);
                if (typeSpec.DerivedTypes != null)
                {
                    foreach (Type derived in typeSpec.DerivedTypes.Values)
                    {
                        if (MetaSerializerTypeRegistry.GetTypeSpec(derived).IsConcreteObject)
                            concreteTypes.Add(derived);
                    }
                }

                if (concreteTypes.Count == 0)
                {
                    // Abstract class and there is no implementing type.
                    // This is likely an unused extension point, which are legal. Extract as an
                    // empty struct. The contents do not matter as this value is always null (since
                    // there is no constructible type deriving this).
                    Func<object, object> nullGetter = (object _) => null;
                    spec = new ParamSpec(fullName, ParamSpec.Kind.Struct, nullGetter, Array.Empty<ParamSpec>());
                    return true;
                }
                else if (concreteTypes.Count == 1)
                {
                    // Normal struct. Extract fields. This might not be the type we started with, for example
                    // if the root type is abstract and there is only one concrete deriving type.
                    // Note that even though the types might be different, we don't need to convert to the potential
                    // subclass since boxing hides the types anyway.

                    bool            isRuntimeTyped  = concreteTypes[0] != type;
                    List<ParamSpec> structMembers   = new List<ParamSpec>();
                    AddParamFields(eventType, path: fullName + ":", debugPath: debugName + ":" + (isRuntimeTyped ? $"[{concreteTypes[0].GetNestedClassName()}]:" : null), concreteTypes[0], structMembers);

                    if (structMembers.Count == 0)
                    {
                        // Could not find any fields in the data. The data is probably some
                        // aggregate type that should have a custom scalar-formatter or the
                        // aggregate type is missing formattable fields.
                        throw new InvalidOperationException(
                            $"Invalid analytics event field type in {eventType.ToGenericTypeString()} in {debugName}, Type = {concreteTypes[0].ToGenericTypeString()}. " +
                            "Type has no fields and it cannot be written. You may use BigQueryAnalyticsFormatAttribute to disable or customize formatting of a field.");
                    }

                    // If this is a derived type, add a synthetic type field as in Count > 1 case below.
                    // This makes the data format forwards compatible.
                    if (isRuntimeTyped)
                        structMembers.Insert(0, new ParamSpec(fullName + ":$t", ParamSpec.Kind.StringConstant, null, stringConstant: concreteTypes[0].GetNestedClassName()));

                    spec = new ParamSpec(fullName, ParamSpec.Kind.Struct, getter, structMembers.ToArray());
                    return true;
                }
                else
                {
                    // Dynamically typed field. Type switch.
                    OrderedDictionary<Type, ParamSpec> typedParamSpecs = new OrderedDictionary<Type, ParamSpec>(concreteTypes.Count);
                    foreach (Type concreteType in concreteTypes)
                    {
                        List<ParamSpec> structMembers = new List<ParamSpec>();
                        AddParamFields(eventType, path: fullName + ":", debugPath: debugName + ":" + concreteType.GetNestedClassName()+ ":", concreteType, structMembers);

                        if (structMembers.Count == 0)
                        {
                            // Could not find any fields in the data. As with normal struct case, the data is probably some
                            // aggregate that is missing the custom scalar formatter.
                            throw new InvalidOperationException(
                                $"Invalid analytics event field type in {eventType.ToGenericTypeString()} in {debugName}, Type = {concreteType.ToGenericTypeString()}. " +
                                "Type has no fields and it cannot be written. You may use BigQueryAnalyticsFormatAttribute to disable or customize formatting of a field.");
                        }

                        // Add a synthetic type field to allow detecting the type from result without having to resort to heuristics.
                        structMembers.Insert(0, new ParamSpec(fullName + ":$t", ParamSpec.Kind.StringConstant, null, stringConstant: concreteType.GetNestedClassName()));

                        // The object value is already getted at parent RuntimeTyped node. This is a sub-referencetype so no conversion is needed.
                        Func<object, object> typedGetter = (obj) => obj;

                        ParamSpec typedSpec = new ParamSpec(fullName, ParamSpec.Kind.Struct, typedGetter, structMembers.ToArray());
                        typedParamSpecs.Add(concreteType, typedSpec);
                    }

                    spec = new ParamSpec(fullName, ParamSpec.Kind.RuntimeTyped, getter, null, typedParamSpecs);
                    return true;
                }
            }

            static string TryGetExampleEnum(Type type)
            {
                Array values = Enum.GetValues(type);
                if (values.Length == 0)
                    return null;
                IEnumerator e = values.GetEnumerator();
                if (!e.MoveNext())
                    return null;
                return e.Current?.ToString();
            }

            static string TryGetExampleStringId(Type type)
            {
                FieldInfo field = type.BaseType?.GetField("s_interned", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly);
                if (field == null)
                    return null;
                IDictionary dict = field.GetValue(null) as IDictionary;
                if (dict == null)
                    return null;
                IEnumerator e = dict.Values.GetEnumerator();
                if (!e.MoveNext())
                    return null;
                return e.Current?.ToString();
            }
        }

        readonly OrderedDictionary<Type, EventFormatter> _eventFormatters;

        internal BigQueryFormatter(IEnumerable<AnalyticsEventSpec> events)
        {
            _eventFormatters = events.ToOrderedDictionary(keySelector: ev => ev.Type, elementSelector: ev => new EventFormatter(ev));
        }

        /// <summary>
        /// Formats a timestamp into TIMESTAMP REST api format (not the canonical format).
        /// </summary>
        static string FormatTimestampString(MetaTime timestamp)
        {
            // YYYY-MM-DD HH:MM[:SS[.SSSSSS]]
            DateTime dt = timestamp.ToDateTime();
            int microseconds = dt.Millisecond * 1000;
            return string.Format(CultureInfo.InvariantCulture, "{0:D4}-{1:D2}-{2:D2} {3:D2}:{4:D2}:{5:D2}.{6:D6}", dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second, microseconds);
        }

        /// <summary>
        /// Formats an event into the BigQuery analytics table format, and returns the resulting rows.
        /// This should be inserted into event_params column.
        /// </summary>
        List<BigQueryInsertRow> FormatEventArgs(EventFormatter formatter, AnalyticsEventBase eventBase)
        {
            return formatter.Format(eventBase);
        }

        /// <summary>
        /// Formats a context into the BigQuery analytics table format and retuns the (columnName, row)
        /// pair. If there is no context, (null, null) is returned.
        /// </summary>
        (string, BigQueryInsertRow) TryFormatContext(AnalyticsContextBase contextBase)
        {
            // Populate context from envelope.

            if (contextBase is PlayerAnalyticsContext playerContext)
            {
                BigQueryInsertRow context = new BigQueryInsertRow();
                List<BigQueryInsertRow> experiments = new List<BigQueryInsertRow>();
                foreach ((string experimentId, string variantId) in playerContext.Experiments)
                {
                    experiments.Add(new BigQueryInsertRow()
                    {
                        {"experiment", experimentId },
                        {"variant", variantId },
                    });
                }
                context.Add("session_number", playerContext.SessionNumber);
                context.Add("experiments", experiments);
                return ("player", context);
            }
            else if (contextBase is GuildAnalyticsContext guildContext)
            {
                // Guild context has not yet any fields and empty records are not supported.
                //BigQueryInsertRow context = new BigQueryInsertRow();
                //return ("guild", context);
            }
            else if (contextBase is ServerAnalyticsContext serverContext)
            {
                // Server context has not yet any fields and empty records are not supported.
            }

            return (null, null);
        }

        List<BigQueryInsertRow> FormatEventLabels(OrderedDictionary<AnalyticsLabel, string> labels)
        {
            if (labels == null || labels.Count == 0)
                return new List<BigQueryInsertRow>();

            List<BigQueryInsertRow> rows = new List<BigQueryInsertRow>(capacity: labels.Count);
            foreach ((AnalyticsLabel label, string value) in labels)
            {
                BigQueryInsertRow record = new BigQueryInsertRow();
                record.Add("name", label.ToString());
                record.Add("string_value", value);
                rows.Add(record);
            }
            return rows;
        }

        /// <summary>
        /// Formats and writer the analytics event into the write buffer. If <paramref name="enableBigQueryEventDeduplication"/> is set, a deduplication key
        /// is defined for each written event. The BigQuery backend will then pool any deduplicable events over a short window and remove duplicates, which may
        /// occur due to retrying inserts when the attempt had ended up with an undeterminate result (either success or fail, but cannot know which one).
        /// </summary>
        public void WriteEvent(BigQueryBatchWriter.BatchBuffer buffer, AnalyticsEventEnvelope eventEnvelope, bool enableBigQueryEventDeduplication)
        {
            string id = string.Format(CultureInfo.InvariantCulture, "{0:X16}{1:X16}", eventEnvelope.UniqueId.High, eventEnvelope.UniqueId.Low);

            AnalyticsEventBase eventBase = eventEnvelope.Payload;

            if (!_eventFormatters.TryGetValue(eventBase.GetType(), out EventFormatter eventFormatter))
                throw new ArgumentException($"Type {eventBase.GetType().ToGenericTypeString()} is not a known AnalyticsEvent", nameof(eventEnvelope));

            OrderedDictionary<string, object> fields = new OrderedDictionary<string, object>();
            fields.Add("source_id", eventEnvelope.Source.ToString());
            fields.Add("event_id", id);
            fields.Add("event_timestamp", FormatTimestampString(eventEnvelope.ModelTime));
            fields.Add("event_name", eventFormatter.EventName);
            fields.Add("event_schema_version", eventEnvelope.SchemaVersion);
            fields.Add("event_params", FormatEventArgs(eventFormatter, eventBase));

            (string contextName, BigQueryInsertRow contextRow) = TryFormatContext(eventEnvelope.Context);
            if (contextName != null)
                fields.Add(contextName, contextRow);

            fields.Add("labels", FormatEventLabels(eventEnvelope.Labels));

            BigQueryInsertRow row = new BigQueryInsertRow();
            if (enableBigQueryEventDeduplication)
                row.InsertId = id;
            row.Add(fields);
            buffer.Add(row);
        }

        /// <summary>
        /// Creates an example object that could be a result of formatting the type.
        /// </summary>
        public object GetExampleResultObject(AnalyticsEventSpec spec)
        {
            static object SingleParamContainer(string keyName, string valueName, object value)
            {
                IDictionary<string, object> dst = new ExpandoObject();
                dst.Add("key", keyName);
                dst.Add(valueName, value);
                return dst;
            }
            static void ExtractExampleEventParamData(List<object> dst, string namePrefix, EventFormatter.ParamSpec spec)
            {
                switch (spec.ValueKind)
                {
                    case EventFormatter.ParamSpec.Kind.Integer:
                        dst.Add(SingleParamContainer(namePrefix + spec.FullName, "int_value", spec.GetExampleValue?.Invoke() ?? 123));
                        return;

                    case EventFormatter.ParamSpec.Kind.Float:
                        dst.Add(SingleParamContainer(namePrefix + spec.FullName, "double_value", spec.GetExampleValue?.Invoke() ?? 45.5));
                        return;

                    case EventFormatter.ParamSpec.Kind.String:
                        dst.Add(SingleParamContainer(namePrefix + spec.FullName, "string_value", spec.GetExampleValue?.Invoke() ?? "ABC"));
                        return;

                    case EventFormatter.ParamSpec.Kind.StringConstant:
                        dst.Add(SingleParamContainer(namePrefix + spec.FullName, "string_value", spec.StringConstant));
                        return;

                    case EventFormatter.ParamSpec.Kind.Struct:
                        foreach (EventFormatter.ParamSpec subSpec in spec.StructMembers)
                            ExtractExampleEventParamData(dst, namePrefix, subSpec);
                        return;

                    case EventFormatter.ParamSpec.Kind.RuntimeEnumerated:
                        // This is an array so expand it to 2 elements.
                        // \todo: Add mechanism for caller to select desired amount?
                        ExtractExampleEventParamData(dst, namePrefix + spec.FullName + ":0", spec.ElementParamSpec);
                        ExtractExampleEventParamData(dst, namePrefix + spec.FullName + ":1", spec.ElementParamSpec);
                        return;

                    case EventFormatter.ParamSpec.Kind.RuntimeTyped:
                        // Expand all types, i.e. if field "A:Reward" can be either SuperReward or HyperReward,
                        // we expand the argument of both of them. The results should remain readable as each set
                        // of field is prefixed with the $t type discriminator.
                        // \todo: Add mechanism for caller to select certain types
                        foreach (EventFormatter.ParamSpec subSpec in spec.TypedParams.Values)
                            ExtractExampleEventParamData(dst, namePrefix, subSpec);
                        return;
                }

                throw new InvalidOperationException("invalid kind");
            }
            static object[] GetExampleEventParams(EventFormatter formatter)
            {
                List<object> dst = new List<object>();
                foreach (EventFormatter.ParamSpec spec in formatter.TopLevelParameterSpecs)
                    ExtractExampleEventParamData(dst, null, spec);
                return dst.ToArray();
            }
            static object[] GetExampleLabels()
            {
                List<object> dst = new List<object>();
                foreach (AnalyticsLabel label in AnalyticsLabel.AllValues)
                {
                    dynamic record = new ExpandoObject();
                    record.name = label.ToString();
                    record.string_value = "labelValue";
                    dst.Add(record);
                }
                return dst.ToArray();
            }

            if (!_eventFormatters.TryGetValue(spec.Type, out EventFormatter formatter))
                throw new ArgumentException($"Type {spec.Type.ToGenericTypeString()} is not a known AnalyticsEvent", nameof(spec));

            dynamic obj = new ExpandoObject();
            obj.source_id = $"{spec.CategoryName}:123";
            obj.event_id = string.Format(CultureInfo.InvariantCulture, "{0:X16}{1:X16}", 0x11223344AABBCCDD, 0x66778899EEFF0011);
            obj.event_timestamp = FormatTimestampString(MetaTime.FromDateTime(new DateTime(2020, 10, 10, 14, 40, 12, DateTimeKind.Utc)));
            obj.event_name = formatter.EventName;
            obj.event_schema_version = spec.SchemaVersion;
            obj.event_params = GetExampleEventParams(formatter);
            obj.labels = GetExampleLabels();

            if (spec.CategoryName == "Player")
            {
                obj.player = new ExpandoObject();
                obj.player.session_number = 4;

                dynamic experimentExample = new ExpandoObject();
                experimentExample.experiment = "experimentId";
                experimentExample.variant = "variantId";
                obj.player.experiments = new List<ExpandoObject> { experimentExample }.ToArray();
            }

            return obj;
        }
    }
}
