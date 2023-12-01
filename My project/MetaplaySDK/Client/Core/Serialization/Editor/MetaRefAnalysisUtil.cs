// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Config;
using Metaplay.Core.Player;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Metaplay.Core.Serialization
{
    /// <summary>
    /// Utilities for analyzing reference propagation within game configs.
    /// </summary>
    // \todo This could use comments and cleanup.
    public static class MetaRefAnalysisUtil
    {
        public struct ItemTypeReference : IEquatable<ItemTypeReference>
        {
            public Type From;
            public Type To;
            public string Path;

            public ItemTypeReference(Type from, Type to, string path)
            {
                From = from;
                To = to;
                Path = path;
            }

            public override bool Equals(object obj) => obj is ItemTypeReference other && Equals(other);

            public bool Equals(ItemTypeReference other)
            {
                return From == other.From
                    && To == other.To
                    && Path == other.Path;
            }

            public override int GetHashCode() => Util.CombineHashCode(From?.GetHashCode() ?? 0, To?.GetHashCode() ?? 0, Path?.GetHashCode() ?? 0);
            public static bool operator ==(ItemTypeReference left, ItemTypeReference right) => left.Equals(right);
            public static bool operator !=(ItemTypeReference left, ItemTypeReference right) => !(left == right);
        }

        public class AnalysisContext
        {
            public IGameConfig GameConfig;
            public ExperimentVariantPair? PatchId;
            public OrderedSet<ConfigItemId> RootItems;
            public OrderedDictionary<ConfigItemId, OrderedSet<(ConfigItemId, string)>> ItemReverseReferences;
            public OrderedSet<ItemTypeReference> RelevantTypeLevelReferences;
            public OrderedSet<ItemTypeReference> DisabledTypeLevelReferences;

            public AnalysisContext(
                IGameConfig gameConfig,
                ExperimentVariantPair? patchId,
                OrderedSet<ConfigItemId> rootItems,
                OrderedDictionary<ConfigItemId, OrderedSet<(ConfigItemId, string)>> itemReverseReferences,
                OrderedSet<ItemTypeReference> relevantTypeLevelReferences,
                OrderedSet<ItemTypeReference> disabledTypeLevelReferences)
            {
                GameConfig = gameConfig;
                PatchId = patchId;
                RootItems = rootItems;
                ItemReverseReferences = itemReverseReferences;
                RelevantTypeLevelReferences = relevantTypeLevelReferences;
                DisabledTypeLevelReferences = disabledTypeLevelReferences;
            }
        }

        public static AnalysisContext CreateInitialAnalysisContextForPatch(DynamicTraversal.Resources traversalResources, GameConfigImportResources importResources, ExperimentVariantPair patchId)
        {
            ISharedGameConfig sharedConfig = (ISharedGameConfig)GameConfigFactory.Instance.ImportGameConfig(GameConfigImportParams.Specialization(importResources, new OrderedSet<ExperimentVariantPair> { patchId }));

            OrderedSet<ConfigItemId> roots = CollectDirectlyPatchedItems(sharedConfig);

            return CreateInitialAnalysisContext(
                traversalResources,
                sharedConfig,
                roots);
        }

        public static AnalysisContext CreateInitialAnalysisContext(DynamicTraversal.Resources traversalResources, IGameConfig gameConfig, OrderedSet<ConfigItemId> roots)
        {
            OrderedDictionary<ConfigItemId, OrderedSet<(ConfigItemId, string)>> reverseReferences = CollectItemReverseReferences(traversalResources, gameConfig);

            OrderedSet<ItemTypeReference> relevantTypeLevelReferences = new OrderedSet<ItemTypeReference>();

            _ = ResolveReachability(
                roots,
                tryGetNodeNeighbors: (ConfigItemId referredId) =>
                {
                    if (!reverseReferences.TryGetValue(referredId, out OrderedSet<(ConfigItemId ReferringId, string Path)> referrings))
                        return null;

                    List<(ConfigItemId, ItemTypeReference)> neighbors = new List<(ConfigItemId, ItemTypeReference)>();

                    foreach ((ConfigItemId referringId, string path) in referrings)
                    {
                        ItemTypeReference reference = new ItemTypeReference(referringId.ItemType, referredId.ItemType, path);
                        relevantTypeLevelReferences.Add(reference);
                        neighbors.Add((referringId, reference));
                    }

                    return neighbors;
                });

            relevantTypeLevelReferences = relevantTypeLevelReferences.OrderBy(r => (r.From.Name, r.To.Name, r.Path)).ToOrderedSet();

            return new AnalysisContext(
                gameConfig: gameConfig,
                patchId: null,
                rootItems: roots,
                itemReverseReferences: reverseReferences,
                relevantTypeLevelReferences: relevantTypeLevelReferences,
                disabledTypeLevelReferences: new OrderedSet<ItemTypeReference>());
        }

        public class AnalysisResult
        {
            public OrderedSet<ConfigItemId> ReachableItems;
            public OrderedDictionary<ItemTypeReference, int> TypeLevelReferenceInfluences;

            public AnalysisResult(OrderedSet<ConfigItemId> reachableItems, OrderedDictionary<ItemTypeReference, int> typeLevelReferenceInfluences)
            {
                ReachableItems = reachableItems;
                TypeLevelReferenceInfluences = typeLevelReferenceInfluences;
            }
        }

        public static AnalysisResult Analyze(AnalysisContext ctx)
        {
            ReachabilityResult<ConfigItemId, ItemTypeReference> reachabilityResult = ResolveReachability(
                ctx.RootItems,
                tryGetNodeNeighbors: (ConfigItemId referredId) =>
                {
                    if (!ctx.ItemReverseReferences.TryGetValue(referredId, out OrderedSet<(ConfigItemId ReferringId, string Path)> referrings))
                        return null;

                    List<(ConfigItemId, ItemTypeReference)> neighbors = new List<(ConfigItemId, ItemTypeReference)>();

                    foreach ((ConfigItemId referringId, string path) in referrings)
                    {
                        ItemTypeReference reference = new ItemTypeReference(referringId.ItemType, referredId.ItemType, path);

                        if (ctx.DisabledTypeLevelReferences.Contains(reference))
                            continue;

                        neighbors.Add((referringId, reference));
                    }

                    return neighbors;
                });

            return new AnalysisResult(
                reachabilityResult.ReachableNodes,
                reachabilityResult.EdgeInfluences);
        }

        public static List<ConfigItemId> CollectAllItems(IGameConfig config)
        {
            List<ConfigItemId> allItemIds = new List<ConfigItemId>();

            foreach (IGameConfigLibraryEntry library in GetLibraries(config))
            {
                foreach (object key in library.EnumerateAll().Select(kv => kv.Key))
                {
                    ConfigItemId referringId = new ConfigItemId(library.GetInfoByKey(key).GetType(), key);
                    allItemIds.Add(referringId);
                }
            }

            return allItemIds;
        }

        public static OrderedSet<ConfigItemId> CollectDirectlyPatchedItems(IGameConfig config)
        {
            OrderedSet<ConfigItemId> patchedItemIds = new OrderedSet<ConfigItemId>();

            foreach (IGameConfigLibraryEntry library in GetLibraries(config))
            {
                foreach (object key in library.EnumerateDirectlyPatchedKeys())
                {
                    ConfigItemId referringId = new ConfigItemId(library.GetInfoByKey(key).GetType(), key);
                    patchedItemIds.Add(referringId);
                }
            }

            return patchedItemIds;
        }

        public static OrderedSet<ConfigItemId> CollectIndirectlyPatchedItems(IGameConfig config)
        {
            OrderedSet<ConfigItemId> patchedItemIds = new OrderedSet<ConfigItemId>();

            foreach (IGameConfigLibraryEntry library in GetLibraries(config))
            {
                foreach (object key in library.EnumerateIndirectlyPatchedKeys())
                {
                    ConfigItemId referringId = new ConfigItemId(library.GetInfoByKey(key).GetType(), key);
                    patchedItemIds.Add(referringId);
                }
            }

            return patchedItemIds;
        }

        public static OrderedDictionary<ConfigItemId, OrderedSet<(ConfigItemId, string)>> CollectItemReverseReferences(DynamicTraversal.Resources traversalResources, IGameConfig config)
        {
            OrderedDictionary<ConfigItemId, OrderedSet<(ConfigItemId, string)>> references = CollectItemReferences(traversalResources, config);
            OrderedDictionary<ConfigItemId, OrderedSet<(ConfigItemId, string)>> reverseReferences = ComputeReverseReferences(references);
            return reverseReferences;
        }

        struct ItemMetaRefEntry
        {
            public IMetaRef MetaRef;
            public string VaguePath;

            public ItemMetaRefEntry(IMetaRef metaRef, string vaguePath)
            {
                MetaRef = metaRef;
                VaguePath = vaguePath;
            }
        }

        class MetaRefCollectingVisitor : DynamicTraversal.PathVisitor
        {
            public MetaRefCollectingVisitor(DynamicTraversal.Resources resources) : base(resources)
            {
            }

            public List<ItemMetaRefEntry> MetaRefs = new List<ItemMetaRefEntry>();

            public override void Traverse(Type type, object obj)
            {
                if (!Resources.TypeContainsMetaRefs(type))
                    return;

                base.Traverse(type, obj);
            }

            public override void VisitMetaRef(Type type, object obj)
            {
                base.VisitMetaRef(type, obj);

                if (obj != null)
                {
                    string vaguePath = string.Concat(_currentPath.Select(part => VaguePathPart(part.Str)));
                    MetaRefs.Add(new ItemMetaRefEntry((IMetaRef)obj, vaguePath));
                }
            }
        }

        static string VaguePathPart(string str)
        {
            if (str.StartsWith("[", StringComparison.Ordinal)
             && str.EndsWith("]", StringComparison.Ordinal))
            {
                return "[]";
            }

            return str;
        }

        static IEnumerable<IGameConfigLibraryEntry> GetLibraries(IGameConfig config)
        {
            foreach (GameConfigRepository.GameConfigEntryInfo entryInfo in GameConfigRepository.Instance.GetGameConfigTypeInfo(config.GetType()).Entries.Values)
            {
                if (!entryInfo.MemberInfo.GetDataMemberType().IsGameConfigLibrary())
                    continue;

                IGameConfigLibraryEntry library = (IGameConfigLibraryEntry)entryInfo.MemberInfo.GetDataMemberGetValueOnDeclaringType()(config);
                yield return library;
            }
        }

        static OrderedDictionary<object, List<ItemMetaRefEntry>> CollectMetaRefsWithPaths<TInfo>(DynamicTraversal.Resources traversalResources, IEnumerable<KeyValuePair<object, object>> items)
            where TInfo : class, IGameConfigData, new()
        {
            OrderedDictionary<object, List<ItemMetaRefEntry>> itemMetaRefs = new OrderedDictionary<object, List<ItemMetaRefEntry>>();

            foreach ((object key, object item) in items)
            {
                MetaRefCollectingVisitor visitor = new MetaRefCollectingVisitor(traversalResources);
                visitor.Traverse(typeof(GameConfigDataContent<TInfo>), new GameConfigDataContent<TInfo>((TInfo)item));
                itemMetaRefs.Add(key, visitor.MetaRefs);
            }

            return itemMetaRefs;
        }

        static OrderedDictionary<ConfigItemId, OrderedSet<(ConfigItemId, string)>> CollectItemReferences(DynamicTraversal.Resources traversalResources, IGameConfig config)
        {
            OrderedDictionary<ConfigItemId, OrderedSet<(ConfigItemId, string)>> referencesPerItem = new OrderedDictionary<ConfigItemId, OrderedSet<(ConfigItemId, string)>>();

            foreach (IGameConfigLibraryEntry library in GetLibraries(config))
            {
                MethodInfo collectMethod =
                    typeof(MetaRefAnalysisUtil)
                    .GetMethod(nameof(CollectMetaRefsWithPaths), BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .MakeGenericMethod(library.GetType().GetGenericArguments()[1]);

                IEnumerable<KeyValuePair<object, object>> items = library.EnumerateAll();
                OrderedDictionary<object, List<ItemMetaRefEntry>> itemsMetaRefs = (OrderedDictionary<object, List<ItemMetaRefEntry>>)collectMethod.Invoke(null, new object[] { traversalResources, items });

                foreach ((object key, List<ItemMetaRefEntry> itemMetaRefs) in itemsMetaRefs)
                {
                    ConfigItemId referringId = new ConfigItemId(library.GetInfoByKey(key).GetType(), key);

                    OrderedSet<(ConfigItemId, string)> references = new OrderedSet<(ConfigItemId, string)>();

                    foreach (ItemMetaRefEntry entry in itemMetaRefs)
                    {
                        ConfigItemId referredId = new ConfigItemId(entry.MetaRef.ItemType, entry.MetaRef.KeyObject);
                        referredId = ((GameConfigBase)config).CanonicalizedConfigItemId(referredId);
                        references.Add((referredId, entry.VaguePath));
                    }

                    referencesPerItem.Add(referringId, references);
                }
            }

            return referencesPerItem;
        }

        static OrderedDictionary<ConfigItemId, OrderedSet<(ConfigItemId, string)>> ComputeReverseReferences(OrderedDictionary<ConfigItemId, OrderedSet<(ConfigItemId, string)>> references)
        {
            OrderedDictionary<ConfigItemId, OrderedSet<(ConfigItemId, string)>> reverseReferences = new OrderedDictionary<ConfigItemId, OrderedSet<(ConfigItemId, string)>>();

            foreach ((ConfigItemId referrerId, OrderedSet<(ConfigItemId, string)> referreds) in references)
            {
                foreach ((ConfigItemId referredId, string path) in referreds)
                {
                    if (!reverseReferences.TryGetValue(referredId, out OrderedSet<(ConfigItemId ReferrerId, string Path)> referrers))
                    {
                        referrers = new OrderedSet<(ConfigItemId, string)>();
                        reverseReferences.Add(referredId, referrers);
                    }

                    referrers.Add((referrerId, path));
                }
            }

            return reverseReferences;
        }

        struct ReachabilityResult<TNode, TEdgeLabel>
        {
            public OrderedSet<TNode> ReachableNodes;
            public OrderedDictionary<TEdgeLabel, int> EdgeInfluences;

            public ReachabilityResult(OrderedSet<TNode> reachableNodes, OrderedDictionary<TEdgeLabel, int> edgeInfluences)
            {
                ReachableNodes = reachableNodes;
                EdgeInfluences = edgeInfluences;
            }
        }

        delegate IEnumerable<(TNode, TEdgeLabel)> TryGetNodeNeighborsFunc<TNode, TEdgeLabel>(TNode node);

        static ReachabilityResult<TNode, TEdgeLabel> ResolveReachability<TNode, TEdgeLabel>(IEnumerable<TNode> startNodes, TryGetNodeNeighborsFunc<TNode, TEdgeLabel> tryGetNodeNeighbors)
        {
            // Breadth-first search.
            // In addition to computing the reachable nodes, also compute the "influence" for each "edge label".
            // "Edge label": each edge has a label; many edges can have the same label.
            // "Influence" of an edge label: how many nodes were reached via a path containing an edge with that label.
            // For each node we reach, we maintain the sequence of edge labels leading to it (excluding duplicates).
            // Assuming there are much fewer edge labels than edges, this should not be horribly inefficient.

            OrderedSet<TNode> reachable = new OrderedSet<TNode>(startNodes);
            Queue<TNode> queue = new Queue<TNode>(startNodes);
            Dictionary<TNode, IEnumerable<TEdgeLabel>> usedEdgeLabels = new Dictionary<TNode, IEnumerable<TEdgeLabel>>();

            foreach (TNode node in startNodes)
                usedEdgeLabels.Add(node, Enumerable.Empty<TEdgeLabel>());

            OrderedDictionary<TEdgeLabel, int> edgeInfluences = new OrderedDictionary<TEdgeLabel, int>();

            while (queue.TryDequeue(out TNode current))
            {
                IEnumerable<TEdgeLabel> currentUsedEdgeLabels = usedEdgeLabels[current];
                foreach (TEdgeLabel label in currentUsedEdgeLabels)
                    edgeInfluences[label] = 1 + edgeInfluences.GetValueOrDefault(label, 0);

                IEnumerable<(TNode, TEdgeLabel)> neighbors = tryGetNodeNeighbors(current);
                if (neighbors != null)
                {
                    foreach ((TNode neighbor, TEdgeLabel edgeLabel) in neighbors)
                    {
                        bool newlySeen = reachable.Add(neighbor);
                        if (newlySeen)
                        {
                            IEnumerable<TEdgeLabel> neighborUsedEdgeLabels;
                            if (!currentUsedEdgeLabels.Contains(edgeLabel))
                                neighborUsedEdgeLabels = Enumerable.Append(currentUsedEdgeLabels, edgeLabel);
                            else
                                neighborUsedEdgeLabels = currentUsedEdgeLabels;

                            queue.Enqueue(neighbor);
                            usedEdgeLabels.Add(neighbor, neighborUsedEdgeLabels);
                        }
                    }
                }
            }

            return new ReachabilityResult<TNode, TEdgeLabel>(reachable, edgeInfluences);
        }
    }
}
