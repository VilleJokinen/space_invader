// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Client;
using Metaplay.Core.Message;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Serialization;
using System;

namespace Metaplay.Core.Web3
{
    public class NftClient : IMetaplaySubClient
    {
        public ClientSlot ClientSlot => ClientSlotCore.Nft;

        IMetaplaySubClientServices _clientServices;
        LogChannel _log;

        void IMetaplaySubClient.Initialize(IMetaplaySubClientServices clientServices)
        {
            _clientServices = clientServices;
            _log = clientServices.CreateLogChannel("nft");
        }

        /// <summary>
        /// Initiate a player-NFT transaction. <see cref="PlayerNftTransaction.Execute"/>
        /// is executed synchronously, and then cancellation and finalization is executed
        /// eventually, when the backend has completed the transaction.
        /// </summary>
        public void ExecuteNftTransaction(PlayerNftTransaction transaction)
        {
            IPlayerClientContext    playerContext   = _clientServices.ClientStore.GetPlayerClientContext();
            IPlayerModelBase        playerModel     = playerContext.Journal.StagedModel;

            playerContext.FlushActions();

            // \note This is similar to the initial parts of what the server does in its handler of PlayerNftTransactionRequest

            // Clone owned NFTs for transaction
            OrderedDictionary<NftKey, MetaNft> nfts = transaction.TargetNfts.ToOrderedDictionary(key => key, key => playerModel.Nft.OwnedNfts[key]);
            nfts = MetaSerialization.CloneTagged(nfts, MetaSerializationFlags.IncludeAll, playerModel.LogicVersion, playerModel.GetDataResolver());

            // Initiate

            PlayerExecuteNftTransaction executeTransaction = new PlayerExecuteNftTransaction(transaction, nfts);
            MetaActionResult executeResult = playerContext.DryExecuteAction(executeTransaction);
            if (executeResult == MetaActionResult.Success)
            {
                playerContext.ExecuteAction(executeTransaction);
                playerContext.MarkPendingActionsAsFlushed();
            }
            else
                _log.Warning("Dry-run of transaction {TransactionType} failed: {ExecuteResult}. Sending to server anyway for testing and consistency purposes.", transaction.GetType().Name, executeResult);

            // Send to server. Server will coordinate the rest of the transaction.

            _clientServices.MessageDispatcher.SendMessage(new PlayerNftTransactionRequest(transaction));
        }

        void IMetaplaySubClient.OnSessionStart(SessionProtocol.SessionStartSuccess successMessage, ClientSessionStartResources sessionStartResources) {}
        void IMetaplaySubClient.OnSessionStop() { }
        void IMetaplaySubClient.OnDisconnected() { }
        void IMetaplaySubClient.EarlyUpdate() { }
        void IMetaplaySubClient.UpdateLogic(MetaTime time) { }
        void IMetaplaySubClient.FlushPendingMessages() { }

        void IDisposable.Dispose() { }
    }
}
