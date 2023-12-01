// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Game.Logic;
using Metaplay.Cloud.Entity;
using Metaplay.Core;
using Metaplay.Server;
using System;
using static System.FormattableString;

namespace Game.Server.Player
{
    [EntityConfig]
    public class PlayerConfig : PlayerConfigBase
    {
        public override Type EntityActorType => typeof(PlayerActor);
    }

    /// <summary>
    /// Entity actor class representing a player.
    /// </summary>
    public sealed class PlayerActor : PlayerActorBase<PlayerModel>, IPlayerModelServerListener
    {
        public PlayerActor(EntityId playerId) : base(playerId)
        {
        }

        protected override string RandomNewPlayerName()
        {
            return Invariant($"Guest {new Random().Next(100_000)}");
        }

        protected override void OnSwitchedToModel(PlayerModel model)
        {
            model.ServerListener = this;
        }
    }
}
