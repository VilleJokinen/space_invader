// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;

namespace Metaplay.Core.Player
{
    [MetaSerializable]
    [MetaImplicitMembersRange(100, 110)]
    [ModelActionExecuteFlags(ModelActionExecuteFlags.LeaderSynchronized)]
    public abstract class PlayerActionBase : ModelAction<IPlayerModelBase>
    {
        /// <summary>
        /// Unique identifier for the player action. Only unique for within a single player during a single session.
        /// </summary>
        public int  Id  { get; set; }
    }

    /// <summary>
    /// Base class for all <see cref="ModelAction"/>s affecting <see cref="PlayerModel"/>.
    ///
    /// The Execute() method receives the current <see cref="PlayerModel"/> as an argument.
    /// Logging and client/server event listeners can be accessed from it.
    /// </summary>
    [MetaSerializable]
    public abstract class PlayerActionCore<TModel> : PlayerActionBase where TModel : IPlayerModelBase
    {
        public PlayerActionCore() { }

        public override MetaActionResult InvokeExecute(IPlayerModelBase player, bool commit)
        {
            return Execute((TModel)player, commit);
        }

        /// <summary>
        /// Execute the given action against a <see cref="PlayerModel"/> object. Typically this is run on the client
        /// upon the player performing an action, and then slightly later on the server where the server's version of
        /// <see cref="PlayerModel"/> is updated. In some cases like detecting Desyncs, Actions can get executed
        /// again, on a previous state.
        ///
        /// The <see cref="Execute(IPlayerModel, bool)"/> should consist of two phases: validation and commit.
        /// All the action validation must happen before modifying the state. This ensures that a hacked
        /// client sending invalid <see cref="PlayerAction"/>s will not be able to modify the actual game state.
        /// The validation happens via calls to <see cref="ModelAction.Validate(bool, string)"/>, which will log
        /// any validation failures and return the validation result. If false is returned, the calling code should
        /// return a relevant <see cref="MetaActionResult"/>.
        ///
        /// The action's <see cref="Execute(IPlayerModel, bool)"/> method should only modify the state if
        /// <paramref name="commit"/> is true.
        ///
        /// The execution may also trigger listener callbacks to let the client or the server know of key events.
        /// The listeners are accessible via ctx.ClientListener and ctx.ServerListener, respectively. ClientListener
        /// listener is typically used for updating UI, spawning effects, etc. ServerListener can be used to send
        /// let the server-side PlayerActor messages to other entities, interact with the database, etc.
        /// </summary>
        /// <param name="player">Player model to modify, also provides access to logging and listeners</param>
        /// <param name="commit">Boolean indicating whether the actions should modify the state or just do a dry-run
        /// (ie, only perform validations)</param>
        public abstract MetaActionResult Execute(TModel player, bool commit);
    }

    /// <summary>
    /// Base type for a transaction player-side finalization action.
    /// </summary>
    [MetaImplicitMembersRange(110, 120)]
    public abstract class PlayerTransactionFinalizingActionBase : PlayerSynchronizedServerActionBase
    {
    }

    public abstract class PlayerTransactionFinalizingActionCore<TModel> : PlayerTransactionFinalizingActionBase where TModel : IPlayerModelBase
    {
        public override MetaActionResult InvokeExecute(IPlayerModelBase player, bool commit)
        {
            return Execute((TModel)player, commit);
        }

        public abstract MetaActionResult Execute(TModel player, bool commit);
    }
}
