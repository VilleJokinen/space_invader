// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if UNITY_WEBGL && !UNITY_EDITOR
#   define UNITY_WEBGL_BUILD
#endif

using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace Metaplay.Core
{
    /// <summary>
    /// Base interface for <see cref="StringId{T}"/>.
    /// </summary>
    public interface IStringId
    {
        string Value { get; }
    }

    /// <summary>
    /// Utility functions for <see cref="StringId{TStringId}"/>.
    /// </summary>
    public static class StringIdUtil
    {
#if UNITY_WEBGL_BUILD
        static WebConcurrentDictionary<Type, Func<string, IStringId>> s_factory = new WebConcurrentDictionary<Type, Func<string, IStringId>>();
#else
#pragma warning disable MP_WGL_00 // "Feature is poorly supported in WebGL". False positive, this is non-WebGL.
        static ConcurrentDictionary<Type, Func<string, IStringId>> s_factory = new ConcurrentDictionary<Type, Func<string, IStringId>>();
#pragma warning restore MP_WGL_00
#endif

#if UNITY_WEBGL_BUILD
        internal static WebConcurrentDictionary<string, TStringId> RegisterFactory<TStringId>(Func<string, IStringId> factory) where TStringId : StringId<TStringId>, new()
        {
            if (!s_factory.TryAdd(typeof(TStringId), factory))
                throw new InvalidOperationException($"Double-registration of {typeof(TStringId).ToGenericTypeString()}");

            return new WebConcurrentDictionary<string, TStringId>();
        }
#else
        internal static ConcurrentDictionary<string, TStringId> RegisterFactory<TStringId>(Func<string, IStringId> factory) where TStringId : StringId<TStringId>, new()
        {
            if (!s_factory.TryAdd(typeof(TStringId), factory))
                throw new InvalidOperationException($"Double-registration of {typeof(TStringId).ToGenericTypeString()}");

#pragma warning disable MP_WGL_00 // "Feature is poorly supported in WebGL". False positive, this is non-WebGL.
            return new ConcurrentDictionary<string, TStringId>();
#pragma warning restore MP_WGL_00
        }
#endif

        /// <summary>
        /// Create a <see cref="StringId{TStringId}"/> instance when its type is only known dynamically.
        /// </summary>
        /// <param name="type">Type of StringId to create</param>
        /// <param name="value">String value that the StringId instance should have</param>
        /// <returns></returns>
        public static IStringId CreateDynamic(Type type, string value)
        {
            if (!s_factory.TryGetValue(type, out Func<string, IStringId> createFunc))
            {
                // On the dynamic path the StringId-type may not have been initialized yet,
                // force type initialization here. The call is idempotent and thread-safe (relies on
                // static class constructor thread-safety). As the factory dictionary access is also
                // thread safe (using ConcurrentDictionary) the whole operation here is also thread safe.
                MethodInfo registerFunc = type.GetMethod("EnsureTypeInitialized", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
                registerFunc.Invoke(null, null);

                createFunc = s_factory[type];
            }
            return createFunc(value);
        }

        /// <summary>
        /// Custom regex for config-parsing StringIds.
        /// Allow same kind of content as ConfigLexer's vanilla Identifier tokens,
        /// except allow also dots and hyphens after the first character.
        /// For example:
        ///  foo.bar.com.some_in_app_purchase
        ///  pt-BR   (standard language code for Brazilian Portuguese, can be used as a LanguageId)
        /// </summary>
        static readonly ConfigLexer.CustomTokenSpec s_configLexerTokenSpec = new ConfigLexer.CustomTokenSpec(@"[a-zA-Z_][a-zA-Z0-9_\-.]*", name: "StringId");

        public static IStringId ConfigParse(Type type, ConfigLexer lexer)
        {
            string stringIdValue = lexer.ParseCustomToken(s_configLexerTokenSpec);
            return CreateDynamic(type, stringIdValue);
        }
    }

    /// <summary>
    /// Type-safe identifiers for identifying in-game things. Main use case is "dynamic enums", where
    /// enum-like behaviour is desired, but such that game configs can define the values at runtime instead
    /// of needing to be declared in code.
    ///
    /// A concrete StringId can be declare by deriving from the StringId class as follows:
    /// <example>
    /// <code>
    /// class MyTypeId : StringId&lt;MyTypeId&gt; { }
    /// </code>
    /// </example>
    ///
    /// The implementation uses interning (all equal values share the same underlying string), so that equality
    /// comparison can be done using a very fast reference comparison.
    ///
    /// An empty value is represented by the StringId reference itself being null. In order to avoid multiple
    /// empty values, calling <see cref="FromString(string)"/> with null argument returns a null StringId.
    ///
    /// Only ASCII characters are allowed, keep things simple. Values can be up to 1024 characters/bytes in length.
    /// </summary>
    /// <typeparam name="TStringId">Concrete type of StringId (must be derived from <c>StringId</c>)</typeparam>
    // \note: IEquatable<TStringId> covers also IEquatable<StringId<TStringId>>. All instances of StringId<TStringId> are always of type TStringId.
    [TypeConverter(typeof(StringIdTypeConverter))]
    public abstract class StringId<TStringId>
        : IStringId
        , IEquatable<TStringId>
        , IComparable<TStringId>
        , IComparable
        where TStringId : StringId<TStringId>, new()
    {
        public const int    MaxLength   = 1024; // Maximum length of StringId in characters & bytes (only allows ASCII characters)

        public string       Value   { get; private set; }

        // \todo [petri] use weak dictionary for interned values, so temporary values can be forgotten (eg, by clients making requests with spoofed StringIds)
#if UNITY_WEBGL_BUILD
        static readonly WebConcurrentDictionary<string, TStringId> s_interned = StringIdUtil.RegisterFactory<TStringId>(value => FromString(value));
#else
        static readonly ConcurrentDictionary<string, TStringId> s_interned = StringIdUtil.RegisterFactory<TStringId>(value => FromString(value));
#endif

        protected StringId()
        {
        }

        /// <summary>
        /// Ensure that the static class constructor has been run for the concrete StringId class. This is invoked via reflection by
        /// StringIdUtil.CreateDynamic to make sure that the associated factory has been registered.
        /// </summary>
        protected static void EnsureTypeInitialized()
        {
            // \note If this function is empty, it does not trigger initialization of s_interned (at least on .NET Core 3.1)!
            if (s_interned == null)
                throw new InvalidOperationException($"{typeof(TStringId).ToGenericTypeString()}.{nameof(EnsureTypeInitialized)}(): s_interned is null");
        }

        /// <summary>
        /// Create a new StringId based on raw string value. The returned values are interned such
        /// that creating StringId for the same value will always return the same reference.
        ///
        /// Passing in null causes the method to return null. Passing in an empty string
        /// causes an <see cref="ArgumentException"/> to be thrown.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static TStringId FromString(string value)
        {
            if (value == null)
                return null;

            if (value == "")
                throw new ArgumentException($"Empty string not accepted value for {typeof(TStringId).ToGenericTypeString()}", nameof(value));

            // GetOrAdd() allocates memory, so try to get the value first
            if (s_interned.TryGetValue(value, out TStringId existing))
                return existing;
            else
                return s_interned.GetOrAdd(value, CreateValueInternal);
        }

        static TStringId CreateValueInternal(string str)
        {
            if (str.Length > MaxLength)
            {
                // Dummy call to make sure IL2CPP doesn't tree-shake EnsureTypeInitialized() method away
                EnsureTypeInitialized();

                throw new ArgumentException($"Value for {typeof(TStringId).ToGenericTypeString()} is too long (length={str.Length}, max={MaxLength})");
            }

            // Only allow ASCII characters: UTF8 encoded length should be the same as string length
            if (Encoding.UTF8.GetByteCount(str) != str.Length)
                throw new ArgumentException($"Invalid characters for {typeof(TStringId).ToGenericTypeString()}, only ASCII allowed: '{str}'");

            return new TStringId { Value = str };
        }

        public static bool operator ==(StringId<TStringId> a, StringId<TStringId> b) => ReferenceEquals(a, b);

        public static bool operator !=(StringId<TStringId> a, StringId<TStringId> b) => !(a == b);

        public override bool    Equals      (object obj) => (obj is TStringId other) ? ReferenceEquals(this, other) : false;
        public override int     GetHashCode () => Value.GetHashCode();
        public override string  ToString    () => Value;

        public bool Equals(TStringId other) => ReferenceEquals(this, other);

        public int CompareTo(TStringId other)
        {
            if (other == null)
                return +1;
            else
                return string.Compare(Value, other.Value, StringComparison.InvariantCulture);
        }

        int IComparable.CompareTo(object obj) => (obj is TStringId other) ? CompareTo(other) : 1;
    }

    /// <summary>
    /// Type converter for StringId types.
    /// Assigned to <see cref="StringId{T}"/> with <see cref="TypeConverterAttribute"/>,
    /// makes StringId types parseable as strings in various utilities
    /// such as server-side runtime options.
    /// </summary>
    public class StringIdTypeConverter : StringTypeConverterHelper<IStringId>
    {
        Type _stringIdType;

        public StringIdTypeConverter(Type stringIdType)
        {
            _stringIdType = stringIdType;
        }

        protected override string ConvertValueToString(IStringId obj)
            => obj.ToString();

        protected override IStringId ConvertStringToValue(string str)
            => StringIdUtil.CreateDynamic(_stringIdType, str);
    }
}
