using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[Serializable]
public struct PropEntry
{
    public GameObject prefab;
    [Range(0f,1f), Tooltip("Fraction of room tiles to fill with this prop")]
    public float spawnRate;
}

public class CorridorFirstDungeonGenerator : SimpleRandomWalkDungeonGenerator
{
    [Header("Corridor Settings")]
    [SerializeField] private int corridorLength = 14, corridorCount = 5;
    [SerializeField][Range(0.1f,1)] private float roomPercent = 0.8f;

    [Header("References")]
    [SerializeField] private PlayerController playerController;
    [SerializeField] private EnemySpawn enemySpawn;
    [SerializeField] private GridRescan gridRescan;
    [SerializeField] private GameObject minimapObjectPrefab;

    [Header("Prop Spawning")]
    [Tooltip("List of all prop types & how densely to spawn them")]
    [SerializeField] private List<PropEntry> propEntries;

    private List<GameObject> spawnedProps = new List<GameObject>();

    [Header("Per-Room Wall Fill")]
    [Tooltip("Padding added around a room when filling empty cells (in tiles)")]
    [SerializeField] private int fillPadding = 2;
    [Tooltip("Minimum fill probability for a room (0..1)")]
    [Range(0f,1f)]
    [SerializeField] private float fillPerRoomMin = 0.8f;
    [Tooltip("Maximum fill probability for a room (0..1)")]
    [Range(0f,1f)]
    [SerializeField] private float fillPerRoomMax = 1f;

    private Dictionary<Vector2Int, HashSet<Vector2Int>> roomsDictionary
        = new Dictionary<Vector2Int, HashSet<Vector2Int>>();
    private List<Color> roomColors = new List<Color>();
    private HashSet<Vector2Int> corridorPositions;

    [Header("Loading")]
    public LoadingScreen loadingScreen; // assign in inspector (optional)

    private Vector2Int exitTilePosition;
    private GameObject exitTileObject;

    private List<GameObject> spawnedEnemies = new List<GameObject>();
    public event Action OnDungeonGenerationCompleted;

    /// <summary>
    /// Public entry point to start generation (uses loading screen coroutine internally).
    /// Call this from other scripts.
    /// </summary>
    public void GenerateDungeon()
    {
        // optional: prevent overlapping generation requests
        StopAllCoroutines();

        StartCoroutine(GenerateWithLoadingCoroutine());
    }

    // Called by the base class when procedural generation should run.
    // We now start a coroutine so the loading screen can animate.
    protected override void RunProceduralGeneration()
    {
        StartCoroutine(GenerateWithLoadingCoroutine());
    }

    /// <summary>
    /// Wrapper that shows the loading screen while running the generation coroutine.
    /// Falls back to running the coroutine directly if loadingScreen is null.
    /// </summary>
    private IEnumerator GenerateWithLoadingCoroutine()
    {
        if (loadingScreen != null)
        {
            // Use the LoadingScreen helper (expected signature: ShowWhile(IEnumerator, float))
            // If your LoadingScreen API differs, replace with FadeIn/FadeOut calls.
            yield return StartCoroutine(loadingScreen.ShowWhile(CorridorFirstGenerationCoroutine(), loadingScreen.defaultFadeDuration));
        }
        else
        {
            yield return StartCoroutine(CorridorFirstGenerationCoroutine());
        }
    }

    private IEnumerator CorridorFirstGenerationCoroutine()
    {
        // destroy any props from the previous dungeon
        foreach (var go in spawnedProps)
            DestroyImmediate(go);
        spawnedProps.Clear();

        // --- NEW: destroy any leftover "Item Drop" objects from previous floor ---
        var itemDrops = GameObject.FindGameObjectsWithTag("Item Drop");
        if (itemDrops != null && itemDrops.Length > 0)
        {
            Debug.Log($"[DungeonGen] Removing {itemDrops.Length} item drop object(s) from previous floor.");
            foreach (var it in itemDrops)
            {
                if (it == null) continue;
                // Destroy (allow OnDestroy to run)
                Destroy(it);
            }

            // Let Unity process destroy messages before continuing generation
            yield return null;
        }

        // Find all Spell components in the scene (this includes TeleportSpell and other children).
        var allSpells = FindObjectsOfType<Spell>();
        if (allSpells != null && allSpells.Length > 0)
        {
            Debug.Log($"[DungeonGen] Removing {allSpells.Length} spell object(s) from previous floor.");
            foreach (var sp in allSpells)
            {
                if (sp == null) continue;
                // Destroy the root GameObject of the spell; Spell.OnDestroy will remove it from any static lists.
                Destroy(sp.gameObject);
            }

            // Let Unity process destroy messages (OnDestroy runs) before continuing generation.
            yield return null;
        }

        tilemapVisualizer.Clear();

        // small yield to let UI update
        yield return null;

        var floorPositions = new HashSet<Vector2Int>();
        var potentialRoomPositions = new HashSet<Vector2Int>();

        CreateCorridors(floorPositions, potentialRoomPositions);

        // yield so the overlay becomes visible
        yield return null;

        var roomPositions = CreateRooms(potentialRoomPositions);

        // yield
        yield return null;

        var deadEnds = FindAllDeadEnds(floorPositions);
        CreateRoomsAtDeadEnd(deadEnds, roomPositions);
        floorPositions.UnionWith(roomPositions);

        // Paint floors
        tilemapVisualizer.PaintFloorTiles(floorPositions);

        // Let a frame pass so floor painting renders
        yield return null;

        // -------------------------------
        // NEW: Choose a spawn room *before* selecting an exit room / spawning enemies.
        // This ensures exit selection can avoid the spawn room and enemies can be filtered out.
        // -------------------------------
        Vector2Int spawnRoom = Vector2Int.zero;
        HashSet<Vector2Int> spawnRoomTiles = null;
        if (roomsDictionary != null && roomsDictionary.Count > 0)
        {
            // pick a random room center as spawn room
            spawnRoom = roomsDictionary.Keys.ElementAt(UnityEngine.Random.Range(0, roomsDictionary.Count));
            spawnRoomTiles = roomsDictionary.ContainsKey(spawnRoom) ? roomsDictionary[spawnRoom] : null;
            Debug.Log($"[DungeonGen] Chosen spawn room center: {spawnRoom} (tiles: {(spawnRoomTiles?.Count ?? 0)})");
        }
        else
        {
            Debug.LogWarning("[DungeonGen] No rooms found to choose spawn room from.");
        }

        // -------------------------------
        // Choose and paint exit tile, but avoid the spawn room unless it's the only room
        // -------------------------------
        if (roomsDictionary == null || roomsDictionary.Count == 0)
        {
            Debug.LogWarning("No rooms found when selecting exit tile.");
        }
        else
        {
            Vector2Int exitRoomCenter;

            if (roomsDictionary.Count == 1)
            {
                // only one room — allow exit in the spawn room (since it's the only room)
                exitRoomCenter = roomsDictionary.Keys.ElementAt(0);
            }
            else
            {
                // choose a random room center that is NOT the spawn room
                var possibleExitRooms = roomsDictionary.Keys.Where(k => k != spawnRoom).ToList();
                if (possibleExitRooms.Count == 0)
                {
                    // fallback (shouldn't happen given the Count > 1 check)
                    exitRoomCenter = roomsDictionary.Keys.ElementAt(UnityEngine.Random.Range(0, roomsDictionary.Count));
                }
                else
                {
                    exitRoomCenter = possibleExitRooms.ElementAt(UnityEngine.Random.Range(0, possibleExitRooms.Count));
                }
            }

            exitTilePosition = GetRandomPositionInRoom(exitRoomCenter);
            tilemapVisualizer.PaintSingleTile(
                tilemapVisualizer.floorTilemap,
                tilemapVisualizer.exitTile,
                exitTilePosition);
        }

        // yield
        yield return null;

        // Create walls
        WallGenerator.CreateWalls(floorPositions, tilemapVisualizer);

        // yield after walls painted
        yield return null;

        // Ensure corridors have the two-layer mandatory border (unchanged)
        if (corridorPositions != null && corridorPositions.Count > 0)
        {
            tilemapVisualizer.FillTwoLayerBorderForCells(corridorPositions);
        }

        // yield
        yield return null;

        // Parameters for per-room variable outer fill:
        int roomPadding = 6;               // total padding to consider (>=2 ensures two mandatory layers present)
        float roomFillProbability = 0.15f; // chance per cell in bounding box to place filler wall

        // Ensure every room gets the mandatory two-layer border first
        foreach (var kv in roomsDictionary)
        {
            HashSet<Vector2Int> roomFloor = kv.Value;
            tilemapVisualizer.FillTwoLayerBorderForCells(roomFloor);
        }

        // yield
        yield return null;

        // Now perform per-room variable fill (outside the mandatory two layers)
        int roomIndex = 0;
        int roomsTotal = roomsDictionary.Count;
        foreach (var kv in roomsDictionary)
        {
            HashSet<Vector2Int> roomFloor = kv.Value;
            tilemapVisualizer.FillEmptyWallsForRoom(roomFloor, roomPadding, roomFillProbability);

            // Debug: verify that at least one wall tile exists in the two-layer border
            int wallCount = tilemapVisualizer.CountWallTilesAroundRoom(roomFloor);
            if (wallCount == 0)
            {
                Debug.LogWarning($"Room at key {kv.Key} has ZERO border wall tiles after painting — investigate (room size {roomFloor.Count}).");
            }

            // Yield every few rooms to keep UI responsive (tweak frequency as needed)
            roomIndex++;
            if ((roomIndex & 7) == 0) // every 8 rooms
                yield return null;
        }

        // yield before props
        yield return null;

        // --- PROP SPAWN PASS (interior-only) ---
        int propLoopCounter = 0;
        foreach (var kv in roomsDictionary)
        {
            // Start with all tiles of this room
            var allTiles = kv.Value;

            // Exclude any tile adjacent to a corridor
            var interiorTiles = allTiles
                .Where(tile =>
                    !Direction2D.cardinalDirectionsList
                        .Any(dir => corridorPositions.Contains(tile + dir)))
                .ToList();

            // For each prop type, pick a subset of interior tiles
            foreach (var entry in propEntries)
            {
                if (entry.prefab == null || entry.spawnRate <= 0f)
                    continue;

                int toSpawn = Mathf.RoundToInt(interiorTiles.Count * entry.spawnRate);
                for (int i = 0; i < toSpawn && interiorTiles.Count > 0; i++)
                {
                    int idx = UnityEngine.Random.Range(0, interiorTiles.Count);
                    var cell = interiorTiles[idx];
                    interiorTiles.RemoveAt(idx);

                    Vector3 worldPos = tilemapVisualizer
                        .floorTilemap
                        .GetCellCenterWorld((Vector3Int)cell);

                    // Instantiate _and_ track
                    var obj = Instantiate(entry.prefab, worldPos, Quaternion.identity, transform);
                    spawnedProps.Add(obj);

                    // yield occasionally inside large prop loops
                    propLoopCounter++;
                    if ((propLoopCounter & 31) == 0) // every 32 props
                        yield return null;
                }
            }
        }

        // yield after spawning props
        yield return null;

        // Spawn player in the chosen spawn room (if one exists)
        if (roomsDictionary.Count > 0)
        {
            // spawnRoom and spawnRoomTiles were set earlier
            playerController.SpawnPlayer(spawnRoom);
        }
        else
        {
            Debug.LogWarning("No rooms available to spawn player.");
        }

        // yield
        yield return null;

        // Spawn enemies
        EnemyController.DestroyAllMinimapMarkersAndTargets();
        spawnedEnemies.ForEach(Destroy);
        spawnedEnemies.Clear();
        
    if (enemySpawn != null)
        {
            // Use the enemySpawn to create enemies (returns List<GameObject>)
            List<GameObject> newEnemies = enemySpawn.SpawnEnemies(floorPositions);

            if (newEnemies != null && newEnemies.Count > 0)
            {
                // Only remove enemies that fall inside the spawn room if there is MORE THAN ONE room.
                // If there's only one room, allow enemies to spawn there.
                if (roomsDictionary != null && roomsDictionary.Count > 1
                    && spawnRoomTiles != null && spawnRoomTiles.Count > 0
                    && tilemapVisualizer != null)
                {
                    for (int i = newEnemies.Count - 1; i >= 0; i--)
                    {
                        var e = newEnemies[i];
                        if (e == null) continue;

                        Vector3Int enemyCell = tilemapVisualizer.floorTilemap.WorldToCell(e.transform.position);
                        Vector2Int enemyCell2 = new Vector2Int(enemyCell.x, enemyCell.y);
                        if (spawnRoomTiles.Contains(enemyCell2))
                        {
                            // Destroy and remove from the list returned by the spawner
                            Destroy(e);
                            newEnemies.RemoveAt(i);
                        }
                    }
                }
                else if (roomsDictionary != null && roomsDictionary.Count <= 1)
                {
                    Debug.Log("[DungeonGen] Single-room level: allowing enemies to spawn in the spawn room.");
                }

                // Add remaining enemies to our tracked list
                spawnedEnemies.AddRange(newEnemies);
            }
        }
        else
        {
            Debug.LogWarning("[DungeonGen] EnemySpawn reference is not set; no enemies spawned.");
        }


        // yield
        yield return null;

        // Move or instantiate exit tile objects
        FindAndMoveExitTileObject();

        // yield and let frame complete so visuals update before rescan
        yield return new WaitForEndOfFrame();

        // Delayed graph rescan - keep as a coroutine to yield a frame first
        yield return StartCoroutine(DelayedRescanGraph());

        // Generation complete: fire event
        OnDungeonGenerationCompleted?.Invoke();

        // final yield to ensure last UI update
        yield return null;
    }

    private IEnumerator DelayedRescanGraph()
    {
        // Wait for end of frame to ensure all visual updates are done
        yield return new WaitForEndOfFrame();

        // Now rescan the graph
        RescanGraph();

        yield break;
    }

    private void SpawnEnemies(HashSet<Vector2Int> floorPositions)
    {
        // Ensure that enemySpawn reference is not null
        if (enemySpawn != null)
        {
            foreach (GameObject enemy in spawnedEnemies)
            {
                DestroyImmediate(enemy);
            }

            // Call the SpawnEnemies method from the EnemySpawn script and add the spawned enemies to the list
            List<GameObject> newEnemies = enemySpawn.SpawnEnemies(floorPositions);
            if (newEnemies != null)
            {
                spawnedEnemies.AddRange(newEnemies);
            }
        }
        else
        {
            Debug.LogError("EnemySpawn reference is not set.");
        }
    }

    public HashSet<Vector2Int> GetFloorTilePositions()
    {
        // Collect all floor tile positions from the tilemap visualizer
        HashSet<Vector2Int> floorTilePositions = new HashSet<Vector2Int>();

        // Assuming your tilemapVisualizer has a method to retrieve floor tile positions
        // Modify this according to your actual implementation
        foreach (Vector2Int position in tilemapVisualizer.GetFloorTilePositions())
        {
            floorTilePositions.Add(position);
        }

        return floorTilePositions;
    }

    public List<Vector2Int> FindFarthestPoints(Vector2Int startPosition)
    {
        HashSet<Vector2Int> floorPositions = GetFloorTilePositions();
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        Dictionary<Vector2Int, int> distances = new Dictionary<Vector2Int, int>();

        queue.Enqueue(startPosition);
        distances[startPosition] = 0;

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            foreach (Vector2Int direction in Direction2D.cardinalDirectionsList)
            {
                Vector2Int neighbor = current + direction;
                if (floorPositions.Contains(neighbor) && !distances.ContainsKey(neighbor))
                {
                    distances[neighbor] = distances[current] + 1;
                    queue.Enqueue(neighbor);
                }
            }
        }

        if (distances.Count == 0)
            return new List<Vector2Int>();

        int maxDistance = distances.Values.Max();
        List<Vector2Int> farthestPoints = distances.Where(pair => pair.Value == maxDistance).Select(pair => pair.Key).ToList();
        return farthestPoints;
    }

    private Vector2Int GetRandomPositionInRoom(Vector2Int roomPosition)
    {
        // Adjust this method to return a random position within the given room position
        // For simplicity, you can just return the center of the room
        return roomPosition;
    }

    private void CreateRoomsAtDeadEnd(List<Vector2Int> deadEnds, HashSet<Vector2Int> roomFloors)
    {
        foreach (var position in deadEnds)
        {
            if (roomFloors.Contains(position) == false)
            {
                var room = RunRandomWalk(randomWalkParameters, position);
                roomFloors.UnionWith(room);
            }
        }
    }

    private List<Vector2Int> FindAllDeadEnds(HashSet<Vector2Int> floorPositions)
    {
        List<Vector2Int> deadEnds = new List<Vector2Int>();
        foreach (var position in floorPositions)
        {
            int neighboursCount = 0;
            foreach (var direction in Direction2D.cardinalDirectionsList)
            {
                if (floorPositions.Contains(position + direction))
                    neighboursCount++;
            }
            if (neighboursCount == 1)
                deadEnds.Add(position);
        }
        return deadEnds;
    }

    private HashSet<Vector2Int> CreateRooms(HashSet<Vector2Int> potentialRoomPositions)
    {
        HashSet<Vector2Int> roomPositions = new HashSet<Vector2Int>();
        int roomToCreateCount = Mathf.RoundToInt(potentialRoomPositions.Count * roomPercent);

        List<Vector2Int> roomsToCreate = potentialRoomPositions.OrderBy(x => Guid.NewGuid()).Take(roomToCreateCount).ToList();
        ClearRoomData();
        foreach (var roomPosition in roomsToCreate)
        {
            var roomFloor = RunRandomWalk(randomWalkParameters, roomPosition);

            SaveRoomData(roomPosition, roomFloor);
            roomPositions.UnionWith(roomFloor);
        }
        return roomPositions;
    }

    private void CreateCorridors(HashSet<Vector2Int> floorPositions, HashSet<Vector2Int> potentialRoomPositions)
    {
        var currentPosition = startPosition;
        potentialRoomPositions.Add(currentPosition);

        for (int i = 0; i < corridorCount; i++)
        {
            var corridor = ProceduralGenerationAlgorithms.RandomWalkCorridor(currentPosition, corridorLength);
            currentPosition = corridor[corridor.Count - 1];
            potentialRoomPositions.Add(currentPosition);
            floorPositions.UnionWith(corridor);
        }
        corridorPositions = new HashSet<Vector2Int>(floorPositions);
    }

    private void FindAndMoveExitTileObject()
    {
        // Find the preexisting exit tile object in the hierarchy
        exitTileObject = GameObject.Find("ExitTile");
        GameObject exitTileMiniObject = GameObject.Find("ExitTileMini");

        // Convert Vector2Int position to Vector3 for positioning
        Vector3 exitTileWorldPosition = tilemapVisualizer.floorTilemap.GetCellCenterWorld((Vector3Int)exitTilePosition);

        // If the exit tile object is found, move it to the correct position
        if (exitTileObject != null)
        {
            exitTileObject.transform.position = exitTileWorldPosition;
        }
        else
        {
            Debug.LogError("ExitTile object not found in the hierarchy.");
        }

        // If the ExitTileMini object is found, move it to the same position
        if (exitTileMiniObject != null)
        {
            exitTileMiniObject.transform.position = exitTileWorldPosition;
        }
        else
        {
            // If ExitTileMini doesn't exist, instantiate it from the prefab
            if (minimapObjectPrefab != null)
            {
                exitTileMiniObject = Instantiate(minimapObjectPrefab, exitTileWorldPosition, Quaternion.identity);
                exitTileMiniObject.name = "ExitTileMini"; // Ensure it's named correctly in the hierarchy
            }
            else
            {
                Debug.LogError("Minimap Object Prefab is not assigned.");
            }
        }
    }

    private void ClearRoomData()
    {
        roomsDictionary.Clear();
        roomColors.Clear();
    }

    private void SaveRoomData(Vector2Int roomPosition, HashSet<Vector2Int> roomFloor)
    {
        roomsDictionary[roomPosition] = roomFloor;
        roomColors.Add(UnityEngine.Random.ColorHSV());
    }

    private void CheckForExitTile()
    {
        // Get the current player position
        Vector2Int playerPosition = playerController.GetCurrentTilePosition();

        // Check if the player's position matches the coordinates of the exit tile
        if (playerPosition == exitTilePosition)
        {
            // Generate a new dungeon via coroutine (so it will use loading screen too)
            StartCoroutine(GenerateWithLoadingCoroutine());
        }
    }

    public void RescanGraph()
    {
        // Call the RescanGraph method from GridRescan script if it exists
        if (gridRescan != null)
        {
            gridRescan.RescanGraph();
        }
        else
        {
            Debug.LogError("GridRescan reference is not set.");
        }
    }
}
