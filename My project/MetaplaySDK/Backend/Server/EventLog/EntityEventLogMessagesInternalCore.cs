// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.EventLog;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Serialization;
using Metaplay.Core.TypeCodes;
using System.Collections.Generic;

namespace Metaplay.Server.EventLog
{
    [MetaMessage(MessageCodesCore.TriggerEntityEventLogFlushing, MessageDirection.ServerInternal)]
    public class TriggerEventLogFlushing : MetaMessage
    {
    }

    /// <summary>
    /// Request the entity to scan a number of entries from its event log.
    /// The response is of (a subclass of) type <see cref="EntityEventLogScanResponse{TEntry}"/>
    ///
    /// This scan API is cursor-based; besides the entries, the response
    /// includes a cursor which can be used to continue the scan from where
    /// the previous scan ended (assuming the relevant entries haven't been
    /// deleted in the meantime).
    /// </summary>
    [MetaMessage(MessageCodesCore.EntityEventLogScanRequest, MessageDirection.ServerInternal)]
    public class EntityEventLogScanRequest : MetaMessage
    {
        /// <summary>
        /// The scan starts at this cursor and moves forwards (i.e. towards
        /// newer log entries).
        ///
        /// If this cursor indicates an entry older than the oldest still
        /// available entry, the scan starts at the oldest available entry
        /// instead. In particular, a cursor with 0 for both
        /// <see cref="EntityEventLogCursor.SegmentId"/>
        /// and <see cref="EntityEventLogCursor.EntryIndexWithinSegment"/>
        /// can be used start the scan from the oldest available entry.
        /// </summary>
        public EntityEventLogCursor StartCursor { get; private set; }
        /// <summary>
        /// Maximum number of entries to return. The amount of entries returned
        /// can be lower if the end of the event log is reached.
        /// </summary>
        public int                  NumEntries  { get; private set; }

        // \todo [nuutti] Add support for scanning backwards?

        EntityEventLogScanRequest() { }
        public EntityEventLogScanRequest(EntityEventLogCursor startCursor, int numEntries)
        {
            StartCursor = startCursor;
            NumEntries = numEntries;
        }
    }

    /// <summary>
    /// Response to <see cref="EntityEventLogScanRequest"/>.
    /// </summary>
    [MetaImplicitMembersRange(100, 200)]
    public abstract class EntityEventLogScanResponse<TEntry> : MetaMessage
        where TEntry : MetaEventLogEntry
    {
        /// <summary>
        /// Event log entries returned by the scan.
        /// These can be fewer than requested if the current end of the log was reached.
        ///
        /// \note MetaSerialized, because event log entries can contain arbitrary stuff, including game config references.
        /// </summary>
        public MetaSerialized<List<TEntry>> Entries             { get; set; }

        /// <summary>
        /// A cursor that can be used to perform a scan starting from where this scan ended.
        /// </summary>
        public EntityEventLogCursor         ContinuationCursor  { get; set; }
    }
}
