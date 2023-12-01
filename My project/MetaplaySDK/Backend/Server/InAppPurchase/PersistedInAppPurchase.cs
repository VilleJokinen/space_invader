// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Persistence;
using Metaplay.Core;
using Metaplay.Core.InAppPurchase;
using Metaplay.Core.Model;
using Metaplay.Core.Serialization;
using Microsoft.EntityFrameworkCore;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Metaplay.Server.InAppPurchase
{
    /// <summary>
    /// Database-persisted in-app purchase event (queryable by transactionId).
    /// </summary>
    [Table("InAppPurchases")]
    public class PersistedInAppPurchase : IPersistedItem
    {
        /// <summary>
        /// Id of the purchase, as returned by
        /// <see cref="InAppPurchaseTransactionIdUtil.ResolveTransactionDeduplicationId"/>.
        /// This is not necessarily the transaction id as reported by Unity,
        /// see comments in the above-mentioned method for more information.
        /// </summary>
        [Key]
        [PartitionKey]
        [Required]
        [MaxLength(InAppPurchaseUtil.TransactionIdMaxLengthCodePoints)]
        [Column(TypeName = "varchar(512)")]
        public string   TransactionId   { get; set; }

        [Required]
        public byte[]   Event           { get; set; }   // tagged-serialized InAppPurchaseEvent

        [Required]
        public bool     IsValidReceipt  { get; set; }

        [Required]
        [Column(TypeName = "varchar(64)")]
        public string   PlayerId        { get; set; }

        [Required]
        [Column(TypeName = "DateTime")]
        public DateTime CreatedAt       { get; set; }

        public EntityId PlayerEntityId => EntityId.ParseFromString(PlayerId);

        PersistedInAppPurchase() { }
        public PersistedInAppPurchase(string transactionDeduplicationId, MetaSerialized<InAppPurchaseEvent> ev, bool isValidReceipt, EntityId playerId, DateTime createdAt)
        {
            TransactionId   = transactionDeduplicationId;
            Event           = ev.Bytes;
            IsValidReceipt  = isValidReceipt;
            PlayerId        = playerId.ToString();
            CreatedAt       = createdAt;
        }
    }

    /// <summary>
    /// Database-persisted item describing a player's IAP subscription.
    /// Note that for subscription purchases, both this item as well as
    /// <see cref="PersistedInAppPurchase"/> are stored.
    /// This item serves to provide the information that is required
    /// for checking subscription renewals, in particular the "original
    /// transaction id" and the receipt.
    ///
    /// The key is the combination of the purchasing player's id combined
    /// with the original transaction id. Note that multiple player accounts
    /// can have subscriptions with the same original transaction id,
    /// because the same subscription can be restored on multiple player
    /// accounts.
    ///
    /// This has an index on the original transaction id. This is used
    /// for disabling a subscription instance on all except the player
    /// who last purchased or restored it.
    ///
    /// Additionally, this has an index on the purchasing player's id.
    /// This index is not really employed at the moment, but they seem
    /// like they might be helpful in possible customer support or debugging.
    /// </summary>
    [Table("InAppPurchaseSubscriptions")]
    [Index(nameof(PlayerId))]
    [Index(nameof(OriginalTransactionId))]
    public class PersistedInAppPurchaseSubscription : IPersistedItem
    {
        [Key]
        [Required]
        [MaxLength(InAppPurchaseUtil.TransactionIdMaxLengthCodePoints + 1 + 17)] // \note + 1 + 17 to account for suffix Player:xxxxxxxxxx/
        [Column(TypeName = "varchar(530)")]
        public string   PlayerAndOriginalTransactionId { get; set; }

        [PartitionKey]
        [Required]
        [Column(TypeName = "varchar(64)")]
        public string   PlayerId                { get; set; }

        /// <summary>
        /// The "original transaction id" of the subscription, as reported by the store platform.
        /// For Apple this is original_transaction_id. For Google this is purchaseToken.
        /// This is essentially the id of the subscription instance.
        /// </summary>
        /// <remarks>
        /// Note that this is not necessarily the transaction id of the transaction
        /// that was validated by the server. The transaction validated by the server
        /// might have been a renewal or restoration.
        /// </remarks>
        [Required]
        [MaxLength(InAppPurchaseUtil.TransactionIdMaxLengthCodePoints)]
        [Column(TypeName = "varchar(512)")]
        public string   OriginalTransactionId   { get; set; }

        /// <summary>
        /// Tagged-serialized InAppPurchaseSubscriptionPersistedInfo.
        /// </summary>
        [Required]
        public byte[]   SubscriptionInfo        { get; set; }

        [Required]
        [Column(TypeName = "DateTime")]
        public DateTime CreatedAt               { get; set; }

        public EntityId PlayerEntityId => EntityId.ParseFromString(PlayerId);

        public static string CreatePrimaryKey(EntityId playerId, string originalTransactionId) => playerId.ToString() + "/" + originalTransactionId;

        PersistedInAppPurchaseSubscription() { }
        public PersistedInAppPurchaseSubscription(EntityId playerId, string originalTransactionId, MetaSerialized<InAppPurchaseSubscriptionPersistedInfo> subscriptionInfo, DateTime createdAt)
        {
            PlayerAndOriginalTransactionId = CreatePrimaryKey(playerId, originalTransactionId);
            PlayerId = playerId.ToString();
            OriginalTransactionId = originalTransactionId;
            SubscriptionInfo = subscriptionInfo.Bytes;
            CreatedAt = createdAt;
        }
    }

    /// <summary>
    /// Info about an IAP subscription, needed when checking subscription renewal.
    /// </summary>
    [MetaSerializable]
    public class InAppPurchaseSubscriptionPersistedInfo
    {
        [MetaMember(1)] public InAppPurchasePlatform    Platform                            { get; private set; }
        /// <summary>
        /// The id with which the corresponding <see cref="PersistedInAppPurchase"/> is persisted.
        /// </summary>
        /// <remarks>
        /// Not used for anything at the moment.
        /// Since <see cref="PersistedInAppPurchaseSubscription"/> and <see cref="PersistedInAppPurchase"/>
        /// are persisted separately, there's a possible edge case where this refers to a nonexistent item.
        /// </remarks>
        [MetaMember(2)] public string                   PersistedValidatedTransactionId     { get; private set; }
        /// <summary>
        /// The <see cref="InAppPurchaseEvent.PurchaseTime"/> of the corresponding purchase.
        /// </summary>
        [MetaMember(3)] public MetaTime                 ValidatedTransactionPurchaseTime    { get; private set; }
        [MetaMember(4)] public InAppProductId           ProductId                           { get; private set; }
        [MetaMember(5)] public string                   PlatformProductId                   { get; private set; }
        /// <inheritdoc cref="PersistedInAppPurchaseSubscription.OriginalTransactionId"/>
        [MetaMember(6)] public string                   OriginalTransactionId               { get; private set; }
        /// <summary>
        /// The validated receipt from the corresponding purchase.
        /// </summary>
        [MetaMember(7)] public string                   Receipt                             { get; private set; }

        InAppPurchaseSubscriptionPersistedInfo() { }
        public InAppPurchaseSubscriptionPersistedInfo(InAppPurchasePlatform platform, string persistedValidatedTransactionId, MetaTime validatedTransactionPurchaseTime, InAppProductId productId, string platformProductId, string originalTransactionId, string receipt)
        {
            Platform = platform;
            PersistedValidatedTransactionId = persistedValidatedTransactionId;
            ValidatedTransactionPurchaseTime = validatedTransactionPurchaseTime;
            ProductId = productId;
            PlatformProductId = platformProductId;
            OriginalTransactionId = originalTransactionId;
            Receipt = receipt;
        }
    }
}
