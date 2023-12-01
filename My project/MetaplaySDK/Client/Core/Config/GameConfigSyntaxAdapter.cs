// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Linq;

namespace Metaplay.Core.Config
{
    /// <summary>
    /// Attribute to allow converting the legacy game config legacy syntax to be understandable
    /// by the new game config pipeline. Use this to specify replacement rules for the header
    /// row cells. Eg, 'Type -> Type #key' converts any cell with payload 'Type' to 'Type #key'.
    /// All '[Array]' style syntax is also converted to 'Array[&lt;ndx&gt;]' with automatically
    /// computed indexes.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true)]
    public class GameConfigSyntaxAdapterAttribute : Attribute
    {
        public struct ReplaceRule
        {
            public readonly string From;
            public readonly string To;

            public ReplaceRule(string from, string to)
            {
                From = from;
                To = to;
            }
        }

        public readonly ReplaceRule[]   HeaderReplaces;
        public readonly ReplaceRule[]   HeaderPrefixReplaces;

        public GameConfigSyntaxAdapterAttribute(string[] headerReplaces = null, string[] headerPrefixReplaces = null)
        {
            HeaderReplaces = ParseReplaceRules(headerReplaces);
            HeaderPrefixReplaces = ParseReplaceRules(headerPrefixReplaces);
        }

        static ReplaceRule[] ParseReplaceRules(string[] inputs)
        {
            if (inputs == null)
                return Array.Empty<ReplaceRule>();

            return inputs.Select(str =>
            {
                string[] parts = str.Split(" -> ");
                if (parts.Length != 2)
                    throw new ArgumentException("Header replace rule must be exactly in the format '<from> -> <to>' (must have space before and after the arrow!)");
                return new ReplaceRule(parts[0], parts[1]);
            }).ToArray();
        }
    }
}
