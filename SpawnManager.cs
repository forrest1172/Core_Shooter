using UnityEngine;

public class SpawnManager : MonoBehaviour
{
    public static SpawnManager Instance;

    public SpawnPoint[] spawnPoints;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void Start()
    {
        // Cache all spawnpoints once
        spawnPoints = FindObjectsByType<SpawnPoint>(FindObjectsSortMode.None);
    }

    // Your existing method (kept)
    public Transform GetNextSpawn()
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogError("No SpawnPoints found");
            return null;
        }

        int randNum = Random.Range(0, spawnPoints.Length);
        // Debug.Log($"SpawnManager selected spawn index {randNum}");
        return spawnPoints[randNum].transform;
    }

    // NEW: convenience for respawn code
    public bool TryGetSpawnPose(out Vector3 pos, out Quaternion rot)
    {
        pos = Vector3.zero;
        rot = Quaternion.identity;

        Transform t = GetNextSpawn();
        if (t == null) return false;

        pos = t.position;
        rot = t.rotation;
        return true;
    }
}
