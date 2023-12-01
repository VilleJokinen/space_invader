// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Player;
using Metaplay.Core.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Metaplay.Core.Config
{
    /// <summary>
    /// Specifies what parts of the deduplicated (i.e. shared across specialized config instances)
    /// config content a config instance owns.
    /// <para>
    /// Owning the content means the config instance is allowed to mutate it
    /// during its config importing process. Specifically, it may mutate it
    /// in order to duplicate config items that are "indirectly" patched due
    /// to MetaRefs, and in order to resolve MetaRefs in the config data,
    /// and similar purposes.
    /// </para>
    /// <para>
    /// For normal config instances at runtime, this will typically be
    /// <see cref="None"/>, the other ownership types being intended for
    /// when the <see cref="GameConfigImportResources"/> itself is being constructed.
    /// See notes there.
    /// </para>
    /// </summary>
    public enum GameConfigDeduplicationOwnership
    {
        /// <summary>
        /// The game config instance does not own any of the deduplicated storage.
        /// </summary>
        None = 0,
        /// <summary>
        /// The game config instance owns the baseline parts of the deduplicated storage.
        /// </summary>
        Baseline,
        /// <summary>
        /// The game config instance owns the parts of the deduplicated storage
        /// which belong to a single patch. The patch id is known in the context
        /// (e.g. <see cref="GameConfigImportParams.ActivePatches"/>).
        /// </summary>
        SinglePatch,
    }

    /// <summary>
    /// Identifies a patch within a particular <see cref="GameConfigLibraryDeduplicationStorage{TKey, TInfo}"/>.
    /// Within a deduplication storage, patches are assigned simple index numbers, allowing certain
    /// operations to be more efficient than if the patches were always referred to by <see cref="ExperimentVariantPair"/>.
    /// In particular <see cref="ConfigPatchIdSet"/> membership testing can now use a bool array,
    /// instead of a hash-set of <see cref="ExperimentVariantPair"/> which would be more expensive (hashing etc).
    /// </summary>
    public readonly struct ConfigPatchIndex : IEquatable<ConfigPatchIndex>
    {
        public readonly int Value;

        public ConfigPatchIndex(int value)
        {
            Value = value;
        }

        public override bool Equals(object obj) => obj is ConfigPatchIndex other && Equals(other);
        public bool Equals(ConfigPatchIndex other) => Value == other.Value;
        public override int GetHashCode() => Value.GetHashCode();
        public static bool operator ==(ConfigPatchIndex left, ConfigPatchIndex right) => left.Equals(right);
        public static bool operator !=(ConfigPatchIndex left, ConfigPatchIndex right) => !(left == right);
    }


    /// <summary>
    /// Identifies a set of patches in a deduplication storage.
    /// This is valid within a particular <see cref="GameConfigLibraryDeduplicationStorage{TKey, TInfo}"/>,
    /// as it relies on the <see cref="ConfigPatchIndex"/> values assigned to the patches.
    /// </summary>
    public class ConfigPatchIdSet
    {
        readonly List<ConfigPatchIndex> _presentPatchIndexes;
        readonly bool[] _patchIsPresent;

        public ConfigPatchIdSet(OrderedSet<ExperimentVariantPair> presentPatchIds, OrderedDictionary<ExperimentVariantPair, ConfigPatchIndex> allPatchIndexes)
        {
            _presentPatchIndexes = new List<ConfigPatchIndex>(capacity: presentPatchIds.Count);
            int totalNumPatches = allPatchIndexes.Count;
            _patchIsPresent = new bool[totalNumPatches];
            foreach (ExperimentVariantPair patchId in presentPatchIds)
            {
                ConfigPatchIndex patchIndex = allPatchIndexes[patchId];
                _presentPatchIndexes.Add(patchIndex);
                _patchIsPresent[patchIndex.Value] = true;
            }
        }

        public bool Contains(ConfigPatchIndex patchIndex) => _patchIsPresent != null && _patchIsPresent[patchIndex.Value];

        public ConfigPatchIndex Single() => _presentPatchIndexes.Single();
        public IEnumerable<ConfigPatchIndex> Enumerate() => _presentPatchIndexes ?? Enumerable.Empty<ConfigPatchIndex>();

        public int Count => _presentPatchIndexes?.Count ?? 0;
    }

    /// <summary>
    /// Untyped base interface for <see cref="GameConfigLibraryDeduplicationStorage{TKey, TInfo}"/>.
    /// This is empty and technically useless, used only for clarity in some places (instead of using just <c>object</c>).
    /// </summary>
    public interface IGameConfigLibraryDeduplicationStorage
    {
    }

    /// <summary>
    /// Holds the data that is deduplicated (i.e. shared) between multiple specializations of a <see cref="GameConfigLibrary{TKey, TInfo}"/>.
    /// For each item, this holds the baseline instance of that item, as well the overrides of that item due to patches.
    /// </summary>
    public class GameConfigLibraryDeduplicationStorage<TKey, TInfo> : IGameConfigLibraryDeduplicationStorage
        where TInfo : class, IGameConfigData<TKey>, new()
    {
        public readonly OrderedDictionary<ExperimentVariantPair, ConfigPatchIndex> PatchIdToIndex;
        public readonly OrderedDictionary<ConfigPatchIndex, ExperimentVariantPair> PatchIndexToId;

        public readonly OrderedDictionary<TKey, GameConfigLibraryPatchedItemEntry<TInfo>> PatchedItemEntries = new OrderedDictionary<TKey, GameConfigLibraryPatchedItemEntry<TInfo>>();
        public int NumBaselineItems;

        public readonly OrderedDictionary<ConfigPatchIndex, PatchInfo> PatchInfos = new OrderedDictionary<ConfigPatchIndex, PatchInfo>();

        public class PatchInfo
        {
            public OrderedSet<TKey> DirectlyPatchedItems = new OrderedSet<TKey>();
            public OrderedSet<TKey> IndirectlyPatchedItems = new OrderedSet<TKey>();
            public OrderedSet<TKey> AppendedItems = new OrderedSet<TKey>();
        }

        public GameConfigLibraryDeduplicationStorage(IEnumerable<ExperimentVariantPair> allPatchIds)
        {
            PatchIdToIndex = new OrderedDictionary<ExperimentVariantPair, ConfigPatchIndex>();
            PatchIndexToId = new OrderedDictionary<ConfigPatchIndex, ExperimentVariantPair>();

            foreach ((ExperimentVariantPair patchId, int index) in allPatchIds.ZipWithIndex())
            {
                ConfigPatchIndex patchIndex = new ConfigPatchIndex(index);
                PatchIdToIndex.Add(patchId, patchIndex);
                PatchIndexToId.Add(patchIndex, patchId);
            }
        }

        public ConfigPatchIdSet CreatePatchIdSet(OrderedSet<ExperimentVariantPair> activePatchIds)
        {
            return new ConfigPatchIdSet(activePatchIds, PatchIdToIndex);
        }

        /// <summary>
        /// Get the item instance for <paramref name="key"/> for the specialization represented by <paramref name="activePatches"/>,
        /// or null if no such item exists in that specialization.
        /// </summary>
        public TInfo TryGetItem(TKey key, ConfigPatchIdSet activePatches)
        {
            if (PatchedItemEntries.TryGetValue(key, out GameConfigLibraryPatchedItemEntry<TInfo> patchedItemEntry))
                return patchedItemEntry.TryGetItem(activePatches);
            else
                return null;
        }

        /// <summary>
        /// Adds <see cref="items"/> as baseline item instances.
        /// </summary>
        /// <remarks>
        /// This should be called before any <see cref="PopulatePatch"/>
        /// </remarks>
        public void PopulateBaseline(OrderedDictionary<TKey, TInfo> baselineItems)
        {
            if (PatchedItemEntries.Count != 0)
                throw new InvalidOperationException($"{nameof(PopulateBaseline)} called more than once");

            OrderedDictionary<TKey, OrderedSet<ConfigItemId>> references = GameConfigUtil.CollectReferencesFromItems<TKey, TInfo>(baselineItems.Values.ToList());

            foreach ((TKey key, TInfo info) in baselineItems)
            {
                PatchedItemEntries.Add(key, new GameConfigLibraryPatchedItemEntry<TInfo>(
                    baselineMaybe: new GameConfigLibraryPatchedItemEntry<TInfo>.ItemData(
                        info,
                        references.GetValueOrDefault(key)),
                    patchOverridesMaybe: null));
            }

            NumBaselineItems = baselineItems.Count;
        }

        /// <summary>
        /// Adds the items in <paramref name="patch"/> as patched item instances,
        /// belonging to patch index <paramref name="patchIndex"/>.
        /// </summary>
        /// <remarks>
        /// This only deals with directly-patched items.
        /// Indirect patching is done with <see cref="DuplicateIndirectlyPatchedItems"/>
        /// (the indirectly-patched items are determined in <see cref="GameConfigBase.DuplicateItemsDueToReferences"/>).
        /// </remarks>
        public void PopulatePatch(ConfigPatchIndex patchIndex, GameConfigLibraryPatch<TKey, TInfo> patch)
        {
            IEnumerable<KeyValuePair<TKey, TInfo>> allItemsInPatch =
                patch.EnumerateReplacedItems()
                .Concat(patch.EnumerateAppendedItems());

            OrderedDictionary<TKey, OrderedSet<ConfigItemId>> references = GameConfigUtil.CollectReferencesFromItems<TKey, TInfo>(allItemsInPatch.Select(kv => kv.Value).ToList());

            PatchInfo patchInfo = new PatchInfo();
            PatchInfos.Add(patchIndex, patchInfo);

            foreach ((TKey key, TInfo info) in patch.EnumerateReplacedItems())
            {
                if (!PatchedItemEntries.TryGetValue(key, out GameConfigLibraryPatchedItemEntry<TInfo> entry)
                    || entry.BaselineMaybe == null)
                {
                    throw new InvalidOperationException($"Item '{key}' is among the item replacements in patch {PatchIndexToId[patchIndex]}, but that item doesn't exist in the baseline");
                }

                entry.AddPatchOverride(new GameConfigLibraryPatchedItemEntry<TInfo>.PatchOverride(
                    patchIndex,
                    new GameConfigLibraryPatchedItemEntry<TInfo>.ItemData(
                        info,
                        references.GetValueOrDefault(key)),
                    isDirectlyPatched: true));

                patchInfo.DirectlyPatchedItems.Add(key);
            }

            foreach ((TKey key, TInfo info) in patch.EnumerateAppendedItems())
            {
                if (PatchedItemEntries.TryGetValue(key, out GameConfigLibraryPatchedItemEntry<TInfo> entry)
                    && entry.BaselineMaybe != null)
                {
                    throw new InvalidOperationException($"Item '{key}' is among the appended items in patch {PatchIndexToId[patchIndex]}, but that item already exists in the baseline");
                }

                if (entry == null)
                {
                    entry = new GameConfigLibraryPatchedItemEntry<TInfo>(baselineMaybe: null, patchOverridesMaybe: null);
                    PatchedItemEntries.Add(key, entry);
                }

                entry.AddPatchOverride(new GameConfigLibraryPatchedItemEntry<TInfo>.PatchOverride(
                    patchIndex,
                    new GameConfigLibraryPatchedItemEntry<TInfo>.ItemData(
                        info,
                        references.GetValueOrDefault(key)),
                    isDirectlyPatched: true));

                patchInfo.DirectlyPatchedItems.Add(key);

                patchInfo.AppendedItems.Add(key);
            }
        }

        /// <summary>
        /// Clones the baseline items identified by <paramref name="keys"/> and adds those clones as overrides
        /// for patch <paramref name="patchIndex"/>.
        /// Each override will represent an "indirectly-patched" item, which means that it wasn't directly
        /// targeted by the patch but needed to be duplicated because it (possibly transitively) refers to a
        /// directly-patched item.
        /// </summary>
        /// <remarks>
        /// If the given patch already has an override for a given item, then nothing is done for that item.
        /// </remarks>
        public void DuplicateIndirectlyPatchedItems(ConfigPatchIndex patchIndex, IEnumerable<TKey> keys)
        {
            PatchInfo patchInfo = PatchInfos[patchIndex];

            List<TInfo> originalItems = new List<TInfo>();
            List<OrderedSet<ConfigItemId>> itemReferences = new List<OrderedSet<ConfigItemId>>();
            foreach (TKey key in keys)
            {
                if (patchInfo.DirectlyPatchedItems.Contains(key))
                {
                    // Item already belongs to this patch.
                    // This can happen due to reference cycles in a patch.
                    // GameConfigBase.DuplicateItemsDueToReferences ends up calling this method
                    // simply based on MetaRef propagation, without omitting those items
                    // that were already directly patched by this patch. That's ok.
                    // This is easier to check and tolerate here.
                    continue;
                }

                GameConfigLibraryPatchedItemEntry<TInfo> entry = PatchedItemEntries[key];

                // Indirectly-patched instances are clones of the baseline.
                GameConfigLibraryPatchedItemEntry<TInfo>.ItemData baseline = entry.BaselineMaybe;
                originalItems.Add(baseline.Item);
                // ReferencesFromThisItemMaybe do not need to be re-collected from the clone, can just re-use those from the baseline.
                itemReferences.Add(baseline.ReferencesFromThisItemMaybe);
            }

            IReadOnlyList<TInfo> clonedItems = MetaSerialization.CloneTableTagged(originalItems, MetaSerializationFlags.IncludeAll, logicVersion: null, resolver: null, maxCollectionSizeOverride: int.MaxValue);

            foreach ((TInfo clonedItem, OrderedSet<ConfigItemId> references) in clonedItems.Zip(itemReferences, (x, y) => (x, y)))
            {
                TKey key = clonedItem.ConfigKey;

                PatchedItemEntries[key].AddPatchOverride(new GameConfigLibraryPatchedItemEntry<TInfo>.PatchOverride(
                    patchIndex,
                    new GameConfigLibraryPatchedItemEntry<TInfo>.ItemData(
                        clonedItem,
                        references),
                    isDirectlyPatched: false));

                patchInfo.IndirectlyPatchedItems.Add(key);
            }
        }

        /// <summary>
        /// Returns the baseline item instances.
        /// </summary>
        public IEnumerable<TInfo> EnumerateItemsBelongingToBaseline()
        {
            return PatchedItemEntries.Values
                .Select(entry => entry.BaselineMaybe?.Item)
                .Where(item => item != null);
        }

        /// <summary>
        /// Returns the items belonging to the given patch.
        /// Items that are not overridden by the patch (either directly or indirectly)
        /// are not returned.
        /// </summary>
        /// <remarks>
        /// The order in which the items are returned should be considered unspecified.
        /// In particular, it is not guaranteed to be the order in which the items appear
        /// in the config.
        /// </remarks>
        public IEnumerable<TInfo> EnumerateItemsBelongingToPatch(ConfigPatchIndex patchIndex)
        {
            PatchInfo patchInfo = PatchInfos[patchIndex];
            return patchInfo.DirectlyPatchedItems
                .Concat(patchInfo.IndirectlyPatchedItems)
                .Select(key => PatchedItemEntries[key].GetItemBelongingToPatch(patchIndex));
        }
    }

    /// <summary>
    /// Holds multiple copies of a config item:
    /// baseline, and overrides per single patch.
    /// <para>
    /// For each item, in addition to holding the config date item itself, this also holds
    /// result of <see cref="GameConfigUtil.TryCollectReferencesFromItem"/> for that item.
    /// This is an optimization to allow re-using that data without collecting it from each item more than once.
    /// That reference information is used by <see cref="GameConfigBase.DuplicateItemsDueToReferences"/>.
    /// </para>
    /// </summary>
    public class GameConfigLibraryPatchedItemEntry<TInfo>
        where TInfo : class
    {
        /// <summary>
        /// Baseline item, or null if this item has no baseline (i.e. is appended by a patch).
        /// </summary>
        public readonly ItemData BaselineMaybe;
        /// <summary>
        /// This has an entry for each patch which affects this item.
        /// If this item is not affected by any patch, this is null (to avoid
        /// empty list allocation in the common case).
        /// </summary>
        public List<PatchOverride> PatchOverridesMaybe;

        public GameConfigLibraryPatchedItemEntry(ItemData baselineMaybe, List<PatchOverride> patchOverridesMaybe)
        {
            BaselineMaybe = baselineMaybe;
            PatchOverridesMaybe = patchOverridesMaybe;
        }

        public void AddPatchOverride(PatchOverride patchOverride)
        {
            if (PatchOverridesMaybe == null)
                PatchOverridesMaybe = new List<PatchOverride> { patchOverride };
            else
                PatchOverridesMaybe.Add(patchOverride);
        }

        public class ItemData
        {
            public readonly TInfo Item;
            /// <summary>
            /// Result of <see cref="GameConfigUtil.TryCollectReferencesFromItem"/> for <see cref="Item"/>.
            /// </summary>
            public readonly OrderedSet<ConfigItemId> ReferencesFromThisItemMaybe;

            public ItemData(TInfo item, OrderedSet<ConfigItemId> referencesFromThisItemMaybe)
            {
                Item = item;
                ReferencesFromThisItemMaybe = referencesFromThisItemMaybe;
            }
        }

        public class PatchOverride
        {
            /// <summary>
            /// The patch that this item belongs to.
            /// </summary>
            public readonly ConfigPatchIndex PatchIndex;
            public readonly ItemData Data;
            /// <summary>
            /// Whether the item was directly targeted by the patch, as opposed to being
            /// duplicated due to referring to patched items. This distinction is used
            /// when getting an item instance for a multi-patch specialization
            /// (see <see cref="TryGetItemAndDefiningPatch"/>, which implements <see cref="TryGetItem"/>).
            /// </summary>
            public readonly bool IsDirectlyPatched;

            public PatchOverride(ConfigPatchIndex patchIndex, ItemData data, bool isDirectlyPatched)
            {
                PatchIndex = patchIndex;
                Data = data;
                IsDirectlyPatched = isDirectlyPatched;
            }
        }

        /// <summary>
        /// Get the item instance for the specialization represented by <paramref name="activePatches"/>,
        /// or null if the item isn't defined for that specialization.
        /// <para>
        /// - If the item is not affected by any patch in the specialization, then the baseline item is returned.
        /// - Otherwise, this first prioritizes directly-patched items, and only returns an indirectly-patched
        ///   item if there are no directly-patched ones. Among the directly-patched items, the one returned
        ///   is the one that appears latest in <see cref="PatchOverridesMaybe"/>, i.e. the one that was added last.
        ///   Similarly for indirectly-patched items.
        /// These details about prioritizing directly-patched items has to do with how these items are accessed
        /// by <see cref="GameConfigBase.DuplicateItemsDueToReferences"/>, which happens before the duplication
        /// propagation has been done. If an item has a conflict between a direct and indirect patch, or between
        /// multiple indirect patches, it may need to be duplicated, which the logic in
        /// <see cref="GameConfigBase.DuplicateItemsDueToReferences"/> takes care of. Such duplicated items for
        /// multi-patch specializations will be placed in <see cref="GameConfigLibrary{TKey, TInfo}._exclusivelyOwnedItems"/>
        /// and will not be accessed from the deduplication storage at runtime.
        /// </para>
        /// </summary>
        public TInfo TryGetItem(ConfigPatchIdSet activePatches)
        {
            return TryGetItemData(activePatches)?.Item;
        }

        /// <summary>
        /// Like <see cref="TryGetItem"/> but returns <see cref="ItemData.ReferencesFromThisItemMaybe"/>
        /// (which may be null) instead, and throws if the item isn't defined in the given specialization.
        /// </summary>
        public OrderedSet<ConfigItemId> GetReferencesOrNullFromItem(ConfigPatchIdSet activePatches)
        {
            ItemData itemData = TryGetItemData(activePatches);
            if (itemData == null)
                throw new InvalidOperationException("Item is not defined in the given specialization");

            return itemData.ReferencesFromThisItemMaybe;
        }

        /// <summary>
        /// Internal implementation for <see cref="TryGetItem"/>, <see cref="GetReferencesOrNullFromItem"/>.
        /// </summary>
        ItemData TryGetItemData(ConfigPatchIdSet activePatches)
        {
            PatchOverride patchOverrideMaybe = TryGetPatchOverride(activePatches);
            if (patchOverrideMaybe != null)
                return patchOverrideMaybe.Data;
            else
                return BaselineMaybe;
        }

        /// <summary>
        /// Determines which patch defines the item, in the give specialization:
        /// If the item is defined by a patch, this returns the index of that patch.
        /// If the item is defined by the baseline, this returns null.
        ///
        /// If the item is affected by multiple patches, the behavior is a bit complex.
        /// See comment on <see cref="TryGetItem"/> for more elaboration.
        /// </summary>
        /// <param name="key">
        /// The key of the item whose definer to determine.
        /// </param>
        public ConfigPatchIndex? GetItemDefinerPatchOrNullForBaseline(ConfigPatchIdSet activePatches)
        {
            PatchOverride patchOverrideMaybe = TryGetPatchOverride(activePatches);
            if (patchOverrideMaybe != null)
                return patchOverrideMaybe.PatchIndex;
            else if (BaselineMaybe != null)
                return null;
            else
                throw new InvalidOperationException("Item is not defined in the given specialization");
        }

        PatchOverride TryGetPatchOverride(ConfigPatchIdSet activePatches)
        {
            if (PatchOverridesMaybe != null
                && activePatches.Count > 0) // \note Fast-path for baseline
            {
                // Search among directly-patched items first.
                for (int i = PatchOverridesMaybe.Count - 1; i >= 0; i--)
                {
                    PatchOverride patchOverride = PatchOverridesMaybe[i];

                    if (patchOverride.IsDirectlyPatched
                        && activePatches.Contains(patchOverride.PatchIndex))
                    {
                        return patchOverride;
                    }
                }

                // Only then search among indirectly-patched items.
                for (int i = PatchOverridesMaybe.Count - 1; i >= 0; i--)
                {
                    PatchOverride patchOverride = PatchOverridesMaybe[i];

                    if (!patchOverride.IsDirectlyPatched
                        && activePatches.Contains(patchOverride.PatchIndex))
                    {
                        return patchOverride;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Gets the item belonging to the given patch.
        /// </summary>
        public TInfo GetItemBelongingToPatch(ConfigPatchIndex patchIndex)
        {
            if (PatchOverridesMaybe != null)
            {
                foreach (PatchOverride patchOverride in PatchOverridesMaybe)
                {
                    if (patchOverride.PatchIndex == patchIndex)
                        return patchOverride.Data.Item;
                }
            }

            throw new InvalidOperationException("Item is not defined in the given patch");
        }
    }

    /// <summary>
    /// Deduplicated storage for the top-level <see cref="GameConfigBase"/> (rather than per library like <see cref="GameConfigLibraryDeduplicationStorage{TKey, TInfo}"/>.
    /// </summary>
    public class GameConfigTopLevelDeduplicationStorage
    {
        public OrderedDictionary<ConfigItemId, OrderedSet<ConfigItemId>> BaselineReferences = null;
        public OrderedDictionary<ConfigItemId, OrderedSet<ConfigItemId>> BaselineReverseReferences = null;
    }
}
