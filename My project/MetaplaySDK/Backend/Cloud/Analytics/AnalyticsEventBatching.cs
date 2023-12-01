// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Analytics;
using Metaplay.Core.Config;
using Metaplay.Core.IO;
using Metaplay.Core.Json;
using Metaplay.Core.League;
using Metaplay.Core.Math;
using Metaplay.Core.Memory;
using Metaplay.Core.Model;
using Metaplay.Core.Serialization;
using System;
using System.Collections;
using System.Collections.Generic;

namespace Metaplay.Cloud.Analytics
{
    // SERVER EVENT BATCHING

    [MetaReservedMembers(1, 100)]
    public abstract class PlayerAnalyticsContext : AnalyticsContextBase
    {
        /// <summary>
        /// Session number of the current session. 1 for the first session. null if there is no session.
        /// </summary>
        [MetaMember(1)] public int?                                 SessionNumber   { get; private set; }
        /// <summary>
        /// Active experiments the player is in, and the variant within. Keys are ExperimentAnalyticsId and values Variant AnalyticsIds.
        /// </summary>
        [MetaMember(2)] public OrderedDictionary<string, string>    Experiments     { get; private set; }

        protected PlayerAnalyticsContext() { }
        public PlayerAnalyticsContext(int? sessionNumber, OrderedDictionary<string, string> experiments)
        {
            SessionNumber = sessionNumber;
            Experiments = experiments;
        }
    }

    [MetaSerializableDerived(1)]
    public sealed class DefaultPlayerAnalyticsContext : PlayerAnalyticsContext
    {
        DefaultPlayerAnalyticsContext() {}

        public DefaultPlayerAnalyticsContext(int? sessionNumber, OrderedDictionary<string, string> experiments)
        : base(sessionNumber, experiments) { }
    }

    [MetaSerializableDerived(2)]
    public sealed class GuildAnalyticsContext : AnalyticsContextBase
    {
    }

    [MetaSerializableDerived(3)]
    public sealed class ServerAnalyticsContext : AnalyticsContextBase
    {
    }


    [MetaSerializableDerived(4)]
    [LeaguesEnabledCondition]
    public sealed class DivisionAnalyticsContext : AnalyticsContextBase
    {
    }


    [MetaSerializable]
    public struct AnalyticsEventEnvelope
    {
        [MetaMember(7)] public EntityId                                     Source          { get; private set; }   // Entity where event happened
        //[MetaMember(2)] public MetaTime                                     CollectedAt     { get; private set; }   // Wall clock time when event was collected
        [MetaMember(3)] public MetaTime                                     ModelTime       { get; private set; }   // Time of Model when event was triggered
        [MetaMember(4)] public MetaUInt128                                  UniqueId        { get; private set; }   // Unique id for the event (for deduplication purposes)
        [MetaMember(1)] public string                                       EventType       { get; private set; }   // Type of the event (maps to the event class)
        [MetaMember(5)] public int                                          SchemaVersion   { get; private set; }   // Schema version of event (defaults to 1)
        [MetaMember(6)] public AnalyticsEventBase                           Payload         { get; private set; }   // Event payload (or parameters)
        [MetaMember(8)] public AnalyticsContextBase                         Context         { get; private set; }   // Event context
        [JsonSerializeNullCollectionAsEmpty]
        [MetaMember(9)] public OrderedDictionary<AnalyticsLabel, string>    Labels          { get; private set; }   // Set of custom labels. This may be null to signify empty set.


        public AnalyticsEventEnvelope(EntityId source, MetaTime collectedAt, MetaTime modelTime, MetaUInt128 uniqueId, string eventType, int schemaVersion, AnalyticsEventBase payload, AnalyticsContextBase context, OrderedDictionary<AnalyticsLabel, string> labels)
        {
            Source          = source;
            //CollectedAt     = collectedAt; // \note excluded for now, as doesn't seem useful enough
            ModelTime       = modelTime;
            UniqueId        = uniqueId;
            EventType       = eventType;
            SchemaVersion   = schemaVersion;
            Payload         = payload;
            Context         = context;
            Labels          = labels;
        }
    }

    /// <summary>
    /// Batch of analytics events for sending to <see cref="AnalyticsDispatcherActor"/>.
    /// </summary>
    public sealed class AnalyticsEventBatch
    {
        public EntityId                     SourceId    { get; private set; }
        public AnalyticsEventEnvelope[]     Events      { get; private set; }
        public int                          Count       => Events.Length;

        public AnalyticsEventBatch(EntityId sourceId, AnalyticsEventEnvelope[] events)
        {
            SourceId = sourceId;
            Events = events;
        }
    }

    public sealed class SerializedAnalyticsEventBatch : IDisposable
    {
        struct EventInfo
        {
            public IGameConfigDataResolver  Resolver;
            public int?                     LogicVersion;

            public EventInfo(IGameConfigDataResolver resolver, int? logicVersion)
            {
                Resolver = resolver;
                LogicVersion = logicVersion;
            }
        }

        public EntityId     SourceId    { get; }
        SegmentedIOBuffer   _buffer;
        List<EventInfo>     _eventInfos;

        public int Count => _eventInfos?.Count ?? 0;

        public SerializedAnalyticsEventBatch(EntityId sourceId)
        {
            SourceId = sourceId;
            _buffer = null;
            _eventInfos = null;
        }

        public void Add(AnalyticsEventEnvelope ev, IGameConfigDataResolver resolver, int? logicVersion)
        {
            if (_buffer == null)
                _buffer = new SegmentedIOBuffer(segmentSize: 4096);
            if (_eventInfos == null)
                _eventInfos = new List<EventInfo>();

            using (IOWriter writer = new IOWriter(_buffer, IOWriter.Mode.Append))
            {
                MetaSerialization.SerializeTagged(writer, ev, MetaSerializationFlags.IncludeAll, logicVersion);
            }
            _eventInfos.Add(new EventInfo(resolver, logicVersion));
        }

        /// <summary>
        /// Creates a new batch and moves the serialized events into it. Original batch is left empty.
        /// </summary>
        public SerializedAnalyticsEventBatch StealToNewSerializedEventBatch()
        {
            SerializedAnalyticsEventBatch newBatch = new SerializedAnalyticsEventBatch(SourceId);
            newBatch._buffer = _buffer;
            newBatch._eventInfos = _eventInfos;
            _buffer = null;
            _eventInfos = null;
            return newBatch;
        }

        public AnalyticsEventBatch Deserialize(IMetaLogger errorLog)
        {
            if (Count == 0)
                return new AnalyticsEventBatch(SourceId, Array.Empty<AnalyticsEventEnvelope>());

            AnalyticsEventEnvelope[] envelopes = new AnalyticsEventEnvelope[Count];
            int writeNdx = 0;
            using (IOReader reader = new IOReader(_buffer))
            {
                for (int ndx = 0; ndx < Count; ++ndx)
                {
                    try
                    {
                        EventInfo eventInfo = _eventInfos[ndx];
                        envelopes[writeNdx] = MetaSerialization.DeserializeTagged<AnalyticsEventEnvelope>(reader, MetaSerializationFlags.IncludeAll, resolver: eventInfo.Resolver, logicVersion: eventInfo.LogicVersion);
                        writeNdx++;
                    }
                    catch (Exception ex)
                    {
                        errorLog.Error("Failed to deserialize Analytics Event. Event is lost. Error: {Error}", ex);
                    }
                }
            }

            // Compact array
            if (writeNdx != envelopes.Length)
                envelopes = envelopes.AsSpan(start: 0, length: writeNdx).ToArray();

            return new AnalyticsEventBatch(SourceId, envelopes);
        }

        public void Clear()
        {
            _buffer?.Dispose();
            _buffer = null;
            _eventInfos = null;
        }

        public void Dispose()
        {
            Clear();
        }
    }

    /// <summary>
    /// Helpers for dealing with AnalyticsEventBatch objects.
    /// </summary>
    public static class AnalyticsEventBatchHelper
    {
        public struct EventEnumerator : IEnumerator<AnalyticsEventEnvelope>
        {
            List<AnalyticsEventBatch> _batches;
            int _nextBatchIndex;
            AnalyticsEventBatch _batch;
            int _nextEventIndex;

            internal EventEnumerator(List<AnalyticsEventBatch> batches)
            {
                _batches = batches;
                _nextBatchIndex = 0;
                _batch = null;
                _nextEventIndex = 0;
                Current = default;
            }

            public AnalyticsEventEnvelope Current { get; private set; }
            object IEnumerator.Current => Current;

            public bool MoveNext()
            {
                for (;;)
                {
                    if (_batch != null)
                    {
                        if (_nextEventIndex < _batch.Count)
                        {
                            Current = _batch.Events[_nextEventIndex];
                            _nextEventIndex++;
                            return true;
                        }

                        _batch = null;
                    }

                    // for default ctor
                    if (_batches == null)
                        return false;

                    if (_nextBatchIndex < _batches.Count)
                    {
                        _batch = _batches[_nextBatchIndex];
                        _nextBatchIndex++;
                        _nextEventIndex = 0;
                        continue;
                    }

                    return false;
                }
            }

            public void Reset()
            {
                _nextBatchIndex = 0;
                _nextEventIndex = 0;
            }

            public int NumRemainingEvents
            {
                get
                {
                    int result = 0;

                    // this batch
                    if (_batch != null)
                    {
                        result += _batch.Count - _nextEventIndex; // can be 0 if next is last in batch
                    }

                    // next batches
                    if (_batches != null)
                    {
                        for (int ndx = _nextBatchIndex; ndx < _batches.Count; ++ndx)
                            result += _batches[ndx].Count;
                    }

                    return result;
                }
            }

            /// <summary>
            /// Number of complete batches remaining, i.e current batch is not included.
            /// </summary>
            public int NumRemainingBatches
            {
                get
                {
                    int result = 0;

                    if (_batches != null)
                    {
                        result += _batches.Count - _nextBatchIndex;
                    }

                    return result;
                }
            }

            void IDisposable.Dispose() { }
        }

        public struct EventEnumerable : IEnumerable<AnalyticsEventEnvelope>
        {
            List<AnalyticsEventBatch> _batches;

            internal EventEnumerable(List<AnalyticsEventBatch> batches)
            {
                _batches = batches;
            }

            public EventEnumerator GetEnumerator() => new EventEnumerator(_batches);
            IEnumerator<AnalyticsEventEnvelope> IEnumerable<AnalyticsEventEnvelope>.GetEnumerator() => GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        /// <summary>
        /// Enumerates all events in a set of batches.
        /// </summary>
        public static EventEnumerable EnumerateBatches(List<AnalyticsEventBatch> batches)
        {
            return new EventEnumerable(batches);
        }
    }

    // \todo [petri] better listener/handler pattern?
    public static class EventBatchListener
    {
        public delegate void HandleBatchDelegate(SerializedAnalyticsEventBatch batch);

        public static HandleBatchDelegate HandleBatch { get; set; }
    }

    public class AnalyticsEventBatcher<TPayload, TContext>
        where TPayload : AnalyticsEventBase
        where TContext : AnalyticsContextBase
    {
        EntityId                        _sourceId;
        int                             _maxBatchSize;
        SerializedAnalyticsEventBatch   _batch;

        public AnalyticsEventBatcher(EntityId sourceId, int maxBatchSize)
        {
            _sourceId       = sourceId;
            _maxBatchSize   = maxBatchSize;
            _batch          = new SerializedAnalyticsEventBatch(_sourceId);
        }

        public void Enqueue(EntityId source, MetaTime collectedAt, MetaTime modelTime, MetaUInt128 uniqueId, string eventType, int schemaVersion, TPayload payload, TContext context, OrderedDictionary<AnalyticsLabel, string> labels, IGameConfigDataResolver resolver, int? logicVersion)
        {
            _batch.Add(new AnalyticsEventEnvelope(source, collectedAt, modelTime, uniqueId, eventType, schemaVersion, payload, context, labels), resolver, logicVersion);
            if (_batch.Count >= _maxBatchSize)
                Flush();
        }

        public void Flush()
        {
            // If has any events, flush the batch
            if (_batch.Count > 0)
            {
                //DebugLog.Info("FLUSH BATCH OF {NumEvents} EVENTS FROM {SourceId}", _currentBatch.Count, _currentBatch.SourceId);

                EventBatchListener.HandleBatch?.Invoke(_batch);

                // Clear the buffer. In the usual case the HandleBatch would consume the contents but in the
                // case of an error, this might not happen.
                _batch.Clear();
            }
        }
    }
}
