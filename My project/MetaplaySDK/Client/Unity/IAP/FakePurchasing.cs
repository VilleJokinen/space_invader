// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System;

namespace Metaplay.Unity.IAP
{
    /// <summary>
    /// Fake "Unity Purchasing", for testing IAPManager etc. without needing to
    /// add the Unity IAP service to the project.
    ///
    /// Does not fully correspond to Unity Purchasing: only contains minimal stuff as needed by IAPManager etc.
    ///
    /// Not a real IAP implementation, obviously.
    /// </summary>
    public static class FakePurchasing
    {
        public static class UnityPurchasing
        {
            [Obsolete("Obsoleted as in the real UnityPurchasing. Use the overload of Initialize that takes a IDetailedStoreListener instead.")]
            public static void Initialize(IStoreListener listener, ConfigurationBuilder builder)
            {
                InitializeImpl(listener, builder);
            }

            public static void Initialize(IDetailedStoreListener listener, ConfigurationBuilder builder)
            {
                InitializeImpl(listener, builder);
            }

            static void InitializeImpl(IStoreListener listener, ConfigurationBuilder builder)
            {
                _fakeStore = new FakeStore();
                _fakeStore.Initialize(listener, builder);
            }

            static FakeStore _fakeStore;
        }

        public class ConfigurationBuilder
        {
            public HashSet<ProductDefinition> products { get; } = new HashSet<ProductDefinition>();

            public static ConfigurationBuilder Instance(StandardPurchasingModule _) => new ConfigurationBuilder();

            public void AddProduct(string id, ProductType productType, IDs ids)
            {
                products.Add(new ProductDefinition
                {
                    id              = id,
                    storeSpecificId = ids.FirstOrDefault(entry => entry.Value == PurchasingMisc.UnityFakeStoreName).Key ?? id,
                    productType     = productType,
                });
            }
        }

        public class StandardPurchasingModule
        {
            public static StandardPurchasingModule Instance() => _instance;

            static StandardPurchasingModule _instance = new StandardPurchasingModule();
        }

        public enum ProductType
        {
            Consumable,
            NonConsumable,
            Subscription,
        }

        public static class GooglePlay
        {
            public const string Name = "GooglePlay";
        }

        public static class AppleAppStore
        {
            public const string Name = "AppleAppStore";
        }

        public class IDs : IEnumerable<KeyValuePair<string, string>>
        {
            List<KeyValuePair<string, string>> _entries = new List<KeyValuePair<string, string>>();

            public void Add(string storeSpecificId, string storeName)
            {
                _entries.Add(new KeyValuePair<string, string>(storeSpecificId, storeName));
            }

            public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _entries.GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        public interface IStoreListener
        {
            void OnInitialized(IStoreController controller, IExtensionProvider extensions);
            void OnInitializeFailed(InitializationFailureReason error);
            void OnInitializeFailed(InitializationFailureReason error, string message);
            [Obsolete("Obsoleted as in the real UnityPurchasing. Use IDetailedStoreListener.OnPurchaseFailed instead.")]
            void OnPurchaseFailed(Product i, PurchaseFailureReason p);
            PurchaseProcessingResult ProcessPurchase(PurchaseEventArgs e);
        }

        public interface IDetailedStoreListener : IStoreListener
        {
            void OnPurchaseFailed(Product product, Extension.PurchaseFailureDescription failureDescription);
        }

        public static class Extension
        {
            public class PurchaseFailureDescription
            {
                public string productId { get; private set; }
                public PurchaseFailureReason reason { get; private set; }
                public string message { get; private set; }

                public PurchaseFailureDescription(string productId_, PurchaseFailureReason reason_, string message_)
                {
                    productId = productId_;
                    reason = reason_;
                    message = message_;
                }
            }
        }

        public interface IStoreController
        {
            ProductCollection products { get; }

            void InitiatePurchase(Product product);
            void ConfirmPendingPurchase(Product product);
        }

        public interface IExtensionProvider
        {
            T GetExtension<T>() where T : class;
        }

        public interface IAppleExtensions
        {
            void RestoreTransactions(Action<bool> callback);
            void RestoreTransactions(Action<bool, string> callback);
        }

        public enum InitializationFailureReason
        {
            DebugForcedFailure,
        }

        public class Product
        {
            public bool availableToPurchase;
            public ProductDefinition definition;
            public ProductMetadata metadata;
            public string transactionID;
            public string receipt;

            public string originalTransactionId;
        }

        public class ProductDefinition
        {
            public string id;
            public string storeSpecificId;
            public ProductType productType;
        }

        public class ProductMetadata
        {
            public string localizedPriceString;
        }

        public enum PurchaseFailureReason
        {
            PurchasingUnavailable,
            ExistingPurchasePending,
            ProductUnavailable,
            SignatureInvalid,
            UserCancelled,
            PaymentDeclined,
            DuplicateTransaction,
            Unknown,

            DebugForcedFailure,
        }

        public class PurchaseEventArgs
        {
            public Product purchasedProduct;
        }

        public enum PurchaseProcessingResult
        {
            Pending,
        }

        public class ProductCollection
        {
            public Product[] all { get; }

            public ProductCollection(Product[] products)
            {
                all = products;
            }

            public Product WithID(string id) => all.FirstOrDefault(product => product.definition.id == id);
        }
    }
}
