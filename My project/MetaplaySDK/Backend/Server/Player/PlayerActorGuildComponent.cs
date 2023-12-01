// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if !METAPLAY_DISABLE_GUILDS

using Akka.Actor;
using Metaplay.Cloud;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.Entity.Synchronize;
using Metaplay.Core;
using Metaplay.Core.Guild;
using Metaplay.Core.Guild.Messages.Core;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Serialization;
using Metaplay.Server.Database;
using Metaplay.Server.Guild;
using Metaplay.Server.Guild.InternalMessages;
using Metaplay.Server.GuildDiscovery;
using Metaplay.Server.MultiplayerEntity.InternalMessages;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Metaplay.Server
{
    public abstract partial class PlayerActorBase<TModel, TPersisted>
    {
        protected abstract class GuildComponentBase : EntityGuildComponent
        {
            internal class TriggerGuildCreation { public static TriggerGuildCreation Instance = new TriggerGuildCreation(); }
            internal class RefreshGuildMemberPlayerData { public static RefreshGuildMemberPlayerData Instance = new RefreshGuildMemberPlayerData(); }
            internal class SyncPersistedGuildOpsToGuild { public static SyncPersistedGuildOpsToGuild Instance = new SyncPersistedGuildOpsToGuild(); }

            protected abstract PlayerActorBase<TModel, TPersisted> PlayerBase { get; }
            protected override EntityActor OwnerActor => PlayerBase;
            protected IPlayerGuildState GuildState => PlayerBase.Model.GuildState;
            protected IActorRef _self => PlayerBase._self;
            protected IMetaLogger _log => PlayerBase._log;

            bool _pendingGuildMemberPlayerDataUpdate = false;
            public int GuildIncarnation = 1;
            int _persistedGuildOpEpoch = 0;
            bool _pendingSyncPersistedGuildOpsToGuild = false;
            int _persistedPlayerOpEpoch = 0;
            bool _shouldSendGuildPlayerOpEpochUpdateAfterPersist = false;
            bool _triggerGuildCreationEnqueued = false;

            /// <summary>
            /// Fetches the player-owned subset of the guild member data. See <see cref="GuildMemberPlayerDataBase"/>.
            /// </summary>
            protected abstract GuildMemberPlayerDataBase CreateGuildMemberPlayerData();

            /// <summary>
            /// Fetches the context which is used in guild discovery. See <see cref="GuildDiscoveryPlayerContext"/>
            /// for more details.
            /// </summary>
            protected abstract GuildDiscoveryPlayerContextBase CreateGuildDiscoveryContext();

            /// <summary>
            /// Converts the user-supplied guild creation request params into a validated GuildCreationParamsBase.
            /// This allows Player to augment the request with [ServerOnly] or other implicit data, or even reject
            /// the request early: If this method returns <c>null</c>, the guild creation request is refused.
            /// Note that the generated request will be validated in <see cref="GuildCreationRequestParamsBase"/>.
            /// </summary>
            protected abstract GuildCreationParamsBase TryCreateGuildCreationParamsFromRequest(GuildCreationRequestParamsBase requestParams);

            /// <summary>
            /// Creates the player avatar, i.e. the visible information of a player that is shown in guild invites.
            /// </summary>
            protected abstract GuildInviterAvatarBase CreateGuildInviterAvatar();

            public void PostLoad()
            {
                // If we had a pending Guild Creation, trigger it now
                if (GuildState.PendingGuildCreation != null)
                {
                    EnqueueTriggerGuildCreation();
                }

                _persistedGuildOpEpoch = GuildState.LastPendingGuildOpEpoch;
                _persistedPlayerOpEpoch = GuildState.LastPlayerOpEpoch;

                // If we had pending actions to guild, flush them now
                // \todo: might as well check that we still are in the guild. Or enqueue that check?
                if (GuildState.PendingGuildOps?.Count > 0)
                {
                    EnqueueSyncPersistedGuildOpsToGuild();
                }

                // \todo: should we ask/notify guild of our Epoch so it can send lost ops? Handled in session init anyway.
            }

            public void PersistState()
            {
                // If pending operations with guild, schedule update now that they are persisted
                _persistedGuildOpEpoch = GuildState.LastPendingGuildOpEpoch;
                if (GuildState.PendingGuildOps?.Count > 0)
                    EnqueueSyncPersistedGuildOpsToGuild();

                // Update guild epochs and inform guild

                _persistedPlayerOpEpoch = GuildState.LastPlayerOpEpoch;
                if (_shouldSendGuildPlayerOpEpochUpdateAfterPersist)
                {
                    _shouldSendGuildPlayerOpEpochUpdateAfterPersist = false;
                    if (GuildState.GuildId != EntityId.None)
                        PlayerBase.CastMessage(GuildState.GuildId, new InternalGuildPlayerOpsCommitted(PlayerBase._entityId, GuildState.GuildMemberInstanceId, GuildState.LastPlayerOpEpoch));
                }
            }

            public AssociatedEntityRefBase GetSessionStartAssociatedGuildEntity()
            {
                if (GuildState.GuildId == EntityId.None)
                    return null;
                EntityId guildId = EntityId.None;
                int memberInstanceId = 0;
                int lastGuildPlayerOpEpoch = 0;
                int committedGuildPlayerOpEpoch = 0;
                int committedGuildGuildOpEpoch = 0;
                OrderedDictionary<int, GuildMemberGuildOpLogEntry> committedPendingGuildOps = null;

                // Player thinks it is in a guild. Session verifies this, and informs us if we are wrong
                // with InternalPlayerSessionClearGuildStateRequest.
                guildId = GuildState.GuildId;
                memberInstanceId = GuildState.GuildMemberInstanceId;
                lastGuildPlayerOpEpoch = GuildState.LastPlayerOpEpoch;
                committedGuildPlayerOpEpoch = _persistedPlayerOpEpoch;
                committedGuildGuildOpEpoch = _persistedGuildOpEpoch;
                committedPendingGuildOps = TryGetCommittedPendingGuildOps();

                return new InternalOwnedGuildAssociationRef(
                    playerId: PlayerBase._entityId,
                    guildId: guildId,
                    memberInstanceId: memberInstanceId,
                    lastPlayerOpEpoch: lastGuildPlayerOpEpoch,
                    committedPlayerOpEpoch: committedGuildPlayerOpEpoch,
                    committedGuildOpEpoch: committedGuildGuildOpEpoch,
                    committedPendingGuildOps: committedPendingGuildOps,
                    playerLoginData: CreateGuildMemberPlayerData());
            }


            public bool HasPendingOperations => _triggerGuildCreationEnqueued;

            void ClearCurrentGuildState()
            {
                // \note: Creation is not cleared
                GuildState.GuildId = EntityId.None;
                GuildState.LastPendingGuildOpEpoch = 0;
                GuildState.PendingGuildOps = null;
                GuildState.LastPlayerOpEpoch = 0;
                GuildState.GuildMemberInstanceId = 0;
                _pendingSyncPersistedGuildOpsToGuild = false;
                _shouldSendGuildPlayerOpEpochUpdateAfterPersist = false;
            }

            [MessageHandler]
            public async Task HandleGuildCreateRequest(GuildCreateRequest request)
            {
                GuildCreationParamsBase creationParams = null;

                if (GuildState.PendingGuildCreation != null)
                    _log.Warning("Player attempted to create a new guild while previous creation was in progress");
                else if (GuildState.GuildId != EntityId.None)
                    _log.Warning("Player attempted to create a new guild while it was a member of the guild {GuildId}", GuildState.GuildId);
                else
                {
                    // state is ok. Try to create params from request (may be rejected)
                    creationParams = TryCreateGuildCreationParamsFromRequest(request.CreationParams);
                    if (creationParams == null)
                        _log.Debug("Guild creation request was refused.");
                }

                // error path
                if (creationParams == null)
                {
                    // Bump incarnation to invalidate any in-flight requests. This shoud be no-op as there
                    // should not be any requests while create is in progress, but let's do it just be sure.
                    GuildIncarnation++;
                    PlayerBase.PublishMessage(EntityTopic.Owner, new InternalSessionPlayerGuildCreateFailed(GuildIncarnation));
                    return;
                }

                GuildState.PendingGuildCreation = creationParams;
                await PlayerBase.PersistStateIntermediate();

                // Guild creation can have long running validations, for example if we need to validate
                // names or descriptions with a third-party service. Enqueue it.
                // This enqueue-pattern also to support guild-creation-via-action path.
                EnqueueTriggerGuildCreation();
            }

            void EnqueueTriggerGuildCreation()
            {
                if (_triggerGuildCreationEnqueued)
                    return;
                _triggerGuildCreationEnqueued = true;
                _self.Tell(TriggerGuildCreation.Instance, _self);
            }

            [CommandHandler]
            async Task HandleTriggerGuildCreation(TriggerGuildCreation _)
            {
                _triggerGuildCreationEnqueued = false;

                if (GuildState.PendingGuildCreation == null)
                    return;

                if (!IntegrationRegistry.Get<GuildRequirementsValidator>().ValidateGuildCreation(GuildState.PendingGuildCreation))
                {
                    _log.Warning("Guild creation rejected, did not pass validation. CreateParams = {CreateParams}", PrettyPrint.Compact(GuildState.PendingGuildCreation));

                    GuildState.PendingGuildCreation = null;
                    await PlayerBase.PersistStateIntermediate();

                    // Bump incarnation to invalidate any in-flight requests. This shoud be no-op as there
                    // should not be any requests while create is in progress, but let's do it just be sure.
                    GuildIncarnation++;

                    PlayerBase.PublishMessage(EntityTopic.Owner, new InternalSessionPlayerGuildCreateFailed(GuildIncarnation));
                    return;
                }

                await CompletePendingGuildCreation();
            }

            async Task CompletePendingGuildCreation()
            {
                GuildMemberPlayerDataBase playerData = CreateGuildMemberPlayerData();

                // Create and setup a guild
                EntityId guildId = await DatabaseEntityUtil.CreateNewGuildAsync(_log);

                using (EntitySynchronize sync = await PlayerBase.EntitySynchronizeAsync(guildId, new InternalGuildSetupSync.Begin(GuildState.PendingGuildCreation)))
                {
                    var setupResponse = await sync.ReceiveAsync<InternalGuildSetupSync.SetupResponse>();
                    if (!setupResponse.IsSuccess)
                    {
                        // already created? Cannot really advance
                        _log.Warning("Guild refused setup from after creation. GuildId={GuildId}.", guildId);
                        throw new InvalidOperationException("Newly created guild refused initial setup");
                    }

                    // Update our records that we think we are a member of it.
                    // Player always commits first so that if guild fails, we reset back to consistent state on re-login.
                    ClearCurrentGuildState();
                    GuildState.GuildId = guildId;
                    GuildState.GuildMemberInstanceId = setupResponse.MemberInstanceId;
                    GuildState.PendingGuildCreation = null;
                    await PlayerBase.PersistStateIntermediate();

                    GuildIncarnation++;

                    // Join the guild.
                    // Note that if we crash here, we subscribe next time as if we already were a member. The Guild
                    // will handle this special case. The intent is that if this actor is restarted, it does not
                    // need to know whether is its supposed to "join" or "subscribe" to the guild.
                    // (or more specifically, the session does not need to know).

                    sync.Send(new InternalGuildSetupSync.PlayerCommitted(PlayerBase._entityId, playerData, GuildIncarnation));
                    await sync.ReceiveAsync<InternalGuildSetupSync.GuildCommitted>();
                }

                var committedPendingGuildOps = TryGetCommittedPendingGuildOps();
                InternalOwnedGuildAssociationRef guildAssociation = new InternalOwnedGuildAssociationRef(
                    playerId: PlayerBase._entityId,
                    guildId: guildId,
                    memberInstanceId: GuildState.GuildMemberInstanceId,
                    lastPlayerOpEpoch: GuildState.LastPlayerOpEpoch,
                    committedPlayerOpEpoch: _persistedPlayerOpEpoch,
                    committedGuildOpEpoch: _persistedGuildOpEpoch,
                    committedPendingGuildOps: committedPendingGuildOps,
                    playerLoginData: playerData);

                PlayerBase.PublishMessage(EntityTopic.Owner, new InternalSessionPlayerJoinedAGuild(guildAssociation, createdTheGuild: true, GuildIncarnation));
            }

            [EntityAskHandler]
            async Task<InternalPlayerGuildLeaveResponse> HandleInternalPlayerGuildLeaveRequest(InternalPlayerGuildLeaveRequest request)
            {
                // might have been kicked earlier
                if (GuildState.GuildId != request.GuildId || GuildIncarnation != request.GuildIncarnation)
                {
                    return InternalPlayerGuildLeaveResponse.CreateStaleRequest();
                }

                // If we have uncommitted guild ops, commit them first. Otherwise they would be lost
                if (_persistedGuildOpEpoch != GuildState.LastPendingGuildOpEpoch)
                    await PlayerBase.PersistStateIntermediate();

                LeaveGuildResults results = await LeaveGuildAsync(forceLeave: false);
                if (results.HasFlag(LeaveGuildResults.WasKickedAlready))
                    return InternalPlayerGuildLeaveResponse.CreateStaleRequest();

                return InternalPlayerGuildLeaveResponse.CreateOk(GuildIncarnation, sessionDesynchronized: results.HasFlag(LeaveGuildResults.SessionDesynchronized));
            }

            [Flags]
            public enum LeaveGuildResults
            {
                None = 0,
                SessionDesynchronized = 0x01,
                WasKickedAlready = 0x02,
            }

            /// <summary>
            /// Leave the guild, if in any. If <paramref name="forceLeave"/> is set, the leave handshake does no attempt to settle any pending guild and player operations. Otherwise,
            /// the leave handshake will complete any pending operations, and then leave the guild.
            /// </summary>
            public async Task<LeaveGuildResults> LeaveGuildAsync(bool forceLeave)
            {
                if (GuildState.GuildId == EntityId.None)
                    return LeaveGuildResults.None;

                // Clear from guild first

                LeaveGuildResults leaveResults = LeaveGuildResults.None;
                int numTimesAttempted = 0;
                for (; ; )
                {
                    InternalGuildLeaveResponse leaveResponse = await PlayerBase.EntityAskAsync<InternalGuildLeaveResponse>(GuildState.GuildId, new InternalGuildLeaveRequest(PlayerBase._entityId, GuildState.GuildMemberInstanceId, _persistedPlayerOpEpoch, GuildState.PendingGuildOps, forceLeave));

                    if (leaveResponse.Result == InternalGuildLeaveResponse.ResultCode.Ok)
                        break;
                    else if (leaveResponse.Result == InternalGuildLeaveResponse.ResultCode.Kicked)
                    {
                        // special case. Session asked us to leave but turns out we were (possibly) kicked.
                        // Try to handle as kicked. If we weren't kicked, handle as leave
                        CompleteKickedFromGuildResults results = await TryCompleteKickedFromGuild();

                        if (results.HasFlag(CompleteKickedFromGuildResults.SessionDesynchronized))
                            leaveResults |= LeaveGuildResults.SessionDesynchronized;

                        if (results.HasFlag(CompleteKickedFromGuildResults.WasKicked))
                        {
                            // handle as kick (i.e. we were kicked first, then received leave request)
                            // \note: no need to handle sessionDesynchronized separately here, TryCompleteKickedFromGuild has reported that alrady
                            leaveResults |= LeaveGuildResults.WasKickedAlready;
                            return leaveResults;
                        }
                        else
                        {
                            // handle as leave
                            break;
                        }
                    }
                    else if (leaveResponse.Result == InternalGuildLeaveResponse.ResultCode.PendingPlayerOps)
                    {
                        // If we must execute and commit pending operations
                        GuildPlayerOpResults commitResults = ExecuteGuildMemberPlayerOps(leaveResponse.PendingPlayerOps);
                        await PlayerBase.PersistStateIntermediate();

                        if (commitResults.HasFlag(GuildPlayerOpResults.SessionDesynchronized))
                            leaveResults |= LeaveGuildResults.SessionDesynchronized;

                        numTimesAttempted++;
                        if (numTimesAttempted > 3)
                        {
                            await PlayerBase.PersistStateIntermediate();
                            throw new Exception("Player-Guild relationship did not settle after 3 iterations during leave.");
                        }
                        continue;
                    }
                    else
                        throw new InvalidOperationException("illegal result");

                    // unreachable
                }

                // clear from player data
                ClearCurrentGuildState();
                await PlayerBase.PersistStateIntermediate();

                GuildIncarnation++;

                return leaveResults;
            }

            [EntityAskHandler]
            async Task<InternalPlayerJoinGuildResponse> HandleInternalPlayerJoinGuildRequest(InternalPlayerJoinGuildRequest request)
            {
                if (GuildIncarnation != request.GuildIncarnation)
                {
                    // Stale request. Check this first as this is somewhat expected
                    return InternalPlayerJoinGuildResponse.CreateRefused();
                }
                else if (GuildState.GuildId != EntityId.None)
                {
                    _log.Warning("Player attempted to join a guild while in another guild");
                    return InternalPlayerJoinGuildResponse.CreateRefused();
                }
                else if (GuildState.PendingGuildCreation != null)
                {
                    _log.Warning("Player attempted to join a guild while a creation was in progress");
                    return InternalPlayerJoinGuildResponse.CreateRefused();
                }

                GuildMemberPlayerDataBase playerData = CreateGuildMemberPlayerData();
                int numTimesAttempted = 0;
                for (; ; )
                {
                    using (EntitySynchronize guildSync = await PlayerBase.EntitySynchronizeAsync(request.OriginalRequest.GuildId, new InternalGuildJoinGuildSync.Begin(PlayerBase._entityId, request.OriginalRequest, playerData)))
                    {
                        var guildPreflight = await guildSync.ReceiveAsync<InternalGuildJoinGuildSync.PreflightDone>();

                        if (guildPreflight.Result == InternalGuildJoinGuildSync.PreflightDone.ResultCode.Reject)
                        {
                            _log.Debug("Guild {GuildId} rejected join attempt", request.OriginalRequest.GuildId);
                            return InternalPlayerJoinGuildResponse.CreateRefused();
                        }
                        else if (guildPreflight.Result == InternalGuildJoinGuildSync.PreflightDone.ResultCode.TryAgain)
                        {
                            numTimesAttempted++;
                            if (numTimesAttempted > 3)
                            {
                                await PlayerBase.PersistStateIntermediate();
                                throw new Exception("Player-Guild relationship did not settle after 3 iterations during kick.");
                            }
                            continue;
                        }
                        else if (guildPreflight.Result != InternalGuildJoinGuildSync.PreflightDone.ResultCode.Ok)
                            throw new InvalidOperationException("invalid result");

                        _log.Debug("Guild {GuildId} accepted join attempt.", request.OriginalRequest.GuildId);

                        // Guild accepts us. Update our records that we think we are a member of it
                        ClearCurrentGuildState();
                        GuildState.GuildId = request.OriginalRequest.GuildId;
                        GuildState.GuildMemberInstanceId = guildPreflight.MemberInstanceId;
                        await PlayerBase.PersistStateIntermediate();

                        GuildIncarnation++;

                        // Tell guild we have committed and wait for it to commit as well
                        guildSync.Send(new InternalGuildJoinGuildSync.PlayerCommitted());
                        await guildSync.ReceiveAsync<InternalGuildJoinGuildSync.GuildCommitted>();

                        OrderedDictionary<int, GuildMemberGuildOpLogEntry> committedPendingGuildOps = TryGetCommittedPendingGuildOps();
                        InternalOwnedGuildAssociationRef guildAssociation = new InternalOwnedGuildAssociationRef(
                            playerId: PlayerBase._entityId,
                            guildId: GuildState.GuildId,
                            memberInstanceId: GuildState.GuildMemberInstanceId,
                            lastPlayerOpEpoch: GuildState.LastPlayerOpEpoch,
                            committedPlayerOpEpoch: _persistedPlayerOpEpoch,
                            committedGuildOpEpoch: _persistedGuildOpEpoch,
                            committedPendingGuildOps: committedPendingGuildOps,
                            playerLoginData: playerData);

                        return InternalPlayerJoinGuildResponse.CreateOk(guildAssociation, GuildIncarnation);
                    }

                    // unreachable
                }
            }

            [EntitySynchronizeHandler]
            async Task HandleInternalGuildTransactionPlayerSyncBegin(EntitySynchronize sync, InternalGuildTransactionPlayerSync.Begin begin)
            {
                bool earlyCancel = false;

                if (begin.ForcePlayerCancel)
                {
                    _log.Debug("Guild transaction force cancelled by session. Cancelling.");
                    earlyCancel = true;
                }
                else if (GuildState.GuildId == EntityId.None)
                {
                    _log.Debug("Guild transaction but player is not in a guild. Cancelling.");
                    earlyCancel = true;
                }
                else if (GuildIncarnation != begin.GuildIncarnation)
                {
                    // incarnation changed, i.e. the session is in a different guild than we we thought. Since the player agrees with
                    // client, the session and client are both wrong and transaction must be cancelled. This should be very rare, so
                    // log a warning
                    _log.Warning("Guild transaction targeted a guild it is not a member of. Requested IncarnationId={RequestIncarnationId}, Current incarnationId={IncarnationId}.", begin.GuildIncarnation, GuildIncarnation);
                    earlyCancel = true;
                }

                IGuildTransaction transaction = begin.Transaction.Deserialize(resolver: null, logicVersion: PlayerBase._logicVersion);
                var consistencyMode = transaction.ConsistencyMode;
                transaction.InvokingPlayerId = PlayerBase._entityId;

                // Plan for player
                ITransactionPlan playerPlan;
                try
                {
                    playerPlan = transaction.PlanForPlayer(PlayerBase.Model);
                }
                catch (TransactionPlanningFailure)
                {
                    // plan wants to abort.
                    // If we are here, it means that planning succeeded on client, i.e. we are not in sync. Must terminate.
                    sync.Send(InternalGuildTransactionPlayerSync.Planned.CreateInternalError($"got TranssactionPlanningFailure while planning for player"));
                    return;
                }

                // Send plan to guild, and wait for response. Guild may optionally inform use that we cannot
                // continue yet as we have preceesing ops uncommitted. We must execute the ops before the
                // initiating action is executed. (Consistent Transaction is appended to ops. If this transaction
                // would complete before preceeding ops, the epoch order is broken).
                //
                // Except if we are doing an EarlyCancel in which case we don't need to inform guild at all

                InternalGuildTransactionPlayerSync.Commit commitCommand;
                if (!earlyCancel)
                {
                    sync.Send(InternalGuildTransactionPlayerSync.Planned.CreateOk(playerPlan, GuildState.GuildMemberInstanceId, GuildState.LastPlayerOpEpoch));
                    commitCommand = await sync.ReceiveAsync<InternalGuildTransactionPlayerSync.Commit>();

                    if (commitCommand.PreceedingPlayerOps != null)
                    {
                        GuildPlayerOpResults combinedResult = ExecuteGuildMemberPlayerOps(commitCommand.PreceedingPlayerOps);

                        // Mark that guild is intereste in epoch update
                        // This happens even if we didn't need to run anything
                        _shouldSendGuildPlayerOpEpochUpdateAfterPersist = true;

                        if (combinedResult.HasFlag(GuildPlayerOpResults.SessionDesynchronized))
                        {
                            // session is lost. Kick owner, but this only applies after this call. This transaction
                            // will be completed.

                            PlayerBase.KickPlayerIfConnected(PlayerForceKickOwnerReason.InternalError);
                        }
                    }
                }
                else
                {
                    commitCommand = null;
                }

                // Execute initiating action. This has been executed by the client already

                PlayerActionBase initiatingAction = transaction.CreateInitiatingPlayerAction(playerPlan);

                if (initiatingAction != null)
                {
                    // \todo: could combine Dry-run and actual running
                    MetaActionResult dryRunResult = ModelUtil.DryRunAction(PlayerBase.Model, initiatingAction);
                    if (dryRunResult != MetaActionResult.Success)
                    {
                        // As with above TranssactionPlanningFailure, if we are here, it means that planning succeeded on client, and we are not in sync. Must terminate.
                        sync.Send(InternalGuildTransactionPlayerSync.Planned.CreateInternalError($"failed to dry-run initiating action: {dryRunResult}"));
                        return;
                    }

                    // \hack: #journalhack
                    PlayerBase._playerJournal.StageAction(initiatingAction, ArraySegment<uint>.Empty);
                    using (var _ = PlayerBase._playerJournal.Commit(PlayerBase._playerJournal.StagedPosition, ModelJournal<IPlayerModelBase>.Follower.ConflictResolutionMode.ContinueEvenIfConflicts))
                    {
                    }
                }

                // Now we have caught up with the past. Handle early cancellation

                if (earlyCancel)
                {
                    PlayerTransactionFinalizingActionBase cancelAction = transaction.CreateCancelingPlayerAction(playerPlan);
                    int trackingId;
                    MetaSerialized<PlayerTransactionFinalizingActionBase> serializedCancelAction;

                    if (cancelAction != null)
                    {
                        trackingId = PlayerBase.RegisterSynchronizedServerAction(cancelAction);
                        serializedCancelAction = MetaSerialization.ToMetaSerialized(cancelAction, MetaSerializationFlags.SendOverNetwork, logicVersion: PlayerBase._logicVersion);
                    }
                    else
                    {
                        trackingId = 0;
                        serializedCancelAction = default;
                    }

                    sync.Send(InternalGuildTransactionPlayerSync.Planned.CreateCancel(serializedCancelAction, trackingId));

                    // Flush pubsub with the pig
                    PlayerBase.PublishMessage(EntityTopic.Owner, new InternalPig(begin.PubsubPiggingId));
                    return;
                }

                // Late cancel?

                if (commitCommand.IsCancel)
                {
                    PlayerTransactionFinalizingActionBase cancelAction = transaction.CreateCancelingPlayerAction(playerPlan);
                    int trackingId;
                    MetaSerialized<PlayerTransactionFinalizingActionBase> serializedCancelAction;

                    if (cancelAction != null)
                    {
                        trackingId = PlayerBase.RegisterSynchronizedServerAction(cancelAction);
                        serializedCancelAction = MetaSerialization.ToMetaSerialized(cancelAction, MetaSerializationFlags.SendOverNetwork, logicVersion: PlayerBase._logicVersion);
                    }
                    else
                    {
                        trackingId = 0;
                        serializedCancelAction = default;
                    }

                    sync.Send(new InternalGuildTransactionPlayerSync.Committed(serializedCancelAction, trackingId));

                    // Flush pubsub with the pig
                    PlayerBase.PublishMessage(EntityTopic.Owner, new InternalPig(begin.PubsubPiggingId));
                    return;
                }

                // Success

                ITransactionPlan finalizingPlan = transaction.PlanForFinalizing(playerPlan, commitCommand.GuildPlan, commitCommand.ServerPlan);
                PlayerTransactionFinalizingActionBase finalizingAction = transaction.CreateFinalizingPlayerAction(finalizingPlan);
                int finializingTrackingId;
                MetaSerialized<PlayerTransactionFinalizingActionBase> serializedfinalizingAction;

                if (finalizingAction != null)
                {
                    finializingTrackingId = PlayerBase.RegisterSynchronizedServerAction(finalizingAction);
                    serializedfinalizingAction = MetaSerialization.ToMetaSerialized(finalizingAction, MetaSerializationFlags.SendOverNetwork, logicVersion: PlayerBase._logicVersion);
                }
                else
                {
                    finializingTrackingId = 0;
                    serializedfinalizingAction = default;
                }

                // Apply implicit op epoch for EC transactions
                if (consistencyMode == GuildTransactionConsistencyMode.EventuallyConsistent)
                    GuildState.LastPlayerOpEpoch++;

                if (GuildState.LastPlayerOpEpoch != commitCommand.ExpectedPlayerOpEpoch)
                    throw new InvalidOperationException($"Oplog epoch mismatch after transaction: Expected {commitCommand.ExpectedPlayerOpEpoch}, got {GuildState.LastPlayerOpEpoch}");

                sync.Send(new InternalGuildTransactionPlayerSync.Committed(serializedfinalizingAction, finializingTrackingId));

                // Flush pubsub with the pig
                PlayerBase.PublishMessage(EntityTopic.Owner, new InternalPig(begin.PubsubPiggingId));
            }

            [EntityAskHandler]
            InternalGuildDiscoveryPlayerContextResponse HandleInternalGuildDiscoveryPlayerContextRequest(InternalGuildDiscoveryPlayerContextRequest _)
            {
                return new InternalGuildDiscoveryPlayerContextResponse(CreateGuildDiscoveryContext());
            }

            public async Task<bool> OnAssociatedEntityRefusalAsync(AssociatedEntityRefBase associationBase, InternalEntitySubscribeRefusedBase refusal)
            {
                // \note: request comes from session during the initial handshake, so we don't need to kick it or
                //        notify it back with InternalSessionPlayerJoinedAGuild/KickedFromGuild.

                switch (refusal)
                {
                    case InternalEntitySubscribeRefusedBase.Builtins.NotAParticipant:
                    {
                        // Player is not a member of the guild. Clear state and try again.
                        _log.Debug("Resolving unsettled operation on session init: Not a member of guild {GuildId}", GuildState.GuildId);

                        ClearCurrentGuildState();
                        await PlayerBase.PersistStateIntermediate();

                        GuildIncarnation++;
                        return true;
                    }

                    case InternalGuildMemberSubscribeRefused { Result: InternalGuildMemberSubscribeRefused.ResultCode.Kicked }:
                    {
                        // Player kicked while it was offline. Clear state and try again.
                        _log.Debug("Resolving unsettled operation on session init: Kicked from the guild {GuildId}", GuildState.GuildId);
                        _ = await TryCompleteKickedFromGuild();
                        return true;
                    }

                    case InternalGuildMemberSubscribeRefused { Result: InternalGuildMemberSubscribeRefused.ResultCode.PendingPlayerOps } pendingPlayerOps:
                    {
                        // there was some pending work. Push them on first to make sure any
                        // previous-session's lost actions happen before new session ops.
                        // During a session, we (the session) don't worry about the ordering.
                        //
                        // We need to resubscribe as this might modify the "playerResponse"
                        _log.Debug("Resolving unsettled operation on session init: Missing {NumOps} oplog entries", pendingPlayerOps.PendingPlayerOps.Count);
                        _ = ExecuteGuildMemberPlayerOps(pendingPlayerOps.PendingPlayerOps);

                        // Mark that we need to reply when we persists (We RUN the ops there, but haven't
                        // committed them yet).
                        _shouldSendGuildPlayerOpEpochUpdateAfterPersist = true;
                        return true;
                    }

                    case InternalGuildMemberSubscribeRefused { Result: InternalGuildMemberSubscribeRefused.ResultCode.GuildOpEpochSkip } guildOpEpochSkip:
                    {
                        // Guild is more in the future than player w.r.t guild ops. Player must skip it's epoch to avoid losing changes.
                        if (guildOpEpochSkip.GuildOpSkipTo > GuildState.LastPendingGuildOpEpoch)
                        {
                            _log.Warning("Skipping guild op epoch from {From} to {To}", GuildState.LastPendingGuildOpEpoch, guildOpEpochSkip.GuildOpSkipTo);

                            GuildState.LastPendingGuildOpEpoch = guildOpEpochSkip.GuildOpSkipTo;
                            await PlayerBase.PersistStateIntermediate();
                        }
                        return true;
                    }

                    default:
                        throw new InvalidOperationException($"Invalid refusal: {refusal.GetType().ToGenericTypeString()}");
                }
            }

            /// <summary>
            /// Enqueues the update of the <see cref="CreateGuildMemberPlayerData"/> to the guild.
            /// </summary>
            public void EnqueueGuildMemberPlayerDataUpdate()
            {
                // postpone to coalesce updates and avoid re-entrancy from action listener
                if (_pendingGuildMemberPlayerDataUpdate)
                    return;
                _pendingGuildMemberPlayerDataUpdate = true;
                _self.Tell(RefreshGuildMemberPlayerData.Instance, sender: _self);
            }

            [CommandHandler]
            void HandleRefreshGuildMemberPlayerData(RefreshGuildMemberPlayerData command)
            {
                _pendingGuildMemberPlayerDataUpdate = false;

                if (GuildState.GuildId == EntityId.None)
                    return;

                // \note: there is very slim race here. If the player is updated just before a new session starts,
                //        this message is sent first. Then the session starts and updates the player data via the
                //        subscription to the guild. Now, if the delivery of this message is delayed so much the
                //        session round trip is faster, this message will be delivered after the latter message and
                //        possibly overwrite newer data with old.
                PlayerBase.CastMessage(GuildState.GuildId, new InternalGuildMemberPlayerDataUpdate(PlayerBase._entityId, GuildState.GuildMemberInstanceId, CreateGuildMemberPlayerData()));
            }

            public enum GuildActionExecutionMode
            {
                /// <summary>
                /// Action is executed on guild on best-effort basis. If Guild crashes or there is an uncoordinated server shutdown,
                /// the action may be lost. If this entity crashes and is rolled back to a time before this call, the action may still
                /// be executed.
                /// </summary>
                Relaxed = 0,

                /// <summary>
                /// Action is executed on guild. If Guild crashes or there is an uncoordinated server shutdown,
                /// the action is executed on a later date. If Guild state is rolled back due to a crash, the
                /// action is executed again. If this entity crashes and is rolled back to a time before this call,
                /// the action is not executed on the guild.
                /// </summary>
                EventuallyConsistent,

                // \todo: should there be ConsistentWithUniqueExecution that guarantees single execution by persisting
                //        after execution. Hence rollbacks cannot affect it. Good if there are side-effects? Should be
                //        possible to emulate manually as well.
            }

            /// <summary>
            /// Enqueues <paramref name="guildAction"/> to be executed on the current guild. If the player is not currently in the guild,
            /// does nothing. Note that the player might already be kicked from current guild, but has not yet been informed of it. In this
            /// case, also does nothing.
            /// </summary>
            public void EnqueueGuildAction(GuildActionBase guildAction, GuildActionExecutionMode mode)
            {
                if (GuildState.GuildId == EntityId.None)
                    return;

                MetaSerialized<GuildActionBase> serializedAction = MetaSerialization.ToMetaSerialized(guildAction, MetaSerializationFlags.IncludeAll, PlayerBase._logicVersion);

                switch (mode)
                {
                    case GuildActionExecutionMode.Relaxed:
                    {
                        PlayerBase.CastMessage(GuildState.GuildId, new InternalGuildEnqueueMemberActionRequest(PlayerBase._entityId, GuildState.GuildMemberInstanceId, serializedAction));
                        break;
                    }

                    case GuildActionExecutionMode.EventuallyConsistent:
                    {
                        // Write actions to PendingGuildOps (pseudo-WAL) and schedule persisting the WAL. After WAL is persisted we, inform Guild.
                        GuildState.LastPendingGuildOpEpoch++;
                        int epoch = GuildState.LastPendingGuildOpEpoch;

                        if (GuildState.PendingGuildOps == null)
                            GuildState.PendingGuildOps = new OrderedDictionary<int, GuildMemberGuildOpLogEntry>();
                        GuildState.PendingGuildOps.Add(epoch, new GuildOpRunGuildAction(serializedAction));

                        PlayerBase.SchedulePersistState();
                        break;
                    }

                    default:
                        throw new InvalidEnumArgumentException(nameof(mode), (int)mode, typeof(GuildActionExecutionMode));
                }
            }

            /// <summary>
            /// Informs guild of all pending, persisted ops that should be executed. Pending
            /// persisted guild ops are the ops which have been enqueued and before the last
            /// persist. (i.e. they have their epoch less or equal to _persistedGuildOpEpoch).
            /// </summary>
            void EnqueueSyncPersistedGuildOpsToGuild()
            {
                if (_pendingSyncPersistedGuildOpsToGuild)
                    return;
                _pendingSyncPersistedGuildOpsToGuild = true;
                _self.Tell(SyncPersistedGuildOpsToGuild.Instance, sender: _self);
            }

            OrderedDictionary<int, GuildMemberGuildOpLogEntry> TryGetCommittedPendingGuildOps()
            {
                if (GuildState.PendingGuildOps == null)
                    return null;

                OrderedDictionary<int, GuildMemberGuildOpLogEntry> committedOps = null;
                foreach ((int epoch, GuildMemberGuildOpLogEntry pendingOp) in GuildState.PendingGuildOps)
                {
                    // only choose persisted
                    if (epoch > _persistedGuildOpEpoch)
                        continue;
                    if (committedOps == null)
                        committedOps = new OrderedDictionary<int, GuildMemberGuildOpLogEntry>();
                    committedOps.Add(epoch, pendingOp);
                }
                return committedOps;
            }

            [CommandHandler]
            void HandleSyncPersistedGuildOpsToGuild(SyncPersistedGuildOpsToGuild command)
            {
                if (!_pendingSyncPersistedGuildOpsToGuild)
                    return;

                _pendingSyncPersistedGuildOpsToGuild = false;

                if (GuildState.GuildId == EntityId.None)
                    return;

                OrderedDictionary<int, GuildMemberGuildOpLogEntry> committedPendingOps = TryGetCommittedPendingGuildOps();
                if (committedPendingOps != null)
                    PlayerBase.CastMessage(GuildState.GuildId, new InternalGuildRunPendingGuildOpsRequest(PlayerBase._entityId, GuildState.GuildMemberInstanceId, committedPendingOps));
            }

            [MessageHandler]
            void HandleInternalPlayerPendingGuildOpsCommitted(EntityId fromEntityId, InternalPlayerPendingGuildOpsCommitted opsCommitted)
            {
                // we are in a different guild now
                if (GuildState.GuildId != fromEntityId)
                    return;

                // Kick request refers to some other time we were in this guild
                if (GuildState.GuildMemberInstanceId != opsCommitted.MemberInstanceId)
                    return;

                // We might send multiple identical/overlapping requests. So we might get multiple responses.

                if (GuildState.PendingGuildOps == null)
                    return;

                GuildState.PendingGuildOps.RemoveWhere(kv => kv.Key <= opsCommitted.CommittedGuildOpEpoch);
                if (GuildState.PendingGuildOps.Count == 0)
                    GuildState.PendingGuildOps = null;
            }

            [MessageHandler]
            async Task HandleInternalPlayerKickedFromGuild(InternalPlayerKickedFromGuild kickedFromGuild)
            {
                // We are in a different (or not in a) guild right now?
                // Reply back that yes, we have indeed been kicked from the guild. Note that
                // we reply with the request's InstanceId.
                // We cannot change GuildId without committing, so no need to commit again.
                if (GuildState.GuildId != kickedFromGuild.GuildId)
                {
                    PlayerBase.CastMessage(kickedFromGuild.GuildId, new InternalGuildPlayerClearKickedState(PlayerBase._entityId, kickedFromGuild.MemberInstanceId));
                    return;
                }

                // Kick request refers to some other time we were in this guild.
                // Reply that yes, we have acknowledged that this is no longer relevant for us. Note that
                // we reply with the request's InstanceId. (We could ignore this message and it would
                // get sorted out in next login, but in case this is a churned account, that might never come).
                // This cannot happen in a normal execution, but selective DB rollbacks may cause this.
                if (GuildState.GuildMemberInstanceId != kickedFromGuild.MemberInstanceId)
                {
                    _log.Warning("Got stale kick-notification. Invalid member instance. Clearing.");
                    PlayerBase.CastMessage(kickedFromGuild.GuildId, new InternalGuildPlayerClearKickedState(PlayerBase._entityId, kickedFromGuild.MemberInstanceId));
                    return;
                }

                // Kick request came from our guild. Try to handle if (it could turn out to be stale)
                _ = await TryCompleteKickedFromGuild();
            }

            [Flags]
            enum CompleteKickedFromGuildResults
            {
                None = 0,
                WasKicked = 0x01,
                SessionDesynchronized = 0x02,
            }

            async Task<CompleteKickedFromGuildResults> TryCompleteKickedFromGuild()
            {
                // We have received information that we have been kicked from our guild. Do the leave handshake
                // to handle unfinished ops.
                // Note that it could turn out this is stale request if guild does a rollback (or other shenanigans).

                EntityId guildId = GuildState.GuildId;
                int memberInstanceId = GuildState.GuildMemberInstanceId;
                bool sessionDesynchronized = false;
                int numTimesAttempted = 0;
                IGuildMemberKickReason kickReason;

                _log.Debug("Starting kicked-from-guild handshake with guild {GuildId}", guildId);

                for (; ; )
                {
                    InternalGuildPeekKickedStateResponse isKickedResponse = await PlayerBase.EntityAskAsync<InternalGuildPeekKickedStateResponse>(guildId, new InternalGuildPeekKickedStateRequest(PlayerBase._entityId, memberInstanceId, _persistedPlayerOpEpoch));

                    if (isKickedResponse.Result == InternalGuildPeekKickedStateResponse.ResultCode.NotKicked)
                    {
                        // we are not really kicked. Nothing to do.
                        return 0;
                    }
                    else if (isKickedResponse.Result == InternalGuildPeekKickedStateResponse.ResultCode.Kicked)
                    {
                        kickReason = isKickedResponse.KickReasonOrNull;
                        break;
                    }
                    else if (isKickedResponse.Result == InternalGuildPeekKickedStateResponse.ResultCode.NotAMember)
                    {
                        kickReason = null;
                        break;
                    }

                    if (isKickedResponse.Result == InternalGuildPeekKickedStateResponse.ResultCode.PendingPlayerOps)
                    {
                        // If we must execute and commit pending operations
                        GuildPlayerOpResults commitResults = ExecuteGuildMemberPlayerOps(isKickedResponse.PendingPlayerOps);
                        await PlayerBase.PersistStateIntermediate();

                        if (commitResults.HasFlag(GuildPlayerOpResults.SessionDesynchronized))
                            sessionDesynchronized = true;
                    }

                    numTimesAttempted++;
                    if (numTimesAttempted > 3)
                    {
                        await PlayerBase.PersistStateIntermediate();
                        throw new Exception("Player-Guild relationship did not settle after 3 iterations during kick.");
                    }
                }

                _log.Debug("Successfully completed kicked-from-guild handshake with guild {GuildId}. Now leaving.", guildId);

                // clear from player data
                ClearCurrentGuildState();
                // \todo: Delivering kick reason to client: Store to GuildState.KickedState { GuildId, KickReason } if
                //        we want to? Should send a message to Client?
                _ = kickReason; // \todo Remove this discard when kickReason is actually used
                await PlayerBase.PersistStateIntermediate();

                GuildIncarnation++;

                // Let session know
                if (sessionDesynchronized)
                    PlayerBase.KickPlayerIfConnected(PlayerForceKickOwnerReason.InternalError);
                else
                    PlayerBase.PublishMessage(EntityTopic.Owner, new InternalSessionPlayerKickedFromGuild(GuildIncarnation));

                // Let guild know we have acknowledged the kick (and possibly committed it)
                PlayerBase.CastMessage(guildId, new InternalGuildPlayerClearKickedState(PlayerBase._entityId, memberInstanceId));

                if (sessionDesynchronized)
                    return CompleteKickedFromGuildResults.WasKicked | CompleteKickedFromGuildResults.SessionDesynchronized;
                else
                    return CompleteKickedFromGuildResults.WasKicked;
            }

            [Flags]
            enum GuildPlayerOpResults
            {
                None = 0,

                /// <summary>
                /// Model has been mutated. If there is a session, it must be killed.
                /// </summary>
                SessionDesynchronized = 0x01,
            }

            GuildPlayerOpResults ExecuteGuildMemberPlayerOps(OrderedDictionary<int, GuildMemberPlayerOpLogEntry> ops)
            {
                GuildPlayerOpResults combinedResult = GuildPlayerOpResults.None;
                foreach ((int epoch, GuildMemberPlayerOpLogEntry op) in ops)
                {
                    if (epoch <= GuildState.LastPlayerOpEpoch)
                        continue;
                    combinedResult |= ExecuteGuildMemberPlayerOp(epoch, op);
                }
                return combinedResult;
            }

            GuildPlayerOpResults ExecuteGuildMemberPlayerOp(int epoch, GuildMemberPlayerOpLogEntry op)
            {
                GuildState.LastPlayerOpEpoch = epoch;

                // We need to be a bit careful if execution of an action crashes we don't end up with a
                // infinite loop (because guild will send the actions again until they are executed).

                try
                {
                    switch (op)
                    {
                        case PlayerGuildOpTransactionCommitted txnCommit:
                        {
                            _log.Debug("Executing on epoch={Epoch} PlayerGuildOpTransactionCommitted", epoch);

                            PlayerActionBase initiatingAction = null;
                            if (!txnCommit.InitiatingAction.IsEmpty)
                                initiatingAction = txnCommit.InitiatingAction.Deserialize(resolver: null, logicVersion: PlayerBase._logicVersion);

                            PlayerTransactionFinalizingActionBase finalizingAction = null;
                            if (!txnCommit.FinalizingAction.IsEmpty)
                                finalizingAction = txnCommit.FinalizingAction.Deserialize(resolver: null, logicVersion: PlayerBase._logicVersion);

                            // must run immediately.

                            if (initiatingAction != null)
                            {
                                MetaActionResult result = ModelUtil.RunAction(PlayerBase.Model, initiatingAction, NullChecksumEvaluator.Context);
                                if (result != MetaActionResult.Success)
                                {
                                    _log.Warning("Execute of resurrected transaction initiating action failed. ActionResult: {Result}", result);

                                    // Init failing leads to abort.
                                    finalizingAction = null;
                                }
                            }
                            if (finalizingAction != null)
                            {
                                MetaActionResult result = ModelUtil.RunAction(PlayerBase.Model, finalizingAction, NullChecksumEvaluator.Context);
                                if (result != MetaActionResult.Success)
                                    _log.Warning("Execute of resurrected transaction finalizing action failed. ActionResult: {Result}", result);
                            }

                            // If we have a session here, it means we have made there has been
                            // a transaction during the the session but the session was not aware
                            // of. Session cannot continue.

                            return GuildPlayerOpResults.SessionDesynchronized;
                        }

                        default:
                        {
                            _log.Warning("Unhandled player op {Type}. Ignored.", op.GetType());
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log.Warning("Executing ExecuteGuildMemberGuildOp failed. Dropping op. Error={Cause}", ex);
                }

                return GuildPlayerOpResults.None;
            }

            [EntityAskHandler]
            InternalPlayerGetGuildInviterAvatarResponse HandleInternalPlayerGetGuildInviterAvatarRequest(InternalPlayerGetGuildInviterAvatarRequest request)
            {
                GuildInviterAvatarBase inviterAvatar = CreateGuildInviterAvatar();
                return new InternalPlayerGetGuildInviterAvatarResponse(MetaSerialization.ToMetaSerialized<GuildInviterAvatarBase>(inviterAvatar, MetaSerializationFlags.IncludeAll, PlayerBase._logicVersion));
            }
        }

        protected abstract class GuildComponentBase<TPlayer> : GuildComponentBase where TPlayer : PlayerActorBase<TModel, TPersisted>
        {
            protected TPlayer Player { get; set; }
            protected override PlayerActorBase<TModel, TPersisted> PlayerBase => Player;

            public GuildComponentBase(TPlayer player)
            {
                Player = player;
            }
        }

        protected override sealed EntityComponent CreateComponent(Type componentType)
        {
            if (componentType.IsSubclassOf(typeof(GuildComponentBase)))
            {
                Guilds = CreateGuildComponent();
                return Guilds;
            }
            return base.CreateComponent(componentType);
        }

        protected virtual GuildComponentBase CreateGuildComponent()
        {
            return null;
        }

        protected GuildComponentBase Guilds { get; private set; }
    }
}

#endif
