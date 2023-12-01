using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Events;
using System;
using Random = UnityEngine.Random;
using Metaplay.Sample;

public class planets : MonoBehaviour, IPointerClickHandler
{

    public int planetIndex;
    

    public planets targetPlanet;

    public GameManager gameManager;

    public bool isClicked1;
    public bool isClicked2;


    public Vector2 clickPosition1;
    public Vector2 clickPosition2;

    
    public TMP_Text planet_populationText;
    public TMP_Text planet_max_populationText;


    public SpriteRenderer sprite_planet;




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
                
                gameManager.onPlanetClick(this);


            }
        }
    }






    


    void Start()
    {
         
        gameManager = FindAnyObjectByType<GameManager>();

        sprite_planet = GetComponent<SpriteRenderer>();

        gameManager.AddPlanet(this);


        //List<planets> pL = gameManager.planetList;
        //List<GameObject> spL = gameManager.selectedPlanetsList;


    }   

    void Update()
    {
        int ownership = MetaplayClient.PlayerModel.PlanetList[planetIndex].Ownership;


        int planet_population = MetaplayClient.PlayerModel.PlanetList[planetIndex].Population;
        int planet_max_population = MetaplayClient.PlayerModel.PlanetList[planetIndex].MaxPopulation;

        planet_position = trackedObject.position;

        planet_populationText.text = string.Format("{0}", planet_population);
        planet_max_populationText.text = string.Format("Max: {0}", planet_max_population);


        
        

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


        if (ownership == 1)
        {
            sprite_planet.color = (new Color32(0, 57, 255, 255));
        }
        else if (ownership == 2)
        {
            sprite_planet.color = (new Color32(137, 20, 92, 255));
        }
        else if (ownership == 3)
        {
            sprite_planet.color = (new Color32(255, 0, 29, 255));
        }

       

       
    }

   

}

