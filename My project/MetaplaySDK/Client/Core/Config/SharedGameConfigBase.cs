// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.InAppPurchase;
using Metaplay.Core.Localization;
using Metaplay.Core.Offers;
using Metaplay.Core.Player;
using System.Collections.Generic;
using System.Linq;

namespace Metaplay.Core.Config
{
    public class SharedGameConfigBase : GameConfigBase, ISharedGameConfig
    {
        #region Stub SDK libraries
        // Default languages config has english only
        static readonly IGameConfigLibrary<LanguageId, LanguageInfo>                 _defaultLanguages      = new BuiltinGameConfigLibrary<LanguageId, LanguageInfo>(Enumerable.Repeat(new LanguageInfo(LanguageId.FromString("en"), "English"), 1));
        static readonly IGameConfigLibrary<InAppProductId, InAppProductInfoBase>     _defaultInAppProducts  = new EmptyGameConfigLibrary<InAppProductId, InAppProductInfoBase>();
        static readonly IGameConfigLibrary<PlayerSegmentId, PlayerSegmentInfoBase>   _defaultPlayerSegments = new EmptyGameConfigLibrary<PlayerSegmentId, PlayerSegmentInfoBase>();
        static readonly IGameConfigLibrary<MetaOfferId, MetaOfferInfoBase>           _defaultOffers         = new EmptyGameConfigLibrary<MetaOfferId, MetaOfferInfoBase>();
        static readonly IGameConfigLibrary<MetaOfferGroupId, MetaOfferGroupInfoBase> _defaultOfferGroups    = new EmptyGameConfigLibrary<MetaOfferGroupId, MetaOfferGroupInfoBase>();
        #endregion

        #region SDK library implementation selectors
        // The backing fields for the ISharedGameConfig library getters, initialized in RegisterSDKIntegrations()
        IGameConfigLibrary<LanguageId, LanguageInfo>                 _languagesIntegration = null;
        IGameConfigLibrary<InAppProductId, InAppProductInfoBase>     _inAppProductsIntegration = null;
        IGameConfigLibrary<PlayerSegmentId, PlayerSegmentInfoBase>   _playerSegmentsIntegration = null;
        IGameConfigLibrary<MetaOfferId, MetaOfferInfoBase>           _offersIntegration = null;
        IGameConfigLibrary<MetaOfferGroupId, MetaOfferGroupInfoBase> _offerGroupsIntegration = null;
        #endregion

        #region ISharedGameConfig explicit implementations
        IGameConfigLibrary<LanguageId, LanguageInfo> ISharedGameConfig.                Languages       => _languagesIntegration;
        IGameConfigLibrary<InAppProductId, InAppProductInfoBase> ISharedGameConfig.    InAppProducts   => _inAppProductsIntegration;
        IGameConfigLibrary<PlayerSegmentId, PlayerSegmentInfoBase> ISharedGameConfig.  PlayerSegments  => _playerSegmentsIntegration;
        IGameConfigLibrary<MetaOfferId, MetaOfferInfoBase> ISharedGameConfig.          MetaOffers      => _offersIntegration;
        IGameConfigLibrary<MetaOfferGroupId, MetaOfferGroupInfoBase> ISharedGameConfig.MetaOfferGroups => _offerGroupsIntegration;
        #endregion

        // Derived (redundant) data related to MetaOffers and MetaOfferGroups.
        public OrderedDictionary<OfferPlacementId, List<MetaOfferGroupInfoBase>> MetaOfferGroupsPerPlacementInMostImportantFirstOrder { get; private set; }
        public OrderedDictionary<MetaOfferId, List<MetaOfferGroupInfoBase>>      MetaOfferContainingGroups                            { get; private set; }

        void InitializeMetaOfferDerivedData()
        {
            // Create MetaOfferGroupsPerPlacementInMostImportantFirstOrder: placement -> [group]  mapping, where the group list is in priority order.
            MetaOfferGroupsPerPlacementInMostImportantFirstOrder =
                _offerGroupsIntegration.Values
                    .GroupBy(offerGroup => offerGroup.Placement)
                    .ToOrderedDictionary(
                        g => g.Key,
                        g => g.OrderBy(offerGroup => offerGroup.Priority).ToList());

            // Create MetaOfferContainingGroups: offer -> [group]  mapping.
            MetaOfferContainingGroups = new OrderedDictionary<MetaOfferId, List<MetaOfferGroupInfoBase>>();
            foreach (MetaOfferId offerId in _offersIntegration.Keys)
                MetaOfferContainingGroups.Add(offerId, new List<MetaOfferGroupInfoBase>());
            foreach (MetaOfferGroupInfoBase groupInfo in _offerGroupsIntegration.Values)
            {
                foreach (MetaOfferInfoBase offerInfo in groupInfo.Offers.MetaRefUnwrap())
                    MetaOfferContainingGroups[offerInfo.OfferId].Add(groupInfo);
            }
        }

        protected override sealed void RegisterSDKIntegrations()
        {
            _languagesIntegration = RegisterIntegration("Languages", _defaultLanguages);
            _inAppProductsIntegration = RegisterIntegration("InAppProducts", _defaultInAppProducts);
            _playerSegmentsIntegration = RegisterIntegration("PlayerSegments", _defaultPlayerSegments);
            _offersIntegration = RegisterIntegration("Offers", _defaultOffers);
            _offerGroupsIntegration = RegisterIntegration("OfferGroups", _defaultOfferGroups);
        }

        protected override void OnLoaded()
        {
            base.OnLoaded();
            InitializeMetaOfferDerivedData();
        }

        public override void BuildTimeValidate(GameConfigValidationResult validationResult)
        {
            base.BuildTimeValidate(validationResult);

            PlayerSegmentInfoBase.CheckInternalReferences(_playerSegmentsIntegration);
        }

        protected override void Validate()
        {
            base.Validate();

            PlayerSegmentInfoBase.CheckInternalReferences(_playerSegmentsIntegration);
        }
    }

    public abstract class LegacySharedGameConfigTemplate<
        TInAppProductInfo,
        TPlayerSegmentInfo,
        TMetaOfferInfo,
        TMetaOfferGroupInfo
        > : SharedGameConfigBase
        where TInAppProductInfo : InAppProductInfoBase, new()
        where TPlayerSegmentInfo : PlayerSegmentInfoBase, new()
        where TMetaOfferInfo : MetaOfferInfoBase, new()
        where TMetaOfferGroupInfo : MetaOfferGroupInfoBase, new()
    {
        [GameConfigEntry("Languages")]
        [GameConfigSyntaxAdapter(headerReplaces: new string[] { "LanguageId -> LanguageId #key" })]
        public GameConfigLibrary<LanguageId, LanguageInfo> Languages { get; protected set; } = new GameConfigLibrary<LanguageId, LanguageInfo>();

        [GameConfigEntry("InAppProducts", requireArchiveEntry: false)]
        [GameConfigSyntaxAdapter(headerReplaces: new string[] { "ProductId -> ProductId #key" })]
        public GameConfigLibrary<InAppProductId, TInAppProductInfo> InAppProducts { get; protected set; } = new GameConfigLibrary<InAppProductId, TInAppProductInfo>();

        [GameConfigEntry("PlayerSegments", requireArchiveEntry: false)]
        [GameConfigSyntaxAdapter(headerReplaces: new string[] { "SegmentId -> SegmentId #key" })]
        [GameConfigEntryTransform(typeof(PlayerSegmentBasicInfoSourceItemBase<>))]
        public GameConfigLibrary<PlayerSegmentId, TPlayerSegmentInfo> PlayerSegments { get; protected set; } = new GameConfigLibrary<PlayerSegmentId, TPlayerSegmentInfo>();

        [GameConfigEntry("Offers", requireArchiveEntry: false)]
        [GameConfigSyntaxAdapter(headerReplaces: new string[] { "OfferId -> OfferId #key" })]
        [GameConfigEntryTransform(typeof(MetaOfferSourceConfigItemBase<>))]
        public GameConfigLibrary<MetaOfferId, TMetaOfferInfo> Offers { get; protected set; } = new GameConfigLibrary<MetaOfferId, TMetaOfferInfo>();

        [GameConfigEntry("OfferGroups", requireArchiveEntry: false)]
        [GameConfigSyntaxAdapter(headerReplaces: new string[] { "GroupId -> GroupId #key" })]
        [GameConfigSyntaxAdapter(headerReplaces: new string[] { "#StartDate -> Schedule.Start.Date", "#StartTime -> Schedule.Start.Time" }, headerPrefixReplaces: new string[] { "# -> Schedule." })]
        [GameConfigEntryTransform(typeof(MetaOfferGroupSourceConfigItemBase<>))]
        public GameConfigLibrary<MetaOfferGroupId, TMetaOfferGroupInfo> OfferGroups { get; protected set; } = new GameConfigLibrary<MetaOfferGroupId, TMetaOfferGroupInfo>();
    }

    public abstract class LegacySharedGameConfigTemplate<
        TInAppProductInfo,
        TPlayerSegmentInfo
        > : LegacySharedGameConfigTemplate<
            TInAppProductInfo,
            TPlayerSegmentInfo,
            DefaultMetaOfferInfo,
            DefaultMetaOfferGroupInfo
            >
        where TInAppProductInfo : InAppProductInfoBase, new()
        where TPlayerSegmentInfo : PlayerSegmentInfoBase, new()
    {
    }

    public abstract class LegacySharedGameConfigTemplate<
        TInAppProductInfo
        > : LegacySharedGameConfigTemplate<
            TInAppProductInfo,
            DefaultPlayerSegmentInfo
            >
        where TInAppProductInfo : InAppProductInfoBase, new()
    {
    }

    public abstract class LegacySharedGameConfigBase : LegacySharedGameConfigTemplate<DefaultInAppProductInfo> { }
}
