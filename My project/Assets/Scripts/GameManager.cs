using Game.Logic;
using Metaplay.Unity;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using DG.Tweening.Core.Easing;

// This file contains Metaplay sample code. It can be adapted to suit your project's needs or you can
// replace the functionality completely with your own.
namespace Metaplay.Sample
{
    /// <summary>
    /// Represents the in-game application logic. Only gets spawned after a session has been
    /// established with the server, so we can assume all the state has been setup already.
    /// </summary>
    public class GameManager : MonoBehaviour, IPlayerModelClientListener
    {

        public List<planets> planetList;
        public GameObject shipPrefab;

        public GameObject winText;
        public GameObject loseText;

        public TMP_Text winsText;
        public TMP_Text lossesText;

        planets firstClickedPlanet;



        public void manageText(int gameState)
        {
            switch (gameState)
            {
                case 1:
                    winText.SetActive(false);
                    loseText.SetActive(false);

                    break;
                case 2:
                    EnableAndDisable(loseText);
                    break;
                case 3:
                    EnableAndDisable(winText);
                    break;
                default:
                    Debug.LogError("Invalid variable value. It should be 1, 2, or 3.");
                    break;
            }
        }


        void EnableAndDisable(GameObject targetGameObject)
        {
            // Enable the game object
            targetGameObject.SetActive(true);

            // Wait for 2 seconds
            StartCoroutine(DisableAfterDelay(targetGameObject, 4f));
        }

        System.Collections.IEnumerator DisableAfterDelay(GameObject targetGameObject, float delay)
        {
            yield return new WaitForSeconds(delay);

            // Disable the game object after the delay
            targetGameObject.SetActive(false);
        }

        public void onPlanetClick(planets clickedPlanet)
        {
            if(firstClickedPlanet == null )
            {
                firstClickedPlanet = clickedPlanet;
            }
            else
            {
                Spaceship ship = new Spaceship(firstClickedPlanet.planetIndex, clickedPlanet.planetIndex);
                MetaplayClient.PlayerContext.ExecuteAction(ship);
                firstClickedPlanet = null;
            }
        }

        void Start()
        {
            
        }

        void Update()
        {
            winsText.text = string.Format("Wins: {0}", MetaplayClient.PlayerModel.wins);
            lossesText.text = string.Format("Losses: {0}", MetaplayClient.PlayerModel.losses);

        }

        public void AddPlanet(planets planets)
        {
            planetList.Add(planets);

        }

        public void onShipSent(int sourcePlanet, int destinationPlanet, int SpaceshipPopulation)
        {
            Debug.Log("ship sent lol");
            Instantiate(shipPrefab).GetComponent<spaceship>().Attack(planetList.Find((p)=>p.planetIndex==sourcePlanet), planetList.Find((p) => p.planetIndex == destinationPlanet), SpaceshipPopulation);
        }
    }
}