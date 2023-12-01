// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using UnityEngine;

namespace Metaplay.Unity.IAP.Receipts
{
    /// <remarks>
    /// Serialized using <see cref="UnityEngine.JsonUtility"/>,
    /// field names are meaningful.
    /// </remarks>
    [Serializable]
    public class DevelopmentReceiptContent
    {
        public string   productId;
        public string   transactionId;
        public string   originalTransactionId;
        public float    validationDelaySeconds              = 0f;
        public float    validationTransientErrorProbability = 0f;
        public bool     subscriptionIsAcquiredViaFamilySharing = false;
        public string   paymentType = "Normal";
    }

    /// <remarks>
    /// Serialized using <see cref="UnityEngine.JsonUtility"/>,
    /// field names are meaningful.
    /// </remarks>
    public class DevelopmentReceiptAndSignature
    {
        public string Receipt;
        public string Signature;

        public DevelopmentReceiptAndSignature(){ }
        public DevelopmentReceiptAndSignature(string receipt, string signature)
        {
            Receipt     = receipt;
            Signature   = signature;
        }

        public static DevelopmentReceiptAndSignature FromContent(DevelopmentReceiptContent content)
        {
            return FromRawReceipt(JsonUtility.ToJson(content));
        }

        public static DevelopmentReceiptAndSignature FromRawReceipt(string rawReceipt)
        {
            return new DevelopmentReceiptAndSignature(
                receipt:    Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(rawReceipt)),
                signature:  Metaplay.Core.Util.ComputeSHA1(rawReceipt));
        }
    }

    /// <summary>
    /// Corresponds to Unity Purchasing's json-encoded receipt.
    /// (Except for <see cref="IsMetaplayFakeStore"/>.)
    /// </summary>
    /// <remarks>
    /// Serialized using <see cref="UnityEngine.JsonUtility"/>,
    /// field names are meaningful.
    /// </remarks>
    [Serializable]
    public class UnityReceipt
    {
        public string Store;

        public string TransactionID;

        /// <summary>
        /// Platform-specific receipt payload. Contains actual receipt and potentially also signature.
        /// </summary>
        public string Payload;

        /// <summary>
        /// Set by <see cref="FakeStore" /> to distinguish it from Unity Purchasing.
        /// Used to for some decisions on how to process receipt <see cref="Payload" />
        /// for fake (development) store platform.
        /// </summary>
        public bool IsMetaplayFakeStore = false;
    }
}
