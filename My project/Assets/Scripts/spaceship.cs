using System.Collections;
using TMPro;
using UnityEngine;
using Random = UnityEngine.Random;

public class spaceship : MonoBehaviour
{

    public GameManager gameManager;
    public planets _sourcePlanet;
    public planets _destinationPlanet;
    public SpriteRenderer spriteRenderer_spaceship;
    public TMP_Text spaceship_populationText;
    public int spaceship_population;
    public float duration = 2.0f;
    public void onMovement()
    {
        if (_sourcePlanet.ownership == planets.Ownership.Own)
        {
            spaceship_population = _sourcePlanet.planet_population / 2;
            _sourcePlanet.planet_population = _sourcePlanet.planet_population / 2;

            StartCoroutine(moveSpaceship());
        }
    }

    public void aiAttack(planets sourcePlanet, planets targetPlanet)
    {
        Debug.Log("ATTACK STARTED");
        _sourcePlanet = sourcePlanet;
        _destinationPlanet = targetPlanet;
        
        StartCoroutine(moveSpaceship());
    }
    void Start()
    {
        gameManager = GameObject.Find("GameManager").GetComponent<GameManager>();

    }

    void Update()
    {
        spaceship_populationText.text = string.Format("Population: {0}", spaceship_population);
    }

    IEnumerator aiMoveSpaceship()
    {
        spriteRenderer_spaceship.enabled = true;
        spaceship_populationText.enabled = true;
        Vector3 startPosition = _sourcePlanet.planet_position;
        Vector3 endPosition = _destinationPlanet.planet_position;
        float elapsedTime = 0.0f;
        Vector2 direction = (endPosition - transform.position).normalized;



        while (elapsedTime < duration)
        {
            float t = elapsedTime / duration;
            transform.position = Vector2.Lerp(startPosition, endPosition, t);
            elapsedTime += Time.deltaTime;
            yield return null;

        }
    }
    IEnumerator moveSpaceship()
    {
        

        spriteRenderer_spaceship.enabled = true;
        spaceship_populationText.enabled = true;
        Vector3 startPosition = _sourcePlanet.planet_position;
        Vector3 endPosition = _destinationPlanet.planet_position;
        float elapsedTime = 0.0f;
        Vector2 direction = (endPosition - transform.position).normalized;
        
        

        while (elapsedTime < duration)
        {
            float t = elapsedTime / duration;
            transform.position = Vector2.Lerp(startPosition, endPosition, t);
            elapsedTime += Time.deltaTime;
            yield return null;

        }

        spaceshipAtTarget();

    }

    public void spaceshipAtTarget()
    {
        if (gameManager.spaceshipOwnership == 1)
        {
            if (_destinationPlanet.ownership == planets.Ownership.Own)
            {
                _destinationPlanet.planet_population = _destinationPlanet.planet_population + spaceship_population;
                spaceship_population = 0;
                _destinationPlanet.ownership = planets.Ownership.Own;
                spriteRenderer_spaceship.enabled = false;
                spaceship_populationText.enabled = false;
            }

            else if(_destinationPlanet.ownership == planets.Ownership.Neutral)
            {
                if (gameManager.spaceshipOwnership == 1)
                {
                    gameManager.ownPlanets += 1;
                }
                _destinationPlanet.planet_population = _destinationPlanet.planet_population + spaceship_population;
                spaceship_population = 0;
                _destinationPlanet.ownership = planets.Ownership.Own;
                spriteRenderer_spaceship.enabled = false;
                spaceship_populationText.enabled = false;
            }

            else if (_destinationPlanet.ownership == planets.Ownership.Enemy)
            {
                spriteRenderer_spaceship.enabled = false;
                spaceship_populationText.enabled = false;

                if (_destinationPlanet.planet_population > spaceship_population)
                {
                    _destinationPlanet.planet_population = _destinationPlanet.planet_population - spaceship_population;
                }

                else if (_destinationPlanet.planet_population == spaceship_population)
                {
                    _destinationPlanet.planet_population = 0;
                    _destinationPlanet.ownership = planets.Ownership.Neutral;
                }
                else if (_destinationPlanet.planet_population < spaceship_population)
                {
                    if (gameManager.spaceshipOwnership == 1)
                    {
                        gameManager.ownPlanets += 1;
                    }
                    _destinationPlanet.planet_population = spaceship_population - _destinationPlanet.planet_population;
                    _destinationPlanet.ownership = planets.Ownership.Own;
                }
                
            }
        }

        else if (gameManager.spaceshipOwnership == 2) 
        {
            if (_destinationPlanet.ownership == planets.Ownership.Enemy)
            {
                _destinationPlanet.planet_population = _destinationPlanet.planet_population + spaceship_population;
                spaceship_population = 0;
                _destinationPlanet.ownership = planets.Ownership.Enemy;
                spriteRenderer_spaceship.enabled = false;
                spaceship_populationText.enabled = false;
            }

            else if (_destinationPlanet.ownership == planets.Ownership.Neutral)
            {
                _destinationPlanet.planet_population = _destinationPlanet.planet_population + spaceship_population;
                spaceship_population = 0;
                _destinationPlanet.ownership = planets.Ownership.Enemy;
                spriteRenderer_spaceship.enabled = false;
                spaceship_populationText.enabled = false;
            }

            else if (_destinationPlanet.ownership == planets.Ownership.Own)
            {
                spriteRenderer_spaceship.enabled = false;
                spaceship_populationText.enabled = false;

                if (_destinationPlanet.planet_population > spaceship_population)
                {
                    _destinationPlanet.planet_population = _destinationPlanet.planet_population - spaceship_population;
                }

                else if (_destinationPlanet.planet_population == spaceship_population)
                {
                    _destinationPlanet.planet_population = 0;
                    _destinationPlanet.ownership = planets.Ownership.Neutral;
                }
                else if (_destinationPlanet.planet_population < spaceship_population)
                {
                    if (gameManager.spaceshipOwnership == 2)
                                        {
                                            gameManager.ownPlanets -= 1;
                                        }
                    _destinationPlanet.planet_population = spaceship_population - _destinationPlanet.planet_population;
                    _destinationPlanet.ownership = planets.Ownership.Enemy;

                    
                }
            }
        }
       
    }
}
