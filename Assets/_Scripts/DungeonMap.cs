using UnityEngine;
using System;
using System.IO;
using System.Collections;

public class DungeonMap : MonoBehaviour
{
    // [SerializeField]
    // public CorridorFirstDungeonGenerator dungeonGenerator; // Reference to your dungeon generator script

    // private void Start()
    // {
    //     // Subscribe to the event when the dungeon generation is completed
    //     dungeonGenerator.OnDungeonGenerationCompleted += GenerateDungeonImage;
    // }

    // private void GenerateDungeonImage()
    // {
    //     // Capture the screenshot
    //     StartCoroutine(CaptureScreenshot());
    // }

    // private IEnumerator CaptureScreenshot()
    // {
    //     yield return new WaitForEndOfFrame();

    //     // Create a Texture2D to store the screenshot
    //     Texture2D screenshotTexture = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);

    //     // Read the pixels from the screen and apply them to the Texture2D
    //     screenshotTexture.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
    //     screenshotTexture.Apply();

    //     // Encode the Texture2D to a PNG byte array
    //     byte[] bytes = screenshotTexture.EncodeToPNG();

    //     // Define the file path where the screenshot will be saved
    //     string filePath = Path.Combine(Application.persistentDataPath, "DungeonScreenshot_" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".png");

    //     // Write the PNG byte array to a file
    //     File.WriteAllBytes(filePath, bytes);

    //     Debug.Log("Dungeon screenshot saved to: " + filePath);
    // }
}
