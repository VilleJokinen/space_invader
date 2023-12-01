// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using Metaplay.Core.Serialization;
using Metaplay.Core.TypeCodes;

namespace Metaplay.Core.Player
{
    /// <summary>
    /// Special case of <see cref="PlayerAction"/> which can only be triggered by the server.
    ///
    /// Unsynchronized server actions should only modify parts of state that are marked with the [NoChecksum]
    /// attribute. The action execution  time is <i>not synchronized</i> and the action is run at different points
    /// in time (as Model observes time) on client and server. Server executes the action first, and then client at
    /// some later time. Any modification to state that is included in the checksum checks will become inconsistent
    /// and fail the checsum checks.
    /// </summary>
    [MetaImplicitMembersRange(110, 120)]
    [ModelActionExecuteFlags(ModelActionExecuteFlags.FollowerUnsynchronized)]
    public abstract class PlayerUnsynchronizedServerActionBase : PlayerActionBase
    {
    }

    /// <summary>
    /// <inheritdoc cref="PlayerUnsynchronizedServerActionBase"/>
    /// </summary>
    public abstract class PlayerUnsynchronizedServerActionCore<TModel> : PlayerUnsynchronizedServerActionBase where TModel : IPlayerModelBase
    {
        public override MetaActionResult InvokeExecute(IPlayerModelBase player, bool commit)
        {
            return Execute((TModel)player, commit);
        }

        public abstract MetaActionResult Execute(TModel player, bool commit);
    }

    /// <summary>
    /// Server request the client to execute a given <see cref="PlayerUnsynchronizedServerActionBase"/>.
    /// </summary>
    [MetaMessage(MessageCodesCore.PlayerExecuteUnsynchronizedServerAction, MessageDirection.ServerToClient)]
    public class PlayerExecuteUnsynchronizedServerAction : MetaMessage
    {
        public MetaSerialized<PlayerActionBase> Action { get; private set; }   // Action to execute on the client
        public int TrackingId { get; private set; }

        PlayerExecuteUnsynchronizedServerAction() { }
        public PlayerExecuteUnsynchronizedServerAction(MetaSerialized<PlayerActionBase> action, int trackingId)
        {
            Action = action;
            TrackingId = trackingId;
        }
    }

    /// <summary>
    /// Announce execution of an unsynchronized server action by id. This is a marker action.
    /// </summary>
    [ModelAction(ActionCodesCore.PlayerUnsynchronizedServerActionMarker)]
    public sealed class PlayerUnsynchronizedServerActionMarker : PlayerActionCore<IPlayerModelBase>
    {
        public int TrackingId { get; private set; }

        /// <summary>
        /// Action that the client executed. This is set by server just before this marker
        /// is executed as a no-op. This allows capture of the Executed action in the server
        /// journal and hence allows better error logging in the case of a checksum mismatch.
        /// Specifically in the case that the original action is the cause of the checksum mismatch,
        /// error detection will print this field along the action, rather than just printing the
        /// opaque TrackingId.
        /// </summary>
        [ServerOnly]
        public PlayerActionBase ClientExecutedAction { get; set; }

        PlayerUnsynchronizedServerActionMarker() { }
        public PlayerUnsynchronizedServerActionMarker(int trackingId)
        {
            TrackingId = trackingId;
        }

        public override MetaActionResult Execute(IPlayerModelBase player, bool commit)
        {
            // This action is a placeholder for the a real unsynchronized server action. The real
            // action is being executed by client, but the action was already executed on server
            // (in an unsynchronized manner). Hence this (marker) is only run on server, and even
            // here it doesn't do anything.
            return MetaActionResult.Success;
        }
    }
}
