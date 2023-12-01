// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if NETCOREAPP // cloud
using Akka.Actor;
#endif
using System;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Globalization;
using Metaplay.Core.Config;
using static System.FormattableString;

namespace Metaplay.Core
{
    /// <summary>
    /// Field represents secure information (such as passwords) and should be sanitized when outputted into logs
    /// or sent to the dashboard. Also works when serializing to json. In json-serialization, all non-null values
    /// are converted to strings.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class SensitiveAttribute : Attribute
    {
    }

    // PrettyPrintFlag

    [Flags]
    public enum PrettyPrintFlag
    {
        None        = 0,
        SizeOnly    = 1 << 0,   // Only print size of a collection
        Shorten     = 1 << 2,   // Show a shortened version of the data to avoid spamming logs.
        Hide        = 1 << 3,   // Hide member field from printing
        HideInDiff  = 1 << 4,   // Hide member field from printing, but only when printing object diffs
    }

    // PrettyPrintAttribute

    public class PrettyPrintAttribute : Attribute
    {
        public PrettyPrintFlag Flags { get; private set; }

        public PrettyPrintAttribute(PrettyPrintFlag flags)
        {
            Flags = flags;
        }
    }

    // PrettyPrinter

    public static class PrettyPrinter
    {
        public const int MaxDepth   = 20;           // Maximum recursion depth
        public const int MaxLength  = 1024 * 1024;  // Maximum length of output

        private static Dictionary<Type, Func<object, bool, string>> _customFormatters = new Dictionary<Type, Func<object, bool, string>>();

        static readonly string[] s_indent = Enumerable.Range(0, MaxDepth + 1).Select(depth => new string(' ', depth * 2)).ToArray();

        public static void RegisterFormatter(Type type, Func<object, bool, string> formatter)
        {
            _customFormatters.Add(type, formatter);
        }

        public static void RegisterFormatter<T>(Func<T, bool, string> formatter)
        {
            _customFormatters.Add(typeof(T), (obj, isCompact) => formatter((T)obj, isCompact));
        }

        private static void Print(StringBuilder sb, object obj, bool isCompact, int depth, PrettyPrintFlag flags, bool isSensitive, bool isRoot = false)
        {
            try
            {
                if (sb.Length >= MaxLength)
                    return;

                if (obj == null)
                {
                    sb.Append("null");
                    return;
                }

                if (isSensitive)
                {
                    sb.Append("XXX");
                    return;
                }

                if (depth > MaxDepth)
                {
                    sb.Append("<recursed too deep>");
                    return;
                }

                Type type = obj.GetType();
                string indent = isCompact ? "" : s_indent[depth];
                string lf = isCompact ? "" : "\n";

                // For IGameConfigData references, print the ConfigKey (except if on root level)
                if (type.ImplementsInterface<IGameConfigData>() && !isRoot)
                {
                    // Find proper IGameConfigData<T>
                    foreach (Type interfaceType in type.GetInterfaces())
                    {
                        if (!interfaceType.IsGenericType)
                            continue;
                        if (interfaceType.GetGenericTypeDefinition() != typeof(IGameConfigKey<>))
                            continue;

                        object configKey = interfaceType.GetProperty("ConfigKey").GetValue(obj);
                        Print(sb, configKey, isCompact, depth, flags, isSensitive);
                        return;
                    }
                }

                // Handle simple classes first
                if (_customFormatters.TryGetValue(type, out Func<object, bool, string> customFunc))
                    sb.Append(customFunc(obj, isCompact));
                else if (typeof(MulticastDelegate).IsAssignableFrom(type))
                    sb.Append("event");
                else if (type == typeof(object))
                    sb.Append("object");
                else if (type == typeof(string))
                {
                    string str = (string)obj;

                    if (flags.HasFlag(PrettyPrintFlag.Shorten))
                        str = Util.ShortenString(str, maxLength: 24);

                    sb.Append(str);
                }
                else if (obj is IMetaRef metaRef)
                {
                    if (metaRef.IsResolved)
                        sb.Append("(resolved: ");
                    else
                        sb.Append("(non-resolved: ");

                    Print(sb, metaRef.KeyObject, isCompact, depth, flags, isSensitive);
                    sb.Append(")");
                }
                else if (obj is Uri uri)
                    sb.Append(uri.OriginalString);
                else if (!type.IsClass)
                    sb.Append(Util.ObjectToStringInvariant(obj));
                else if (typeof(Exception).IsAssignableFrom(type))
                    sb.Append(Util.ObjectToStringInvariant(obj));
                else if (obj is Type typeObj)
                    sb.Append(typeObj.ToNamespaceQualifiedTypeString());
#if NETCOREAPP // cloud
                else if (obj is IActorRef)
                    sb.Append(Util.ObjectToStringInvariant(obj));
#endif
                else if (typeof(IEnumerable).IsAssignableFrom(type))
                {
                    if (typeof(IDictionary).IsAssignableFrom(type))
                    {
                        // Key-Value collections

                        IDictionary dict = (IDictionary)obj;
                        if (dict.Count == 0)
                        {
                            sb.Append("{}");
                            return;
                        }

                        sb.Append(isCompact ? "{ " : "{\n");

                        if (flags.HasFlag(PrettyPrintFlag.SizeOnly))
                        {
                            sb.Append(Invariant($"{dict.Count} elems"));
                        }
                        else
                        {
                            bool isFirst = true;
                            foreach (DictionaryEntry entry in dict)
                            {
                                if (isCompact && !isFirst)
                                    sb.Append(", ");
                                isFirst = false;

                                sb.Append(isCompact ? "" : indent + "  ");
                                Print(sb, entry.Key, isCompact, depth, PrettyPrintFlag.None, isSensitive: false);
                                sb.Append(": ");
                                Print(sb, entry.Value, isCompact, depth + 1, PrettyPrintFlag.None, isSensitive: false);
                                sb.Append(isCompact ? "" : "\n");
                            }
                        }

                        sb.Append(isCompact ? " }" : indent + "}");
                    }
                    else if (typeof(Array).IsAssignableFrom(type) && ((Array)obj).Rank > 1)
                    {
                        // Multi dimensional arrays

                        Array array = (Array)obj;

                        sb.Append("[");
                        bool isFirst = true;
                        for (int dim = 0; dim < array.Rank; dim++)
                        {
                            sb.Append(array.GetLength(dim).ToString(CultureInfo.InvariantCulture));
                            if (!isFirst)
                                sb.Append(", ");
                            isFirst = false;
                        }
                        sb.Append("]");
                    }
                    else
                    {
                        // Value collections
                        IEnumerable enumerable = (IEnumerable)obj;
                        ICollection collectionMaybe = obj as ICollection;

                        Type guessedElemType = type.GetElementType() ?? (type.GetGenericArguments().Length > 0 ? type.GetGenericArguments()[0] : null);
                        if (guessedElemType == typeof(byte) || flags.HasFlag(PrettyPrintFlag.SizeOnly))
                        {
                            int count;
                            if (collectionMaybe != null)
                                count = collectionMaybe.Count;
                            else
                            {
                                count = 0;
                                foreach (object elem in enumerable)
                                    count++;
                            }
                            if (guessedElemType == typeof(byte))
                                sb.Append(Invariant($"[ {count} bytes ]"));
                            else if (count > 0)
                                sb.Append(Invariant($"[ {count} elems ]"));
                            else
                                sb.Append(Invariant($"[]"));
                        }
                        else
                        {
                            sb.Append("[");

                            int ndx = 0;
                            foreach (object elem in enumerable)
                            {
                                if (isCompact)
                                {
                                    if (ndx == 0)
                                        sb.Append(' ');
                                    else
                                        sb.Append(", ");
                                }
                                else
                                {
                                    sb.Append('\n');
                                    sb.Append(Invariant($"{indent}  [{ndx}] = "));
                                }

                                Print(sb, elem, isCompact, depth + 1, PrettyPrintFlag.None, isSensitive: false);
                                ndx++;
                            }

                            if (ndx == 0)
                            {
                                // empty
                                sb.Append(']');
                            }
                            else if (isCompact)
                            {
                                sb.Append(" ]");
                            }
                            else
                            {
                                sb.Append($"\n{indent}]\n");
                            }
                        }
                    }
                }
                else if (obj is IDynamicEnum dynamicEnum)
                {
                    sb.Append(dynamicEnum.Name);
                }
                else if (obj is IStringId stringId)
                {
                    sb.Append($"&{type.Name}.{stringId}");
                }
                else // generic object
                {
                    sb.Append(type.ToGenericTypeString());
                    sb.Append(isCompact ? "{ " : " {\n");

                    bool isFirst = true;
                    foreach (PropertyInfo prop in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        // Ignore generated props & ones with IgnoreDataMember attribute
                        if (prop.GetGetMethod(true) == null || prop.GetGetMethod(true).IsStatic || prop.Name == "Descriptor" || Attribute.IsDefined(prop, typeof(IgnoreDataMemberAttribute), inherit: true))
                            continue;
                        // Ignore Indexer properties
                        if (prop.GetIndexParameters().Length > 0)
                            continue;
                        // Ignore explicitly implemented properties. These are hidden by default in C#, so it's safe to say they should be hidden here as well.
                        if (prop.Name.Contains('.'))
                            continue;

                        PrettyPrintAttribute attrib = prop.GetCustomAttribute<PrettyPrintAttribute>();
                        PrettyPrintFlag memberFlags = (attrib != null) ? attrib.Flags : PrettyPrintFlag.None;
                        bool isMemberSensitive = prop.GetCustomAttribute<SensitiveAttribute>() != null;

                        if (memberFlags.HasFlag(PrettyPrintFlag.Hide))
                            continue;

                        if (!isFirst && isCompact)
                            sb.Append(", ");
                        isFirst = false;

                        sb.Append(isCompact ? "" : indent + "  ");
                        sb.Append(prop.Name);
                        sb.Append(isCompact ? "=" : " = ");

                        object value = prop.GetValue(obj);
                        Print(sb, value, isCompact, depth + 1, memberFlags, isMemberSensitive);

                        sb.Append(lf);
                    }

                    foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Where(field => !field.Name.Contains("__BackingField")))
                    {
                        // Ignore static fields and ones with IgnoreDataMember attribute
                        if (field.IsStatic || Attribute.IsDefined(field, typeof(IgnoreDataMemberAttribute), inherit: true))
                            continue;

                        // Ignore MulticastDelegates (events)
                        if (typeof(MulticastDelegate).IsAssignableFrom(field.FieldType))
                            continue;

                        PrettyPrintAttribute attrib = field.GetCustomAttribute<PrettyPrintAttribute>();
                        PrettyPrintFlag memberFlags = (attrib != null) ? attrib.Flags : PrettyPrintFlag.None;
                        bool isMemberSensitive = field.GetCustomAttribute<SensitiveAttribute>() != null;

                        if (memberFlags.HasFlag(PrettyPrintFlag.Hide))
                            continue;

                        if (!isFirst && isCompact)
                            sb.Append(", ");
                        isFirst = false;

                        sb.Append(isCompact ? "" : indent + "  ");
                        sb.Append(field.Name);
                        sb.Append(isCompact ? "=" : " = ");

                        object value = field.GetValue(obj);
                        Print(sb, value, isCompact, depth + 1, memberFlags, isMemberSensitive);

                        sb.Append(lf);
                    }

                    sb.Append(isCompact ? " }" : indent + "}\n");
                }
            }
            catch (Exception ex)
            {
                sb.Append(Invariant($"Exception when printing '{obj}' ({obj.GetType()}: {ex}\n"));
            }
        }

        public static string Compact(object obj)
        {
            StringBuilder sb = new StringBuilder(128);
            Print(sb, obj, true, 0, PrettyPrintFlag.None, isSensitive: false, isRoot: true);
            return sb.ToString();
        }

        public static string Verbose(object obj)
        {
            StringBuilder sb = new StringBuilder(128);
            Print(sb, obj, false, 0, PrettyPrintFlag.None, isSensitive: false, isRoot: true);
            string str = sb.ToString();
            return RemoveEmptyLines(str);
        }

        static string RemoveEmptyLines(string str)
        {
            // \todo [petri] kludge to drop empty lines .. should really fix Print(), but it's a bit tricky
            return Regex.Replace(str, @"^\s*$\n|\r", string.Empty, RegexOptions.Multiline);
        }

        static bool IsEqual(object a, object b)
        {
            // \todo [petri] this is slow, but only used for checksum mismatch reports..
            return Compact(a) == Compact(b);
        }

        static void Difference(StringBuilder sb, object a, object b, int depth)
        {
            if (IsEqual(a, b))
                return;
            else if (a == null)
            {
                sb.Append("(null vs <entry>)");
                return;
            }
            else if (b == null)
            {
                sb.Append("(<entry> vs null)");
                return;
            }

            string indent = s_indent[depth];

            Type type = a.GetType();
            if (type != b.GetType())
            {
                sb.Append($"Mismatched types {a.GetType().Name} vs {b.GetType().Name} !!");
                return;
            }

            if (typeof(ICollection).IsAssignableFrom(type))
            {
                if (typeof(IDictionary).IsAssignableFrom(type))
                {
                    IDictionary aDict = (IDictionary)a;
                    IDictionary bDict = (IDictionary)b;

                    sb.Append("{\n");

                    foreach (DictionaryEntry entry in aDict)
                    {
                        object key = entry.Key;
                        if (bDict.Contains(key))
                        {
                            if (!IsEqual(bDict[key], aDict[key]))
                                sb.Append(Invariant($"{indent}  {key}: "));
                            Difference(sb, aDict[key], bDict[key], depth + 1);
                            sb.Append("\n");
                        }
                        else
                            sb.Append(Invariant($"{indent}  {key}: (<entry> vs null)\n"));
                    }

                    foreach (DictionaryEntry entry in bDict)
                    {
                        object key = entry.Key;
                        if (!aDict.Contains(key))
                            sb.Append(Invariant($"{indent}  {key}: (null vs <entry>)\n"));
                    }

                    sb.Append(indent + "}");
                }
                else // value collection
                {
                    ICollection aColl = (ICollection)a;
                    ICollection bColl = (ICollection)b;

                    if (aColl.Count != bColl.Count)
                        sb.Append(Invariant($" ((size mismatch: {aColl.Count} vs {bColl.Count})) "));

                    sb.Append("[\n");

                    int maxSize = System.Math.Max(aColl.Count, bColl.Count);
                    IEnumerator aEnum = aColl.GetEnumerator();
                    IEnumerator bEnum = bColl.GetEnumerator();
                    for (int ndx = 0; ndx < maxSize; ndx++)
                    {
                        object aElem = aEnum.MoveNext() ? aEnum.Current : null;
                        object bElem = bEnum.MoveNext() ? bEnum.Current : null;

                        if (!IsEqual(aElem, bElem))
                        {
                            sb.Append(Invariant($"{indent}  [{ndx}] = "));
                            Difference(sb, aElem, bElem, depth + 1);
                            sb.Append("\n");
                        }
                    }

                    sb.Append(indent + "]");
                }
            }
            else if (type == typeof(string))
            {
                if (!IsEqual(a, b))
                    sb.Append($"{(string)a} vs {(string)b}");
            }
            else if (type.IsClass && !type.ImplementsInterface<IMetaRef>())
            {
                sb.Append(type.Name);
                sb.Append(" {\n");

                foreach (var prop in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    // Ignore generated props
                    if (prop.GetGetMethod(true) == null || prop.GetGetMethod(true).IsStatic || prop.Name == "Descriptor" || Attribute.IsDefined(prop, typeof(IgnoreDataMemberAttribute), inherit: true))
                        continue;
                    // Ignore Indexer properties
                    if (prop.GetIndexParameters().Length > 0)
                        continue;
                    // Ignore explicitly implemented properties. These are hidden by default in C#, so it's safe to say they should be hidden here as well.
                    if (prop.Name.Contains('.'))
                        continue;

                    // Check the HideInDiff flag.
                    // \todo [nuutti] `Difference` should support also the other PrettyPrintFlags and also SensitiveAttribute!
                    //                Most are only supported in `Print` at the moment!
                    PrettyPrintAttribute attrib = prop.GetCustomAttribute<PrettyPrintAttribute>();
                    PrettyPrintFlag memberFlags = (attrib != null) ? attrib.Flags : PrettyPrintFlag.None;
                    if (memberFlags.HasFlag(PrettyPrintFlag.HideInDiff))
                        continue;

                    object aValue = prop.GetValue(a);
                    object bValue = prop.GetValue(b);
                    if (!IsEqual(aValue, bValue))
                    {
                        sb.Append($"{indent}  {prop.Name} = ");
                        Difference(sb, aValue, bValue, depth + 1);
                        sb.Append("\n");
                    }
                }

                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Where(field => !field.Name.Contains("__BackingField")))
                {
                    // Ignore static fields and ones with IgnoreDataMember attribute
                    if (field.IsStatic || Attribute.IsDefined(field, typeof(IgnoreDataMemberAttribute), inherit: true))
                        continue;

                    // Check the HideInDiff flag.
                    // \todo [nuutti] `Difference` should support also the other PrettyPrintFlags and also SensitiveAttribute!
                    //                Most are only supported in `Print` at the moment!
                    PrettyPrintAttribute attrib = field.GetCustomAttribute<PrettyPrintAttribute>();
                    PrettyPrintFlag memberFlags = (attrib != null) ? attrib.Flags : PrettyPrintFlag.None;
                    if (memberFlags.HasFlag(PrettyPrintFlag.HideInDiff))
                        continue;

                    object aValue = field.GetValue(a);
                    object bValue = field.GetValue(b);
                    if (!IsEqual(aValue, bValue))
                    {
                        sb.Append($"{indent}  {field.Name} = ");
                        Difference(sb, aValue, bValue, depth + 1);
                        sb.Append("\n");
                    }
                }

                sb.Append(indent + "}\n");
            }
            else
            {
                Print(sb, a, true, 0, PrettyPrintFlag.None, isSensitive: false);
                sb.Append(" vs ");
                Print(sb, b, true, 0, PrettyPrintFlag.None, isSensitive: false);
            }
        }

        public static string Difference<T>(T a, T b)
        {
            StringBuilder sb = new StringBuilder(512);
            Difference(sb, a, b, 0);
            return RemoveEmptyLines(sb.ToString());
        }
    }

    // PrettyPrintable

    public struct PrettyPrintable
    {
        object  _value;
        bool    _isVerbose;

        public PrettyPrintable(object value, bool isVerbose)
        {
            _value = value;
            _isVerbose = isVerbose;
        }

        public override string ToString()
        {
            return _isVerbose ? PrettyPrinter.Verbose(_value) : PrettyPrinter.Compact(_value);
        }
    }

    // PrettyPrint: only returns wrappers, which can later be converted to strings.

    public class PrettyPrint
    {
        public static PrettyPrintable Compact(object obj)
        {
            return new PrettyPrintable(obj, false);
        }

        public static PrettyPrintable Verbose(object obj)
        {
            return new PrettyPrintable(obj, true);
        }
    }
}
