using UnityEngine;

namespace Basis
{
    /// <summary>
    /// Applies the server-authoritative "static / locked" state to a spawned item by forwarding it to
    /// every <see cref="IBasisStaticLockable"/> on the object (pickup props, vehicles, …). Used by both
    /// the spawn path (late joiners spawning an already-static item) and the runtime toggle broadcast.
    /// </summary>
    public static class BasisSpawnedStaticState
    {
        public static void Apply(GameObject root, bool isStatic)
        {
            if (root == null)
            {
                return;
            }

            IBasisStaticLockable[] lockables = root.GetComponentsInChildren<IBasisStaticLockable>(true);
            for (int i = 0; i < lockables.Length; i++)
            {
                lockables[i]?.SetStatic(isStatic);
            }
        }
    }
}
