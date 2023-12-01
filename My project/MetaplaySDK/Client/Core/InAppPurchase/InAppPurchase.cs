// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Config;
using Metaplay.Core.Math;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Rewards;
using Metaplay.Core.Serialization;
using Metaplay.Core.TypeCodes;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Metaplay.Core.InAppPurchase
{
    /// <summary>
    /// Platform for in-app purchases. Validation method is chosen based on platform.
    /// </summary>
    [MetaSerializable]
    public enum InAppPurchasePlatform
    {
        Google = 0,             // Google Play purchase
        Apple = 1,              // Apple App Store purchase
        Development = 2,        // Development platform (eg, Windows/Mac) purchase
        _ReservedDontUse3 = 3   // Reserved for now
    }

    /// <summary>
    /// Status of a pending in-app purchase event.
    /// </summary>
    [MetaSerializable]
    public enum InAppPurchaseStatus
    {
        /// <summary>
        /// Purchase is waiting to be validated by the server.
        /// </summary>
        PendingValidation   = 0,
        /// <summary>
        /// Purchase has been successfully validated.
        /// </summary>
        ValidReceipt        = 1,
        /// <summary>
        /// Receipt was invalid.
        /// </summary>
        InvalidReceipt      = 2,
        /// <summary>
        /// The given transaction (for a consumable item) has already been used.
        /// </summary>
        ReceiptAlreadyUsed  = 3,
        /// <summary>
        /// Reserved value.
        /// </summary>
        _Reserved_4         = 4,
        /// <summary>
        /// Reserved value.
        /// </summary>
        _Reserved_5         = 5,
        /// <summary>
        /// Reserved value.
        /// </summary>
        _Reserved_6         = 6,
        /// <summary>
        /// The purchase was valid, but it was refunded by customer's request.
        /// </summary>
        Refunded            = 7,
        /// <summary>
        /// Content is missing for the purchase, and the purchase cannot be finished.
        /// This does *not* mean that the receipt was invalid - receipt is not validated for missing-content purchases.
        /// </summary>
        MissingContent      = 8,
    }

    /// <summary>
    /// Client-reported reason for in-app purchase failure.
    /// </summary>
    [MetaSerializable]
    public enum InAppPurchaseClientRefuseReason
    {
        Unknown = 0,
        CompletionActionFailed = 1,
        UnityUserCancelled = 2,
        UnityPurchasingUnavailable = 3,
        UnityExistingPurchasePending = 4,
        UnityProductUnavailable = 5,
        UnitySignatureInvalid = 6,
        UnityPaymentDeclined = 7,
        UnityDuplicateTransaction = 8,
    }

    /// <summary>
    /// Indicates whether a purchase was a real or test purchase.
    /// Resolved in a store-specific manner when the purchase is validated.
    /// For example, Apple and Google Play stores support testing/sandbox purchases,
    /// which are represented by <see cref="Sandbox"/>. Real money purchases
    /// are represented by <see cref="Normal"/>.
    /// <para>
    /// By convention, when using a nullable <c>InAppPurchasePaymentType?</c>,
    /// the null value means the payment type could not be determined.
    /// See <see cref="InAppPurchaseEvent.PaymentType"/> for possible reasons.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Note that <see cref="Normal"/> only indicates real money usage when using
    /// a real store platform. When using the <see cref="InAppPurchasePlatform.Development"/>
    /// fake store platform, real money is never involved, even for <see cref="Normal"/>.
    /// </remarks>
    [MetaSerializable]
    public enum InAppPurchasePaymentType
    {
        /// <summary>
        /// Normal real money purchase.
        /// </summary>
        Normal = 0,
        /// <summary>
        /// Sandbox (test) purchase.
        /// </summary>
        Sandbox = 1,
    }

    /// <summary>
    /// Base class for recording the resolved contents of a purchased IAP.
    /// "Resolved contents" here means concrete information about the purchased
    /// content, useful for customer service purposes even if IAP configs are changed
    /// after the purchase. For example, resolved content could be "X gems",
    /// instead of "in-app config entry X".
    ///
    /// A derived class can be defined in game-specific code, or
    /// <see cref="ResolvedPurchaseMetaRewards"/> can be used if the contents
    /// are appropriately represented by <see cref="MetaPlayerRewardBase"/>s.
    /// </summary>
    [MetaSerializable]
    public abstract class ResolvedPurchaseContentBase { }

    /// <summary>
    /// A pre-defined <see cref="ResolvedPurchaseContentBase"/> subclass representing
    /// the resolved contents as a list of <see cref="MetaPlayerRewardBase"/>s.
    /// This can be used by game code when appropriate.
    /// </summary>
    [MetaSerializableDerived(1000)]
    public class ResolvedPurchaseMetaRewards : ResolvedPurchaseContentBase
    {
        [MetaMember(1)] public List<MetaPlayerRewardBase> Rewards { get; private set; }

        public ResolvedPurchaseMetaRewards() { }
        public ResolvedPurchaseMetaRewards(IEnumerable<MetaPlayerRewardBase> rewards)
        {
            Rewards = (rewards ?? throw new ArgumentNullException(nameof(rewards))).ToList();
        }

    }

    /// <summary>
    /// In-app purchase event. Contains the necessary data required to validate whether a given
    /// event is a legit purchase.
    /// </summary>
    [MetaSerializable]
    [MetaReservedMembers(1, 12), MetaReservedMembers(13, 17), MetaReservedMembers(18, 100)] // Reserve 1-99, except 12 and 17 for compatibility reasons.
    [MetaDeserializationConvertFromIntegrationImplementation]
    public abstract class InAppPurchaseEvent : IMetaIntegrationConstructible<InAppPurchaseEvent>
    {
        /// <summary>
        /// Platform on which the purchase was made on
        /// </summary>
        [MetaMember(1)] public InAppPurchasePlatform                    Platform                        { get; protected set; }

        /// <summary>
        /// Unique id for transaction (all platforms)
        /// </summary>
        [MetaMember(2)] public string                                   TransactionId                   { get; protected set; }

        /// <summary>
        /// Unique id for product
        /// </summary>
        [MetaMember(3)] public InAppProductId                           ProductId                       { get; protected set; }

        /// <summary>
        /// Platform-specific product id being purchased (needs to match the receipt)
        /// </summary>
        [MetaMember(4)] public string                                   PlatformProductId               { get; protected set; }

        /// <summary>
        /// Receipt of the purchase (base64-encoded for iOS/Development, JSON for Android)
        /// </summary>
        [PrettyPrint(PrettyPrintFlag.Shorten)]
        [MetaMember(5)] public string                                   Receipt                         { get; protected set; }

        /// <summary>
        /// Signature (base64 on Android, SHA1 hash for Development)
        /// </summary>
        [MetaMember(6)] public string                                   Signature                       { get; protected set; }

        /// <summary>
        /// Status of the IAP event (pending validation, succeeded, failed)
        /// </summary>
        [MetaMember(7)] public InAppPurchaseStatus                      Status                          { get; set; }

        /// <summary>
        /// The OrderId on Google Play, null on other store platforms.
        /// For Google Play, OrderId is different from PurchaseToken, which is the value
        /// used for <see cref="TransactionId"/>. OrderId is useful for customer support,
        /// since it's the value presented to the user in Google Play.
        ///
        /// This is first passed in by the client in <see cref="PlayerInAppPurchased"/>,
        /// and after purchase validation is overwritten by the server in
        /// <see cref="PlayerInAppPurchaseValidated"/>.
        /// </summary>
        [MetaMember(16)] public string                                  OrderId                         { get; set; }

        /// <summary>
        /// Has the TransactionID been used before. Receipt validation may succeed for duplicate non-consumable products during restore purchases
        /// </summary>
        [MetaMember(8)] public bool                                     IsDuplicateTransaction          { get; set; }

        /// <summary>
        /// For debugging: how many times server has attempted validation of this purchase. ServerOnly, updated directly in server code.
        /// </summary>
        [MetaMember(18), ServerOnly] public int                         NumValidationsStarted           { get; set; } = 0;

        /// <summary>
        /// For debugging: how many validations of this purchase ended in a transient (retryable) error. ServerOnly, updated directly in server code.
        /// </summary>
        [MetaMember(19), ServerOnly] public int                         NumValidationTransientErrors    { get; set; } = 0;

        /// <summary>
        /// Initial time of the purchase (i.e., when the claim was made by the client)
        /// </summary>
        [MetaMember(9)] public MetaTime                                 PurchaseTime                    { get; set; }

        /// <summary>
        /// Reference price of the purchase (in USD)
        /// </summary>
        [MetaMember(10)] public F64                                     ReferencePrice                  { get; set; }

        /// <summary>
        /// Time when the purchase was claimed (i.e., when the contents were moved into inventory)
        /// </summary>
        [MetaMember(11)] public MetaTime                                ClaimTime                       { get; protected set; }

        /// <summary>
        /// Dynamic content tied to this purchase, if any.
        /// </summary>
        [MetaMember(13)] public DynamicPurchaseContent                  DynamicContent                  { get; set; }

        /// <summary>
        /// If true, the purchase is missing (dynamic) content, and will not finish successfully.
        /// However, we still want the server to check whether the transaction id has already been
        /// used in the past (i.e. duplicate purchase), so that we can confirm it to the store if
        /// needed. We want to do this because duplicate transaction attempts are a legitimate
        /// cause for missing content: namely, dynamic purchase content can be missing because it
        /// was already consumed when this same transaction was performed in the past, but failed
        /// to be confirmed to the store. If it indeed was a duplicate, then (and only then) we
        /// want to confirm the purchase instead of leaving it in pending status in the store.
        /// </summary>
        [MetaMember(22)] public bool                                    HasMissingContent               { get; set; } = false;

        /// <summary>
        /// Game-specific resolved content, set when purchase is claimed.
        /// </summary>
        [MetaMember(14)] public ResolvedPurchaseContentBase             ResolvedContent                 { get; set; }

        /// <summary>
        /// Resolved rewards for DynamicContent, set when purchase is claimed.
        /// </summary>
        [MetaMember(15)] public ResolvedPurchaseMetaRewards             ResolvedDynamicContent          { get; set; }

        /// <summary>
        /// The game-specific analytics identifier for the purchase. For example, "GemPack1" or "StoreSpecialOffer2020-1-A"
        /// </summary>
        [MetaMember(20)] public string                                  GameProductAnalyticsId          { get; set; }

        /// <summary>
        /// The game-specific purchase context for analytics. For example "MyGamePurchaseContext { Placement=ShopTab, Group=EventOffers }"
        /// </summary>
        [MetaMember(21)] public MetaSerialized<PurchaseAnalyticsContext> GamePurchaseAnalyticsContext   { get; set; }

        /// <summary>
        /// For subscriptions, this is assigned after the purchase has been validated,
        /// and describes the state of the subscription reported by the IAP store.
        /// Null for non-subscription products.
        /// </summary>
        [MetaMember(23)] public SubscriptionQueryResult                 SubscriptionQueryResult         { get; set; }

        /// <summary>
        /// Note: usually you'll want to access <see cref="OriginalTransactionId"/> instead.
        ///
        /// <para>
        /// Assigned after the purchase has been validated, this is the purchase's
        /// _original_ transaction id if it is different from <see cref="TransactionId"/>,
        /// or null if it is the same. It is stored in this manner (instead of just always
        /// storing original transaction id here) to save space in the common case where
        /// original transaction id is the same as TransactionId - transaction ids can be
        /// pretty large on some platforms.
        /// </para>
        /// </summary>
        [MetaMember(24)] public string OriginalTransactionIdIfDifferentFromTransactionId { get; set; }

        /// <summary>
        /// Get the original transaction id of the purchase. Only available after the purchase
        /// has been validated; null otherwise.
        ///
        /// The original transaction id is needed to recognize purchase restorations:
        /// on some platforms (Apple), purchase restorations get new transaction ids,
        /// but share the original transaction id with the original purchase.
        /// (On some other platforms (Google), purchase restorations seem to use the
        /// same transaction id as the original purchase, so just for those platforms,
        /// this member would be unnecessary, but is also harmless.)
        /// </summary>
        public string OriginalTransactionId
        {
            get
            {
                if (Status == InAppPurchaseStatus.PendingValidation)
                    return null;

                return OriginalTransactionIdIfDifferentFromTransactionId
                       ?? TransactionId;
            }
        }

        /// <summary>
        /// Indicates whether a purchase was a real or test purchase.
        /// Resolved in a store-specific manner when the purchase is validated.
        ///
        /// This is <c>null</c> when:
        /// - The purchase was validated before this property was introduced (in Metaplay SDK release 23).
        /// - The purchase hasn't been validated yet.
        /// - The purchase was not valid.
        /// - The purchase was valid, but the payment type could not be determined (for example because a request to Google Play's server failed).
        /// - The purchase was valid, but the payment type was something not supported by <see cref="InAppPurchasePaymentType"/> (for example "Reward from a video ad" supported by Google Play).
        /// - The purchase was on Google Play, but the server-side GooglePlayStoreOptions.EnableAndroidPublisherApi was false when the purchase was validated.
        /// </summary>
        [MetaMember(25)] public InAppPurchasePaymentType? PaymentType { get; set; }

        /// <remarks>
        /// This is a normal method instead of a setter for <see cref="OriginalTransactionId"/>
        /// because it has slightly tricky semantics.
        /// </remarks>
        public void SetOriginalTransactionId(string originalTransactionId)
        {
            if (originalTransactionId != TransactionId)
                OriginalTransactionIdIfDifferentFromTransactionId = originalTransactionId;
            else
                OriginalTransactionIdIfDifferentFromTransactionId = null;
        }

        /// <summary>
        /// Migrates original transaction id from inside <see cref="SubscriptionQueryResult"/>
        /// (if available) to a new top-level member of <see cref="InAppPurchaseEvent"/>.
        /// </summary>
        [MetaOnDeserialized]
        void TryMigrateOriginalTransactionIdFromSubscriptionQueryResult()
        {
            string originalTransactionIdFromSubscription = SubscriptionQueryResult?.LegacyOriginalTransactionId;
            if (originalTransactionIdFromSubscription == null)
            {
                // One of the following is true:
                // - This is not a subscription purchase
                //   => do nothing - nothing to migrate
                // - This is a subscription purchase, but still pending validation, so SubscriptionQueryResult is not yet set
                //   => do nothing - the purchase validation will get done using the new code, and no migration will be needed
                // - This migration has already been run
                //   => do nothing
                return;
            }

            // Migrate.
            SetOriginalTransactionId(originalTransactionIdFromSubscription);
            SubscriptionQueryResult.LegacyOriginalTransactionId = null;
        }

        protected InAppPurchaseEvent() { }

        public static InAppPurchaseEvent ForGoogle(string transactionId, InAppProductId productId, string platformProductId, string receipt, string signature, string orderId)
        {
            InAppPurchaseEvent ev = IntegrationRegistry.Create<InAppPurchaseEvent>();
            ev.Platform            = InAppPurchasePlatform.Google;
            ev.TransactionId       = transactionId;
            ev.ProductId           = productId;
            ev.PlatformProductId   = platformProductId;
            ev.Receipt             = receipt;
            ev.Signature           = signature;
            ev.Status              = InAppPurchaseStatus.PendingValidation;
            ev.OrderId             = orderId;
            return ev;
        }

        public static InAppPurchaseEvent ForApple(string transactionId, InAppProductId productId, string platformProductId, string receipt)
        {
            InAppPurchaseEvent ev = IntegrationRegistry.Create<InAppPurchaseEvent>();
            ev.Platform            = InAppPurchasePlatform.Apple;
            ev.TransactionId       = transactionId;
            ev.ProductId           = productId;
            ev.PlatformProductId   = platformProductId;
            ev.Receipt             = receipt;
            ev.Status              = InAppPurchaseStatus.PendingValidation;
            return ev;
        }

        public static InAppPurchaseEvent ForDevelopment(string transactionId, InAppProductId productId, string platformProductId, string receipt, string signature)
        {
            InAppPurchaseEvent ev = IntegrationRegistry.Create<InAppPurchaseEvent>();
            ev.Platform            = InAppPurchasePlatform.Development;
            ev.TransactionId       = transactionId;
            ev.ProductId           = productId;
            ev.PlatformProductId   = platformProductId;
            ev.Receipt             = receipt;
            ev.Signature           = signature;
            ev.Status              = InAppPurchaseStatus.PendingValidation;
            return ev;
        }

        /// <summary>
        /// Make a copy of the event with sensitive fields emptied (Receipt & Signature),
        /// and with <paramref name="claimTime"/> assigned to <see cref="ClaimTime"/>.
        /// </summary>
        /// <returns>Cloned copy of the event with sensitive fields emptied, and with <see cref="ClaimTime"/> set.</returns>
        public InAppPurchaseEvent CloneForHistory(MetaTime claimTime, IGameConfigDataResolver resolver)
        {
            InAppPurchaseEvent cloned = Clone(resolver);
            cloned.Receipt = null;
            cloned.Signature = null;
            cloned.ClaimTime = claimTime;
            return cloned;
        }

        public InAppPurchaseEvent Clone(IGameConfigDataResolver resolver)
        {
            return MetaSerialization.CloneTagged(this, MetaSerializationFlags.IncludeAll, logicVersion: null, resolver);
        }

        /// <summary>
        /// Returns the analytics context of the purchase. If there is no context, context is unreadable with the current version, or if context is not yet set in
        /// this stage of IAP validation, returns null.
        /// </summary>
        public PurchaseAnalyticsContext TryGetGamePurchaseAnalyticsContext()
        {
            try
            {
                if (!GamePurchaseAnalyticsContext.IsEmpty)
                    return GamePurchaseAnalyticsContext.Deserialize(resolver: null, logicVersion: null);
            }
            catch
            {
            }
            return null;
        }

        /// <summary>
        /// Invoked when client claims to have purchased the product in an in-app store
        /// but before the receipt has been validated on the server (and in particular,
        /// before the purchased product is granted to the player).
        /// </summary>
        public virtual void OnPurchased(IPlayerModelBase player, InAppProductInfoBase productInfo) { }

        /// <summary>
        /// Invoked after the server has validated the purchase, regardless of the outcome
        /// of the validation. Observe <see cref="Status"/> to determine what the outcome was.
        /// </summary>
        public virtual void OnValidated(IPlayerModelBase player) { }
    }

    [MetaSerializableDerived(100)]
    public class DefaultInAppPurchaseEvent : InAppPurchaseEvent
    {
    }

    /// <summary>
    /// Immutable properties of an in-app purchase required for purchase validation.
    ///
    /// <para>
    /// This, instead of <see cref="InAppPurchaseEvent"/>, is intended to be passed around within the server-side when
    /// doing purchase validation. <see cref="InAppPurchaseEvent"/> contains mutable state and can also contain
    /// parts that require an <see cref="Config.IGameConfigDataResolver"/> in order to deserialize (which can be
    /// problematic in actor messaging), and those parts are not needed for purchase validation, so it's cleaner
    /// and safer to use a separate type.
    /// </para>
    /// </summary>
    ///
    /// <remarks>
    /// Similar separation could be used in some other places as well. For example, <see cref="PlayerInAppPurchased"/>
    /// doesn't really need the stateful parts in its <see cref="PlayerInAppPurchased.Event"/> member.
    /// Really the only(?) places that should have mutable state are the storage sites of the purchases in PlayerModel.
    /// </remarks>
    [MetaSerializable]
    public class InAppPurchaseTransactionInfo
    {
        [MetaMember(1)] public InAppPurchasePlatform    Platform                { get; private set; }
        [MetaMember(2)] public string                   TransactionId           { get; private set; }
        [MetaMember(3)] public InAppProductId           ProductId               { get; private set; }
        [MetaMember(10)] public InAppProductType        ProductType             { get; private set; }
        [MetaMember(4)] public string                   PlatformProductId       { get; private set; }
        [PrettyPrint(PrettyPrintFlag.Shorten)]
        [MetaMember(5)] public string                   Receipt                 { get; private set; }
        [MetaMember(6)] public string                   Signature               { get; private set; }
        [MetaMember(9)] public MetaTime                 PurchaseTime            { get; private set; }
        [MetaMember(8)] public bool                     HasMissingContent       { get; private set; }
        [MetaMember(7)] public bool                     AllowTestPurchases      { get; private set; }

        InAppPurchaseTransactionInfo() {}

        InAppPurchaseTransactionInfo(InAppPurchasePlatform platform, string transactionId, InAppProductId productId, InAppProductType productType, string platformProductId, string receipt, string signature, MetaTime purchaseTime, bool hasMissingContent, bool allowTestPurchases)
        {
            Platform                = platform;
            TransactionId           = transactionId;
            ProductId               = productId;
            ProductType             = productType;
            PlatformProductId       = platformProductId;
            Receipt                 = receipt;
            Signature               = signature;
            PurchaseTime            = purchaseTime;
            HasMissingContent       = hasMissingContent;
            AllowTestPurchases      = allowTestPurchases;
        }

        public static InAppPurchaseTransactionInfo FromPurchaseEvent(InAppPurchaseEvent ev, InAppProductType productType, bool allowTestPurchases = false)
        {
            return new InAppPurchaseTransactionInfo(
                platform:           ev.Platform,
                transactionId:      ev.TransactionId,
                productId:          ev.ProductId,
                productType:        productType,
                platformProductId:  ev.PlatformProductId,
                receipt:            ev.Receipt,
                signature:          ev.Signature,
                purchaseTime:       ev.PurchaseTime,
                hasMissingContent:  ev.HasMissingContent,
                allowTestPurchases: allowTestPurchases);
        }
    }

    /// <summary>
    /// Utility functions for dealing with in-app purchases
    /// </summary>
    public static class InAppPurchaseUtil
    {
        // \note Just to be safe, we count transaction id length in unicode code points
        //       in order to be consistent with database varchar lengths, even though
        //       legitimate transaction ids are unlikely to ever contain non-ascii stuff.
        public const int TransactionIdMinLengthCodePoints = 8;
        public const int TransactionIdMaxLengthCodePoints = 512; // \note Used in varchar(N) for the database key PersistedInAppPurchase.TransactionId, so needs to be low enough.

        /// <summary>
        /// Validate that the <see cref="InAppPurchaseEvent"/> is properly formed. This is used
        /// to filter out the noise from lowest effort hack attempts.
        /// </summary>
        /// <param name="ev">In-app purchase event to validate</param>
        /// <returns></returns>
        public static bool IsValidPurchaseEvent(InAppPurchaseEvent ev)
        {
            // Must be of expected integration type
            if (ev.GetType() != IntegrationRegistry.GetSingleIntegrationType<InAppPurchaseEvent>())
                return false;

            // Must have transactionId
            if (string.IsNullOrEmpty(ev.TransactionId))
                return false;

            // TransactionId must have reasonable length
            int transactionIdLengthCodePoints = Util.GetNumUnicodeCodePointsPermissive(ev.TransactionId);
            if (transactionIdLengthCodePoints < TransactionIdMinLengthCodePoints
             || transactionIdLengthCodePoints > TransactionIdMaxLengthCodePoints)
                return false;

            // Must have ProductId
            if (ev.ProductId == null)
                return false;

            // Must have PlatformProductId
            if (string.IsNullOrEmpty(ev.PlatformProductId))
                return false;

            // Receipt is required on all platforms
            if (string.IsNullOrEmpty(ev.Receipt))
                return false;

            switch (ev.Platform)
            {
                case InAppPurchasePlatform.Apple:
                    // Receipt must be non-empty
                    if (string.IsNullOrEmpty(ev.Receipt))
                        return false;

                    // Receipt must be base64-encoded
                    if (!Util.IsBase64Encoded(ev.Receipt))
                        return false;

                    // \todo [petri] better Receipt/
                    break;

                case InAppPurchasePlatform.Google:
                    // Must have signature
                    if (string.IsNullOrEmpty(ev.Signature))
                        return false;

                    // Signature must be base64-encoded
                    if (!Util.IsBase64Encoded(ev.Signature))
                        return false;

                    // \todo [petri] better checks
                    break;

                case InAppPurchasePlatform.Development:
                    // Must have signature
                    if (string.IsNullOrEmpty(ev.Signature))
                        return false;
                    break;

                default:
                    // Invalid platform
                    return false;
            }

            // \todo [petri] implement!
            return true;
        }
    }

    /// <summary>
    /// Client claims to have made a valid in-app purchase. Request to validate it on the server.
    /// </summary>
    [ModelAction(ActionCodesCore.PlayerInAppPurchased)]
    public class PlayerInAppPurchased : PlayerActionCore<IPlayerModelBase>
    {
        public InAppPurchaseEvent Event { get; private set; }

        PlayerInAppPurchased() { }
        public PlayerInAppPurchased(InAppPurchaseEvent ev) { Event = ev; }

        public override MetaActionResult Execute(IPlayerModelBase player, bool commit)
        {
            if (!InAppPurchaseUtil.IsValidPurchaseEvent(Event))
            {
                player.Log.Warning("Purchase event is invalid!");
                return MetaActionResult.InvalidInAppPurchaseEvent;
            }

            if (player.PendingInAppPurchases.ContainsKey(Event.TransactionId))
            {
                player.Log.Warning("Transaction {TransactionId} already pending", Event.TransactionId);
                return MetaActionResult.InvalidInAppTransactionId;
            }

            if (player.PendingInAppPurchases.Count >= PlayerModelConstants.MaxPendingInAppTransactions)
            {
                player.Log.Warning("Maximum number of pending in-app transactions exceeded");
                return MetaActionResult.TooManyPendingInAppPurchases;
            }

            if (Event.Status != InAppPurchaseStatus.PendingValidation)
            {
                player.Log.Warning("Transaction in invalid state {Status}", Event.Status);
                return MetaActionResult.InvalidInAppTransactionState;
            }

            if (!player.GameConfig.InAppProducts.TryGetValue(Event.ProductId, out InAppProductInfoBase productInfo))
            {
                player.Log.Warning("InAppProductId {ProductId} does not exist in GameConfig.InAppProduct", Event.ProductId);
                return MetaActionResult.InvalidInAppProductId;
            }

            // Check that PlatformProductId matches the product
            switch (Event.Platform)
            {
                case InAppPurchasePlatform.Google:
                    if (!string.Equals(Event.PlatformProductId, productInfo.GoogleId))
                    {
                        player.Log.Warning("Google PlatformProductId mismatch: got {PlatformProductId}, expecting {GoogleId}", Event.PlatformProductId, productInfo.GoogleId);
                        return MetaActionResult.InvalidInAppPlatformProductId;
                    }
                    break;

                case InAppPurchasePlatform.Apple:
                    if (!string.Equals(Event.PlatformProductId, productInfo.AppleId))
                    {
                        player.Log.Warning("Apple PlatformProductId mismatch: got {PlatformProductId}, expecting {AppleId}", Event.PlatformProductId, productInfo.AppleId);
                        return MetaActionResult.InvalidInAppPlatformProductId;
                    }
                    break;

                case InAppPurchasePlatform.Development:
                    if (!string.Equals(Event.PlatformProductId, productInfo.DevelopmentId))
                    {
                        player.Log.Warning("Development PlatformProductId mismatch: got {PlatformProductId}, expecting {DevelopmentId}", Event.PlatformProductId, productInfo.DevelopmentId);
                        return MetaActionResult.InvalidInAppPlatformProductId;
                    }
                    break;

                default:
                    player.Log.Warning("Invalid InAppProduct platform: {Platform}", Event.Platform);
                    return MetaActionResult.InvalidInAppPlatform;
            }

            if (commit)
            {
                // Clone the event, because we're gonna be mutating it, and we don't want to mutate the Event member of this action
                InAppPurchaseEvent eventState = Event.Clone(player.GetDataResolver());
                CommitPurchaseState(player, eventState, productInfo);
            }

            return MetaActionResult.Success;
        }

        static void CommitPurchaseState(IPlayerModelBase player, InAppPurchaseEvent eventState, InAppProductInfoBase productInfo)
        {
            // Store purchase time & price to purchase event
            eventState.PurchaseTime = player.CurrentTime;
            eventState.ReferencePrice = productInfo.Price;

            if (productInfo.HasDynamicContent)
            {
                if (player.PendingDynamicPurchaseContents.TryGetValue(eventState.ProductId, out PendingDynamicPurchaseContent pendingContent)
                 && pendingContent.Status == PendingDynamicPurchaseContentStatus.ConfirmedByServer)
                {
                    // Move dynamic content from pending contents lookup into this purchase event
                    player.PendingDynamicPurchaseContents.Remove(eventState.ProductId);
                    eventState.DynamicContent = pendingContent.Content;
                    eventState.GameProductAnalyticsId = pendingContent.GameProductAnalyticsId;
                    eventState.GamePurchaseAnalyticsContext = SerializePurchaseContext(pendingContent.GameAnalyticsContext);
                }
                else
                {
                    // Missing dynamic content. The purchase won't finish and receipt won't be validated, but a duplicate transaction check will be done on the server.
                    player.Log.Warning("Product {ProductId} is specified as having dynamic content, but dynamic state is not available (Status = {DynamicContentStatus}). This might be because it's a duplicate transaction; the server will check.", eventState.ProductId, pendingContent?.Status.ToString() ?? "<missing>");
                    eventState.HasMissingContent = true;
                }
            }
            else
            {
                // \note Existence is not checked, this is best-effort. Game integration may wait for confirmation but is not required to do so.
                if (player.PendingNonDynamicPurchaseContexts.TryGetValue(eventState.ProductId, out PendingNonDynamicPurchaseContext nonDynamicPurchaseContext))
                {
                    player.PendingNonDynamicPurchaseContexts.Remove(eventState.ProductId);

                    // \note We don't check the status, as we don't require the existense.
                    eventState.GameProductAnalyticsId = nonDynamicPurchaseContext.GameProductAnalyticsId;
                    eventState.GamePurchaseAnalyticsContext = SerializePurchaseContext(nonDynamicPurchaseContext.GameAnalyticsContext);
                }
            }

            // Game-specific handling
            eventState.OnPurchased(player, productInfo);

            // Store in pending transactions
            player.PendingInAppPurchases[eventState.TransactionId] = eventState;

            // Trigger validation on server
            player.ServerListenerCore.InAppPurchased(eventState, productInfo);
        }

        static MetaSerialized<PurchaseAnalyticsContext> SerializePurchaseContext(PurchaseAnalyticsContext context)
        {
            if (context == null)
                return default;
            else
                return new MetaSerialized<PurchaseAnalyticsContext>(context, MetaSerializationFlags.IncludeAll, logicVersion: null);
        }
    }

    /// <summary>
    /// Server has validated the pending IAP purchase. The validation either succeeded or failed.
    /// Failures can happen due to invalid or duplicate purchases.
    ///
    /// Depending on the outcome of the validation, the <see cref="InAppPurchaseEvent.Status"/>
    /// may be updated accordingly, or for invalid purchases, the purchase is immediately
    /// removed from the player's pending purchases.
    /// </summary>
    [ModelAction(ActionCodesCore.PlayerInAppPurchaseValidated)]
    public class PlayerInAppPurchaseValidated : PlayerUnsynchronizedServerActionCore<IPlayerModelBase>
    {
        public string                   TransactionId           { get; private set; }
        public InAppPurchaseStatus      Status                  { get; private set; }
        public bool                     IsDuplicateTransaction  { get; private set; }
        /// <summary>
        /// If present, overwrites <see cref="InAppPurchaseEvent.OrderId"/>.
        /// </summary>
        public string                   OrderId                 { get; private set; }
        public string                   OriginalTransactionId   { get; private set; }
        /// <summary>
        /// Null for non-subscription products.
        /// </summary>
        public SubscriptionQueryResult  SubscriptionQueryResult { get; private set; }
        public InAppPurchasePaymentType? PaymentType            { get; private set; }

        PlayerInAppPurchaseValidated() { }
        public PlayerInAppPurchaseValidated(string transactionId, InAppPurchaseStatus status, bool isDuplicateTransaction, string orderId, string originalTransactionId, SubscriptionQueryResult subscription, InAppPurchasePaymentType? paymentType)
        {
            TransactionId           = transactionId;
            Status                  = status;
            IsDuplicateTransaction  = isDuplicateTransaction;
            OrderId                 = orderId;
            OriginalTransactionId   = originalTransactionId;
            SubscriptionQueryResult = subscription;
            PaymentType             = paymentType;
        }

        public static PlayerInAppPurchaseValidated ForFailure(string transactionId, InAppPurchaseStatus status, bool isDuplicateTransaction)
        {
            return new PlayerInAppPurchaseValidated(transactionId, status, isDuplicateTransaction, orderId: null, originalTransactionId: null, subscription: null, paymentType: null);
        }

        public override MetaActionResult Execute(IPlayerModelBase player, bool commit)
        {
            if (!player.PendingInAppPurchases.TryGetValue(TransactionId, out InAppPurchaseEvent ev))
            {
                player.Log.Warning("Failed to execute {Action}: No such transactionId {TransactionId} in player.PendingInAppPurchases", nameof(PlayerInAppPurchaseValidated), TransactionId);
                return MetaActionResult.InvalidInAppTransactionId;
            }

            if (commit)
            {
                // Store the validation result to the pending purchase state.
                // - If validation was successful, the purchase should be claimed with PlayerClaimPendingInAppPurchase.
                // - If the purchase was a duplicate, it should be cleared with PlayerClearPendingDuplicateInAppPurchase.
                // - If the purchase was invalid, the pending state will be removed already during this action (see below).
                player.Log.Info("IAP validation for transaction {TransactionId} complete: status={Status}, isDuplicateTransaction={IsDuplicateTransaction}!", TransactionId, Status, IsDuplicateTransaction);

                // Store status
                ev.Status = Status;

                // Persist info about duplicacy in the purchase event, because if the purchase is interrupted, this info is needed on next launch
                ev.IsDuplicateTransaction = IsDuplicateTransaction;

                // Overwrite original client-provided OrderId by server-provided, validated value (if available).
                if (OrderId != null)
                    ev.OrderId = OrderId;

                ev.SetOriginalTransactionId(OriginalTransactionId);

                // Remember subscription info. This will be used when the purchase is claimed.
                ev.SubscriptionQueryResult = SubscriptionQueryResult;

                ev.PaymentType = PaymentType;

                // Game-specific handling
                ev.OnValidated(player);

                if (Status == InAppPurchaseStatus.ValidReceipt
                 || Status == InAppPurchaseStatus.ReceiptAlreadyUsed)
                {
                    player.ClientListenerCore.InAppPurchaseValidated(ev);
                }
                else if (Status == InAppPurchaseStatus.InvalidReceipt)
                {
                    // If invalid receipt, remove pending transaction right away; no separate claiming is done.
                    // The purchase isn't supposed to be confirmed to the store.
                    player.RemoveAndCatalogCompletedPendingInAppPurchase(TransactionId);

                    // Trigger client-side actions (such as showing an error popup)
                    player.ClientListenerCore.InAppPurchaseValidationFailed(ev);
                }
                else if (Status == InAppPurchaseStatus.MissingContent)
                {
                    player.Log.Warning("Purchase content was missing for transaction {TransactionId} and cannot be completed.", TransactionId);

                    player.RemoveAndCatalogCompletedPendingInAppPurchase(TransactionId);
                }
            }

            return MetaActionResult.Success;
        }
    }

    /// <summary>
    /// Client claims a pending fully-validated in-app product contents. The contents of the product
    /// are moved into the player's state and the pending IAP is cleared.
    /// </summary>
    [ModelAction(ActionCodesCore.PlayerClaimPendingInAppPurchase)]
    public class PlayerClaimPendingInAppPurchase : PlayerActionCore<IPlayerModelBase>
    {
        public string TransactionId { get; private set; }

        public PlayerClaimPendingInAppPurchase() { }
        public PlayerClaimPendingInAppPurchase(string transactionId) { TransactionId = transactionId; }

        public override MetaActionResult Execute(IPlayerModelBase player, bool commit)
        {
            if (string.IsNullOrEmpty(TransactionId))
            {
                player.Log.Warning("Failed to execute {Action}: Missing transactionId", nameof(PlayerClaimPendingInAppPurchase));
                return MetaActionResult.InvalidInAppTransactionId;
            }

            if (!player.PendingInAppPurchases.TryGetValue(TransactionId, out InAppPurchaseEvent ev))
            {
                player.Log.Warning("Failed to execute {Action}: player.PendingInAppPurchases does not contain {TransactionId}", nameof(PlayerClaimPendingInAppPurchase), TransactionId);
                return MetaActionResult.InvalidInAppTransactionState;
            }

            if (ev.Status != InAppPurchaseStatus.ValidReceipt)
            {
                player.Log.Warning("Failed to execute {Action}: invalid transaction status: transactionId={TransactionId}, status={Status}, expecting ValidReceipt", nameof(PlayerClaimPendingInAppPurchase), TransactionId, ev.Status);
                return MetaActionResult.InvalidInAppTransactionState;
            }

            if (commit)
            {
                // Update state

                // Claim dynamic content, if any
                if (ev.DynamicContent != null)
                {
                    player.Log.Info("Claiming dynamic content for pending IAP {TransactionId} / {ProductId}", TransactionId, ev.ProductId);
                    ev.DynamicContent.OnPurchased(player);

                    List<MetaPlayerRewardBase> dynamicRewards = ev.DynamicContent.PurchaseRewards;
                    if (dynamicRewards == null)
                    {
                        player.Log.Warning("A {DynamicContentType} for {TransactionId} / {ProductId} has null PurchaseRewards; treating as empty", ev.DynamicContent.GetType().Name, TransactionId, ev.ProductId);
                        dynamicRewards = new List<MetaPlayerRewardBase>();
                    }

                    MetaRewardSourceProvider rewardSourceProvider = IntegrationRegistry.Get<MetaRewardSourceProvider>();
                    foreach (MetaPlayerRewardBase reward in dynamicRewards)
                        reward.InvokeConsume(player, rewardSourceProvider.DeclareInAppRewardSource(ev));
                    // Remember resolved dynamic rewards for history
                    ev.ResolvedDynamicContent = new ResolvedPurchaseMetaRewards(dynamicRewards);
                }

                // Claim static configured content
                if (!player.GameConfig.InAppProducts.TryGetValue(ev.ProductId, out InAppProductInfoBase productInfo))
                    player.Log.Warning("ProductId {ProductId} doesn't exist in GameConfig, removing transaction {TransactionId} without giving loot", ev.ProductId, TransactionId);
                else if (productInfo.Type == InAppProductType.Subscription)
                {
                    if (ev.SubscriptionQueryResult != null)
                    {
                        SubscriptionQueryResult subscription = ev.SubscriptionQueryResult;

                        player.Log.Info("Claiming pending subscription IAP {TransactionId} / {ProductId}", TransactionId, ev.ProductId);
                        player.IAPSubscriptions.SetSubscriptionInstanceState(productInfo.ProductId, ev.OriginalTransactionId, subscription, clearReuseDisablement: true);

                        player.OnClaimedSubscriptionPurchase(ev, productInfo);
                    }
                    else
                        player.Log.Warning($"{nameof(ev.SubscriptionQueryResult)} is missing when claiming purchase {{TransactionId}} / {{ProductId}}", TransactionId, ev.ProductId);
                }
                else
                {
                    player.Log.Info("Claiming static content for pending IAP {TransactionId} / {ProductId}", TransactionId, ev.ProductId);
                    player.OnClaimedInAppProduct(ev, productInfo, out ResolvedPurchaseContentBase resolvedContent);
                    // Remember resolved content for history
                    ev.ResolvedContent = resolvedContent;
                }

                player.EventStream.Event(new PlayerEventInAppPurchased(ev.ProductId, ev.Platform, ev.TransactionId, ev.OrderId, ev.PlatformProductId, ev.ReferencePrice, ev.GameProductAnalyticsId, ev.PaymentType, ev.TryGetGamePurchaseAnalyticsContext(), ev.ResolvedContent, ev.ResolvedDynamicContent, ev.SubscriptionQueryResult?.State));

                // Remove claimed transaction
                player.RemoveAndCatalogCompletedPendingInAppPurchase(TransactionId);

                // Trigger client-side actions (such as confirming the purchase to the IAP store)
                player.ClientListenerCore.InAppPurchaseClaimed(ev);
            }

            return MetaActionResult.Success;
        }
    }

    /// <summary>
    /// Client clears a pending in-app purchase that failed due to being a duplicate transaction.
    /// The <see cref="InAppPurchaseEvent.Status"/> of the purchase must be <see cref="InAppPurchaseStatus.ReceiptAlreadyUsed"/>.
    /// </summary>
    [ModelAction(ActionCodesCore.PlayerClearPendingDuplicateInAppPurchase)]
    public class PlayerClearPendingDuplicateInAppPurchase : PlayerActionCore<IPlayerModelBase>
    {
        public string TransactionId { get; private set; }

        public PlayerClearPendingDuplicateInAppPurchase() { }
        public PlayerClearPendingDuplicateInAppPurchase(string transactionId) { TransactionId = transactionId; }

        public override MetaActionResult Execute(IPlayerModelBase player, bool commit)
        {
            if (string.IsNullOrEmpty(TransactionId))
            {
                player.Log.Warning("Failed to execute {Action}: Missing transactionId", nameof(PlayerClearPendingDuplicateInAppPurchase));
                return MetaActionResult.InvalidInAppTransactionId;
            }

            if (!player.PendingInAppPurchases.TryGetValue(TransactionId, out InAppPurchaseEvent ev))
            {
                player.Log.Warning("Failed to execute {Action}: player.PendingInAppPurchases does not contain {TransactionId}", nameof(PlayerClearPendingDuplicateInAppPurchase), TransactionId);
                return MetaActionResult.InvalidInAppTransactionState;
            }

            if (ev.Status != InAppPurchaseStatus.ReceiptAlreadyUsed)
            {
                player.Log.Warning("Failed to execute {Action}: invalid transaction status: transactionId={TransactionId}, status={Status}, expecting {ExpectedStatus}", nameof(PlayerClearPendingDuplicateInAppPurchase), TransactionId, ev.Status, nameof(InAppPurchaseStatus.ReceiptAlreadyUsed));
                return MetaActionResult.InvalidInAppTransactionState;
            }

            if (commit)
            {
                player.Log.Info("Clearing pending duplicate IAP {TransactionId} / {ProductId}", TransactionId, ev.ProductId);

                player.RemoveAndCatalogCompletedPendingInAppPurchase(TransactionId);

                // Trigger client-side actions (such as confirming the purchase to the IAP store)
                player.ClientListenerCore.DuplicateInAppPurchaseCleared(ev);
            }

            return MetaActionResult.Success;
        }
    }

    /// <summary>
    /// Client claims to have attempted and failed (or cancelled) an in-app purchase.
    /// </summary>
    [ModelAction(ActionCodesCore.PlayerInAppPurchaseClientRefused)]
    public class PlayerInAppPurchaseClientRefused : PlayerActionCore<IPlayerModelBase>
    {
        public InAppProductId                   ProductId           { get; private set; }
        public InAppPurchaseClientRefuseReason  Reason              { get; private set; }

        PlayerInAppPurchaseClientRefused() { }
        public PlayerInAppPurchaseClientRefused(InAppProductId productId, InAppPurchaseClientRefuseReason reason)
        {
            ProductId = productId;
            Reason = reason;
        }

        public override MetaActionResult Execute(IPlayerModelBase player, bool commit)
        {
            // The failures may be non-fatal, client may report spurious errors and retry. Don't alter the flow, just record an analytics event.
            // Try to get the context from state. These might fail but in that case we just use null values.

            string                      gameProductId       = null;
            PurchaseAnalyticsContext    gamePurchaseContext = null;

            if (player.GameConfig.InAppProducts.TryGetValue(ProductId, out InAppProductInfoBase productInfo))
            {
                if (productInfo.HasDynamicContent)
                {
                    if (player.PendingDynamicPurchaseContents.TryGetValue(ProductId, out PendingDynamicPurchaseContent pendingDynamicContent))
                    {
                        gameProductId = pendingDynamicContent.GameProductAnalyticsId;
                        gamePurchaseContext = pendingDynamicContent.GameAnalyticsContext;
                    }
                }
                else
                {
                    if (player.PendingNonDynamicPurchaseContexts.TryGetValue(ProductId, out PendingNonDynamicPurchaseContext pendingContext))
                    {
                        gameProductId = pendingContext.GameProductAnalyticsId;
                        gamePurchaseContext = pendingContext.GameAnalyticsContext;
                    }
                }
            }

            if (commit)
            {
                player.EventStream.Event(new PlayerEventInAppPurchaseClientRefused(Reason, ProductId, gameProductId, gamePurchaseContext));
            }

            return MetaActionResult.Success;
        }
    }
}
