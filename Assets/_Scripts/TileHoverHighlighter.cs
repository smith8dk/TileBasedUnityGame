using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;


[RequireComponent(typeof(Tilemap))]
public class TileHoverHighlighter : MonoBehaviour
{
    [Header("Highlight Settings")]
    [SerializeField] private Color highlightColor = Color.yellow;

    [Header("Range Settings")]
    [Tooltip("Player Transform used to compute which tiles fall within the square range.")]
    [SerializeField] private Transform playerTransform;
    [Tooltip("Side‑length of the square (in tiles) centered on the player within which tiles can be highlighted or clicked.")]
    [SerializeField] private int rangeInTiles = 5;

    [Header("References")]
    [SerializeField] private Tilemap tilemap;
    [SerializeField] private Camera cam;

    // Tracks the last hovered cell position.
    private Vector3Int? previousCellPos = null;
    private Dictionary<Vector3Int, Color> originalColors = new Dictionary<Vector3Int, Color>();

    [Header("Enable / Disable Highlighting")]
    [SerializeField] private bool highlightingEnabled = true;

    [Header("Spawn Settings")]
    [SerializeField] private GameObject defaultSpawnPrefab;
    [SerializeField] private Transform spawnParent;

    private void Awake()
    {
        if (tilemap == null)
            tilemap = GetComponent<Tilemap>();
        if (cam == null)
            cam = Camera.main;
    }

    private void Update()
    {
        if (!highlightingEnabled)
        {
            ClearPreviousHighlight();
            return;
        }

        HandleMouseHover();
        HandleMouseClick();
    }

    public void EnableHighlighting()   => highlightingEnabled = true;
    public void DisableHighlighting()  { highlightingEnabled = false; ClearPreviousHighlight(); }
    public bool IsHighlightingEnabled() => highlightingEnabled;

    private void HandleMouseHover()
    {
        Vector3 worldPos = cam.ScreenToWorldPoint(Input.mousePosition);
        worldPos.z = 0f;
        Vector3Int cellPos = tilemap.WorldToCell(worldPos);

        if (previousCellPos.HasValue && previousCellPos.Value == cellPos)
        {
            if (!tilemap.HasTile(cellPos))
                ClearPreviousHighlight();
            return;
        }

        ClearPreviousHighlight();

        if (!tilemap.HasTile(cellPos) || !IsWithinSquareRange(cellPos))
        {
            previousCellPos = null;
            return;
        }

        HighlightCell(cellPos);
        previousCellPos = cellPos;
    }

    /// <summary>
    /// Fired when the user left‑clicks on a tile that:
    /// 1) exists, 2) is within the square range, and 3) highlighting is enabled.
    /// Passes the cell coordinates of that tile.
    /// </summary>
    public event Action<Vector3Int> OnValidTileClick;

    private void HandleMouseClick()
    {
        if (!highlightingEnabled || !Input.GetMouseButtonDown(0))
            return;

        Vector3 worldPos = cam.ScreenToWorldPoint(Input.mousePosition);
        worldPos.z = 0;
        Vector3Int cellPos = tilemap.WorldToCell(worldPos);

        // Only if there’s a tile here AND it’s in range…
        if (tilemap.HasTile(cellPos) && IsWithinSquareRange(cellPos))
        {
            // 1) Highlight script can still spawn its prefab if it wants:
            SpawnPrefabAtTile(cellPos);

            // 2) And *also* notify listeners that “yes, this was a valid click”:
            OnValidTileClick?.Invoke(cellPos);

            // 3) Then disable highlighting
            DisableHighlighting();
        }
    }

    private bool IsWithinSquareRange(Vector3Int cellPos)
    {
        if (playerTransform == null) return false;

        Vector3Int playerCell = tilemap.WorldToCell(playerTransform.position);

        int half = rangeInTiles / 2;    
        int dx = cellPos.x - playerCell.x;
        int dy = cellPos.y - playerCell.y;
        return Mathf.Abs(dx) <= half && Mathf.Abs(dy) <= half;
    }

    private void HighlightCell(Vector3Int cellPos)
    {
        if (!originalColors.ContainsKey(cellPos))
        {
            tilemap.SetTileFlags(cellPos, TileFlags.None);
            originalColors[cellPos] = tilemap.GetColor(cellPos);
        }
        tilemap.SetColor(cellPos, highlightColor);
    }

    private void ClearPreviousHighlight()
    {
        if (previousCellPos.HasValue)
        {
            var prev = previousCellPos.Value;
            if (originalColors.TryGetValue(prev, out var orig))
            {
                tilemap.SetTileFlags(prev, TileFlags.None);
                tilemap.SetColor(prev, orig);
                originalColors.Remove(prev);
            }
            previousCellPos = null;
        }
    }

    private void OnDisable()
    {
        ClearPreviousHighlight();
    }

    public void SpawnPrefabAtTile(Vector3Int cellPos, GameObject prefab = null)
    {
        var toSpawn = prefab ?? defaultSpawnPrefab;
        if (toSpawn == null || tilemap == null) return;

        var spawnPos = tilemap.GetCellCenterWorld(cellPos);
        if (spawnParent != null)
            Instantiate(toSpawn, spawnPos, Quaternion.identity, spawnParent);
        else
            Instantiate(toSpawn, spawnPos, Quaternion.identity);
    }

    public void SpawnPrefabAtTile(int x, int y, GameObject prefab = null)
    {
        SpawnPrefabAtTile(new Vector3Int(x, y, 0), prefab);
    }
}
