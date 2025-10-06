public interface IDamageable
{
    /// <summary>
    /// Called to apply damage to this object.
    /// </summary>
    /// <param name="amount">How much damage to take.</param>
    void TakeDamage(int amount);
}
