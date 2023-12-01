// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;

namespace Metaplay.Cloud.RuntimeOptions
{
    /// <summary>
    /// Utility for binding configuration values into RuntimeOptions
    /// </summary>
    public static class RuntimeOptionsBinder
    {
        public struct BindingResults
        {
            public struct Section
            {
                public RuntimeOptionsBase Options;
                public string ContentHash;
                public OrderedDictionary<string, RuntimeOptionsSourceSet.Source> MappingsSources; // FieldPath -> Source
            }

            public OrderedDictionary<Type, Section> Sections;
            public Warning[] Warnings;
            public Exception[] Errors;

            public BindingResults(OrderedDictionary<Type, Section> sections, Warning[] warnings, Exception[] errors)
            {
                Sections = sections;
                Warnings = warnings;
                Errors = errors;
            }
        }

        public struct Warning
        {
            public readonly RuntimeOptionsSourceSet.Source Source;
            public readonly string Message;

            public Warning(RuntimeOptionsSourceSet.Source source, string message)
            {
                Source = source;
                Message = message;
            }

            public override string ToString() => Message;
        }

        public struct RuntimeOptionDefinition
        {
            public Type     Type;
            public string   SectionName;

            public RuntimeOptionDefinition(Type type, string sectionName)
            {
                Type = type;
                SectionName = sectionName;
            }
        }

        struct Context
        {
            public OrderedDictionary<string, RuntimeOptionsBase> Options;
            public List<Warning> Warnings;
            public List<Exception> Errors;
            public OrderedDictionary<Type, string> ContentHashes;
            public OrderedDictionary<Type, OrderedDictionary<string, RuntimeOptionsSourceSet.Source>> MappingsSources; // Section -> (FieldPath -> Source)

            public RuntimeOptionsBase CurrentOptions;
            public RuntimeOptionsSourceSet.Source CurrentSource;

            public static Context New(List<RuntimeOptionDefinition> definitions)
            {
                Context ctx = new Context()
                {
                    Options = new OrderedDictionary<string, RuntimeOptionsBase>(),
                    Warnings = new List<Warning>(),
                    Errors = new List<Exception>(),
                    ContentHashes = new OrderedDictionary<Type, string>(),
                    MappingsSources = new OrderedDictionary<Type, OrderedDictionary<string, RuntimeOptionsSourceSet.Source>>(),
                };

                foreach (RuntimeOptionDefinition definition in definitions)
                    ctx.Options.Add(definition.SectionName, (RuntimeOptionsBase)Activator.CreateInstance(definition.Type));

                foreach (RuntimeOptionsBase opts in ctx.Options.Values)
                    ctx.MappingsSources[opts.GetType()] = new OrderedDictionary<string, RuntimeOptionsSourceSet.Source>();

                return ctx;
            }

            public BindingResults ToResult()
            {
                OrderedDictionary<Type, BindingResults.Section> sections = new OrderedDictionary<Type, BindingResults.Section>();
                foreach (RuntimeOptionsBase opts in Options.Values)
                {
                    BindingResults.Section section = new BindingResults.Section();
                    section.Options = opts;
                    section.ContentHash = ContentHashes.GetValueOrDefault(opts.GetType(), "no-mappings");
                    section.MappingsSources = MappingsSources[opts.GetType()];
                    sections.Add(section.Options.GetType(), section);
                }

                return new BindingResults(sections, Warnings.ToArray(), Errors.ToArray());
            }

            public void WarnKey(IConfigurationSection key, string message)
            {
                string fullMessage = $"{CurrentSource.Name} config {key.Path}: {message}";
                Warnings.Add(new Warning(CurrentSource, fullMessage));
            }

            public void WarnUnrecognizedKey(IConfigurationSection key, string didYouMean, string hint)
            {
                string message = $"No such property.";
                if (didYouMean != null)
                    message += $" Did you mean \"{didYouMean}\"?";
                if (hint != null)
                    message += $" {hint}";
                WarnKey(key, message);
            }

            public void WarnUnrecognizedSection(IConfigurationSection key, string didYouMean, string hint)
            {
                string message = $"No such Runtime Option.";
                if (didYouMean != null)
                    message += $" Did you mean \"{didYouMean}\"?";
                if (hint != null)
                    message += $" {hint}";
                WarnKey(key, message);
            }

            public void Error(IConfigurationSection key, string message, Exception innerException = null)
            {
                string fullMessage = $"{CurrentSource.Name} config {key.Path}: {message}";
                Errors.Add(new FormatException(fullMessage, innerException));
            }
        }

        class CouldNotBindException : Exception
        {
        }

        /// <summary>
        /// Binds values from Source sets into the given config entries, returning both the bound
        /// RuntimeOptions and any warnings. For the resulting RuntimeOptions, the value are only bound
        /// but <see cref="RuntimeOptionsBase.OnLoadedAsync"/> is not called.
        /// </summary>
        public static BindingResults BindToRuntimeOptions(List<RuntimeOptionDefinition> definitions, RuntimeOptionsSourceSet sources)
        {
            Context ctx = Context.New(definitions);

            foreach (RuntimeOptionsSourceSet.Source source in sources.Sources)
                BindSingleSource(ctx, source);

            return ctx.ToResult();
        }

        /// <summary>
        /// Binds values from a single Source into the given config entries, returning both the bound
        /// RuntimeOptions and any warnings. For the resulting RuntimeOptions, the value are only bound
        /// but <see cref="RuntimeOptionsBase.OnLoadedAsync"/> is not called.
        /// </summary>
        public static BindingResults BindToRuntimeOptions(List<RuntimeOptionDefinition> definitions, RuntimeOptionsSourceSet.Source source)
        {
            Context ctx = Context.New(definitions);

            BindSingleSource(ctx, source);

            return ctx.ToResult();
        }

        /// <summary>
        /// Checks all fields in RuntimeOptions are parseable, i.e. they are trivially parseable or a string converter has been registered
        /// from them.
        /// </summary>
        public static void CheckRuntimeOptionsIsParseable(Type runtimeOptsType)
        {
            CheckObjectIsParseable(runtimeOptsType, runtimeOptsType);
        }

        static void BindSingleSource(Context ctx, RuntimeOptionsSourceSet.Source source)
        {
            foreach (IConfigurationSection section in source.ConfigRoot.GetChildren())
            {
                ctx.CurrentSource = source;
                ctx.CurrentOptions = null;

                // Real section.
                RuntimeOptionsBase options = TryGetOptionsForSection(ctx, source, section);
                if (options == null)
                    continue;

                ctx.CurrentOptions = options;
                try
                {
                    _ = BindConfigurationToObject(ctx, options.GetType(), options, section);
                }
                catch (CouldNotBindException)
                {
                }
                catch (Exception ex)
                {
                    ctx.Errors.Add(new InvalidOperationException($"Failed to bind {options.GetType().ToGenericTypeString()} from {source.Name}.", ex));
                }

                // In case of a typo, multiple configs can map into a single RuntimeOption. Combine hashes
                ctx.ContentHashes[options.GetType()] = ctx.ContentHashes.GetValueOrDefault(options.GetType()) + GetSectionContentHash(section);
            }
        }

        static RuntimeOptionsBase TryGetOptionsForSection(Context ctx, RuntimeOptionsSourceSet.Source source, IConfigurationSection section)
        {
            if (ctx.Options.TryGetValue(section.Key, out RuntimeOptionsBase exactNameOptions))
                return exactNameOptions;

            // No such section. Try to figure out what is the reason

            // -Options suffix
            if (section.Key.EndsWith("Options", StringComparison.OrdinalIgnoreCase))
            {
                string nameWithoutOptions = section.Key.Substring(0, section.Key.Length - "Options".Length);

                if (ctx.Options.ContainsKey(nameWithoutOptions))
                {
                    ctx.WarnUnrecognizedSection(
                        key: section,
                        didYouMean: nameWithoutOptions,
                        hint: "Sections do not have \"-Options\" suffix.");

                    // continue with corrected section name
                    return ctx.Options[nameWithoutOptions];
                }
                else if (TryFindInsensitive(nameWithoutOptions, ctx.Options.Keys, out string wronglyCasedMatch))
                {
                    ctx.WarnUnrecognizedSection(
                        key: section,
                        didYouMean: wronglyCasedMatch,
                        hint: "Names are Case Sensitive. Sections do not have \"-Options\" suffix.");

                    // continue with corrected section name
                    return ctx.Options[wronglyCasedMatch];
                }
                else
                {
                    if (source.TolerateUnknownFields)
                    {
                        ctx.WarnUnrecognizedSection(
                            key: section,
                            didYouMean: null,
                            hint: "Sections do not have \"-Options\" suffix.");
                        return null;
                    }
                }
            }
            else if (TryFindInsensitive(section.Key, ctx.Options.Keys, out string wronglyCasedMatch))
            {
                ctx.WarnUnrecognizedSection(
                    key: section,
                    didYouMean: wronglyCasedMatch,
                    hint: "Names are Case Sensitive.");

                // continue with corrected section name
                return ctx.Options[wronglyCasedMatch];
            }
            else
            {
                if (source.TolerateUnknownFields)
                {
                    ctx.WarnUnrecognizedSection(
                        key: section,
                        didYouMean: null,
                        hint: null);
                    return null;
                }
            }

            ctx.Error(section, $"No such Runtime Option");
            return null;
        }

        static bool TryFindInsensitive(string needle, IEnumerable<string> haystack, out string match)
        {
            match = haystack.Where(haystraw => string.Equals(needle, haystraw, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
            return match != null;
        }

        static object BindConfigurationToObject(Context ctx, Type targetType, object originalMaybe, IConfigurationSection configElement)
        {
            IConfigurationSection[] children = configElement.GetChildren().ToArray();

            // \todo: multiple sources can touch the same value.
            ctx.MappingsSources[ctx.CurrentOptions.GetType()][configElement.Path] = ctx.CurrentSource;

            if (configElement.Value != null)
            {
                if (children.Length != 0)
                    ctx.Error(configElement, $"Config element is invalid. It has both value \"{configElement.Value}\" and child elements.");

                object result = BindValueToObject(ctx, targetType, configElement.Value, errorSourceSection: configElement);
                return result;
            }

            if (children.Length == 0)
            {
                // Null terminal value. Allow for nullable targets.
                if (targetType.CanBeNull())
                    return null;

                ctx.Error(configElement, $"Config element is invalid. It has no value nor child elements, nor is the target type nullable.");
                throw new CouldNotBindException();
            }

            if (targetType.IsDictionary())
                return BindConfigurationToDictionary(ctx, targetType, originalMaybe, configElement, children);
            else if (targetType.IsArray)
                return BindConfigurationToArray(ctx, targetType, originalMaybe, children);
            else if (targetType.ImplementsGenericInterface(typeof(ICollection<>)))
                return BindConfigurationToICollection(ctx, targetType, originalMaybe, configElement, children);

            // If the type has a [TypeConverter] attribute for a converter that parses the data from a primitive sequence (string[]),
            // parse the children as such.
            TypeConverter converter = TypeDescriptor.GetConverter(targetType);
            if (converter.CanConvertFrom(typeof(string[])))
            {
                object[] sequenceObjects = BindToSequence(ctx, elementType: typeof(string), children);
                string[] sequenceStrings = new string[sequenceObjects.Length];
                Array.Copy(sequenceObjects, sequenceStrings, sequenceObjects.Length);
                return converter.ConvertFrom(sequenceStrings);
            }

            return BindConfigurationToClass(ctx, targetType, originalMaybe, configElement, children);
        }

        static object BindValueToObject(Context ctx, Type targetType, string value, IConfigurationSection errorSourceSection)
        {
            try
            {
                // Nullable<T> := "" | T
                if (targetType.IsGenericTypeOf(typeof(Nullable<>)))
                {
                    if (value == "")
                        return null;
                    return BindValueToObject(ctx, targetType.GetSystemNullableElementType(), value, errorSourceSection);
                }

                // Default DateTime parser has silly defaults for timezones.
                if (targetType == typeof(DateTime))
                {
                    DateTime dt = DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                    if (dt.Kind != DateTimeKind.Utc)
                        ctx.WarnKey(errorSourceSection, $"Datetime value \"{value}\" is not an UTC timestamp. Use UTC timestamps by adding Z timezone specifier.");
                    return dt;
                }

                // Other types should use [TypeConverter] attribute to register custom handling.
                TypeConverter converter = TypeDescriptor.GetConverter(targetType);
                return converter.ConvertFromInvariantString(value);
            }
            catch (Exception ex)
            {
                if (value == "")
                {
                    // No value. This could be just a stray "Foo:". For top level objects, we know it must be a stray
                    // section header, but for nested objects we cannot be sure.
                    if (errorSourceSection.Path == errorSourceSection.Key)
                        ctx.WarnKey(errorSourceSection, $"Stray section header for {targetType.ToGenericTypeString()}. Ignoring. Note that in YAML, sections with no member fields are interpreted as empty strings.");
                    else if (ctx.CurrentSource.TolerateEmptyObjectValues)
                        ctx.WarnKey(errorSourceSection, $"Config element is an empty string that cannot be parsed as type {targetType.ToGenericTypeString()}. Ignoring. Note that in YAML, sections with no member fields are interpreted as empty strings.");
                    else
                        ctx.Error(errorSourceSection, $"Config element is invalid. Cannot parse an empty string as type {targetType.ToGenericTypeString()}. Note that in YAML, sections with no member fields are interpreted as empty strings.", ex);
                }
                else
                    ctx.Error(errorSourceSection, $"Config element is invalid. Cannot parse string \"{value}\" as type {targetType.ToGenericTypeString()}.", ex);
                throw new CouldNotBindException();
            }
        }

        static object BindConfigurationToClass(Context ctx, Type targetType, object originalMaybe, IConfigurationSection sectionConfig, IConfigurationSection[] children)
        {
            object target = originalMaybe;
            if (target == null)
            {
                try
                {
                    target = Activator.CreateInstance(targetType);
                }
                catch (Exception ex)
                {
                    ctx.Error(sectionConfig, $"Cannot instantiate {targetType.ToGenericTypeString()}", ex);
                    throw new CouldNotBindException();
                }
            }

            foreach (IConfigurationSection fieldConfig in children)
            {
                PropertyInfo pi = targetType.GetProperty(fieldConfig.Key, BindingFlags.Public | BindingFlags.Instance);

                // If no direct hit, try case insensitive search
                if (pi == null)
                {
                    pi = targetType.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(candidate => candidate.Name.Equals(fieldConfig.Key, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
                    if (pi != null)
                        ctx.WarnKey(fieldConfig, $"No such property in {targetType.ToGenericTypeString()}. Using \"{pi.Name}\" instead. Names are Case Sensitive.");
                }

                // If missing fields are to be tolerated, just warn and continue
                if (pi == null)
                {
                    if (ctx.CurrentSource.TolerateUnknownFields)
                    {
                        ctx.WarnUnrecognizedKey(fieldConfig, didYouMean: null, hint: null);
                        continue;
                    }

                    ctx.Error(fieldConfig, $"No such property in {targetType.ToGenericTypeString()}.");
                    continue;
                }

                if (pi.GetCustomAttribute<ComputedValueAttribute>() != null)
                {
                    ctx.Error(fieldConfig, $"Attempted to set config property \"{fieldConfig.Key}\" which is readonly due to [ComputedValue].");
                    continue;
                }
                if (pi.GetCustomAttribute<IgnoreDataMemberAttribute>() != null)
                {
                    ctx.Error(fieldConfig, $"Attempted to set config property \"{fieldConfig.Key}\" which is readonly due to [IgnoreDataMember].");
                    continue;
                }

                Action<object, object> setter = pi.GetDataMemberSetValueOnDeclaringType();
                if (setter == null)
                {
                    ctx.Error(fieldConfig, $"Property (\"{fieldConfig.Key}\") in {targetType.ToGenericTypeString()} is readonly.");
                    continue;
                }

                Func<object, object> getter = pi.GetDataMemberGetValueOnDeclaringType();
                if (getter == null)
                {
                    ctx.Error(fieldConfig, $"Property (\"{fieldConfig.Key}\") in {targetType.ToGenericTypeString()} is unreadable.");
                    continue;
                }

                try
                {
                    object fieldValue = getter(target);
                    object newValue = BindConfigurationToObject(ctx, pi.PropertyType, fieldValue, fieldConfig);
                    setter(target, newValue);
                }
                catch (CouldNotBindException)
                {
                }
            }

            return target;
        }

        /// <summary>
        /// Merges Dictionary with the input values.
        /// </summary>
        static object BindConfigurationToDictionary(Context ctx, Type targetType, object originalMaybe, IConfigurationSection sectionConfig, IConfigurationSection[] children)
        {
            (Type keyType, Type valueType) = targetType.GetDictionaryKeyAndValueTypes();

            object target = originalMaybe;
            if (target == null)
            {
                try
                {
                    target = Activator.CreateInstance(targetType);
                }
                catch (Exception ex)
                {
                    ctx.Error(sectionConfig, $"Cannot instantiate {targetType.ToGenericTypeString()}", ex);
                    throw new CouldNotBindException();
                }
            }

            bool hasAllNumberedElements = true;
            int errorsBefore = ctx.Errors.Count;
            foreach ((IConfigurationSection element, int ndx) in children.ZipWithIndex())
            {
                if (element.Value != null || element.GetChildren().Count() != 1 || element.Key != ndx.ToString(CultureInfo.InvariantCulture))
                {
                    hasAllNumberedElements = false;
                    break;
                }
            }

            Type idictType = typeof(IDictionary<,>).MakeGenericType(keyType, valueType);
            foreach ((IConfigurationSection element, int ndx) in children.ZipWithIndex())
            {
                try
                {
                    object keyObject = BindValueToObject(ctx, keyType, element.Key, errorSourceSection: element);
                    object originalValue = GetDictionaryValueOrNull(idictType, target, keyObject);
                    object valueObject = BindConfigurationToObject(ctx, valueType, originalValue, element);

                    SetDictionaryValue(idictType, target, keyObject, valueObject);
                }
                catch (CouldNotBindException)
                {
                }
            }

            if (errorsBefore != ctx.Errors.Count && children.Length > 0 && hasAllNumberedElements)
            {
                ctx.WarnKey(sectionConfig, $"This dictionary is declared as a sequence of elements. Dictionaries elements should be declared as direct sub-fields of the object, not as a sequence of Keys.");
            }

            return target;
        }

        static object[] BindToSequence(Context ctx, Type elementType, IConfigurationSection[] children)
        {
            List<object> objects = new List<object>();

            foreach ((IConfigurationSection element, int ndx) in children.ZipWithIndex())
            {
                if (element.Key != ndx.ToString(CultureInfo.InvariantCulture))
                {
                    if (int.TryParse(element.Key, NumberStyles.None, CultureInfo.InvariantCulture, out int _))
                        ctx.WarnKey(element, $"Sequence elements are not contiguous, element index {(uint)ndx} claimed index {element.Key}");
                    else
                        ctx.WarnKey(element, $"Expected sequence at element index {(uint)ndx} but got non-index {element.Key}");
                }

                try
                {
                    object elementObject = BindConfigurationToObject(ctx, elementType, originalMaybe: null, element);
                    objects.Add(elementObject);
                }
                catch (CouldNotBindException)
                {
                }
            }

            return objects.ToArray();
        }

        /// <summary>
        /// Replaces the Array with the new sequence of new entries.
        /// </summary>
        static object BindConfigurationToArray(Context ctx, Type targetType, object originalMaybe, IConfigurationSection[] children)
        {
            Type elementType = targetType.GetCollectionElementType();
            object[] elements = BindToSequence(ctx, elementType, children);

            Array target = Array.CreateInstance(elementType, length: elements.Length);
            Array.Copy(elements, target, elements.Length);

            return target;
        }

        /// <summary>
        /// Replaces the ICollection with the new sequence of new entries.
        /// </summary>
        static object BindConfigurationToICollection(Context ctx, Type targetType, object originalMaybe, IConfigurationSection sectionConfig, IConfigurationSection[] children)
        {
            Type elementType = targetType.GetCollectionElementType();
            object[] elements = BindToSequence(ctx, elementType, children);

            object target;
            try
            {
                target = Activator.CreateInstance(targetType);
            }
            catch (Exception ex)
            {
                ctx.Error(sectionConfig, $"Cannot instantiate {targetType.ToGenericTypeString()}", ex);
                throw new CouldNotBindException();
            }

            Type icollectionType = typeof(ICollection<>).MakeGenericType(elementType);
            foreach (object element in elements)
                AddToICollection(icollectionType, target, element);

            return target;
        }

        static object GetDictionaryValueOrNull(Type idictType, object dict, object key)
        {
            MethodInfo tryGetValue = idictType.GetMethod("TryGetValue");
            object[] tryGetValueArgs = new object[] { key, null };
            object retVal = tryGetValue.Invoke(dict, tryGetValueArgs);
            if (retVal is true)
                return tryGetValueArgs[1];
            return null;
        }

        static void SetDictionaryValue(Type idictType, object dict, object key, object value)
        {
            PropertyInfo indexer = idictType.GetProperty("Item");
            MethodInfo setter = indexer.GetSetMethodOnDeclaringType();
            setter.Invoke(dict, new object[] { key, value });
        }

        static void AddToICollection(Type icollectionType, object collection, object elem)
        {
            MethodInfo add = icollectionType.GetMethod("Add");
            add.Invoke(collection, new object[] { elem });
        }

        static bool IsValueAndParseable(Type type)
        {
            // Has value conversion? If so, assume it's a primitive.
            TypeConverter converter = TypeDescriptor.GetConverter(type);
            if (converter.CanConvertFrom(typeof(string)))
                return true;

            return false;
        }

        static void CheckObjectIsParseable(Type runtimeOptsType, Type type)
        {
            if (type.IsCollection())
            {
                if (type.IsDictionary())
                {
                    (Type keyType, Type valueType) = type.GetDictionaryKeyAndValueTypes();

                    if (!IsValueAndParseable(keyType))
                        throw new InvalidOperationException($"In {runtimeOptsType.ToGenericTypeString()}, dictionary {type.ToGenericTypeString()} is unparseable because it has a key type {keyType.ToGenericTypeString()} which has no converter from string. Implement a TypeConverter.");

                    CheckObjectIsParseable(runtimeOptsType, valueType);
                }
                else if (type.IsArray)
                {
                    CheckObjectIsParseable(runtimeOptsType, type.GetElementType());
                }
                else if (type.ImplementsGenericInterface(typeof(ICollection<>)))
                {
                    CheckObjectIsParseable(runtimeOptsType, type.GetCollectionElementType());
                }
                else
                    throw new InvalidOperationException($"In {runtimeOptsType.ToGenericTypeString()}, type {type.ToGenericTypeString()} is unsupported collection type. Only IDictionary<,>, ICollection<>, and array are supported.");
            }
            else
            {
                // Directly parseable
                if (IsValueAndParseable(type))
                    return;

                // Objects must be constructible and all properties must be parseable
                ConstructorInfo ci = type.GetConstructor(Type.EmptyTypes);
                if (ci == null && !type.IsValueType)
                    throw new InvalidOperationException($"In {runtimeOptsType.ToGenericTypeString()}, object {type.ToGenericTypeString()} is unparseable because it has no default constructor.");

                foreach (PropertyInfo pi in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
                {
                    if (pi.GetCustomAttribute<IgnoreDataMemberAttribute>() != null)
                        continue;
                    if (pi.GetCustomAttribute<ComputedValueAttribute>() != null)
                        continue;

                    Action<object, object> setter = pi.GetDataMemberSetValueOnDeclaringType();
                    if (setter == null)
                        continue;

                    CheckObjectIsParseable(runtimeOptsType, pi.PropertyType);
                }
            }
        }

        static string GetSectionContentHash(IConfigurationSection section)
        {
            // Resolve the full payload of the config section (to avoid spurious updates)
            // \todo [petri] would be enough to just compute aggregate hash, no need to generate interim string
            string payload = GetSectionString(section);
            string contentHash = Util.ComputeMD5(payload);
            return contentHash;
        }

        static string GetSectionString(IConfigurationSection section)
        {
            StringBuilder sb = new StringBuilder(128);
            GetSectionString(sb, section);
            return sb.ToString();
        }

        static void GetSectionString(StringBuilder sb, IConfigurationSection section)
        {
            // Append value (if has one)
            if (section.Value != null)
                sb.AppendLine($"{section.Path} = {section.Value}");

            // Recurse to children
            foreach (IConfigurationSection child in section.GetChildren())
                GetSectionString(sb, child);
        }
    }
}
