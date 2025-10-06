using System.Collections.Generic;
using UnityEngine;

public class Graph
{
    // Offsets for 4‑way connectivity
    public static readonly List<Vector2Int> neighbours4Directions = new List<Vector2Int>
    {
        new Vector2Int(0, 1),  // UP
        new Vector2Int(1, 0),  // RIGHT
        new Vector2Int(0, -1), // DOWN
        new Vector2Int(-1, 0)  // LEFT
    };

    // Offsets for 8‑way connectivity
    public static readonly List<Vector2Int> neighbours8Directions = new List<Vector2Int>
    {
        new Vector2Int(0, 1),   // UP
        new Vector2Int(1, 0),   // RIGHT
        new Vector2Int(0, -1),  // DOWN
        new Vector2Int(-1, 0),  // LEFT
        new Vector2Int(1, 1),   // UP‑RIGHT
        new Vector2Int(1, -1),  // DOWN‑RIGHT
        new Vector2Int(-1, 1),  // UP‑LEFT
        new Vector2Int(-1, -1)  // DOWN‑LEFT
    };

    // The set of valid vertices in this graph
    private readonly HashSet<Vector2Int> graph;

    public Graph(IEnumerable<Vector2Int> vertices)
    {
        // Use a HashSet for O(1) Contains()
        graph = new HashSet<Vector2Int>(vertices);
    }

    /// <summary>
    /// Get all 4‑directional neighbors of 'pos' that are actually in the graph.
    /// </summary>
    public List<Vector2Int> GetNeighbours4Directions(Vector2Int pos)
    {
        return GetNeighbours(pos, neighbours4Directions);
    }

    /// <summary>
    /// Get all 8‑directional neighbors of 'pos' that are actually in the graph.
    /// </summary>
    public List<Vector2Int> GetNeighbours8Directions(Vector2Int pos)
    {
        return GetNeighbours(pos, neighbours8Directions);
    }

    // Shared implementation for both variants
    private List<Vector2Int> GetNeighbours(Vector2Int pos, List<Vector2Int> offsets)
    {
        var results = new List<Vector2Int>(offsets.Count);
        foreach (var offset in offsets)
        {
            var candidate = pos + offset;
            if (graph.Contains(candidate))
                results.Add(candidate);
        }
        return results;
    }
}
