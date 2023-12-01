// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Config;
using Metaplay.Core.Player;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Metaplay.Core.Activables
{
    public struct MetaActivableKey : IEquatable<MetaActivableKey>
    {
        public MetaActivableKindId  KindId;
        public object               ActivableId;

        public MetaActivableKey(MetaActivableKindId kindId, object activableId)
        {
            KindId = kindId ?? throw new ArgumentNullException(nameof(kindId));
            ActivableId = activableId ?? throw new ArgumentNullException(nameof(activableId));
        }

        public bool Equals(MetaActivableKey other)
        {
            return KindId == other.KindId
                && ActivableIdEquals(ActivableId, other.ActivableId);
        }

        static bool ActivableIdEquals(object a, object b)
        {
            if (a is null)
                return b is null;
            return a.Equals(b);
        }

        public override bool Equals(object obj) => obj is MetaActivableKey key && Equals(key);
        public override int GetHashCode()
        {
            // \note Tolerating null KindId and ActivableId here, in case this MetaActivableKey struct was default-constructed.
            return Util.CombineHashCode(KindId?.GetHashCode() ?? 0, ActivableId?.GetHashCode() ?? 0);
        }

        public override string ToString()
        {
            string activableIdStr = ActivableId == null ? null : Util.ObjectToStringInvariant(ActivableId);
            return $"{KindId}/{activableIdStr}";
        }
    }

    public static class MetaActivableUtil
    {
        public static void CheckRepositoryInitialized()
        {
            if (MetaActivableRepository.Instance == null)
                throw new InvalidOperationException($"{nameof(MetaActivableRepository)} must be initialized before using {nameof(MetaActivableUtil)}");
        }

        public static IGameConfigLibraryEntry TryGetGameConfigLibraryForKind(MetaActivableKindId kindId, ISharedGameConfig config)
        {
            CheckRepositoryInitialized();

            MetaActivableRepository.KindSpec kind = MetaActivableRepository.Instance.AllKinds[kindId];
            if (kind.GameConfigLibrary == null)
                return null;

            return kind.GameConfigLibrary.GetMemberValue(config);
        }

        public static IGameConfigLibraryEntry GetGameConfigLibraryForKind(MetaActivableKindId kindId, ISharedGameConfig config)
        {
            return TryGetGameConfigLibraryForKind(kindId, config)
                   ?? throw new InvalidOperationException($"Config library for {MetaActivableRepository.Instance.AllKinds[kindId].DisplayName} not found in {config.GetType().Name}");
        }

        public static IMetaActivableSet GetPlayerActivableSetForKind(MetaActivableKindId kindId, IPlayerModelBase player)
        {
            CheckRepositoryInitialized();

            MetaActivableRepository.KindSpec kind = MetaActivableRepository.Instance.AllKinds[kindId];
            if (!kind.PlayerSubModel.TryGetMemberValue(player, out IMetaActivableSet activableSet))
                throw new InvalidOperationException($"Model (state) for {kind.DisplayName} not found in {player.GetType().Name}");

            return activableSet;
        }

        public static IEnumerable<object> GetActivableIdsOfKind(MetaActivableKindId kindId, ISharedGameConfig config)
        {
            IGameConfigLibraryEntry library = TryGetGameConfigLibraryForKind(kindId, config);
            if (library == null)
                return Enumerable.Empty<object>();

            return library.EnumerateAll().Select(kv => kv.Key);
        }

        public static IEnumerable<MetaActivableKindId> GetKindIdsInCategory(MetaActivableCategoryId categoryId)
        {
            CheckRepositoryInitialized();

            MetaActivableRepository.CategorySpec category = MetaActivableRepository.Instance.AllCategories[categoryId];
            return category.Kinds.Select(kind => kind.Id);
        }

        public static IEnumerable<MetaActivableKey> GetActivableKeysOfKind(MetaActivableKindId kindId, ISharedGameConfig config)
        {
            return GetActivableIdsOfKind(kindId, config).Select(activableId => new MetaActivableKey(kindId, activableId));
        }

        public static IEnumerable<MetaActivableKey> GetActivableKeysInCategory(MetaActivableCategoryId categoryId, ISharedGameConfig config)
        {
            return GetKindIdsInCategory(categoryId).SelectMany(kindId => GetActivableKeysOfKind(kindId, config));
        }

        public static IMetaActivableConfigData GetActivableGameConfigData(MetaActivableKey activableKey, ISharedGameConfig config)
        {
            IGameConfigLibraryEntry library = GetGameConfigLibraryForKind(activableKey.KindId, config);
            return (IMetaActivableConfigData)library.GetInfoByKey(activableKey.ActivableId);
        }

        public static bool TryGetVisibleStatus(MetaActivableKey activableKey, IPlayerModelBase player, ISharedGameConfig config, out MetaActivableVisibleStatus visibleStatus)
        {
            IMetaActivableSet           playerActivables    = GetPlayerActivableSetForKind(activableKey.KindId, player);
            IMetaActivableConfigData    activableInfo       = GetActivableGameConfigData(activableKey, config);

            return playerActivables.TryGetVisibleStatus(activableInfo, player, out visibleStatus);
        }
    }
}
