// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using Metaplay.Core.Client;

namespace Metaplay.Core.Player
{
    /// <summary>
    /// A player-specific state relating to a specific <see cref="ClientSlot"/> sub-client.
    /// This state could contain for example the player's current division,
    /// or a history of previous divisions. This data does not necessarily represent
    /// the active Entity of an EntityClient.
    /// </summary>
    [MetaSerializable]
    public abstract class PlayerSubClientStateBase
    {
    }
}
