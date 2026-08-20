using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Optional per-camera source for volumetric fog. The camera can either use its normal volume
/// stack, resolve fog from a separate world-volume layer mask, or suppress fog entirely.
/// Other post-processing continues to use the camera's normal volume stack.
/// </summary>
[DisallowMultipleComponent]
public sealed class VolumetricFogCameraSource : MonoBehaviour
{
    /// <summary>When true, resolve fog from <see cref="WorldVolumeLayerMask"/> instead of the camera stack.</summary>
    public bool UseWorldFog;

    /// <summary>When true, the volumetric fog renderer skips this camera.</summary>
    public bool SuppressFog;

    /// <summary>Volume layers used when <see cref="UseWorldFog"/> is enabled.</summary>
    public LayerMask WorldVolumeLayerMask = 1;

    private VolumeStack worldVolumeStack;

    public VolumetricFogVolumeComponent ResolveFogVolume()
    {
        if (SuppressFog) return null;

        VolumeManager manager = VolumeManager.instance;
        if (!UseWorldFog)
        {
            return manager.stack.GetComponent<VolumetricFogVolumeComponent>();
        }

        if (!manager.isInitialized) return null;

        if (worldVolumeStack == null)
        {
            worldVolumeStack = manager.CreateStack();
        }

        manager.Update(worldVolumeStack, transform, WorldVolumeLayerMask);
        return worldVolumeStack.GetComponent<VolumetricFogVolumeComponent>();
    }

    private void OnDestroy()
    {
        if (worldVolumeStack == null) return;

        VolumeManager manager = VolumeManager.instance;
        if (manager.isInitialized)
        {
            manager.DestroyStack(worldVolumeStack);
        }
        worldVolumeStack = null;
    }
}
