// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.InAppPurchase;
using Metaplay.Core.Localization;
using Metaplay.Core.Offers;
using Metaplay.Core.Player;
using System.Collections.Generic;

namespace Metaplay.Core.Config
{
    public interface IGameConfig : IGameConfigDataResolver
    {
        void Import(GameConfigImportParams importParams);
        void OnConfigEntriesPopulated(bool isBuildingConfigs = false);
        void BuildTimeValidate(GameConfigValidationResult validationResult);
        ConfigArchiveEntry[] ExportMpcArchiveEntries();
    }

    /// <summary>
    /// Base interface for game-specific SharedGameConfig classes.
    /// </summary>
    public interface ISharedGameConfig : IGameConfig
    {
        IGameConfigLibrary<LanguageId, LanguageInfo> Languages { get; }

        IGameConfigLibrary<InAppProductId, InAppProductInfoBase> InAppProducts { get; }

        IGameConfigLibrary<PlayerSegmentId, PlayerSegmentInfoBase> PlayerSegments { get; }

        IGameConfigLibrary<MetaOfferId, MetaOfferInfoBase> MetaOffers { get; }
        IGameConfigLibrary<MetaOfferGroupId, MetaOfferGroupInfoBase> MetaOfferGroups { get; }
        OrderedDictionary<OfferPlacementId, List<MetaOfferGroupInfoBase>> MetaOfferGroupsPerPlacementInMostImportantFirstOrder { get; }
        OrderedDictionary<MetaOfferId, List<MetaOfferGroupInfoBase>> MetaOfferContainingGroups { get; }
    }

    /// <summary>
    /// Base interface for game-specific ServerGameConfig classes.
    /// </summary>
    public interface IServerGameConfig : IGameConfig
    {
        IGameConfigLibrary<PlayerExperimentId, PlayerExperimentInfo> PlayerExperiments { get; }
    }
}
