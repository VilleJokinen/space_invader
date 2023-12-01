// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Model;

namespace Metaplay.Cloud.Analytics
{
    /// <summary>
    /// Label for a custom data in an analytics event data.
    /// </summary>
    [MetaSerializable]
    public class AnalyticsLabel : DynamicEnum<AnalyticsLabel>
    {
        protected AnalyticsLabel(int value, string name) : base(value, name, isValid: true)
        {
        }
    }
}
