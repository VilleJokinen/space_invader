using Metaplay.Sample;
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
    

    public void Attack(planets sourcePlanet, planets targetPlanet, int SpaceshipPopulation)
    {
        Debug.Log("ATTACK STARTED");
        _sourcePlanet = sourcePlanet;
        _destinationPlanet = targetPlanet;
        spaceship_population = SpaceshipPopulation;

        StartCoroutine(moveSpaceship());
    }
    void Start()
    {
        gameManager = FindAnyObjectByType<GameManager>();

    }

    void Update()
    {
        spaceship_populationText.text = string.Format("{0}", spaceship_population);
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

        
        Destroy(gameObject);
    }

    

    
}
