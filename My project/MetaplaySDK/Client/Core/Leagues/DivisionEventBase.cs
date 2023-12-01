// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Analytics;
using Metaplay.Core.EventLog;
using Metaplay.Core.Model;

namespace Metaplay.Core.League
{
    /// <summary>
    /// Base class for division-specific analytics events, both Metaplay core and
    /// game-specific event types.
    /// </summary>
    [AnalyticsEventCategory("Division")]
    public abstract class DivisionEventBase : EntityEventBase
    {
    }
}

