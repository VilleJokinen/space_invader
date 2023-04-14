using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using TMPro;


public class GameManager : MonoBehaviour
{
    public List<planets> planetList;
    public List<GameObject> selectedPlanetsList;

    public spaceship spaceshipPrefab;

    public planets firstClickedPlanet;
    public planets secondClickedPlanet;

    public int spaceshipOwnership;

    public TMP_Text winText;
    public TMP_Text loseText;

    public int ownPlanets;
    public int neutralPlanets;
    public int enemyPlanets;
    public enum GameState
    {
        win,
        lose,
        ongoing
    }

    public GameState gameState;
    public void OnPlanetClicked(planets planet)
    {

        if (firstClickedPlanet == null)
        {
            
            
            firstClickedPlanet = planet;
            

        }
        else if (secondClickedPlanet == null)
        {

            secondClickedPlanet = planet;
            if(firstClickedPlanet == secondClickedPlanet)
            {
                firstClickedPlanet = null;
                secondClickedPlanet = null;
                return;
            }
            //var spaceship = Instantiate(spaceshipPrefab);

            //spaceship.aiAttack(firstClickedPlanet, secondClickedPlanet);
            Attack(firstClickedPlanet, secondClickedPlanet);

            //spaceship.onMovement();

            firstClickedPlanet = null;
            secondClickedPlanet = null;


        }
    }


    void Start()
    {
        ownPlanets = 3;
        neutralPlanets = 1;
        enemyPlanets = 3;
        gameState = GameState.ongoing;
        planets clickHandler = FindObjectOfType<planets>();
        

    }

    void Update()
    {
        if(gameState == GameState.ongoing)
        {
            //check if win or lose
            if(ownPlanets == 7)
            {
                gameState = GameState.win;
            }
            else if(ownPlanets == 0)
            {
                gameState = GameState.lose;
            }
        }

        switch (gameState)
        {
            case GameState.win:
                winText.gameObject.SetActive(true);
                loseText.gameObject.SetActive(false);
                break;

            case GameState.lose:
                loseText.gameObject.SetActive(true);
                winText.gameObject.SetActive(false);
                break;

            case GameState.ongoing:
                loseText.gameObject.SetActive(false);
                winText.gameObject.SetActive(false);
                break;
            default:
                break;

        }
    }

    public void AddPlanet(planets planets)
    {
        planetList.Add(planets);
        
    }
    public float duration = 2.0f;
    internal void Attack(planets sourcePlanet, planets targetPlanet)
    {
        //var index = Random.RandomRange(0, gameManager.planetList.Count);
        //randomPlanet = gameManager.planetList[index];
        //spaceship.sourcePlanet = this;
        //spaceship.aiAttack();



        var spaceship = Instantiate(spaceshipPrefab);
        
        spaceship.spaceship_population = sourcePlanet.planet_population / 2;
        sourcePlanet.planet_population = sourcePlanet.planet_population / 2;
        if (sourcePlanet.ownership == planets.Ownership.Own)
        {
            spaceshipOwnership = 1; //own
        }
        else if(sourcePlanet.ownership == planets.Ownership.Enemy)
        {
            spaceshipOwnership = 2; //enemy
        }
        spaceship.aiAttack(sourcePlanet, targetPlanet);
        
    }
}
