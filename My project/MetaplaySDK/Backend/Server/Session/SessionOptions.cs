// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using System;

namespace Metaplay.Server
{
    [RuntimeOptions("Session", isStatic: false, "Configuration options for player sessions.")]
    public class SessionOptions : RuntimeOptionsBase
    {
        /// <summary>
        /// Used for bounding the length of sessions to some (large) value.
        /// This can be useful if outliers with very long sessions are not desired.
        /// </summary>
        [MetaDescription("Optional: The maximum amount of time that a session can be active.")]
        public TimeSpan? MaximumSessionLength { get; private set; } = null;
    }
}
