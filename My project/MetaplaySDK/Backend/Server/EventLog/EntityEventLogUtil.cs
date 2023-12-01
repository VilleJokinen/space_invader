// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud;
using Metaplay.Cloud.Persistence;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.EventLog;
using Metaplay.Core.IO;
using Metaplay.Core.Math;
using Metaplay.Core.Memory;
using Metaplay.Core.Model;
using Metaplay.Core.Serialization;
using Metaplay.Server.Database;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;
using static System.FormattableString;

namespace Metaplay.Server.EventLog
{
    public class EntityEventLogOptionsAttribute : RuntimeOptionsAttribute
    {
        public EntityEventLogOptionsAttribute(string entityTypeName)
            : base(
                  sectionName: $"EventLogFor{entityTypeName}",
                  isStatic: false,
                  sectionDescription: $"Configuration options for the storage of `{entityTypeName}` entity event logs.")
        {
        }
    }

    public abstract class EntityEventLogOptionsBase : RuntimeOptionsBase
    {
        /// <summary>
        /// Desired retention duration for persisted events. A
        /// persisted segment becomes eligible for deletion when all
        /// of its entries are older than this, assuming there are
        /// more than <see cref="MinPersistedSegmentsToRetain"/>
        /// persisted segments for the entity. Also, a persisted
        /// segment may be deleted even if its entries are younger
        /// than this, if there are more than <see cref="MaxPersistedSegmentsToRetain"/>
        /// persisted segments for the entity.
        /// </summary>
        /// <remarks>
        /// A segment is not guaranteed to be deleted immediately as
        /// soon as it reaches this age. Segment deletion is only
        /// considered when performing certain event log operations
        /// (namely, the flushing of new segments to the database).
        /// </remarks>
        [MetaDescription("The minimum amount of time that the event logs are persisted before automatic deletion.")]
        public TimeSpan RetentionDuration { get; private set; } = TimeSpan.FromDays(2 * 7);

        /// <summary>
        /// Desired number of entries to put in each of the separately-
        /// persisted segments. At least
        /// NumEntriesPerPersistedSegment*MinPersistedSegmentsToRetain
        /// entries will be kept available.
        /// </summary>
        /// <remarks>
        /// Existing segments should not be assumed to have any specific
        /// number of entries, as this configuration can be changed.
        /// </remarks>
        [MetaDescription("The number of event logs to persist in each segment.")]
        public int NumEntriesPerPersistedSegment   { get; private set; } = 50;

        /// <summary>
        /// Minimum number of persisted segments to retain per entity.
        /// At least this many segments are retained, even if older
        /// than <see cref="RetentionDuration"/>.
        /// </summary>
        [MetaDescription($"The minimum number of segments to retain per entity.")]
        public int MinPersistedSegmentsToRetain { get; private set; } = 20;

        /// <summary>
        /// Maximum number of persisted segments to retain per entity.
        /// At most this many segments are retained, even if younger
        /// than <see cref="RetentionDuration"/>. Oldest segments are
        /// deleted first.
        /// </summary>
        [MetaDescription($"The maximum number of segments to retain per entity.")]
        public int MaxPersistedSegmentsToRetain { get; private set; } = 1000;

        /// <summary>
        /// Maximum number of persisted segments to remove in one
        /// removal pass. This limit exists to avoid latency spikes in
        /// the entity actor: for example, if a player returns to the
        /// game after a long time, there might be lots of old segments
        /// to remove, but we don't want to remove all at once. Instead,
        /// at most this many segments will be removed each time a new
        /// segment is flushed to the database.
        /// </summary>
        /// <remarks>
        /// This must be at least 2 to guarantee that segment removal
        /// will eventually catch up. Segment removal is done only when
        /// a new segment is flushed to the database, so if this is 2,
        /// each flush-and-remove step will decrease the number of
        /// persisted segments by at most 1.
        /// </remarks>
        [MetaDescription("The maximum number of segments to remove from the database in one removal pass.")]
        public int MaxPersistedSegmentsToRemoveAtOnce { get; private set; } = 5;

        public override Task OnLoadedAsync()
        {
            if (RetentionDuration < TimeSpan.Zero)
                throw new InvalidOperationException($"{nameof(RetentionDuration)} must be non-negative (is {RetentionDuration})");

            if (NumEntriesPerPersistedSegment <= 0)
                throw new InvalidOperationException($"{nameof(NumEntriesPerPersistedSegment)} must be positive (is {NumEntriesPerPersistedSegment})");

            if (MinPersistedSegmentsToRetain < 0)
                throw new InvalidOperationException($"{nameof(MinPersistedSegmentsToRetain)} must be non-negative (is {MinPersistedSegmentsToRetain})");
            if (MaxPersistedSegmentsToRetain < 0)
                throw new InvalidOperationException($"{nameof(MaxPersistedSegmentsToRetain)} must be non-negative (is {MaxPersistedSegmentsToRetain})");
            if (MinPersistedSegmentsToRetain > MaxPersistedSegmentsToRetain)
                throw new InvalidOperationException($"{nameof(MinPersistedSegmentsToRetain)} must be less than or equal to {nameof(MaxPersistedSegmentsToRetain)} ({MinPersistedSegmentsToRetain} vs {MaxPersistedSegmentsToRetain})");

            if (MaxPersistedSegmentsToRemoveAtOnce < 2)
                throw new InvalidOperationException($"{nameof(MaxPersistedSegmentsToRemoveAtOnce)} must at least 2 (is {MaxPersistedSegmentsToRemoveAtOnce})");

            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Persisted payload for an event log segment.
    /// </summary>
    [MetaSerializable]
    public abstract class MetaEventLogSegmentPayload<TEntry> where TEntry : MetaEventLogEntry
    {
        [MetaMember(1)] public List<TEntry> Entries;
    }

    public static class PersistedEntityEventLogSegmentUtil
    {
        public static string CreateGlobalId(EntityId ownerId, int segmentSequentialId)  => Invariant($"{ownerId}/{segmentSequentialId}");
        public static string GetPartitionKey(EntityId ownerId)                          => ownerId.ToString();
    }

    /// <summary>
    /// A cursor for addressing an event log entry, used when scanning the event log.
    /// This is represented in a manner that allows directly addressing the relevant
    /// database-stored segment, without needing to do persistent external bookkeeping
    /// about which segment contains which entry ids.
    /// </summary>
    [MetaSerializable]
    public class EntityEventLogCursor
    {
        /// <summary>
        /// The segment into which this cursor points.
        /// </summary>
        [MetaMember(1)] public int SegmentId                { get; private set; }
        /// <summary>
        /// The position within the segment this cursor points to.
        /// This index can be equal to the number of entries in the segment,
        /// which should be understood as pointing to the end of the segment.
        /// </summary>
        [MetaMember(2)] public int EntryIndexWithinSegment  { get; private set; }

        EntityEventLogCursor() { }
        public EntityEventLogCursor(int segmentId, int entryIndexWithinSegment)
        {
            SegmentId = segmentId;
            EntryIndexWithinSegment = entryIndexWithinSegment;
        }
    }

    [Index(nameof(OwnerId))]
    public abstract class PersistedEntityEventLogSegment : IPersistedItem
    {
        /// <summary> Id that contains both the owner entity id and the segment sequential id. </summary>
        [Key]
        [Required]
        [MaxLength(128)]
        [Column(TypeName = "varchar(128)")]
        public string   GlobalId            { get; set; }

        /// <summary> Id of the entity that owns the event log. </summary>
        /// <remarks> This is also used as the partition key, so segments get stored on the same shard as the entity itself. </remarks>
        [PartitionKey]
        [Required]
        [MaxLength(64)]
        [Column(TypeName = "varchar(64)")]
        public string   OwnerId             { get; set; }

        [Required]
        public int      SegmentSequentialId { get; set; }

        /// <summary>
        /// Serialized MetaEventLogSegmentPayload<TEntry>-implementing object,
        /// serialized via its concrete static type.
        /// Furthermore, the serialized blob is compressed (and header-prefixed)
        /// with <see cref="CompressPayload"/>, except in old non-compressed
        /// legacy items. See also <see cref="UncompressPayload"/>.
        /// </summary>
        [Required]
        public byte[]   Payload             { get; set; }

        [Required]
        [Column(TypeName = "DateTime")]
        public DateTime FirstEntryTimestamp { get; set; } // \note based on CollectedAt

        [Required]
        [Column(TypeName = "DateTime")]
        public DateTime LastEntryTimestamp  { get; set; } // \note based on CollectedAt

        [Required]
        [Column(TypeName = "DateTime")]
        public DateTime CreatedAt           { get; set; }

        const byte PayloadHeaderMagicPrefixByte = 255; // Arbitrary byte different from (byte)WireDataType.NullableStruct

        public static byte[] CompressPayload(byte[] uncompressed)
        {
            // Compress, and add header prefix.

            using (FlatIOBuffer buffer = new FlatIOBuffer())
            {
                using (IOWriter writer = new IOWriter(buffer))
                {
                    writer.WriteByte(PayloadHeaderMagicPrefixByte);

                    writer.WriteVarInt(1); // schema version
                    writer.WriteVarInt((int)CompressionAlgorithm.Deflate);

                    byte[] compressed = CompressUtil.DeflateCompress(uncompressed);
                    writer.WriteBytes(compressed, 0, compressed.Length);
                }

                return buffer.ToArray();
            }
        }

        public byte[] UncompressPayload()
        {
            // Check header and decompress (unless header is missing because it's legacy).

            if (Payload[0] == (byte)WireDataType.NullableStruct)
            {
                // Payload is from before schema version was added. Payload is the plain serialized data.
                return Payload;
            }
            else if (Payload[0] == PayloadHeaderMagicPrefixByte)
            {
                using (IOReader reader = new IOReader(Payload, offset: 1, size: Payload.Length - 1))
                {
                    int schemaVersion = reader.ReadVarInt();
                    if (schemaVersion == 1)
                    {
                        CompressionAlgorithm compressionAlgorithm = (CompressionAlgorithm)reader.ReadVarInt();

                        // \todo [nuutti] Support offset in CompressUtil.Decompress to avoid ad hoc handling here?
                        if (compressionAlgorithm == CompressionAlgorithm.Deflate)
                            return CompressUtil.DeflateDecompress(Payload, offset: reader.Offset);
                        else
                            throw new InvalidOperationException($"Invalid compression algorithm {compressionAlgorithm}");
                    }
                    else
                        throw new InvalidOperationException($"Invalid schema version: {schemaVersion}");
                }
            }
            else
                throw new InvalidOperationException($"Expected {nameof(Payload)} to start with either byte {(byte)WireDataType.NullableStruct} ({nameof(WireDataType)}.{nameof(WireDataType.NullableStruct)}) or {PayloadHeaderMagicPrefixByte}, but it starts with {Payload[0]}");
        }
    }

    public static class EntityEventLogUtil<TEntry, TSegmentPayload, TPersistedSegment, TScanResponse>
        where TEntry : MetaEventLogEntry
        where TSegmentPayload : MetaEventLogSegmentPayload<TEntry>, new()
        where TPersistedSegment : PersistedEntityEventLogSegment, new()
        where TScanResponse : EntityEventLogScanResponse<TEntry>, new()
    {
        public static void AddEntry(MetaEventLog<TEntry> eventLog, EntityEventLogOptionsBase config, MetaTime collectedAt, MetaUInt128 uniqueId, Func<MetaEventLogEntry.BaseParams, TEntry> createEntry)
        {
            TEntry entry = createEntry(new MetaEventLogEntry.BaseParams(eventLog.RunningEntryId, collectedAt, uniqueId));

            // Add entry.
            MetaDebug.Assert(entry.SequentialId == eventLog.RunningEntryId, "SequentialId not set properly");
            eventLog.LatestSegmentEntries.Add(entry);
            eventLog.RunningEntryId++;

            // If configured limit is reached, move LatestSegmentEntries to PendingSegments.
            // It'll get flushed to database later.
            //
            // Note that *all* the entries of LatestSegmentEntries are copied into one
            // segment, instead of taking exactly NumEntriesPerPersistedSegment entries
            // (it might mismatch in case the NumEntriesPerPersistedSegment config was
            // changed).
            // This is so that existing `EntityEventLogCursor`s remain valid: an
            // existing cursor might point into what was earlier in LatestSegmentEntries,
            // and in order for the cursor addressing to remain correct, the number of
            // entries per segment must not decrease (which it would if only a part of
            // the entries in LatestSegmentEntries were taken into a segment).
            if (eventLog.LatestSegmentEntries.Count >= config.NumEntriesPerPersistedSegment)
            {
                List<TEntry> entriesCopy = new List<TEntry>(eventLog.LatestSegmentEntries);
                eventLog.PendingSegments.Add(new MetaEventLog<TEntry>.PendingSegment(entriesCopy));
                eventLog.LatestSegmentEntries.Clear();
                eventLog.RunningSegmentId++;
            }
        }

        /// <summary>
        /// Whether the log contains segments waiting to be flushed to the database.
        /// </summary>
        public static bool CanFlush(MetaEventLog<TEntry> eventLog, EntityEventLogOptionsBase config)
        {
            return eventLog.PendingSegments.Count > 0;
        }

        /// <summary>
        /// Helper that combines flushing of a segment from the event log,
        /// and removal of old segments according to the retention limits in <paramref name="config"/>.
        /// You'll likely want to call this when <see cref="CanFlush"/> returns true.
        /// </summary>
        public static async Task TryFlushAndCullSegmentsAsync(MetaEventLog<TEntry> eventLog, EntityEventLogOptionsBase config, EntityId ownerId, int logicVersion, IMetaLogger log, Func<Task> persistModelAsync)
        {
            await TryFlushSegmentsAsync(eventLog, config, ownerId, logicVersion, log);
            await TryRemoveOldSegmentsAsync(eventLog, config, ownerId, log, persistModelAsync);
        }

        /// <summary>
        /// Flush pending segments (if any) from the model's eventLog into separate database items.
        /// </summary>
        public static async Task TryFlushSegmentsAsync(MetaEventLog<TEntry> eventLog, EntityEventLogOptionsBase config, EntityId ownerId, int logicVersion, IMetaLogger log)
        {
            while (eventLog.PendingSegments.Count > 0)
            {
                // Pop the oldest pending segment, and persist it.

                List<TEntry>    segmentEntries      = eventLog.PendingSegments[0].Entries;
                int             segmentSequentialId = eventLog.RunningSegmentId - eventLog.PendingSegments.Count;
                eventLog.PendingSegments.RemoveAt(0);

                await PersistSegmentAsync(segmentSequentialId, segmentEntries, ownerId, logicVersion, log).ConfigureAwait(false);
            }
        }

        async static Task PersistSegmentAsync(int segmentSequentialId, List<TEntry> segmentEntries, EntityId ownerId, int logicVersion, IMetaLogger log)
        {
            TSegmentPayload payload = new TSegmentPayload()
            {
                Entries = segmentEntries
            };

            TPersistedSegment persistedSegment = new TPersistedSegment
            {
                GlobalId            = PersistedEntityEventLogSegmentUtil.CreateGlobalId(ownerId, segmentSequentialId),
                OwnerId             = ownerId.ToString(),
                SegmentSequentialId = segmentSequentialId,
                Payload             = PersistedEntityEventLogSegment.CompressPayload(MetaSerialization.SerializeTagged(payload, MetaSerializationFlags.Persisted, logicVersion)),
                FirstEntryTimestamp = segmentEntries.First().CollectedAt.ToDateTime(),
                LastEntryTimestamp  = segmentEntries.Last().CollectedAt.ToDateTime(),
                CreatedAt           = DateTime.UtcNow,
            };

            // Upsert.
            //
            // A segment item with the same id may exist, in case a previous incarnation of the entity crashed before persisting.
            // In that case, the existing segment is irrelevant (as the entity model has authority), and shall be overwritten.
            //
            // Also for this reason, the entity model does not need to be persisted right away.

            MetaDatabase db = MetaDatabase.Get();
            await db.InsertOrUpdateAsync(persistedSegment).ConfigureAwait(false);
        }

        /// <summary>
        /// Remove old persisted segments if allowed by the retention
        /// limits in <paramref name="config"/>
        /// </summary>
        /// <param name="persistModelAsync">Persist the model that contains eventLog.</param>
        public static async Task TryRemoveOldSegmentsAsync(MetaEventLog<TEntry> eventLog, EntityEventLogOptionsBase config, EntityId ownerId, IMetaLogger log, Func<Task> persistModelAsync)
        {
            DateTime retentionCutoffTime = DateTime.UtcNow - config.RetentionDuration;

            // Figure out how many segments we can remove while respecting
            // the retention limits set by `config`.
            //
            // This next loop does not modify eventLog, and does not remove
            // the persisted segments; that is done after this loop. This loop
            // only increments numSegmentsToRemove to an appropriate value.

            int numSegmentsToRemove = 0;
            while (true)
            {
                // numSegmentsToRemove currently holds a "safe" value, i.e. a number of
                // segments that we could remove while still respecting the configured
                // retention limits; though not yet necessarily the largest such number.
                // Now, we set canRemoveOneMoreSegment to whether numSegmentsToRemove
                // can be incremented and still have it respect the configured limits.

                bool canRemoveOneMoreSegment;

                if (numSegmentsToRemove >= config.MaxPersistedSegmentsToRemoveAtOnce)
                {
                    // Reached the limit of how many should be removed at once.
                    // Cannot remove more.
                    canRemoveOneMoreSegment = false;
                }
                else if (eventLog.NumAvailablePersistedSegments - numSegmentsToRemove <= config.MinPersistedSegmentsToRetain)
                {
                    // The tentative number of remaining segments after removal
                    // is already at or below minimum.
                    // Cannot remove more.
                    canRemoveOneMoreSegment = false;
                }
                else if (eventLog.NumAvailablePersistedSegments - numSegmentsToRemove > config.MaxPersistedSegmentsToRetain)
                {
                    // The tentative number of remaining segments after removal
                    // is *above* (but not exactly at) maximum.
                    // *Can* remove at least one more.
                    canRemoveOneMoreSegment = true;
                }
                else
                {
                    // Check time-based retention limit based on LastEntryTimestamp of the next segment.

                    // speculatedRemoveSegmentId is the id of the segment that would get removed
                    // if we were to increment numSegmentsToRemove, but would not get removed with
                    // the current value of numSegmentsToRemove.
                    int     speculatedRemoveSegmentId           = eventLog.OldestAvailableSegmentId + numSegmentsToRemove;
                    string  speculatedRemoveSegmentGlobalId     = PersistedEntityEventLogSegmentUtil.CreateGlobalId(ownerId, speculatedRemoveSegmentId);

                    // From database, get just the LastEntryTimestamp of the segment.
                    // In particular, omit the Payload.
                    DateTime? segmentLastEntryTimestamp =
                        await MetaDatabase.Get().TryGetEventLogSegmentLastEntryTimestamp<TPersistedSegment>(
                            primaryKey:     speculatedRemoveSegmentGlobalId,
                            partitionKey:   PersistedEntityEventLogSegmentUtil.GetPartitionKey(ownerId));

                    if (segmentLastEntryTimestamp.HasValue)
                    {
                        // According to time-based retention, allow removing the segment if
                        // its last (i.e. youngest) entry is older than the retention duration.
                        canRemoveOneMoreSegment = segmentLastEntryTimestamp.Value < retentionCutoffTime;
                    }
                    else
                    {
                        // Segment is missing from database? Shouldn't happen. But if it does, allow "removing" it.
                        log.Warning($"Segment {{PersistedSegmentId}} not found when trying to get its {nameof(PersistedEntityEventLogSegment.LastEntryTimestamp)}, this shouldn't happen", speculatedRemoveSegmentGlobalId);
                        canRemoveOneMoreSegment = true;
                    }
                }

                // Either we can still keep going,
                // or we've found the maximum appropriate value for numSegmentsToRemove.

                if (canRemoveOneMoreSegment)
                    numSegmentsToRemove++;
                else
                    break;
            }

            // Omit the rest if we're not gonna remove anything.
            // In particular, don't do persistModelAsync().
            if (numSegmentsToRemove == 0)
                return;

            // Perform the segment removal based on the numSegmentsToRemove we just determined.

            int removeSegmentIdsStart   = eventLog.OldestAvailableSegmentId;
            int removeSegmentIdsEnd     = eventLog.OldestAvailableSegmentId + numSegmentsToRemove;

            // Update eventLog's bookkeeping to reflect the removal of the segments.
            eventLog.OldestAvailableSegmentId = removeSegmentIdsEnd;

            // Persist the model.
            // Model is persisted before the actual removal of the persisted segments,
            // in order to ensure that the model's bookkeeping never indicates segments
            // that no longer exist.
            await persistModelAsync();

            // Finally, remove the actual persisted segments.
            MetaDatabase db = MetaDatabase.Get();
            for (int segmentSequentialId = removeSegmentIdsStart; segmentSequentialId < removeSegmentIdsEnd; segmentSequentialId++)
            {
                string persistedSegmentId = PersistedEntityEventLogSegmentUtil.CreateGlobalId(ownerId, segmentSequentialId);
                bool removeOk = await db.RemoveAsync<TPersistedSegment>(
                    primaryKey:     persistedSegmentId,
                    partitionKey:   PersistedEntityEventLogSegmentUtil.GetPartitionKey(ownerId)
                    ).ConfigureAwait(false);

                // Tolerate failure of removal, even though it shouldn't happen.
                if (!removeOk)
                    log.Warning("Removal of {TPersistedSegment} failed, this shouldn't happen; id={PersistedSegmentId}", typeof(TPersistedSegment).Name, persistedSegmentId);
            }
        }

        /// <summary>
        /// Remove all segments associated with the given entity, regardless of whether
        /// eventLog's bookkeeping reflects their existence.
        /// This is to be used when deleting an entity.
        /// </summary>
        /// <param name="persistModelAsync">Persist the model that contains eventLog.</param>
        public static async Task RemoveAllSegmentsAsync(MetaEventLog<TEntry> eventLog, EntityId ownerId, Func<Task> persistModelAsync)
        {
            if (eventLog.NumAvailablePersistedSegments > 0)
            {
                // Update eventLog's bookkeeping to reflect the removal of the segments.
                eventLog.ForgetAllPersistedSegments();

                // Persist the model.
                // Model is persisted before the actual removal of the persisted segments,
                // in order to ensure that the model's bookkeeping never indicates segments
                // that no longer exist.
                await persistModelAsync();
            }

            // Finally, remove all owned segments from the database.
            // This is done regardless of whether the eventLog's bookkeeping
            // acknowledged the segments; persisted segments can exist without
            // corresponding bookkeeping, since the entity model (which is where
            // eventLog exists) and the segments are not persisted atomically
            // with each other.
            await MetaDatabase.Get(QueryPriority.Low).RemoveAllEventLogSegmentsOfEntityAsync<TPersistedSegment>(ownerId);
        }

        /// <summary>
        /// Scan log entries according to given <see cref="EntityEventLogScanRequest"/>.
        /// </summary>
        public static async Task<TScanResponse> ScanEntriesAsync(MetaEventLog<TEntry> eventLog, EntityId ownerId, IGameConfigDataResolver gameConfigResolver, int logicVersion, EntityEventLogScanRequest request)
        {
            if (request.StartCursor.SegmentId > eventLog.RunningSegmentId)
                throw new ArgumentOutOfRangeException(nameof(request), $"Start cursor is at segment {request.StartCursor.SegmentId}, but the latest segment has id {eventLog.RunningSegmentId}");
            if (request.StartCursor.EntryIndexWithinSegment < 0)
                throw new ArgumentOutOfRangeException(nameof(request), $"Start cursor's {nameof(request.StartCursor.EntryIndexWithinSegment)} cannot be negative (is {request.StartCursor.EntryIndexWithinSegment})");
            if (request.NumEntries < 0)
                throw new ArgumentOutOfRangeException(nameof(request), $"Requested number of entries cannot be negative (is {request.NumEntries})");

            // persistedSegmentIdsEnd is one past the id of the last persisted segment.
            // Equivalently, the first segment in PendingSegments, or equal to
            // RunningSegmentId if PendingSegments is empty.
            int persistedSegmentIdsEnd = eventLog.RunningSegmentId - eventLog.PendingSegments.Count;

            // Iterators for the segment id and entry index.
            // Start at the given cursor, then updated in the code below.
            int currentSegmentId                = request.StartCursor.SegmentId;
            int currentEntryIndexWithinSegment  = request.StartCursor.EntryIndexWithinSegment;

            // Clamp the iterators if they point at earlier than the oldest available segment.
            if (currentSegmentId < eventLog.OldestAvailableSegmentId)
            {
                currentSegmentId                = eventLog.OldestAvailableSegmentId;
                currentEntryIndexWithinSegment  = 0;
            }

            List<TEntry> resultEntries = new List<TEntry>();

            // Loop over the segments, starting from the current value of currentSegmentId,
            // towards newer segments, continuing until either the requested number of entries
            // is reached, or the end of the segments is reached.
            // After this loop:
            // - resultEntries contains all the entries to return
            // - currentSegmentId and currentEntryIndexWithinSegment form the continuation cursor

            while (true)
            {
                // Fetch all the entries from the segment identified by currentSegmentId.
                // The segment comes from one of the following places:
                // - database-persisted segment (when oldestAvailableSegmentId <= currentSegmentId < persistedSegmentIdsEnd)
                // - eventLog.PendingSegments (when persistedSegmentIdsEnd <= currentSegmentId < eventLog.RunningSegmentId)
                // - the latest, potentially still incomplete segment (when currentSegmentId == eventLog.RunningSegmentId)
                // That is, database-persisted segments come before (i.e. are older) than pending segments,
                // and pending segments come before the latest segment.
                // Note the exclusive-end id ranges for database-persisted and pending segments;
                // it is possible that there are none of either.

                List<TEntry> segmentEntries;

                if (currentSegmentId < persistedSegmentIdsEnd)
                {
                    // The segment is database-persisted, fetch it.
                    segmentEntries = await ReadPersistedSegmentEntriesAsync(ownerId, gameConfigResolver, logicVersion, currentSegmentId).ConfigureAwait(false);
                }
                else if (currentSegmentId < eventLog.RunningSegmentId)
                {
                    // Segment is not database-persisted, but is older than the latest segment. It's a pending segment.
                    segmentEntries = eventLog.PendingSegments[currentSegmentId - persistedSegmentIdsEnd].Entries;
                }
                else
                {
                    // Segment is neither database-persisted nor a pending segment. It's the latest segment.
                    segmentEntries = eventLog.LatestSegmentEntries;
                }

                // From current segment, starting at the current entry index, get as many entries as are
                // still needed to fulfill the request, or if there's not enough, then get all.
                //
                // numEntriesStillWanted might be 0, that's ok and then we'll just take 0 entries.
                //
                // currentEntryIndexWithinSegment might be equal to the number of entries in this segment
                // (in case the start cursor was from a previous scan which left the index there) and
                // that's also ok, we'll just take 0 entries from this segment.

                int                 numEntriesStillWanted           = request.NumEntries - resultEntries.Count;
                IEnumerable<TEntry> resultEntriesFromThisSegment    = segmentEntries
                                                                      .Skip(currentEntryIndexWithinSegment)
                                                                      .Take(numEntriesStillWanted);

                resultEntries.AddRange(resultEntriesFromThisSegment);

                // Advance index within current segment according to how many entries we took.
                currentEntryIndexWithinSegment += resultEntriesFromThisSegment.Count();

                if (resultEntries.Count >= request.NumEntries)
                {
                    MetaDebug.Assert(resultEntries.Count == request.NumEntries, Invariant($"Took more entries than wanted ({resultEntries.Count} vs {request.NumEntries})"));

                    // We've got the requested number of entries. Stop.
                    // \note We might *also* have reached the end of the current segment, in
                    //       which case we could bump the cursor to the next segment (unless
                    //       we're already at the last segment) so that the next scan would
                    //       start there. But there's no need, the above logic handles end-
                    //       of-segment index anyway.
                    break;
                }
                else if (currentSegmentId >= eventLog.RunningSegmentId)
                {
                    MetaDebug.Assert(currentSegmentId == eventLog.RunningSegmentId, Invariant($"Went past {nameof(eventLog.RunningSegmentId)} ({currentSegmentId} > {eventLog.RunningSegmentId}); shouldn't happen since there's a precondition check for start cursor"));

                    // Reached end of the last segment without getting the requested number of
                    // entries. Stop the scan, but don't increment currentSegmentId - the last
                    // segment might still get more entries, so a future scan using the current
                    // position as start cursor might still get entries from there.
                    break;
                }
                else
                {
                    // Reached end of a non-last segment, and don't yet have the requested
                    // number of entries. Keep going from the start of the next segment.
                    currentSegmentId++;
                    currentEntryIndexWithinSegment = 0;
                }
            }

            // Debug check: IDs must be consecutive
            for (int i = 1; i < resultEntries.Count; i++)
            {
                TEntry current  = resultEntries[i];
                TEntry previous = resultEntries[i-1];

                MetaDebug.Assert(current.SequentialId == previous.SequentialId+1, "Non-sequential event log entry IDs: {PreviousId} vs {CurrentId}", previous.SequentialId, current.SequentialId);
            }

            return new TScanResponse
            {
                Entries = MetaSerialization.ToMetaSerialized(resultEntries, MetaSerializationFlags.IncludeAll, logicVersion),

                // Continuation cursor is where this scan ended.
                ContinuationCursor = new EntityEventLogCursor(
                    segmentId: currentSegmentId,
                    entryIndexWithinSegment: currentEntryIndexWithinSegment),
            };
        }

        static async Task<List<TEntry>> ReadPersistedSegmentEntriesAsync(EntityId ownerId, IGameConfigDataResolver gameConfigResolver, int logicVersion, int segmentSequentialId)
        {
            MetaDatabase        db                  = MetaDatabase.Get();
            string              persistedSegmentId  = PersistedEntityEventLogSegmentUtil.CreateGlobalId(ownerId, segmentSequentialId);
            TPersistedSegment   persistedSegment    =
                await db.TryGetAsync<TPersistedSegment>(
                    primaryKey:     persistedSegmentId,
                    partitionKey:   PersistedEntityEventLogSegmentUtil.GetPartitionKey(ownerId)
                    ).ConfigureAwait(false);

            if (persistedSegment == null)
                throw new InvalidOperationException($"Getting {typeof(TPersistedSegment).Name} failed, id={persistedSegmentId}");

            TSegmentPayload payload = MetaSerialization.DeserializeTagged<TSegmentPayload>(persistedSegment.UncompressPayload(), MetaSerializationFlags.Persisted, gameConfigResolver, logicVersion);
            List<TEntry>    entries = payload.Entries;

            return entries;
        }

        static List<T> GetAndRemoveNFirst<T>(List<T> list, int count)
        {
            List<T> result = list.GetRange(0, count);
            list.RemoveRange(0, count);
            return result;
        }
    }
}
