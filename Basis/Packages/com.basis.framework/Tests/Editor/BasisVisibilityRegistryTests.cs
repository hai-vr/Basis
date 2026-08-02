using System.Collections.Generic;
using Basis.Scripts.Rendering;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Registry bookkeeping for entries that carry a root transform. Removal is an O(1) swap-back across
/// three structures at once — the TransformAccessArray, the dense→slot list, and the slot→dense
/// inverse — and a desync there silently points the bounds job at the wrong avatar.
/// </summary>
public class BasisVisibilityRegistryTests
{
    private readonly List<GameObject> _spawned = new List<GameObject>();

    [TearDown]
    public void Cleanup()
    {
        BasisVisibilitySystem.Dispose();
        BasisVisibilityDatabase.Dispose();
        for (int index = 0; index < _spawned.Count; index++)
        {
            if (_spawned[index] != null)
            {
                Object.DestroyImmediate(_spawned[index]);
            }
        }
        _spawned.Clear();
    }

    private Transform NewRoot(string name)
    {
        GameObject host = new GameObject(name);
        _spawned.Add(host);
        return host.transform;
    }

    private int RegisterWithRoot(string name)
    {
        return BasisVisibilityDatabase.Register(
            null, NewRoot(name), float3.zero, new float3(1f), BasisVisibilityFlags.Dynamic | BasisVisibilityFlags.CullEligible);
    }

    [Test]
    public void RemovingAMiddleEntry_RepointsTheSwappedRow()
    {
        int a = RegisterWithRoot("a");
        int b = RegisterWithRoot("b");
        int c = RegisterWithRoot("c");

        Assert.AreEqual(3, BasisVisibilityDatabase.DynamicCount);

        BasisVisibilityDatabase.Unregister(a);

        Assert.AreEqual(2, BasisVisibilityDatabase.DynamicCount);
        Assert.AreEqual(2, BasisVisibilityDatabase.Roots.length);

        Assert.AreEqual(BasisVisibilityDatabase.InvalidHandle, BasisVisibilityDatabase.SlotToDense[a]);
        Assert.AreEqual(1, BasisVisibilityDatabase.SlotToDense[b], "untouched row must keep its dense index");
        Assert.AreEqual(0, BasisVisibilityDatabase.SlotToDense[c], "last row swapped into the hole");
        Assert.AreEqual(c, BasisVisibilityDatabase.DenseToSlot[0]);
        Assert.AreEqual(b, BasisVisibilityDatabase.DenseToSlot[1]);
    }

    [Test]
    public void RemovingTheLastEntry_LeavesOthersAlone()
    {
        int a = RegisterWithRoot("a");
        int b = RegisterWithRoot("b");

        BasisVisibilityDatabase.Unregister(b);

        Assert.AreEqual(1, BasisVisibilityDatabase.DynamicCount);
        Assert.AreEqual(0, BasisVisibilityDatabase.SlotToDense[a]);
        Assert.AreEqual(BasisVisibilityDatabase.InvalidHandle, BasisVisibilityDatabase.SlotToDense[b]);
        Assert.AreEqual(a, BasisVisibilityDatabase.DenseToSlot[0]);
    }

    [Test]
    public void EveryDenseRowRoundTripsThroughTheInverseMap()
    {
        int[] slots = { RegisterWithRoot("a"), RegisterWithRoot("b"), RegisterWithRoot("c"), RegisterWithRoot("d") };

        BasisVisibilityDatabase.Unregister(slots[1]);
        BasisVisibilityDatabase.Unregister(slots[0]);

        int dynamic = BasisVisibilityDatabase.DynamicCount;
        for (int dense = 0; dense < dynamic; dense++)
        {
            int slot = BasisVisibilityDatabase.DenseToSlot[dense];
            Assert.AreEqual(dense, BasisVisibilityDatabase.SlotToDense[slot],
                "dense row " + dense + " does not round-trip through slot " + slot);
        }
    }

    [Test]
    public void ReRegisteringAfterRemoval_RebuildsTheRow()
    {
        int a = RegisterWithRoot("a");
        BasisVisibilityDatabase.Unregister(a);

        int reused = RegisterWithRoot("a-again");

        Assert.AreEqual(a, reused, "slot should come back off the free list");
        Assert.AreEqual(1, BasisVisibilityDatabase.DynamicCount);
        Assert.AreEqual(0, BasisVisibilityDatabase.SlotToDense[reused]);
    }

    [Test]
    public void DynamicEntryWithoutARoot_IsForcedAlwaysVisible()
    {
        int handle = BasisVisibilityDatabase.Register(
            null, null, float3.zero, new float3(1f), BasisVisibilityFlags.Dynamic | BasisVisibilityFlags.CullEligible);

        uint flags = BasisVisibilityDatabase.Flags[handle];
        Assert.AreNotEqual(0u, flags & (uint)BasisVisibilityFlags.AlwaysVisible,
            "nothing would ever refresh its centre, so it must not be cullable");
        Assert.AreEqual(0, BasisVisibilityDatabase.DynamicCount);
    }

    [Test]
    public void StaticEntryWithoutARoot_StaysCullable()
    {
        int handle = BasisVisibilityDatabase.Register(
            null, null, float3.zero, new float3(1f), BasisVisibilityFlags.Static | BasisVisibilityFlags.CullEligible);

        uint flags = BasisVisibilityDatabase.Flags[handle];
        Assert.AreEqual(0u, flags & (uint)BasisVisibilityFlags.AlwaysVisible,
            "authored fixed bounds need no refresh, so a static entry stays cullable");
    }

    [Test]
    public void RegisteredEntry_SeedsBothBaseAndWorldExtents()
    {
        int handle = RegisterWithRoot("a");
        Assert.AreEqual(new float3(1f), BasisVisibilityDatabase.BaseExtents[handle]);
        Assert.AreEqual(new float3(1f), BasisVisibilityDatabase.Extents[handle]);
    }

    [Test]
    public void MaxAxisScale_IsOneForIdentity()
    {
        Assert.AreEqual(1f, BasisVisibilityMath.MaxAxisScale(Matrix4x4.identity), 1e-5f);
    }

    [Test]
    public void MaxAxisScale_TracksUniformScale()
    {
        Matrix4x4 m = Matrix4x4.TRS(new Vector3(3f, 0f, 0f), Quaternion.Euler(0f, 40f, 0f), Vector3.one * 2.5f);
        Assert.AreEqual(2.5f, BasisVisibilityMath.MaxAxisScale(m), 1e-4f);
    }

    [Test]
    public void MaxAxisScale_TakesTheLargestAxis()
    {
        Matrix4x4 m = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(0.5f, 3f, 1f));
        Assert.AreEqual(3f, BasisVisibilityMath.MaxAxisScale(m), 1e-4f);
    }

    [Test]
    public void MaxAxisScale_IsRotationInvariant()
    {
        Matrix4x4 unrotated = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * 1.75f);
        Matrix4x4 rotated = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(23f, 57f, 91f), Vector3.one * 1.75f);

        Assert.AreEqual(
            BasisVisibilityMath.MaxAxisScale(unrotated),
            BasisVisibilityMath.MaxAxisScale(rotated),
            1e-4f);
    }
}
