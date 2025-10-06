using UnityEngine;
using Pathfinding;

public class GridRescan : MonoBehaviour
{
    // Reference to the AstarPath component
    private AstarPath astarPath;

    void Start()
    {
        // Get the AstarPath component
        astarPath = FindObjectOfType<AstarPath>();
        
        if (astarPath == null)
        {
            Debug.LogError("AstarPath component not found in the scene. Please ensure you have it added.");
        }
    }

    // Method to rescan the specific graph
    public void RescanGraph()
    {
        // Check if AstarPath component is available
        if (astarPath != null)
        {
            // Optionally you can rescan all graphs with:
            // AstarPath.active.Scan();
            
            // To rescan a specific graph, you need to get the graph you want to rescan.
            // Assuming we are using a GridGraph
            GridGraph gridGraph = astarPath.data.gridGraph;

            if (gridGraph != null)
            {
                // Rescan the specific graph
                astarPath.Scan(gridGraph);
                Debug.Log("Graph rescanned successfully.");
            }
            else
            {
                Debug.LogError("GridGraph not found. Ensure you have a GridGraph in your AstarPath data.");
            }
        }
    }
}
