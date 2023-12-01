// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if !METAPLAY_DISABLE_GUILDS

using Metaplay.Core.Model;
using Metaplay.Core.Player;
using System;
using System.Runtime.Serialization;

namespace Metaplay.Core.Guild
{
    [MetaSerializable(MetaSerializableFlags.ImplicitMembers)]
    [MetaImplicitMembersDefaultRangeForMostDerivedClass(1, 100)]
    public interface ITransactionPlan
    {
    }

    public class TransactionPlanningFailure : Exception
    {
    }

    public enum GuildTransactionConsistencyMode
    {
        /// <summary>
        /// In the case of an uncontrolled shutdown or crash, in case of a succesful Transaction, changes
        /// made to Player or Guild may be lost. In particular, it is possible that changes made to Player
        /// are lost but changes made to Guild survive, or vice versa.
        /// </summary>
        Relaxed = 0,

        /// <summary>
        /// In the case of an uncontrolled shutdown or crash, in case of a succesful Transaction, changes
        /// made to Player and Guild may either both be lost, or both survive. This causes re-executing
        /// player actions in case of a rollback, but there will be exactly single execution on eventual
        /// surviving timeline.
        /// <para>
        /// EventuallyConsistent consistency mode causes an additional database write on server during the
        /// transaction. This increases database load and transaction latency compared to Relaxed mode.
        /// </para>
        /// </summary>
        EventuallyConsistent,

        // \todo: is there a demand for something in between. Something that allows only Player or both
        //        rolling back. Or only Guild or both.
    }

    /// <summary>
    /// GuildTransaction allows Guild and Player state to be inspected and mutated as if it was a single operation.
    ///
    /// GuildTransaction is operation in which Guild and Player state are inspected at the same time by the server, and
    /// then an server-side action is executed on both Guild and Player. These server-issued actions are called Finalizing
    /// actions and they end the Transaction. Additionally for improved feedback and to allow to manage state more
    /// conveniently, a transaction contains an client-side "Initiating" action. This actions is a
    /// part of the transaction, but does not have any execution order guarantees except for that it happens before
    /// the finalizing actions.
    ///
    /// Player actions are executed as follows:
    /// <code>
    /// <![CDATA[
    ///                      +---------+                 +---------+
    ///                      | Client  |                 | Server  |
    ///                      +---------+                 +---------+
    ///                           |                          |
    ///                           | Transaction request      |
    ///                           |------------------------->|
    /// / Initiating Player \  -- |                          | -- / Initiating Player \
    /// \ Action            /     |                          |    \ Action            /
    ///                           |                          |
    ///    / Normal actions \  -- | Flush                    |
    ///    \ and Ticks      /     |--->                      |
    ///                           |                          |
    ///                           |        Transaction Reply |
    ///                           |<-------------------------|
    /// / Finalizing Player \  -- |                    Flush |
    /// \ Action            /     |                     ---> | -- / Normal actions \
    ///                           |                          |    \ and Ticks      /
    ///                           | Transcation Ack          |
    ///                           |------------------------->|
    ///                           |                          | -- / Finalizing Player \
    ///                           |                          |    \ Action            /
    ///                           V                          V
    /// ]]>
    /// </code>
    ///
    /// Guild actions are executed as follows:
    /// <code>
    /// <![CDATA[
    ///                      +---------+                 +---------+
    ///                      | Client  |                 | Server  |
    ///                      +---------+                 +---------+
    ///                           |                          |
    ///                           |                    Flush | -- / Normal actions \
    ///                           |                      <---|    \ and Ticks      /
    ///                           |                          |
    ///                           | Transaction request      |
    ///                           |------------------------->|
    ///                           |                          | -- / Finalizing Guild \
    ///                           | Flush                    |    \ Action           /
    ///    / Normal actions \  -- | <---                     |
    ///    \ and Ticks      /     |                          |
    ///                           |        Transaction Reply |
    ///                           |<-------------------------|
    /// / Finalizing Guild  \  -- |                          |
    /// \ Action            /     |                          |
    ///                           |                          |
    ///                           V                          V
    /// ]]>
    /// </code>
    ///
    /// For performance reasons, during the inspection of the Guild and the Player state, only a subset of the state
    /// is inspected. This subset is called a "Plan". For PlayerModel we create a "PlayerPlan" and for GuildModel
    /// we create a "GuildPlan". Additionally, we have an option to supply server-side secrets in the form of
    /// "ServerPlan" but this is currently unused. For convenience these Plans are combined into a "FinalizingPlan"
    /// which is then used to create the final Actions.
    ///
    /// Data flows as follows:
    /// <code>
    /// <![CDATA[
    ///          +-------------+    +------------+
    ///          | PlayerModel |    | GuildModel |
    ///          +-------------+    +------------+
    ///                |                   |
    ///                V                   V
    ///         (Player Planning)   (Guild Planning)
    ///                |                   |
    ///                V                   V
    ///          +------------+     +-----------+   +------------+
    ///          | PlayerPlan |     | GuildPlan |   | ServerPlan |
    ///          +------------+     +-----------+   +------------+
    ///             | |    |                |              |
    ///             | |    '------------.   |    .---------'
    ///             | |                 V   V    V
    ///             | |             ( Final Planning )
    ///             | |                     |
    ///             | |                     V
    ///             | |              +----------------+
    ///             | |              | FinalizingPlan |
    ///             | |              +----------------+
    ///             | |                          | |
    ///             | '---------.                | '----------------.
    ///             V           V                V                  V
    /// +--------------+  +--------------+  +--------------+  +-------------+
    /// |  Initiating  |  |  Finalizing  |  |  Finalizing  |  | Finalizing  |
    /// | PlayerAction |  | (Cancelling) |  | (Successful) |  | GuildAction |
    /// +--------------+  | PlayerAction |  | PlayerAction |  +-------------+
    ///                   +--------------+  +--------------+
    /// ]]>
    /// </code>
    ///
    /// Transaction can Terminate in three different ways.
    ///
    /// If transaction preconditions are not fulfilled, the transaction is Aborted. This is invoked by either
    /// throwing TransactionPlanningFailure during Player Planning step, or by having execution of the Initiating
    /// PlayerAction complete non-successfully. For example, if player does not have sufficient amount of resources
    /// to buy a certain item. When transaction is Aborted, no actions are executed.
    ///
    /// If transaction becomes stale for system or game-specific reasons, the transaction is Cancelled. For example,
    /// if player is kicked from the guild, but client has not observed this yet. Or the item is no longer available
    /// when the server attempts to process a purchase request. This can be invoked by throwing a
    /// TransactionPlanningFailure during any of the remaining planning phases. When transaction is Cancelled, no guild
    /// actions are executed and hence guild model is not modified. The player model executes both Initiating PlayerAction
    /// and then the Cancelling PlayerAction. Cancelling action is executed as if it were a Finalizing PlayerAction.
    ///
    /// If transaction is successful, Guild executes Finalizing GuildAction, and player executes both Initiating
    /// PlayerAction and Finalizing PlayerAction.
    ///
    /// Sequence of operations is as follows:
    /// <code>
    /// <![CDATA[
    ///            |
    ///            V
    ///     [ Player planning ]  ---> (failure) --> [ Abort ]
    ///            |
    ///            V
    ///    [ Initiating Action ] ---> (failure) --> [ Abort ]
    ///            |
    ///        (success)
    ///            |
    ///            V
    ///   [ Guild and Server Planning ]  ---> (failure) --> [ Canceling Action ]
    ///            |
    ///        (success)
    ///            |
    ///            V
    ///  [ Finalizing Actions ]
    /// ]]>
    /// </code>
    /// </summary>
    [MetaSerializable(MetaSerializableFlags.ImplicitMembers)]
    [MetaImplicitMembersDefaultRangeForMostDerivedClass(1, 100)]
    public interface IGuildTransaction
    {
        [IgnoreDataMember] EntityId InvokingPlayerId { get; set; }
        [IgnoreDataMember] GuildTransactionConsistencyMode ConsistencyMode { get; }

        /// <summary>
        /// Extracts the relevant player information for planning. If the call throws <see cref="TranssactionPlanningFailure"/>,
        /// the transaction is aborted.
        /// <para>If no plan is required, method may return <c>null</c>.</para>
        /// </summary>
        ITransactionPlan                        PlanForPlayer                   (IPlayerModelBase player);

        /// <summary>
        /// Extracts the relevant guild information for planning. If the call throws <see cref="TransactionPlanningFailure"/>,
        /// the transaction is canceled and the cancellation action will be executed. If the player is no longer in the guild,
        /// the transaction is automatically canceled.
        /// <para>If no plan is required, method may return <c>null</c>.</para>
        /// </summary>
        ITransactionPlan                        PlanForGuild                    (IGuildModelBase guild, GuildMemberBase member);

        /// <summary>
        /// Combines the plans extracted with <c>PlanForPlayer</c> and <c>PlanForGuild</c>. <paramref name="serverPlan"/> is computed
        /// by the handler in the SessionActor and will be null if no custom extractor is defined. If the call throws
        /// <see cref="TranssactionPlanningFailure"/>, the transaction is canceled and the cancellation action will be executed.
        /// <para>If no plan is required, method may return <c>null</c>.</para>
        /// </summary>
        ITransactionPlan                        PlanForFinalizing               (ITransactionPlan playerPlan, ITransactionPlan guildPlan, ITransactionPlan serverPlan);

        /// <summary>
        /// Creates a player action that is run by the client when it submits the Transaction for processing. This is
        /// run by the client at the start of the transaction. If no action is required, method may return <c>null</c>.
        /// <para>
        /// If the execution of this action fails, the transaction is aborted and no changes are made.
        /// </para>
        /// <para>
        /// If the execution of this action completes successfully, the PlayerModel must be left in a state where
        /// the Finalizing and the Canceling action will complete successfully.
        /// </para>
        /// <para>
        /// For example, in case of a "Buy item" transaction, this action would check player has enough money and
        /// reserve it (remove it). On finalization, we then give the item or on cancellation refund the reserved
        /// money if buying fails. If we were to remove the money only at the finalization, a player could spend
        /// all their money before transaction completed and reach negative balance.
        /// </para>
        /// <para>If no action is required, method may return <c>null</c>.</para>
        /// </summary>
        PlayerActionBase                        CreateInitiatingPlayerAction    (ITransactionPlan playerPlan);

        /// <summary>
        /// Creates an action that applies the planned changes to the player. This execution of the action should
        /// always succeed. If no action is required, method may return <c>null</c>.
        /// </summary>
        PlayerTransactionFinalizingActionBase   CreateFinalizingPlayerAction    (ITransactionPlan finalizingPlan);

        /// <summary>
        /// Creates an action that cancels changes made in the player initiating action. This execution of the
        /// action should always succeed. If no action is required, method may return <c>null</c>.
        /// </summary>
        PlayerTransactionFinalizingActionBase   CreateCancelingPlayerAction    (ITransactionPlan playerPlan);

        /// <summary>
        /// Creates an action that applies the planned changes to the guild. This execution of the action should
        /// always succeed. If no action is required, method may return <c>null</c>.
        /// </summary>
        GuildActionBase                         CreateFinalizingGuildAction     (ITransactionPlan finalizingPlan);
    }

    /// <summary>
    /// <inheritdoc cref="IGuildTransaction"/>
    /// </summary>
    public abstract class GuildTransactionBase
        <
            TPlayerModel,
            TGuildModel,
            TGuildMember,
            TPlayerPlan,
            TGuildPlan,
            TServerPlan,
            TFinalizingPlan
        >
        : IGuildTransaction
        where TPlayerModel : IPlayerModelBase
        where TGuildModel : IGuildModelBase
        where TGuildMember : GuildMemberBase
        where TPlayerPlan: ITransactionPlan
        where TGuildPlan : ITransactionPlan
        where TServerPlan : ITransactionPlan
        where TFinalizingPlan : ITransactionPlan
    {
        [IgnoreDataMember] public EntityId InvokingPlayerId { get; set; }
        [IgnoreDataMember] public abstract GuildTransactionConsistencyMode ConsistencyMode { get; }

        /// <inheritdoc cref="IGuildTransaction.PlanForPlayer(IPlayerModelBase)"/>
        public abstract TPlayerPlan                             PlanForPlayer                   (TPlayerModel player);

        /// <inheritdoc cref="IGuildTransaction.PlanForGuild(IGuildModelBase,GuildMemberBase)"/>
        public abstract TGuildPlan                              PlanForGuild                    (TGuildModel guild, TGuildMember member);

        /// <inheritdoc cref="IGuildTransaction.PlanForFinalizing(ITransactionPlan, ITransactionPlan, ITransactionPlan)"/>
        public abstract TFinalizingPlan                         PlanForFinalizing               (TPlayerPlan playerPlan, TGuildPlan guildPlan, TServerPlan serverPlan);


        /// <inheritdoc cref="IGuildTransaction.CreateInitiatingPlayerAction(ITransactionPlan)"/>
        public abstract PlayerActionBase                        CreateInitiatingPlayerAction    (TPlayerPlan playerPlan);

        /// <inheritdoc cref="IGuildTransaction.CreateFinalizingPlayerAction(ITransactionPlan)"/>
        public abstract PlayerTransactionFinalizingActionBase   CreateFinalizingPlayerAction    (TFinalizingPlan finalizingPlan);

        /// <inheritdoc cref="IGuildTransaction.CreateCancelingPlayerAction(ITransactionPlan)"/>
        public abstract PlayerTransactionFinalizingActionBase   CreateCancelingPlayerAction     (TPlayerPlan playerPlan);

        /// <inheritdoc cref="IGuildTransaction.CreateFinalizingGuildAction(ITransactionPlan)"/>
        public abstract GuildActionBase                         CreateFinalizingGuildAction     (TFinalizingPlan finalizingPlan);

        #region IGuildTransaction

        ITransactionPlan IGuildTransaction.PlanForPlayer(IPlayerModelBase player) => PlanForPlayer((TPlayerModel)player);
        ITransactionPlan IGuildTransaction.PlanForGuild(IGuildModelBase guild, GuildMemberBase member) => PlanForGuild((TGuildModel)guild, (TGuildMember)member);
        ITransactionPlan IGuildTransaction.PlanForFinalizing(ITransactionPlan playerPlan, ITransactionPlan guildPlan, ITransactionPlan serverPlan) => PlanForFinalizing((TPlayerPlan)playerPlan, (TGuildPlan)guildPlan, (TServerPlan)serverPlan);
        PlayerActionBase IGuildTransaction.CreateInitiatingPlayerAction(ITransactionPlan playerPlan) => CreateInitiatingPlayerAction((TPlayerPlan)playerPlan);
        PlayerTransactionFinalizingActionBase IGuildTransaction.CreateFinalizingPlayerAction(ITransactionPlan finalizerPlan) => CreateFinalizingPlayerAction((TFinalizingPlan)finalizerPlan);
        PlayerTransactionFinalizingActionBase IGuildTransaction.CreateCancelingPlayerAction(ITransactionPlan playerPlan) => CreateCancelingPlayerAction((TPlayerPlan)playerPlan);
        GuildActionBase IGuildTransaction.CreateFinalizingGuildAction(ITransactionPlan finalizerPlan) => CreateFinalizingGuildAction((TFinalizingPlan)finalizerPlan);

        #endregion
    }
}

#endif
