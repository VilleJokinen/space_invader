// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Analytics;
using Metaplay.Core.EventLog;
using Metaplay.Core.Model;

namespace Metaplay.Server.ServerAnalyticsEvents
{
    /// <summary>
    /// Base class for server's analytics events, both Metaplay core and
    /// game-specific event types. Server events may be useful for conveying
    /// information about server state into the analytics pipeline.
    /// </summary>
    [MetaSerializable]
    [AnalyticsEventCategory("Server")]
    public abstract class ServerEventBase : EntityEventBase
    {
    }
}
