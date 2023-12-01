// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;

namespace Metaplay.Core.Json
{
    public enum JsonSerializationMode
    {
        /// <summary>
        /// Default serialization mode
        /// </summary>
        Default,

        /// <summary>
        /// Serialize for GDPR export
        /// </summary>
        GdprExport,

        /// <summary>
        /// Serialize analytics events
        /// </summary>
        AnalyticsEvents,

        /// <summary>
        /// Serialize for use in AdminAPI (dashboard).
        /// </summary>
        AdminApi,
    }

    /// <summary>
    /// The field or property is only included in the JSON output in the defined serialization mode. In other modes, the field or property is ignored.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class IncludeOnlyInJsonSerializationModeAttribute : Attribute
    {
        public readonly JsonSerializationMode Mode;

        public IncludeOnlyInJsonSerializationModeAttribute(JsonSerializationMode mode) => Mode = mode;
    }
}
