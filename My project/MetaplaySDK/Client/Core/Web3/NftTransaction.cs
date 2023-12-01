// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Serialization;
using Metaplay.Core.TypeCodes;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Metaplay.Core.Web3
{
    /// <summary>
    /// Represents a transaction involving the player model and a set of NFTs
    /// owned by the player.
    /// <para>
    /// Since <see cref="PlayerNftSubModel.OwnedNfts"/> is not the authoritative
    /// state of the owned NFTs (but is only a replica), player-nft transactions
    /// are not as simple as just updating the player model.
    /// </para>
    /// <para>
    /// Transactions are thus executed as a multi-step operation which can still
    /// fail after <see cref="Execute"/> has been run.
    /// <para>
    /// If the transaction fails after <see cref="Execute"/> has been run,
    /// (e.g. because the player just lost ownership of an NFT updated by the transaction),
    /// then <see cref="CancelPlayer"/> will be run eventually, and it should cancel
    /// any changes done to the player in <see cref="Execute"/>.
    /// </para>
    /// <para>
    /// If the transaction succeeds, then <see cref="FinalizePlayer"/> will be run
    /// eventually after <see cref="Execute"/>. Player modifications that cannot be
    /// safely undone should go to <see cref="FinalizePlayer"/> instead of <see cref="Execute"/>.
    /// In particular, taking resources away from the player model should be done in
    /// <see cref="Execute"/> (and undone in <see cref="CancelPlayer"/>), wheres giving
    /// resources to the player should be done in <see cref="FinalizePlayer"/>.
    /// This is because giving resources to the player cannot usually be undone safely,
    /// if there is a possibility that the player could spend those resources until the
    /// cancellation gets run.
    /// </para>
    /// <para>
    /// If <see cref="Execute"/> returns other than <see cref="MetaActionResult.Success"/>,
    /// that is an early failure and neither <see cref="CancelPlayer"/> nor <see cref="FinalizePlayer"/>
    /// will get run. In such a case, <see cref="Execute"/> should not perform any modifications
    /// to either the player model or the NFTs.
    /// </para>
    /// </summary>
    [MetaSerializable]
    [MetaReservedMembers(100, 200)]
    public abstract class PlayerNftTransaction
    {
        /// <summary>
        /// Declares the NFTs to be updated by this transaction.
        /// This determines which NFTs get passed to <see cref="Execute"/>.
        /// </summary>
        public abstract IEnumerable<NftKey> TargetNfts { get; }

        /// <summary>
        /// If you need to compute some data in <see cref="Execute"/> and pass that into
        /// <see cref="CancelPlayer"/> and <see cref="FinalizePlayer"/>, define that data
        /// as a subclass of this, and assign that to the <c>context</c> parameter in
        /// <see cref="Execute"/>.
        /// </summary>
        [MetaSerializable]
        public abstract class ContextBase { }

        /// <summary>
        /// Perform the initial modifications to <paramref name="player"/> and <paramref name="nfts"/>,
        /// except if <paramref name="commit"/> is false in which case the method should only check
        /// any preconditions but should not do modifications (similar to the <c>commit</c> boolean in
        /// PlayerActions).
        /// If precondition checks fail, no modifications should be done and a result other than
        /// <see cref="MetaActionResult.Success"/> should be returned.
        /// </summary>
        /// <remarks>
        /// <paramref name="nfts"/> only contains the NFTs specified by <see cref="TargetNfts"/>.
        /// NFT modifications need to be done via the <paramref name="nfts"/> parameter instead of
        /// modifying <paramref name="player"/>.<see cref="IPlayerModelBase.Nft"/>.<see cref="PlayerNftSubModel.OwnedNfts"/>.
        /// Direct modification of OwnedNfts will not take effect!
        /// </remarks>
        /// <param name="context">
        /// See <see cref="ContextBase"/> for explanation.
        /// </param>
        public abstract MetaActionResult Execute(IPlayerModelBase player, OrderedDictionary<NftKey, MetaNft> nfts, bool commit, ref ContextBase context);
        /// <summary>
        /// Cancel any modifications done to the player in <see cref="Execute"/>,
        /// e.g. refund in-game resources which were taken as payment for the operation.
        /// Called when the transaction fails after <see cref="Execute"/> had completed successfully.
        /// </summary>
        /// <param name="context">
        /// See <see cref="ContextBase"/> for explanation.
        /// </param>
        public abstract void CancelPlayer(IPlayerModelBase player, ContextBase context);
        /// <summary>
        /// Finalize the transaction, e.g. give in-game resources as reward for the operation.
        /// Called when the transaction has been successfully completed.
        /// </summary>
        /// <param name="context">
        /// See <see cref="ContextBase"/> for explanation.
        /// </param>
        public abstract void FinalizePlayer(IPlayerModelBase player, ContextBase context);
    }

    /// <summary>
    /// Part of the SDK's NFT transaction execution mechanism.
    /// Client requests to run the specified transaction.
    /// The client shall have already run the <see cref="PlayerNftTransaction.Execute"/> on its
    /// own copy of the player model, and the server will do the same on its copy.
    /// If <see cref="PlayerNftTransaction.Execute"/> runs successfully (i.e. with <see cref="MetaActionResult.Success"/>),
    /// then finalization or cancellation will be eventually delivered using a synchronized server action
    /// (see <see cref="PlayerFinalizeNftTransaction"/> and <see cref="PlayerCancelNftTransaction"/>).
    /// </summary>
    [MetaMessage(MessageCodesCore.PlayerNftTransactionRequest, MessageDirection.ClientToServer), MessageRoutingRuleOwnedPlayer]
    public class PlayerNftTransactionRequest : MetaMessage
    {
        public PlayerNftTransaction Transaction;

        PlayerNftTransactionRequest() { }
        public PlayerNftTransactionRequest(PlayerNftTransaction transaction)
        {
            Transaction = transaction;
        }
    }

    /// <summary>
    /// Part of the SDK's NFT transaction execution mechanism.
    /// Runs the <see cref="PlayerNftTransaction.Execute"/> and does related bookeeping.
    /// </summary>
    [ModelAction(ActionCodesCore.PlayerExecuteNftTransaction)]
    // \todo #nft Action execution flag hack: this is not really an unsynchronized server action.
    //       Should instead come up with some ModelActionExecuteFlags.AdHoc which is not permitted
    //       by any of the normal action execution mechanisms.
    //       Explanation:
    //       We're only using an arbitrary non-client-permitting execution flag here because we
    //       do not want to allow the client to invoke transaction execution actions "on their own",
    //       i.e. without being tied to a transaction. The transaction execution action is run
    //       in an ad-hoc manner on both the client and server, involving the
    //       PlayerNftTransactionRequest message.
    public class PlayerExecuteNftTransaction : PlayerUnsynchronizedServerActionCore<IPlayerModelBase>
    {
        public PlayerNftTransaction Transaction;
        public OrderedDictionary<NftKey, MetaNft> Nfts;

        PlayerExecuteNftTransaction() { }
        public PlayerExecuteNftTransaction(PlayerNftTransaction transaction, OrderedDictionary<NftKey, MetaNft> nfts)
        {
            Transaction = transaction;
            Nfts = nfts;
        }

        public override MetaActionResult Execute(IPlayerModelBase player, bool commit)
        {
            // Don't permit multiple pending transactions at the same time.
            // This could be relaxed, but simplifies the implementation for now.
            if (player.Nft.TransactionState != null)
                return MetaActionResult.NftTransactionAlreadyPending;

            if (commit)
            {
                player.Log.Debug("Executing the initiation of NFT transaction {TransactionType}", Transaction.GetType());

                // Transaction state is kept in the player model but is not intended to be accessed directly
                // by user code. SDK passes the transaction state (NFTs and user context) explicitly to the
                // appropriate methods of the transaction.

                // Initialize transaction state.
                player.Nft.TransactionState = new PlayerNftTransactionState();
                // Clone the NFTs into the transaction state. Clone, because `Nfts` is a member of an action and actions shouldn't be mutated.
                player.Nft.TransactionState.Nfts = MetaSerialization.CloneTagged(Nfts, MetaSerializationFlags.IncludeAll, player.LogicVersion, player.GetDataResolver());
                // Execute.
                PlayerNftTransaction.ContextBase userContext = null;
                MetaActionResult result = Transaction.Execute(player, player.Nft.TransactionState.Nfts, commit: true, ref userContext);
                // Store user context (if any) into transaction state, will be later on passed to cancellation or finalization.
                player.Nft.TransactionState.UserContext = userContext;

                // Check the execution did not change parts of the MetaNft that it should not change,
                // and update NFT's bookkeeping state (UpdateCounter).
                foreach ((NftKey key, MetaNft nft) in Nfts)
                {
                    MetaNft updatedNft = player.Nft.TransactionState.Nfts[key];
                    if (updatedNft.OwnerEntity != nft.OwnerEntity)
                        throw new InvalidOperationException($"NFT transaction {Transaction.GetType()} modified the owner entity of {key} from {nft.OwnerEntity} to {updatedNft.OwnerEntity}. Player-NFT transactions are not permitted to change an NFT's owner entity.");
                    if (updatedNft.OwnerAddress != nft.OwnerAddress)
                        throw new InvalidOperationException($"NFT transaction {Transaction.GetType()} modified the owner address of {key} from {nft.OwnerAddress} to {updatedNft.OwnerAddress}. Player-NFT transactions are not permitted to change an NFT's owner address.");
                    if (updatedNft.IsMinted != nft.IsMinted)
                        throw new InvalidOperationException($"NFT transaction {Transaction.GetType()} modified the {nameof(nft.IsMinted)} flag of {key} from {nft.IsMinted} to {updatedNft.IsMinted}. Player-NFT transactions are not permitted to change an NFT's mintedness status.");
                    if (updatedNft.UpdateCounter != nft.UpdateCounter)
                        throw new InvalidOperationException($"NFT transaction {Transaction.GetType()} modified the {nameof(nft.UpdateCounter)} of {key} from {nft.UpdateCounter} to {updatedNft.UpdateCounter}. {nameof(nft.UpdateCounter)} is updated automatically by the SDK and shouldn't be modified by user code.");

                    updatedNft.UpdateCounter++;
                }

                return result;
            }
            else
            {
                PlayerNftTransaction.ContextBase context = null;
                return Transaction.Execute(player, Nfts, commit: false, ref context);
            }
        }
    }

    /// <summary>
    /// Part of the SDK's NFT transaction execution mechanism.
    /// Run the finalization of a successful transaction.
    /// In addition to running the user-defined <see cref="PlayerNftTransaction.FinalizePlayer"/>,
    /// this also updates the OwnedNfts replica in the player model
    /// to reflect the updated NFTs.
    /// </summary>
    [ModelAction(ActionCodesCore.PlayerFinalizeNftTransaction)]
    public class PlayerFinalizeNftTransaction : PlayerSynchronizedServerActionCore<IPlayerModelBase>
    {
        public PlayerNftTransaction Transaction;

        PlayerFinalizeNftTransaction() { }
        public PlayerFinalizeNftTransaction(PlayerNftTransaction transaction)
        {
            Transaction = transaction;
        }

        public override MetaActionResult Execute(IPlayerModelBase player, bool commit)
        {
            if (commit)
            {
                player.Log.Debug("Finalizing NFT transaction {TransactionType}", Transaction.GetType());
                foreach ((NftKey key, MetaNft nft) in player.Nft.TransactionState.Nfts)
                    player.Nft.OwnedNfts[key] = nft;
                Transaction.FinalizePlayer(player, player.Nft.TransactionState.UserContext);
                player.Nft.TransactionState = null;
            }

            return MetaActionResult.Success;
        }
    }

    /// <summary>
    /// Part of the SDK's NFT transaction execution mechanism.
    /// Run the cancellation of a successful transaction.
    /// </summary>
    [ModelAction(ActionCodesCore.PlayerCancelNftTransaction)]
    public class PlayerCancelNftTransaction : PlayerSynchronizedServerActionCore<IPlayerModelBase>
    {
        public PlayerNftTransaction Transaction;

        PlayerCancelNftTransaction() { }
        public PlayerCancelNftTransaction(PlayerNftTransaction transaction)
        {
            Transaction = transaction;
        }

        public override MetaActionResult Execute(IPlayerModelBase player, bool commit)
        {
            if (commit)
            {
                player.Log.Debug("Cancelling NFT transaction {TransactionType}", Transaction.GetType());
                Transaction.CancelPlayer(player, player.Nft.TransactionState.UserContext);
                player.Nft.TransactionState = null;
            }

            return MetaActionResult.Success;
        }
    }
}
