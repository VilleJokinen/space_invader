// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Application;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Cloud.Services;
using Metaplay.Cloud.Sharding;
using Metaplay.Core;
using Metaplay.Core.InAppPurchase;
using Metaplay.Core.Model;
using Metaplay.Core.TypeCodes;
using Metaplay.Server.Services;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using static Metaplay.Cloud.Sharding.EntityShard;
using static System.FormattableString;

namespace Metaplay.Server.InAppPurchase
{
    /// <summary>
    /// An error happened during the validation of an in-app purchase receipt.
    /// </summary>
    //public class InAppPurchaseValidationError : Exception
    //{
    //    public readonly HttpStatusCode  StatusCode;
    //    public readonly string          Content;
    //
    //    public InAppPurchaseValidationError(HttpStatusCode statusCode, string content)
    //    {
    //        StatusCode  = statusCode;
    //        Content     = content;
    //    }
    //}

    /// <summary>
    /// Request validation of an in-app purchase event (receipt).
    ///
    /// Sent to <see cref="InAppPurchaseValidatorActor"/>, responded to with <see cref="ValidateInAppPurchaseResponse"/>.
    /// </summary>
    [MetaMessage(MessageCodesCore.ValidateInAppPurchaseRequest, MessageDirection.ServerInternal)]
    public class ValidateInAppPurchaseRequest : MetaMessage
    {
        public InAppPurchaseTransactionInfo TransactionInfo { get; private set; }

        ValidateInAppPurchaseRequest() { }
        public ValidateInAppPurchaseRequest(InAppPurchaseTransactionInfo transactionInfo){ TransactionInfo = transactionInfo; }
    }

    /// <summary>
    /// Describes the result of IAP validation.
    /// </summary>
    [MetaSerializable]
    public enum InAppPurchaseValidationResult
    {
        /// <summary> Purchase is valid (though it might still be a duplicate of a past purchase; this is determined by other means, outside InAppPurchaseValidatorActor). </summary>
        Valid,
        /// <summary> Purchase is invalid: transaction id does not match receipt, or signature is invalid, or some other such error. </summary>
        Invalid,
        /// <summary> Could not determine purchase validity, for example because a third-party server couldn't be reached. Try again later. </summary>
        TransientError,
    }

    /// <summary>
    /// Response to a <see cref="ValidateInAppPurchaseRequest"/>. Covers both success and failure cases.
    /// </summary>
    [MetaMessage(MessageCodesCore.ValidateInAppPurchaseResponse, MessageDirection.ServerInternal)]
    public class ValidateInAppPurchaseResponse : MetaMessage
    {
        public InAppPurchaseTransactionInfo     TransactionInfo         { get; private set; }
        public InAppPurchaseValidationResult    Result                  { get; private set; }
        public string                           GoogleOrderId           { get; private set; }
        public string                           OriginalTransactionId   { get; private set; }
        /// <summary>
        /// Available if the product is a subscription and the purchase was valid.
        /// </summary>
        public SubscriptionQueryResult          SubscriptionMaybe       { get; private set; }
        public InAppPurchasePaymentType?        PaymentType             { get; private set; }
        public string                           FailReason              { get; private set; }

        ValidateInAppPurchaseResponse() { }

        public static ValidateInAppPurchaseResponse Valid(
            InAppPurchaseTransactionInfo transactionInfo,
            string googleOrderId,
            string originalTransactionId,
            SubscriptionQueryResult subscriptionMaybe,
            InAppPurchasePaymentType? paymentType)
        {
            return new ValidateInAppPurchaseResponse
            {
                TransactionInfo         = transactionInfo,
                Result                  = InAppPurchaseValidationResult.Valid,
                GoogleOrderId           = googleOrderId,
                OriginalTransactionId   = originalTransactionId,
                SubscriptionMaybe       = subscriptionMaybe,
                PaymentType             = paymentType,
            };
        }

        public static ValidateInAppPurchaseResponse Invalid(InAppPurchaseTransactionInfo transactionInfo, string reason)
        {
            return new ValidateInAppPurchaseResponse
            {
                TransactionInfo = transactionInfo,
                Result          = InAppPurchaseValidationResult.Invalid,
                FailReason      = reason,
            };
        }

        public static ValidateInAppPurchaseResponse TransientError(InAppPurchaseTransactionInfo transactionInfo, string reason)
        {
            return new ValidateInAppPurchaseResponse
            {
                TransactionInfo = transactionInfo,
                Result          = InAppPurchaseValidationResult.TransientError,
                FailReason      = reason,
            };
        }
    }

    [EntityConfig]
    internal sealed class InAppPurchaseValidatorConfig : EphemeralEntityConfig
    {
        public override EntityKind          EntityKind              => EntityKindCloudCore.InAppValidator;
        public override Type                EntityActorType         => typeof(InAppPurchaseValidatorActor);
        public override IShardingStrategy   ShardingStrategy        => new ServiceShardingStrategy(EntityKindCloudCore.InAppValidator);
        public override TimeSpan            ShardShutdownTimeout    => TimeSpan.FromSeconds(10);
    }

    /// <summary>
    /// Request to query an IAP subscription's state from the store.
    /// Response is <see cref="InAppPurchaseSubscriptionStateResponse"/>.
    /// </summary>
    /// <remarks>
    /// This should only be used for subscriptions whose purchase has already been
    /// validated in the past, using <see cref="ValidateInAppPurchaseRequest"/>.
    /// This does not validate the purchase.
    ///
    /// This can be either sent as a plain message with <see cref="EntityActor.CastMessage"/>
    /// (in which case the response is also sent as a plain message), or used
    /// with <see cref="EntityActor.EntityAskAsync"/>.
    /// </remarks>
    [MetaMessage(MessageCodesCore.InAppPurchaseSubscriptionStateRequest, MessageDirection.ServerInternal)]
    public class InAppPurchaseSubscriptionStateRequest : MetaMessage
    {
        public InAppPurchaseSubscriptionPersistedInfo SubscriptionInfo { get; private set; }

        InAppPurchaseSubscriptionStateRequest() { }
        public InAppPurchaseSubscriptionStateRequest(InAppPurchaseSubscriptionPersistedInfo subscriptionInfo)
        {
            SubscriptionInfo = subscriptionInfo;
        }
    }

    /// <summary>
    /// Response to <see cref="InAppPurchaseSubscriptionStateRequest"/>.
    /// If the subscription state was successfully queried, <see cref="Result"/>
    /// is set to <see cref="ResultCode.Ok"/> and <see cref="Subscription"/>
    /// is set to the query result.
    /// If an error occurred, <see cref="Result"/> is set to other than <see cref="ResultCode.Ok"/>,
    /// and <see cref="ErrorReason"/> describes the error.
    /// </summary>
    /// <remarks>
    /// If the store's server responds that the subscription's state is no longer
    /// available (for example because it expired a long time ago), that is still
    /// considered a successful query, but <see cref="Subscription"/>'s
    /// <see cref="SubscriptionQueryResult.State"/> will be null.
    /// </remarks>
    [MetaMessage(MessageCodesCore.InAppPurchaseSubscriptionStateResponse, MessageDirection.ServerInternal)]
    public class InAppPurchaseSubscriptionStateResponse : MetaMessage
    {
        [MetaSerializable]
        public enum ResultCode
        {
            Ok,
            Error,
        }

        public string                   OriginalTransactionId   { get; private set; }
        public ResultCode               Result                  { get; private set; }
        public SubscriptionQueryResult  Subscription            { get; private set; }
        public string                   ErrorReason             { get; private set; }

        InAppPurchaseSubscriptionStateResponse() { }

        public static InAppPurchaseSubscriptionStateResponse Ok(string originalTransactionId, SubscriptionQueryResult subscription)
        {
            return new InAppPurchaseSubscriptionStateResponse
            {
                OriginalTransactionId = originalTransactionId,
                Result = ResultCode.Ok,
                Subscription = subscription,
            };
        }

        public static InAppPurchaseSubscriptionStateResponse Error(string originalTransactionId, string reason)
        {
            return new InAppPurchaseSubscriptionStateResponse
            {
                OriginalTransactionId = originalTransactionId,
                Result = ResultCode.Error,
                ErrorReason = reason,
            };
        }
    }

    /// <summary>
    /// In-app purchase validator service. Communicates with the platform-specific servers to resolve
    /// whether a given in-app purchase receipt is valid or forged.
    ///
    /// This actor is stateless and if there is a crash during the validation of a request, this actor
    /// will forget about the request. The retrying logic needs to be where the requests are sent from.
    ///
    /// <remark>
    /// Note that there may be significant latency in some cases with the requests to the external
    /// services at times.
    /// </remark>
    ///
    /// Handles <see cref="ValidateInAppPurchaseRequest"/> messages and responds to those with a
    /// <see cref="ValidateInAppPurchaseResponse"/> message.
    /// </summary>
    public class InAppPurchaseValidatorActor : EphemeralEntityActor
    {
        protected static Prometheus.Counter c_validationsStarted        = Prometheus.Metrics.CreateCounter("game_iapvalidator_validations_started_total", "Number of IAP validations started (by platform)", "platform");
        protected static Prometheus.Counter c_validationTaskResults     = Prometheus.Metrics.CreateCounter("game_iapvalidator_validation_results_total", "Number of IAP validation tasks that returned a result (by platform, result, and payment type (for detecting sandbox purchases))", "platform", "result", "paymentType");
        protected static Prometheus.Counter c_validationTaskExceptions  = Prometheus.Metrics.CreateCounter("game_iapvalidator_validation_exceptions_total", "Number of IAP validation tasks that failed due to an exception (by platform)", "platform");

        protected static Prometheus.Counter c_subscriptionStateQueriesStarted       = Prometheus.Metrics.CreateCounter("game_iapvalidator_subscription_state_queries_started_total", "Number of IAP subscription state queries started (by platform)", "platform");
        protected static Prometheus.Counter c_subscriptionStateQueryTaskResults     = Prometheus.Metrics.CreateCounter("game_iapvalidator_subscription_state_query_results_total", "Number of IAP subscription state queries that returned a result (by platform and result)", "platform", "result");
        protected static Prometheus.Counter c_subscriptionStateQueryTaskExceptions  = Prometheus.Metrics.CreateCounter("game_iapvalidator_subscription_state_query_exceptions_total", "Number of IAP subscription state queries that failed due to an exception (by platform)", "platform");

        protected override AutoShutdownPolicy ShutdownPolicy => AutoShutdownPolicy.ShutdownNever();

        HttpClient _appleHttpClient = HttpUtil.CreateJsonHttpClient();

        /// <summary>
        /// Google JSON receipt provided by the game client.
        /// This info available to the game client is similar to that returned
        /// by Google's receipt validation web API (documented in
        /// https://developers.google.com/android-publisher/api-ref/purchases/products#resource),
        /// but there are some fields that this doesn't contain.
        /// In particular, this does not contain purchaseType,
        /// which is used for detecting test purchases.
        ///
        /// This is used for the actual purchase validation, but then purchaseType is queried
        /// using the web API (see <see cref="TryFetchGooglePaymentTypeAsync"/>.
        /// </summary>
        internal class GoogleReceipt
        {
            [JsonProperty("orderId")]           public string   OrderId             { get; set; }
            [JsonProperty("packageName")]       public string   PackageName         { get; set; }
            [JsonProperty("productId")]         public string   ProductId           { get; set; }
            [JsonProperty("purchaseTime")]      public long     PurchaseTime        { get; set; }
            [JsonProperty("purchaseState")]     public int      PurchaseState       { get; set; }
            [JsonProperty("purchaseToken")]     public string   PurchaseToken       { get; set; }
        }

        /// <summary>
        /// Payload stored in Google IAP credentials JSON.
        /// </summary>
        internal class GoogleCredentials
        {
            [JsonProperty("client_email")]      public string   ClientEmail     { get; set; }
            [JsonProperty("private_key")]       public string   PrivateKey      { get; set; }
        }

        /// <summary>
        /// Individual purchase inside Apple IAP receipt.
        /// </summary>
        internal class AppleInAppItem
        {
            [JsonProperty("quantity")]                  public int      Quantity                { get; set; }
            [JsonProperty("product_id")]                public string   ProductId               { get; set; }
            [JsonProperty("transaction_id")]            public string   TransactionId           { get; set; }
            [JsonProperty("original_transaction_id")]   public string   OriginalTransactionId   { get; set; }
            [JsonProperty("purchase_date_ms")]          public string   PurchaseDateMs          { get; set; }
            [JsonProperty("original_purchase_date_ms")] public string   OriginalPurchaseDateMs  { get; set; }
            [JsonProperty("is_trial_period")]           public string   IsTrialPeriod           { get; set; }
        }

        /// <summary>
        /// Full Apple IAP receipt. Can contain multiple individual purchases.
        /// </summary>
        internal class AppleReceipt
        {
            [JsonProperty("receipt_type")]              public string                   ReceiptType     { get; set; }
            [JsonProperty("bundle_id")]                 public string                   BundleId        { get; set; }
            [JsonProperty("receipt_creation_date_ms")]  public string                   CreationDateMs  { get; set; }
            [JsonProperty("original_purchase_date_ms")] public string                   PurchaseDateMs  { get; set; }
            [JsonProperty("in_app")]                    public List<AppleInAppItem>     Items           { get; set; }
        }

        /// <summary>
        /// An item in the latest_receipt_info list in Apple's validation response.
        /// Used for getting subscription state.
        /// </summary>
        internal class AppleLatestReceiptInfoItem
        {
            [JsonProperty("original_transaction_id")]   public string   OriginalTransactionId   { get; set; }
            [JsonProperty("expires_date_ms")]           public string   ExpiresDateMs           { get; set; }
            [JsonProperty("original_purchase_date_ms")] public string   OriginalPurchaseDateMs  { get; set; }
            [JsonProperty("web_order_line_item_id")]    public string   WebOrderLineItemId      { get; set; }
            [JsonProperty("in_app_ownership_type")]     public string   InAppOwnershipType      { get; set; }

            public MetaTime GetExpiresDate()
            {
                return MetaTime.FromMillisecondsSinceEpoch(long.Parse(ExpiresDateMs, CultureInfo.InvariantCulture));
            }

            public MetaTime GetOriginalPurchaseDate()
            {
                return MetaTime.FromMillisecondsSinceEpoch(long.Parse(OriginalPurchaseDateMs, CultureInfo.InvariantCulture));
            }
        }

        /// <summary>
        /// An item in the pending_renewal_info list in Apple's validation response.
        /// Used for getting subscription state.
        /// </summary>
        internal class ApplePendingRenewalInfoItem
        {
            [JsonProperty("original_transaction_id")]   public string   OriginalTransactionId   { get; set; }
            [JsonProperty("product_id")]                public string   ProductId               { get; set; }
            [JsonProperty("auto_renew_status")]         public string   AutoRenewStatus         { get; set; }
        }

        /// <summary>
        /// JSON response from Apple receipt validation API.
        ///
        /// See: https://developer.apple.com/library/archive/releasenotes/General/ValidateAppStoreReceipt/Chapters/ValidateRemotely.html
        /// </summary>
        internal class AppleValidateResponse
        {
            [JsonProperty("status")]                public int                                  Status              { get; set; }
            [JsonProperty("receipt")]               public AppleReceipt                         Receipt             { get; set; }
            [JsonProperty("latest_receipt")]        public string                               LatestReceipt       { get; set; } // Only present if the receipt contains auto-renewable subscriptions
            [JsonProperty("latest_receipt_info")]   public List<AppleLatestReceiptInfoItem>     LatestReceiptInfo   { get; set; } // Only present if the receipt contains auto-renewable subscriptions
            [JsonProperty("pending_renewal_info")]  public List<ApplePendingRenewalInfoItem>    PendingRenewalInfo  { get; set; } // Only present if the receipt contains auto-renewable subscriptions
            [JsonProperty("is-retryable")]          public bool                                 IsRetryable         { get; set; }
            [JsonProperty("environment")]           public string                               Environment         { get; set; }

            public enum StatusCode
            {
                Success                         = 0,
                InvalidJson                     = 21000,
                InvalidReceipt                  = 21002,
                AuthenticationFailed            = 21003,
                SecretMismatch                  = 21004,
                ServerUnavailable               = 21005,
                TestEnvironmentReceipt          = 21007,
                ProductionEnvironmentReceipt    = 21008,
                NotAuthorized                   = 21010,
            }
        }

        /// <summary>
        /// Dummy JSON structure for development receipts.
        /// </summary>
        internal class InAppPurchaseReceiptDevelopment
        {
            [JsonProperty("productId")]                             public string   ProductId                           { get; set; }
            [JsonProperty("transactionId")]                         public string   TransactionId                       { get; set; }
            [JsonProperty("originalTransactionId")]                 public string   OriginalTransactionId               { get; set; }
            [JsonProperty("validationDelaySeconds")]                public float    ValidationDelaySeconds              { get; set; }
            [JsonProperty("validationTransientErrorProbability")]   public float    ValidationTransientErrorProbability { get; set; }

            // Optional fields for allowing the client to set custom parameters for subscription IAPs.
            // If these are not present, hard-coded defaults are used instead.
            [JsonProperty("subscriptionIsAcquiredViaFamilySharing")]    public bool         SubscriptionIsAcquiredViaFamilySharing  { get; set; } = false;
            [JsonProperty("subscriptionStart")]                         public MetaTime     SubscriptionStart                       { get; set; }
            [JsonProperty("subscriptionDuration")]                      public MetaDuration SubscriptionDuration                    { get; set; }
            [JsonProperty("subscriptionNumPeriodsToActivate")]          public int          SubscriptionNumPeriodsToActivate        { get; set; }
            [JsonProperty("subscriptionNumPeriodsToRetainInfo")]        public int          SubscriptionNumPeriodsToRetainInfo      { get; set; }

            [JsonProperty("paymentType")] public InAppPurchasePaymentType? PaymentType { get; set; } = InAppPurchasePaymentType.Normal;
        }

        // Members
        RSACryptoServiceProvider    _googlePlayPublicKey;   // Decoded application public key for validating in-app purchases

        public InAppPurchaseValidatorActor(EntityId entityId) : base(entityId)
        {
            GooglePlayStoreOptions storeOpts = RuntimeOptionsRegistry.Instance.GetCurrent<GooglePlayStoreOptions>();
            _googlePlayPublicKey = storeOpts.GetGooglePlayPublicKeyRSA();
        }

        [MessageHandler]
        public void HandleValidateInAppPurchaseRequest(EntityId fromEntityId, ValidateInAppPurchaseRequest validate)
        {
            // \todo [petri] add some tracking of in-flight requests? timeouts? retries? max workers/concurrents? don't allow shutdown when requests active?
            ContinueTaskOnActorContext(
                ValidatePurchaseEventAsync(validate.TransactionInfo),
                result =>
                {
                    c_validationTaskResults.WithLabels(validate.TransactionInfo.Platform.ToString(), result.Result.ToString(), result.PaymentType?.ToString() ?? "unknown").Inc();
                    CastMessage(fromEntityId, result);
                },
                error =>
                {
                    c_validationTaskExceptions.WithLabels(validate.TransactionInfo.Platform.ToString()).Inc();
                    _log.Warning("InAppPurchase validation failed for {EntityId}: {Error}", fromEntityId, error);
                    CastMessage(fromEntityId, ValidateInAppPurchaseResponse.TransientError(validate.TransactionInfo, $"Got exception: {error.Message}"));
                }
            );
        }

        void HandleInAppPurchaseSubscriptionStateRequestImpl(EntityId fromEntityId, InAppPurchaseSubscriptionStateRequest request, Action<InAppPurchaseSubscriptionStateResponse> respond)
        {
            // \todo [petri] add some tracking of in-flight requests? timeouts? retries? max workers/concurrents? don't allow shutdown when requests active?
            ContinueTaskOnActorContext(
                QuerySubscriptionStateAsync(request.SubscriptionInfo),
                result =>
                {
                    c_subscriptionStateQueryTaskResults.WithLabels(request.SubscriptionInfo.Platform.ToString(), result.Result.ToString()).Inc();
                    respond(result);
                },
                error =>
                {
                    c_subscriptionStateQueryTaskExceptions.WithLabels(request.SubscriptionInfo.Platform.ToString()).Inc();
                    _log.Warning("InAppPurchase subscription state query failed for {EntityId}: {Error}", fromEntityId, error);
                    respond(InAppPurchaseSubscriptionStateResponse.Error(request.SubscriptionInfo.OriginalTransactionId, $"Got exception: {error.Message}"));
                }
            );
        }

        [MessageHandler]
        public void HandleInAppPurchaseSubscriptionStateRequestMessage(EntityId fromEntityId, InAppPurchaseSubscriptionStateRequest request)
        {
            HandleInAppPurchaseSubscriptionStateRequestImpl(fromEntityId, request, response => CastMessage(fromEntityId, response));
        }

        /// <summary>
        /// EntityAsk variant of <see cref="HandleInAppPurchaseSubscriptionStateRequestMessage"/>.
        /// This is provided because there are scenarios where the requester wants to await on
        /// the request, which cannot be done if the response is a separate message.
        /// </summary>
        [EntityAskHandler]
        public void HandleInAppPurchaseSubscriptionStateRequestAsk(EntityAsk ask, InAppPurchaseSubscriptionStateRequest request)
        {
            HandleInAppPurchaseSubscriptionStateRequestImpl(ask.FromEntityId, request, response => ReplyToAsk(ask, response));
        }

        Task<ValidateInAppPurchaseResponse> ValidatePurchaseEventAsync(InAppPurchaseTransactionInfo txnInfo)
        {
            c_validationsStarted.WithLabels(txnInfo.Platform.ToString()).Inc();

            switch (txnInfo.Platform)
            {
                case InAppPurchasePlatform.Google:
                    return ValidatePurchaseGoogleAsync(txnInfo, txnInfo.PlatformProductId, txnInfo.Receipt, txnInfo.Signature);

                case InAppPurchasePlatform.Apple:
                    return ValidatePurchaseAppleAsync(txnInfo, txnInfo.PlatformProductId, txnInfo.Receipt);

                case InAppPurchasePlatform.Development:
                    return ValidatePurchaseDevelopment(txnInfo, txnInfo.PlatformProductId, txnInfo.Receipt, txnInfo.Signature);

                default:
                    _log.Warning("Unsupported platform {Platform} for InAppPurchaseEvent", txnInfo.Platform);
                    return Task.FromResult(ValidateInAppPurchaseResponse.Invalid(txnInfo, "Unsupported IAP platform"));
            }
        }

        Task<InAppPurchaseSubscriptionStateResponse> QuerySubscriptionStateAsync(InAppPurchaseSubscriptionPersistedInfo subInfo)
        {
            c_subscriptionStateQueriesStarted.WithLabels(subInfo.Platform.ToString()).Inc();

            switch (subInfo.Platform)
            {
                case InAppPurchasePlatform.Google:
                    return QuerySubscriptionStateGoogleAsync(subInfo);

                case InAppPurchasePlatform.Apple:
                    return QuerySubscriptionStateAppleAsync(subInfo);

                case InAppPurchasePlatform.Development:
                    return QuerySubscriptionStateDevelopmentAsync(subInfo);

                default:
                    _log.Warning("Unsupported platform {Platform} for QuerySubscriptionStateAsync", subInfo.Platform);
                    return Task.FromResult(InAppPurchaseSubscriptionStateResponse.Error(subInfo.OriginalTransactionId, "Unsupported IAP platform"));
            }
        }

        #region Google

        async Task<ValidateInAppPurchaseResponse> ValidatePurchaseGoogleAsync(InAppPurchaseTransactionInfo txnInfo, string platformProductId, string receiptJson, string signature64)
        {
            if (_googlePlayPublicKey == null)
            {
                _log.Warning($"{nameof(GooglePlayStoreOptions)}.{nameof(GooglePlayStoreOptions.GooglePlayPublicKey)} is empty");
                return ValidateInAppPurchaseResponse.TransientError(txnInfo, "No Google Play public key");
            }

            // Decode signature
            try
            {
                byte[] signature = Convert.FromBase64String(signature64);
                byte[] receiptBytes = Encoding.UTF8.GetBytes(receiptJson);

                // Verify receipt is legit
                using (SHA1 sha = SHA1.Create())
                {
                    if (!_googlePlayPublicKey.VerifyData(receiptBytes, sha, signature))
                    {
                        _log.Warning("Signature check failed for receipt");
                        return ValidateInAppPurchaseResponse.Invalid(txnInfo, "Signature check failed");
                    }
                }

                // Decode receipt payload
                GoogleReceipt receipt = JsonConvert.DeserializeObject<GoogleReceipt>(receiptJson);
                //_log.Debug("Validate receipt: {0}", PrettyPrint.Verbose(receipt));

                // Validate package name
                GooglePlayStoreOptions storeOpts = RuntimeOptionsRegistry.Instance.GetCurrent<GooglePlayStoreOptions>();
                if (string.IsNullOrEmpty(storeOpts.AndroidPackageName))
                {
                    _log.Warning($"{nameof(GooglePlayStoreOptions)}.{nameof(GooglePlayStoreOptions.AndroidPackageName)} is not specified, allowing receipts with any PackageName");
                }
                else if (receipt.PackageName != storeOpts.AndroidPackageName)
                {
                    _log.Warning("Android PackageName mismatch: got {ReceiptPackageName}, expecting {AndroidPackageName}", receipt.PackageName, storeOpts.AndroidPackageName);
                    return ValidateInAppPurchaseResponse.Invalid(txnInfo, "PackageName mismatch");
                }

                // Validate transactionId
                // \note Accepting both PurchaseToken and OrderId. Some older versions of Unity seem to use OrderId for TransactionId, while newer ones use PurchaseToken.
                //       See InAppPurchaseTransactionIdUtil.ResolveTransactionDeduplicationId.
                if (txnInfo.TransactionId != receipt.PurchaseToken
                 && txnInfo.TransactionId != receipt.OrderId)
                {
                    _log.Warning("TransactionId mismatch: supposed transaction id {TransactionId} matches neither PurchaseToken ({PurchaseToken}) nor OrderId ({OrderId})", txnInfo.TransactionId, receipt.PurchaseToken, receipt.OrderId);
                    return ValidateInAppPurchaseResponse.Invalid(txnInfo, "TransactionId mismatch");
                }

                // Validate productId
                if (receipt.ProductId != platformProductId)
                {
                    _log.Warning("Android ProductId mismatch: got {ReceiptProductId}, expecting {ProductId}", receipt.ProductId, platformProductId);
                    return ValidateInAppPurchaseResponse.Invalid(txnInfo, "ProductId mismatch");
                }

                // Validate purchase state.
                // Must be Purchased, which is value 0.
                if (receipt.PurchaseState != 0)
                {
                    _log.Warning("Google transaction {TransactionId} for {ProductId} has purchaseState {PurchaseState}, expected 0 (= Purchased)", txnInfo.TransactionId, platformProductId, receipt.PurchaseState);
                    return ValidateInAppPurchaseResponse.Invalid(txnInfo, Invariant($"Unexpected purchaseState {receipt.PurchaseState}"));
                }

                // Validation success!

                // Resolve payment type (i.e. check if it's a test purchase) by querying from Google,
                // if Android publisher API is enabled.
                InAppPurchasePaymentType? paymentType = await TryFetchGooglePaymentTypeAsync(receipt);

                // Finally, if it's a subscription product, we still need to get the subscription state.
                // This involves a request to Google's androidpublisher API.
                SubscriptionQueryResult subscription;
                if (txnInfo.ProductType == InAppProductType.Subscription)
                    subscription = await FetchGoogleSubscriptionInfoAsync(receipt);
                else
                    subscription = null;

                _log.Info("Successfully validated Google transaction {TransactionId} for {ProductId} (paymentType: {PaymentType}) (subscription: {Subscription}): {Receipt}",
                    txnInfo.TransactionId,
                    platformProductId,
                    paymentType,
                    PrettyPrint.Compact(subscription),
                    PrettyPrint.Compact(receipt));

                return ValidateInAppPurchaseResponse.Valid(
                    transactionInfo: txnInfo,
                    googleOrderId: receipt.OrderId,
                    originalTransactionId: receipt.PurchaseToken,
                    subscriptionMaybe: subscription,
                    paymentType: paymentType);
            }
            catch (FormatException ex)
            {
                _log.Warning("Unable to parse signature/receipt: {Exception}", ex);
                return ValidateInAppPurchaseResponse.Invalid(txnInfo, "Invalid receipt format");
            }
            catch (Exception ex)
            {
                _log.Warning("Unexpected exception during validation:txnInfo {Exception}", ex);
                return ValidateInAppPurchaseResponse.Invalid(txnInfo, "Unhandled exception");
            }
        }

        async Task<InAppPurchaseSubscriptionStateResponse> QuerySubscriptionStateGoogleAsync(InAppPurchaseSubscriptionPersistedInfo subInfo)
        {
            string receiptJson = subInfo.Receipt;
            GoogleReceipt receipt = JsonConvert.DeserializeObject<GoogleReceipt>(receiptJson);
            SubscriptionQueryResult subscription = await FetchGoogleSubscriptionInfoAsync(receipt);
            _log.Info("Successfully queried Google subscription for product {PlatformProductId}, purchase {PurchaseToken}: {SubscriptionState}", subInfo.PlatformProductId, receipt.PurchaseToken, subscription.State);
            return InAppPurchaseSubscriptionStateResponse.Ok(receipt.PurchaseToken, subscription);
        }

        async Task<InAppPurchasePaymentType?> TryFetchGooglePaymentTypeAsync(GoogleReceipt receipt)
        {
            GooglePlayStoreOptions storeOpts = RuntimeOptionsRegistry.Instance.GetCurrent<GooglePlayStoreOptions>();
            // Don't require all projects to configure the Android publisher API.
            // InAppPurchasePaymentType is non-essential information if the project is not interested in it.
            if (!storeOpts.EnableAndroidPublisherApi)
                return null;

            // \todo For now we tolerate errors, which might be transient or might happen due to some misconfiguration.
            //       Consider always requiring Android publisher API configuration or putting this payment type check
            //       behind an explicit flag, and then make this less tolerant.
            Google.Apis.AndroidPublisher.v3.Data.ProductPurchase googlePurchase;
            try
            {
                googlePurchase = await AndroidPublisherServiceSingleton.Instance.Purchases.Products.Get(receipt.PackageName, receipt.ProductId, receipt.PurchaseToken).ExecuteAsync();
            }
            catch (Exception ex)
            {
                _log.Error("Failed to get Google purchase info (for purchaseType), treating as unknown payment type: {Error}", ex);
                return null;
            }

            if (!googlePurchase.PurchaseType.HasValue)
            {
                // Field not set: normal payment.
                return InAppPurchasePaymentType.Normal;
            }
            else if (googlePurchase.PurchaseType == 0)
            {
                // 0: Test purchase.
                return InAppPurchasePaymentType.Sandbox;
            }
            else
            {
                // Others:
                // 1: Purchased using a promo code
                // 2: Reward from a video ad
                // Add support for these if needed. Check if Apple has anything similar.
                return null;
            }
        }

        /// <summary>
        /// Query info about the subscription described by the given receipt.
        ///
        /// If the query fails, this throws.
        ///
        /// If the query succeeds and the subscription info was available,
        /// this returns a non-null result with non-null
        /// <see cref="SubscriptionQueryResult.State"/>.
        ///
        /// If the subscription info was no longer available (for example
        /// because it expired a long time ago) but the query is otherwise
        /// successful, then this also returns non-null, but with null
        /// <see cref="SubscriptionQueryResult.State"/>.
        /// </summary>
        async Task<SubscriptionQueryResult> FetchGoogleSubscriptionInfoAsync(GoogleReceipt receipt)
        {
            MetaTime queryStartTime = MetaTime.Now;

            // Query the subscription state from Google.
            SubscriptionInstanceState? subscriptionState;
            try
            {
                Google.Apis.AndroidPublisher.v3.Data.SubscriptionPurchase googleSubscription = await AndroidPublisherServiceSingleton.Instance.Purchases.Subscriptions.Get(receipt.PackageName, receipt.ProductId, receipt.PurchaseToken).ExecuteAsync();
                subscriptionState = new SubscriptionInstanceState(
                    isAcquiredViaFamilySharing: false, // No family sharing on Google
                    startTime:                  MetaTime.FromMillisecondsSinceEpoch(googleSubscription.StartTimeMillis.Value),
                    expirationTime:             MetaTime.FromMillisecondsSinceEpoch(googleSubscription.ExpiryTimeMillis.Value),
                    renewalStatus:              googleSubscription.AutoRenewing.Value
                                                    ? SubscriptionRenewalStatus.ExpectedToRenew
                                                    : SubscriptionRenewalStatus.NotExpectedToRenew,
                    numPeriods:                 TryGetNumPeriodsFromGoogleSubscription(googleSubscription) ?? 1);
            }
            catch (Google.GoogleApiException googleException)
            {
                // Certain forms of API exceptions are not necessarily
                // actual errors, but instead indicate that the subscription
                // state is no longer available from Google.

                if (googleException.HttpStatusCode == System.Net.HttpStatusCode.Gone)
                    subscriptionState = null;
                else
                    throw;
            }

            return new SubscriptionQueryResult(
                stateQueriedAt: queryStartTime,
                state: subscriptionState);
        }

        /// <summary>
        /// From a google subscription, resolve the number of periods the subscription has
        /// been active on. At the initial purchase, the number is 1, and then increases
        /// by one at each renewal.
        ///
        /// If given subscription info does not contain data of expected form, null is returned.
        /// </summary>
        int? TryGetNumPeriodsFromGoogleSubscription(Google.Apis.AndroidPublisher.v3.Data.SubscriptionPurchase subscription)
        {
            string orderId = subscription.OrderId;
            if (orderId == null)
                return null;

            // Order id of form GPA.1234-1234-1234-12345 refers to the initial purchase.
            // Order id of form GPA.1234-1234-1234-12345..0
            //              and GPA.1234-1234-1234-12345..1
            // and so on, refer to the renewals of the subscription, with the last number
            // (after the "..") indicating the index of the renewal.
            int twoDotsIndex = orderId.IndexOf("..", StringComparison.Ordinal);
            if (twoDotsIndex < 0)
                return 1;
            else
            {
                string renewalNumberString = orderId.Substring(twoDotsIndex + 2);
                if (!int.TryParse(renewalNumberString, NumberStyles.None, CultureInfo.InvariantCulture, out int renewalNumber))
                    return null;

                // Renewal number 0 means first renewal, which means 2 active periods
                // (initial purchase and one renewal). And so on.
                int periodNumber = renewalNumber + 2;

                // Furthermore, SubscriptionPurchase.OrderId's comment says:
                // "If the subscription was canceled because payment was declined, this will be the order id from the payment declined order."
                // We do not want to count the "payment declined" order.
                // Presumably we can check this from CancelReason: "1. Subscription was canceled by the system, for example because of a billing problem"
                if (subscription.CancelReason == 1)
                    return periodNumber - 1;
                else
                    return periodNumber;
            }
        }

        #endregion

        #region Apple

        async Task<ValidateInAppPurchaseResponse> ValidatePurchaseAppleAsync(InAppPurchaseTransactionInfo txnInfo, string platformProductId, string receipt64)
        {
            AppleStoreOptions storeOpts = RuntimeOptionsRegistry.Instance.GetCurrent<AppleStoreOptions>();
            MetaTime queryStartTime = MetaTime.Now;

            try
            {
                (AppleValidateResponse response, InAppPurchasePaymentType paymentType) = await DoAppleValidationRequestAsync(
                    storeOpts,
                    receipt64: receipt64,
                    transactionId: txnInfo.TransactionId,
                    sandboxIsExplicitlyAllowed: txnInfo.AllowTestPurchases);

                if (response.Status == 0)
                {
                    // Receipt is valid, check that the contents match expected values
                    AppleReceipt receipt = response.Receipt;

                    // \todo [petri] allow ReceiptType == "ProductionSandbox" only when dev features enabled?

                    // Check bundleId match (if not configure in AppleStoreOptions, allow any bundleId to succeed)
                    if (string.IsNullOrEmpty(storeOpts.IosBundleId))
                    {
                        _log.Warning($"{nameof(AppleStoreOptions)}.{nameof(AppleStoreOptions.IosBundleId)} is empty, allowing {{ReceiptBundleId}} in receipt", receipt.BundleId);
                    }
                    else if (receipt.BundleId != storeOpts.IosBundleId)
                    {
                        _log.Warning("Unexpected bundleId {ReceiptBundleId}, expecting {BundleId}", receipt.BundleId, storeOpts.IosBundleId);
                        return ValidateInAppPurchaseResponse.Invalid(txnInfo, "BundleId mismatch");
                    }

                    // Find the matching transaction id
                    AppleInAppItem txn = receipt.Items.Find(item => item.TransactionId == txnInfo.TransactionId);
                    if (txn == null)
                    {
                        _log.Warning("Transaction {TransactionId} not found in the receipt", txnInfo.TransactionId);
                        return ValidateInAppPurchaseResponse.Invalid(txnInfo, "Transaction not found in receipt");
                    }

                    // Expect quantity == 1
                    if (txn.Quantity != 1)
                        _log.Warning("Transaction {TransactionId} has unexpected quantity {Quantity}", txnInfo.TransactionId, txn.Quantity);

                    // Check productId match: either direct match or ends with ".<productId>"
                    // \todo [petri] is it safe to assume "<bundleId>.<productId>", which seems to be the case?
                    bool isValidProductId = (txn.ProductId == platformProductId) || txn.ProductId.EndsWith("." + platformProductId, StringComparison.Ordinal);
                    if (!isValidProductId)
                    {
                        _log.Warning("Transaction {TransactionId} productId mismatch: got {TransactionProductId}, expected {ProductId}", txnInfo.TransactionId, txn.ProductId, platformProductId);
                        return ValidateInAppPurchaseResponse.Invalid(txnInfo, "ProductId mismatch");
                    }

                    // Validation success!

                    // Finally, if it's a subscription product, we still need to get the subscription state from the receipt.
                    SubscriptionQueryResult subscription;
                    if (txnInfo.ProductType == InAppProductType.Subscription)
                    {
                        SubscriptionInstanceState? subscriptionState = TryGetAppleSubscriptionState(platformProductId: platformProductId, originalTransactionId: txn.OriginalTransactionId, response);
                        subscription = new SubscriptionQueryResult(
                            stateQueriedAt:         queryStartTime,
                            state:                  subscriptionState);
                    }
                    else
                        subscription = null;

                    _log.Info("Successfully validated Apple transaction {TransactionId} for {ProductId} (paymentType: {PaymentType}) (subscription: {Subscription}): {Transaction}",
                        txnInfo.TransactionId,
                        platformProductId,
                        paymentType,
                        PrettyPrint.Compact(subscription),
                        PrettyPrint.Compact(txn));

                    return ValidateInAppPurchaseResponse.Valid(
                        transactionInfo: txnInfo,
                        googleOrderId: null,
                        originalTransactionId: txn.OriginalTransactionId,
                        subscriptionMaybe: subscription,
                        paymentType: paymentType);
                }
                else if (response.Status == (int)AppleValidateResponse.StatusCode.InvalidReceipt)
                {
                    _log.Warning("Invalid receipt received for: {Receipt}", receipt64);
                    return ValidateInAppPurchaseResponse.Invalid(txnInfo, Invariant($"StatusCode: {response.Status}"));
                }
                else
                {
                    LogAppleValidationNonSuccessStatusCode(operationDescriptionForLog: "validating purchase", response.Status, receipt64: receipt64);
                    return ValidateInAppPurchaseResponse.TransientError(txnInfo, Invariant($"StatusCode: {response.Status}"));
                }
            }
            catch (Exception ex)
            {
                _log.Warning("Exception happened during validation: {Exception}", ex);
                return ValidateInAppPurchaseResponse.TransientError(txnInfo, $"Validation failed: {ex}");
            }
        }

        async Task<InAppPurchaseSubscriptionStateResponse> QuerySubscriptionStateAppleAsync(InAppPurchaseSubscriptionPersistedInfo subInfo)
        {
            string originalTransactionId = subInfo.OriginalTransactionId;
            string receipt64 = subInfo.Receipt;

            AppleStoreOptions storeOpts = RuntimeOptionsRegistry.Instance.GetCurrent<AppleStoreOptions>();
            MetaTime queryStartTime = MetaTime.Now;

            try
            {

                (AppleValidateResponse response, InAppPurchasePaymentType _) = await DoAppleValidationRequestAsync(
                    storeOpts,
                    receipt64: receipt64,
                    transactionId: originalTransactionId,
                    // \note Here, always allow sandbox.
                    //       Player developer flag was checked at the original purchase
                    //       of the subscription, so there's no need to check it here.
                    sandboxIsExplicitlyAllowed: true);

                if (response.Status == 0)
                {
                    SubscriptionInstanceState? subscriptionState = TryGetAppleSubscriptionState(platformProductId: subInfo.PlatformProductId, originalTransactionId: originalTransactionId, response);
                    _log.Info("Successfully queried subscription state for Apple product {PlatformProductId}, transaction {TransactionId}: {SubscriptionState}", subInfo.PlatformProductId, originalTransactionId, subscriptionState);

                    SubscriptionQueryResult subscription = new SubscriptionQueryResult(
                        stateQueriedAt:         queryStartTime,
                        state:                  subscriptionState);

                    return InAppPurchaseSubscriptionStateResponse.Ok(originalTransactionId, subscription);
                }
                else
                {
                    LogAppleValidationNonSuccessStatusCode(operationDescriptionForLog: "querying subscription state", response.Status, receipt64: receipt64);
                    return InAppPurchaseSubscriptionStateResponse.Error(originalTransactionId, Invariant($"StatusCode: {response.Status}"));
                }
            }
            catch (Exception ex)
            {
                _log.Warning("Exception happened during subscription state query: {Exception}", ex);
                return InAppPurchaseSubscriptionStateResponse.Error(originalTransactionId, $"Query failed: {ex}");
            }
        }

        void LogAppleValidationNonSuccessStatusCode(string operationDescriptionForLog, int statusCode, string receipt64)
        {
            if (statusCode == (int)AppleValidateResponse.StatusCode.AuthenticationFailed
             || statusCode == (int)AppleValidateResponse.StatusCode.SecretMismatch)
            {
                string errorDescription = statusCode == (int)AppleValidateResponse.StatusCode.AuthenticationFailed
                                          ? "authentication failure"
                                          : "secret mismatch";

                _log.Error("Received statusCode={StatusCode} ({ErrorDescription}) when {OperationDescription}. Please make sure the correct App Store shared secret is configured in RuntimeOptions, in AppleStore:" + nameof(AppleStoreOptions.AppleSharedSecret), statusCode, errorDescription, operationDescriptionForLog);
            }
            else
                _log.Warning("Received statusCode={StatusCode} when {OperationDescription} for: {Receipt}", statusCode, operationDescriptionForLog, receipt64);
        }

        async Task<(AppleValidateResponse, InAppPurchasePaymentType)> DoAppleValidationRequestAsync(AppleStoreOptions storeOpts, string receipt64, string transactionId, bool sandboxIsExplicitlyAllowed)
        {
            // \todo [nuutti] Figure out whether `exclude-old-transactions` is beneficial.
            string reqJson;
            if (storeOpts.AppleSharedSecret == null)
                reqJson = $"{{\"receipt-data\":\"{receipt64}\"}}";
            else
                reqJson = $"{{\"receipt-data\":\"{receipt64}\",\"password\":\"{storeOpts.AppleSharedSecret}\"}}";

            const string productionUri  = "https://buy.itunes.apple.com/verifyReceipt";
            const string sandboxUri     = "https://sandbox.itunes.apple.com/verifyReceipt";

            bool acceptSandbox = sandboxIsExplicitlyAllowed || storeOpts.AcceptSandboxPurchases;

            if (storeOpts.AcceptProductionPurchases)
            {
                // Production purchases are accepted.
                // - First, we try the production URI.
                // - If we get status code 21007 (TestEnvironmentReceipt), then if sandbox purchases are also accepted, we try the sandbox URI.

                AppleValidateResponse productionResponse = await HttpUtil.RequestJsonPostAsync<AppleValidateResponse>(_appleHttpClient, productionUri, reqJson);

                if (productionResponse.Status == (int)AppleValidateResponse.StatusCode.TestEnvironmentReceipt &&
                    acceptSandbox)
                {
                    _log.Info("Forwarding Apple IAP to sandbox: {0}", transactionId);

                    AppleValidateResponse sandboxResponse = await HttpUtil.RequestJsonPostAsync<AppleValidateResponse>(_appleHttpClient, sandboxUri, reqJson);
                    return (sandboxResponse, InAppPurchasePaymentType.Sandbox);
                }
                else
                    return (productionResponse, InAppPurchasePaymentType.Normal);
            }
            else if (acceptSandbox)
            {
                // Only sandbox purchases are accepted. Only use the sandbox verification URI.

                AppleValidateResponse sandboxResponse = await HttpUtil.RequestJsonPostAsync<AppleValidateResponse>(_appleHttpClient, sandboxUri, reqJson);
                return (sandboxResponse, InAppPurchasePaymentType.Sandbox);
            }
            else
                throw new InvalidOperationException($"Tried to validate a purchase, but {nameof(AppleStoreOptions.AcceptProductionPurchases)} and {nameof(AppleStoreOptions.AcceptSandboxPurchases)} (in {nameof(AppleStoreOptions)}) are both false");
        }

        /// <summary>
        /// Try to get the subscription state of the given purchase
        /// from the Apple validation response. Returns null if the
        /// information is not found from the response.
        /// </summary>
        SubscriptionInstanceState? TryGetAppleSubscriptionState(string platformProductId, string originalTransactionId, AppleValidateResponse validateResponse)
        {
            if (platformProductId == null)
                throw new ArgumentNullException(nameof(platformProductId));
            if (originalTransactionId == null)
                throw new ArgumentNullException(nameof(originalTransactionId));

            if (validateResponse.LatestReceiptInfo == null)
            {
                _log.Info("latest_receipt_info is null when trying to get state of auto-renewing subscription. Assuming it is expired and has been pruned.");
                return null;
            }

            IEnumerable<AppleLatestReceiptInfoItem> subscriptionInfos = validateResponse.LatestReceiptInfo.Where(it => it.OriginalTransactionId == originalTransactionId);
            if (!subscriptionInfos.Any())
            {
                _log.Info("original_transaction_id {OriginalTransactionId} not found in latest_receipt_info when trying to get state of auto-renewing subscription. Assuming it is expired and has been pruned.", originalTransactionId);
                return null;
            }

            AppleLatestReceiptInfoItem latestSubscriptionInfo = subscriptionInfos.MaxBy(it => it.GetExpiresDate());

            ApplePendingRenewalInfoItem pendingRenewalInfoMaybe;
            if (validateResponse.PendingRenewalInfo != null)
            {
                pendingRenewalInfoMaybe = validateResponse.PendingRenewalInfo
                                          .FirstOrDefault(it => it.ProductId == platformProductId
                                                             && it.OriginalTransactionId == originalTransactionId);
            }
            else
                pendingRenewalInfoMaybe = null;

            string appleAutoRenewStatusMaybe = pendingRenewalInfoMaybe?.AutoRenewStatus;

            SubscriptionRenewalStatus renewalStatus = appleAutoRenewStatusMaybe == "1" ? SubscriptionRenewalStatus.ExpectedToRenew
                                                    : appleAutoRenewStatusMaybe == "0" ? SubscriptionRenewalStatus.NotExpectedToRenew
                                                    :                                    SubscriptionRenewalStatus.Unknown;

            // Count number of periods as number of distinct web_order_line_item_id among items with this original_transaction_id.
            // Apple's documentation on web_order_line_item_id says:
            // "A unique identifier for purchase events across devices, including subscription-renewal events. This value is the primary key for identifying subscription purchases."
            int numPeriods = subscriptionInfos.DistinctBy(it => it.WebOrderLineItemId).Count();

            return new SubscriptionInstanceState(
                isAcquiredViaFamilySharing: latestSubscriptionInfo.InAppOwnershipType == "FAMILY_SHARED",
                startTime:                  latestSubscriptionInfo.GetOriginalPurchaseDate(),
                expirationTime:             latestSubscriptionInfo.GetExpiresDate(),
                renewalStatus,
                numPeriods:                 numPeriods);
        }

        #endregion

        #region Development/debugging pseudo-platform

        async Task<ValidateInAppPurchaseResponse> ValidatePurchaseDevelopment(InAppPurchaseTransactionInfo txnInfo, string platformProductId, string receipt64, string signature)
        {
            // Only allow when development features enabled and not in production mode
            EnvironmentOptions envOpts = RuntimeOptionsRegistry.Instance.GetCurrent<EnvironmentOptions>();
            if (!envOpts.EnableDevelopmentFeatures)
                return ValidateInAppPurchaseResponse.Invalid(txnInfo, $"Development features must be enabled for dev purchases to succeed!");

            MetaTime queryStartTime = MetaTime.Now;

            try
            {
                // Parse receipt
                string receiptJson = Encoding.UTF8.GetString(Convert.FromBase64String(receipt64));
                InAppPurchaseReceiptDevelopment devReceipt = JsonConvert.DeserializeObject<InAppPurchaseReceiptDevelopment>(receiptJson);

                // Debug delay
                if (devReceipt.ValidationDelaySeconds > 0f)
                    await Task.Delay(TimeSpan.FromSeconds(devReceipt.ValidationDelaySeconds));

                MetaTime queryEndTime = MetaTime.Now;
                MetaTime nominalQueryTime = MetaTime.FromMillisecondsSinceEpoch((queryStartTime.MillisecondsSinceEpoch + queryEndTime.MillisecondsSinceEpoch) / 2);

                // Debug transient error
                if (new Random().NextDouble() < devReceipt.ValidationTransientErrorProbability)
                    return ValidateInAppPurchaseResponse.TransientError(txnInfo, "Debug transient error");

                // Claimed TransactionId must match that in receipt
                if (txnInfo.TransactionId != devReceipt.TransactionId)
                    return ValidateInAppPurchaseResponse.Invalid(txnInfo, "TransactionId mismatch");

                // ProductIds must match
                if (devReceipt.ProductId != platformProductId)
                    return ValidateInAppPurchaseResponse.Invalid(txnInfo, $"ProductId mismatch: got {devReceipt.ProductId}, expecting {platformProductId}");

                // Signature must match json receipt (not base64 encoded)
                if (Util.ComputeSHA1(receiptJson) != signature)
                    return ValidateInAppPurchaseResponse.Invalid(txnInfo, "Invalid signature");

                // Validation success.

                // Finally, if it's a subscription, get the subscription state from the receipt.
                SubscriptionQueryResult subscription;
                if (txnInfo.ProductType == InAppProductType.Subscription)
                {
                    // It's a subscription product, so we still need to get the subscription state from the receipt.
                    SubscriptionInstanceState? subscriptionState = TryGetDevelopmentSubscriptionState(devReceipt, nominalQueryTime: nominalQueryTime, validatedPurchaseTime: txnInfo.PurchaseTime);
                    subscription = new SubscriptionQueryResult(
                        stateQueriedAt:         queryStartTime,
                        state:                  subscriptionState);
                }
                else
                    subscription = null;

                return ValidateInAppPurchaseResponse.Valid(
                    transactionInfo: txnInfo,
                    googleOrderId: null,
                    originalTransactionId: devReceipt.OriginalTransactionId,
                    subscriptionMaybe: subscription,
                    paymentType: devReceipt.PaymentType);
            }
            catch (Exception ex)
            {
                _log.Warning("Exception during validation of development receipt: {Exception}", ex);
                return ValidateInAppPurchaseResponse.TransientError(txnInfo, "Exception happened");
            }
        }

        async Task<InAppPurchaseSubscriptionStateResponse> QuerySubscriptionStateDevelopmentAsync(InAppPurchaseSubscriptionPersistedInfo subInfo)
        {
            string originalTransactionId = subInfo.OriginalTransactionId;
            string receipt64 = subInfo.Receipt;

            MetaTime queryStartTime = MetaTime.Now;

            // Parse receipt
            string receiptJson = Encoding.UTF8.GetString(Convert.FromBase64String(receipt64));
            InAppPurchaseReceiptDevelopment devReceipt = JsonConvert.DeserializeObject<InAppPurchaseReceiptDevelopment>(receiptJson);

            // Debug delay
            if (devReceipt.ValidationDelaySeconds > 0f)
                await Task.Delay(TimeSpan.FromSeconds(devReceipt.ValidationDelaySeconds));

            MetaTime queryEndTime = MetaTime.Now;
            MetaTime nominalQueryTime = MetaTime.FromMillisecondsSinceEpoch((queryStartTime.MillisecondsSinceEpoch + queryEndTime.MillisecondsSinceEpoch) / 2);

            // Debug transient error
            if (new Random().NextDouble() < devReceipt.ValidationTransientErrorProbability)
                return InAppPurchaseSubscriptionStateResponse.Error(originalTransactionId, "Debug transient error");

            SubscriptionInstanceState? subscriptionState = TryGetDevelopmentSubscriptionState(devReceipt, nominalQueryTime: nominalQueryTime, validatedPurchaseTime: subInfo.ValidatedTransactionPurchaseTime);

            SubscriptionQueryResult subscription = new SubscriptionQueryResult(
                stateQueriedAt:         queryStartTime,
                state:                  subscriptionState);

            return InAppPurchaseSubscriptionStateResponse.Ok(originalTransactionId, subscription);
        }

        SubscriptionInstanceState? TryGetDevelopmentSubscriptionState(InAppPurchaseReceiptDevelopment receipt, MetaTime nominalQueryTime, MetaTime validatedPurchaseTime)
        {
            MetaTime     start                      = receipt.SubscriptionStart;
            MetaDuration duration                   = receipt.SubscriptionDuration;
            int          numPeriodsToActivate       = receipt.SubscriptionNumPeriodsToActivate;
            int          numPeriodsToRetainInfo     = receipt.SubscriptionNumPeriodsToRetainInfo;

            // If no values are set, default to a short subscription that is active
            // starting from now and renews a few times.
            if (start == default(MetaTime))
            {
                start                   = validatedPurchaseTime;
                duration                = MetaDuration.FromMinutes(1);
                numPeriodsToActivate    = 3;
                numPeriodsToRetainInfo  = 5;
            }

            int unclampedPeriodIndex = (int)((nominalQueryTime - start) / duration);

            if (unclampedPeriodIndex >= numPeriodsToRetainInfo)
                return null;
            else
            {
                int clampedPeriodIndex = Math.Min(unclampedPeriodIndex, numPeriodsToActivate - 1);

                return new SubscriptionInstanceState(
                    isAcquiredViaFamilySharing: receipt.SubscriptionIsAcquiredViaFamilySharing,
                    startTime:                  start,
                    expirationTime:             start + (clampedPeriodIndex+1)*duration,
                    renewalStatus:              unclampedPeriodIndex + 1 < numPeriodsToActivate
                                                    ? SubscriptionRenewalStatus.ExpectedToRenew
                                                    : SubscriptionRenewalStatus.NotExpectedToRenew,
                    numPeriods:                 clampedPeriodIndex + 1);
            }
        }

        #endregion
    }

    public static class InAppPurchaseTransactionIdUtil
    {
        /// <summary>
        /// Resolve the id that should be used for deduplicating the given transaction;
        /// that is, the id that should be used as <see cref="PersistedInAppPurchase.TransactionId"/>.
        ///
        /// For Google Play purchases, this is not as simple as always just using
        /// <see cref="InAppPurchaseTransactionInfo.TransactionId"/>.
        /// See comments in method for details.
        /// </summary>
        public static string ResolveTransactionDeduplicationId(InAppPurchaseTransactionInfo txnInfo)
        {
            if (txnInfo.Platform == InAppPurchasePlatform.Google)
            {
                // Google Play receipt:
                // We use the PurchaseToken from the receipt.
                // We don't want to use txnInfo.TransactionId for deduplicating the purchase,
                // because old and new versions of Unity differ in what value they report
                // as the TransactionId: old Unity versions report OrderId, and new versions
                // report PurchaseToken. Because we don't want to allow duplicate purchases
                // (one using PurchaseToken and the other using OrderId), we consistently
                // use PurchaseToken.
                // See also validation code in InAppPurchaseValidatorActor.ValidatePurchaseGoogleAsync.

                InAppPurchaseValidatorActor.GoogleReceipt receipt;
                try
                {
                    receipt = JsonConvert.DeserializeObject<InAppPurchaseValidatorActor.GoogleReceipt>(txnInfo.Receipt);
                }
                catch
                {
                    // Shouldn't happen. Tolerate here, but allow validation flow to proceed,
                    // letting it fail at a more natural/debuggable point.
                    return txnInfo.TransactionId;
                }

                if (receipt.PurchaseToken == null)
                {
                    // Shouldn't happen. Tolerate here, but allow validation flow to proceed,
                    // letting it fail at a more natural/debuggable point.
                    return txnInfo.TransactionId;
                }

                return receipt.PurchaseToken;
            }
            else if (txnInfo.Platform == InAppPurchasePlatform.Apple)
            {
                // Apple App Store receipt:
                // txnInfo.TransactionId as-is is appropriate for deduplication.
                // It is the transaction_id from a purchase info in the receipt.
                return txnInfo.TransactionId;
            }
            else if (txnInfo.Platform == InAppPurchasePlatform.Development)
            {
                return txnInfo.TransactionId;
            }
            else
                throw new InvalidOperationException($"Unhandled {nameof(InAppPurchasePlatform)}: {txnInfo.Platform}");
        }
    }
}
