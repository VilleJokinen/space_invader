// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Math;
using Metaplay.Core.Model;
using System.Collections.Generic;

namespace Metaplay.Core.EventLog
{
    /// <summary>
    /// Base for entries in an event log, such as the player event log.
    /// </summary>
    [MetaSerializable]
    [MetaReservedMembers(100, 200)]
    public abstract class MetaEventLogEntry
    {
        /// <summary> Id of the entry, allocated sequentially among entries within an event log. </summary>
        [MetaMember(100)] public int            SequentialId  { get; private set; }
        /// <summary> Real time when the entry was added. </summary>
        [MetaMember(101)] public MetaTime       CollectedAt   { get; private set; }
        [MetaMember(102)] public MetaUInt128    UniqueId      { get; private set; }

        /// <summary>
        /// Struct wrapping the base constructor parameters, just to reduce boilerplate in subclass construction.
        /// </summary>
        public readonly struct BaseParams
        {
            public readonly int         SequentialId;
            public readonly MetaTime    CollectedAt;
            public readonly MetaUInt128 UniqueId;

            public BaseParams(int sequentialId, MetaTime collectedAt, MetaUInt128 uniqueId)
            {
                SequentialId = sequentialId;
                CollectedAt = collectedAt;
                UniqueId = uniqueId;
            }
        }

        public MetaEventLogEntry(){ }
        public MetaEventLogEntry(BaseParams baseParams)
        {
            SequentialId    = baseParams.SequentialId;
            CollectedAt     = baseParams.CollectedAt;
            UniqueId        = baseParams.UniqueId;
        }
    }

    /// <summary>
    /// Base class for the parts of an event log that are stored in an entity's model.
    /// Event log segments are flushed to separate database items in order to keep
    /// the model storage size reasonably small.
    /// </summary>
    [MetaSerializable]
    public abstract class MetaEventLog<TEntry> where TEntry : MetaEventLogEntry
    {
        /// <summary>
        /// The id for the next entry.
        /// Equivalently, the number of entries ever added to the event log
        /// (but not necessarily the number of entries still available, which may be lower).
        /// </summary>
        [MetaMember(100)] public int                                RunningEntryId              = 0;
        /// <summary>
        /// The latest entries added to the log, in order of addition; oldest first.
        /// When this list grows long enough, it's copied to <see cref="PendingSegments"/>
        /// (from which it will later get flushed to the database) and cleared.
        /// Number of entries per segment is controlled by
        /// EntityEventLogConfiguration.NumEntriesPerPersistedSegment .
        /// </summary>
        [MetaMember(101)] public List<TEntry>                       LatestSegmentEntries        = new List<TEntry>();
        /// <summary>
        /// The id of the latest segment, i.e. the still-incomplete segment whose entries
        /// are in <see cref="LatestSegmentEntries"/>.
        /// When <see cref="LatestSegmentEntries"/> is moved into <see cref="PendingSegments"/>
        /// and cleared, this is incremented.
        /// </summary>
        [MetaMember(103)] public int                                RunningSegmentId            = 0;
        /// <summary>
        /// Segments waiting to be flushed to separate database items; oldest first.
        /// </summary>
        [MetaMember(105)] public List<PendingSegment>               PendingSegments             = new List<PendingSegment>();
        /// <summary>
        /// Id of the oldest available segment.
        /// Whether the oldest segment is persisted, or in <see cref="PendingSegments"/>,
        /// or <see cref="LatestSegmentEntries"/>, depends on <see cref="RunningSegmentId"/>
        /// and on the count of <see cref="PendingSegments"/>.
        /// </summary>
        [MetaMember(106)] public int                                OldestAvailableSegmentId    = 0;
        /// <summary>
        /// Legacy, only kept for state migration.
        /// </summary>
        [MetaMember(104)] List<LegacyMetaEventLogSegmentMetadata> _legacyPersistedSegmentMetadatas = null;

        [MetaSerializable]
        public class PendingSegment
        {
            [MetaMember(1)] public List<TEntry> Entries;

            PendingSegment() { }
            public PendingSegment(List<TEntry> entries) { Entries = entries; }
        }

        public int NumAvailablePersistedSegments => RunningSegmentId - OldestAvailableSegmentId - PendingSegments.Count;

        /// <summary>
        /// Update this event log's resident bookkeeping to forget its persisted segments.
        /// This does *not* remove the corresponding database items of the segments;
        /// this should only be used when actually performing the corresponding database
        /// item deletion, or when it is explicitly desired to forget the database items
        /// without deleting the database items (such as when export/importing a player).
        /// </summary>
        public void ForgetAllPersistedSegments()
        {
            OldestAvailableSegmentId += NumAvailablePersistedSegments;
        }

        /// <summary>
        /// On deserialization, migrate <see cref="_legacyPersistedSegmentMetadatas"/>
        /// to <see cref="OldestAvailableSegmentId"/>.
        /// </summary>
        [MetaOnDeserialized]
        void MigratePersistedSegmentMetadatas()
        {
            if (_legacyPersistedSegmentMetadatas != null) // Only migrate if not already migrated.
            {
                // Migrate to new state: only remember the id of the oldest persisted segment,
                // or if none exist then the id of the oldest pending segment, or if none exist
                // then the latest segment (RunningSegmentId).
                OldestAvailableSegmentId = _legacyPersistedSegmentMetadatas.Count > 0
                                           ? _legacyPersistedSegmentMetadatas[0].SegmentSequentialId
                                           : RunningSegmentId - PendingSegments.Count;

                // Forget legacy state.
                _legacyPersistedSegmentMetadatas = null;
            }
        }
    }

    /// <summary>
    /// Legacy, only kept for state migration.
    /// </summary>
    [MetaSerializable]
    public struct LegacyMetaEventLogSegmentMetadata
    {
        /// <summary> Id of the segment, allocated sequentially among segments within an event log. </summary>
        [MetaMember(1)] public int SegmentSequentialId  { get; private set; }
        /// <summary> The <see cref="MetaEventLogEntry.SequentialId"/> of the first entry in the segment. </summary>
        [MetaMember(2)] public int StartEntryId         { get; private set; }
        /// <summary> The number of entries in the segment. </summary>
        [MetaMember(3)] public int NumEntries           { get; private set; }
    }
}
