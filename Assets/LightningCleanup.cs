using UnityEngine;

public class LightningCleanup : StateMachineBehaviour
{
    // Called when the state finishes exiting (i.e. after 100% of the clip)
    override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        // Disable the sprite or GameObject:
        var sr = animator.GetComponent<SpriteRenderer>();
        if (sr != null) sr.enabled = false;
        // or animator.gameObject.SetActive(false);
    }
}
