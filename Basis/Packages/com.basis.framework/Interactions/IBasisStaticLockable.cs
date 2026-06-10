namespace Basis
{
    /// <summary>
    /// Implemented by components on a spawned item that can be frozen / locked by the library
    /// "Static" toggle. Pickup props disable interaction and freeze their rigidbody; vehicles lock
    /// out movement. Routing through this interface keeps the framework from having to reference the
    /// vehicles package directly (the dependency only goes the other way).
    /// </summary>
    public interface IBasisStaticLockable
    {
        /// <summary>
        /// Apply (true) or release (false) the static / locked state. Called on the main thread,
        /// both at spawn time and when a server broadcast toggles the state.
        /// </summary>
        void SetStatic(bool isStatic);
    }
}
