// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Collections.Generic;
using System.Linq;
using static Metaplay.Core.Config.GameConfigSyntaxTree;
using static System.FormattableString;

namespace Metaplay.Core.Config
{
    public static class GameConfigSyntaxTreeUtil
    {
        public const string VariantIdKey = "/Variant";
        public const string AliasesKey = "/Aliases";

        static void CopyValues(ObjectNode dst, ObjectNode src)
        {
            foreach ((NodeMemberId memberId, NodeBase srcChild) in src.Members)
            {
                if (memberId.VariantId != null)
                {
                    // \todo How should this deal with multi-variant ObjectNodes
                    //       (i.e. where NodeMemberId can have non-null variantId)?
                    //       Or is this only ever used after variant expansion?
                    throw new MetaAssertException($"{nameof(CopyValues)} is not meant to be used with multi-variant {nameof(ObjectNode)}s");
                }

                // Any missing values in destination get copied to as-is (empty collections are also considered
                // missing for purposes of inheritance), nested objects are handled recursively.
                NodeBase dstChild = dst.TryGetChild(memberId.Name);
                if (dstChild is null || (dstChild is CollectionNode collection && collection.Elements.Length == 0))
                {
                    dst.Members[memberId] = srcChild;
                }
                else if (srcChild is ObjectNode srcChildObject)
                {
                    if (!(dstChild is ObjectNode dstChildObject))
                        throw new InvalidOperationException($"Trying to copy object type to non-object"); // \todo [petri] error message
                    CopyValues(dstChildObject, srcChildObject);
                }
            }
        }

        // For each (non-baseline) variant item, copy missing values from corresponding baseline item.
        // If there is no corresponding baseline item, then the item is considered to be newly-appended by
        // the variant and left unchanged.
        // \todo [nuutti] Design: How to combine with "copy-from-above" inheritance (InheritMissingValues)?
        public static void InheritVariantValuesFromBaseline(IEnumerable<RootObject> objects)
        {
            // Find all baseline objects
            Dictionary<ObjectId, RootObject> baselines = new Dictionary<ObjectId, RootObject>();
            foreach (RootObject obj in objects)
            {
                if (obj.VariantId != null)
                    continue;

                // \todo Throws due to duplicate value if all identity columns not marked as such -- do a dupe check earlier!
                baselines.Add(obj.Id, obj);
            }

            // Fill missing variant values from baseline objects
            foreach (RootObject obj in objects)
            {
                if (obj.VariantId == null)
                    continue;

                if (!baselines.TryGetValue(obj.Id, out RootObject baseline))
                    continue;

                CopyValues(dst: obj.Node, src: baseline.Node);
            }
        }

        /// <summary>
        /// Given a <see cref="RootObject"/> whose <see cref="RootObject.Node"/> has possibly
        /// a top-level member with name <see cref="AliasesKey"/>, produce a <see cref="RootObject"/>
        /// which has that aliases string (if any) as its <see cref="RootObject.Aliases"/> and the
        /// top-level member removed.
        /// </summary>
        /// <remarks>
        /// The returned object may share parts of its <see cref="RootObject.Node"/> tree
        /// with the original.
        /// </remarks>
        public static RootObject ExtractAliases(RootObject obj)
        {
            if (obj.Aliases != null)
                throw new ArgumentException("Object already has its aliases extracted", nameof(obj));

            // Member id corresponding to aliases column ("/Aliases").
            NodeMemberId aliasesMemberId = new NodeMemberId(name: AliasesKey, variantId: null);

            if (obj.Node.Members.TryGetValue(aliasesMemberId, out NodeBase aliasesNode))
            {
                ScalarNode aliasesScalar = (ScalarNode)aliasesNode;

                // Shallow-copy the top-level object, with the "/Aliases" member removed.
                OrderedDictionary<NodeMemberId, NodeBase> newTopLevelMembers = new OrderedDictionary<NodeMemberId, NodeBase>(obj.Node.Members);
                newTopLevelMembers.Remove(aliasesMemberId);
                ObjectNode newTopLevelNode = new ObjectNode(newTopLevelMembers);

                return new RootObject(obj.Id, newTopLevelNode, obj.Location, aliases: aliasesScalar.Value, variantId: obj.VariantId);
            }
            else
                return obj;
        }

        /// <summary>
        /// Check for any duplicates of (ItemId, VariantId) tuples in the root-level <paramref name="objects"/>.
        /// Information about duplicates is written into <paramref name="buildLog"/>.
        /// </summary>
        /// <param name="buildLog"></param>
        /// <param name="objects"></param>
        internal static void DetectDuplicateObjects(GameConfigBuildLog buildLog, IEnumerable<RootObject> objects)
        {
            Dictionary<(ObjectId, string), RootObject> objectsByKey = new(objects.Count());
            foreach (RootObject obj in objects)
            {
                if (objectsByKey.TryGetValue((obj.Id, obj.VariantId), out RootObject duplicate))
                {
                    string variantSuffix = obj.VariantId != null ? $" (variant '{obj.VariantId}')" : "";
                    string otherLocationUrl = duplicate.Location.SourceInfo.GetLocationUrl(duplicate.Location);
                    buildLog.WithLocation(obj.Location).Error(Invariant($"Duplicate object with key '{obj.Id}'{variantSuffix}, other copy is at {otherLocationUrl}"));
                }
                else
                    objectsByKey.Add((obj.Id, obj.VariantId), obj);
            }
        }

        /// <summary>
        /// Given a <see cref="RootObject"/> whose <see cref="RootObject.Node"/> tree has possibly
        /// multiple variants defined (i.e. <see cref="ObjectNode.Members"/> having keys with non-null
        /// <see cref="NodeMemberId.VariantId"/>, possibly in a deeply nested location within the tree;
        /// corresponding to column overrides), expand all the variants into separate top-level
        /// <see cref="RootObject"/>s instead, with their <see cref="RootObject.VariantId"/> assigned.
        /// Also if the top-level node has a "/Variant" member (corresponding to a row override),
        /// then that also produces object(s) with <see cref="RootObject.VariantId"/> assigned.
        /// <para>
        /// Note that returned non-baseline variants will *not* have copies of the baseline values.
        /// That copying is done as a later step.
        /// </para>
        /// </summary>
        public static IEnumerable<RootObject> ExtractVariants(RootObject obj)
        {
            if (obj.VariantId != null)
                throw new ArgumentException("Object already has its variant id extracted", nameof(obj));

            // Member id corresponding to row override variant id column ("/Variant").
            NodeMemberId variantMemberId = new NodeMemberId(name: VariantIdKey, variantId: null);

            OrderedDictionary<string, NodeBase> extractedVariants = ExtractNestedVariantsImpl(obj.Node);
            foreach ((string nestedVariantId, NodeBase extractedVariantNode) in extractedVariants)
            {
                ObjectNode extractedVariantObj = (ObjectNode)extractedVariantNode;

                // Variant id comes from either the extracted nested variant id (column override)
                // or top-level node /Variant member (row override),
                // or neither if it's a baseline row.

                if (nestedVariantId != null)
                {
                    // Variant expanded from column overrides.

                    if (extractedVariantObj.Members.ContainsKey(variantMemberId))
                    {
                        // \todo Better error. This corresponds to a variant row that also contains values in variant columns (double-check this claim).
                        throw new InvalidOperationException($"Extracted variant contains value for '{VariantIdKey}'");
                    }

                    yield return new RootObject(obj.Id, extractedVariantObj, obj.Location, aliases: obj.Aliases, variantId: nestedVariantId);
                }
                else if (extractedVariantObj.Members.TryGetValue(variantMemberId, out NodeBase variantIdsNode))
                {
                    // Row override with possibly multiple variants specified in the /Variant member.

                    ScalarNode variantIdsScalar = (ScalarNode)variantIdsNode;
                    string[] variantIds = variantIdsScalar.Value.Split(',').Select(x => x.Trim()).Where(x => x != string.Empty).ToArray();
                    if (variantIds.Length == 0)
                    {
                        // \todo Pass build log here, and better error
                        throw new InvalidOperationException("Malformed row override");
                    }

                    // Remove the /Variant member for clarity: Further pipeline processing should only use RootObject.VariantId.
                    extractedVariantObj.Members.Remove(variantMemberId);

                    if (variantIds.Count() != 1)
                    {
                        // Multiple variants specified in the /Variant member.
                        // Need to make as many clones of the object, so that the returned RootObjects are fully unique
                        // and don't share any of the node tree with each other. This might not entirely necessary in some
                        // cases but this keeps it simpler to think about.
                        foreach (string variantId in variantIds)
                        {
                            ObjectNode variantObj = (ObjectNode)extractedVariantObj.Clone();
                            yield return new RootObject(obj.Id, variantObj, obj.Location, aliases: obj.Aliases, variantId: variantId);
                        }
                    }
                    else
                    {
                        // There's only 1 variant specified in the /Variant member, so we can avoid cloning the object.
                        // The object won't be used anywhere else so we can use it as is.
                        yield return new RootObject(obj.Id, extractedVariantObj, obj.Location, aliases: obj.Aliases, variantId: variantIds.Single());
                    }
                }
                else
                {
                    // Baseline

                    yield return new RootObject(obj.Id, extractedVariantObj, obj.Location, aliases: obj.Aliases, variantId: null);
                }
            }
        }

        /// <summary>
        /// Recursive implementation used by <see cref="ExtractVariants"/>.
        /// Extracts the variants specified by the variants within <see cref="ObjectNode.Members"/>.
        /// </summary>
        static OrderedDictionary<string, NodeBase> ExtractNestedVariantsImpl(NodeBase node)
        {
            switch (node)
            {
                // Scalar is a leaf node and doesn't define any variants.
                case ScalarNode scalar:
                    return new OrderedDictionary<string, NodeBase> { { null, scalar } };

                // nulls can occur in collections, where elements were omitted.
                // It behaves similar to a scalar here.
                case null:
                    return new OrderedDictionary<string, NodeBase> { { null, null } };

                // Collection doesn't directly define variants, but within it there may be
                // ObjectNodes which do.
                // Note: if any part within a collection has a variant value, then the entire
                // collection will be overridden by that variant. In other words, variants
                // cannot override only parts of a collection.
                case CollectionNode collection:
                {
                    // Empty (zero element) collections cannot have variants, just return the empty collection
                    if (collection.Elements.Length == 0)
                    {
                        return new OrderedDictionary<string, NodeBase>
                        {
                            { null, collection }
                        };
                    }

                    // Recursively expand each potentially-multi-variant element of this collection.
                    // For each variant returned by the element expansions, establish a new single-variant collection.

                    OrderedDictionary<string, List<NodeBase>> elementsPerVariant = new OrderedDictionary<string, List<NodeBase>>();

                    foreach ((NodeBase originalElement, int elementIndex) in collection.Elements.ZipWithIndex())
                    {
                        OrderedDictionary<string, NodeBase> elementPerVariant = ExtractNestedVariantsImpl(originalElement);
                        foreach ((string variantId, NodeBase element) in elementPerVariant)
                        {
                            List<NodeBase> elements = elementsPerVariant.GetOrAddDefaultConstructed(variantId);
                            while (elements.Count < elementIndex)
                                elements.Add(null); // \todo What to do with missing collection entries? #missing-collection-entry

                            elements.Add(element);
                        }
                    }

                    return elementsPerVariant.ToOrderedDictionary(kv => kv.Key, kv => (NodeBase)new CollectionNode(kv.Value));
                }

                // Object's members can either directly define variants, or may be sub-trees which
                // recursively expand into variants.
                case ObjectNode objectNode:
                {
                    // Recursively expand each potentially-multi-variant member of this object.
                    // For each variant returned by the member expansions, establish a new single-variant object.

                    OrderedDictionary<string, OrderedDictionary<NodeMemberId, NodeBase>> membersPerVariant = new OrderedDictionary<string, OrderedDictionary<NodeMemberId, NodeBase>>();

                    foreach ((NodeMemberId memberId, NodeBase originalChildNode) in objectNode.Members)
                    {
                        OrderedDictionary<string, NodeBase> childPerVariant = ExtractNestedVariantsImpl(originalChildNode);

                        if (memberId.VariantId != null)
                        {
                            // For a variant definition at this node in the object tree,
                            // the subtree must be single-variant.
                            // (I.e. cannot have variants inside variants.)

                            if (childPerVariant.Count > 1 || !childPerVariant.ContainsKey(null))
                            {
                                // \todo Better error. Figure out if this can even happen with the sheet transformation.
                                //       Also, could allow childOverrides to contain the same variantId - but unclear if there's any need for that.
                                throw new InvalidOperationException("Conflicting variants in syntax tree");
                            }

                            NodeMemberId nonVariantMemberId = new NodeMemberId(memberId.Name, variantId: null);
                            membersPerVariant.GetOrAddDefaultConstructed(memberId.VariantId).Add(nonVariantMemberId, childPerVariant[null]);
                        }
                        else
                        {
                            // For a non-variant definition at this node in the object tree,
                            // the subtree may be multi-variant and expand into multiple single-variant subtrees.

                            foreach ((string childVariantId, NodeBase childNode) in childPerVariant)
                                membersPerVariant.GetOrAddDefaultConstructed(childVariantId).Add(memberId, childNode);
                        }
                    }

                    // Filter any variants which only have empty collections (so that implicitly inserted collections
                    // don't cause their parent objects to be created).
                    membersPerVariant.RemoveWhere(kvp =>
                    {
                        (string variantId, OrderedDictionary<NodeMemberId, NodeBase> members) = kvp;
                        bool isAllEmptyCollections = members.Values.All(m => m is CollectionNode collection && collection.Elements.Length == 0);
                        return isAllEmptyCollections;
                    });

                    return membersPerVariant.ToOrderedDictionary(kv => kv.Key, kv => (NodeBase)new ObjectNode(kv.Value));
                }

                default:
                    throw new InvalidOperationException($"Invalid {nameof(NodeBase)} type {node.GetType()}");
            }
        }
    }
}
