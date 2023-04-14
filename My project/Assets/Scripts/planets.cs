using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Events;
using System;
using Random = UnityEngine.Random;

public class planets : MonoBehaviour, IPointerClickHandler
{
    
    public double percentageFull;
    public planets targetPlanet;

    public GameManager gameManager;
    public planets randomPlanet;
    public double tresholdPercentage = 80.00;
    public bool isClicked1;
    public bool isClicked2;


    public Vector2 clickPosition1;
    public Vector2 clickPosition2;

    public int planet_population;
    public int planet_max_population;
    public TMP_Text planet_populationText;
    public TMP_Text ownershipText;
    public TMP_Text planet_max_populationText;

    public TMP_Text planet_positionText;

    SpriteRenderer sprite_planet;


    private float timer = 0.0f;

    public Transform trackedObject;
    public Vector2 planet_position;

    public event Action OnGameObjectClicked;

    public float planet_size = 1.0f;

    public void Awake()
    {

    }
    public void OnPointerClick(PointerEventData eventData)
    {



        Vector2 rayOrigin = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        RaycastHit2D hit = Physics2D.Raycast(rayOrigin, Vector2.zero);



        if (hit.collider != null)
        {
            if (hit.collider.gameObject == gameObject)
            {

                OnGameObjectClicked?.Invoke();
                gameManager.OnPlanetClicked(this);



            }
        }
    }


    public enum Ownership
    {
        Own,
        Neutral,
        Enemy
    };



    public Ownership ownership;


    void Start()
    {
         
        gameManager = GameObject.Find("GameManager").GetComponent<GameManager>();

        sprite_planet = GetComponent<SpriteRenderer>();

        gameManager.AddPlanet(this);


        //List<planets> pL = gameManager.planetList;
        //List<GameObject> spL = gameManager.selectedPlanetsList;


    }   

    void Update()
    {

        planet_position = trackedObject.position;

        planet_populationText.text = string.Format("Population: {0}", planet_population);
        ownershipText.text = string.Format("Ownership: {0}", ownership);
        planet_max_populationText.text = string.Format("Max population: {0}", planet_max_population);
        planet_positionText.text = string.Format("Position: {0}", planet_position);


        timer += Time.deltaTime;

        if(timer >= 2.0f && ownership == Ownership.Own)
        {


            onPopulationGrowth();
        }

        else if(timer >= 3.0f && ownership == Ownership.Enemy)
        {
            onPopulationGrowth();

        }

        planet_size = planet_max_population;

        if (planet_max_population >= 1 && planet_max_population <= 10)
        {
            planet_size = 1.0f;
            transform.localScale = new Vector2(planet_size, planet_size);
        }

        else if(planet_max_population >= 11 && planet_max_population <= 20)
        {
            planet_size = 1.1f;
            transform.localScale = new Vector2(planet_size, planet_size);
        }

        else if (planet_max_population >= 21 && planet_max_population <= 35)
        {
            planet_size = 1.2f;
            transform.localScale = new Vector2(planet_size, planet_size);
        }

        else if (planet_max_population >= 36 && planet_max_population <= 50)
        {
            planet_size = 1.3f;
            transform.localScale = new Vector2(planet_size, planet_size);
        }

        else if(planet_max_population >= 51 && planet_max_population <= 65)
        {
            planet_size = 1.4f;
            transform.localScale = new Vector2(planet_size, planet_size);
        }
        else if (planet_max_population >= 66 && planet_max_population <= 80)
        {
            planet_size = 1.5f;
            transform.localScale = new Vector2(planet_size, planet_size);
        }
        else if (planet_max_population >= 81 && planet_max_population <= 99)
        {
            planet_size = 1.6f;
            transform.localScale = new Vector2(planet_size, planet_size);
        }
        else if (planet_max_population >= 100)
        {
            planet_size = 1.7f;
            transform.localScale = new Vector2(planet_size, planet_size);

        }

        

        if (ownership == Ownership.Own)
        {
            sprite_planet.color = (new Color32(0, 57, 255, 255));
        }
        else if (ownership == Ownership.Neutral)
        {
            sprite_planet.color = (new Color32(137, 20, 92, 255));
        }
        else if (ownership == Ownership.Enemy)
        {
            sprite_planet.color = (new Color32(255, 0, 29, 255));
        }

        if (planet_population > planet_max_population)
        {
            planet_population = planet_max_population;
        }

        percentageFull = (100 * planet_population) / planet_max_population;

        
        if(percentageFull >= tresholdPercentage && ownership == Ownership.Enemy)
        {

            //gameManager.planetList;
            //foreach(var planet in gameManager.planetList)
            //{

            //}

            generateRandomPlanet();
            
            
            gameManager.Attack(this, randomPlanet);
        }

    }

    void generateRandomPlanet()
    {
        var index = Random.RandomRange(0, gameManager.planetList.Count);
        randomPlanet = gameManager.planetList[index];
        if (randomPlanet == this)
        {
            generateRandomPlanet();
        }
    }
    void onPopulationGrowth()
    {
        if (planet_population > 0)
        {
            planet_population += 1;
        }

        timer = 0.0f;
    }

}

