// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using Metaplay.Core.Serialization;
using Metaplay.Core.TypeCodes;
using System.Collections.Generic;

namespace Metaplay.Core.Player
{
    /// <summary>
    /// A checksum mismatch has been detected on the server. This generally happens when there's
    /// non-deterministic logic either in either <c>PlayerModel.Tick()</c> or a <c>PlayerAction.Execute()</c>.
    /// </summary>
    [MetaMessage(MessageCodesCore.PlayerChecksumMismatch, MessageDirection.ServerToClient)]
    public class PlayerChecksumMismatch : MetaMessage
    {
        public int      Tick        { get; private set; }   // Tick on which the checksum mismatch was detected
        public int      ActionIndex { get; private set; }   // Index of action on tick (or -1 for tick itself)
        public byte[]   AfterState  { get; private set; }   // State after the operation
        public byte[]   BeforeState { get; private set; }   // State before the operation (\todo [petri] support multiple checkpoints?)

        PlayerChecksumMismatch() { }
        public PlayerChecksumMismatch(int tick, int actionIndex, byte[] afterState, byte[] beforeState)
        {
            Tick        = tick;
            ActionIndex = actionIndex;
            AfterState  = afterState;
            BeforeState = beforeState;
        }
    }

    /// <summary>
    /// Client-reported details from a checksum mismatch situation so that the server can log/store the details.
    /// </summary>
    // \todo [petri] include serialized client state instead of computed diff?
    // \todo [petri] include last X client-side log events?
    [MetaMessage(MessageCodesCore.PlayerChecksumMismatchDetails, MessageDirection.ClientToServer), MessageRoutingRuleOwnedPlayer]
    public class PlayerChecksumMismatchDetails : MetaMessage
    {
        public int                              TickNumber      { get; private set; }
        public MetaSerialized<PlayerActionBase> Action          { get; private set; }
        public string                           PlayerModelDiff { get; private set; }
        public List<string>                     VagueDifferencePathsMaybe { get; private set; }

        public PlayerChecksumMismatchDetails() { }
        public PlayerChecksumMismatchDetails(int tickNumber, MetaSerialized<PlayerActionBase> action, string playerModelDiff, List<string> vagueDifferencePathsMaybe)
        {
            TickNumber      = tickNumber;
            Action          = action;
            PlayerModelDiff = playerModelDiff;
            VagueDifferencePathsMaybe = vagueDifferencePathsMaybe;
        }
    }

    /// <summary>
    /// Flush a set of <see cref="PlayerAction"/>s from client to server. Also informs the server
    /// that the client tick has advanced to the given position of time.
    /// </summary>
    [MetaMessage(MessageCodesCore.PlayerFlushActions, MessageDirection.ClientToServer), MessageRoutingRuleOwnedPlayer]
    public class PlayerFlushActions : MetaMessage
    {
        /// <summary>
        /// Number of ticks in a single <see cref="PlayerFlushActions"/>.
        /// Obeyed by client, validated by server in PlayerActorBase.
        /// </summary>
        public const int MaxTicksPerFlush = 1000;

        [MetaSerializable]
        public struct Operation
        {
            [MetaMember(1)] public PlayerActionBase Action          { get; private set; } // if null, then Tick
            [MetaMember(2)] public int              NumSteps        { get; private set; }
            [MetaMember(3)] public int              StartTick       { get; private set; }
            [MetaMember(4)] public int              OperationIndex  { get; private set; }

            public Operation(JournalPosition startPosition, PlayerActionBase action, int numSteps)
            {
                StartTick       = startPosition.Tick;
                OperationIndex  = startPosition.Operation;
                Action          = action;
                NumSteps        = numSteps;
            }
        }

        public MetaSerialized<List<Operation>> Operations { get; private set; }
        [PrettyPrint(PrettyPrintFlag.SizeOnly)]
        public uint[] Checksums { get; private set; }

        public PlayerFlushActions() { }
        public PlayerFlushActions(MetaSerialized<List<Operation>> operation, uint[] checksums)
        {
            Operations  = operation;
            Checksums   = checksums;
        }
    }

    /// <summary>
    /// Server acknowledges to the client that all actions have been received until the
    /// given position in time. The client can then purge from its memory any debug data
    /// from before the position in time.
    /// </summary>
    [MetaMessage(MessageCodesCore.PlayerAckActions, MessageDirection.ServerToClient)]
    public class PlayerAckActions : MetaMessage
    {
        public int UntilPositionTick { get; private set; }
        public int UntilPositionOperation { get; private set; }
        public int UntilPositionStep { get; private set; }

        public PlayerAckActions() {}
        public PlayerAckActions(JournalPosition untilPosition)
        {
            UntilPositionTick = untilPosition.Tick;
            UntilPositionOperation = untilPosition.Operation;
            UntilPositionStep = untilPosition.Step;
        }
    }

    /// <summary>
    /// Client requests a player name change. The name gets validated server side
    /// </summary>
    [MetaMessage(MessageCodesCore.PlayerChangeOwnNameRequest, MessageDirection.ClientToServer), MessageRoutingRuleOwnedPlayer]
    public class PlayerChangeOwnNameRequest : MetaMessage
    {
        public string NewName { get; private set; }

        public PlayerChangeOwnNameRequest() { }
        public PlayerChangeOwnNameRequest(string newName) { NewName = newName; }
    }

    /// <summary>
    /// Client requests to schedule their player's deletion
    /// </summary>
    [MetaMessage(MessageCodesCore.PlayerScheduleDeletionRequest, MessageDirection.ClientToServer), MessageRoutingRuleOwnedPlayer]
    public class PlayerScheduleDeletionRequest : MetaMessage
    {
        public PlayerScheduleDeletionRequest() { }
    }

    /// <summary>
    /// Client requests to cancel a scheduled deletion of their player
    /// </summary>
    [MetaMessage(MessageCodesCore.PlayerCancelScheduledDeletionRequest, MessageDirection.ClientToServer), MessageRoutingRuleOwnedPlayer]
    public class PlayerCancelScheduledDeletionRequest : MetaMessage
    {
        public PlayerCancelScheduledDeletionRequest() { }
    }
}
