
using System.Collections.Generic;
using UnityEngine;

public class InterestManager : MonoBehaviour
{
    public static InterestManager Instance { get; private set; }

    [Header("Spawning")]
    [SerializeField] private InterestObject interestPrefab;
    [SerializeField] private int maxActiveInterests = 5;
    [SerializeField] private float spawnWidth = 20f;
    [SerializeField] private float spawnHeight = 20f;

    private readonly List<InterestObject> activeInterests =
        new List<InterestObject>();

    public int ActiveCount => activeInterests.Count;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    public void SpawnInterest()
    {
        activeInterests.RemoveAll(item => item == null);

        if (interestPrefab == null ||
            activeInterests.Count >= maxActiveInterests)
            return;

        Vector3 position = new Vector3(
            Random.Range(-spawnWidth, spawnWidth),
            0.5f,
            Random.Range(-spawnHeight, spawnHeight)
        );

        InterestObject interest = Instantiate(
            interestPrefab, position, Quaternion.identity);

        activeInterests.Add(interest);
    }

    public void RemoveInterest(InterestObject interest)
    {
        if (interest == null)
            return;

        activeInterests.Remove(interest);
        Destroy(interest.gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
}

