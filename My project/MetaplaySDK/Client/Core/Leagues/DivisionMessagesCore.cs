// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Client;
using Metaplay.Core.Model;
using Metaplay.Core.MultiplayerEntity.Messages;

namespace Metaplay.Core.League.Messages
{
    //
    // This file contains Server-to-client and client-to-server messages that are part of Metaplay core.
    //

    /// <summary>
    /// Implementation for <see cref="ChannelContextDataBase"/> for division. This type may be extended
    /// by inheriting it.
    /// </summary>
    [LeaguesEnabledCondition]
    [MetaSerializableDerived(102)]
    [MetaReservedMembers(200, 300)]
    public class DivisionChannelContextData : ChannelContextDataBase
    {
        /// <summary>
        /// Participant Id in the division. Participant owns the channel.
        /// </summary>
        [MetaMember(201)] public EntityId ParticipantId { get; private set; }

        DivisionChannelContextData() { }
        public DivisionChannelContextData(ClientSlot clientSlot, EntityId participantId) : base(clientSlot)
        {
            ParticipantId = participantId;
        }
    }
}
