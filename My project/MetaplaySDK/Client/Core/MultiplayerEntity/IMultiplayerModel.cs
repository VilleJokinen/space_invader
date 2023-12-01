// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Config;
using Metaplay.Core.Model;

namespace Metaplay.Core.MultiplayerEntity
{
    /// <summary>
    /// Untyped subset of <see cref="IMultiplayerModel{TModel}"/>.
    /// </summary>
    public interface IMultiplayerModel : IModel
    {
        /// <summary>
        /// The LogChannel of this Model. Actions and Ticks should use this to log
        /// messages.
        /// </summary>
        LogChannel Log { get; set; }

        /// <summary>
        /// The GameCofig active for this Model. Actions and Ticks should use this Config
        /// as their config data source.
        /// </summary>
        ISharedGameConfig GameConfig { get; set; }

        /// <summary>
        /// Timestamp when Model was initialized for the first time.
        /// </summary>
        MetaTime CreatedAt { get; set; }

        /// <summary>
        /// The number of Ticks executed per second on this Model. This should be a constant value.
        /// </summary>
        int TicksPerSecond { get; }

        /// <summary>
        /// Time at the beginning of the Tick 0.
        /// </summary>
        MetaTime TimeAtFirstTick { get; }

        /// <summary>
        /// The number of ticks executed since <see cref="TimeAtFirstTick"/>.
        /// </summary>
        int CurrentTick { get; }

        /// <summary>
        /// The EntityId of the entity owning this model.
        /// </summary>
        EntityId EntityId { get; set; }

        /// <summary>
        /// The current time of the model based on the current tick.
        /// </summary>
        MetaTime CurrentTime { get; }

        /// <summary>
        /// Resets model time to <paramref name="timeAtFirstTick"/> by resetting <see cref="TimeAtFirstTick"/>
        /// to the given value and <see cref="CurrentTick"/> to 0-
        /// </summary>
        void ResetTime(MetaTime timeAtFirstTick);

        /// <summary>
        /// Called after model time was fast forwarded by specified amount of time. Used when time has
        /// progressed on the server, but there's no client to advance it. Mainly called when starting
        /// a new session (time may have elapsed if Actor was already resident in memory), or when doing
        /// the final persist into database (happens somewhat after the session has terminated).
        /// </summary>
        /// <param name="elapsedTime">The amount of time that was fast forwarded</param>
        void OnFastForwardTime(MetaDuration elapsedTime);

        /// <summary>
        /// Returns the name displayed as the name of this entity in the Dashboard.
        /// </summary>
        string GetDisplayNameForDashboard();

        /// <summary>
        /// Creates the <see cref="MultiplayerMemberPrivateStateBase"/> for the corresponding player.
        /// Returning <c>null</c> signifies no private data exists for the member. Note that while
        /// memberId is usually a PlayerId, it is the Id of the Member entity which could be any type
        /// of an entity.
        /// </summary>
        MultiplayerMemberPrivateStateBase GetMemberPrivateState(EntityId memberId);
    }

    /// <summary>
    /// The interface all Multiplayer Entity Models must implement. The <typeparamref name="TModel"/> parameter should be
    /// the type for the concrete class itself. For example <c>class MyModel : IMultiplayerModel&lt;MyModel&gt;</c>.
    /// </summary>
    /// <typeparam name="TModel">The concrete model type.</typeparam>
    public interface IMultiplayerModel<TModel> : IModel<TModel>, IMultiplayerModel
        where TModel : IMultiplayerModel<TModel>
    {
    }

    /// <summary>
    /// Contains member-specific private data of a multiplayer entity member. Member's private data is data
    /// stored in Model that is visible only to Server (ServerOnly) and the corresponding member.
    /// This can be useful for example in voting scenarios where whether a player has voted is
    /// shared but each player's vote should only be visible to the server and the player itself.
    /// In that case, the implementing type would contain the given vote of the player, if any.
    ///
    /// This type is used to deliver the private fields to a each member separately. Server calls
    /// <see cref="IMultiplayerModel.GetMemberPrivateState(EntityId)"/> to create this state and
    /// client then consumes the data in <see cref="ApplyToModel(IModel)"/>
    /// </summary>
    [MetaSerializable]
    [MetaImplicitMembersDefaultRangeForMostDerivedClass(1,100)]
    public abstract class MultiplayerMemberPrivateStateBase
    {
        [MetaMember(100)] public EntityId MemberId { get; private set; }

        protected MultiplayerMemberPrivateStateBase() { }
        protected MultiplayerMemberPrivateStateBase(EntityId memberId)
        {
            MemberId = memberId;
        }

        public abstract void ApplyToModel(IModel model);
    }
}
