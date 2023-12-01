// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;

namespace Metaplay.Core.Analytics
{
    /// <summary>
    /// Determines the formatting mode of an field in Analytics Event when processed by
    /// BigQuery Analytics Sink.
    /// </summary>
    public enum BigQueryAnalyticsFormatMode
    {
        /// <summary>
        /// Field, Property or Type is ignored and data is not written
        /// </summary>
        Ignore = 0,
    }

    /// <summary>
    /// Specifies formatting rule for the Field, Property or a Type. If formatting is defined
    /// for both the type and the field (or property), the mode in field (or property) declaration
    /// is used.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Class | AttributeTargets.Struct)]
    public class BigQueryAnalyticsFormatAttribute : Attribute
    {
        public BigQueryAnalyticsFormatMode Mode { get; private set; }
        public BigQueryAnalyticsFormatAttribute(BigQueryAnalyticsFormatMode mode)
        {
            Mode = mode;
        }
    }

    /// <summary>
    /// Specifies the name of an analytics event, or a parameter in an analytics event,
    /// for BigQuery. Without this, the default name is used (C# class name for events,
    /// or member name for parameters).
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Field)]
    public class BigQueryAnalyticsNameAttribute : Attribute
    {
        public string Name { get; private set; }

        public BigQueryAnalyticsNameAttribute(string name)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
        }
    }
}
