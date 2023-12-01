// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;

namespace Metaplay.Core
{
    /// <summary>
    /// Define a text description for a runtime option, an AdminApi permission, or similar,
    /// including support for markdown.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public class MetaDescriptionAttribute : Attribute
    {
        public readonly string Description;

        public MetaDescriptionAttribute(string description) { Description = description; }
    }
}
