public enum BasisJiggleColliderTier
{
    /// <summary>Feet + arms + hand spheres + fingers (every authored collider).</summary>
    Full = 0,
    /// <summary>Drop the per-finger colliders; keep feet, arms and the hand spheres.</summary>
    NoFingers = 1,
    /// <summary>Hand spheres only — arm and foot colliders removed.</summary>
    HandsOnly = 2,
    /// <summary>No jiggle colliders at all.</summary>
    None = 3,
}
