// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.IO;
using Metaplay.Core.Memory;
using Metaplay.Core.Model;
using Metaplay.Core.Serialization;
using System;
using System.Collections.Generic;

namespace Metaplay.Unity
{
    public class TimelineEntry
    {
        public Type                             ModelType { get; private set; }     // Type of the Model class
        public string                           Name { get; private set; }          // Name of operation (for display purposes)
        public readonly int                     Tick;                               // Tick number
        public readonly ModelAction             Action;                             // Action (null for ticks)
        public SegmentedIOBuffer                AfterState { get; private set; }    // Serialized state after the operation
        public readonly int                     LogicVersion;
        public readonly IGameConfigDataResolver Resolver;

        public TimelineEntry(Type modelType, string name, int tick, ModelAction action, SegmentedIOBuffer afterState, int logicVersion, IGameConfigDataResolver resolver)
        {
            ModelType       = modelType;
            Name            = name;
            Tick            = tick;
            Action          = action;
            AfterState      = afterState;
            LogicVersion    = logicVersion;
            Resolver        = resolver;
        }
    }

    // TimelineHistory

    public class TimelineHistory : ITimelineHistory
    {
        const int                       HistoryBufferSize = 200;

        public bool                     IsEnabled   { get; private set; } = false;
        public List<TimelineEntry>      Entries     { get; } = new List<TimelineEntry>();

        Stack<SegmentedIOBuffer>        _buffers    = new Stack<SegmentedIOBuffer>(); // Recycled buffers

        public TimelineHistory()
        {
        }

        public void Dispose()
        {
            Clear();
        }

        public void SetEnabled(bool isEnabled)
        {
            if (isEnabled == IsEnabled)
                return;
            IsEnabled = isEnabled;

            if (!IsEnabled)
                Clear();
        }

        public void AddEntry<TModel>(string name, int tick, ModelAction action, TModel afterState) where TModel : class, IModel
        {
            // If not enabled, skip
            if (!IsEnabled)
                return;

            // Resolve the beforeState (== afterState of the previous entry of the same modelType)
            Type modelType = afterState.GetType();

            // Serialize state
            SegmentedIOBuffer afterStateBuffer = AllocateBuffer();
            JournalUtil.Serialize(afterStateBuffer, afterState, MetaSerializationFlags.IncludeAll, afterState.LogicVersion);

            // Store entry
            Entries.Add(new TimelineEntry(modelType, name, tick, action, afterStateBuffer, afterState.LogicVersion, afterState.GetDataResolver()));

            // Trim to max history size
            while (Entries.Count > HistoryBufferSize)
            {
                // Pop the oldest entry from the list
                TimelineEntry entry = Entries[0];
                Entries.RemoveAt(0);

                // Return the AfterState buffer into the pool
                SegmentedIOBuffer afterBuffer = entry.AfterState;
                afterBuffer.Clear();
                _buffers.Push(afterBuffer);
            }
        }

        public void ExportEntry(int entryNdx, out string operationStr, out string differenceStr, out string beforeStateStr, out string afterStateStr)
        {
            TimelineEntry entry = Entries[entryNdx];
            operationStr = (entry.Action != null) ? PrettyPrinter.Verbose(entry.Action) : null;

            // Find the beforeState for the operation (an older entry that refers to the same ModelType)
            SegmentedIOBuffer beforeStateBuffer = null;
            for (int ndx = entryNdx - 1; ndx >= 0; ndx--)
            {
                if (Entries[ndx].ModelType == entry.ModelType)
                {
                    beforeStateBuffer = Entries[ndx].AfterState;
                    break;
                }
            }

            // Try to deserialize state before & after the operation
            IModel beforeState = null;
            IModel afterState;
            try
            {
                if (beforeStateBuffer != null)
                {
                    using (IOReader beforeReader = new IOReader(beforeStateBuffer))
                        beforeState = MetaSerialization.DeserializeTagged<IModel>(beforeReader, MetaSerializationFlags.ComputeChecksum, entry.Resolver, entry.LogicVersion);
                }

                using (IOReader afterReader = new IOReader(entry.AfterState))
                    afterState = MetaSerialization.DeserializeTagged<IModel>(afterReader, MetaSerializationFlags.ComputeChecksum, entry.Resolver, entry.LogicVersion);
            }
            catch (Exception ex)
            {
                beforeStateStr = "failed";
                afterStateStr = "failed";
                differenceStr = $"Failed to deserialize snapshot {entry.Name}: {ex}";
                return;
            }

            if (beforeStateBuffer != null)
            {
                try
                {
                    differenceStr = PrettyPrinter.Difference(beforeState, afterState);
                }
                catch (Exception ex)
                {
                    differenceStr = $"Failed to compute difference for entry {entry.Name}: {ex}";
                }
            }
            else
                differenceStr = "Cannot compute due to before state not being known";

            beforeStateStr = PrettyPrinter.Verbose(beforeState);
            afterStateStr = PrettyPrinter.Verbose(afterState);
        }

        void Clear()
        {
            // Clear all entries & dispose owned AfterState buffers
            foreach (TimelineEntry entry in Entries)
                entry.AfterState.Dispose();
            Entries.Clear();

            // Dispose all buffers
            foreach (SegmentedIOBuffer buffer in _buffers)
                buffer.Dispose();
            _buffers.Clear();
        }

        SegmentedIOBuffer AllocateBuffer()
        {
            if (_buffers.Count > 0)
                return _buffers.Pop();
            else
                return new SegmentedIOBuffer();
        }
    }
}
