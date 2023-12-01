// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

// Set `Purchasing` to alias either the real UnityEngine.Purchasing, or Metaplay.Unity.IAP.FakePurchasing,
// depending on project configuration. Also, certain Unity Purchasing versions added new interfaces and
// obsoleted old ones, so use the new ones according to the version. Also FakePurchasing defines the new interfaces.
#if METAPLAY_USE_FAKE_UNITY_PURCHASING
    #define USE_UNITY_PURCHASING_4_6_0_INTERFACES
    #define USE_UNITY_PURCHASING_4_8_0_INTERFACES
    using Purchasing = Metaplay.Unity.IAP.FakePurchasing;
#elif METAPLAY_HAS_UNITY_PURCHASING
    #if METAPLAY_HAS_UNITY_PURCHASING_4_6_0_OR_NEWER
        #define USE_UNITY_PURCHASING_4_6_0_INTERFACES
    #endif
    #if METAPLAY_HAS_UNITY_PURCHASING_4_8_0_OR_NEWER
        #define USE_UNITY_PURCHASING_4_8_0_INTERFACES
    #endif
    using Purchasing = UnityEngine.Purchasing;
#else
    // Unity IAP not available, and also fake store is not opted-into; IAPManager will be effectively disabled.
    // Using the fake namespace just so the code will compile without having to #if it all out.
    #define USE_UNITY_PURCHASING_4_6_0_INTERFACES
    #define USE_UNITY_PURCHASING_4_8_0_INTERFACES
    using Purchasing = Metaplay.Unity.IAP.FakePurchasing;
#endif

using Metaplay.Core;
using Metaplay.Core.Client;
using Metaplay.Core.InAppPurchase;
using Metaplay.Core.Player;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace Metaplay.Unity.IAP
{
    /// <summary>
    /// Manages the client-side flow of in-app purchases between the game, the IAP store,
    /// and Metaplay.
    /// This is an optional component. The game can choose to either use <c>IAPManager</c>
    /// or implement the corresponding functionality on its own.
    /// When using <see cref="DefaultIntegration.MetaplayClient"/>, <c>IAPManager</c> can be
    /// enabled with <see cref="MetaplayIAPOptions.EnableIAPManager"/> in
    /// <see cref="MetaplayClientOptions.IAPOptions"/>.
    ///
    /// <para>
    /// IAPManager will interface with the actual Unity Purchasing API only if
    /// METAPLAY_USE_FAKE_UNITY_PURCHASING is not defined, and Unity IAP is available
    /// in the project (and thus METAPLAY_HAS_UNITY_PURCHASING is defined based on
    /// the versionDefines in the Metaplay.Unity.asmdef).
    ///
    /// If METAPLAY_USE_FAKE_UNITY_PURCHASING is defined, IAPManager will interface
    /// with a fake IAP interface that mimics (a subset of) the Unity Purchasing API,
    /// meant as a development convenience for when the real Unity IAP hasn't yet been
    /// installed in the project. If you use METAPLAY_USE_FAKE_UNITY_PURCHASING early
    /// in your development of IAPs, you should remove it once you've installed the
    /// real Unity IAP. The fake IAP interface should not be used in production, since
    /// it doesn't use the actual IAP store.
    ///
    /// If METAPLAY_USE_FAKE_UNITY_PURCHASING is not defined and Unity IAP is also
    /// not available, then the store will not be initialized.
    /// </para>
    ///
    /// <para>
    /// A purchase is initiated by calling <see cref="TryBeginPurchaseProduct(InAppProductId)"/>.
    /// <c>IAPManager</c> will initiate a purchase in the IAP store, and will handle the store's
    /// requests for processing the purchase. Processing the purchase in our case means
    /// sending the purchase to the server (using PlayerActions) for validation.
    /// When a purchase has been validated, <c>IAPManager</c> invokes the appropriate
    /// PlayerActions to claim or clear the purchase, and confirms it to the store.
    /// </para>
    ///
    /// <para>
    /// To help track the purchase flow status (for example to update the game shop UI)
    /// <c>IAPManager</c> provides various events such as <see cref="OnPurchaseFinishedInMetaplay"/>.
    /// Alternatively, see <see cref="IAPFlowTracker" /> for a simpler interface for simple UI uses.
    /// </para>
    ///
    /// <para>
    /// Dynamic-content purchases have the extra "content confirmation" round-trip with the server,
    /// which happens before the normal IAP flow. <see cref="RegisterPendingDynamicPurchase(InAppProductId)"/>
    /// can be called to tell <c>IAPManager</c> to track the server's confirmation of the dynamic
    /// content. After the server has confirmed that dynamic content has been assigned for the
    /// product, <c>IAPManager</c> will automatically initiate the actual IAP flow with the IAP store.
    ///
    /// Note that the game code will still need to invoke the action that assigns the dynamic
    /// content for the product (e.g. <see cref="Core.Offers.PlayerPreparePurchaseMetaOffer"/>)
    /// before calling <see cref="RegisterPendingDynamicPurchase"/>.
    /// <see cref="RegisterPendingDynamicPurchase"/> can be called either explicitly after
    /// invoking the action, or from the handler of
    /// <see cref="IPlayerModelClientListenerCore.PendingDynamicPurchaseContentAssigned"/>.
    /// </para>
    /// </summary>
    public class IAPManager
#if USE_UNITY_PURCHASING_4_8_0_INTERFACES
        : Purchasing.IDetailedStoreListener
#else
        : Purchasing.IStoreListener
#endif
    {
        #region Debug

        /// <summary>
        /// Set to true to stop <c>IAPManager</c> from claiming validated purchases.
        /// </summary>
        public bool DebugOmitIAPClaim { get; set; } = false;

        #endregion

        #region Private fields

        LogChannel _log = MetaplaySDK.Logs.CreateChannel("iapManager");

        ISessionContextProvider _sessionContextProvider;

        class Store
        {
            public Purchasing.IStoreController Controller { get; }
            public Purchasing.IExtensionProvider Extensions { get; }

            public Store(Purchasing.IStoreController controller, Purchasing.IExtensionProvider extensions)
            {
                Controller = controller;
                Extensions = extensions;
            }
        }

        StoreInitializationFailure? _storeInitFailure   = null;
        Store                       _store              = null;

        /// <summary> Actions to be executed once we have a Metaplay session </summary>
        Queue<Action>   _pendingMetaplaySessionFuncs    = new Queue<Action>();
        /// <summary> Actions to be executed once the store has been initialized </summary>
        Queue<Action>   _pendingStoreFuncs              = new Queue<Action>();

        /// <summary>
        /// Purchases that were received from <see cref="IPlayerModelClientListenerCore"/> calls.
        /// Their handling needs to be delayed for safety, as the listeners are executed during PlayerAction execution.
        /// </summary>
        Queue<InAppPurchaseEvent> _delayedPlayerModelListenerFinishedPurchases = new Queue<InAppPurchaseEvent>();

        /// <summary>
        /// Products that have had their pending dynamic content assigned recently, and
        /// <c>IAPManager</c> is now polling for them to become confirmed by server.
        /// See <see cref="RegisterPendingDynamicPurchase(InAppProductId)"/>
        /// </summary>
        HashSet<InAppProductId> _pendingDynamicPurchases = new HashSet<InAppProductId>();

        /// <summary>
        /// As <see cref="_pendingDynamicPurchases"/>, but for static purchase context
        /// </summary>
        HashSet<InAppProductId> _pendingStaticPurchaseContexts = new HashSet<InAppProductId>();

        #endregion

        #region Game-facing interface

        // \note Some wrapping of the Purchasing types is done here for convenience,
        //       so that users might not need to have '#if's for METAPLAY_USE_FAKE_UNITY_PURCHASING
        //       and METAPLAY_HAS_UNITY_PURCHASING so much.

        public struct StoreInitializationFailure
        {
            public Purchasing.InitializationFailureReason Reason { get; }
            public string Message { get; }
            public StoreInitializationFailure(Purchasing.InitializationFailureReason reason, string message) { Reason = reason; Message = message; }
        }

        public struct StorePurchaseFailure
        {
            public Purchasing.PurchaseFailureReason Reason { get; }
#if USE_UNITY_PURCHASING_4_8_0_INTERFACES
            public Purchasing.Extension.PurchaseFailureDescription Description { get; }
#endif

            public StorePurchaseFailure(Purchasing.PurchaseFailureReason reason)
            {
                Reason = reason;
#if USE_UNITY_PURCHASING_4_8_0_INTERFACES
                Description = null;
#endif
            }

#if USE_UNITY_PURCHASING_4_8_0_INTERFACES
            public StorePurchaseFailure(Purchasing.Extension.PurchaseFailureDescription description)
            {
                Reason = description?.reason ?? Purchasing.PurchaseFailureReason.Unknown;
                Description = description;
            }
#endif

            public override string ToString()
            {
#if USE_UNITY_PURCHASING_4_8_0_INTERFACES
                return $"{{ Reason: {Reason}, ProductId: {Description?.productId ?? "<null>"}, Message: {Description?.message ?? "<null>"} }}";
#else
                return $"{{ Reason: {Reason} }}";
#endif
            }
        }

        public struct StoreProductInfo
        {
            public Purchasing.Product Product { get; }
            public StoreProductInfo(Purchasing.Product product) { Product = product; }
        }

        public delegate void OnStoreInitializationSucceededHandler      ();
        public delegate void OnStoreInitializationFailedHandler         (StoreInitializationFailure failure);
        public delegate void OnPendingDynamicPurchaseRegisteredHandler  (InAppProductId productId);
        public delegate void OnPendingDynamicPurchaseUnRegisteredHandler(InAppProductId productId);
        public delegate void OnPendingStaticPurchaseContextRegisteredHandler  (InAppProductId productId);
        public delegate void OnPendingStaticPurchaseContextUnRegisteredHandler(InAppProductId productId);
        public delegate void OnInitiatingPurchaseHandler                (InAppProductId productId);
        public delegate void OnStorePurchaseFailedHandler               (InAppProductId productId, StorePurchaseFailure failure);
        public delegate void PurchaseEventHandler                       (InAppPurchaseEvent purchaseEvent);

        // Store initialization success and failure events
        public event OnStoreInitializationSucceededHandler  OnStoreInitializationSucceeded;
        public event OnStoreInitializationFailedHandler     OnStoreInitializationFailed;

        // Relevant events during a purchase flow.
        public event OnPendingDynamicPurchaseRegisteredHandler      OnPendingDynamicPurchaseRegistered;     // When RegisterPendingDynamicPurchase was called.
        public event OnPendingDynamicPurchaseUnRegisteredHandler    OnPendingDynamicPurchaseUnregistered;   // When session ended and then-pending dynamic purchases were unregistered by IAPManager.
        public event OnPendingStaticPurchaseContextRegisteredHandler      OnPendingStaticPurchaseRegistered;     // When RegisterPendingStaticPurchase was called.
        public event OnPendingStaticPurchaseContextUnRegisteredHandler    OnPendingStaticPurchaseUnregistered;   // When session ended and then-pending static purchases were unregistered by IAPManager.
        public event OnInitiatingPurchaseHandler                    OnInitiatingPurchase;                   // When we initiate a purchase with the store.
        public event OnStorePurchaseFailedHandler                   OnStorePurchaseFailed;                  // Purchase failed already in store (e.g. due to cancellation by user), before it was ever handed to Metaplay.
        public event PurchaseEventHandler                           OnFailedToStartPurchaseValidation;      // PlayerInAppPurchased action failed unexpectedly. Processing of this purchase will not continue.
        public event PurchaseEventHandler                           OnStartedPurchaseValidation;            // When we actually started validation of a purchase. The purchase event will be sent to server.
        public event PurchaseEventHandler                           OnPurchaseFinishedInMetaplay;           // Purchase processing was finished by Metaplay (it was either a success or failure), and PlayerModel updated accordingly. See the parameter purchase event's Status for the result.

        public bool                         StoreIsAvailable    => _store != null;
        public StoreInitializationFailure?  StoreInitFailure    => _storeInitFailure;

        /// <summary>
        /// Try to get the store product info (including <see cref="Purchasing.Product"/>) for the given Metaplay product id.
        /// </summary>
        /// <returns>Store product for the id, or null if no matching store product found.</returns>
        public StoreProductInfo? TryGetStoreProductInfo(InAppProductId productId)
        {
            Purchasing.Product product = _store?.Controller.products.WithID(productId.ToString());
            if (product == null)
                return null;

            return new StoreProductInfo(product);
        }

        /// <summary>
        /// Whether the product is available to purchase. A product can be unavailable
        /// for several reasons: the store might not be initialized; the product might not
        /// be found in the store; or the product might be disabled in the store.
        /// </summary>
        public bool StoreProductIsAvailable(InAppProductId productId)
        {
            return TryGetStoreProductInfo(productId)?.Product.availableToPurchase ?? false;
        }

        /// <summary>
        /// Try to begin purchasing the given product. This can fail if the store
        /// has not been initialized, or if no matching store product is found.
        /// </summary>
        /// <returns>Whether the purchase was initiated with the store (i.e. <see cref="Purchasing.IStoreController.InitiatePurchase(Purchasing.Product)"/> was called).</returns>
        /// <remarks>Even if this succeeds, the purchase can still fail.</remarks>
        public bool TryBeginPurchaseProduct(InAppProductId productId)
        {
            StoreProductInfo? product = TryGetStoreProductInfo(productId);
            if (!product.HasValue)
                return false;

            _log.Info("Initiating purchase for {ProductId}", productId);
            OnInitiatingPurchase?.Invoke(productId);
            _store.Controller.InitiatePurchase(product.Value.Product);
            return true;
        }

        /// <summary>
        /// Notify <c>IAPManager</c> that <paramref name="productId"/> has had dynamic content assigned
        /// (into <see cref="IPlayerModelBase.PendingDynamicPurchaseContents"/>), and should be purchased.
        /// <c>IAPManager</c> will begin polling its <see cref="PendingDynamicPurchaseContent.Status"/>,
        /// and will initiate a purchase when it becomes <see cref="PendingDynamicPurchaseContentStatus.ConfirmedByServer"/>.
        /// </summary>
        /// <remarks>
        /// By design, this is only remembered during the current session. If the session ends before
        /// the purchase has been confirmed by the server, <c>IAPManager</c> won't automatically initiate the purchase.
        /// This is for UX reasons.
        /// </remarks>
        public void RegisterPendingDynamicPurchase(InAppProductId productId)
        {
            OnPendingDynamicPurchaseRegistered?.Invoke(productId);
            _pendingDynamicPurchases.Add(productId);
        }

        /// <summary>
        /// As <see cref="RegisterPendingDynamicPurchase"/> but only for non-dynamic purchase contexts.
        /// </summary>
        public void RegisterPendingStaticPurchase(InAppProductId productId)
        {
            OnPendingStaticPurchaseRegistered?.Invoke(productId);
            _pendingStaticPurchaseContexts.Add(productId);
        }

        /// <summary>
        /// Call IAppleExtensions.RestoreTransactions.
        /// On Apple platform, this initiates purchase restoration. If your game
        /// has products that can be restored (e.g. subscriptions), then on the
        /// Apple platform, your game UI should provide a purchase restoration
        /// button which calls this method.
        /// </summary>
        public void RestoreTransactions()
        {
            _store?.Extensions?.GetExtension<Purchasing.IAppleExtensions>()?.RestoreTransactions(
#if USE_UNITY_PURCHASING_4_6_0_INTERFACES
                (bool success, string error) =>
#else
                (bool success) =>
#endif
                {
                    if (success)
                        _log.Info("IAppleExtensions.RestoreTransactions succeeded");
                    else
                    {
#if USE_UNITY_PURCHASING_4_6_0_INTERFACES
                        _log.Warning("IAppleExtensions.RestoreTransactions failed: {Error}", error);
#else
                        _log.Warning("IAppleExtensions.RestoreTransactions failed");
#endif
                    }
                });
        }

        #endregion

        #region Store initialization and IStoreListener/IDetailedStoreListener

        void BeginInitializeStore()
        {
            _log.Debug("Initializing store with {NumProducts} products", PlayerModel.GameConfig.InAppProducts.Count);

            Purchasing.ConfigurationBuilder builder = Purchasing.ConfigurationBuilder.Instance(Purchasing.StandardPurchasingModule.Instance());

            foreach (InAppProductInfoBase iapInfo in PlayerModel.GameConfig.InAppProducts.Values)
            {
                Purchasing.IDs ids = new Purchasing.IDs();
                ids.Add(iapInfo.GoogleId,         Purchasing.GooglePlay.Name);
                ids.Add(iapInfo.AppleId,          Purchasing.AppleAppStore.Name);
                ids.Add(iapInfo.DevelopmentId,    PurchasingMisc.UnityFakeStoreName);

                builder.AddProduct(iapInfo.ProductId.ToString(), ConvertToStoreProductType(iapInfo.Type), ids);
            }

            Purchasing.UnityPurchasing.Initialize(this, builder);
        }

        void Purchasing.IStoreListener.OnInitialized(Purchasing.IStoreController controller, Purchasing.IExtensionProvider extensions)
        {
            _log.Info("Store initialized with {NumProducts} products, {NumProductsAvailable} of them available", controller.products.all.Length, controller.products.all.Count(p => p.availableToPurchase));

            _store = new Store(controller, extensions);

            OnStoreInitializationSucceeded?.Invoke();

            if (_pendingStoreFuncs.Count > 0)
                _log.Debug("Flushing {NumPendingFuncs} pending store funcs", _pendingStoreFuncs.Count);
            FlushPendingFuncs(_pendingStoreFuncs);
        }

        void Purchasing.IStoreListener.OnInitializeFailed(Purchasing.InitializationFailureReason reason)
        {
            _log.Warning("Store initialization failed, reason: {Reason}", reason);
            OnInitializeFailedImpl(reason, message: null);
        }

#if USE_UNITY_PURCHASING_4_6_0_INTERFACES
        void Purchasing.IStoreListener.OnInitializeFailed(Purchasing.InitializationFailureReason reason, string message)
        {
            _log.Warning("Store initialization failed, reason: {Reason}, message: {Message}", reason, message ?? "<null>");
            OnInitializeFailedImpl(reason, message);
        }
#endif

        void OnInitializeFailedImpl(Purchasing.InitializationFailureReason reason, string message)
        {
            StoreInitializationFailure failure = new StoreInitializationFailure(reason, message);
            _storeInitFailure = failure;
            OnStoreInitializationFailed?.Invoke(failure);
        }

        Purchasing.PurchaseProcessingResult Purchasing.IStoreListener.ProcessPurchase(Purchasing.PurchaseEventArgs storePurchaseEventArgs)
        {
            string transactionId = storePurchaseEventArgs.purchasedProduct.transactionID;
            string productId = storePurchaseEventArgs.purchasedProduct.definition.id;
            _log.Info("Got ProcessPurchase for purchase {TransactionId} of product {ProductId}", transactionId, productId);
            // \note We convert the PurchaseEventArgs here, before passing it to ProcessPurchaseImpl
            //       for the possibly-delayed handling, so that we don't keep a reference to the possibly mutable(?)
            //       Product within the PurchaseEventArgs.
            bool shouldProcessPurchase = TryConvertToMetaplayPurchaseEvent(storePurchaseEventArgs, out InAppPurchaseEvent purchaseEvent, out string ignoreReason);
            if (shouldProcessPurchase)
                ProcessPurchaseImpl(purchaseEvent);
            else
                _log.Info("Ignoring purchase {TransactionId} of product {ProductId}. Reason: {IgnoreReason}", transactionId, productId, ignoreReason);

            return Purchasing.PurchaseProcessingResult.Pending;
        }

        void ProcessPurchaseImpl(InAppPurchaseEvent purchaseEvent)
        {
            ExecuteOrEnqueueMetaplaySessionFunc(() =>
            {
                if (PlayerModel.PendingInAppPurchases.ContainsKey(purchaseEvent.TransactionId))
                {
                    // \note This can happen because the IAP store can repeat its request for the game to process a purchase that the game has already reported as pending. This is OK.
                    _log.Info("Attempted to begin processing purchase {TransactionId} of product {ProductId}, but it's already pending", purchaseEvent.TransactionId, purchaseEvent.ProductId);
                }
                else
                {
                    _log.Info("Will begin processing purchase {TransactionId} of product {ProductId}", purchaseEvent.TransactionId, purchaseEvent.ProductId);

                    // Run the action. Success is checked from whether the pending purchase was added.
                    PlayerContext.ExecuteAction(new PlayerInAppPurchased(purchaseEvent));
                    bool wasSuccess = PlayerModel.PendingInAppPurchases.ContainsKey(purchaseEvent.TransactionId);

                    if (wasSuccess)
                        OnStartedPurchaseValidation?.Invoke(purchaseEvent.Clone(PlayerModel.GetDataResolver())); // \note Cloning for safety
                    else
                    {
                        _log.Warning("PlayerInAppPurchased unexpectedly failed to add pending purchase");
                        PlayerContext.ExecuteAction(new PlayerInAppPurchaseClientRefused(purchaseEvent.ProductId, InAppPurchaseClientRefuseReason.CompletionActionFailed));
                        OnFailedToStartPurchaseValidation?.Invoke(purchaseEvent);
                    }
                }
            });
        }

        void Purchasing.IStoreListener.OnPurchaseFailed(Purchasing.Product product, Purchasing.PurchaseFailureReason reason)
        {
            OnPurchaseFailedImpl(GetInAppProductId(product), new StorePurchaseFailure(reason));
        }

#if USE_UNITY_PURCHASING_4_8_0_INTERFACES
        void Purchasing.IDetailedStoreListener.OnPurchaseFailed(Purchasing.Product product, Purchasing.Extension.PurchaseFailureDescription failureDescription)
        {
            OnPurchaseFailedImpl(GetInAppProductId(product), new StorePurchaseFailure(failureDescription));
        }
#endif

        void OnPurchaseFailedImpl(InAppProductId productId, StorePurchaseFailure failure)
        {
            _log.Info("Purchase of product {ProductId} failed: {Failure}", productId, failure);
            OnStorePurchaseFailed?.Invoke(productId, failure);

            InAppPurchaseClientRefuseReason reasonCode = GetClientRefuseReasonCode(failure.Reason);

            ExecuteOrEnqueueMetaplaySessionFunc(() =>
            {
                PlayerContext.ExecuteAction(new PlayerInAppPurchaseClientRefused(productId, reasonCode));
            });
        }

        #endregion

        #region Life cycle and updating

        public IAPManager(ISessionContextProvider sessionContextProvider)
        {
            _sessionContextProvider = sessionContextProvider ?? throw new ArgumentNullException(nameof(sessionContextProvider));

#if METAPLAY_USE_FAKE_UNITY_PURCHASING
            bool shouldInitializeStore = true;
            _log.Info("Created IAP manager (using fake Unity IAP)");
#elif METAPLAY_HAS_UNITY_PURCHASING
            bool shouldInitializeStore = true;
            _log.Info("Created IAP manager (using Unity IAP)");
#else
            bool shouldInitializeStore = false;
            _log.Warning("IAP store will not be initialized because Unity Purchasing is not available (and METAPLAY_USE_FAKE_UNITY_PURCHASING is also not defined).");
#endif

            if (shouldInitializeStore)
                ExecuteOrEnqueueMetaplaySessionFunc(BeginInitializeStore);
        }

        public void OnSessionStarted()
        {
            if (_pendingMetaplaySessionFuncs.Count > 0)
                _log.Debug("Flushing {NumPendingFuncs} pending session funcs", _pendingMetaplaySessionFuncs.Count);
            FlushPendingFuncs(_pendingMetaplaySessionFuncs);
        }

        public void OnSessionEnded()
        {
            foreach (InAppProductId productId in _pendingDynamicPurchases)
                OnPendingDynamicPurchaseUnregistered?.Invoke(productId);

            _pendingDynamicPurchases.Clear();

            foreach (InAppProductId productId in _pendingStaticPurchaseContexts)
                OnPendingStaticPurchaseUnregistered?.Invoke(productId);

            _pendingStaticPurchaseContexts.Clear();
        }

        public void Update()
        {
            if (MetaplaySessionExists)
            {
                UpdatePurchasePreparationTracking();

                if (!DebugOmitIAPClaim)
                    UpdatePendingPurchases();
            }
        }

        void UpdatePurchasePreparationTracking()
        {
            // Initiate purchase for pending dynamic-content purchases whose content has been confirmed by the server,
            // or pending static purchases whose context has been confirmed by the server.

            // \note Postponing deletion to avoid mutating hashset while traversing it.
            List<InAppProductId> pendingDynamicPurchasesToDelete = null;
            foreach (InAppProductId productId in _pendingDynamicPurchases)
            {
                if (PlayerModel.PendingDynamicPurchaseContents.TryGetValue(productId, out PendingDynamicPurchaseContent pendingContent)
                    && pendingContent.Status == PendingDynamicPurchaseContentStatus.ConfirmedByServer)
                {
                    bool beginPurchaseOk = TryBeginPurchaseProduct(productId);
                    if (beginPurchaseOk)
                    {
                        if (pendingDynamicPurchasesToDelete == null)
                            pendingDynamicPurchasesToDelete = new List<InAppProductId>();
                        pendingDynamicPurchasesToDelete.Add(productId);
                    }
                }
            }
            if (pendingDynamicPurchasesToDelete != null)
            {
                foreach (InAppProductId productId in pendingDynamicPurchasesToDelete)
                    _pendingDynamicPurchases.Remove(productId);
            }

            // \note Postponing deletion to avoid mutating hashset while traversing it.
            List<InAppProductId> pendingStaticPurchaseContextsToDelete = null;
            foreach (InAppProductId productId in _pendingStaticPurchaseContexts)
            {
                if (PlayerModel.PendingNonDynamicPurchaseContexts.TryGetValue(productId, out PendingNonDynamicPurchaseContext pendingContext)
                    && pendingContext.Status == PendingPurchaseAnalyticsContextStatus.ConfirmedByServer)
                {
                    bool beginPurchaseOk = TryBeginPurchaseProduct(productId);
                    if (beginPurchaseOk)
                    {
                        if (pendingStaticPurchaseContextsToDelete == null)
                            pendingStaticPurchaseContextsToDelete = new List<InAppProductId>();
                        pendingStaticPurchaseContextsToDelete.Add(productId);
                    }
                }
            }
            if (pendingStaticPurchaseContextsToDelete != null)
            {
                foreach (InAppProductId productId in pendingStaticPurchaseContextsToDelete)
                    _pendingStaticPurchaseContexts.Remove(productId);
            }
        }

        void UpdatePendingPurchases()
        {
            // Handle any delayed actions resulting from IPlayerModelClientListenerCore functions

            while (_delayedPlayerModelListenerFinishedPurchases.TryDequeue(out InAppPurchaseEvent ev))
                OnPurchaseFinishedInMetaplay?.Invoke(ev);

            // Poll pending IAP purchases, and handle those that need handling

            // \note Postponing reactions to avoid mutating PendingInAppPurchases while traversing it:
            List<Action> completionActions = null;
            foreach (InAppPurchaseEvent purchaseEvent in PlayerModel.PendingInAppPurchases.Values)
            {
                switch (purchaseEvent.Status)
                {
                    case InAppPurchaseStatus.PendingValidation:
                        // Do nothing, still waiting for server validation
                        break;

                    case InAppPurchaseStatus.ValidReceipt:
                        // Has been validated by server, now we can claim and confirm it
                        if (completionActions == null)
                            completionActions = new List<Action>();
                        completionActions.Add(() =>
                        {
                            _log.Info("Claiming purchase {TransactionId} of product {ProductId}", purchaseEvent.TransactionId, purchaseEvent.ProductId);
                            PlayerContext.ExecuteAction(new PlayerClaimPendingInAppPurchase(purchaseEvent.TransactionId));
                            HandleConfirmablyFinishedPurchase("successfully-claimed", purchaseEvent);
                        });
                        break;

                    case InAppPurchaseStatus.InvalidReceipt:
                        // Invalid receipt (removal is handled in PlayerInAppPurchaseValidated action, so we shouldn't be here)
                        _log.Error("Purchase {TransactionId} of product {ProductId} has invalid receipt but exists in pending purchases", purchaseEvent.TransactionId, purchaseEvent.ProductId);
                        break;

                    case InAppPurchaseStatus.ReceiptAlreadyUsed:
                        // Can happen (besides just cheaters) when a past purchase has been already claimed but its confirmation has not reached the
                        // IAP store, and the IAP store asks the game to re-process the purchase. The client cannot reject such
                        // re-processing cases on its own, as only the server knows about past purchases that have already been claimed.
                        // We clear and confirm it.
                        if (completionActions == null)
                            completionActions = new List<Action>();
                        completionActions.Add(() =>
                        {
                            _log.Info("Confirming {TransactionId} of product {ProductId}, where receipt had already been used", purchaseEvent.TransactionId, purchaseEvent.ProductId);
                            PlayerContext.ExecuteAction(new PlayerClearPendingDuplicateInAppPurchase(purchaseEvent.TransactionId));
                            HandleConfirmablyFinishedPurchase("duplicate", purchaseEvent);
                        });
                        break;
                }
            }
            if (completionActions != null)
            {
                foreach (Action completion in completionActions)
                    completion.Invoke();
            }
        }

        #endregion

        #region Optional IAP-related IPlayerModelClientListenerCore handlers, relayed by state manager

        // \note Here a handler exists only for failed-validation purchases, and invoking it is optional for the game.
        //       IAPManager does not rely on listeners for handling of claimed and duplicate-cleared purchases;
        //       those are handled directly in IAPManager.UpdatePendingPurchases .
        //
        //       The only facility provided by this handler is delaying the listening of validation-failed purchases,
        //       so that it does not occur during a PlayerAction.

        public void InAppPurchaseValidationFailed(InAppPurchaseEvent ev)
        {
            _log.Warning("Validation of purchase {TransactionId} of {ProductId} failed", ev.TransactionId, ev.ProductId);

            _delayedPlayerModelListenerFinishedPurchases.Enqueue(ev);
        }

        #endregion

        #region Helpers

        IPlayerClientContext    PlayerContext   => _sessionContextProvider.PlayerContext;
        IPlayerModelBase        PlayerModel     => PlayerContext?.Journal.StagedModel ?? null;

        // \note This is true even in the case that PlayerContext only exists because
        //       the game state is lingering in the background after a connection error
        //       happened. That should be ok, it might just result in PlayerActions
        //       being executed in vain.
        bool MetaplaySessionExists => PlayerContext != null;

        void HandleConfirmablyFinishedPurchase(string descriptionForLog, InAppPurchaseEvent ev)
        {
            OnPurchaseFinishedInMetaplay?.Invoke(ev.Clone(PlayerModel.GetDataResolver())); // \note Cloning for safety

            ExecuteOrEnqueueStoreFunc(() =>
            {
                StoreProductInfo? productInfoMaybe = TryGetStoreProductInfo(ev.ProductId);

                if (productInfoMaybe.HasValue)
                {
                    Purchasing.Product storeProduct = productInfoMaybe.Value.Product;

                    if (storeProduct.transactionID == ev.TransactionId)
                    {
                        _log.Info("Confirming {Description} purchase {TransactionId} of {ProductId}", descriptionForLog, ev.TransactionId, ev.ProductId);
                        _store.Controller.ConfirmPendingPurchase(storeProduct);
                    }
                    else
                        _log.Warning("Tried to confirm {Description} purchase {TransactionId} of {ProductId}, but store product has different transaction id {StoreTransactionId}", descriptionForLog, ev.TransactionId, ev.ProductId, storeProduct.transactionID ?? "<null>");
                }
                else
                    _log.Warning("Tried to confirm {Description} purchase {TransactionId} of {ProductId}, but matching store product was not found", descriptionForLog, ev.TransactionId, ev.ProductId);
            });
        }

        void ExecuteOrEnqueueMetaplaySessionFunc(Action func)
        {
            ExecuteOrEnqueueFunc("session", _pendingMetaplaySessionFuncs, MetaplaySessionExists, func);
        }

        void ExecuteOrEnqueueStoreFunc(Action func)
        {
            ExecuteOrEnqueueFunc("store", _pendingStoreFuncs, _store != null, func);
        }

        void ExecuteOrEnqueueFunc(string funcKindName, Queue<Action> funcQueue, bool executeNow, Action func)
        {
            if (executeNow)
                func();
            else
            {
                _log.Debug("Delaying a {FuncKind} func", funcKindName);
                funcQueue.Enqueue(func);
            }
        }

        void FlushPendingFuncs(Queue<Action> funcs)
        {
            while (funcs.TryDequeue(out Action func))
            {
                try
                {
                    func();
                }
                catch (Exception ex)
                {
                    _log.Error("Exception when executing pending func: {Error}", ex);
                    MetaplaySDK.IncidentTracker.ReportUnhandledException(ex);
                }
            }
        }

        #endregion

        #region Misc utilities

        static Purchasing.ProductType ConvertToStoreProductType(InAppProductType ourType)
        {
            switch (ourType)
            {
                case InAppProductType.Consumable:       return Purchasing.ProductType.Consumable;
                case InAppProductType.NonConsumable:    return Purchasing.ProductType.NonConsumable;
                case InAppProductType.Subscription:     return Purchasing.ProductType.Subscription;
                default:
                    throw new InvalidEnumArgumentException(nameof(ourType), (int)ourType, typeof(InAppProductType));
            }
        }

        static bool TryConvertToMetaplayPurchaseEvent(Purchasing.PurchaseEventArgs e, out InAppPurchaseEvent purchaseEvent, out string ignoreReason)
        {
            IAPReceiptExtraction.ReceiptInfo    receiptInfo         = IAPReceiptExtraction.GetReceiptInfo(e);
            string                              receipt             = receiptInfo.Receipt;
            string                              signature           = receiptInfo.Signature; // \note May be null, depending on store
            string                              orderId             = receiptInfo.OrderId; // \note Only available for Google Play, null for others
            string                              transactionId       = receiptInfo.TransactionId;
            InAppProductId                      productId           = GetInAppProductId(e.purchasedProduct);
            string                              platformProductId   = e.purchasedProduct.definition.storeSpecificId;

            if (receiptInfo.IsDeferredPurchase)
            {
                purchaseEvent = null;
                ignoreReason = "The purchase is in 'deferred' state";
                return false;
            }

            switch (receiptInfo.Store)
            {
                case IAPReceiptExtraction.Store.AppleAppStore:  purchaseEvent = InAppPurchaseEvent.ForApple          (transactionId, productId, platformProductId, receipt);                        break;
                case IAPReceiptExtraction.Store.GooglePlay:     purchaseEvent = InAppPurchaseEvent.ForGoogle         (transactionId, productId, platformProductId, receipt, signature, orderId);    break;
                case IAPReceiptExtraction.Store.Fake:           purchaseEvent = InAppPurchaseEvent.ForDevelopment    (transactionId, productId, platformProductId, receipt, signature);             break;
                default:
                    throw new NotImplementedException($"Unhandled {nameof(IAPReceiptExtraction.Store)}: {receiptInfo.Store}");
            }

            ignoreReason = null;
            return true;
        }

        static InAppProductId GetInAppProductId(Purchasing.Product product)
        {
            return InAppProductId.FromString(product.definition.id);
        }

        InAppPurchaseClientRefuseReason GetClientRefuseReasonCode(Purchasing.PurchaseFailureReason reason)
        {
            switch (reason)
            {
                case Purchasing.PurchaseFailureReason.PurchasingUnavailable:    return InAppPurchaseClientRefuseReason.UnityPurchasingUnavailable;
                case Purchasing.PurchaseFailureReason.ExistingPurchasePending:  return InAppPurchaseClientRefuseReason.UnityExistingPurchasePending;
                case Purchasing.PurchaseFailureReason.ProductUnavailable:       return InAppPurchaseClientRefuseReason.UnityProductUnavailable;
                case Purchasing.PurchaseFailureReason.SignatureInvalid:         return InAppPurchaseClientRefuseReason.UnitySignatureInvalid;
                case Purchasing.PurchaseFailureReason.UserCancelled:            return InAppPurchaseClientRefuseReason.UnityUserCancelled;
                case Purchasing.PurchaseFailureReason.PaymentDeclined:          return InAppPurchaseClientRefuseReason.UnityPaymentDeclined;
                case Purchasing.PurchaseFailureReason.DuplicateTransaction:     return InAppPurchaseClientRefuseReason.UnityDuplicateTransaction;
                default:                                                        return InAppPurchaseClientRefuseReason.Unknown;
            }
        }

        #endregion
    }
}
