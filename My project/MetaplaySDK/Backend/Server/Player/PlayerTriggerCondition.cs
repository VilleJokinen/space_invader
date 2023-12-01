// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Analytics;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Metaplay.Server
{
    [MetaSerializable]
    public interface PlayerTriggerCondition
    {
        IEnumerable<Type> EventTypesToConsider { get; }
        bool SatisfiesCondition(PlayerEventBase ev);
    }

    [MetaSerializableDerived(1)]
    public class PlayerTriggerConditionByTriggerType : PlayerTriggerCondition
    {
        [MetaMember(1)]
        public int EventTypeCode { get; private set; }

        [JsonIgnore]
        Type _eventType;

        [JsonIgnore]
        public IEnumerable<Type> EventTypesToConsider => Enumerable.Repeat(_eventType, _eventType == null ? 0 : 1);

        public PlayerTriggerConditionByTriggerType() { }

        [MetaOnDeserialized]
        void UpdateEventType()
        {
            if (AnalyticsEventRegistry.EventSpecsByTypeCode.TryGetValue(EventTypeCode, out AnalyticsEventSpec spec))
                _eventType = spec.Type;
            else
                _eventType = null;
        }

        public bool SatisfiesCondition(PlayerEventBase ev)
        {
            return _eventType?.IsAssignableFrom(ev.GetType()) ?? false;
        }
    }
}
