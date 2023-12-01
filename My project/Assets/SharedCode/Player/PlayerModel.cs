// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Tasks;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace Game.Logic
{
    /// <summary>
    /// Class for storing the state and updating the logic for a single 
    /// </summary>
    [MetaSerializableDerived(1)]
    [SupportedSchemaVersions(1, 1)]
    public class PlayerModel :
        PlayerModelBase<
            PlayerModel,
            PlayerStatisticsCore
            >
    {
        public const int TicksPerSecond = 2;
        protected override int GetTicksPerSecond() => TicksPerSecond;

        [IgnoreDataMember] public IPlayerModelServerListener ServerListener { get; set; } = EmptyPlayerModelServerListener.Instance;
        [IgnoreDataMember] public IPlayerModelClientListener ClientListener { get; set; } = EmptyPlayerModelClientListener.Instance;

        // Player profile
        [MetaMember(100)] public sealed override EntityId           PlayerId    { get; set; }
        [MetaMember(101), NoChecksum] public sealed override string PlayerName  { get; set; }
        [MetaMember(102)] public sealed override int                PlayerLevel { get; set; }

        // Game-specific state

        [MetaMember(299)] public List<Planets> PlanetList { get; set; }

        [MetaMember(300)] public RandomPCG random {  get; set; }

        [MetaMember(301)] public int gameState { get; set; } // 1 = ongoing, 2 = lost, 3 = win

        [MetaMember(302)] public int wins {  get; set; }
        [MetaMember(303)] public int losses {  get; set; }

        [MetaSerializable]
        public class Planets
        {
            [MetaMember(300)] public int Population { get; set; }
            [MetaMember(301)] public int MaxPopulation { get; set; }
            [MetaMember(302)] public int Ownership { get; set; } = 1;

        }

        protected override void GameInitializeNewPlayerModel(MetaTime now, ISharedGameConfig gameConfig, EntityId playerId, string name)
        {
            // Setup initial state for new player
            PlayerId    = playerId;
            PlayerName  = name;
            PlanetList = new List<Planets>()
            {
                new Planets() {Population = 25, MaxPopulation = 100, Ownership = 1},
                new Planets() {Population = 25, MaxPopulation = 100, Ownership = 1},
                new Planets() {Population = 25, MaxPopulation = 100, Ownership = 1},
                new Planets() {Population = 0, MaxPopulation = 100, Ownership = 2},
                new Planets() {Population = 25, MaxPopulation = 100, Ownership = 3},
                new Planets() {Population = 25, MaxPopulation = 100, Ownership = 3},
                new Planets() {Population = 25, MaxPopulation = 100, Ownership = 3},

            };

            random = RandomPCG.CreateNew();
            gameState = 1;
        }

        protected override void GameTick(IChecksumContext checksumCtx)
        {

            

            int ownPlanets = 0;
            int neutralPlanets = 0;
            int enemyPlanets = 0;
            foreach (Planets p in PlanetList)
            {
                if (p.Ownership != 2)
                {
                    p.Population += 1;

                }
                if (p.Population > p.MaxPopulation)
                {
                    p.Population = p.MaxPopulation;
                }
                if (p.Ownership == 1)
                {
                    ownPlanets++;
                }
                else if (p.Ownership == 2)
                {
                    neutralPlanets++;
                }
                else 
                { 
                    enemyPlanets++; 
                }
            }
            

            int planetIndex = 0;
            foreach (Planets p in PlanetList) 
            { 
                if (p.Ownership == 3)
                {
                    if(p.Population / (float) p.MaxPopulation >= 0.50)
                    {
                        int randomPlanet = random.NextInt(PlanetList.Count);
                        if(randomPlanet != planetIndex && PlanetList[randomPlanet].Ownership != 3) 
                        {
                            sendShip(planetIndex, randomPlanet);
                        }
                        
                    }
                }
                planetIndex++;
            }
            if (ownPlanets == 7)
            {
                Log.Information("Win!");
                gameState = 3;
                wins++;
                ClientListener.manageText(gameState);
                restartGame();
                
            }
            else if (ownPlanets == 0)
            {
                Log.Information("Loss");
                gameState = 2;
                losses++;
                ClientListener.manageText(gameState);
                restartGame();
            }
            else
            {
                gameState = 1;
                ClientListener.manageText(gameState);
            }

        }

        public void restartGame()
        {
            
            PlanetList = new List<Planets>()
            {
                new Planets() {Population = 25, MaxPopulation = 100, Ownership = 1},
                new Planets() {Population = 25, MaxPopulation = 100, Ownership = 1},
                new Planets() {Population = 25, MaxPopulation = 100, Ownership = 1},
                new Planets() {Population = 0, MaxPopulation = 100, Ownership = 2},
                new Planets() {Population = 25, MaxPopulation = 100, Ownership = 3},
                new Planets() {Population = 25, MaxPopulation = 100, Ownership = 3},
                new Planets() {Population = 25, MaxPopulation = 100, Ownership = 3},

            };
            gameState = 1;
            Log.Information("Game restarted");

        }

        public void sendShip(int firstClickedPlanet, int secondClickedPlanet)
        {
            int SpaceshipPopulation = PlanetList[firstClickedPlanet].Population / 2;
            int spaceshipOwnership = PlanetList[firstClickedPlanet].Ownership;

            PlanetList[firstClickedPlanet].Population /= 2;

            int secondPlanetOwnership = PlanetList[secondClickedPlanet].Ownership;


            //jos lähettäjä on own
            if(spaceshipOwnership == 1)
            {
                if(secondPlanetOwnership == 1)
                {
                    PlanetList[secondClickedPlanet].Population += SpaceshipPopulation;
                }
                else if(secondPlanetOwnership == 2)
                {
                    PlanetList[secondClickedPlanet].Population += SpaceshipPopulation;
                    PlanetList[secondClickedPlanet].Ownership = 1;

                }
                else if(secondPlanetOwnership == 3)
                {
                    if (PlanetList[secondClickedPlanet].Population > SpaceshipPopulation)
                    {
                        PlanetList[secondClickedPlanet].Population -= SpaceshipPopulation;
                    }
                    else if(PlanetList[secondClickedPlanet].Population < SpaceshipPopulation)
                    {
                        PlanetList[secondClickedPlanet].Population = SpaceshipPopulation - PlanetList[secondClickedPlanet].Population;
                        PlanetList[secondClickedPlanet].Ownership = 1;
                    }
                    else if(PlanetList[secondClickedPlanet].Population == SpaceshipPopulation)
                    {
                        PlanetList[secondClickedPlanet].Population = 0;
                        PlanetList[secondClickedPlanet].Ownership = 2;
                    }
                }
            }

            //jos lähtettäjä on enemy
            else if(spaceshipOwnership == 3)
            {
                if(secondPlanetOwnership == 1)
                {
                    if(PlanetList[secondClickedPlanet].Population > SpaceshipPopulation)
                    {
                        PlanetList[secondClickedPlanet].Population -= SpaceshipPopulation;
                    }
                    else if(PlanetList[secondClickedPlanet].Population < SpaceshipPopulation)
                    {
                        PlanetList[secondClickedPlanet].Population = SpaceshipPopulation - PlanetList[secondClickedPlanet].Population;
                        PlanetList[secondClickedPlanet].Ownership = 3;
                    }
                    else if(PlanetList[secondClickedPlanet].Population == SpaceshipPopulation)
                    {
                        PlanetList[secondClickedPlanet].Population = 0;
                        PlanetList[secondClickedPlanet].Ownership = 2;
                    }
                }
                else if(secondPlanetOwnership == 2)
                {
                    PlanetList[secondClickedPlanet].Population += SpaceshipPopulation;
                    PlanetList[secondClickedPlanet].Ownership = 3;
                }
                else if(secondPlanetOwnership == 3)
                {
                    PlanetList[secondClickedPlanet].Population += SpaceshipPopulation;
                }
                
            }    

            ClientListener.onShipSent(firstClickedPlanet, secondClickedPlanet, SpaceshipPopulation);
        }
        

        #region Schema migrations

        // Example migration from schema v1 to v2
        //[MigrationFromVersion(1)]
        //void Migrate1To2()
        //{
        //    NumClicks += 10;
        //}

        #endregion
    }

    
}

