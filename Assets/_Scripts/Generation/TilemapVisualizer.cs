using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class TilemapVisualizer : MonoBehaviour
{
    [SerializeField] public Tilemap floorTilemap, wallTilemap, minimapTilemap;

    [Header("Floor Variants")]
    [Tooltip("Pick one of these at random when painting each floor cell")]
    [SerializeField] private List<TileBase> floorVariants;

    [SerializeField] public TileBase floorTile, wallTop, wallSideRight, wallSideLeft, wallBottom, wallFull,
        wallInnerCornerDownLeft, wallInnerCornerDownRight,
        wallDiagonalCornerDownRight, wallDiagonalCornerDownLeft, wallDiagonalCornerUpRight, wallDiagonalCornerUpLeft;

    [SerializeField] public TileBase exitTile;
    [SerializeField] private Tile minimapTile;

    [Header("Fill-with Wall Settings")]
    [Tooltip("If set, this tile will be used to fill empty cells (preferred).")]
    [SerializeField] private TileBase fillWallTile;
    [Tooltip("If set, one of these variants will be chosen at random per cell.")]
    [SerializeField] private List<TileBase> fillWallVariants;

    [Header("Milestone Color Rotation")]
    [Tooltip("Colors to rotate through each time the DungeonFloorManager milestone event fires.")]
    [SerializeField] private List<Color> milestoneColors = new List<Color>() { Color.magenta };

    [Tooltip("Fallback color used when milestoneColors is empty.")]
    [SerializeField] private Color milestoneFallbackColor = Color.magenta;

    [Tooltip("Whether to apply the color to the minimap tilemap as well.")]
    [SerializeField] private bool applyToMinimap = true;

    // internal index of the *next* color to apply when a milestone fires
    private int nextColorIndex = 0;

    // cached reference to manager used for unsubscribing
    private DungeonFloorManager floorManager;

    private void Start()
    {
        // Try to get manager instance (use Instance if available; otherwise find in scene)
        floorManager = DungeonFloorManager.Instance ?? FindObjectOfType<DungeonFloorManager>();

        if (floorManager != null)
        {
            // Compute color state based on how many milestones have already occurred
            int count = (milestoneColors != null && milestoneColors.Count > 0) ? milestoneColors.Count : 1;

            // Defensive: ensure milestone interval positive
            int interval = Math.Max(1, floorManager.MilestoneInterval);

            // How many milestones have been passed already (integer division)
            int milestonesPassed = (floorManager.CurrentFloor / interval);

            // Index of the color corresponding to the most recent milestone (if any)
            int lastIndex = (count > 0) ? (milestonesPassed % count) : 0;

            // Apply the color corresponding to lastIndex so visuals are in sync at start
            Color startColor = (milestoneColors != null && milestoneColors.Count > 0)
                ? milestoneColors[lastIndex]
                : milestoneFallbackColor;
            ApplyColor(startColor);

            // Next color to apply when a milestone fires:
            nextColorIndex = (lastIndex + 1) % Math.Max(1, count);

            // Subscribe to milestone event (ensure no double-subscribe)
            floorManager.OnMilestoneReached -= HandleMilestoneReached;
            floorManager.OnMilestoneReached += HandleMilestoneReached;
        }
        else
        {
            Debug.LogWarning("[TilemapVisualizer] DungeonFloorManager not found — milestone subscription skipped.");
            // Still apply fallback color to be safe
            ApplyColor(milestoneFallbackColor);
        }
    }

    private void OnDestroy()
    {
        if (floorManager != null)
            floorManager.OnMilestoneReached -= HandleMilestoneReached;
        else if (DungeonFloorManager.Instance != null)
            DungeonFloorManager.Instance.OnMilestoneReached -= HandleMilestoneReached;
    }

    private void HandleMilestoneReached(int floor)
    {
        // Choose the color at nextColorIndex (or fallback) and apply it, then advance index.
        if (milestoneColors != null && milestoneColors.Count > 0)
        {
            Color c = milestoneColors[nextColorIndex];
            ApplyColor(c);

            nextColorIndex = (nextColorIndex + 1) % milestoneColors.Count;
        }
        else
        {
            ApplyColor(milestoneFallbackColor);
        }
    }

    /// <summary>
    /// Applies the given color to configured tilemaps immediately.
    /// Changing the tilemap.color tints all tiles already painted and ones painted later.
    /// </summary>
    /// <param name="c">Color to apply</param>
    private void ApplyColor(Color c)
    {
        if (floorTilemap != null) floorTilemap.color = c;
        if (wallTilemap != null) wallTilemap.color = c;
        if (applyToMinimap && minimapTilemap != null) minimapTilemap.color = c;

        Debug.Log($"[TilemapVisualizer] Applied milestone color {c} to tilemaps.");
    }

    // ----------------- existing tile painting methods (unchanged) -----------------

    public void PaintFloorTiles(IEnumerable<Vector2Int> floorPositions)
    {
        foreach (var pos in floorPositions)
        {
            TileBase chosen = (floorVariants != null && floorVariants.Count > 0)
                ? floorVariants[UnityEngine.Random.Range(0, floorVariants.Count)]
                : floorTile;

            PaintSingleTile(floorTilemap, chosen, pos);
        }
    }

    public void PaintTiles(IEnumerable<Vector2Int> positions, Tilemap tilemap, TileBase tile)
    {
        foreach (var position in positions)
        {
            PaintSingleTile(tilemap, tile, position);
        }
    }

    internal GameObject PaintSingleBasicWall(Vector2Int position, string binaryType)
    {
        int typeAsInt = Convert.ToInt32(binaryType, 2);
        TileBase tile = null;

        if (WallTypesHelper.wallTop.Contains(typeAsInt))
        {
            tile = wallTop;
        }
        else if (WallTypesHelper.wallSideRight.Contains(typeAsInt))
        {
            tile = wallSideRight;
        }
        else if (WallTypesHelper.wallSideLeft.Contains(typeAsInt))
        {
            tile = wallSideLeft;
        }
        else if (WallTypesHelper.wallBottm.Contains(typeAsInt))
        {
            tile = wallBottom;
        }
        else if (WallTypesHelper.wallFull.Contains(typeAsInt))
        {
            tile = wallFull;
        }

        if (tile != null)
        {
            GameObject wallTile = PaintSingleTile(wallTilemap, tile, position);
            PaintSingleTile(minimapTilemap, minimapTile, position);
            return wallTile;
        }
        else
        {
            return null;
        }
    }

    internal GameObject PaintSingleCornerWall(Vector2Int position, string binaryType)
    {
        int typeAsInt = Convert.ToInt32(binaryType, 2);
        TileBase tile = null;

        if (WallTypesHelper.wallInnerCornerDownLeft.Contains(typeAsInt))
        {
            tile = wallInnerCornerDownLeft;
        }
        else if (WallTypesHelper.wallInnerCornerDownRight.Contains(typeAsInt))
        {
            tile = wallInnerCornerDownRight;
        }
        else if (WallTypesHelper.wallDiagonalCornerDownLeft.Contains(typeAsInt))
        {
            tile = wallDiagonalCornerDownLeft;
        }
        else if (WallTypesHelper.wallDiagonalCornerDownRight.Contains(typeAsInt))
        {
            tile = wallDiagonalCornerDownRight;
        }
        else if (WallTypesHelper.wallDiagonalCornerUpRight.Contains(typeAsInt))
        {
            tile = wallDiagonalCornerUpRight;
        }
        else if (WallTypesHelper.wallDiagonalCornerUpLeft.Contains(typeAsInt))
        {
            tile = wallDiagonalCornerUpLeft;
        }
        else if (WallTypesHelper.wallFullEightDirections.Contains(typeAsInt))
        {
            tile = wallFull;
        }
        else if (WallTypesHelper.wallBottmEightDirections.Contains(typeAsInt))
        {
            tile = wallBottom;
        }

        if (tile != null)
        {
            GameObject wallTile = PaintSingleTile(wallTilemap, tile, position);
            PaintSingleTile(minimapTilemap, minimapTile, position);
            return wallTile;
        }
        else
        {
            return null;
        }
    }

    public GameObject PaintSingleTile(Tilemap tilemap, TileBase tile, Vector2Int position)
    {
        var tilePosition = tilemap.WorldToCell((Vector3Int)position);
        tilemap.SetTile(tilePosition, tile);
        return tilemap.GetInstantiatedObject(tilePosition);
    }

    public IEnumerable<Vector2Int> GetFloorTilePositions()
    {
        var floorPositions = new List<Vector2Int>();

        BoundsInt bounds = floorTilemap.cellBounds;
        TileBase[] allTiles = floorTilemap.GetTilesBlock(bounds);

        for (int x = bounds.xMin; x < bounds.xMax; x++)
        {
            for (int y = bounds.yMin; y < bounds.yMax; y++)
            {
                Vector3Int localPlace = new Vector3Int(x, y, (int)floorTilemap.transform.position.y);
                if (allTiles[(x - bounds.xMin) + (y - bounds.yMin) * bounds.size.x] != null)
                {
                    floorPositions.Add(new Vector2Int(x, y));
                }
            }
        }

        return floorPositions;
    }

    public IEnumerable<Vector2Int> GetWallTilePositions()
    {
        var wallPositions = new List<Vector2Int>();
        BoundsInt bounds = wallTilemap.cellBounds;
        TileBase[] tiles = wallTilemap.GetTilesBlock(bounds);

        for (int x = bounds.xMin; x < bounds.xMax; x++)
        {
            for (int y = bounds.yMin; y < bounds.yMax; y++)
            {
                int index = (x - bounds.xMin) + (y - bounds.yMin) * bounds.size.x;
                if (tiles[index] != null)
                    wallPositions.Add(new Vector2Int(x, y));
            }
        }

        return wallPositions;
    }

    public void PaintMinimapTiles(HashSet<Vector2Int> wallPositions)
    {
        foreach (var position in wallPositions)
        {
            Vector3Int tilePosition = (Vector3Int)position;
            minimapTilemap.SetTile(tilePosition, minimapTile);
        }
    }

    public void ClearMinimapTiles(IEnumerable<Vector2Int> positions)
    {
        if (positions == null) return;
        foreach (var p in positions)
        {
            Vector3Int cell = (Vector3Int)p;
            minimapTilemap.SetTile(cell, null);
        }
    }

    public void Clear()
    {
        floorTilemap.ClearAllTiles();
        wallTilemap.ClearAllTiles();
        minimapTilemap.ClearAllTiles();
    }

    public void FillTwoLayerBorderForCells(IEnumerable<Vector2Int> cells)
    {
        if (cells == null) return;

        TileBase layerTile = (fillWallTile != null) ? fillWallTile
            : (fillWallVariants != null && fillWallVariants.Count > 0 ? fillWallVariants[UnityEngine.Random.Range(0, fillWallVariants.Count)] : (wallFull ?? wallBottom ?? floorTile));

        HashSet<Vector2Int> set = new HashSet<Vector2Int>(cells);

        List<Vector2Int> offsetsLayer1 = new List<Vector2Int>();
        List<Vector2Int> offsetsLayer2 = new List<Vector2Int>();
        for (int dx = -2; dx <= 2; dx++)
        {
            for (int dy = -2; dy <= 2; dy++)
            {
                var off = new Vector2Int(dx, dy);
                int cheb = Math.Max(Math.Abs(dx), Math.Abs(dy));
                if (cheb == 1) offsetsLayer1.Add(off);
                else if (cheb == 2) offsetsLayer2.Add(off);
            }
        }

        var layer1 = new HashSet<Vector2Int>();
        var layer2 = new HashSet<Vector2Int>();

        foreach (var c in set)
        {
            foreach (var off in offsetsLayer1)
            {
                var p = c + off;
                if (!set.Contains(p))
                    layer1.Add(p);
            }
            foreach (var off in offsetsLayer2)
            {
                var p = c + off;
                if (!set.Contains(p))
                    layer2.Add(p);
            }
        }

        layer2.ExceptWith(set);
        layer2.ExceptWith(layer1);

        foreach (var p in layer1)
        {
            Vector3Int tilePos = new Vector3Int(p.x, p.y, 0);
            if (floorTilemap.GetTile(tilePos) == null && wallTilemap.GetTile(tilePos) == null)
            {
                PaintSingleTile(wallTilemap, layerTile, p);
                PaintSingleTile(minimapTilemap, minimapTile, p);
            }
        }
        foreach (var p in layer2)
        {
            Vector3Int tilePos = new Vector3Int(p.x, p.y, 0);
            if (floorTilemap.GetTile(tilePos) == null && wallTilemap.GetTile(tilePos) == null)
            {
                PaintSingleTile(wallTilemap, layerTile, p);
                PaintSingleTile(minimapTilemap, minimapTile, p);
            }
        }
    }

    public void FillEmptyTilesInBounds(BoundsInt bounds)
    {
        for (int x = bounds.xMin; x < bounds.xMax; x++)
        {
            for (int y = bounds.yMin; y < bounds.yMax; y++)
            {
                var pos = new Vector3Int(x, y, 0);

                TileBase existingFloor = floorTilemap.GetTile(pos);
                TileBase existingWall = wallTilemap.GetTile(pos);

                if (existingFloor == null && existingWall == null)
                {
                    TileBase chosen = (floorVariants != null && floorVariants.Count > 0)
                        ? floorVariants[UnityEngine.Random.Range(0, floorVariants.Count)]
                        : floorTile;

                    floorTilemap.SetTile(pos, chosen);
                }
            }
        }
    }

    public void FillEmptyWallsForRoom(HashSet<Vector2Int> roomFloor, int padding, float fillProbability)
    {
        if (roomFloor == null || roomFloor.Count == 0)
            return;

        FillTwoLayerBorderForCells(roomFloor);

        if (padding <= 2 || fillProbability <= 0f)
            return;

        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var p in roomFloor)
        {
            if (p.x < minX) minX = p.x;
            if (p.y < minY) minY = p.y;
            if (p.x > maxX) maxX = p.x;
            if (p.y > maxY) maxY = p.y;
        }

        minX -= padding;
        minY -= padding;
        maxX += padding;
        maxY += padding;

        var excluded = new HashSet<Vector2Int>(roomFloor);

        for (int dx = -2; dx <= 2; dx++)
        {
            for (int dy = -2; dy <= 2; dy++)
            {
                int cheb = Math.Max(Math.Abs(dx), Math.Abs(dy));
                if (cheb == 1 || cheb == 2)
                {
                    foreach (var cell in roomFloor)
                    {
                        excluded.Add(cell + new Vector2Int(dx, dy));
                    }
                }
            }
        }

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                Vector2Int pos = new Vector2Int(x, y);
                if (excluded.Contains(pos)) continue;

                Vector3Int tilePos = new Vector3Int(x, y, 0);
                if (floorTilemap.GetTile(tilePos) != null || wallTilemap.GetTile(tilePos) != null)
                    continue;

                if (UnityEngine.Random.value <= fillProbability)
                {
                    TileBase chosen;
                    if (fillWallVariants != null && fillWallVariants.Count > 0)
                        chosen = fillWallVariants[UnityEngine.Random.Range(0, fillWallVariants.Count)];
                    else if (fillWallTile != null)
                        chosen = fillWallTile;
                    else
                        chosen = wallFull ?? wallBottom ?? wallTop ?? floorTile;

                    PaintSingleTile(wallTilemap, chosen, pos);
                    PaintSingleTile(minimapTilemap, minimapTile, pos);
                }
            }
        }
    }

    public int CountWallTilesAroundRoom(HashSet<Vector2Int> roomFloor)
    {
        if (roomFloor == null || roomFloor.Count == 0) return 0;

        var checkedPositions = new HashSet<Vector2Int>();

        for (int dx = -2; dx <= 2; dx++)
        {
            for (int dy = -2; dy <= 2; dy++)
            {
                int cheb = Math.Max(Math.Abs(dx), Math.Abs(dy));
                if (cheb == 0) continue;

                foreach (var cell in roomFloor)
                {
                    var p = cell + new Vector2Int(dx, dy);
                    if (checkedPositions.Add(p))
                    {
                        var t = wallTilemap.GetTile((Vector3Int)p);
                        if (t != null)
                        {
                            // counted later
                        }
                    }
                }
            }
        }

        int count = 0;
        foreach (var pos in checkedPositions)
        {
            var tile = wallTilemap.GetTile((Vector3Int)pos);
            if (tile != null) count++;
        }
        return count;
    }
}
