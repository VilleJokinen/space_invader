// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Persistence;
using Metaplay.Cloud.Services.Geolocation;
using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Serialization;
using Metaplay.Core.Web3;
using Metaplay.Server.AdminApi.Controllers;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Net;
using static System.FormattableString;

namespace Metaplay.Server.AdminApi.AuditLog
{
    public class MetaplayAuditLogEventCodesCore
    {
        public const int Invalid = 1;
    }

    /// <summary>
    /// Audit log event as persisited in the database
    /// </summary>
    [Table("AuditLogEvents")]
    [Index(nameof(Source))]
    [Index(nameof(Target))]
    public class PersistedAuditLogEvent : IPersistedItem
    {
        [Key]
        [Required]
        [MaxLength(64)]
        [Column(TypeName = "varchar(64)")]
        public string               EventId                 { get; set; }   // EventID

        [Required]
        [MaxLength(256)]
        [Column(TypeName = "varchar(256)")]
        public string               Source                  { get; set; }   // Stores SourceType:SourceId

        [Required]
        [PartitionKey]
        [MaxLength(256)]
        [Column(TypeName = "varchar(256)")]
        public string               Target                  { get; set; }   // Stores TargetType:TargetId. NB: This is the partitionkey, so that player events live on the same shard as the player data

        [MaxLength(64)]
        [Column(TypeName = "varchar(64)")]
        public string               SourceIpAddress         { get; set; }   // IP address of the origin of this request

        [MaxLength(16)]
        [Column(TypeName = "varchar(16)")]
        public string               SourceCountryIsoCode    { get; set; }   // Country ISO code of the origin of this request

        [Required]
        public byte[]               CompressedPayload       { get; set; }   // Stores serialised type derived from AuditLogEventBase

        [Required]
        public CompressionAlgorithm CompressionAlgorithm    { get; set; }   // Compression algorithm for Payload

        public PersistedAuditLogEvent() { }
        public PersistedAuditLogEvent(EventId eventId, EventSource source, EventTarget target, IPAddress sourceIpAddress, EventPayloadBase auditLogEvent)
        {
            EventId = eventId.ToString();
            Source = source.ToString();
            Target = target.ToString();
            SourceIpAddress = sourceIpAddress != null ? sourceIpAddress.ToString() : null;
            PlayerLocation? playerLocation = sourceIpAddress != null ? Geolocation.Instance.TryGetPlayerLocation(sourceIpAddress) : null;
            SourceCountryIsoCode = playerLocation?.Country.IsoCode;
            byte[] uncompressedPayload = MetaSerialization.SerializeTagged(auditLogEvent, MetaSerializationFlags.Persisted, logicVersion: null);
            byte[] compressedPayload = CompressUtil.DeflateCompress(uncompressedPayload);
            if (compressedPayload.Length < uncompressedPayload.Length)
            {
                CompressedPayload = compressedPayload;
                CompressionAlgorithm = CompressionAlgorithm.Deflate;
            }
            else
            {
                CompressedPayload = uncompressedPayload;
                CompressionAlgorithm = CompressionAlgorithm.None;
            }
        }
    }

    /// <summary>
    /// In-memory representation of a PersistedAuditLogEvent
    /// </summary>
    public class AuditLogEvent
    {
        public MetaTime                                     CreatedAt           { get; private set; }
        public string                                       EventId             { get; private set; }
        [ExcludeFromGdprExport] public EventSource          Source              { get; private set; }
        [ExcludeFromGdprExport] public EventTarget          Target              { get; private set; }
        [ExcludeFromGdprExport] public string               SourceCountryIsoCode{ get; private set; }
        [ExcludeFromGdprExport] public string               SourceIpAddress     { get; private set; }
        [ExcludeFromGdprExport] public EventPayloadBase     Payload             { get; private set; }
        public List<string>                                 RelatedEventIds     => Payload.RelatedEventIds;
        public string                                       DisplayTitle        => Payload.EventTitle;
        public string                                       DisplayDescription  => Payload.EventDescriptionErrorWrapper;

        public AuditLogEvent() { }
        public AuditLogEvent(PersistedAuditLogEvent persistedAuditLogEvent)
        {
            CreatedAt = AuditLog.EventId.ExtractTime(persistedAuditLogEvent.EventId);
            EventId = persistedAuditLogEvent.EventId;
            Source = EventSource.FromPersistedAuditLogEvent(persistedAuditLogEvent);
            Target = EventTarget.FromPersistedAuditLogEvent(persistedAuditLogEvent);
            SourceCountryIsoCode = persistedAuditLogEvent.SourceCountryIsoCode;
            SourceIpAddress = persistedAuditLogEvent.SourceIpAddress;
            byte[] uncompressedPayloadData = CompressUtil.Decompress(persistedAuditLogEvent.CompressedPayload, persistedAuditLogEvent.CompressionAlgorithm);
            try
            {
                Payload = MetaSerialization.DeserializeTagged<EventPayloadBase>(uncompressedPayloadData, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);
            }
            catch (MetaSerializationException ex)
            {
                // Something went wrong in the deserialization - maybe the type changed? We'll replace
                // the payload with a special 'invalid' type rather than propagate the exception
                try
                {
                    string stringData = TaggedWireSerializer.ToString(uncompressedPayloadData);
                    Payload = new InvalidLogEvent(ex.Message, stringData);
                }
                catch
                {
                    Payload = new InvalidLogEvent(ex.Message, "Could not provide raw data.");
                }
            }
        }
    }

    /// <summary>
    /// Event Ids consist of a timestamp and a guid, combined together into a
    /// human-readable string. A set of Ids will sort into time order (with up
    /// to millisecond resolution)
    /// </summary>
    public class EventId
    {
        private string Value;
        private EventId(MetaTime time, string idString)
        {
            long ms = time.MillisecondsSinceEpoch;
            byte[] bytes = new byte[8];
            for (int i = 0; i < 8; ++i)
                bytes[i] = (byte)(ms >> (56 - 8 * i));
            Value = $"{Util.ToHexString(bytes)}-{idString}";
        }
        static public EventId FromTime(MetaTime time)
        {
            return new EventId(time, Guid.NewGuid().ToString());
        }
        static public EventId SearchStringFromTime(MetaTime time)
        {
            return new EventId(time, "");
        }
        public override string ToString() => Value;
        static public MetaTime ExtractTime(string eventId)
        {
            byte[] bytes = Util.ParseHexString(eventId.Substring(0, 16));
            long ms = 0;
            for (int i = 0; i < 8; ++i)
                ms |= (long)bytes[i] << (56 - 8 * i);
            return MetaTime.FromMillisecondsSinceEpoch(ms);
        }
    }

    /// <summary>
    /// Event sources are made up of two parts, type and id, separated by a
    /// colon. Currently there is only one source: the Admin API. It's type is
    /// signified by the type string "$AdminApi" and the ID part is the userId
    /// as given by the Admin Api - in a system with correctly configured
    /// authentication this will be the email address of the user.
    /// </summary>
    public class EventSource
    {
        public string SourceType    { get; }
        public string SourceId      { get; }

        private EventSource(string sourceType, string sourceId)
        {
            SourceType = sourceType;
            SourceId = sourceId;
        }
        public override string ToString() => $"{SourceType}:{SourceId}";

        public static EventSource FromAdminApi(string userId)
        {
            return new EventSource("$AdminApi", userId);
        }
        public static EventSource FromPersistedAuditLogEvent(PersistedAuditLogEvent persistedAuditLogEvent)
        {
            string[] sourceParts = persistedAuditLogEvent.Source.Split(':');
            return new EventSource(sourceParts[0], sourceParts[1]);
        }
    }

    /// <summary>
    /// Event targets are made up of two parts, type and id, separated by a
    /// colon. For entities, this is the same as the EntityId, eg: for a player
    /// the type is "Player" and the id is the alphanumeric part of the EntityId
    /// leading to a target of "Player:xxxxxxxxxx". For non-entity targets (eg:
    /// the game server) the type is the name of the system (but prefixed with a
    /// $ so that it never gets confused as an EntityId) and the id is a
    /// type-specific string.
    public class EventTarget
    {
        public string TargetType    { get; }
        public string TargetId      { get; }
        private EventTarget(string targetType, string targetId)
        {
            TargetType = targetType;
            TargetId = targetId;
        }
        public override string ToString() => $"{TargetType}:{TargetId}";

        public static EventTarget FromPersistedAuditLogEvent(PersistedAuditLogEvent persistedAuditLogEvent)
        {
            string[] targetParts = persistedAuditLogEvent.Target.Split(':');
            return new EventTarget(targetParts[0], targetParts[1]);
        }

        public static EventTarget FromEntityId(EntityId target)
        {
            (string targetKind, string targetValue) = target.GetKindValueStrings();
            return new EventTarget(targetKind,  targetValue);
        }
        public static EventTarget FromGameServer(string subsystemName)
        {
            return new EventTarget("$GameServer", subsystemName);
        }
        public static EventTarget FromBroadcast(int broadcastId)
        {
            return new EventTarget("$Broadcast", Invariant($"{broadcastId}"));
        }
        public static EventTarget FromNotification(int notificationId)
        {
            return new EventTarget("$Notification", Invariant($"{notificationId}"));
        }
        public static EventTarget FromExperiment(string experimentId)
        {
            return new EventTarget("$Experiment", Invariant($"{experimentId}"));
        }
        public static EventTarget FromGameConfig(MetaGuid configId)
        {
            return new EventTarget("$GameConfig", Invariant($"{configId}"));
        }
        public static EventTarget FromLocalization(MetaGuid localizationId)
        {
            return new EventTarget("$Localization", Invariant($"{localizationId}"));
        }
        public static EventTarget FromNftKey(NftKey nftKey)
        {
            return new EventTarget("$Nft", $"{nftKey.CollectionId}/{nftKey.TokenId}");
        }
        public static EventTarget FromNftCollectionId(NftCollectionId collectionId)
        {
            return new EventTarget("$NftCollection", collectionId.Value);
        }
    }

    /// <summary>
    /// Base class for a log event's payload. EventTitle should return a short, human-readable
    /// verb for the event while EventDescription should return a short description (one
    /// sentence or so) of the event
    /// </summary>
    [MetaSerializable]
    [MetaReservedMembers(100, 200)]
    public abstract class EventPayloadBase
    {
        [MetaMember(100)] public List<string> RelatedEventIds { get; private set; } = new List<string>();
        public EventPayloadBase() { }
        abstract public string EventTitle { get; }
        [JsonIgnore] abstract public string EventDescription { get; }

        [JsonProperty("eventDescription")]
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

        public void SetRelatedEventIds(List<AuditLog.EventId> eventIds)
        {
            RelatedEventIds = eventIds.Select(x => x.ToString()).ToList();
        }
    }

    /// <summary>
    /// The builder is used to encapsulate the payload and anything that is needed
    /// to make a target from the payload.
    /// </summary>
    public abstract class EventBuilder
    {
        public EventPayloadBase Payload { get; }
        public EventTarget Target { get; }
        protected EventBuilder(EventPayloadBase payload, EventTarget target)
        {
            Payload = payload;
            Target = target;
        }
    }

    /// <summary>
    /// Special log event that represents a failed payload deserialization
    /// </summary>
    [MetaSerializableDerived(MetaplayAuditLogEventCodesCore.Invalid)]
    public class InvalidLogEvent : EventPayloadBase
    {
        [MetaMember(1)] public string Reason { get; private set; }
        [MetaMember(2)] public string RawData { get; private set; }
        public InvalidLogEvent() { }
        public InvalidLogEvent(string reason, string rawData) { Reason = reason; RawData = rawData; }
        override public string EventTitle => "Invalid event";
        override public string EventDescription => $"Failed to deserialize payload (reason: '{Reason}').";
    }
}
