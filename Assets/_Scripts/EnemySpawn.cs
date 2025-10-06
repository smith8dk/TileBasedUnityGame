using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class EnemySpawn : MonoBehaviour
{
    [SerializeField] private GameObject[] enemyPrefabs; // Array of enemy prefabs to choose from
    [SerializeField] private int minEnemies = 3; // Minimum number of enemies to spawn
    [SerializeField] private int maxEnemies = 6; // Maximum number of enemies to spawn
    [SerializeField] [Range(0f, 1f)] private float enemyDensity = 0.5f; // Density of enemies in the dungeon

    private void Start()
    {
        // Find the dungeon generator
        CorridorFirstDungeonGenerator dungeonGenerator = FindObjectOfType<CorridorFirstDungeonGenerator>();
        if (dungeonGenerator != null)
        {

        }
        else
        {
            Debug.LogError("EnemySpawn: Dungeon generator not found!");
        }
    }

    public List<GameObject> SpawnEnemies(HashSet<Vector2Int> floorTilePositions)
    {
        List<GameObject> spawnedEnemies = new List<GameObject>();

        // Calculate the number of enemies to spawn based on density
        int totalEnemies = Mathf.RoundToInt(floorTilePositions.Count * enemyDensity);
        int enemiesToSpawn = Random.Range(minEnemies, Mathf.Min(totalEnemies, maxEnemies) + 1);

        // Randomly select positions to spawn enemies
        Vector2Int[] spawnPositions = floorTilePositions.ToArray();
        ShuffleArray(spawnPositions); // Shuffle the array to randomize spawn positions

        for (int i = 0; i < enemiesToSpawn && i < spawnPositions.Length; i++)
        {
            Vector2Int spawnPosition = spawnPositions[i];

            // Randomly select an enemy prefab from the array
            GameObject enemyPrefab = enemyPrefabs[Random.Range(0, enemyPrefabs.Length)];

            // Instantiate the selected enemy prefab at the spawn position
            GameObject enemy = Instantiate(enemyPrefab, new Vector3(spawnPosition.x + 0.5f, spawnPosition.y + 0.5f, 0f), Quaternion.identity);
            spawnedEnemies.Add(enemy);
        }

        return spawnedEnemies;
    }


    private void ShuffleArray(Vector2Int[] array)
    {
        // Fisher-Yates shuffle algorithm
        for (int i = array.Length - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            Vector2Int temp = array[i];
            array[i] = array[j];
            array[j] = temp;
        }
    }
}
