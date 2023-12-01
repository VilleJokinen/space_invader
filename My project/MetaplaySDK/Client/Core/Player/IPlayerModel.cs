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
using Metaplay.Core.Serialization;
using Metaplay.Core.Session;
using Metaplay.Core.Web3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

#if !METAPLAY_DISABLE_GUILDS
using Metaplay.Core.Guild;
#endif

namespace Metaplay.Core.Player
{
    /// <summary>
    /// Server-side event listener interface, to be used by Metaplay core callbacks.
    /// Game-specific callbacks should go to game-specific <c>IPlayerModelServerListener</c>.
    /// </summary>
    public interface IPlayerModelServerListenerCore
    {
        void OnPlayerNameChanged            (string newName);
        void LanguageChanged                (LanguageInfo newLanguage, ContentHash languageVersion);
        void DynamicInAppPurchaseRequested  (InAppProductId productId);
        void StaticInAppPurchaseContextRequested (InAppProductId productId);
        void InAppPurchased                 (InAppPurchaseEvent ev, InAppProductInfoBase productInfo);
        void FirebaseMessagingTokenAdded    (string token);
        //void SocialAuthenticateRequested(SocialAuthenticateRequest authRequest);

        #if !METAPLAY_DISABLE_GUILDS
        /// <summary>
        /// Data returned by PlayerActor.CreateGuildMemberPlayerData has changed and should
        /// be updated to the guild.
        /// </summary>
        void GuildMemberPlayerDataChanged   ();
        #endif

        void ActivableActivationStarted     (MetaActivableKey activableKey);
        void ActivableConsumed              (MetaActivableKey activableKey);
        void ActivableFinalized             (MetaActivableKey activableKey);

        void MetaOfferActivationStarted     (MetaOfferGroupId groupId, MetaOfferId offerId);
        void MetaOfferPurchased             (MetaOfferGroupId groupId, MetaOfferId offerId);
        void OnPlayerBannedStatusChanged    (bool isBanned);

        void AuthMethodAttached             (AuthenticationKey authKey);
        void AuthMethodDetached             (AuthenticationKey authKey);
    }

    /// <summary>
    /// Empty implementation of <see cref="IPlayerModelServerListenerCore"/>, so that there can always be a listener
    /// object and no null checks are needed.
    /// </summary>
    public class EmptyPlayerModelServerListenerCore : IPlayerModelServerListenerCore
    {
        public static readonly EmptyPlayerModelServerListenerCore Instance = new EmptyPlayerModelServerListenerCore();

        public void OnPlayerNameChanged             (string newName) { }
        public void LanguageChanged                 (LanguageInfo newLanguage, ContentHash languageVersion) { }
        public void DynamicInAppPurchaseRequested   (InAppProductId productId) { }
        public void StaticInAppPurchaseContextRequested (InAppProductId productId) { }
        public void InAppPurchased                  (InAppPurchaseEvent ev, InAppProductInfoBase productInfo) { }
        public void FirebaseMessagingTokenAdded     (string token) { }
        //public void SocialAuthenticateRequested(SocialAuthenticateRequest authRequest) { }

        #if !METAPLAY_DISABLE_GUILDS
        public void GuildMemberPlayerDataChanged    () { }
        #endif

        public void ActivableActivationStarted      (MetaActivableKey activableKey) { }
        public void ActivableConsumed               (MetaActivableKey activableKey) { }
        public void ActivableFinalized              (MetaActivableKey activableKey) { }

        public void MetaOfferActivationStarted      (MetaOfferGroupId groupId, MetaOfferId offerId) { }
        public void MetaOfferPurchased              (MetaOfferGroupId groupId, MetaOfferId offerId) { }
        public void OnPlayerBannedStatusChanged     (bool isBanned) { }

        public void AuthMethodAttached              (AuthenticationKey authKey) { }
        public void AuthMethodDetached              (AuthenticationKey authKey) { }
    }

    /// <summary>
    /// Client-side event listener interface, to be used by Metaplay core callbacks.
    /// Game-specific callbacks should go to game-specific <c>IPlayerModelClientListener</c>.
    /// </summary>
    public interface IPlayerModelClientListenerCore
    {
        void OnPlayerNameChanged            (string newName);
        void PendingDynamicPurchaseContentAssigned      (InAppProductId productId);
        void PendingStaticInAppPurchaseContextAssigned  (InAppProductId productId);

        /// <summary>
        /// Called when IAP receipt has completed server-side validation and was deemed invalid.
        /// </summary>
        void InAppPurchaseValidationFailed  (InAppPurchaseEvent ev);

        /// <summary>
        /// Called when IAP receipt has completed server-side validation successfully. The status of the purchase
        /// <paramref name="ev"/> is either <see cref="InAppPurchaseStatus.ValidReceipt"/> in the case of a valid,
        /// unused receipt, or <see cref="InAppPurchaseStatus.ReceiptAlreadyUsed"/> if the receipt was valid but
        /// the item has been already used.
        /// </summary>
        void InAppPurchaseValidated         (InAppPurchaseEvent ev);
        void InAppPurchaseClaimed           (InAppPurchaseEvent ev);
        void DuplicateInAppPurchaseCleared  (InAppPurchaseEvent ev);
        void OnPlayerScheduledForDeletionChanged();
        void OnNewInGameMail                (PlayerMailItem mail);
    }

    /// <summary>
    /// Empty implementation of <see cref="IPlayerModelClientListenerCore"/>, so that there can always be a listener
    /// object and no null checks are needed.
    /// </summary>
    public class EmptyPlayerModelClientListenerCore : IPlayerModelClientListenerCore
    {
        public static readonly EmptyPlayerModelClientListenerCore Instance = new EmptyPlayerModelClientListenerCore();

        public void OnPlayerNameChanged (string newName) { }
        public void PendingDynamicPurchaseContentAssigned      (InAppProductId productId) { }
        public void PendingStaticInAppPurchaseContextAssigned  (InAppProductId productId) { }
        public void InAppPurchaseValidationFailed (InAppPurchaseEvent ev) { }
        public void InAppPurchaseValidated (InAppPurchaseEvent ev) { }
        public void InAppPurchaseClaimed (InAppPurchaseEvent ev) { }
        public void DuplicateInAppPurchaseCleared (InAppPurchaseEvent ev) { }
        public void OnPlayerScheduledForDeletionChanged() { }
        public void OnNewInGameMail(PlayerMailItem mail) { }
    }

    /// <summary>
    /// Constants related to PlayerModels.
    /// </summary>
    public static class PlayerModelConstants
    {
        public const int    MaxPendingInAppTransactions             = 100;      // Maximum number of pending IAP transactions to have in flight
        public const int    LoginHistoryMaxSize                     = 20;       // Number of login events to remember
        public const int    DeviceHistoryMaxSize                    = 20;       // Number of distinct devices to remember
        public const int    DuplicateInAppPurchaseHistoryMaxSize    = 10;       // Number of duplicate in-app purchase events to remember
        public const int    FailedInAppPurchaseHistoryMaxSize       = 10;       // Number of failed in-app purchase events to remember
        public const int    PlayerSearchTableVersion                = 1;        // Version of the data layout in the PlayerNameSearch table
    }

    [MetaSerializable]
    public class PlayerPendingSynchronizedServerActions
    {
        [MetaMember(1)] public MetaSerialized<PlayerActionBase>[] PendingActions;
    }

    /// <summary>
    /// The information source used for selecting a language for a Player. Sources are in increasing priority.
    /// </summary>
    [MetaSerializable]
    public enum LanguageSelectionSource
    {
        /// <summary>
        /// Language was chosen automatically during account creation with no user information.
        /// </summary>
        AccountCreationAutomatic = 0,

        /// <summary>
        /// Language was chosen automatically by server.
        /// </summary>
        ServerSideAutomatic = 1,

        /// <summary>
        /// Language was chosen automatically by end user device.
        /// </summary>
        UserDeviceAutomatic = 2,

        /// <summary>
        /// Language was chosen by end user.
        /// </summary>
        UserSelected = 3,
    }

    [MetaSerializable]
    public class PlayerExperimentsState
    {
        [MetaSerializable]
        public struct ExperimentAssignment
        {
            /// <summary>
            /// The Variant Group the player was assigned into. By convention, the variant <c>null</c> is the control group.
            /// </summary>
            [MetaMember(1)] public ExperimentVariantId VariantId;

            public ExperimentAssignment(ExperimentVariantId variantId)
            {
                VariantId = variantId;
            }
        }

        /// <summary>
        /// The assignment information for each Experiment the player is assigned into.
        /// </summary>
        [MetaMember(1)] public OrderedDictionary<PlayerExperimentId, ExperimentAssignment> ExperimentGroupAssignment = new OrderedDictionary<PlayerExperimentId, ExperimentAssignment>();
    }

    /// <summary>
    /// The pausing status of the client app. Pausing happens when app is put into
    /// the background.
    /// </summary>
    [MetaSerializable]
    public enum ClientAppPauseStatus
    {
        /// <summary>
        /// Client app is running, i.e. the game is not paused.
        /// </summary>
        Running = 0,

        /// <summary>
        /// Client app is paused.
        /// </summary>
        Paused,

        /// <summary>
        /// Client app is resuming from background. This is the time span from when the application gets the notification
        /// it's being resumed from pause to the end of the first frame. During this first frame, the client handles all
        /// past due actions such as flushing pending ticks.
        /// </summary>
        Unpausing,
    }

    /// <summary>
    /// Common base interface for PlayerModel classes.
    /// </summary>
    public interface IPlayerModelBase : IModel<IPlayerModelBase>, IMetaIntegrationConstructible<IPlayerModelBase>
    {
        LogChannel                                                              Log                     { get; set; }
        AnalyticsEventHandler<IPlayerModelBase, PlayerEventBase>                AnalyticsEventHandler   { get; set; }
        ContextWrappingAnalyticsEventHandler<IPlayerModelBase, PlayerEventBase> EventStream             { get; }

        IPlayerModelServerListenerCore                          ServerListenerCore          { get; set; }
        IPlayerModelClientListenerCore                          ClientListenerCore          { get; set; }
        ISharedGameConfig                                       GameConfig                  { get; set; }

        MetaTime                                                CurrentTime                 { get; }
        EntityId                                                PlayerId                    { get; set; }
        string                                                  PlayerName                  { get; set; }
        int                                                     PlayerLevel                 { get; }

        MetaTime                                                TimeAtFirstTick             { get; }   // Model's logical time at CurrentTick 0, set by server to keep determinism. Used together with CurrentTick and the ticks-per-second constant to calculate the current logic time (CurrentTime).
        int                                                     CurrentTick                 { get; }   // Current tick, since TimeAtFirstTick. Advanced at rate of TicksPerSecond.
        int                                                     TicksPerSecond              { get; }

        /// <summary>
        /// Is there currently Session for this player. Session may linger for a while after client application has been closed
        /// in case the client reconnects. See also <see cref="IsClientConnected"/> and <see cref="ClientAppPauseStatus"/>.
        /// </summary>
        bool                                                    IsOnline                    { get; set; }
        string                                                  SessionDeviceGuid           { get; set; }   // Device being used during current session, set at login.
        SessionToken                                            SessionToken                { get; set; }   // Token of the current session, set at login.
        bool                                                    IsBanned                    { get; set; }   // Is the player banned?
        /// <summary>
        /// Info about player's preferred time zone. Reported by client, but may be corrected by server. The server can also restrict changing this after initial login, see IPlayerModelBase.UpdateTimeZone.
        /// </summary>
        PlayerTimeZoneInfo                                      TimeZoneInfo                { get; set; }
        PlayerLocation?                                         LastKnownLocation           { get; set; }   // Last known location (if any) based on IP geolocation. Set on server at beginning of session if a location is successfully resolved.
        PlayerStatisticsBase                                    Stats                       { get; }

        /// <summary>
        /// Current language of the player. May be <c>None</c> if localization is not enabled or if the player has not logged
        /// </summary>
        LanguageId                                              Language                    { get; set; }

        /// <summary>
        /// The selection source that selected the current language.
        /// </summary>
        LanguageSelectionSource                                 LanguageSelectionSource     { get; set; }

        List<string>                                            FirebaseMessagingTokensLegacy   { get; } // \note #legacy #notification. The PushNotifications member is the new one.
        PlayerPushNotifications                                 PushNotifications               { get; }

        List<InAppPurchaseEvent>                                            InAppPurchaseHistory            { get; }
        int                                                                 NumDuplicateInAppPurchases      { get; set; }
        List<InAppPurchaseEvent>                                            DuplicateInAppPurchaseHistory   { get; }
        int                                                                 NumFailedInAppPurchases         { get; set; }
        List<InAppPurchaseEvent>                                            FailedInAppPurchaseHistory      { get; }
        OrderedDictionary<string, InAppPurchaseEvent>                       PendingInAppPurchases           { get; }
        OrderedDictionary<InAppProductId, PendingDynamicPurchaseContent>    PendingDynamicPurchaseContents  { get; }
        OrderedDictionary<InAppProductId, PendingNonDynamicPurchaseContext> PendingNonDynamicPurchaseContexts { get; }
        PlayerSubscriptionsModel                                            IAPSubscriptions                { get; }
        F64                                                                 TotalIapSpend                   { get; set; }

        List<PlayerMailItem>                                    MailInbox                   { get; }
        OrderedSet<int>                                         ReceivedBroadcastIds        { get; }

        List<PlayerLoginEvent>                                  LoginHistory                { get; }
        OrderedDictionary<string, PlayerDeviceEntry>            DeviceHistory               { get; }
        OrderedDictionary<AuthenticationKey, PlayerAuthEntryBase> AttachedAuthMethods       { get; }

        PlayerDeletionStatus                                    DeletionStatus              { get; set; }
        MetaTime                                                ScheduledForDeletionAt      { get; set; }

        PlayerEventLog                                          EventLog                    { get; }

        #if !METAPLAY_DISABLE_GUILDS
        IPlayerGuildState                                       GuildState                  { get; }
        #endif
        int                                                     SearchVersion               { get; set; }
        PlayerExperimentsState                                  Experiments                 { get; set; }
        bool                                                    IsDeveloper                 { get; set; }

        /// <summary>
        /// Is there an active (TLS/TCP/Websocket) connection to the client. As the system tolerates short connection
        /// drops, this may be temporarily <c>false</c> even during a session. See also <see cref="IsOnline"/> and <see cref="ClientAppPauseStatus"/>.
        /// <para>This value is Server-only.</para>
        /// </summary>
        bool                                                    IsClientConnected           { get; set; }

        /// <summary>
        /// The application pausing phase as observed by the client. Note that if <see cref="IsClientConnected"/> is <c>false</c>, the client cannot
        /// update the state and this may become out-of-date.
        /// <para>This value is Server-only.</para>
        /// </summary>
        ClientAppPauseStatus                                    ClientAppPauseStatus        { get; set; }

        /// <summary>
        /// List of server actions that were pending when the model was serialized. Do not edit this member. This member is
        /// managed automatically and any changes will be overwritten. Should default to null.
        /// </summary>
        PlayerPendingSynchronizedServerActions                  PendingSynchronizedServerActions { get; set; }

        IPlayerMetaOfferGroups                                  MetaOfferGroups             { get; }
        /// <summary>
        /// Refresh MetaOffers: finalize ended activations of offer groups, and refresh
        /// activations of existing offers and offer groups.
        /// </summary>
        /// <param name="partialRefreshInfo">
        /// If non-null, describes which offer groups this method should consider;
        /// see <see cref="GetMetaOfferGroupsRefreshInfo"/>.
        /// If null, all configured offer groups are considered.
        /// </param>
        void                                                    RefreshMetaOffers(MetaOfferGroupsRefreshInfo? partialRefreshInfo);
        /// <summary>
        /// Resolve info about which offer groups can be refreshed. This can be given to the
        /// <see cref="PlayerRefreshMetaOffers"/> action in order to reduce its workload:
        /// if only a few offers can be refreshed, the action won't need to check all the
        /// existing offers, but only those that were passed in the <see cref="MetaOfferGroupsRefreshInfo"/>.
        /// Furthermore, if <see cref="MetaOfferGroupsRefreshInfo.HasAny"/> returns false,
        /// the action can be omitted entirely.
        /// <para>
        /// The purpose is to reduce workload on the server, when the client can determine
        /// which offers should be refreshed. It is assumed that trusting the client in this
        /// manner is not significantly exploitable. An illegitimate client can at worst
        /// omit the refreshing of some offer groups, but cannot cause illegal refreshes to
        /// happen, because <see cref="PlayerRefreshMetaOffers"/> will still check the conditions
        /// of each given offer group.
        /// </para>
        /// </summary>
        /// <remarks>
        /// In some scenarios involving multiple offer groups, this may return inaccurate results:
        /// for example, it may claim that both offer groups X and Y can be activated, even
        /// though it might be that only one of them can be activated at the same time.
        /// This is because there can be arbitrary activation interdependencies between offer
        /// groups, but this method works by only inspecting the player model's current state,
        /// instead of speculating what its state would be after activating some of the
        /// candidate groups. A similar inaccuracy in the other direction is also possible;
        /// this might claim that offer group Y cannot be activated even if it actually could
        /// (after activating X).
        /// In practice, this may lead to some legal offer group activations to be omitted
        /// during one <see cref="PlayerRefreshMetaOffers"/>. They will then likely be activated
        /// on a subsequent invocation.
        /// </remarks>
        MetaOfferGroupsRefreshInfo                              GetMetaOfferGroupsRefreshInfo();

        PlayerNftSubModel                                       Nft                         { get; }
        OrderedDictionary<ClientSlot, PlayerSubClientStateBase> PlayerSubClientStates       { get; }

        /// <summary>
        /// Initialize state for a newly created player. This method is only used
        /// on the server (or in OfflineServer, if running in offline mode), and
        /// the resulting state is sent to the client over the network.
        /// </summary>
        void InitializeNewPlayerModel(MetaTime now, ISharedGameConfig gameConfig, EntityId playerId, string name);

        /// <summary>
        /// Run sdk-level fixup migrations. This is called on the server before
        /// user-defined schema migrations are run.
        /// </summary>
        void RunPlayerModelBaseFixups();

        /// <summary>
        /// State has been restored after being inactive for a while.
        /// This can happen due to the player logging in, but also for any other reason that causes
        /// the actor on the server-side to wake up and load the player state from the database,
        /// such as the player being viewed via the dashboard.
        ///
        /// This function is only executed on the server.
        ///
        /// For code that should be executed specifically when the player logs in,
        /// the <see cref="OnSessionStarted"/> method is more appropriate.
        /// </summary>
        /// <param name="curTime">Current time at the time of restoring</param>
        /// <param name="elapsedTime">Elapsed time since the model was persisted in the database</param>
        /// <remarks>
        /// As a bit of a quirk, this is also called just after creating the initial model
        /// for a new player (in which case elapsedTime is zero), as well as in a few special
        /// circumstances, such as when creating a fresh player model for purpose of
        /// clearing a player's state due to scheduled deletion.
        /// </remarks>
        void OnRestoredFromPersistedState(MetaTime curTime, MetaDuration elapsedTime);

        /// <inheritdoc cref="Metaplay.Core.MultiplayerEntity.IMultiplayerModel.ResetTime"/>
        void ResetTime(MetaTime timeAtFirstTick);

        /// <inheritdoc cref="Metaplay.Core.MultiplayerEntity.IMultiplayerModel.OnFastForwardTime(MetaDuration)"/>
        void OnFastForwardTime(MetaDuration elapsedTime);

        /// <summary>
        /// Player has logged in and started a game session.
        ///
        /// This function is only executed on the server.
        /// </summary>
        void OnSessionStarted();

        /// <summary>
        /// Player has logged in for the first time is about to start a game session. This is convenient
        /// place for giving out player's initial resources, such as gold or coins. Giving initial resources
        /// here, as opposed to any earlier step like constructor, guarantees the resources are given using
        /// the most up-to-date config version. In particular, if any Player's membership in an Experiment
        /// changes during the login, this is the earliest point in time where the Experiment's game config
        /// is in use.
        ///
        /// This is called before <see cref="OnSessionStarted"/>.
        ///
        /// This function is only executed on the server.
        /// </summary>
        void OnInitialLogin();

        /// <summary>
        /// Handler for when a (non-subscription) IAP was successfully claimed by the player. Add all gained
        /// items and resources into the player's inventory.
        /// </summary>
        /// <param name="ev">Info about the purchase event. Should not be modified by this method!</param>
        /// <param name="productInfoBase">The config info of the purchased product.</param>
        /// <param name="resolvedContent">
        /// Output parameter for resolved contents of the purchase, to be recorded for customer service purposes.
        /// Can be set to null if no info needs to be recorded; otherwise should be assigned an object
        /// of a subclass of <see cref="ResolvedPurchaseContentBase"/>:
        /// either <see cref="ResolvedPurchaseMetaRewards"/>, or a game-defined subclass.
        /// </param>
        void OnClaimedInAppProduct(InAppPurchaseEvent ev, InAppProductInfoBase productInfo, out ResolvedPurchaseContentBase resolvedContent);

        /// <summary>
        /// Called when a subscription IAP was successfully claimed by the player.
        /// It is optional for the game to define this; the subscription gets recorded
        /// in <see cref="IAPSubscriptions"/> automatically by the SDK.
        /// </summary>
        /// <remarks>
        /// The subscription purchase may have been either an actual purchase
        /// or a purchase restoration.
        /// </remarks>
        void OnClaimedSubscriptionPurchase(InAppPurchaseEvent ev, InAppProductInfoBase productInfo);

        void UpdateTotalIapSpend();

        /// <summary>
        /// Updates in-place all EntityIds in the Model using the <paramref name="remapper"/>.
        /// </summary>
        Task RemapEntityIdsAsync(IModelEntityIdRemapper remapper);

        /// <summary>
        /// Debug-force or un-force a phase for the given activable.
        /// See <see cref="PlayerDebugForceSetActivablePhase"/>.
        /// </summary>
        void DebugForceSetActivablePhase(MetaActivableKindId kindId, string activableIdStr, MetaActivableState.DebugPhase? phase);

        /// <summary>
        /// Debug-force a model clock into the given state. This should only be used for testing. Modifying timestamps of a
        /// live model is likely to cause checksum mismatches or errors.
        /// </summary>
        void DebugForceSetModelTime(MetaTime timeAtFirstTick, int currentTick);

        /// <summary>
        /// Remove data from the model that is associated with GameConfig items that are no longer present in the active config.
        /// This is currently only used for activable states but could be extended for anything else that implements a mechanism
        /// for retaining data related to config items that have been removed.
        /// </summary>
        void PurgeStateForRemovedConfigItems();

        /// <summary>
        /// Called whenever a new session starts. Used to update the stored time zone in the PlayerModel. By default, always changes the time zone.
        /// Override to implement restrictions or custom logic to time zone changes.
        /// </summary>
        /// <param name="newTimeZone">Validated client-reported time zone.</param>
        /// <param name="isFirstLogin">True, if this session is the client's first login. False otherwise.</param>
        public void UpdateTimeZone(PlayerTimeZoneInfo newTimeZone, bool isFirstLogin);
    }

    #if !METAPLAY_DISABLE_GUILDS
    /// <summary>
    /// Represents the necessary server-side PlayerModel fields required for a Player to be able to join a Guild.
    /// </summary>
    public interface IPlayerGuildState
    {
        /// <summary>
        /// None if not in a guild. Otherwise the ID of the guild.
        /// </summary>
        EntityId                                                GuildId                     { get; set; }

        /// <summary>
        /// null if not creating a guild. Otherwise the guild initialization arguments that
        /// will be passed to the newly created guild.
        /// </summary>
        GuildCreationParamsBase                                 PendingGuildCreation        { get; set; }

        /// <summary>
        /// Monotonically increasing id for PendingGuildOps. This is the INCLUSIVE upper bound for Ids.
        /// </summary>
        int                                                     LastPendingGuildOpEpoch     { get; set; }

        /// <summary>
        /// Set of ops that are not yet confirmed by guild to have been executed. Key is epoch number.
        /// May be null if empty.
        /// </summary>
        OrderedDictionary<int, GuildMemberGuildOpLogEntry>      PendingGuildOps             { get; set; }

        /// <summary>
        /// Epoch number of the last executed guild enqueued player op.
        /// </summary>
        int                                                     LastPlayerOpEpoch           { get; set; }

        /// <summary>
        /// Uniquely identifies this member instance in this (GuildId) guild. If we leave and join the guild, we
        /// get a new Id.
        /// </summary>
        int                                                     GuildMemberInstanceId       { get; set; }
    }

    /// <summary>
    /// The default implementation for <see cref="IPlayerGuildState"/>.
    /// </summary>
    [MetaSerializable]
    [MetaReservedMembers(1, 100)]
    public class PlayerGuildStateCore : IPlayerGuildState
    {
        [MetaMember(1), ServerOnly] public EntityId                                 GuildId                     { get; set; }
        [MetaMember(2), ServerOnly] public GuildCreationParamsBase                  PendingGuildCreation        { get; set; }
        [MetaMember(3), ServerOnly] public int                                      LastPendingGuildOpEpoch     { get; set; }

        /// Marked as ExcludeFromGdprExport as this represents internal communication protocol state, not game state, and
        /// serializing these is just gonna create a lot irrelevant noise.
        [MetaMember(4), ServerOnly, ExcludeFromGdprExport]
        public OrderedDictionary<int, GuildMemberGuildOpLogEntry>                   PendingGuildOps             { get; set; }
        [MetaMember(5), ServerOnly] public int                                      LastPlayerOpEpoch           { get; set; }
        [MetaMember(6), ServerOnly] public int                                      GuildMemberInstanceId       { get; set; }
    }
    #endif

    /// <summary>
    /// Base interface for game-specific PlayerModel classes.
    /// </summary>
    public interface IPlayerModel<TPlayerModel> : IPlayerModelBase
        where TPlayerModel : IPlayerModel<TPlayerModel>
    {
        /// <summary>
        /// Copy data that should survive player reset.
        /// </summary>
        /// <param name="source">the state of the player before Reset</param>
        void ImportAfterReset(TPlayerModel source);

        /// <summary>
        /// Copy data that should survive player overwrite.
        /// </summary>
        /// <param name="oldModel">the state of the player before Overwrite</param>
        void ImportAfterOverwrite(TPlayerModel oldModel);

        /// <summary>
        /// Clear data that should not be copied when creating a new entity from entity archive (i.e. not overwriting existing).
        /// </summary>
        /// <param name="defaultInitModel">default initialized state</param>
        void ImportAfterCreateNew(TPlayerModel defaultInitModel);
    }

    /// <summary>
    /// Helper methods for <see cref="IPlayerModelBase"/>.
    /// </summary>
    public static class IPlayerModelExtensions
    {
        public static PlayerLocalTime GetCurrentLocalTime(this IPlayerModelBase player)
            => new PlayerLocalTime(player.CurrentTime, player.TimeZoneInfo.CurrentUtcOffset);

        /// <summary>
        /// Record the given content as pending for the given in-app product (asserted to be a dynamic-content product),
        /// and invoke a listener on the server to make the server persist the state and confirm to the client.
        ///
        /// In addition to setting the purchase content, this method also records the <paramref name="gameProductAnalyticsId"/> and <paramref name="gameAnalyticsContext"/>.
        /// The <paramref name="gameProductAnalyticsId"/> is a game-specific opaque id used for analytics. It should identify a purchases from the user's point of view. For
        /// example if two "Shop Offers" use the same InAppProductId (i.e. the same SKU and the price points) but are show to user as two different offers with different
        /// contents, they should use different <paramref name="gameProductAnalyticsId"/>s. The <paramref name="gameAnalyticsContext"/> tracks arbitrary data that will be
        /// stored in the analytics events. Either or both value may be left null if they are not used.
        ///
        /// Should be called from an appropriate PlayerAction after validating that the content can be validly given to the player in its current state.
        /// </summary>
        public static void SetPendingDynamicInAppPurchase(this IPlayerModelBase player, InAppProductInfoBase productInfo, DynamicPurchaseContent content, string gameProductAnalyticsId, PurchaseAnalyticsContext gameAnalyticsContext)
        {
            if (!player.CanSetPendingDynamicInAppPurchase(productInfo, content, out string errorMessage))
                throw new InvalidOperationException(errorMessage);

            player.PendingDynamicPurchaseContents[productInfo.ProductId] = new PendingDynamicPurchaseContent(content, player.SessionDeviceGuid, gameProductAnalyticsId, gameAnalyticsContext, PendingDynamicPurchaseContentStatus.RequestedByClient);
            player.EventStream.Event(new PlayerEventPendingDynamicPurchaseContentAssigned(productInfo.ProductId, content, player.SessionDeviceGuid, gameProductAnalyticsId, gameAnalyticsContext));
            player.ClientListenerCore.PendingDynamicPurchaseContentAssigned(productInfo.ProductId);
            player.ServerListenerCore.DynamicInAppPurchaseRequested(productInfo.ProductId);
        }

        public static bool CanSetPendingDynamicInAppPurchase(this IPlayerModelBase player, InAppProductInfoBase productInfo, DynamicPurchaseContent content, out string errorMessage)
        {
            if (!productInfo.HasDynamicContent)
            {
                errorMessage = $"Cannot set dynamic content for in-app product {productInfo.ProductId} with HasDynamicContent==false";
                return false;
            }

            if (player.PendingInAppPurchases.Values.Any(ev => ev.ProductId == productInfo.ProductId))
            {
                errorMessage = $"Cannot set dynamic content for in-app product {productInfo.ProductId} because an existing purchase of same product is pending";
                return false;
            }

            errorMessage = null;
            return true;
        }

        public static IEnumerable<string> GetAllFirebaseMessagingTokens(this IPlayerModelBase player)
        {
            IEnumerable<string> legacy      = player.FirebaseMessagingTokensLegacy;
            IEnumerable<string> nonLegacy   = player.PushNotifications.GetFirebaseMessagingTokens();

            return legacy.Concat(nonLegacy);
        }

        /// <summary>
        /// Remove a completed purchase from PendingInAppPurchases, and record its state into a purchase history list.
        /// </summary>
        /// <param name="transactionId">The <see cref="InAppPurchaseEvent.TransactionId"/> of the purchase to be removed.</param>
        /// <exception cref="InvalidOperationException">Thrown when the <see cref="InAppPurchaseEvent.Status"/> of the purchase is not a terminal status.</exception>
        /// <remarks>This should only be called from certain <see cref="PlayerAction"/>s that handle IAPs.</remarks>
        public static void RemoveAndCatalogCompletedPendingInAppPurchase(this IPlayerModelBase player, string transactionId)
        {
            // Get the purchase event to store to history, but clone it and clear its signature and receipt (see CloneForHistory).
            InAppPurchaseEvent ev = player.PendingInAppPurchases[transactionId].CloneForHistory(player.CurrentTime, player.GetDataResolver());

            if (ev.Status == InAppPurchaseStatus.PendingValidation)
                throw new InvalidOperationException($"{nameof(RemoveAndCatalogCompletedPendingInAppPurchase)}: {transactionId} still has status {nameof(InAppPurchaseStatus.PendingValidation)}");

            // Remove transaction
            player.PendingInAppPurchases.Remove(transactionId);

            // Collect information according to success/failure

            if (ev.Status == InAppPurchaseStatus.ValidReceipt)
            {
                // For the purposes of cataloging the purchase, we consider it a duplicate of an
                // earlier purchase if it has the same OriginalTransactionId. Such duplicates are
                // caused by purchase restorations and other re-processing of already-made purchases
                // (of NonConsumable or Subscription products). We do not add the duplicates to
                // the InAppPurchaseHistory list, because they would risk bloating the list
                // without bound. Instead we add duplicates to a separate, bounded list.
                bool isDuplicate = player.InAppPurchaseHistory.Any(existingEv => existingEv.OriginalTransactionId == ev.OriginalTransactionId);

                if (isDuplicate)
                {
                    // Keep count of duplicates
                    player.NumDuplicateInAppPurchases += 1;

                    // Add to recent history of duplicate purchases
                    player.DuplicateInAppPurchaseHistory.Add(ev);
                    while (player.DuplicateInAppPurchaseHistory.Count > PlayerModelConstants.DuplicateInAppPurchaseHistoryMaxSize)
                        player.DuplicateInAppPurchaseHistory.RemoveAt(0);
                }
                else
                {
                    // Add to history of successful purchases
                    player.InAppPurchaseHistory.Add(ev);

                    // Update total spend cache
                    player.UpdateTotalIapSpend();
                }
            }
            else if (ev.Status == InAppPurchaseStatus.InvalidReceipt
                  || ev.Status == InAppPurchaseStatus.MissingContent
                  || ev.Status == InAppPurchaseStatus.ReceiptAlreadyUsed)
            {
                // Keep count of failures
                player.NumFailedInAppPurchases += 1;

                // Add to recent history of failed purchases
                player.FailedInAppPurchaseHistory.Add(ev);
                while (player.FailedInAppPurchaseHistory.Count > PlayerModelConstants.FailedInAppPurchaseHistoryMaxSize)
                    player.FailedInAppPurchaseHistory.RemoveAt(0);
            }
            else
                player.Log.Warning("RemoveAndCatalogCompletedPendingInAppPurchase: status {Status} unhandled for cataloging", ev.Status);
        }

        /// <summary>
        /// Migrate existing duplicate purchases from <see cref="IPlayerModelBase.InAppPurchaseHistory"/>
        /// into <see cref="IPlayerModelBase.DuplicateInAppPurchaseHistory"/>, similarly to how
        /// <see cref="RemoveAndCatalogCompletedPendingInAppPurchase"/> now stores them.
        /// This is done as a one-time migration for player states created before
        /// <see cref="IPlayerModelBase.DuplicateInAppPurchaseHistory"/> was added.
        /// </summary>
        public static void MigrateDuplicateIAPHistoryEntries(this IPlayerModelBase player)
        {
            HashSet<string> seenIds = new HashSet<string>();

            // Go through the iap history, keeping track of seen transaction ids.
            // Duplicates get overwritten with null now, and removed after this loop.
            // Also, duplicates are added to DuplicateInAppPurchaseHistory.
            for (int i = 0; i < player.InAppPurchaseHistory.Count; i++)
            {
                InAppPurchaseEvent ev = player.InAppPurchaseHistory[i];

                if (ev.OriginalTransactionId == null) // Shouldn't be null for completed purchases, but being defensive
                    continue;

                if (!seenIds.Add(ev.OriginalTransactionId))
                {
                    player.InAppPurchaseHistory[i] = null;

                    player.NumDuplicateInAppPurchases += 1;
                    player.DuplicateInAppPurchaseHistory.Add(ev);
                }
            }

            // Actually remove
            int numRemoved = player.InAppPurchaseHistory.RemoveAll(ev => ev == null);
            if (numRemoved > 0)
                player.Log.Info("Moved {NumRemoved} duplicate purchase entries from InAppPurchaseHistory to DuplicateInAppPurchaseHistory", numRemoved);

            // Truncate the duplicate history
            int excess = player.DuplicateInAppPurchaseHistory.Count - PlayerModelConstants.DuplicateInAppPurchaseHistoryMaxSize;
            if (excess > 0)
                player.DuplicateInAppPurchaseHistory.RemoveRange(0, excess);

            // Refresh transient derived data
            player.UpdateTotalIapSpend();
        }
    }

    public static class PlayerModelUtil
    {
        public static TModel CreateNewPlayerModel<TModel>(MetaTime now, ISharedGameConfig gameConfig, EntityId playerId, string name)
            where TModel : IPlayerModelBase, new()
        {
            TModel model = new TModel();
            model.InitializeNewPlayerModel(now, gameConfig, playerId, name);
            return model;
        }

        public static IPlayerModelBase CreateNewPlayerModel(MetaTime now, ISharedGameConfig gameConfig, EntityId playerId, string name)
        {
            IPlayerModelBase model = IntegrationRegistry.Create<IPlayerModelBase>();
            model.InitializeNewPlayerModel(now, gameConfig, playerId, name);
            return model;
        }

    }
}
