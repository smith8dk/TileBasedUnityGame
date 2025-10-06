using UnityEngine;

public class PlayerSpawnManager : MonoBehaviour
{
    [SerializeField] private GameObject playerPrefab; // Reference to the player prefab

    public void SpawnPlayer(Vector3 position)
    {
        Instantiate(playerPrefab, position, Quaternion.identity);
    }
}
