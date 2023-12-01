// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;

namespace Metaplay.Core.Model
{
    /// <summary>
    /// Pseudo-enum for representing results from <see cref="PlayerActionCore.Execute(IPlayerModel, bool)"/>.
    /// </summary>
    public class MetaActionResult
    {
        // Success result
        public static readonly MetaActionResult Success                                 = new MetaActionResult(nameof(Success));

        // System error results
        public static readonly MetaActionResult UnknownError                            = new MetaActionResult(nameof(UnknownError));
        public static readonly MetaActionResult InvalidActionType                       = new MetaActionResult(nameof(InvalidActionType));
        public static readonly MetaActionResult InvalidLanguage                         = new MetaActionResult(nameof(InvalidLanguage));
        public static readonly MetaActionResult InvalidMailId                           = new MetaActionResult(nameof(InvalidMailId));
        public static readonly MetaActionResult InvalidMail                             = new MetaActionResult(nameof(InvalidMail));
        public static readonly MetaActionResult MailAlreadyConsumed                     = new MetaActionResult(nameof(MailAlreadyConsumed));
        public static readonly MetaActionResult MailNotConsumed                         = new MetaActionResult(nameof(MailNotConsumed));
        public static readonly MetaActionResult DuplicateMailId                         = new MetaActionResult(nameof(DuplicateMailId));
        public static readonly MetaActionResult InvalidInAppTransactionId               = new MetaActionResult(nameof(InvalidInAppTransactionId));
        public static readonly MetaActionResult InvalidInAppTransactionState            = new MetaActionResult(nameof(InvalidInAppTransactionState));
        public static readonly MetaActionResult InvalidInAppPurchaseEvent               = new MetaActionResult(nameof(InvalidInAppPurchaseEvent));
        public static readonly MetaActionResult InvalidInAppProductId                   = new MetaActionResult(nameof(InvalidInAppProductId));
        public static readonly MetaActionResult TooManyPendingInAppPurchases            = new MetaActionResult(nameof(TooManyPendingInAppPurchases));
        public static readonly MetaActionResult InvalidInAppPlatformProductId           = new MetaActionResult(nameof(InvalidInAppPlatformProductId));
        public static readonly MetaActionResult InvalidInAppPlatform                    = new MetaActionResult(nameof(InvalidInAppPlatform));
        public static readonly MetaActionResult InvalidDynamicInAppPurchaseStatus       = new MetaActionResult(nameof(InvalidDynamicInAppPurchaseStatus));
        public static readonly MetaActionResult InvalidNonDynamicInAppPurchaseStatus    = new MetaActionResult(nameof(InvalidNonDynamicInAppPurchaseStatus));
        public static readonly MetaActionResult CannotSetPendingDynamicPurchase         = new MetaActionResult(nameof(CannotSetPendingDynamicPurchase));
        public static readonly MetaActionResult NoSuchSubscription                      = new MetaActionResult(nameof(NoSuchSubscription));
        public static readonly MetaActionResult ExistingSubscriptionStateIsNewer        = new MetaActionResult(nameof(ExistingSubscriptionStateIsNewer));
        public static readonly MetaActionResult InvalidFirebaseMessagingToken           = new MetaActionResult(nameof(InvalidFirebaseMessagingToken));
        public static readonly MetaActionResult NoSessionDeviceId                       = new MetaActionResult(nameof(NoSessionDeviceId));
        public static readonly MetaActionResult MetaOfferGroupHasNoState                = new MetaActionResult(nameof(MetaOfferGroupHasNoState));
        public static readonly MetaActionResult MetaOfferNotInGroup                     = new MetaActionResult(nameof(MetaOfferNotInGroup));
        public static readonly MetaActionResult MetaOfferNotPurchasable                 = new MetaActionResult(nameof(MetaOfferNotPurchasable));
        public static readonly MetaActionResult MetaOfferDoesNotHaveInAppProduct        = new MetaActionResult(nameof(MetaOfferDoesNotHaveInAppProduct));
        #if !METAPLAY_DISABLE_GUILDS
        public static readonly MetaActionResult NoSuchGuildMember                       = new MetaActionResult(nameof(NoSuchGuildMember));
        public static readonly MetaActionResult GuildOperationNotPermitted              = new MetaActionResult(nameof(GuildOperationNotPermitted));
        public static readonly MetaActionResult GuildOperationStale                     = new MetaActionResult(nameof(GuildOperationStale));
        #endif
        public static readonly MetaActionResult AlreadyHasNft                           = new MetaActionResult(nameof(AlreadyHasNft));
        public static readonly MetaActionResult HasNoSuchNft                            = new MetaActionResult(nameof(HasNoSuchNft));
        public static readonly MetaActionResult NftTransactionAlreadyPending            = new MetaActionResult(nameof(NftTransactionAlreadyPending));
        public static readonly MetaActionResult NoSuchDivisionParticipant               = new MetaActionResult(nameof(NoSuchDivisionParticipant));
        public static readonly MetaActionResult NoSuchDivision                          = new MetaActionResult(nameof(NoSuchDivision));
        public static readonly MetaActionResult InvalidDivisionState                    = new MetaActionResult(nameof(InvalidDivisionState));
        public static readonly MetaActionResult RewardAlreadyClaimed                    = new MetaActionResult(nameof(RewardAlreadyClaimed));
        public static readonly MetaActionResult DuplicateDivisionHistoryEntry           = new MetaActionResult(nameof(DuplicateDivisionHistoryEntry));
        public static readonly MetaActionResult InvalidDivisionHistoryEntry             = new MetaActionResult(nameof(InvalidDivisionHistoryEntry));

        public string Name { get; private set; }

        public MetaActionResult(string name)
        {
            Name = name;
        }

        public override string ToString() => Name;

        /// <summary>
        /// Using reference equality comparison here, as we're only interested in checking whether a
        /// given result is <see cref="Success"/> or not.
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public override bool Equals(object obj) => ReferenceEquals(this, obj);

        public override int GetHashCode() => Name.GetHashCode();
    }

    /// <summary>
    /// Action result for unhandled exceptions during Model.Execute().
    /// </summary>
    //public class MetaActionResultUnhandledException : MetaActionResult
    //{
    //    public Exception Exception { get; private set; }
    //
    //    public MetaActionResultUnhandledException(Exception exception) : base(nameof(MetaActionResultUnhandledException))
    //    {
    //        Exception = exception;
    //    }
    //
    //    public override string ToString() => $"MetaActionResultUnhandledException: {Exception}";
    //}
}
