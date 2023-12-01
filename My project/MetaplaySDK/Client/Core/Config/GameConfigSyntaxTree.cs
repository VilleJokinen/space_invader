// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static System.FormattableString;

namespace Metaplay.Core.Config
{
    /// <summary>
    /// Abstract syntax tree that acts as an intermediate format when parsing game config data from various inputs.
    /// Each of the input types is handled by its own reader class, such as <see cref="GameConfigSpreadsheetReader"/>
    /// for spreadsheet data, that outputs a syntax tree.
    ///
    /// The syntax tree data structure is quite similar to an abstract JSON document. There are nodes for:
    /// - Scalar: An individual item in the input. Eg, the value of a single cell in a spreadsheet.
    /// - Collection: Represents a collection of items. Eg, a range of cells in a spreadsheet.
    /// - Object: Represents an object with named members, similar to C# class.
    ///
    /// The <see cref="GameConfigOutputItemParser"/> is then used to parse the syntax tree into the final
    /// game config object types, usually the classes with the 'Info' suffix.
    /// </summary>
    public static class GameConfigSyntaxTree
    {
        public static class BuiltinTags
        {
            /// <summary>The node in question specifies the object's identity, or part of it.</summary>
            public const string Key     = "key";
            /// <summary>The node is a comment only and should be ignored when parsing the final output item.</summary>
            public const string Comment = "comment";

            public static readonly string[] All = new string[] { Key, Comment };
        }

        /// <summary>
        /// Tag with optional value to give syntax tree nodes additional metadata.
        /// Can be used to mark a node as identity-providing (ie, the node is part of the item's identity),
        /// or to mark a node as a comment, or other similar purposes.
        /// </summary>
        public class NodeTag
        {
            public readonly string Name;
            public readonly string Value;

            public NodeTag(string name, string value)
            {
                Name = name;
                Value = value;
            }

            public override string ToString() => (Value != null) ? $"#{Name}:{Value}" : $"#{Name}";
        }

        /// <summary>
        /// Base class for a syntax tree node.
        /// </summary>
        public abstract class NodeBase
        {
            public GameConfigSourceLocation Location { get; private set; } // Location of the input (eg, cell coordinates), can be null for empty collections and such

            protected NodeBase(GameConfigSourceLocation location)
            {
                Location = location;
            }

            public abstract void ToStringBuilder(StringBuilder sb, string prefix, int depth);

            public string TreeToString(int depth = 0)
            {
                StringBuilder sb = new StringBuilder();
                ToStringBuilder(sb, "$root", depth: depth);
                return sb.ToString();
            }

            public abstract NodeBase Clone();

            /// <summary>
            /// Computes the a new location from a set of sources that covers all the input locations.
            /// For example, with <see cref="GameConfigSpreadsheetLocation"/>, this means the bounding box
            /// of all the nodes. All the input locations must have the same <see cref="GameConfigSourceInfo"/>.
            /// </summary>
            /// <param name="nodes">Collection of input nodes to compute the bounding location for.</param>
            /// <returns></returns>
            internal static GameConfigSourceLocation ComputeLocationUnion(IEnumerable<NodeBase> nodes)
            {
                GameConfigSourceInfo sourceInfo = null;
                int startRow    = int.MaxValue;
                int endRow      = 0;
                int startColumn = int.MaxValue;
                int endColumn   = 0;

                if (nodes != null)
                {
                    // \todo Generalize to support other than spreadsheet locations, too
                    foreach (GameConfigSpreadsheetLocation location in nodes.Where(node => node?.Location != null).Select(node => node.Location).Cast<GameConfigSpreadsheetLocation>())
                    {
                        if (sourceInfo == null)
                            sourceInfo = location.SourceInfo;
                        else
                            MetaDebug.Assert(sourceInfo == location.SourceInfo, "All source locations must have the same SourceInfo");
                        startRow    = System.Math.Min(startRow, location.Rows.Start);
                        endRow      = System.Math.Max(endRow, location.Rows.End);
                        startColumn = System.Math.Min(startColumn, location.Columns.Start);
                        endColumn   = System.Math.Max(endColumn, location.Columns.End);
                    }
                }

                // Return null if no valid input locations
                if (sourceInfo == null)
                    return null;

                return new GameConfigSpreadsheetLocation(sourceInfo, new GameConfigSpreadsheetLocation.CellRange(startRow, endRow), new GameConfigSpreadsheetLocation.CellRange(startColumn, endColumn));
            }
        }

        /// <summary>
        /// Scalar value node, corresponds to a single input sheet cell. Assumed to be a raw string
        /// that is not parsed. Any format-specific escaping should already be handled (eg, CSV escaping
        /// should no longer be here).
        /// </summary>
        public class ScalarNode : NodeBase
        {
            public string Value { get; private set; } // Raw string value

            public ScalarNode(string value, GameConfigSourceLocation location) : base(location)
            {
                Value = value;
            }

            public override void ToStringBuilder(StringBuilder sb, string prefix, int depth)
            {
                string indent = new string(' ', depth * 2);
                sb.AppendLine($"{indent}{prefix} = {Value}");
            }

            public override string ToString() => Invariant($"ScalarNode{{ Value='{Value}', Location={Location} }}");

            public override NodeBase Clone() => new ScalarNode(Value, Location);
        }

        /// <summary>
        /// Linear collection of syntax tree nodes. Agnostic to the layout in the input sheet so can
        /// be either vertical or horizontal array in the input sheets.
        /// </summary>
        public class CollectionNode : NodeBase
        {
            public NodeBase[] Elements { get; private set; }

            // \todo Support passing location explicitly to allow non-null location for empty collections (we still usually know the cells where the content would have been)
            public CollectionNode(IEnumerable<NodeBase> elements) : base(ComputeLocationUnion(elements))
            {
                Elements = elements.ToArray();
            }

            public override void ToStringBuilder(StringBuilder sb, string prefix, int depth)
            {
                string indent = new string(' ', depth * 2);
                sb.AppendLine($"{indent}{prefix}[]");

                for (int ndx = 0; ndx < Elements.Length; ndx++)
                {
                    NodeBase element = Elements[ndx];
                    // \todo Should missing elements be supported - or should GameConfigSpreadsheetReader produce empty-string scalars or empty objects instead of nulls? #missing-collection-entry
                    if (element != null)
                        element.ToStringBuilder(sb, Invariant($"#{ndx}"), depth + 1);
                    else
                        sb.AppendLine(Invariant($"{indent}  #{ndx} is null"));
                }
            }

            public override string ToString()
            {
                const int elementLimit = 4;
                string elementsStr = string.Join(", ", Elements.Take(elementLimit).Select(elem => elem.GetType().Name));
                string elementsSuffix = Elements.Length > elementLimit ? ", ..." : "";
                return Invariant($"CollectionNode{{ Elements=[ {elementsStr}{elementsSuffix} ] }}");
            }

            public override NodeBase Clone() => new CollectionNode(Elements.Select(e => e?.Clone()));
        }

        /// <summary>
        /// Complex object type with named members. Can be hierarchical.
        ///
        /// The members are identified not only by names, but by name+variantId tuples.
        /// Multi-variant objects, that is, objects with variant members (also in deeply nested locations)
        /// can be produced by early syntax tree parsing steps (e.g. sheet -> syntax tree).
        /// Later in the parsing pipeline such "multi-variant" syntax trees trees are expanded
        /// into multiple single-variant <see cref="RootObject"/>s. This is done by
        /// <see cref="GameConfigSyntaxTreeUtil.ExtractVariants(RootObject)"/>.
        ///
        /// Multi-variant syntax tree objects are supported for the purpose of simplifying the
        /// sheet->syntax tree (and possible others) transformation, so that it doesn't need to deal with
        /// the variant expansion at that time.
        /// </summary>
        public class ObjectNode : NodeBase
        {
            public OrderedDictionary<NodeMemberId, NodeBase> Members { get; private set; }

            // \todo Support passing location explicitly to allow non-null location for empty collections (we still usually know the cells where the content would have been)
            public ObjectNode(OrderedDictionary<NodeMemberId, NodeBase> members) : base(ComputeLocationUnion(members.Values))
            {
                Members = members;
            }

            public NodeBase TryGetChild(string name)
            {
                // \note Child getters always assume we're dealing with single-variant trees.
                NodeMemberId memberId = new NodeMemberId(name, variantId: null);
                return Members.GetValueOrDefault(memberId, null);
            }

            public NodeBase GetChild(string name)
            {
                // \note Child getters always assume we're dealing with single-variant trees.
                NodeMemberId memberId = new NodeMemberId(name, variantId: null);
                if (Members.TryGetValue(memberId, out NodeBase value))
                    return value;
                else
                    throw new InvalidOperationException($"No child with name '{name}' exists!");
            }

            public override void ToStringBuilder(StringBuilder sb, string prefix, int depth)
            {
                string indent = new string(' ', depth * 2);
                sb.AppendLine($"{indent}{prefix}:");

                foreach ((NodeMemberId memberId, NodeBase member) in Members)
                    member.ToStringBuilder(sb, memberId.ToString(), depth + 1);
            }

            public override string ToString()
            {
                string membersStr = string.Join(", ", Members.Select(m => Invariant($"{m.Key}={m.Value.GetType().Name}")));
                return $"ObjectNode{{ {membersStr} }}";
            }

            public override NodeBase Clone() => new ObjectNode(Members.ToOrderedDictionary(kv => kv.Key, kv => kv.Value.Clone()));
        }

        /// <summary>
        /// Name+variantId tuple for identifying a member within a <see cref="ObjectNode"/>.
        /// Non-variant members have null <see cref="VariantId"/>.
        /// See comment on <see cref="ObjectNode"/> for more explanation.
        /// </summary>
        public readonly struct NodeMemberId : IEquatable<NodeMemberId>
        {
            public readonly string Name;
            public readonly string VariantId;

            public NodeMemberId(string name, string variantId)
            {
                Name = name;
                VariantId = variantId;
            }

            public override bool Equals(object obj) => obj is NodeMemberId other && Equals(other);

            public bool Equals(NodeMemberId other)
            {
                return Name == other.Name &&
                       VariantId == other.VariantId;
            }

            public override int GetHashCode() => Util.CombineHashCode(Name?.GetHashCode() ?? 0, VariantId?.GetHashCode() ?? 0);
            public static bool operator ==(NodeMemberId a, NodeMemberId b) => a.Equals(b);
            public static bool operator !=(NodeMemberId a, NodeMemberId b) => !(a == b);

            public override string ToString()
            {
                if (VariantId == null)
                    return Name;
                else
                    return $"{Name} (variant {VariantId})";
            }
        }

        /// <summary>
        /// Specify the identity of a root <see cref="ObjectNode"/>. Essentially a wrapper for <code>string[]</code>
        /// which contains the raw cells values of any identity columns (column headers with the '#key' tag).
        /// </summary>
        public struct ObjectId
        {
            public string[] Values;

            public ObjectId(string[] values) => Values = values;

            public override bool Equals(object obj) => (obj is ObjectId other) ? Values.SequenceEqual(other.Values) : false;
            public override string ToString() => Values != null ? string.Join(";", Values) : "n/a";
            public override int GetHashCode()
            {
                int hash = 17;
                foreach (string val in Values)
                    hash = hash * 31 + val.GetHashCode();
                return hash;
            }
        }

        /// <summary>
        /// Represents a root-level object that is parsed from a game config source into the syntax
        /// tree format. In addition to the syntax tree root node, we explicitly keep track of the
        /// object's identity, which variant it belongs to, and the location in the source for parsing.
        /// </summary>
        public struct RootObject
        {
            public readonly ObjectId                    Id;         // Identity of the object (before parsing) -- similar to ConfigKey but not same as we don't have typed ConfigKey during parsing
            public readonly ObjectNode                  Node;       // Root node of the object's syntax tree
            public readonly GameConfigSourceLocation    Location;   // Location in the input where the object is defined

            /// <summary>
            /// Optional aliases for the item, can be null.
            /// Can contain multiple aliases, separated with commas.
            /// Note that this can also be null at an early stage after being output from <see cref="GameConfigSpreadsheetReader"/>
            /// (or equivalent), at which point the aliases may be in a top-level member of <see cref="Node"/> with name <see cref="GameConfigSyntaxTreeUtil.AliasesKey"/>.
            /// A later stage in the pipeline assigns this aliases member.
            /// </summary>
            public readonly string Aliases;

            /// <summary>
            /// Variant id, or null for baseline.
            /// Note that this can also be null at an early stage after being output from <see cref="GameConfigSpreadsheetReader"/>
            /// (or equivalent), at which point a single <see cref="RootObject"/> may correspond to multiple variants of
            /// a final config item. <see cref="GameConfigSyntaxTreeUtil.ExtractVariants"/> produces more refined objects
            /// which do have their proper <see cref="VariantId"/> assigned.
            /// </summary>
            public readonly string          VariantId;

            public RootObject(ObjectId id, ObjectNode node, GameConfigSourceLocation location, string aliases, string variantId)
            {
                Id = id;
                Node = node;
                Location = location;
                Aliases = aliases;
                VariantId = variantId;
            }

            public override string ToString() =>
                Invariant($"RootObject{{ Id={Id}, RootNode={Node}, Location={Location} }}");
        }
    }
}
