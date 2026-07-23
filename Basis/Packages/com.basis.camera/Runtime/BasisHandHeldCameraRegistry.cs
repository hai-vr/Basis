using System;
using System.Collections.Generic;

public static class BasisHandHeldCameraRegistry
{
    private static readonly List<BasisHandHeldCamera> cameras = new List<BasisHandHeldCamera>();

    public static IReadOnlyList<BasisHandHeldCamera> Cameras => cameras;
    public static int Count => cameras.Count;

    public static event Action OnChanged;

    public static void Add(BasisHandHeldCamera camera)
    {
        if (camera == null) return;
        if (cameras.Contains(camera)) return;
        cameras.Add(camera);
        OnChanged?.Invoke();
    }

    public static void Remove(BasisHandHeldCamera camera)
    {
        if (camera == null) return;
        if (cameras.Remove(camera)) OnChanged?.Invoke();
    }
}
