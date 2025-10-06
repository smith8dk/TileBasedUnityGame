// SpikeLife.cs
using UnityEngine;

/// <summary>
/// Simple one-turn Spike life. Inherits Spell so MoveFurther() will be called by the turn system.
/// Spike is stationary and will be destroyed after one MoveFurther() call.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class SpikeLife : Spell
{
    private int turnsLived = 0;

    /// <summary>
    /// Optional call to set owner early.
    /// </summary>
    public void InitializeSpike(GameObject owner = null)
    {
        this.owner = owner;
        ApplyOwnerCollisionIgnore();
    }

    protected override void Start()
    {
        base.Start();

        // Spike is a stationary hazard; prevent movement interpolation.
        isMoving = false;
        moveTime = 0f;

        // Ensure collider is a trigger so it can damage on trigger if desired.
        var col = GetComponent<Collider2D>();
        if (col != null)
            col.isTrigger = true;
    }

    protected override void MoveFurther()
    {
        // Live for exactly 1 MoveFurther() call, then destroy.
        turnsLived++;
        if (turnsLived >= 1)
            Destroy(gameObject);
    }

    // Optional: override OnTriggerEnter2D if you want the spike to damage things immediately.
    // By default it inherits Spell.OnTriggerEnter2D which deals damageAmount and destroys the spike.
}
