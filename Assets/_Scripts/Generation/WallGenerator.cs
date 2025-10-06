using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class WallGenerator
{
    public static void CreateWalls(HashSet<Vector2Int> floorPositions, TilemapVisualizer tilemapVisualizer)
    {
        var basicWallPositions = FindWallsInDirections(floorPositions, Direction2D.cardinalDirectionsList);
        var cornerWallPositions = FindWallsInDirections(floorPositions, Direction2D.diagonalDirectionsList);
        CreateBasicWall(tilemapVisualizer, basicWallPositions, floorPositions);
        CreateCornerWalls(tilemapVisualizer, cornerWallPositions, floorPositions);

        // Add TilemapCollider2D component to the wall tilemap
        AddTilemapCollider2D(tilemapVisualizer.wallTilemap);
    }

    private static void CreateCornerWalls(TilemapVisualizer tilemapVisualizer, HashSet<Vector2Int> cornerWallPositions, HashSet<Vector2Int> floorPositions)
    {
        foreach (var position in cornerWallPositions)
        {
            string neighboursBinaryType = "";
            foreach (var direction in Direction2D.eightDirectionsList)
            {
                var neighbourPosition = position + direction;
                if (floorPositions.Contains(neighbourPosition))
                {
                    neighboursBinaryType += "1";
                }
                else
                {
                    neighboursBinaryType += "0";
                }
            }

            // Paint the corner wall and create the minimap object
            GameObject cornerWallTile = tilemapVisualizer.PaintSingleCornerWall(position, neighboursBinaryType);
            
            // Minimap object creation will be handled inside PaintSingleCornerWall
            AddColliderToTile(cornerWallTile);
        }
    }

    private static void CreateBasicWall(TilemapVisualizer tilemapVisualizer, HashSet<Vector2Int> basicWallPositions, HashSet<Vector2Int> floorPositions)
    {
        foreach (var position in basicWallPositions)
        {
            string neighboursBinaryType = "";
            foreach (var direction in Direction2D.cardinalDirectionsList)
            {
                var neighbourPosition = position + direction;
                if (floorPositions.Contains(neighbourPosition))
                {
                    neighboursBinaryType += "1";
                }
                else
                {
                    neighboursBinaryType += "0";
                }
            }

            // Paint the basic wall and create the minimap object
            GameObject basicWallTile = tilemapVisualizer.PaintSingleBasicWall(position, neighboursBinaryType);
            
            // Minimap object creation will be handled inside PaintSingleBasicWall
            AddColliderToTile(basicWallTile);
        }
    }

    private static HashSet<Vector2Int> FindWallsInDirections(HashSet<Vector2Int> floorPositions, List<Vector2Int> directionList)
    {
        HashSet<Vector2Int> wallPositions = new HashSet<Vector2Int>();
        foreach (var position in floorPositions)
        {
            foreach (var direction in directionList)
            {
                var neighbourPosition = position + direction;
                if (!floorPositions.Contains(neighbourPosition))
                    wallPositions.Add(neighbourPosition);
            }
        }
        return wallPositions;
    }

    private static void AddColliderToTile(GameObject wallTile)
    {
        if (wallTile != null)
        {
            // Add a BoxCollider2D component if it doesn't exist
            if (!wallTile.GetComponent<BoxCollider2D>())
            {
                wallTile.AddComponent<BoxCollider2D>();
            }
        }
    }

    private static void AddTilemapCollider2D(Tilemap tilemap)
    {
        if (tilemap != null)
        {
            // Add a TilemapCollider2D component if it doesn't exist
            if (!tilemap.GetComponent<TilemapCollider2D>())
            {
                tilemap.gameObject.AddComponent<TilemapCollider2D>();
            }
        }
    }
}

