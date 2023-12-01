// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Config;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Metaplay.Core.Json
{
    /// <summary>
    /// Error logger used to capture exceptions during JSON serialization, specifically used in the AdminAPI.
    /// Exceptions in the AdminAPI serialization are ignored and logged after serialization is complete.
    /// </summary>
    public class JsonSerializationErrorLogger
    {
        public List<Exception> Errors { get; } = new List<Exception>();

        public void RecordError(ErrorContext err)
        {
            Errors.Add(err.Error);
        }
    }

    public static class JsonSerialization
    {
        public struct Options
        {
            public JsonSerializationMode    Mode;
            public Type                     TraverseGameConfigDataType;     // Serialize members of given GameConfigData type (normally GameConfigData types are serialized as their config keys)
            public bool                     ExcludeReadOnlyProperties;
            public IGameConfigDataResolver  GameConfigDataResolver;
            public bool                     ExcludeGdprExportMembers;
            public TypeNameHandling         TypeNameHandling;
            public NamingStrategy           NamingStrategy;

            public Options(JsonSerializationMode mode, Type traverseGameConfigDataType, bool excludeReadOnlyProperties, IGameConfigDataResolver gameConfigDataResolver,
                bool excludeGdprExportMembers, TypeNameHandling typeNameHandling, NamingStrategy namingStrategy)
            {
                Mode = mode;
                TraverseGameConfigDataType = traverseGameConfigDataType;
                ExcludeReadOnlyProperties = excludeReadOnlyProperties;
                GameConfigDataResolver = gameConfigDataResolver;
                ExcludeGdprExportMembers = excludeGdprExportMembers;
                TypeNameHandling = typeNameHandling;
                NamingStrategy = namingStrategy;
            }

            /// <summary>
            /// Construct a memberwise clone of the given options.
            /// </summary>
            public Options(Options other)
            {
                this = other;
            }

            public static readonly Options DefaultOptions = new Options(
                mode:                           JsonSerializationMode.Default,
                traverseGameConfigDataType:     null,
                excludeReadOnlyProperties:      false,
                gameConfigDataResolver:         null,
                excludeGdprExportMembers:       false,
                typeNameHandling:               TypeNameHandling.Auto,
                // CamelCaseNamingStrategy converts properties into lower camelcase, for example `someObject.FooBar = 1` into `{ "fooBar": 1 }`
                namingStrategy:                 new CamelCaseNamingStrategy());
        }

        public static readonly JsonSerializer DefaultSerializer = CreateSerializer(Options.DefaultOptions);

        public static readonly JsonSerializer GdprSerializer = CreateSerializer(
            new Options(Options.DefaultOptions)
            {
                Mode                        = JsonSerializationMode.GdprExport,
                ExcludeGdprExportMembers    = true,
            });

        public static readonly JsonSerializer AnalyticsEventSerializer = CreateSerializer(
            new Options(Options.DefaultOptions)
            {
                Mode                        = JsonSerializationMode.AnalyticsEvents,
                TypeNameHandling            = TypeNameHandling.None,
            });

        public static readonly JsonSerializer IncludeObjectTypeSerializer = CreateSerializer(
            new Options(Options.DefaultOptions)
            {
                TypeNameHandling = TypeNameHandling.Objects,
            });

        public static string SerializeToString<T>(T value, JsonSerializer serializer = null)
        {
            StringBuilder sb = new StringBuilder();
            using (StringWriter writer = new StringWriter(sb))
            using (JsonWriter jsonWriter = new JsonTextWriter(writer))
            {
                (serializer ?? DefaultSerializer).Serialize(jsonWriter, value);
            }
            return sb.ToString();
        }

        public static void Serialize<T>(T value, JsonSerializer serializer, JsonTextWriter jsonWriter)
        {
            (serializer ?? DefaultSerializer).Serialize(jsonWriter, value);
        }

        public static T Deserialize<T>(byte[] input, JsonSerializer serializer = null)
        {
            using (MemoryStream stream = new MemoryStream(input))
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
            using (JsonTextReader jsonReader = new JsonTextReader(reader))
                return (serializer ?? DefaultSerializer).Deserialize<T>(jsonReader);
        }

        public static T Deserialize<T>(Stream input, JsonSerializer serializer = null)
        {
            using (StreamReader reader = new StreamReader(input, Encoding.UTF8))
            using (JsonTextReader jsonReader = new JsonTextReader(reader))
                return (serializer ?? DefaultSerializer).Deserialize<T>(jsonReader);
        }

        public static T Deserialize<T>(string input, JsonSerializer serializer = null)
        {
            using (StringReader textReader = new StringReader(input))
            using (JsonTextReader jsonReader = new JsonTextReader(textReader))
                return (serializer ?? DefaultSerializer).Deserialize<T>(jsonReader);
        }

        static void PopulateJsonSettings(JsonSerializerSettings settings, Options options)
        {
            settings.FloatParseHandling = FloatParseHandling.Decimal;
            settings.DateParseHandling = DateParseHandling.None;
            settings.TypeNameHandling = options.TypeNameHandling;
            settings.Converters.Add(new StringEnumConverter());
            if (options.TypeNameHandling == TypeNameHandling.Objects)
                settings.Converters.Add(new NoTypeDictionaryConverter());
            settings.MetadataPropertyHandling = MetadataPropertyHandling.ReadAhead;
            settings.ContractResolver = new MetaplayJsonContractResolver(options, PopulateJsonSettings);
        }

        public static JsonSerializer CreateSerializer(Options options)
        {
            JsonSerializerSettings settings = new JsonSerializerSettings();
            PopulateJsonSettings(settings, options);
            return JsonSerializer.Create(settings);
        }
    }
}
