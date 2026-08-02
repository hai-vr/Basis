using Basis.Scripts.Rendering;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

/// <summary>
/// Change detection compares the freshly computed mask against what was actually written to the
/// renderers, not against the previous frame's mask. That is what makes the apply budget
/// self-healing: anything the budget skips stays different and is re-emitted next tick.
/// </summary>
public class BasisVisibilityChangeTests
{
    [TearDown]
    public void DisposeDatabase()
    {
        BasisVisibilitySystem.Dispose();
        BasisVisibilityDatabase.Dispose();
    }

    private static NativeList<int> RunChange(uint[] masks, byte[] applied, uint[] flags)
    {
        var maskArray = new NativeArray<uint>(masks.Length, Allocator.Temp);
        var appliedArray = new NativeArray<byte>(applied.Length, Allocator.Temp);
        var flagsArray = new NativeArray<uint>(flags.Length, Allocator.Temp);

        for (int index = 0; index < masks.Length; index++)
        {
            maskArray[index] = masks[index];
            appliedArray[index] = applied[index];
            flagsArray[index] = flags[index];
        }

        var changed = new NativeList<int>(masks.Length, Allocator.Temp);
        var job = new BasisVisibilityChangeJob
        {
            VisibleMask = maskArray,
            AppliedVisible = appliedArray,
            Flags = flagsArray,
            Count = masks.Length,
            Changed = changed,
        };
        job.Execute();

        maskArray.Dispose();
        appliedArray.Dispose();
        flagsArray.Dispose();
        return changed;
    }

    private const uint ActiveCullable =
        (uint)(BasisVisibilityFlags.Active | BasisVisibilityFlags.CullEligible);

    [Test]
    public void UnchangedEntries_AreNotEmitted()
    {
        NativeList<int> changed = RunChange(
            new uint[] { 1u, 0u },
            new byte[] { 1, 0 },
            new uint[] { ActiveCullable, ActiveCullable });

        Assert.AreEqual(0, changed.Length);
        changed.Dispose();
    }

    [Test]
    public void BecomingHidden_IsEmitted()
    {
        NativeList<int> changed = RunChange(
            new uint[] { 0u },
            new byte[] { 1 },
            new uint[] { ActiveCullable });

        Assert.AreEqual(1, changed.Length);
        Assert.AreEqual(0, changed[0]);
        changed.Dispose();
    }

    [Test]
    public void BecomingVisible_IsEmitted()
    {
        NativeList<int> changed = RunChange(
            new uint[] { 4u },
            new byte[] { 0 },
            new uint[] { ActiveCullable });

        Assert.AreEqual(1, changed.Length);
        changed.Dispose();
    }

    [Test]
    public void InactiveEntries_AreSkippedEvenWhenTheyDiffer()
    {
        NativeList<int> changed = RunChange(
            new uint[] { 0u },
            new byte[] { 1 },
            new uint[] { (uint)BasisVisibilityFlags.None });

        Assert.AreEqual(0, changed.Length);
        changed.Dispose();
    }

    [Test]
    public void UnappliedChanges_StayPendingForTheNextTick()
    {
        NativeList<int> first = RunChange(
            new uint[] { 0u, 0u },
            new byte[] { 1, 1 },
            new uint[] { ActiveCullable, ActiveCullable });
        Assert.AreEqual(2, first.Length);
        first.Dispose();

        NativeList<int> second = RunChange(
            new uint[] { 0u, 0u },
            new byte[] { 0, 1 },
            new uint[] { ActiveCullable, ActiveCullable });

        Assert.AreEqual(1, second.Length, "the entry the budget skipped must be re-emitted");
        Assert.AreEqual(1, second[0]);
        second.Dispose();
    }

    [Test]
    public void RegisterThenUnregister_ReusesTheSlot()
    {
        BasisVisibilityDatabase.EnsureCreated();

        int first = BasisVisibilityDatabase.Register(null, null, float3.zero, new float3(1f), BasisVisibilityFlags.CullEligible);
        Assert.IsTrue(BasisVisibilityDatabase.IsValid(first));

        BasisVisibilityDatabase.Unregister(first);
        Assert.IsFalse(BasisVisibilityDatabase.IsValid(first));

        int second = BasisVisibilityDatabase.Register(null, null, float3.zero, new float3(1f), BasisVisibilityFlags.CullEligible);
        Assert.AreEqual(first, second);
        Assert.AreEqual(1, BasisVisibilityDatabase.Count);
    }

    [Test]
    public void RegisteredEntry_StartsVisible()
    {
        BasisVisibilityDatabase.EnsureCreated();
        int handle = BasisVisibilityDatabase.Register(null, null, float3.zero, new float3(1f), BasisVisibilityFlags.CullEligible);

        Assert.AreEqual(1, BasisVisibilityDatabase.AppliedVisible[handle]);
        Assert.AreEqual(uint.MaxValue, BasisVisibilityDatabase.VisibleMask[handle]);
    }

    [Test]
    public void SetCullEligible_TogglesOnlyThatFlag()
    {
        BasisVisibilityDatabase.EnsureCreated();
        int handle = BasisVisibilityDatabase.Register(null, null, float3.zero, new float3(1f),
            BasisVisibilityFlags.Dynamic | BasisVisibilityFlags.CullEligible);

        BasisVisibilityDatabase.SetCullEligible(handle, false);
        uint flags = BasisVisibilityDatabase.Flags[handle];
        Assert.AreEqual(0u, flags & (uint)BasisVisibilityFlags.CullEligible);
        Assert.AreNotEqual(0u, flags & (uint)BasisVisibilityFlags.Dynamic);
        Assert.AreNotEqual(0u, flags & (uint)BasisVisibilityFlags.Active);

        BasisVisibilityDatabase.SetCullEligible(handle, true);
        Assert.AreNotEqual(0u, BasisVisibilityDatabase.Flags[handle] & (uint)BasisVisibilityFlags.CullEligible);
    }

    [Test]
    public void GrowingPastInitialCapacity_KeepsEarlierEntries()
    {
        BasisVisibilityDatabase.EnsureCreated();

        int firstHandle = BasisVisibilityDatabase.Register(null, null, new float3(1f, 2f, 3f), new float3(1f), BasisVisibilityFlags.CullEligible);

        int target = BasisVisibilityDatabase.Capacity + 8;
        for (int index = 1; index < target; index++)
        {
            BasisVisibilityDatabase.Register(null, null, float3.zero, new float3(1f), BasisVisibilityFlags.CullEligible);
        }

        Assert.Greater(BasisVisibilityDatabase.Capacity, target - 8);
        Assert.IsTrue(BasisVisibilityDatabase.IsValid(firstHandle));
        Assert.AreEqual(new float3(1f, 2f, 3f), BasisVisibilityDatabase.Centers[firstHandle]);
    }
}
