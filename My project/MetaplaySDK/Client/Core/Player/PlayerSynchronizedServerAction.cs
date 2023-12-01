// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using Metaplay.Core.Serialization;
using Metaplay.Core.TypeCodes;

namespace Metaplay.Core.Player
{
    /// <summary>
    /// Base type for actions that are generated by server and enqueued for client to execute. The client
    /// chooses the suitable time slot to execute the action, and both server and client run the action at the
    /// same Model timeline position. The actions and action contents (parameters) are controlled by server, but
    /// the exact execution time is controlled by client.
    /// </summary>
    [MetaImplicitMembersRange(120, 140)] // range [110, 120) is allocated for PlayerTransactionFinalizingActionBase
    [ModelActionExecuteFlags(ModelActionExecuteFlags.FollowerSynchronized)]
    public abstract class PlayerSynchronizedServerActionBase : PlayerActionBase
    {
    }

    public abstract class PlayerSynchronizedServerActionCore<TModel> : PlayerSynchronizedServerActionBase where TModel : IPlayerModelBase
    {
        public override MetaActionResult InvokeExecute(IPlayerModelBase player, bool commit)
        {
            return Execute((TModel)player, commit);
        }

        public abstract MetaActionResult Execute(TModel player, bool commit);
    }

    /// <summary>
    /// Server requests the client to enqueue a given <see cref="PlayerActionBase"/> for execution.
    /// </summary>
    [MetaMessage(MessageCodesCore.PlayerEnqueueSynchronizedServerAction, MessageDirection.ServerToClient)]
    public sealed class PlayerEnqueueSynchronizedServerAction : MetaMessage
    {
        public MetaSerialized<PlayerActionBase> Action { get; private set; }
        public int TrackingId { get; private set; }

        PlayerEnqueueSynchronizedServerAction() { }
        public PlayerEnqueueSynchronizedServerAction(MetaSerialized<PlayerActionBase> action, int trackingId)
        {
            Action = action;
            TrackingId = trackingId;
        }
    }

    /// <summary>
    /// Announce execution of an synchronized server action by id. This is a marker action.
    /// </summary>
    [ModelAction(ActionCodesCore.PlayerSynchronizedServerActionMarker)]
    public sealed class PlayerSynchronizedServerActionMarker : PlayerActionCore<IPlayerModelBase>
    {
        public int TrackingId { get; private set; }

        PlayerSynchronizedServerActionMarker() { }
        public PlayerSynchronizedServerActionMarker(int trackingId)
        {
            TrackingId = trackingId;
        }

        public override MetaActionResult Execute(IPlayerModelBase player, bool commit)
        {
            // this action is a placeholder and will be substituted at the protocol level
            // hence, this is never executed
            return MetaActionResult.UnknownError;
        }
    }
}