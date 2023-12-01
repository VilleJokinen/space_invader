// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using Newtonsoft.Json;
using System;

namespace Metaplay.Server.EntityArchive
{
    [MetaSerializable]
    [JsonConverter(typeof(ExportedEntityConverter))]
    public struct ExportedEntity
    {
        [MetaMember(1)] public int SchemaVersion;
        [MetaMember(2)] public string Payload;

        public static ExportedEntity Create(int schemaVersion, byte[] payloadData)
        {
            return new ExportedEntity() { SchemaVersion = schemaVersion, Payload = Convert.ToBase64String(payloadData) };
        }
    }

    public class ExportedEntityConverter : JsonConverter
    {
        public override bool CanConvert(Type type) => type == typeof(ExportedEntity);
        public override bool CanWrite => false;

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.String)
            {
                // Allow deserializing payload from string directly, with SchemaVersion not set (backwards compatibility)
                return new ExportedEntity() { Payload = (string)reader.Value };
            }
            else if (reader.TokenType == JsonToken.StartObject)
            {
                existingValue = existingValue ?? serializer.ContractResolver.ResolveContract(objectType).DefaultCreator();
                serializer.Populate(reader, existingValue);
                return existingValue;

            }
            throw new JsonSerializationException();
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }
    }

    public static partial class EntityArchiveUtils
    {
        /// <summary>
        /// Current version of the EntityArchive (json) format. Version history:
        /// 1. Initial format.
        /// 2. Support for importing entities with older schemaVersions.
        ///     - Added "format version" which contains this value.
        ///     - Changed entity payload to `ExportedEntity` from plain string.
        /// </summary>
        public const int EntityArchiveFormatVersion = 2;
    }
}

