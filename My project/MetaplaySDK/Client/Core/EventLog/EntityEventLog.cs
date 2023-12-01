// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Analytics;
using Metaplay.Core.IO;
using Metaplay.Core.Json;
using Metaplay.Core.Model;
using Metaplay.Core.Serialization;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace Metaplay.Core.EventLog
{
    /// <summary>
    /// Entry type specific for an entity's event log.
    /// </summary>
    [MetaSerializable]
    public abstract class EntityEventLogEntry<TPayload, TPayloadDeserializationFailureSubstitute> : MetaEventLogEntry
        where TPayload : EntityEventBase
        where TPayloadDeserializationFailureSubstitute : TPayload, IEntityEventPayloadDeserializationFailureSubstitute, new()
    {
        /// <summary> Model's CurrentTime at the time of the event. </summary>
        [MetaMember(1)] public MetaTime ModelTime               { get; private set; }
        /// <summary> Schema version for the payload type. </summary>
        [MetaMember(3)] public int      PayloadSchemaVersion    { get; private set; }
        /// <summary> Event-specific payload. </summary>
        [MetaOnMemberDeserializationFailure("CreatePayloadDeserializationFailureSubstitute")]
        [MetaMember(2)] public TPayload Payload                 { get; private set; }

        protected EntityEventLogEntry(){ }
        public EntityEventLogEntry(BaseParams baseParams, MetaTime modelTime, int payloadSchemaVersion, TPayload payload) : base(baseParams)
        {
            ModelTime               = modelTime;
            PayloadSchemaVersion    = payloadSchemaVersion;
            Payload                 = payload;
        }

        public static TPayloadDeserializationFailureSubstitute CreatePayloadDeserializationFailureSubstitute(MetaMemberDeserializationFailureParams failureParams)
        {
            TPayloadDeserializationFailureSubstitute substitute = new TPayloadDeserializationFailureSubstitute();
            substitute.Initialize(failureParams);
            return substitute;
        }
    }

    public interface IEntityEventPayloadDeserializationFailureSubstitute
    {
        void Initialize(MetaMemberDeserializationFailureParams failureParams);
    }

    /// <summary>
    /// Base class for entity-specific analytics events, both Metaplay core and
    /// game-specific event types.
    /// </summary>
    [MetaAllowNoSerializedMembers]
    [MetaSerializable]
    public abstract class EntityEventBase : AnalyticsEventBase
    {
        /// <summary> Human-readable description of the event, shown in the dashboard. </summary>
        [JsonIgnore]
        public abstract string EventDescription { get; }

        [JsonProperty("eventDescription")]
        [IncludeOnlyInJsonSerializationMode(JsonSerializationMode.AdminApi)]
        public string EventDescriptionErrorWrapper
        {
            get
            {
                try
                {
                    return EventDescription;
                }
                catch (Exception ex)
                {
                    return $"Failed to get description string: {ex.Message}";
                }
            }
        }
    }

    [MetaSerializable]
    public abstract class EntityEventLog<TPayload, TPayloadDeserializationFailureSubstitute, TEntry> : MetaEventLog<TEntry>
        where TPayload : EntityEventBase
        where TPayloadDeserializationFailureSubstitute : TPayload, IEntityEventPayloadDeserializationFailureSubstitute, new()
        where TEntry : EntityEventLogEntry<TPayload, TPayloadDeserializationFailureSubstitute>
    {
    }

    [MetaSerializable]
    public class EntityEventDeserializationFailureInfo
    {
        const int MaxEventPayloadBytesRetained = 10 * 1024; // Limit number of bytes stored in the substitute, to avoid unbounded bloating in hypothetical case of recurring deserialization failure of substitute itself

        [MetaMember(1)] public int              EventPayloadBytesLength     { get; private set; }
        [MetaMember(2)] public byte[]           EventPayloadBytesTruncated  { get; private set; }
        [MetaMember(8)] public string           EventPayloadTypeName        { get; private set; } = null;
        [MetaMember(3)] public string           ExceptionType               { get; private set; }
        [MetaMember(4)] public string           ExceptionMessage            { get; private set; }
        [MetaMember(5)] public int?             UnknownEventTypeCode        { get; private set; }
        [MetaMember(6)] public int?             UnexpectedWireDataTypeValue { get; private set; }
        [MetaMember(7)] public string           UnexpectedWireDataTypeName  { get; private set; }

        [IgnoreDataMember] public string DescriptionForEvent => $"{ExceptionType} occurred while deserializing event {EventPayloadTypeName}: {ExceptionMessage}";

        EntityEventDeserializationFailureInfo(){ }
        public EntityEventDeserializationFailureInfo(MetaMemberDeserializationFailureParams failureParams)
        {
            EventPayloadBytesLength     = failureParams.MemberPayload.Length;
            EventPayloadBytesTruncated  = failureParams.MemberPayload.Take(MaxEventPayloadBytesRetained).ToArray();
            EventPayloadTypeName        = PeekEventPayloadTypeName(EventPayloadBytesTruncated);

            ExceptionType               = failureParams.Exception?.GetType().Name;
            ExceptionMessage            = failureParams.Exception?.Message;

            UnknownEventTypeCode        = (failureParams.Exception as MetaUnknownDerivedTypeDeserializationException)?.EncounteredTypeCode;

            WireDataType? unexpectedWireDataType = (failureParams.Exception as MetaWireDataTypeMismatchDeserializationException)?.EncounteredWireDataType;
            UnexpectedWireDataTypeValue = (int?)unexpectedWireDataType;
            UnexpectedWireDataTypeName  = unexpectedWireDataType?.ToString();
        }

        [MetaOnDeserialized]
        void EnsureEventPayloadTypeNameIsSet()
        {
            if (EventPayloadTypeName == null)
                EventPayloadTypeName = PeekEventPayloadTypeName(EventPayloadBytesTruncated);
        }

        static string PeekEventPayloadTypeName(byte[] eventPayloadBytesTruncated)
        {
            try
            {
                int typeCode;
                using (IOReader reader = new IOReader(eventPayloadBytesTruncated))
                    typeCode = reader.ReadVarInt();

                MetaSerializableType baseTypeSpec = MetaSerializerTypeRegistry.GetTypeSpec(typeof(AnalyticsEventBase));
                Type concreteType = baseTypeSpec.DerivedTypes[typeCode];
                return concreteType.Name;
            }
            catch
            {
                return "<unknown>";
            }
        }
    }
}
