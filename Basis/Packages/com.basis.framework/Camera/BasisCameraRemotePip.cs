using UnityEngine;

/// <summary>
/// Metadata component attached to the remote PIP camera prefab.
/// Provides configuration such as the default rotation offset for the lens model.
/// </summary>
public class BasisCameraRemotePip : MonoBehaviour
{
    /// <summary>
    /// Default rotation offset applied to the spawned PIP model.
    /// Set in the prefab inspector (e.g., 90 degrees on Y for the lens mesh orientation).
    /// </summary>
    public Vector3 RotationOffset = new Vector3(0, 90, 0);

    /// <summary>
    /// The player ID this remote PIP belongs to. Set at spawn time.
    /// </summary>
    [HideInInspector]
    public ushort PlayerID;
}
