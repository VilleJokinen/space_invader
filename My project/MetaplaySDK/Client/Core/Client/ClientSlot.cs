// This file is part of Metaplay SDK which is released under the Metaplay SDK License.


using Metaplay.Core.Model;

namespace Metaplay.Core.Client
{
    /// <summary>
    /// <para>
    /// A ClientSlot is a unique identifier for all game clients used to distinquish between
    /// them.
    /// </para>
    /// <para>
    /// The slot is used in the <see cref="MetaplayClientStore"/> as a key for each
    /// client and its corresponding <see cref="IEntityClientContext"/> if the client is an
    /// entity client. The slot is also used for delivering <see cref="Metaplay.Core.MultiplayerEntity.Messages.EntityInitialState"/>s
    /// to the correct client and linking up the entity channel.
    /// </para>
    /// <para>
    /// SDK-side <see cref="ClientSlot"/>s are defined in <see cref="ClientSlotCore"/>.
    /// The game can define its own by extending this class and adding new ones.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public class ClientSlot : DynamicEnum<ClientSlot>
    {
        protected ClientSlot(int id, string name) : base(id, name, true) { }
    }
}
