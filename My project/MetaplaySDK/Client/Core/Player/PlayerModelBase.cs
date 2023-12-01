// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Activables;
using Metaplay.Core.Analytics;
using Metaplay.Core.Client;
using Metaplay.Core.Config;
using Metaplay.Core.InAppPurchase;
using Metaplay.Core.InGameMail;
using Metaplay.Core.Localization;
using Metaplay.Core.Math;
using Metaplay.Core.Model;
using Metaplay.Core.Offers;
using Metaplay.Core.Session;
using Metaplay.Core.Web3;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace Metaplay.Core.Player
{
    /// <summary>
    /// Base for a PlayerModel class, i.e. class for storing the state and updating the logic for a single player.
    /// This should be derived from by a game-specific PlayerModel.
    /// This base class implements the game-agnostic parts.
    /// </summary>
    [MetaBlockedMembers(9, 15, 21, 26, 27, 36, 44)]
    [MetaReservedMembers(1, 100)] // previously skipped 6, 10, 11 for compatibility (may want to retain this for existing projects)
    [MetaReservedMembers(10_000, 20_000)]
    public abstract class PlayerModelBase<
          TPlayerModel
        , TPlayerStatistics
        , TPlayerMetaOfferGroups
        #if !METAPLAY_DISABLE_GUILDS
        , TPlayerGuildState
        #endif
        > : IPlayerModel<TPlayerModel>
        where TPlayerModel : PlayerModelBase<
              TPlayerModel
            , TPlayerStatistics
            , TPlayerMetaOfferGroups
            #if !METAPLAY_DISABLE_GUILDS
            , TPlayerGuildState
            #endif
            >
        where TPlayerStatistics : PlayerStatisticsBase, new()
        where TPlayerMetaOfferGroups : IPlayerMetaOfferGroups, new()
        #if !METAPLAY_DISABLE_GUILDS
        where TPlayerGuildState : IPlayerGuildState, new()
        #endif
    {
        // External services, not serialized or PrettyPrinted
        [IgnoreDataMember] public int                                                       LogicVersion            { get; set; } = 0;
        [IgnoreDataMember] public ISharedGameConfig                                         GameConfig              { get; set; }
        [IgnoreDataMember] public LogChannel                                                Log                     { get; set; } = LogChannel.Empty;

        [IgnoreDataMember] public IPlayerModelServerListenerCore                            ServerListenerCore      { get; set; } = EmptyPlayerModelServerListenerCore.Instance;
        [IgnoreDataMember] public IPlayerModelClientListenerCore                            ClientListenerCore      { get; set; } = EmptyPlayerModelClientListenerCore.Instance;

        [IgnoreDataMember] public AnalyticsEventHandler<IPlayerModelBase, PlayerEventBase>  AnalyticsEventHandler   { get; set; } = AnalyticsEventHandler<IPlayerModelBase, PlayerEventBase>.NopHandler;

        [IgnoreDataMember]
        public ContextWrappingAnalyticsEventHandler<IPlayerModelBase, PlayerEventBase> EventStream
            => new ContextWrappingAnalyticsEventHandler<IPlayerModelBase, PlayerEventBase>(context: this, handler: AnalyticsEventHandler);

        public IGameConfigDataResolver GetDataResolver() => GameConfig;

        // \note Sneaky switcheroo of TicksPerSecond, so the same name can be used for a const in PlayerModel
        int IPlayerModelBase.TicksPerSecond => GetTicksPerSecond();

        /// <summary> Get deterministic logic time at CurrentTick. Computed from TimeAtFirstTick, which is set by server. </summary>
        public MetaTime CurrentTime => ModelUtil.TimeAtTick(CurrentTick, TimeAtFirstTick, GetTicksPerSecond());

        const int CurrentBaseFixupVersion = 4;

        [MetaMember(49)] int                                                                        _baseFixupVersion           = 0; // \note Initialize to 0 for existing players from before this field was added. For new players, this is set to CurrentBaseFixupVersion in InitializeNewPlayerModel.

        [MetaMember(1), Transient, ExcludeFromGdprExport] public MetaTime                           TimeAtFirstTick             { get; private set; }   // Model's logical time at CurrentTick 0, set by server to keep determinism. Used together with CurrentTick and the ticks-per-second constant to calculate the current logic time (CurrentTime).
        [MetaMember(2), Transient, ExcludeFromGdprExport] public int                                CurrentTick                 { get; private set; }   // Current tick, since start of session. Advanced at rate of TicksPerSecond.
        [MetaMember(3), Transient, NoChecksum, ExcludeFromGdprExport] public bool                   IsOnline                    { get; set; }   // Is the player currently online?
        [MetaMember(8), Transient, NoChecksum, ExcludeFromGdprExport] public string                 SessionDeviceGuid           { get; set; }   // Device being used during current session, set at login.
        [MetaMember(40), Transient, NoChecksum, ExcludeFromGdprExport] public SessionToken          SessionToken                { get; set; }   // Token of the current session, set at login.
        [MetaMember(4), NoChecksum] public bool                                                     IsBanned                    { get; set; }   // Is the player banned?
        [MetaMember(5)] public PlayerTimeZoneInfo                                                   TimeZoneInfo                { get; set; } = new PlayerTimeZoneInfo();
        [MetaMember(34)] public PlayerLocation?                                                     LastKnownLocation           { get; set; }   // Last known location (if any) based on IP geolocation. Set on server at beginning of session if a location is successfully resolved.
        [MetaMember(12), NoChecksum] public TPlayerStatistics                                       Stats                       { get; private set; } = new TPlayerStatistics();
        [MetaMember(13)] public LanguageId                                                          Language                    { get; set; }
        [MetaMember(37)] public LanguageSelectionSource                                             LanguageSelectionSource     { get; set; }

        [MetaMember(14), ExcludeFromGdprExport] public List<string>                                 FirebaseMessagingTokensLegacy   { get; set; } = new List<string>(); // \note #legacy #notification. The PushNotifications member is the new one.
        [MetaMember(33), ExcludeFromGdprExport] public PlayerPushNotifications                      PushNotifications               { get; set; } = new PlayerPushNotifications();

        [MetaMember(16), NoChecksum] public List<InAppPurchaseEvent>                                InAppPurchaseHistory            { get; private set; } = new List<InAppPurchaseEvent>();                                         // History of successful in-app purchases
        [MetaMember(50), ServerOnly] public int                                                     NumDuplicateInAppPurchases      { get; set; }         = 0;                                                                      // Number of duplicate in-app purchases (that were however accepted because they were not consumables)
        [MetaMember(51), ServerOnly] public List<InAppPurchaseEvent>                                DuplicateInAppPurchaseHistory   { get; private set; } = new List<InAppPurchaseEvent>();                                         // Recent history of duplicate in-app purchases (that were however accepted because they were not consumables)
        [MetaMember(18), ServerOnly] public int                                                     NumFailedInAppPurchases         { get; set; }         = 0;                                                                      // Number of failed in-app purchases
        [MetaMember(19), ServerOnly] public List<InAppPurchaseEvent>                                FailedInAppPurchaseHistory      { get; private set; } = new List<InAppPurchaseEvent>();                                         // Recent history of failed in-app purchases
        [MetaMember(17), NoChecksum] public OrderedDictionary<string, InAppPurchaseEvent>           PendingInAppPurchases           { get; private set; } = new OrderedDictionary<string, InAppPurchaseEvent>();                    // IAP transactions waiting to be validated or claimed
        [MetaMember(35)] public OrderedDictionary<InAppProductId, PendingDynamicPurchaseContent>    PendingDynamicPurchaseContents  { get; private set; } = new OrderedDictionary<InAppProductId, PendingDynamicPurchaseContent>(); // Intended contents for dynamic-content products
        [MetaMember(41)] public OrderedDictionary<InAppProductId, PendingNonDynamicPurchaseContext> PendingNonDynamicPurchaseContexts { get; private set; } = new OrderedDictionary<InAppProductId, PendingNonDynamicPurchaseContext>(); // Intended analytics contexts for non-dynamic products
        [MetaMember(46)] public PlayerSubscriptionsModel                                            IAPSubscriptions                { get; private set; } = new PlayerSubscriptionsModel();
        public F64                                                                                  TotalIapSpend                   { get; set; } = F64.Zero;                                                                       // Cached value of a player's total IAP spend. This is not persisted and is intentionally not a meta-member. See comment in UpdateTotalIapSpend

        [MetaMember(20), NoChecksum, ExcludeFromGdprExport, JsonIgnore] public List<MetaInGameMail> LegacyMailInbox             { get; private set; } = new List<MetaInGameMail>();
        [MetaMember(45)] public List<PlayerMailItem>                                                MailInbox                   { get; private set; } = new List<PlayerMailItem>();
        [MetaMember(22), ServerOnly, ExcludeFromGdprExport] public OrderedSet<int>                  ReceivedBroadcastIds        { get; private set; } = new OrderedSet<int>();

        [MetaMember(23), ServerOnly] public OrderedDictionary<string, PlayerDeviceEntry>            DeviceHistory               { get; private set; } = new OrderedDictionary<string, PlayerDeviceEntry>();
        [MetaMember(24), ServerOnly] public List<PlayerLoginEvent>                                  LoginHistory                { get; private set; } = new List<PlayerLoginEvent>();
        [MetaMember(25), NoChecksum] public OrderedDictionary<AuthenticationKey, LegacyPlayerAuthEntry> LegacyAttachedAuthMethods   { get; private set; } = null;
        [MetaMember(54), NoChecksum] public OrderedDictionary<AuthenticationKey, PlayerAuthEntryBase>   AttachedAuthMethods         { get; private set; } = new OrderedDictionary<AuthenticationKey, PlayerAuthEntryBase>();

        [MetaMember(28), NoChecksum] public PlayerDeletionStatus                                    DeletionStatus              { get; set; } = PlayerDeletionStatus.None;
        [MetaMember(29), NoChecksum] public MetaTime                                                ScheduledForDeletionAt      { get; set; }

        [PrettyPrint(PrettyPrintFlag.HideInDiff)]
        [MetaMember(30), ServerOnly] public PlayerEventLog                                          EventLog                    { get; private set; } = new PlayerEventLog();
        #if !METAPLAY_DISABLE_GUILDS
        [MetaMember(31), ServerOnly] public TPlayerGuildState                                       GuildState                  { get; set; } = new TPlayerGuildState();
        #endif
        [MetaMember(32), ServerOnly] public PlayerPendingSynchronizedServerActions                  PendingSynchronizedServerActions { get; set; } = null;
        [MetaMember(38), ServerOnly] public int                                                     SearchVersion               { get; set; } = 0; // Is the search table in up-to-date format? Used for migrating old players
        [MetaMember(39), ServerOnly] public PlayerExperimentsState                                  Experiments                 { get; set; } = new PlayerExperimentsState();

        [MetaMember(42)] public TPlayerMetaOfferGroups                                              MetaOfferGroups             { get; set; } = new TPlayerMetaOfferGroups();

        [MetaMember(43), Transient]  public bool                                                    IsDeveloper                 { get; set; }
        [MetaMember(47), Transient, ServerOnly]  public bool                                        IsClientConnected           { get; set; }
        [MetaMember(48), Transient, ServerOnly]  public ClientAppPauseStatus                        ClientAppPauseStatus        { get; set; }

        [MetaMember(52)] public PlayerNftSubModel                                                   Nft                         { get; private set; } = new PlayerNftSubModel();

        [MetaMember(53)] public OrderedDictionary<ClientSlot, PlayerSubClientStateBase>             PlayerSubClientStates       { get; private set; } = new OrderedDictionary<ClientSlot, PlayerSubClientStateBase>();

        /// <summary>
        /// Empty constructor is used in the following cases:
        /// - When a model is created for a new player. <see cref="InitializeNewPlayerModel"/>
        ///   will be called just after constructing the model.
        /// - When deserializing the state. This happens on the server when restoring
        ///   the state from the database, and on the client when deserializing the
        ///   state from the server-provided snapshot.
        /// </summary>
        protected PlayerModelBase()
        {
        }

        public void InitializeNewPlayerModel(MetaTime now, ISharedGameConfig gameConfig, EntityId playerId, string name)
        {
            // Set initial persistent state.

            _baseFixupVersion = CurrentBaseFixupVersion;

            Stats = new TPlayerStatistics();
            Stats.InitializeForNewPlayer(now);

            Language = MetaplayCore.Options.DefaultLanguage;

            // Set transient state.
            // \note Ideally we shouldn't need to set transient state here,
            //       but only in OnRestoredFromPersistedState. Setting it
            //       here anyway, to be defensive.

            TimeAtFirstTick = now;
            CurrentTick = 0;

            GameConfig = gameConfig;

            // Userland
            GameInitializeNewPlayerModel(now, gameConfig, playerId, name);
        }

        public void ResetTime(MetaTime timeAtFirstTick)
        {
            TimeAtFirstTick = timeAtFirstTick;
            CurrentTick = 0;
        }

        public void RunPlayerModelBaseFixups()
        {
            while (_baseFixupVersion < CurrentBaseFixupVersion)
            {
                Log.Info("Running PlayerModelBase fixups from {FromVersion} to {ToVersion}", _baseFixupVersion, _baseFixupVersion+1);
                RunBaseFixup(fromVersion: _baseFixupVersion);
                _baseFixupVersion++;
            }
        }

        void RunBaseFixup(int fromVersion)
        {
            // Be careful to consider the following when adding fixups:
            // These base fixups are run before user schema migrations in
            // PlayerActor. This way, from the user code's point of view,
            // the base fixups are always up to date. However, this means
            // the base fixups and the user migrations do not have a well-
            // defined order. Even if a base fixup was added to the code
            // after a user migration was added, the user migration can
            // be run after the base fixup. This means:
            // - These fixups should not depend on the state having been
            //   processed by the user migrations.
            // - These fixups should not do anything that cause old user-
            //   defined migrations to behave incorrectly.
            // Neither can be guaranteed without considering all existing
            // users. Therefore it's best to restrict these fixups to
            // simple fixes that are desired for all existing player states,
            // and not use them for meaningful schema migrations.

            switch (fromVersion)
            {
                case 0:
                    // Remove duplicate entries from InAppPurchaseHistory.
                    // This is a one-time migration; from now on, duplicates won't be
                    // added in InAppPurchaseHistory in the first place.
                    this.MigrateDuplicateIAPHistoryEntries();
                    break;

                case 1:
                    // Intentionally left unused
                    break;

                case 2:
                    // An earlier fixup may have deleted the device history member, resurrect it here
                    if (DeviceHistory == null)
                        DeviceHistory = new OrderedDictionary<string, PlayerDeviceEntry>();

                    // Migrate legacy auth entries
                    FixupPlayerAuthEntries();
                    // Populate partial device history
                    PopulateDeviceHistoryFromLoginHistory();
                    break;

                case 3:
                    // Fix potential null entries in DeviceHistory[].LoginMethods
                    FixupRemoveNullHistoryLoginMethods();
                    break;

                default:
                    throw new InvalidOperationException($"Unhandled PlayerModelBase fixup from version {fromVersion}");
            }
        }

        public void OnRestoredFromPersistedState(MetaTime curTime, MetaDuration elapsedTime)
        {
            // Initialize logical time
            TimeAtFirstTick = curTime;
            CurrentTick = 0;

            // Adjust activables' activations according to possible config changes
            TryAdjustActivableActivations();

            // Userland
            GameOnRestoredFromPersistedState(elapsedTime);
        }

        public int TryAdjustActivableActivations()
        {
            int numAdjusted = 0;

            foreach (MetaActivableRepository.KindSpec kind in MetaActivableRepository.Instance.AllKinds.Values)
            {
                if (!kind.PlayerSubModel.TryGetMemberValue(this, out IMetaActivableSet activableSet))
                    continue;

                if (activableSet == null)
                {
                    MemberInfo memberInfo = kind.PlayerSubModel.PerConcreteContainingType[GetType()].MemberInfo;
                    throw new InvalidOperationException($"{memberInfo.ToMemberWithGenericDeclaringTypeString()} is null but is required for {kind.Id} activables.");
                }

                numAdjusted += activableSet.TryAdjustEachActivation(player: this);
            }

            return numAdjusted;
        }

        void ClearAllErroneousActivableStates()
        {
            foreach (MetaActivableRepository.KindSpec kind in MetaActivableRepository.Instance.AllKinds.Values)
            {
                if (!kind.PlayerSubModel.TryGetMemberValue(this, out IMetaActivableSet activableSet))
                    continue;

                if (activableSet == null)
                {
                    MemberInfo memberInfo = kind.PlayerSubModel.PerConcreteContainingType[GetType()].MemberInfo;
                    throw new InvalidOperationException($"{memberInfo.ToMemberWithGenericDeclaringTypeString()} is null but is required for {kind.Id} activables.");
                }

                activableSet.ClearErroneousActivableStates();
            }
        }

        public void PurgeStateForRemovedConfigItems()
        {
            ClearAllErroneousActivableStates();
        }

        public void DebugForceSetActivablePhase(MetaActivableKindId kindId, string activableIdStr, MetaActivableState.DebugPhase? phase)
        {
            MetaActivableRepository.KindSpec    kindSpec        = MetaActivableRepository.Instance.AllKinds[kindId];
            IMetaActivableSet                   activableSet    = MetaActivableUtil.GetPlayerActivableSetForKind(kindId, player: this);
            IStringId                           activableId     = StringIdUtil.CreateDynamic(kindSpec.ConfigKeyType, activableIdStr); // \note #activable-id-type
            IMetaActivableConfigData            activableInfo   = MetaActivableUtil.GetActivableGameConfigData(new MetaActivableKey(kindId, activableId), GameConfig);

            activableSet.DebugForceSetPhase(activableInfo, player: this, phase);
        }

        public void DebugForceSetModelTime(MetaTime timeAtFirstTick, int currentTick)
        {
            TimeAtFirstTick = timeAtFirstTick;
            CurrentTick = currentTick;
        }

        public void OnFastForwardTime(MetaDuration elapsedTime)
        {
            Log.Debug("Fast-forwarded model ({ElapsedTime})", elapsedTime);

            // Userland
            GameFastForwardTime(elapsedTime);
        }

        public void OnSessionStarted()
        {
            // Refresh offers always at session start (unless game has opted out by overriding ShouldRefreshMetaOffersAtSessionStart).
            // Additionally, offers can be refreshed with the PlayerRefreshMetaOffers action.
            if (ShouldRefreshMetaOffersAtSessionStart)
                RefreshMetaOffers(partialRefreshInfo: null);

            // Userland
            GameOnSessionStarted();
        }

        public void OnInitialLogin()
        {
            GameOnInitialLogin();
        }

        /// <summary>
        /// Cache total IAP spend
        /// </summary>
        [MetaOnDeserialized]
        public void UpdateTotalIapSpend()
        {
            // TODO This doesn't take into account refunds. Since refunds can come to the server at
            // any moment, the correct way to do this would be to calculate this value on the server
            // and communicate it to the client (through ServerFutureActions?). At that point,
            // InAppPurchaseHistory could be make [ServerOnly] again. See discussion in
            // https://github.com/metaplay/idler/pull/214
            F64 totalSpend = F64.Zero;

            // Create a lookup for subscription instances' purchases: originalTransactionId -> purchaseEvent.
            // This is used to resolve the reference prices of subscriptions. The IAP history is the place
            // where the reference prices are stored. We don't necessarily have game config at this point,
            // since this method may be called at deserialization.
            Dictionary<string, InAppPurchaseEvent> subscriptionInstancePurchases = new Dictionary<string, InAppPurchaseEvent>();
            foreach (InAppPurchaseEvent ev in InAppPurchaseHistory)
            {
                if (ev.SubscriptionQueryResult == null)
                    continue;

                subscriptionInstancePurchases[ev.OriginalTransactionId] = ev;
            }

            // Calculate spend from normal IAP purchases.
            foreach (InAppPurchaseEvent ev in InAppPurchaseHistory)
            {
                // Don't count duplicate purchases.
                if (ev.IsDuplicateTransaction)
                    continue;

                // Don't count subscription purchases directly. Subscription spend is calculated differently.
                if (ev.SubscriptionQueryResult != null)
                    continue;

                totalSpend += ev.ReferencePrice;
            }

            // Calculate spend from subscriptions:
            // Multiply reference price by number of periods.
            // \todo The number of periods is here assumed to be the number of real purchases
            //       related to the subscription (initial purchase + renewals), but that is
            //       not necessarily true if there was a free period or some other such discount.
            foreach (SubscriptionModel subscription in IAPSubscriptions.Subscriptions.Values)
            {
                foreach ((string originalTransactionId, SubscriptionInstanceModel instance) in subscription.SubscriptionInstances)
                {
                    // For reused subscriptions, only the latest (active), non-disabled
                    // one is counted. This is slightly peculiar behavior because it means
                    // a player's total spend can decrease when a subscription is reused on
                    // another account. However, showing the spend on the account that is
                    // actually using the subscription seems the most intuitive. Furthermore,
                    // only that account will do renewal checks and thus have the most
                    // up-to-date information.
                    if (instance.DisabledDueToReuse)
                        continue;

                    // Don't count subscriptions acquired via family-sharing,
                    // as no money was spent for them.
                    if (instance.LastKnownState?.IsAcquiredViaFamilySharing ?? false)
                        continue;

                    // \note subscriptionInstancePurchases should always contain the entry, but let's be safe.
                    if (!subscriptionInstancePurchases.TryGetValue(originalTransactionId, out InAppPurchaseEvent subscriptionPurchase))
                        continue;

                    int numPeriods = instance.LastKnownState?.NumPeriods ?? 1;
                    totalSpend += subscriptionPurchase.ReferencePrice * numPeriods;
                }
            }

            TotalIapSpend = totalSpend;
        }

        /// <summary>
        /// Execute a single tick on the PlayerModel. That is, progress time by 1sec/PlayerModel.TicksPerSecond.
        /// </summary>
        /// <param name="checksumCtx">Context for taking checksum snapshot for each stage of the tick</param>
        public void Tick(IChecksumContext checksumCtx)
        {
            //Log.Debug("Tick {currentTick} ({currentTime})", CurrentTick, CurrentTime);

            // Userland
            GameTick(checksumCtx);

            // Update time
            CurrentTick += 1;
        }

        /// <summary>
        /// Import data from old model after a reset or overwrite operation.
        /// </summary>
        void ImportAfterResetOrOverwrite(TPlayerModel source)
        {
            InAppPurchaseHistory                = source.InAppPurchaseHistory;
            NumDuplicateInAppPurchases          = source.NumDuplicateInAppPurchases;
            DuplicateInAppPurchaseHistory       = source.DuplicateInAppPurchaseHistory;
            NumFailedInAppPurchases             = source.NumFailedInAppPurchases;
            FailedInAppPurchaseHistory          = source.FailedInAppPurchaseHistory;
            PendingInAppPurchases               = source.PendingInAppPurchases;
            PendingDynamicPurchaseContents      = source.PendingDynamicPurchaseContents;
            PendingNonDynamicPurchaseContexts   = source.PendingNonDynamicPurchaseContexts;
            IAPSubscriptions                    = source.IAPSubscriptions;
            LoginHistory                        = source.LoginHistory;
            AttachedAuthMethods                 = source.AttachedAuthMethods;
            LegacyAttachedAuthMethods           = source.LegacyAttachedAuthMethods;
            DeviceHistory                       = source.DeviceHistory;
            UpdateTotalIapSpend();
        }

        public void ImportAfterReset(TPlayerModel source)
        {
            ImportAfterResetOrOverwrite(source);
            GameImportAfterReset(source);
        }

        public void ImportAfterOverwrite(TPlayerModel source)
        {
            ImportAfterResetOrOverwrite(source);
            GameImportAfterOverwrite(source);
        }

        public void ImportAfterCreateNew(TPlayerModel defaultInitModel)
        {
            ImportAfterResetOrOverwrite(defaultInitModel);
            GameImportAfterCreateNew();
        }

        protected T GetGameConfig<T>() where T : ISharedGameConfig { return (T)GameConfig; }

        public void SetGameConfig(ISharedGameConfig config) { GameConfig = config; }

        public virtual async Task RemapEntityIdsAsync(IModelEntityIdRemapper remapper)
        {
            // Fix up data by remapping
            PlayerId = await remapper.RemapEntityIdAsync(PlayerId);

            #if !METAPLAY_DISABLE_GUILDS
            if (GuildState.GuildId != EntityId.None)
                GuildState.GuildId = await remapper.RemapEntityIdAsync(GuildState.GuildId);
            #endif
        }

        #region MetaOffers

        IPlayerMetaOfferGroups IPlayerModelBase.MetaOfferGroups => MetaOfferGroups;

        [IgnoreDataMember] protected virtual bool ShouldRefreshMetaOffersAtSessionStart => true;

        /// <inheritdoc cref="IPlayerModelBase.RefreshMetaOffers" />
        public virtual void RefreshMetaOffers(MetaOfferGroupsRefreshInfo? partialRefreshInfo)
        {
            TryFinalizeMetaOfferGroups(partialRefreshInfo);
            RefreshMetaOfferPerOfferActivation(partialRefreshInfo);
            TryActivateMetaOfferGroups(partialRefreshInfo);
        }

        /// <inheritdoc cref="IPlayerModelBase.GetMetaOfferGroupsRefreshInfo" />
        public virtual MetaOfferGroupsRefreshInfo GetMetaOfferGroupsRefreshInfo()
        {
            return new MetaOfferGroupsRefreshInfo(
                // \note Each IEnumerable gets ToList'd here, not only because MetaOfferGroupsRefreshInfo
                //       wants it that way, but to avoid dangerously lazy evaluation of the IEnumerables.
                //       The evaluation depends on the model's current state, and in RefreshMetaOffers we
                //       will be mutating the model and also reading the lists.
                groupsToFinalize: GetMetaOfferGroupsToFinalize().ToList(),
                groupsWithOffersToRefresh: GetMetaOfferGroupsWithOffersToRefresh().ToList(),
                groupsToActivate: GetMetaOfferGroupsToActivate().ToList());
        }

        protected virtual IEnumerable<MetaOfferGroupInfoBase> GetMetaOfferGroupsToFinalize()
        {
            return GameConfig.MetaOfferGroups.Values.Where(groupInfo => MetaOfferGroups.CanBeFinalized(groupInfo.GroupId, player: this));
        }

        protected virtual IEnumerable<MetaOfferGroupInfoBase> GetMetaOfferGroupsWithOffersToRefresh()
        {
            foreach (MetaOfferGroupModelBase offerGroup in MetaOfferGroups.GetActiveStates(player: this))
            {
                if (MetaOfferGroups.HasAnyPerOfferActivationsToRefresh(offerGroup.ActivableInfo, player: this))
                    yield return offerGroup.ActivableInfo;
            }
        }

        protected virtual IEnumerable<MetaOfferGroupInfoBase> GetMetaOfferGroupsToActivate()
        {
            foreach ((OfferPlacementId placementId, IEnumerable<MetaOfferGroupInfoBase> offerGroupsInMostImportantFirstOrder) in GameConfig.MetaOfferGroupsPerPlacementInMostImportantFirstOrder)
            {
                // \note For performance reasons, placement availability is checked here in the outer loop.
                //       #offer-group-placement-condition
                if (!MetaOfferGroups.PlacementIsAvailable(player: this, placementId))
                    continue;

                foreach (MetaOfferGroupInfoBase offerGroupInfo in offerGroupsInMostImportantFirstOrder)
                {
                    if (MetaOfferGroups.CanStartActivation(offerGroupInfo, player: this))
                    {
                        yield return offerGroupInfo;
                        // Break, because this placement would now become occupied, and thus no other groups
                        // in the same placement could be activated at the same time.
                        break;
                    }
                }
            }
        }

        protected virtual void TryFinalizeMetaOfferGroups(MetaOfferGroupsRefreshInfo? partialRefreshInfo)
        {
            IEnumerable<MetaOfferGroupId> candidateGroupIds = partialRefreshInfo.HasValue
                                                              ? partialRefreshInfo.Value.GroupsToFinalize?.Select(groupInfo => groupInfo.GroupId) ?? Enumerable.Empty<MetaOfferGroupId>()
                                                              : GameConfig.MetaOfferGroups.Keys;

            MetaOfferGroups.TryFinalizeEach(candidateGroupIds, player: this);
        }

        protected virtual void RefreshMetaOfferPerOfferActivation(MetaOfferGroupsRefreshInfo? partialRefreshInfo)
        {
            IEnumerable<MetaOfferGroupInfoBase> candidateGroupInfos = partialRefreshInfo.HasValue
                                                                      ? partialRefreshInfo.Value.GroupsWithOffersToRefresh ?? Enumerable.Empty<MetaOfferGroupInfoBase>()
                                                                      : MetaOfferGroups.GetActiveStates(player: this).Select(groupState => groupState.ActivableInfo);

            foreach (MetaOfferGroupInfoBase groupInfo in candidateGroupInfos)
                MetaOfferGroups.RefreshPerOfferActivations(groupInfo, player: this);
        }

        protected virtual void TryActivateMetaOfferGroups(MetaOfferGroupsRefreshInfo? partialRefreshInfo)
        {
            if (partialRefreshInfo.HasValue)
            {
                foreach (MetaOfferGroupInfoBase groupInfo in partialRefreshInfo.Value.GroupsToActivate ?? Enumerable.Empty<MetaOfferGroupInfoBase>())
                {
                    if (!MetaOfferGroups.PlacementIsAvailable(player: this, groupInfo.Placement))
                        continue;

                    MetaOfferGroups.TryStartActivation(groupInfo, player: this);
                }
            }
            else
            {
                foreach ((OfferPlacementId placementId, IEnumerable<MetaOfferGroupInfoBase> offerGroupsInMostImportantFirstOrder) in GameConfig.MetaOfferGroupsPerPlacementInMostImportantFirstOrder)
                {
                    // \note For performance reasons, placement availability is checked here in the outer loop.
                    //       #offer-group-placement-condition
                    if (!MetaOfferGroups.PlacementIsAvailable(player: this, placementId))
                        continue;

                    foreach (MetaOfferGroupInfoBase offerGroupInfo in offerGroupsInMostImportantFirstOrder)
                    {
                        if (MetaOfferGroups.TryStartActivation(offerGroupInfo, player: this))
                        {
                            // Break, because this placement is now occupied, and thus no other groups
                            // in the same placement can be activated at the same time.
                            break;
                        }
                    }
                }
            }
        }

        #endregion

        #region Fixups

        void FixupPlayerAuthEntries()
        {
            if (LegacyAttachedAuthMethods != null)
            {
                foreach ((AuthenticationKey key, LegacyPlayerAuthEntry auth) in LegacyAttachedAuthMethods)
                {
                    PlayerAuthEntryBase newEntry = FixupLegacyPlayerAuthEntry(key, auth);
                    if (newEntry == null)
                        throw new InvalidOperationException($"Missing fixup for authentication record {key}. Custom PlayerModel.FixupLegacyPlayerAuthEntry should be implemented to convert these keys.");
                    AttachedAuthMethods.TryAdd(key, newEntry);
                }
                LegacyAttachedAuthMethods = null;
            }
        }

        void PopulateDeviceHistoryFromLoginHistory()
        {
            foreach (PlayerLoginEvent login in LoginHistory)
            {
                if (DeviceHistory.TryGetValue(login.DeviceId, out PlayerDeviceEntry device))
                {
                    // \note: AuthenticationKey may be null for old log entries
                    if (device.IncompleteHistory && login.DeviceModel == device.DeviceModel && login.AuthenticationKey != null)
                    {
                        device.RecordNewLogin(login.AuthenticationKey, login.Timestamp);
                    }
                }
            }
        }

        /// <summary>
        /// Converts LegacyPlayerAuthEntry into (a proper subtype of) PlayerAuthEntryBase.
        /// </summary>
        protected virtual PlayerAuthEntryBase FixupLegacyPlayerAuthEntry(AuthenticationKey key, LegacyPlayerAuthEntry auth)
        {
            switch (key.Platform)
            {
                case AuthenticationPlatform.DeviceId:
                {
                    // Try to find device info from device history
                    string deviceModel = "unknown";
                    if (DeviceHistory.TryGetValue(key.Id, out PlayerDeviceEntry device))
                        deviceModel = device.DeviceModel;
                    return new PlayerDeviceIdAuthEntry(auth.AttachedAt, deviceModel);
                }
                case AuthenticationPlatform.Development:
                case AuthenticationPlatform.GooglePlay:
                case AuthenticationPlatform.GameCenter:
                case AuthenticationPlatform.GoogleSignIn:
                case AuthenticationPlatform.SignInWithApple:
                case AuthenticationPlatform.FacebookLogin:
                case AuthenticationPlatform.SignInWithAppleTransfer:
                case AuthenticationPlatform.GameCenter2020:
                case AuthenticationPlatform.GameCenter2020UAGT:
                case AuthenticationPlatform.Ethereum:
                case AuthenticationPlatform.ImmutableX:
                    return new PlayerAuthEntryBase.Default(auth.AttachedAt);

                case AuthenticationPlatform._ReservedDontUse1:
                    // Invalid internal state.
                    throw new InvalidOperationException($"Authentication entry with {nameof(AuthenticationPlatform._ReservedDontUse1)} platform is not allowed");
            }
            return null;
        }

        void FixupRemoveNullHistoryLoginMethods()
        {
            foreach (PlayerDeviceEntry entry in DeviceHistory.Values)
                entry.LoginMethods.Remove(null);
        }

        #endregion

        #region IPlayerModel trivial mappings

        PlayerStatisticsBase    IPlayerModelBase.Stats          => Stats;
        #if !METAPLAY_DISABLE_GUILDS
        IPlayerGuildState       IPlayerModelBase.GuildState     => GuildState;
        #endif

        #endregion

        #region Abstracts and virtuals

        public virtual IModelRuntimeData<IPlayerModelBase> GetRuntimeData() => new PlayerModelRuntimeDataBase(this);

        public abstract EntityId    PlayerId        { get; set; }
        public abstract string      PlayerName      { get; set; }
        public abstract int         PlayerLevel     { get; set; }

        protected abstract int GetTicksPerSecond   ();

        /// <inheritdoc cref="InitializeNewPlayerModel"/>
        protected abstract void GameInitializeNewPlayerModel(MetaTime now, ISharedGameConfig gameConfig, EntityId playerId, string name);

        /// <inheritdoc cref="OnRestoredFromPersistedState"/>
        protected virtual void GameOnRestoredFromPersistedState     (MetaDuration elapsedTime){ }

        /// <inheritdoc cref="OnFastForwardTime"/>
        protected virtual void GameFastForwardTime                  (MetaDuration elapsedTime){ }

        /// <inheritdoc cref="OnInitialLogin"/>
        protected virtual void GameOnInitialLogin                   (){ }

        /// <inheritdoc cref="OnSessionStarted"/>
        protected virtual void GameOnSessionStarted                 (){ }

        /// <inheritdoc cref="Tick"/>
        protected virtual void GameTick                             (IChecksumContext checksumCtx){ }

        /// <inheritdoc cref="ImportAfterReset"/>
        protected virtual void GameImportAfterReset                 (TPlayerModel source){ }

        /// <inheritdoc cref="ImportAfterOverwrite"/>
        protected virtual void GameImportAfterOverwrite             (TPlayerModel source){ }

        /// <inheritdoc cref="ImportAfterCreateNew"/>
        protected virtual void GameImportAfterCreateNew             (){ }

        /// <inheritdoc cref="IPlayerModelBase.OnClaimedInAppProduct"/>
        public virtual void OnClaimedInAppProduct(InAppPurchaseEvent ev, InAppProductInfoBase productInfoBase, out ResolvedPurchaseContentBase resolvedContent)
        {
            throw new NotImplementedException($"To integrate in-app purchases, you need to override {nameof(OnClaimedInAppProduct)} in the game-specific player model class");
        }

        /// <inheritdoc cref="IPlayerModelBase.OnClaimedSubscriptionPurchase"/>
        public virtual void OnClaimedSubscriptionPurchase(InAppPurchaseEvent ev, InAppProductInfoBase productInfo)
        {
            // Optional. Do nothing by default.
        }

        /// <inheritdoc cref="IPlayerModelBase.UpdateTimeZone"/>
        public virtual void UpdateTimeZone(PlayerTimeZoneInfo newTimeZone, bool isFirstLogin)
        {
            TimeZoneInfo = newTimeZone;
        }

        #endregion
    }

    /// <summary>
    /// <inheritdoc cref="PlayerModelBase{TPlayerModel, TPlayerStatistics, TPlayerMetaOfferGroups, TPlayerGuildState}"/>
    /// </summary>
    /// <remarks>
    /// Helper around <see cref="PlayerModelBase{TPlayerModel, TPlayerStatistics, TPlayerMetaOfferGroups, TPlayerGuildState}"/>
    /// for setting defaults for some type parameters.
    /// \todo [nuutti] Would be convenient to not need all those type parameters in the first place.
    ///                Figure out a way. Overriding MetaMembers, maybe?
    /// </remarks>
    public abstract class PlayerModelBase<
          TPlayerModel
        , TPlayerStatistics
        #if !METAPLAY_DISABLE_GUILDS
        , TPlayerGuildState
        #endif
        > : PlayerModelBase<
              TPlayerModel
            , TPlayerStatistics
            , DefaultPlayerMetaOfferGroupsModel
            #if !METAPLAY_DISABLE_GUILDS
            , TPlayerGuildState
            #endif
            >
        where TPlayerModel : PlayerModelBase<
              TPlayerModel
            , TPlayerStatistics
            , DefaultPlayerMetaOfferGroupsModel
            #if !METAPLAY_DISABLE_GUILDS
            , TPlayerGuildState
            #endif
            >
        where TPlayerStatistics : PlayerStatisticsBase, new()
        #if !METAPLAY_DISABLE_GUILDS
        where TPlayerGuildState : IPlayerGuildState, new()
        #endif
    {
    }

    #if !METAPLAY_DISABLE_GUILDS
    public abstract class PlayerModelBase<
          TPlayerModel
        , TPlayerStatistics
        > : PlayerModelBase<
              TPlayerModel
            , TPlayerStatistics
            , DefaultPlayerMetaOfferGroupsModel
            , PlayerGuildStateCore
            >
        where TPlayerModel : PlayerModelBase<
              TPlayerModel
            , TPlayerStatistics
            , DefaultPlayerMetaOfferGroupsModel
            , PlayerGuildStateCore
            >
        where TPlayerStatistics : PlayerStatisticsBase, new()
    {
    }
    #endif

    public class PlayerModelRuntimeDataBase : IModelRuntimeData<IPlayerModelBase>
    {
        readonly ISharedGameConfig                                          _gameConfig;
        readonly int                                                        _logicVersion;
        readonly LogChannel                                                 _log;
        readonly IPlayerModelServerListenerCore                             _serverListenerCore;
        readonly IPlayerModelClientListenerCore                             _clientListenerCore;
        readonly AnalyticsEventHandler<IPlayerModelBase, PlayerEventBase>   _analyticsEventHandler;

        public PlayerModelRuntimeDataBase(IPlayerModelBase instance)
        {
            _gameConfig             = instance.GameConfig;
            _logicVersion           = instance.LogicVersion;
            _log                    = instance.Log;
            _serverListenerCore     = instance.ServerListenerCore;
            _clientListenerCore     = instance.ClientListenerCore;
            _analyticsEventHandler  = instance.AnalyticsEventHandler;
        }

        public virtual void CopyResolversTo(IPlayerModelBase instance)
        {
            instance.GameConfig = _gameConfig;
            instance.LogicVersion = _logicVersion;
        }

        public virtual void CopySideEffectListenersTo(IPlayerModelBase instance)
        {
            instance.Log                    = _log;
            instance.ServerListenerCore     = _serverListenerCore;
            instance.ClientListenerCore     = _clientListenerCore;
            instance.AnalyticsEventHandler  = _analyticsEventHandler;
        }
    }

    public abstract class PlayerModelRuntimeDataBase<TPlayerModel> : PlayerModelRuntimeDataBase where TPlayerModel : IPlayerModel<TPlayerModel>
    {
        public PlayerModelRuntimeDataBase(TPlayerModel model) : base(model)
        {
        }

        public virtual void CopyResolversTo(TPlayerModel model)
        {
            base.CopyResolversTo(model);
        }

        public sealed override void CopyResolversTo(IPlayerModelBase instance)
        {
            CopyResolversTo((TPlayerModel)instance);
        }

        public virtual void CopySideEffectListenersTo(TPlayerModel model)
        {
            base.CopySideEffectListenersTo(model);
        }

        public sealed override void CopySideEffectListenersTo(IPlayerModelBase instance)
        {
            CopySideEffectListenersTo((TPlayerModel)instance);
        }
    }

}
