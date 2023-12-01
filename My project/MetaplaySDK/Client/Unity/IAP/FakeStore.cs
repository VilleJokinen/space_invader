// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.InAppPurchase;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using UnityEngine;

using Purchasing = Metaplay.Unity.IAP.FakePurchasing;

namespace Metaplay.Unity.IAP
{
    /// <summary>
    /// A fake IAP store implementation for simple testing of in-app purchases.
    /// This is used by <see cref="IAPManager"/> when METAPLAY_USE_FAKE_UNITY_PURCHASING
    /// is defined. It may be useful for testing early development of IAP in projects
    /// where the actual Unity IAP hasn't yet been installed, but should not be used
    /// for production use since it's not a real IAP store.
    /// METAPLAY_USE_FAKE_UNITY_PURCHASING should be removed once the real Unity IAP
    /// has been installed in the project.
    /// </summary>
    public class FakeStore : Purchasing.IStoreController, Purchasing.IExtensionProvider
    {
        public static FakeStore         InstanceForEditor   { get; private set; }

        IAPFakeStoreConfig              _config             = IAPFakeStoreConfig.Instance;

        Purchasing.ProductCollection    _products           = null;
        Purchasing.IStoreListener       _listener           = null;

        HashSet<string> _pendingTransactions = new HashSet<string>();

        public FakeStore()
        {
            if (MetaplaySDKBehavior.Instance == null)
                throw new InvalidOperationException($"A {nameof(MetaplaySDKBehavior)} instance needs to exist to use {nameof(FakeStore)}");

            InstanceForEditor = this;
        }

        #region Initialization

        public void Initialize(Purchasing.IStoreListener listener, Purchasing.ConfigurationBuilder builder)
        {
            RunPossiblyDelayed("initialization", _config.InitIsSynchronous, _config.AsyncInitDelay, () => InitializeImpl(listener, builder));
        }

        void InitializeImpl(Purchasing.IStoreListener listener, Purchasing.ConfigurationBuilder builder)
        {
            bool success = !_config.ForceInitFailure;

            if (success)
            {
                MetaplaySDK.Logs.IAPFakeStore.Debug("Initializing with success");

                _products = new Purchasing.ProductCollection(builder.products.Select(CreateProduct).ToArray());
                RepaintEditorGUI();
                _listener = listener;

                listener.OnInitialized(controller: this, extensions: this);
            }
            else
            {
                MetaplaySDK.Logs.IAPFakeStore.Debug("Initializing with failure");
                listener.OnInitializeFailed(Purchasing.InitializationFailureReason.DebugForcedFailure);
            }
        }

        Purchasing.Product CreateProduct(Purchasing.ProductDefinition definition)
        {
            return new Purchasing.Product
            {
                availableToPurchase = !_config.DisabledProductIds.Contains(definition.id),
                definition = definition,
                metadata = new Purchasing.ProductMetadata
                {
                    localizedPriceString = string.Format(_config.LocalizedPriceStringFormat, 1.23),
                },
                transactionID = null,
                receipt = null,

                originalTransactionId = null,
            };
        }

        #endregion

        #region IStoreController

        Purchasing.ProductCollection Purchasing.IStoreController.products => _products;

        void Purchasing.IStoreController.InitiatePurchase(Purchasing.Product product)
        {
            RunPossiblyDelayed($"purchase of {product.definition.id}", _config.PurchaseIsSynchronous, _config.AsyncPurchaseDelay, () => InitiatePurchaseImpl(product));
        }

        void Purchasing.IStoreController.ConfirmPendingPurchase(Purchasing.Product product)
        {
            if (product.transactionID == null)
            {
                MetaplaySDK.Logs.IAPFakeStore.Warning("Got confirmation for purchase of product {ProductId}, but there is no transaction on it", product.definition.id);
                return;
            }

            if (!_pendingTransactions.Contains(product.transactionID))
            {
                MetaplaySDK.Logs.IAPFakeStore.Warning("Got confirmation for purchase of product {ProductId}, but it is not pending", product.definition.id);
                return;
            }

            if (_config.IgnorePurchaseConfirmation)
            {
                MetaplaySDK.Logs.IAPFakeStore.Info("Debug-ignoring confirmation of purchase of product {ProductId} with transaction {TransactionId}, the purchase remains pending in the fake store", product.definition.id, product.transactionID);
                return;
            }

            MetaplaySDK.Logs.IAPFakeStore.Debug("Got confirmation for purchase {ProductId} with transaction {TransactionId}", product.definition.id, product.transactionID);

            _pendingTransactions.Remove(product.transactionID);

            if (product.definition.productType == Purchasing.ProductType.Consumable)
            {
                product.transactionID           = null;
                product.receipt                 = null;
                product.originalTransactionId   = null;
            }

            RepaintEditorGUI();
        }

        void InitiatePurchaseImpl(Purchasing.Product product)
        {
            if (product.transactionID != null)
            {
                if (_pendingTransactions.Contains(product.transactionID))
                    OnPurchaseFailed(product, Purchasing.PurchaseFailureReason.ExistingPurchasePending, $"Purchase of {product.definition.id} failed due to an already pending transaction on the product");
                else
                    OnPurchaseFailed(product, Purchasing.PurchaseFailureReason.DuplicateTransaction, $"Purchase of {product.definition.id} failed because it was already purchased and isn't a consumable"); // \todo Is this an appropriate PurchaseFailureReason?

                return;
            }

            if (!product.availableToPurchase)
            {
                OnPurchaseFailed(product, Purchasing.PurchaseFailureReason.ProductUnavailable, $"Purchase of {product.definition.id} failed because the product is not available");
                return;
            }

            if (_config.ForcePurchaseFailure)
            {
                OnPurchaseFailed(product, Purchasing.PurchaseFailureReason.DebugForcedFailure, $"Purchase of {product.definition.id} failed due to force-failure");
                return;
            }

            string transactionId = CreateNewTransactionId();

            MetaplaySDK.Logs.IAPFakeStore.Debug("Process purchase of {ProductId} with transaction id {TransactionId}", product.definition.id, transactionId);

            string receipt = CreateReceipt(platformProductId: product.definition.storeSpecificId, transactionId: transactionId, originalTransactionId: transactionId);

            product.transactionID           = transactionId;
            product.receipt                 = receipt;
            product.originalTransactionId   = transactionId;
            _pendingTransactions.Add(transactionId);
            RepaintEditorGUI();

            _listener.ProcessPurchase(new Purchasing.PurchaseEventArgs{ purchasedProduct = product });
        }

        void OnPurchaseFailed(Purchasing.Product product, Purchasing.PurchaseFailureReason reason, string message)
        {
            MetaplaySDK.Logs.IAPFakeStore.Debug("{PurchaseFailureMessage}", message);

            if (_listener is Purchasing.IDetailedStoreListener detailedListener)
                detailedListener.OnPurchaseFailed(product, new Purchasing.Extension.PurchaseFailureDescription(product.definition.storeSpecificId, reason, message));
            else
            {
#pragma warning disable CS0618 // "[...] is obsolete". Deliberately calling the obsolete overload when user didn't give IDetailedStoreListener. For testing IAPManager's compliance with the older interface.
                _listener.OnPurchaseFailed(product, reason);
#pragma warning restore CS0618
            }
        }

        string CreateNewTransactionId()
        {
            if (_config.UseFixedTransactionId)
                return _config.FixedTransactionId;
            else
            {
                char[] suffix = new char[UnityEngine.Random.Range(200, 300)];
                for (int i = 0; i < suffix.Length; i++)
                    suffix[i] = (char)UnityEngine.Random.Range((int)'a', (int)'z');
                return "fakeTxn_" + new string(suffix);
            }
        }

        string CreateReceipt(string platformProductId, string transactionId, string originalTransactionId)
        {
            Receipts.DevelopmentReceiptAndSignature devReceiptAndSignature;

            if (_config.ForceIllFormedReceipt)
            {
                MetaplaySDK.Logs.IAPFakeStore.Debug("Forcing ill-formed receipt");
                devReceiptAndSignature = Receipts.DevelopmentReceiptAndSignature.FromRawReceipt("{InvalidReceiptJson");
            }
            else
            {
                var content = new Receipts.DevelopmentReceiptContent
                {
                    productId                               = platformProductId,
                    transactionId                           = transactionId,
                    originalTransactionId                   = originalTransactionId,
                    validationDelaySeconds                  = _config.ValidationDelay,
                    validationTransientErrorProbability     = _config.ValidationTransientErrorProbability,
                    subscriptionIsAcquiredViaFamilySharing  = _config.PretendSubscriptionIsFamilyShared,
                    paymentType                             = _config.IAPPaymentType?.ToString(),
                };

                devReceiptAndSignature = Receipts.DevelopmentReceiptAndSignature.FromContent(content);
            }

            if (_config.ForceInvalidSignature)
            {
                MetaplaySDK.Logs.IAPFakeStore.Debug("Forcing invalid signature");
                devReceiptAndSignature.Signature = "InvalidSignature";
            }

            return JsonUtility.ToJson(new Receipts.UnityReceipt
            {
                Store               = PurchasingMisc.UnityFakeStoreName,
                TransactionID       = transactionId,
                Payload             = JsonUtility.ToJson(devReceiptAndSignature),

                IsMetaplayFakeStore = true,
            });
        }

        #endregion

        #region IExtensionProvider

        T Purchasing.IExtensionProvider.GetExtension<T>()
        {
            if (typeof(T) == typeof(Purchasing.IAppleExtensions))
                return (T)(object)new AppleExtensions(this);
            return null;
        }

        class AppleExtensions : Purchasing.IAppleExtensions
        {
            FakeStore _store;

            public AppleExtensions(FakeStore store)
            {
                _store = store;
            }

            public void RestoreTransactions(Action<bool> callback)
            {
                RestoreTransactions((bool success, string message) =>
                {
                    callback(success);
                });
            }

            public void RestoreTransactions(Action<bool, string> callback)
            {
                _store.RunAfter(1f, () =>
                {
                    callback(true, null);

                    _store.RestorePurchases();
                });
            }
        }

        #endregion

        #region Purchase restoration

        public void RestorePurchases()
        {
            foreach (Purchasing.Product product in _products.all)
            {
                if (product.definition.productType != Purchasing.ProductType.Consumable && product.transactionID != null)
                {
                    string newTransactionId = CreateNewTransactionId();
                    string newReceipt       = CreateReceipt(platformProductId: product.definition.storeSpecificId, transactionId: newTransactionId, originalTransactionId: product.originalTransactionId);

                    MetaplaySDK.Logs.IAPFakeStore.Info("Restoring subscription {OldTransactionId} -> {NewTransactionId} (original {OriginalTransactionId}) of {ProductId}", product.transactionID, newTransactionId, product.originalTransactionId, product.definition.id);

                    product.transactionID   = newTransactionId;
                    product.receipt         = newReceipt;
                    _pendingTransactions.Add(newTransactionId);

                    _listener.ProcessPurchase(new Purchasing.PurchaseEventArgs{ purchasedProduct = product });
                }
            }

            RepaintEditorGUI();
        }

        #endregion

        #region Editor actions

        public void ForgetPendingPurchases()
        {
            foreach (Purchasing.Product product in _products.all)
            {
                if (product.transactionID != null)
                {
                    MetaplaySDK.Logs.IAPFakeStore.Info("Forgetting purchase {TransactionId} of {ProductId}", product.transactionID, product.definition.id);

                    _pendingTransactions.Remove(product.transactionID);

                    if (product.definition.productType == Purchasing.ProductType.Consumable)
                    {
                        product.transactionID           = null;
                        product.receipt                 = null;
                        product.originalTransactionId   = null;
                    }
                }
            }

            RepaintEditorGUI();
        }

        public void ReRequestPendingProcessing()
        {
            foreach (Purchasing.Product product in _products.all)
            {
                if (_pendingTransactions.Contains(product.transactionID))
                {
                    MetaplaySDK.Logs.IAPFakeStore.Info("Re-requesting processing of pending purchase {TransactionId} of {ProductId}", product.transactionID, product.definition.id);

                    _listener.ProcessPurchase(new Purchasing.PurchaseEventArgs{ purchasedProduct = product });
                }
            }
        }

        public void ForgetSubscriptions()
        {
            foreach (Purchasing.Product product in _products.all)
            {
                if (product.definition.productType == Purchasing.ProductType.Subscription && product.transactionID != null)
                {
                    MetaplaySDK.Logs.IAPFakeStore.Info("Forgetting subscription {TransactionId} of {ProductId}", product.transactionID, product.definition.id);

                    _pendingTransactions.Remove(product.transactionID);

                    product.transactionID           = null;
                    product.receipt                 = null;
                    product.originalTransactionId   = null;
                }
            }

            RepaintEditorGUI();
        }

        #endregion

        #region Additional editor UI

#if UNITY_EDITOR
        public void ProductsGUI()
        {
            if (_products == null)
            {
                GUILayout.Label("Not initialized");
                return;
            }

            foreach (Purchasing.Product product in _products.all)
            {
                GUILayout.Label($"Id: {product.definition.id} ({product.definition.productType})");
                GUILayout.Label($"Store-specific id: {product.definition.storeSpecificId}");
                GUILayout.Label($"Available to purchase: {product.availableToPurchase}");
                GUILayout.Label($"Transaction id: {product.transactionID ?? "<none>"}");
                GUILayout.Label($"Original Transaction id: {product.originalTransactionId ?? "<none>"}");
                GUILayout.Label($"Pending? {product.transactionID != null && _pendingTransactions.Contains(product.transactionID)}");
                GUILayout.Label("");
            }
        }
#endif

        #endregion

        #region Misc utilities

        void RunPossiblyDelayed(string nameForLogMaybe, bool isSynchronous, float delaySecondsIfAsync, Action func)
        {
            if (isSynchronous)
                func();
            else
            {
                if (nameForLogMaybe != null)
                    MetaplaySDK.Logs.IAPFakeStore.Debug("Delaying {Action} by {DelaySeconds} sec", nameForLogMaybe, delaySecondsIfAsync);

                RunAfter(delaySecondsIfAsync, func);
            }
        }

        void RunAfter(float delaySeconds, Action func)
        {
            MetaplaySDKBehavior.Instance.StartCoroutine(RunAfterCoroutine(delaySeconds, func));
        }

        IEnumerator RunAfterCoroutine(float delaySeconds, Action func)
        {
            yield return new WaitForSeconds(delaySeconds);
            func();
        }

        void RepaintEditorGUI()
        {
#if UNITY_EDITOR
            foreach (FakeStoreEditor editor in Resources.FindObjectsOfTypeAll<FakeStoreEditor>())
                editor.Repaint();
#endif
        }

        #endregion
    }
}
