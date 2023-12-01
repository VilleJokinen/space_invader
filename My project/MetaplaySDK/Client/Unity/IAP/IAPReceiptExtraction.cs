// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using UnityEngine;

#if METAPLAY_USE_FAKE_UNITY_PURCHASING
using Purchasing = Metaplay.Unity.IAP.FakePurchasing;
#elif METAPLAY_HAS_UNITY_PURCHASING
using Purchasing = UnityEngine.Purchasing;
#else
// Unity IAP not available, and also fake store is not opted-into; IAPManager will be effectively disabled.
// Using the fake namespace just so the code will compile without having to #if it all out.
using Purchasing = Metaplay.Unity.IAP.FakePurchasing;
#endif

namespace Metaplay.Unity.IAP
{
/// <summary>
/// Utilities for extracting receipts and signatures from Unity's PurchaseEventArgs.
/// </summary>
public static class IAPReceiptExtraction
{
    public static ReceiptInfo GetReceiptInfo(Purchasing.PurchaseEventArgs e)
    {
        Receipts.UnityReceipt unityReceipt = GetUnityReceipt(e);

        Store   store;
        string  receipt;
        string  signature;
        string  orderId;
        bool    isDeferredPurchase;

        switch (unityReceipt.Store)
        {
            case Purchasing.AppleAppStore.Name:
                store                   = Store.AppleAppStore;
                receipt                 = GetAppleAppStoreReceipt(unityReceipt.Payload);
                signature               = null; // \note No separate signature with Apple App Store
                orderId                 = null; // \note No orderId here, it's a Google Play thing
                isDeferredPurchase      = false;
                break;

            case Purchasing.GooglePlay.Name:
            {
                store                   = Store.GooglePlay;
                (receipt, signature)    = GetGooglePlayReceiptAndSignature(unityReceipt.Payload);

                GooglePlayReceipt parsedReceiptMaybe = TryParseGooglePlayReceipt(receipt);
                orderId                 = parsedReceiptMaybe?.orderId;
                isDeferredPurchase      = parsedReceiptMaybe?.purchaseState == 4;
                break;
            }

            case Metaplay.Unity.IAP.PurchasingMisc.UnityFakeStoreName:
                store                   = Store.Fake;
                (receipt, signature)    = GetDevelopmentReceiptAndSignature(e, unityReceipt);
                orderId                 = null; // \note No orderId here, it's a Google Play thing
                isDeferredPurchase      = false;
                break;

            default:
                throw new IAPReceiptException($"Unhandled store: {unityReceipt.Store}");
        }

        return new ReceiptInfo(
            store:              store,
            transactionId:      unityReceipt.TransactionID,
            receipt:            receipt,
            signature:          signature,
            orderId:            orderId,
            isDeferredPurchase: isDeferredPurchase);
    }

    public class ReceiptInfo
    {
        public Store    Store;
        public string   TransactionId;
        public string   Receipt;
        public string   Signature; // \note May be null, depending on platform
        public string   OrderId; // \note Only available for Google Play, null for others
        public bool     IsDeferredPurchase;

        public ReceiptInfo (Store store, string transactionId, string receipt, string signature, string orderId, bool isDeferredPurchase)
        {
            Store               = store;
            TransactionId       = transactionId;
            Receipt             = receipt;
            Signature           = signature;
            OrderId             = orderId;
            IsDeferredPurchase  = isDeferredPurchase;
        }
    }

    public enum Store
    {
        AppleAppStore,
        GooglePlay,
        Fake,
    }

    public static string GetAppleAppStoreReceipt(string unityReceiptPayload)
    {
        return unityReceiptPayload; // Receipt is the entire payload
    }

    /// <remarks>
    /// Serialized using <see cref="UnityEngine.JsonUtility"/>,
    /// field names are meaningful.
    /// </remarks>
    [Serializable]
    public class GooglePlayPayload
    {
        public string json;
        public string signature;
    }

    public static (string receipt, string signature) GetGooglePlayReceiptAndSignature(string unityReceiptPayload)
    {
        GooglePlayPayload googlePlayPayload = JsonUtility.FromJson<GooglePlayPayload>(unityReceiptPayload);
        return (googlePlayPayload.json, googlePlayPayload.signature);
    }

    /// <remarks>
    /// Serialized using <see cref="UnityEngine.JsonUtility"/>,
    /// field names are meaningful.
    /// </remarks>
    [Serializable]
    public class GooglePlayReceipt
    {
        public string   orderId;
        public int      purchaseState;
    }

    public static GooglePlayReceipt TryParseGooglePlayReceipt(string googlePlayReceipt)
    {
        try
        {
            return JsonUtility.FromJson<GooglePlayReceipt>(googlePlayReceipt);
        }
        catch
        {
            return null;
        }
    }

    /// <remarks>
    /// Serialized using <see cref="UnityEngine.JsonUtility"/>,
    /// field names are meaningful.
    /// </remarks>
    [Serializable]
    public class DevelopmentReceipt
    {
        public string Receipt;
        public string Signature;
    }

    public static (string receipt, string signature) GetDevelopmentReceiptAndSignature(Purchasing.PurchaseEventArgs e, Receipts.UnityReceipt unityReceipt)
    {
        if (unityReceipt.IsMetaplayFakeStore)
        {
            // FakeStore creates a receipt Payload of the proper format expected by Metaplay
            // for development receipts. Use that as is.

            DevelopmentReceipt developmentReceipt = JsonUtility.FromJson<DevelopmentReceipt>(unityReceipt.Payload);
            return (developmentReceipt.Receipt, developmentReceipt.Signature);
        }
        else
        {
            // Actual Unity Purchasing doesn't create a Payload of the format expected by Metaplay
            // for development receipts. So let's just create a development receipt here.

            var content = new Receipts.DevelopmentReceiptContent
            {
                productId               = e.purchasedProduct.definition.storeSpecificId,
                transactionId           = unityReceipt.TransactionID,
                // \note We could also leave originalTransactionId as null, except we're using
                //       JsonUtility.ToJson which for some reason replaces the null with an empty string.
                //       Maybe replace JsonUtility usage with Json.Net, which we're using anyway on the client now.
                originalTransactionId   = unityReceipt.TransactionID,
            };

            var receiptAndSignature = Receipts.DevelopmentReceiptAndSignature.FromContent(content);

            return (receiptAndSignature.Receipt, receiptAndSignature.Signature);
        }
    }

    public static Receipts.UnityReceipt GetUnityReceipt(Purchasing.PurchaseEventArgs e)
    {
        string receipt = e.purchasedProduct.receipt ?? throw new IAPReceiptException("Null receipt string in purchase");
        return JsonUtility.FromJson<Receipts.UnityReceipt>(receipt);
    }

    public class IAPReceiptException : Exception
    {
        public IAPReceiptException(string message) : base(message) { }
        public IAPReceiptException(string message, Exception inner) : base(message, inner) { }
    }
}
}
