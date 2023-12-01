// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Analytics;
using Metaplay.Core.EventLog;
using Metaplay.Core.InAppPurchase;
using Metaplay.Core.Math;
using Metaplay.Core.Model;
using Metaplay.Core.Serialization;
using Metaplay.Core.Session;
using System;
using System.Collections.Generic;
using System.Linq;
using static System.FormattableString;

namespace Metaplay.Core.Player
{
    // Substitute event for when an event log entry payload fails to deserialize
    [AnalyticsEvent(AnalyticsEventCodesCore.PlayerEventDeserializationFailureSubstitute, displayName: "<Failed to Deserialize Event!>", docString: AnalyticsEventDocsCore.PlayerEventDeserializationFailureSubstitute, sendToAnalytics: false)]
    public class PlayerEventDeserializationFailureSubstitute : PlayerEventBase, IEntityEventPayloadDeserializationFailureSubstitute
    {
        [MetaMember(1)] public EntityEventDeserializationFailureInfo FailureInfo { get; private set; }

        public override string EventDescription => FailureInfo?.DescriptionForEvent ?? "Failure info not initialized.";

        public void Initialize(MetaMemberDeserializationFailureParams failureParams)
        {
            FailureInfo = new EntityEventDeserializationFailureInfo(failureParams);
        }
    }

    // Player name change event
    [AnalyticsEvent(AnalyticsEventCodesCore.PlayerNameChanged, displayName: "Name Changed", docString: AnalyticsEventDocsCore.PlayerNameChanged)]
    public class PlayerEventNameChanged : PlayerEventBase
    {
        [MetaSerializable]
        public enum ChangeSource
        {
            Player  = 0,
            Admin   = 1,
        }

        [MetaMember(1)] public ChangeSource Source  { get; private set; }
        [MetaMember(2)] public string       OldName { get; private set; }
        [MetaMember(3)] public string       NewName { get; private set; }

        public override string EventDescription => $"From {OldName} to {NewName} by {Source}.";

        public PlayerEventNameChanged(){ }
        public PlayerEventNameChanged(ChangeSource source, string oldName, string newName)
        {
            Source  = source;
            OldName = oldName;
            NewName = newName;
        }
    }

    // Player pending dynamic IAP content event
    [AnalyticsEvent(AnalyticsEventCodesCore.PlayerPendingDynamicPurchaseContentAssigned, displayName: "In App Purchase Started (with dynamic content)", docString: AnalyticsEventDocsCore.PlayerPendingDynamicPurchaseContentAssigned)]
    [AnalyticsEventKeywords(AnalyticsEventKeywordsCore.InAppPurchase)]
    public class PlayerEventPendingDynamicPurchaseContentAssigned : PlayerEventBase
    {
        [MetaMember(1)] public InAppProductId           ProductId               { get; private set; }

        [FirebaseAnalyticsIgnore] // unsupported aggregate type
        [MetaMember(2)] public DynamicPurchaseContent   Content                 { get; private set; }

        [MetaMember(3)] public string                   DeviceId                { get; private set; }
        [MetaMember(4)] public string                   GameProductAnalyticsId  { get; private set; }
        [FirebaseAnalyticsIgnore] // unsupported aggregate type
        [MetaMember(5)] public PurchaseAnalyticsContext PurchaseContext         { get; private set; }

        public override string EventDescription => $"{ProductId}: {GameProductAnalyticsId} ({PurchaseContext?.GetDisplayStringForEventLog()})";

        PlayerEventPendingDynamicPurchaseContentAssigned(){ }
        public PlayerEventPendingDynamicPurchaseContentAssigned(InAppProductId productId, DynamicPurchaseContent content, string deviceId, string gameProductAnalyticsId, PurchaseAnalyticsContext purchaseContext)
        {
            ProductId = productId ?? throw new ArgumentNullException(nameof(productId));
            Content = content ?? throw new ArgumentNullException(nameof(content));
            DeviceId = deviceId;
            GameProductAnalyticsId = gameProductAnalyticsId;
            PurchaseContext = purchaseContext;
        }
    }

    [AnalyticsEvent(AnalyticsEventCodesCore.PlayerPendingStaticPurchaseContextAssigned, displayName: "In App Purchase Started", docString: AnalyticsEventDocsCore.PlayerPendingStaticPurchaseContextAssigned)]
    [AnalyticsEventKeywords(AnalyticsEventKeywordsCore.InAppPurchase)]
    public class PlayerEventPendingStaticPurchaseContextAssigned : PlayerEventBase
    {
        [MetaMember(1)] public InAppProductId           ProductId               { get; private set; }
        [MetaMember(2)] public string                   DeviceId                { get; private set; }
        [MetaMember(3)] public string                   GameProductAnalyticsId  { get; private set; }
        [FirebaseAnalyticsIgnore] // unsupported aggregate type
        [MetaMember(4)] public PurchaseAnalyticsContext PurchaseContext         { get; private set; }

        public override string EventDescription => $"{ProductId}: {GameProductAnalyticsId} ({PurchaseContext?.GetDisplayStringForEventLog()})";

        PlayerEventPendingStaticPurchaseContextAssigned(){ }
        public PlayerEventPendingStaticPurchaseContextAssigned(InAppProductId productId, string deviceId, string gameProductAnalyticsId, PurchaseAnalyticsContext purchaseContext)
        {
            ProductId = productId ?? throw new ArgumentNullException(nameof(productId));
            DeviceId = deviceId;
            GameProductAnalyticsId = gameProductAnalyticsId;
            PurchaseContext = purchaseContext;
        }
    }

    // Player Facebook authentication was revoked with a webhook
    [AnalyticsEvent(AnalyticsEventCodesCore.PlayerFacebookAuthenticationRevoked, docString: AnalyticsEventDocsCore.PlayerFacebookAuthenticationRevoked)]
    public class PlayerEventFacebookAuthenticationRevoked : PlayerEventBase
    {
        [MetaSerializable]
        public enum RevocationSource
        {
            /// <summary>
            /// Data Deletion Request Callback
            /// </summary>
            DataDeletionRequest = 0,

            /// <summary>
            /// Deauthorize Callback URL
            /// </summary>
            DeauthorizationRequest = 1,
        }

        [MetaMember(1)] public string           ConfirmationCode    { get; private set; }
        [MetaMember(2)] public RevocationSource Source              { get; private set; }

        public override string EventDescription
        {
            get
            {
                switch (Source)
                {
                    case RevocationSource.DataDeletionRequest:
                        return $"Facebook Login Data Deletion request completed with confirmation code {ConfirmationCode}.";
                    case RevocationSource.DeauthorizationRequest:
                        return $"Facebook Deauthorization request completed.";
                    default:
                        return null;
                }
            }
        }

        PlayerEventFacebookAuthenticationRevoked() { }
        public PlayerEventFacebookAuthenticationRevoked(RevocationSource source, string confirmationCode)
        {
            switch (source)
            {
                case RevocationSource.DataDeletionRequest:
                {
                    if (confirmationCode == null)
                        throw new ArgumentNullException(nameof(confirmationCode));
                    break;
                }

                case RevocationSource.DeauthorizationRequest:
                {
                    if (confirmationCode != null)
                        throw new ArgumentException("deauth cannot have a confirmation code. Confirmation is only used in Data Deletion.", nameof(confirmationCode));
                    break;
                }

                default:
                    throw new ArgumentException("unknown source", nameof(source));
            }

            Source = source;
            ConfirmationCode = confirmationCode;
        }
    }

    // Player authentication records have changed due to auth conflict resolution
    [AnalyticsEvent(AnalyticsEventCodesCore.PlayerSocialAuthConflictResolved, docString: AnalyticsEventDocsCore.PlayerSocialAuthConflictResolved)]
    public class PlayerEventSocialAuthConflictResolved : PlayerEventBase
    {
        [MetaSerializable]
        public enum ResolutionOperation
        {
            /// <summary>
            /// Device authentication <see cref="DeviceIdKey"/> was removed and moved to <see cref="OtherPlayerId"/>. This was due to sync with <see cref="SocialIdKey"/>.
            /// </summary>
            DeviceMigrationSource = 0,

            /// <summary>
            /// Device authentication <see cref="DeviceIdKey"/> was added and removed from <see cref="OtherPlayerId"/>. This was due to sync with <see cref="SocialIdKey"/>.
            /// </summary>
            DeviceMigrationDestination = 1,

            /// <summary>
            /// Social authentication <see cref="SocialIdKey"/> was removed and moved to <see cref="OtherPlayerId"/>.
            /// </summary>
            SocialMigrationSource = 2,

            /// <summary>
            /// Social authentication <see cref="SocialIdKey"/> was migrated from <see cref="OtherPlayerId"/>.
            /// </summary>
            SocialMigrationDestination = 3,

            /// <summary>
            /// Social authentication <see cref="SocialIdKey"/> was added since it is a new migration key resolved from existing key.
            /// </summary>
            SocialMigrationKeyAdded = 4,
        }
        [MetaMember(1)] public ResolutionOperation  Operation       { get; private set; }
        [MetaMember(2)] public AuthenticationKey    DeviceIdKey     { get; private set; }
        [MetaMember(3)] public AuthenticationKey    SocialIdKey     { get; private set; }
        [MetaMember(4)] public EntityId             OtherPlayerId   { get; private set; }

        public override string EventDescription
        {
            get
            {
                switch (Operation)
                {
                    case ResolutionOperation.DeviceMigrationSource:         return $"Device removed.\nDevice {DeviceIdKey} migrated from this player onto {OtherPlayerId}.\nSynchronized with {SocialIdKey} social login.";
                    case ResolutionOperation.DeviceMigrationDestination:    return $"Device added.\nDevice {DeviceIdKey} migrated to this player from {OtherPlayerId}.\nSynchronized with {SocialIdKey} social login.";
                    case ResolutionOperation.SocialMigrationSource:         return $"Social login removed.\nLogin account {SocialIdKey} migrated from this player onto {OtherPlayerId}.";
                    case ResolutionOperation.SocialMigrationDestination:    return $"Social login added.\nLogin account {SocialIdKey} migrated to this player from {OtherPlayerId}.";
                    case ResolutionOperation.SocialMigrationKeyAdded:       return $"Social login added.\nLogin account {SocialIdKey} was added to support migrations from existing social account.";
                }
                return "";
            }
        }

        public PlayerEventSocialAuthConflictResolved(){ }
        public PlayerEventSocialAuthConflictResolved(ResolutionOperation operation, AuthenticationKey deviceIdKey, AuthenticationKey socialIdKey, EntityId otherPlayerId)
        {
            Operation = operation;
            DeviceIdKey = deviceIdKey;
            SocialIdKey = socialIdKey;
            OtherPlayerId = otherPlayerId;
        }
    }

    //[AnalyticsEvent(AnalyticsEventCodesCore.PlayerRegistered, docString: AnalyticsEventDocsCore.PlayerRegistered)]
    //public class PlayerEventRegistered : PlayerEventBase
    //{
    //    public override string EventDescription => "";
    //}

    [AnalyticsEvent(AnalyticsEventCodesCore.PlayerClientConnected, docString: AnalyticsEventDocsCore.PlayerClientConnected)]
    [AnalyticsEventKeywords(AnalyticsEventKeywordsCore.Session)]
    public class PlayerEventClientConnected : PlayerEventBase
    {
        [MetaMember(7)] public SessionToken         SessionToken    { get; private set; }
        [MetaMember(1)] public string               DeviceId        { get; private set; }
        [MetaMember(2)] public string               DeviceModel     { get; private set; }
        [MetaMember(3)] public int                  LogicVersion    { get; private set; }
        [FirebaseAnalyticsIgnore] // unsupported aggregate type
        [MetaMember(4)] public PlayerTimeZoneInfo   TimeZoneInfo    { get; private set; }
        [FirebaseAnalyticsIgnore] // unsupported aggregate type
        [MetaMember(5)] public PlayerLocation?      Location        { get; private set; }
        [MetaMember(6)] public string               ClientVersion   { get; private set; }
        [MetaMember(8)] public int                  SessionNumber   { get; private set; }
        [MetaMember(9)] public AuthenticationKey    AuthKey         { get; private set; }
        // \todo [petri] more info: session resume info, LoginDebugDiagnostics? something else?

        //public override string EventDescription => Invariant($"SessionToken={SessionToken}, DeviceId={DeviceId}, DeviceModel={DeviceModel}, LogicVersion={LogicVersion}, TimeZoneInfo={TimeZoneInfo}, Location={Location}, ClientVersion={ClientVersion}");
        public override string EventDescription => Invariant($"Client version {LogicVersion} connection from {(Location.HasValue ? Location.Value.Country.IsoCode : "unknown location")}.");

        private PlayerEventClientConnected() { }
        public PlayerEventClientConnected(SessionToken sessionToken, string deviceId, string deviceModel, int logicVersion, PlayerTimeZoneInfo timeZoneInfo, PlayerLocation? location, string clientVersion, int sessionNumber, AuthenticationKey authKey)
        {
            SessionToken    = sessionToken;
            DeviceId        = deviceId;
            DeviceModel     = deviceModel;
            LogicVersion    = logicVersion;
            TimeZoneInfo    = timeZoneInfo;
            Location        = location;
            ClientVersion   = clientVersion;
            SessionNumber   = sessionNumber;
            AuthKey         = authKey;
        }
    }

    [AnalyticsEvent(AnalyticsEventCodesCore.PlayerClientDisconnected, docString: AnalyticsEventDocsCore.PlayerClientDisconnected)]
    [AnalyticsEventKeywords(AnalyticsEventKeywordsCore.Session)]
    public class PlayerEventClientDisconnected : PlayerEventBase
    {
        [MetaMember(3)] public SessionToken SessionToken { get; private set; }
        // \todo [petri] add more parameters, reason for disconnect
        //[MetaMember(1)] public MetaTime     ConnectedAt { get; private set; }
        //[MetaMember(2)] public MetaDuration Duration    { get; private set; }

        public override string EventDescription => $"Client connection lost.";

        public PlayerEventClientDisconnected() { }
        public PlayerEventClientDisconnected(SessionToken sessionToken/*, MetaTime connectedAt, MetaDuration duration*/)
        {
            SessionToken = sessionToken;
            //ConnectedAt = connectedAt;
            //Duration    = duration;
        }
    }

    //// Meta-event for combining multiple technical sessions to one logical one (allow brief disconnects)
    //[AnalyticsEvent(AnalyticsEventCodesCore.PlayerSessionStatistics, docString: AnalyticsEventDocsCore.PlayerSessionStatistics)]
    //public class PlayerEventSessionStatistics : PlayerEventBase
    //{
    //    [MetaMember(1)] public MetaTime StartTime   { get; private set; }
    //    [MetaMember(2)] public MetaTime EndTime     { get; private set; }
    //    [MetaMember(3)] public MetaTime Duration    { get; private set; }
    //    [MetaMember(4)] public int      NumLogins   { get; private set; } // Number of logins during the logical session (\todo [petri] better stats)
    //    // \todo [petri] more members?
    //
    //    public override string EventDescription => throw new NotImplementedException();
    //
    //    private PlayerEventSessionStatistics() { }
    //    //public PlayerEventSessionStatistics(..) { } \todo [petri] implement
    //}

    [AnalyticsEvent(AnalyticsEventCodesCore.PlayerInAppPurchased, schemaVersion: 2, docString: AnalyticsEventDocsCore.PlayerInAppPurchased)]
    [AnalyticsEventKeywords(AnalyticsEventKeywordsCore.InAppPurchase)]
    public class PlayerEventInAppPurchased : PlayerEventBase
    {
        [MetaMember(2)] public InAppProductId               ProductId               { get; private set; }
        [MetaMember(3)] public InAppPurchasePlatform        Platform                { get; private set; }
        [MetaMember(4)] public string                       TransactionId           { get; private set; }
        [MetaMember(11)] public string                      OrderId                 { get; private set; }
        [MetaMember(5)] public string                       PlatformProductId       { get; private set; }
        [MetaMember(6)] public F64                          ReferencePrice          { get; private set; }
        [MetaMember(7)] public string                       GameProductAnalyticsId  { get; private set; }
        [MetaMember(12)] public InAppPurchasePaymentType?   PaymentType             { get; private set; }
        [FirebaseAnalyticsIgnore] // unsupported aggregate type
        [MetaMember(8)] public PurchaseAnalyticsContext     PurchaseContext         { get; private set; }
        [FirebaseAnalyticsIgnore] // unsupported aggregate type
        [MetaMember(1)] public ResolvedPurchaseContentBase  ResolvedContent         { get; private set; }
        [FirebaseAnalyticsIgnore] // unsupported aggregate type
        [MetaMember(9)] public ResolvedPurchaseContentBase  ResolvedDynamicContent  { get; private set; }
        [FirebaseAnalyticsIgnore] // unsupported aggregate type
        [MetaMember(10)] public SubscriptionInstanceState?  Subscription            { get; private set; }

        public override string EventDescription
        {
            get
            {
                ResolvedPurchaseContentBase content = ResolvedContent ?? ResolvedDynamicContent;
                string contentString = PrettyPrint.Compact(content).ToString(); // \todo [petri] is this safe -- how to do it?
                return $"{GameProductAnalyticsId}: {contentString} ({PurchaseContext?.GetDisplayStringForEventLog()})";
            }
        }

        PlayerEventInAppPurchased() { }
        public PlayerEventInAppPurchased(
            InAppProductId productId,
            InAppPurchasePlatform platform,
            string transactionId,
            string orderId,
            string platformProductId,
            F64 referencePrice,
            string gameProductAnalyticsId,
            InAppPurchasePaymentType? paymentType,
            PurchaseAnalyticsContext purchaseContext,
            ResolvedPurchaseContentBase resolvedContent,
            ResolvedPurchaseContentBase resolvedDynamicContent,
            SubscriptionInstanceState? subscription)
        {
            ProductId = productId;
            Platform = platform;
            TransactionId = transactionId;
            OrderId = orderId;
            PlatformProductId = platformProductId;
            ReferencePrice = referencePrice;
            GameProductAnalyticsId = gameProductAnalyticsId;
            PaymentType = paymentType;
            PurchaseContext = purchaseContext;
            ResolvedContent = resolvedContent;
            ResolvedDynamicContent = resolvedDynamicContent;
            Subscription = subscription;
        }
    }

    [AnalyticsEvent(AnalyticsEventCodesCore.PlayerInAppValidationStarted, docString: AnalyticsEventDocsCore.PlayerInAppValidationStarted)]
    [AnalyticsEventKeywords(AnalyticsEventKeywordsCore.InAppPurchase)]
    public class PlayerEventInAppValidationStarted : PlayerEventBase
    {
        [MetaMember(1)] public InAppProductId               ProductId               { get; private set; }
        [MetaMember(2)] public InAppPurchasePlatform        Platform                { get; private set; }
        [MetaMember(3)] public string                       TransactionId           { get; private set; }
        [MetaMember(8)] public string                       OrderId                 { get; private set; }
        [MetaMember(4)] public string                       PlatformProductId       { get; private set; }
        [MetaMember(5)] public F64                          ReferencePrice          { get; private set; }
        [MetaMember(6)] public string                       GameProductAnalyticsId  { get; private set; }
        [FirebaseAnalyticsIgnore] // unsupported aggregate type
        [MetaMember(7)] public PurchaseAnalyticsContext     PurchaseContext         { get; private set; }

        public override string EventDescription => $"Validating {GameProductAnalyticsId} ({ProductId}:{Platform}:{PlatformProductId}:{PurchaseContext?.GetDisplayStringForEventLog()})";

        PlayerEventInAppValidationStarted() { }
        public PlayerEventInAppValidationStarted(InAppProductId productId, InAppPurchasePlatform platform, string transactionId, string orderId, string platformProductId, F64 referencePrice, string gameProductAnalyticsId, PurchaseAnalyticsContext purchaseContext)
        {
            ProductId = productId;
            Platform = platform;
            TransactionId = transactionId;
            OrderId = orderId;
            PlatformProductId = platformProductId;
            ReferencePrice = referencePrice;
            GameProductAnalyticsId = gameProductAnalyticsId;
            PurchaseContext = purchaseContext;
        }
    }

    [AnalyticsEvent(AnalyticsEventCodesCore.PlayerInAppValidationComplete, docString: AnalyticsEventDocsCore.PlayerInAppValidationComplete)]
    [AnalyticsEventKeywords(AnalyticsEventKeywordsCore.InAppPurchase)]
    public class PlayerEventInAppValidationComplete : PlayerEventBase
    {
        [MetaSerializable]
        public enum ValidationResult
        {
            Valid = 0,
            Invalid = 1,
            Duplicate = 2,
            MissingContent = 3,
        }
        [MetaMember(1)] public ValidationResult             Result                  { get; private set; }
        [MetaMember(2)] public InAppProductId               ProductId               { get; private set; }
        [MetaMember(3)] public InAppPurchasePlatform        Platform                { get; private set; }
        [MetaMember(4)] public string                       TransactionId           { get; private set; }
        [MetaMember(9)] public string                       OrderId                 { get; private set; }
        [MetaMember(5)] public string                       PlatformProductId       { get; private set; }
        [MetaMember(6)] public F64                          ReferencePrice          { get; private set; }
        [MetaMember(7)] public string                       GameProductAnalyticsId  { get; private set; }
        [MetaMember(10)] public InAppPurchasePaymentType?   PaymentType             { get; private set; }
        [FirebaseAnalyticsIgnore] // unsupported aggregate type
        [MetaMember(8)] public PurchaseAnalyticsContext     PurchaseContext         { get; private set; }

        public override string EventDescription
        {
            get
            {
                switch (Result)
                {
                    case ValidationResult.Valid:            return $"Validated receipt for {GameProductAnalyticsId} ({ProductId}:{Platform}:{PlatformProductId}:{PurchaseContext?.GetDisplayStringForEventLog()})";
                    case ValidationResult.Invalid:          return $"INVALID receipt for {GameProductAnalyticsId} ({ProductId}:{Platform}:{PlatformProductId}:{PurchaseContext?.GetDisplayStringForEventLog()})";
                    case ValidationResult.Duplicate:        return $"DUPLICATE receipt for {GameProductAnalyticsId} ({ProductId}:{Platform}:{PlatformProductId}:{PurchaseContext?.GetDisplayStringForEventLog()})";
                    case ValidationResult.MissingContent:   return $"MISSING DYNAMIC CONTENT for {GameProductAnalyticsId} ({ProductId}:{Platform}:{PlatformProductId}:{PurchaseContext?.GetDisplayStringForEventLog()})";
                }
                throw new InvalidOperationException("invalid state");
            }
        }

        PlayerEventInAppValidationComplete() { }
        public PlayerEventInAppValidationComplete(ValidationResult result, InAppProductId productId, InAppPurchasePlatform platform, string transactionId, string orderId, string platformProductId, F64 referencePrice, string gameProductAnalyticsId, InAppPurchasePaymentType? paymentType, PurchaseAnalyticsContext purchaseContext)
        {
            Result = result;
            ProductId = productId;
            Platform = platform;
            TransactionId = transactionId;
            OrderId = orderId;
            PlatformProductId = platformProductId;
            ReferencePrice = referencePrice;
            GameProductAnalyticsId = gameProductAnalyticsId;
            PaymentType = paymentType;
            PurchaseContext = purchaseContext;
        }
    }

    [AnalyticsEvent(AnalyticsEventCodesCore.PlayerInAppPurchaseClientRefused, displayName: "In App Purchase Refused by Client", docString: AnalyticsEventDocsCore.PlayerInAppPurchaseClientRefused)]
    [AnalyticsEventKeywords(AnalyticsEventKeywordsCore.InAppPurchase)]
    public class PlayerEventInAppPurchaseClientRefused : PlayerEventBase
    {
        [MetaMember(1)] public InAppPurchaseClientRefuseReason  Reason                  { get; private set; }
        [MetaMember(2)] public InAppProductId                   ProductId               { get; private set; }
        [MetaMember(3)] public string                           GameProductAnalyticsId  { get; private set; }

        [FirebaseAnalyticsIgnore] // unsupported aggregate type
        [MetaMember(4)] public PurchaseAnalyticsContext         PurchaseContext         { get; private set; }

        public override string EventDescription => $"Purchase of {GameProductAnalyticsId} ({ProductId}) refused by client: {Reason}. ({PurchaseContext?.GetDisplayStringForEventLog()})";

        PlayerEventInAppPurchaseClientRefused() { }
        public PlayerEventInAppPurchaseClientRefused(InAppPurchaseClientRefuseReason reason, InAppProductId productId, string gameProductAnalyticsId, PurchaseAnalyticsContext purchaseContext)
        {
            Reason = reason;
            ProductId = productId;
            GameProductAnalyticsId = gameProductAnalyticsId;
            PurchaseContext = purchaseContext;
        }
    }

    [AnalyticsEvent(AnalyticsEventCodesCore.PlayerIAPSubscriptionStateUpdated, displayName: "IAP Subscription State Updated", docString: AnalyticsEventDocsCore.PlayerIAPSubscriptionStateUpdated)]
    [AnalyticsEventKeywords(AnalyticsEventKeywordsCore.InAppPurchase)]
    public class PlayerEventIAPSubscriptionStateUpdated : PlayerEventBase
    {
        [MetaMember(1)] public InAppProductId               ProductId                   { get; private set; }
        [MetaMember(2)] public string                       OriginalTransactionId       { get; private set; }
        [FirebaseAnalyticsIgnore] // unsupported aggregate type
        [MetaMember(3)] public SubscriptionInstanceState?   SubscriptionInstanceState   { get; private set; }
        [MetaMember(4)] public MetaTime                     OverallExpirationTime       { get; private set; }
        [MetaMember(5)] public SubscriptionRenewalStatus    OverallRenewalStatus        { get; private set; }

        public override string EventDescription => $"Subscription of {ProductId} was updated; expiration time: {OverallExpirationTime}, renewal status: {OverallRenewalStatus}";

        PlayerEventIAPSubscriptionStateUpdated() { }
        public PlayerEventIAPSubscriptionStateUpdated(InAppProductId productId, string originalTransactionId, SubscriptionInstanceState? subscriptionInstanceState, MetaTime overallExpirationTime, SubscriptionRenewalStatus overallRenewalStatus)
        {
            ProductId = productId;
            OriginalTransactionId = originalTransactionId;
            SubscriptionInstanceState = subscriptionInstanceState;
            OverallExpirationTime = overallExpirationTime;
            OverallRenewalStatus = overallRenewalStatus;
        }
    }

    [AnalyticsEvent(AnalyticsEventCodesCore.PlayerIAPSubscriptionDisabledDueToReuse, displayName: "IAP Subscription Disabled Due To Reuse", docString: AnalyticsEventDocsCore.PlayerIAPSubscriptionDisabledDueToReuse)]
    [AnalyticsEventKeywords(AnalyticsEventKeywordsCore.InAppPurchase)]
    public class PlayerEventIAPSubscriptionDisabledDueToReuse : PlayerEventBase
    {
        [MetaMember(1)] public InAppProductId               ProductId                   { get; private set; }
        [MetaMember(2)] public string                       OriginalTransactionId       { get; private set; }

        public override string EventDescription => $"Subscription of {ProductId} was disabled because the same purchase was reused on another player account";

        PlayerEventIAPSubscriptionDisabledDueToReuse() { }
        public PlayerEventIAPSubscriptionDisabledDueToReuse(InAppProductId productId, string originalTransactionId)
        {
            ProductId = productId;
            OriginalTransactionId = originalTransactionId;
        }
    }

    [AnalyticsEvent(AnalyticsEventCodesCore.PlayerExperimentAssignment, displayName: "Assigned into an Experiment", docString: AnalyticsEventDocsCore.PlayerExperimentAssignment)]
    public class PlayerEventExperimentAssignment : PlayerEventBase
    {
        [MetaSerializable]
        public enum ChangeSource
        {
            AutomaticAssign = 0,
            Admin           = 1,
        }

        [MetaMember(1)] public ChangeSource Source                  { get; private set; }
        [MetaMember(2)] public string       ExperimentId            { get; private set; }
        [MetaMember(3)] public string       VariantId               { get; private set; }
        [MetaMember(4)] public string       ExperimentAnalyticsId   { get; private set; }
        [MetaMember(5)] public string       VariantAnalyticsId      { get; private set; }

        public override string EventDescription
        {
            get
            {
                string variantName = $"{VariantId ?? "control"} group";
                switch (Source)
                {
                    case ChangeSource.AutomaticAssign:
                        return $"Automatically assigned into {ExperimentId} Experiment {variantName}.";
                    case ChangeSource.Admin:
                        return $"Admin assigned into {ExperimentId} Experiment {variantName}.";
                    default:
                        return null;
                }
            }
        }

        PlayerEventExperimentAssignment() { }
        public PlayerEventExperimentAssignment(ChangeSource source, PlayerExperimentId experimentId, ExperimentVariantId variantId, string experimentAnalyticsId, string variantAnalyticsId)
        {
            Source = source;
            ExperimentId = experimentId.ToString();
            VariantId = variantId?.ToString();
            ExperimentAnalyticsId = experimentAnalyticsId;
            VariantAnalyticsId = variantAnalyticsId;
        }
    }

    [AnalyticsEvent(AnalyticsEventCodesCore.PlayerDeleted, displayName: "Player is Deleted", docString: AnalyticsEventDocsCore.PlayerDeleted)]
    public class PlayerEventDeleted : PlayerEventBase
    {
        public override string EventDescription => "Player is deleted.";

        public PlayerEventDeleted() { }
    }

    [AnalyticsEvent(AnalyticsEventCodesCore.PlayerModelSchemaMigrated, displayName: "Model Schema Migrated", docString: AnalyticsEventDocsCore.PlayerModelSchemaMigrated)]
    public class PlayerEventModelSchemaMigrated : PlayerEventBase
    {
        [MetaMember(1)] public int  FromVersion { get; private set; }
        [MetaMember(2)] public int  ToVersion   { get; private set; }

        public override string EventDescription => Invariant($"Model schema was migrated from v{FromVersion} to v{ToVersion}");

        PlayerEventModelSchemaMigrated() { }
        public PlayerEventModelSchemaMigrated(int fromVersion, int toVersion)
        {
            FromVersion = fromVersion;
            ToVersion = toVersion;
        }
    }

    [AnalyticsEvent(AnalyticsEventCodesCore.PlayerIncidentRecorded, displayName: "Incident Recorded", docString: AnalyticsEventDocsCore.PlayerIncidentReported)]
    public class PlayerEventIncidentRecorded : PlayerEventBase
    {
        [MetaMember(1)] public string   IncidentId          { get; private set; }
        [MetaMember(2)] public MetaTime OccurredAt          { get; private set; }
        [MetaMember(3)] public string   Type                { get; private set; }
        [MetaMember(4)] public string   SubType             { get; private set; }
        [MetaMember(5)] public string   Reason              { get; private set; }
        [MetaMember(6)] public string   Fingerprint         { get; private set; }

        public override string EventDescription => $"Player incident occurred: {Type} / {SubType}";

        PlayerEventIncidentRecorded() { }
        public PlayerEventIncidentRecorded(string incidentId, MetaTime occurredAt, string type, string subType, string reason, string fingerprint)
        {
            IncidentId = incidentId;
            OccurredAt = occurredAt;
            Type = type;
            SubType = subType;
            Reason = reason;
            Fingerprint = fingerprint;
        }
    }

    // \todo [petri] add social auth events

    // \todo [petri] add IAP events

    // \todo [petri] add player guild-related events (join, leave, search, recommendations, interaction, ..)
}
