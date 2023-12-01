// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Config;
using Metaplay.Core.Serialization;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;

namespace Metaplay.Core.Analytics
{
    public delegate IEnumerable<string> GetAnalyticsEventKeywordsFunc(AnalyticsEventBase ev);

    /// <summary>
    /// Integration entrypoint for overriding properties of MetaplaySDK provided analytics events.
    /// </summary>
    public class AnalyticsEventCustomizations : IMetaIntegrationSingleton<AnalyticsEventCustomizations>
    {
        public struct OverrideableProps
        {
            public bool                 IncludeInEventLog;
            public bool                 SendToAnalytics;
            public bool                 CanTrigger;
            public GetAnalyticsEventKeywordsFunc KeywordsFunc;
            public OverrideableProps(bool includeInEventLog, bool sendToAnalytics, bool canTrigger, GetAnalyticsEventKeywordsFunc keywordsFunc)
            {
                IncludeInEventLog = includeInEventLog;
                SendToAnalytics   = sendToAnalytics;
                CanTrigger        = canTrigger;
                KeywordsFunc      = keywordsFunc;
            }
        }

        public virtual OverrideableProps CustomizeAnalyticsEventSpec(Type eventType, OverrideableProps props)
        {
            return props;
        }
    }

    /// <summary>
    /// Metadata about analytics events.
    /// </summary>
    public class AnalyticsEventSpec
    {
        public readonly Type                 Type;
        public readonly int                  TypeCode;
        public readonly string               EventType;
        public readonly string               DisplayName;
        public readonly string               CategoryName;
        public readonly int                  SchemaVersion;
        public readonly string               DocString;
        public readonly bool                 IncludeInEventLog; // Should the event be written into the actor's EventLog?
        public readonly bool                 SendToAnalytics;   // Should the event be sent to the analytics pipeline?
        public readonly bool                 CanTrigger;        // Can be used by trigger conditions
        public readonly string[]             Parameters;
        [IgnoreDataMember]
        public readonly GetAnalyticsEventKeywordsFunc KeywordsFunc;

        public AnalyticsEventSpec(Type type, AnalyticsEventAttribute attrib, AnalyticsEventCategoryAttribute categoryAttrib)
        {
            Type                = type;
            TypeCode            = attrib.TypeCode;
            EventType           = type.Name; // \todo [petri] using class name as EventType, which might not be flexible enough in all cases
            DisplayName         = attrib.DisplayName ?? GetEventDefaultDisplayName(EventType, categoryAttrib.CategoryName);
            CategoryName        = categoryAttrib.CategoryName;
            SchemaVersion       = attrib.SchemaVersion;
            DocString           = attrib.DocString;
            IEnumerable<string> propertyNames   = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Where(prop => prop.GetSetMethodOnDeclaringType() != null).Select(prop => prop.Name);
            IEnumerable<string> fieldNames      = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Where(field => field.GetCustomAttribute<CompilerGeneratedAttribute>() == null).Select(field => field.Name);
            Parameters   = (propertyNames.Concat(fieldNames)).ToArray();

            // Let integration customize some properties of the analytics event spec. Useful for integration-side modifications
            // of SDK event types.
            AnalyticsEventCustomizations.OverrideableProps overrideableProps = new AnalyticsEventCustomizations.OverrideableProps(
                attrib.IncludeInEventLog,
                attrib.SendToAnalytics,
                attrib.CanTrigger,
                ResolveKeywordsFunc(type));
            overrideableProps = IntegrationRegistry.Get<AnalyticsEventCustomizations>().CustomizeAnalyticsEventSpec(type, overrideableProps);
            IncludeInEventLog = overrideableProps.IncludeInEventLog;
            SendToAnalytics   = overrideableProps.SendToAnalytics;
            CanTrigger        = overrideableProps.CanTrigger;
            KeywordsFunc      = overrideableProps.KeywordsFunc;
        }

        static GetAnalyticsEventKeywordsFunc ResolveKeywordsFunc(Type eventType)
        {
            // Get keywords from AnalyticsEventKeywords attributes in the inheritance chain
            List<string> keywordsForType = eventType.GetCustomAttributes<AnalyticsEventKeywordsAttribute>(inherit: true).SelectMany(x => x.Keywords).Distinct().ToList();

            // Check if event type overrides per-instance keywords.
            MethodInfo getter                 = eventType.GetProperty(nameof(AnalyticsEventBase.KeywordsForEventInstance)).GetGetMethod(false);
            bool       hasPerInstanceKeywords = getter.GetBaseDefinition().DeclaringType != getter.DeclaringType;

            if (keywordsForType.Count == 0 && !hasPerInstanceKeywords)
            {
                return null;
            }
            else if (keywordsForType.Count == 0)
            {
                return ev => ev.KeywordsForEventInstance;
            }
            else if (!hasPerInstanceKeywords)
            {
                return ev => keywordsForType;
            }
            else
            {
                return ev => keywordsForType.Concat(ev.KeywordsForEventInstance).Distinct();
            }
        }

        static string GetEventDefaultDisplayName(string className, string categoryName)
        {
            // \todo Assuming XYZEvent prefix where XYZ is category name
            string prefix                   = categoryName + "Event";
            string classNameWithoutPrefix   = className.StartsWith(prefix, StringComparison.Ordinal)
                                              ? className.Substring(prefix.Length)
                                              : className;

            return SplitCamelCase(classNameWithoutPrefix);
        }

        static string SplitCamelCase(string input)
        {
            // \todo [petri] doesn't work properly with abbreviations (eg, "IAP" -> "I A P")
            return Regex.Replace(input, "([A-Z])", " $1", RegexOptions.Compiled).Trim();
        }
    }

    public static class AnalyticsEventRegistry
    {
        public static bool                                          IsInitialized           { get; private set; }
        public static OrderedDictionary<Type, AnalyticsEventSpec>   EventSpecs              { get; private set; }
        public static OrderedDictionary<int, AnalyticsEventSpec>    EventSpecsByTypeCode    { get; private set; }
        public static IEnumerable<AnalyticsEventSpec>               AllEventSpecs           => EventSpecs.Values;

        public static AnalyticsEventSpec GetEventSpec(Type type)
        {
            if (!IsInitialized)
                throw new InvalidOperationException($"AnalyticsEventRegistry not initialized");

            if (EventSpecs.TryGetValue(type, out AnalyticsEventSpec spec))
                return spec;
            else
                throw new ArgumentException($"Type {type.ToGenericTypeString()} is not a known AnalyticsEvent", nameof(type));
        }

        public static void Initialize()
        {
            if (IsInitialized)
                throw new InvalidOperationException($"{nameof(AnalyticsEventRegistry)} already initialized");

            EventSpecs = ResolveEventSpecs();
            EventSpecsByTypeCode = EventSpecs.ToOrderedDictionary(x => x.Value.TypeCode, x => x.Value);

            IsInitialized = true;
        }

        static OrderedDictionary<Type, AnalyticsEventSpec> ResolveEventSpecs()
        {
            OrderedDictionary<Type, AnalyticsEventSpec> eventSpecs = new OrderedDictionary<Type, AnalyticsEventSpec>();
            HashSet<Type> validatedTypes = new HashSet<Type>();
            List<string> validationErrors = new List<string>();

            foreach (Type eventType in TypeScanner.GetConcreteDerivedTypes<AnalyticsEventBase>())
            {
                AnalyticsEventAttribute attrib = eventType.GetCustomAttribute<AnalyticsEventAttribute>();
                if (attrib == null)
                    throw new InvalidOperationException($"{eventType.Name} does not have the required [AnalyticsEvent] attribute");

                AnalyticsEventCategoryAttribute categoryAttrib = eventType.GetCustomAttribute<AnalyticsEventCategoryAttribute>();
                if (categoryAttrib == null)
                    throw new InvalidOperationException($"{eventType.Name} does not have required [AnalyticsEventCategory] attribute in its base class");

                if (!eventType.IsMetaFeatureEnabled())
                    continue;

                ValidateEventType(eventType, allowConfigReferences: true, validatedTypes, validationErrors);

                eventSpecs.Add(eventType, new AnalyticsEventSpec(eventType, attrib, categoryAttrib));
            }

            foreach (Type contextTypes in TypeScanner.GetConcreteDerivedTypes<AnalyticsContextBase>())
            {
                // \note: Context is not really an event but is serialized with the event and has similar limitations. Except we don't allow
                //        config references in the Context. This allows context to contain information required for determining the (de)serialization
                //        resolvers (experiment information for config specialization).

                if (!contextTypes.IsMetaFeatureEnabled())
                    continue;

                ValidateEventType(contextTypes, allowConfigReferences: false, validatedTypes, validationErrors);
            }

            if (validationErrors.Count > 0)
            {
                List<Exception> errors = new List<Exception>();
                foreach (string validationError in validationErrors)
                    errors.Add(new InvalidOperationException(validationError));

                throw new AggregateException("Validation errors in Analytics Events.", errors);
            }

            return eventSpecs;
        }

        static void ValidateEventType(Type eventType, bool allowConfigReferences, HashSet<Type> validatedTypes, List<string> validationErrors)
        {
            Stack<string> path = new Stack<string>();
            path.Push(eventType.ToGenericTypeString());
            ValidateEventTypeTree(eventType, allowConfigReferences, validationErrors, eventType, validatedTypes, path);
        }

        static void ValidateEventTypeTree(Type eventType, bool allowConfigReferences, List<string> validationErrors, Type scanType, HashSet<Type> checkedTypes, Stack<string> path)
        {
            if (checkedTypes.Contains(scanType))
                return;
            checkedTypes.Add(scanType);

            if (scanType.IsPrimitive)
                return;
            if (scanType.IsEnum)
                return;
            if (TaggedWireSerializer.IsBuiltinType(scanType))
                return;

            MetaSerializableType typeSpec = MetaSerializerTypeRegistry.GetTypeSpec(scanType);
            if (typeSpec.IsAbstract)
            {
                foreach (Type derivedType in typeSpec.DerivedTypes.Values)
                {
                    path.Push($"<Derived type {derivedType.Name}>");
                    ValidateEventTypeTree(eventType, allowConfigReferences, validationErrors, derivedType, checkedTypes, path);
                    path.Pop();
                }
                return;
            }
            else if (typeSpec.WireType == WireDataType.ValueCollection)
            {
                path.Push($"<Element type {scanType.GetCollectionElementType().Name}>");
                ValidateEventTypeTree(eventType, allowConfigReferences, validationErrors, scanType.GetCollectionElementType(), checkedTypes, path);
                path.Pop();
            }
            else if (typeSpec.WireType == WireDataType.KeyValueCollection)
            {
                (Type keyType, Type valueType) = scanType.GetDictionaryKeyAndValueTypes();

                path.Push($"<Key type {keyType.Name}>");
                ValidateEventTypeTree(eventType, allowConfigReferences, validationErrors, keyType, checkedTypes, path);
                path.Pop();

                path.Push($"<Value type {valueType.Name}>");
                ValidateEventTypeTree(eventType, allowConfigReferences, validationErrors, valueType, checkedTypes, path);
                path.Pop();
            }
            else if (typeSpec.IsSystemNullable)
            {
                ValidateEventTypeTree(eventType, allowConfigReferences, validationErrors, scanType.GetSystemNullableElementType(), checkedTypes, path);
            }
            else if (typeSpec.IsGameConfigData)
            {
                if (!allowConfigReferences)
                {
                    // References require a resolver which is illegal
                    validationErrors.Add(
                        $"{eventType.ToGenericTypeString()} contains {scanType.ToGenericTypeString()} which is a config reference. "
                        + "References are not supported in Analytics events as decoding requires the original resolver context. "
                        + $"If only Id is to be emitted, you may use {scanType.GetGenericInterfaceTypeArguments(typeof(IGameConfigData<>))[0].ToGenericTypeString()}, or MetaRef<{scanType.ToGenericTypeString()}>. "
                        + $"If contents are required, use GameConfigDataContent<{scanType.ToGenericTypeString()}>. "
                        + $"Path: {PathToString(path)}");
                }
            }
            else if (typeSpec.IsMetaRef)
            {
                // MetaRefs are OK because they support deserialization without having access to the resolver.
            }
            else if (typeSpec.IsConcreteObject)
            {
                // Check member are valid
                ValidateSingleTypeIsRoundtripSerializable(eventType, validationErrors, typeSpec, path);

                // Check member types
                foreach (MetaSerializableMember member in typeSpec.Members)
                {
                    path.Push($"<Member {member.Name}>");
                    ValidateEventTypeTree(eventType, allowConfigReferences, validationErrors, member.Type, checkedTypes, path);
                    path.Pop();
                }
            }
            else if (scanType.HasGenericAncestor(typeof(StringId<>)))
            {
                // StringIds are ok
            }
            else
            {
                validationErrors.Add($"Unsupported type {scanType.ToGenericTypeString()}");
            }
        }

        static void ValidateSingleTypeIsRoundtripSerializable(Type eventType, List<string> validationErrors, MetaSerializableType typeSpec, Stack<string> path)
        {
            // Check the type is round-trip serializable. We attempt to detect this by verifying that type
            // has no fields or properties without the MetaMember attribute.
            // \note: we detect members by their name as this avoids ambiguity between MemberInfos of base and subclasses. This
            //        is OK as MetaSerialization checks will prevent name overloading anyway.

            OrderedSet<string> members = new OrderedSet<string>();
            foreach (MetaSerializableMember member in typeSpec.Members)
                members.Add(member.MemberInfo.Name);

            foreach (MemberInfo mi in typeSpec.Type.EnumerateInstanceDataMembersInUnspecifiedOrder())
            {
                // If MetaMember, skip
                if (members.Contains(mi.Name))
                    continue;
                // If [IgnoreDataMember], skip
                if (mi.GetCustomAttribute<IgnoreDataMemberAttribute>() != null)
                    continue;

                if (mi is FieldInfo)
                {
                    // nada
                }
                else if (mi is PropertyInfo pi)
                {
                    // If property has no setter, skip. It must be a computed value.
                    if (pi.GetSetValueOnDeclaringType() == null)
                        continue;
                }
                else
                {
                    throw new InvalidOperationException("unreachable");
                }

                validationErrors.Add(
                    $"{eventType.ToGenericTypeString()} contains type {typeSpec.Type.ToGenericTypeString()} "
                    + $"which has a data member {mi.ToMemberWithGenericDeclaringTypeString()} which is not marked as [MetaMember]. "
                    + "Its value would be lost when emitted. If loss of value is intentional, mark it with [IgnoreDataMember]. "
                    + $"Path: {PathToString(path)}");
            }
        }

        static string PathToString(Stack<string> path) => String.Join(" -> ", path.Reverse());
    }
}
