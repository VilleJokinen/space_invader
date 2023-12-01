// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Json;
using Metaplay.Core.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Metaplay.Server.AdminApi
{
    /// <summary>
    /// Utilities for AdminAPI / Dashboard style JSON serialization.
    /// </summary>
    public static class AdminApiJsonSerialization
    {
        /// <summary>
        /// Serializer and deserializer for Dashboard / AdminAPI JSON payloads.
        /// </summary>
        public static readonly JsonSerializer Serializer = CreateSerializer();

        /// <summary>
        /// Json Serialization Settings for AdminAPI without $type tags.
        /// </summary>
        public static readonly JsonSerializerSettings UntypedSerializationSettings = CreateUntypedSerializationSettings();

        public delegate void ApplySettingsOverride(ref JsonSerialization.Options options);

        /// <summary>
        /// Applies Dashboard / AdminAPI serialization settings into the given <paramref name="settings"/>.
        /// </summary>
        public static void ApplySettings(JsonSerializerSettings settings)
        {
            ApplySettingsWithOverrides(settings, null);
        }

        /// <summary>
        /// Applies Dashboard / AdminAPI serialization settings into the given <paramref name="settings"/>.
        /// </summary>
        public static void ApplySettingsWithOverrides(JsonSerializerSettings settings, ApplySettingsOverride applyOverride)
        {
            JsonSerialization.Options options = new JsonSerialization.Options(JsonSerialization.Options.DefaultOptions)
            {
                Mode             = JsonSerializationMode.AdminApi,
                TypeNameHandling = TypeNameHandling.Objects,
            };
            applyOverride?.Invoke(ref options);
            PopulateAdminApiJsonSettings(settings, options);
        }

        static void PopulateAdminApiJsonSettings(JsonSerializerSettings settings, JsonSerialization.Options options)
        {
            settings.FloatParseHandling         = FloatParseHandling.Decimal;
            settings.DateParseHandling          = DateParseHandling.None;
            settings.TypeNameHandling           = options.TypeNameHandling;
            settings.MetadataPropertyHandling   = MetadataPropertyHandling.ReadAhead;
            settings.MaxDepth                   = 32;
            settings.SerializationBinder        = AdminApiJsonBinder.Instance;
            settings.ReferenceLoopHandling      = ReferenceLoopHandling.Error;
            settings.Converters.Add(new StringEnumConverter());
            if (options.TypeNameHandling == TypeNameHandling.Objects)
                settings.Converters.Add(new NoTypeDictionaryConverter());
            settings.ContractResolver = new MetaplayJsonContractResolver(options, PopulateAdminApiJsonSettings);
        }

        static JsonSerializer CreateSerializer()
        {
            JsonSerializerSettings settings = new JsonSerializerSettings();
            ApplySettings(settings);
            return JsonSerializer.Create(settings);
        }

        static JsonSerializerSettings CreateUntypedSerializationSettings()
        {
            JsonSerializerSettings settings = new JsonSerializerSettings();
            JsonSerialization.Options options = new JsonSerialization.Options(JsonSerialization.Options.DefaultOptions)
            {
                Mode                        = JsonSerializationMode.AdminApi,
                TypeNameHandling            = TypeNameHandling.None,
            };
            PopulateAdminApiJsonSettings(settings, options);
            return settings;
        }
    }

    class AdminApiJsonBinder : ISerializationBinder
    {
        public static readonly AdminApiJsonBinder Instance = new AdminApiJsonBinder();
        static Lazy<Dictionary<string, Type>> _nameToType = new Lazy<Dictionary<string, Type>>(GetValidDeserializationTargets);

        public void BindToName(Type serializedType, out string assemblyName, out string typeName)
        {
            // All serializations are allowed, but remove type tags of generated types. The types are just noise.
            if (serializedType.GetCustomAttribute<CompilerGeneratedAttribute>() != null)
            {
                assemblyName = null;
                typeName = "<anon>"; // We cannot remove the tags here, so replace with visualy short but searchable token.
                return;
            }

            // Normal types are identified by their name, but not assembly names.
            assemblyName = null;
            typeName = serializedType.FullName;
        }

        public Type BindToType(string assemblyName, string typeName)
        {
            // Assembly names are not allowed to catch any legacy type names early.
            if (assemblyName != null)
                throw new InvalidOperationException($"Type \"{typeName}, {assemblyName}\" is not allowed AdminAPI deserialization target. Assembly name (suffix after ,) is not allowed.");

            // Get type from precomputed legal targets dictionary.
            if (!_nameToType.Value.TryGetValue(typeName, out Type type))
                throw new InvalidOperationException($"Type {typeName} does not exist or is not allowed AdminAPI deserialization target. Deserialization target must either be statically defined concrete type, or a [MetaSerializable] type.");

            return type;
        }

        static Dictionary<string, Type> GetValidDeserializationTargets()
        {
            Dictionary<string, Type> types = new Dictionary<string, Type>();
            foreach (Type type in TypeScanner.GetAllTypes())
            {
                if (!IsAllowedDeserializationTarget(type))
                    continue;
                types.Add(type.FullName, type);
            }

            return types;
        }

        static bool IsAllowedDeserializationTarget(Type type)
        {
            // All MetaSerializable types are safe to initialize.
            if (MetaSerializerTypeRegistry.TryGetTypeSpec(type, out MetaSerializableType _))
                return true;

            return false;
        }
    }
}
