// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using static Game.Logic.PlayerModel;

namespace Game.Logic
{
    /// <summary>
    /// Game-specific player action class, which attaches all game-specific actions to <see cref="PlayerModel"/>.
    /// </summary>
    /// 

    
    public abstract class PlayerAction : PlayerActionCore<PlayerModel>
    {
    }

    /// <summary>
    /// Registry for game-specific ActionCodes, used by the individual PlayerAction classes.
    /// </summary>
    public static class ActionCodes
    {
        public const int PlayerClickButton = 5000;
        public const int Spaceship = 5001;
        public const int RestartGame = 5002;
    }

    /// <summary>
    /// Game-specific results returned from <see cref="PlayerActionCore.Execute(PlayerModel, bool)"/>.
    /// </summary>
    public static class ActionResult
    {
        // Shadow success result
        public static readonly MetaActionResult Success             = MetaActionResult.Success;

        // Game-specific results
        public static readonly MetaActionResult UnknownError        = new MetaActionResult(nameof(UnknownError));

        public static readonly MetaActionResult NotOwnPlanet = new MetaActionResult(nameof(NotOwnPlanet));
    }

    // Game-specific example action: bump PlayerModel.NumClicked, triggered by button tap

    [ModelAction(ActionCodes.PlayerClickButton)]
    public class PlayerClickButton : PlayerAction
    {
        public PlayerClickButton() { }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            if (commit)
            {
                //player.NumClicks += 1;
                //player.Log.Info("Button clicked!");
            }

            return ActionResult.Success;
        }
    }
    [ModelAction(ActionCodes.Spaceship)]
    public class Spaceship : PlayerAction
    {
        public Spaceship() { }
        public Spaceship(int firstClickedPlanet, int secondClickedPlanet) { this.firstClickedPlanet = firstClickedPlanet; this.secondClickedPlanet = secondClickedPlanet; }

        public int firstClickedPlanet;
        public int secondClickedPlanet;
        


        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            if (player.PlanetList[firstClickedPlanet].Ownership != 1)
            {
                return ActionResult.NotOwnPlanet;
            }
            
            if (commit)
            {
                if(firstClickedPlanet != secondClickedPlanet)
                {
                    player.sendShip(firstClickedPlanet, secondClickedPlanet);

                }


            }

            return ActionResult.Success;
        }
    }

}
