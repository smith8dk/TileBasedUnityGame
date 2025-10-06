using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform target; // Reference to the player's transform

    // Update is called once per frame
    void Update()
    {
        if (target != null)
        {
            // Get the position of the player
            Vector3 targetPosition = target.position;
            // Keep the same z position of the camera
            targetPosition.z = transform.position.z;
            // Set the camera's position to follow the player's position
            transform.position = targetPosition;
        }
    }
}
