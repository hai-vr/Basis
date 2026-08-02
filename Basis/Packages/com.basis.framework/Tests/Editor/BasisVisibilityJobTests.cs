using System.Collections.Generic;
using Basis.Scripts.Rendering;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Frustum stage of the avatar visibility cull. The plane convention is the part most likely to be
/// wrong (Unity's frustum planes point inward), so the cameras here are real ones run through
/// <see cref="GeometryUtility.CalculateFrustumPlanes"/> rather than hand-built planes.
/// </summary>
public class BasisVisibilityJobTests
{
    private readonly List<GameObject> _spawned = new List<GameObject>();

    [TearDown]
    public void DestroySpawned()
    {
        for (int index = 0; index < _spawned.Count; index++)
        {
            if (_spawned[index] != null)
            {
                Object.DestroyImmediate(_spawned[index]);
            }
        }
        _spawned.Clear();
    }

    private Camera BuildCamera(Vector3 position, Quaternion rotation)
    {
        GameObject host = new GameObject("visibility-test-camera");
        _spawned.Add(host);
        host.transform.SetPositionAndRotation(position, rotation);

        Camera camera = host.AddComponent<Camera>();
        camera.enabled = false;
        camera.fieldOfView = 60f;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 100f;
        camera.aspect = 1f;
        return camera;
    }

    private static BasisVisibilityCamera Pack(Camera camera)
    {
        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(camera);
        return new BasisVisibilityCamera
        {
            Plane0 = ToFloat4(planes[0]),
            Plane1 = ToFloat4(planes[1]),
            Plane2 = ToFloat4(planes[2]),
            Plane3 = ToFloat4(planes[3]),
            Plane4 = ToFloat4(planes[4]),
            Plane5 = ToFloat4(planes[5]),
            Position = camera.transform.position,
        };
    }

    private static float4 ToFloat4(Plane plane)
    {
        return new float4(plane.normal.x, plane.normal.y, plane.normal.z, plane.distance);
    }

    private static uint RunFrustum(float3 center, float3 extents, BasisVisibilityFlags flags, float margin, params BasisVisibilityCamera[] cameras)
    {
        return RunFrustum(center, extents, flags, margin, 0f, 0, cameras);
    }

    private static uint RunFrustum(float3 center, float3 extents, BasisVisibilityFlags flags, float margin, float stickyMargin, byte appliedVisible, params BasisVisibilityCamera[] cameras)
    {
        var centers = new NativeArray<float3>(1, Allocator.Temp);
        var extentsArray = new NativeArray<float3>(1, Allocator.Temp);
        var flagsArray = new NativeArray<uint>(1, Allocator.Temp);
        var maskArray = new NativeArray<uint>(1, Allocator.Temp);
        var appliedArray = new NativeArray<byte>(1, Allocator.Temp);
        var cameraArray = new NativeArray<BasisVisibilityCamera>(
            cameras.Length == 0 ? 1 : cameras.Length, Allocator.Temp);

        for (int index = 0; index < cameras.Length; index++)
        {
            cameraArray[index] = cameras[index];
        }

        centers[0] = center;
        extentsArray[0] = extents;
        flagsArray[0] = (uint)flags;
        appliedArray[0] = appliedVisible;

        var job = new BasisVisibilityFrustumJob
        {
            Centers = centers,
            Extents = extentsArray,
            Flags = flagsArray,
            Cameras = cameraArray,
            AppliedVisible = appliedArray,
            CameraCount = cameras.Length,
            Margin = margin,
            StickyMargin = stickyMargin,
            VisibleMask = maskArray,
        };
        job.Execute(0);

        uint mask = maskArray[0];

        centers.Dispose();
        extentsArray.Dispose();
        flagsArray.Dispose();
        appliedArray.Dispose();
        maskArray.Dispose();
        cameraArray.Dispose();
        return mask;
    }

    private const BasisVisibilityFlags Cullable =
        BasisVisibilityFlags.Active | BasisVisibilityFlags.Dynamic | BasisVisibilityFlags.CullEligible;

    [Test]
    public void BoundsInFrontOfCamera_AreVisible()
    {
        BasisVisibilityCamera camera = Pack(BuildCamera(Vector3.zero, Quaternion.identity));
        uint mask = RunFrustum(new float3(0f, 0f, 10f), new float3(0.5f), Cullable, 0f, camera);
        Assert.AreNotEqual(0u, mask);
    }

    [Test]
    public void BoundsBehindCamera_AreNotVisible()
    {
        BasisVisibilityCamera camera = Pack(BuildCamera(Vector3.zero, Quaternion.identity));
        uint mask = RunFrustum(new float3(0f, 0f, -10f), new float3(0.5f), Cullable, 0f, camera);
        Assert.AreEqual(0u, mask);
    }

    [Test]
    public void BoundsBesideCamera_AreNotVisible()
    {
        BasisVisibilityCamera camera = Pack(BuildCamera(Vector3.zero, Quaternion.identity));
        uint mask = RunFrustum(new float3(100f, 0f, 10f), new float3(0.5f), Cullable, 0f, camera);
        Assert.AreEqual(0u, mask);
    }

    [Test]
    public void BoundsBeyondFarPlane_AreNotVisible()
    {
        BasisVisibilityCamera camera = Pack(BuildCamera(Vector3.zero, Quaternion.identity));
        uint mask = RunFrustum(new float3(0f, 0f, 500f), new float3(0.5f), Cullable, 0f, camera);
        Assert.AreEqual(0u, mask);
    }

    [Test]
    public void SecondCameraSeeingBounds_KeepsThemVisible()
    {
        BasisVisibilityCamera forward = Pack(BuildCamera(Vector3.zero, Quaternion.identity));
        BasisVisibilityCamera behind = Pack(BuildCamera(Vector3.zero, Quaternion.Euler(0f, 180f, 0f)));

        uint mask = RunFrustum(new float3(0f, 0f, -10f), new float3(0.5f), Cullable, 0f, forward, behind);

        Assert.AreEqual(0u, mask & 1u, "front camera should not see bounds behind it");
        Assert.AreNotEqual(0u, mask & 2u, "mirror-style second camera should see them");
    }

    [Test]
    public void NoCameras_FailsOpen()
    {
        uint mask = RunFrustum(new float3(0f, 0f, -10f), new float3(0.5f), Cullable, 0f);
        Assert.AreEqual(uint.MaxValue, mask);
    }

    [Test]
    public void NotCullEligible_StaysVisibleEvenWhenOffScreen()
    {
        BasisVisibilityCamera camera = Pack(BuildCamera(Vector3.zero, Quaternion.identity));
        BasisVisibilityFlags shadowCaster = BasisVisibilityFlags.Active | BasisVisibilityFlags.Dynamic;

        uint mask = RunFrustum(new float3(0f, 0f, -10f), new float3(0.5f), shadowCaster, 0f, camera);

        Assert.AreEqual(uint.MaxValue, mask);
    }

    [Test]
    public void AlwaysVisible_StaysVisibleEvenWhenOffScreen()
    {
        BasisVisibilityCamera camera = Pack(BuildCamera(Vector3.zero, Quaternion.identity));
        BasisVisibilityFlags always = Cullable | BasisVisibilityFlags.AlwaysVisible;

        uint mask = RunFrustum(new float3(0f, 0f, -10f), new float3(0.5f), always, 0f, camera);

        Assert.AreEqual(uint.MaxValue, mask);
    }

    [Test]
    public void InactiveEntry_IsNeverVisible()
    {
        BasisVisibilityCamera camera = Pack(BuildCamera(Vector3.zero, Quaternion.identity));
        uint mask = RunFrustum(new float3(0f, 0f, 10f), new float3(0.5f), BasisVisibilityFlags.None, 0f, camera);
        Assert.AreEqual(0u, mask);
    }

    [Test]
    public void Margin_PullsMarginalBoundsBackIntoView()
    {
        BasisVisibilityCamera camera = Pack(BuildCamera(Vector3.zero, Quaternion.identity));
        float3 center = new float3(6.2f, 0f, 10f);
        float3 extents = new float3(0.25f);

        uint withoutMargin = RunFrustum(center, extents, Cullable, 0f, camera);
        uint withMargin = RunFrustum(center, extents, Cullable, 3f, camera);

        Assert.AreEqual(0u, withoutMargin);
        Assert.AreNotEqual(0u, withMargin);
    }

    [Test]
    public void StickyMargin_KeepsAnAlreadyVisibleEntryFromFlickeringOut()
    {
        BasisVisibilityCamera camera = Pack(BuildCamera(Vector3.zero, Quaternion.identity));
        float3 center = new float3(6.2f, 0f, 10f);
        float3 extents = new float3(0.25f);

        uint hiddenEntry = RunFrustum(center, extents, Cullable, 0f, 3f, 0, camera);
        uint visibleEntry = RunFrustum(center, extents, Cullable, 0f, 3f, 1, camera);

        Assert.AreEqual(0u, hiddenEntry, "an off-screen entry gets only the base margin");
        Assert.AreNotEqual(0u, visibleEntry, "one already on screen gets the sticky margin and stays");
    }
}
