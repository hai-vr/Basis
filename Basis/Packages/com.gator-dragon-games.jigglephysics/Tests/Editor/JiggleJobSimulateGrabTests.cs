using NUnit.Framework;
using Unity.Mathematics;

namespace GatorDragonGames.JigglePhysics.Tests {

[TestFixture]
internal class JiggleJobSimulateGrabTests {
    private const float Dt = 0.02f;

    private JiggleSimHarness harness;

    [TearDown]
    public void TearDown() {
        harness?.Dispose();
        harness = null;
    }

    private JiggleSimHarness BuildChain(int realCount, JigglePointParameters? parameters = null) {
        var tree = JiggleTestTree.Chain(realCount, float3.zero, new float3(1f, 0f, 0f), 1f,
            parameters ?? JiggleTestFactory.Params());
        harness = new JiggleSimHarness(tree, Dt);
        return harness;
    }

    [Test]
    public void HardPin_LandsThePointOnTheTarget() {
        BuildChain(3);
        var target = new float3(1f, 0.5f, 0f);
        harness.SetGrab(2, target);

        harness.Step();

        JiggleAssert.AreEqual(target, harness.Point(2).position, JiggleTestFactory.Tolerance);
    }

    [Test]
    public void StrengthHalf_MovesThePointHalfwayPerStep() {
        BuildChain(3);
        harness.SetGrab(2, new float3(1f, 1f, 0f), 0.5f);

        harness.Step();

        JiggleAssert.AreEqual(new float3(1f, 0.5f, 0f), harness.Point(2).position, JiggleTestFactory.Tolerance);
    }

    [Test]
    public void HardPin_ChildRestoresRestLengthAgainstThePinnedPointInTheSamePass() {
        BuildChain(3, JiggleTestFactory.Params(lengthElasticity: 1f));
        var target = new float3(1f, 1f, 0f);
        harness.SetGrab(2, target);

        harness.Step();

        JiggleAssert.AreEqual(target, harness.Point(2).position, JiggleTestFactory.Tolerance);
        Assert.AreEqual(1f, math.distance(harness.Point(3).position, harness.Point(2).position),
            JiggleTestFactory.Tolerance);
    }

    [Test]
    public void UnknownRootID_LeavesTheTreeUntouched() {
        BuildChain(3);
        harness.SetGrab(2, new float3(1f, 1f, 0f), 1f, rootID: 12345);

        harness.Step();

        JiggleAssert.AreEqual(new float3(1f, 0f, 0f), harness.Point(2).position, JiggleTestFactory.Tolerance);
    }

    [Test]
    public void OutOfRangePointIndex_IsIgnored() {
        BuildChain(3);
        harness.SetGrab(99, new float3(1f, 1f, 0f));

        harness.Step();

        JiggleAssert.AreEqual(new float3(1f, 0f, 0f), harness.Point(2).position, JiggleTestFactory.Tolerance);
        JiggleAssert.AreEqual(new float3(2f, 0f, 0f), harness.Point(3).position, JiggleTestFactory.Tolerance);
    }

    [Test]
    public void VirtualRootIndex_IsFilteredOut() {
        BuildChain(3);
        harness.SetGrab(0, new float3(5f, 5f, 5f));

        harness.Step();

        JiggleAssert.AreEqual(new float3(0f, 0f, 0f), harness.Point(1).position, JiggleTestFactory.Tolerance);
    }

    [Test]
    public void RootParticle_IsGrabbableDespiteItsRootPin() {
        // The solver pins this point to its pose and then continues past the other regions, so the
        // grab has to land inside that branch — otherwise a single bone rig has nothing grabbable.
        BuildChain(3, JiggleTestFactory.Params(rootElasticity: 1f));
        harness.SetGrab(1, new float3(0f, 1f, 0f));

        harness.Step();

        JiggleAssert.AreEqual(new float3(0f, 1f, 0f), harness.Point(1).position, JiggleTestFactory.Tolerance);
    }

    [Test]
    public void SingleBoneChain_IsGrabbable() {
        BuildChain(1);
        harness.SetGrab(1, new float3(0f, 0.5f, 0f));

        harness.Step();

        JiggleAssert.AreEqual(new float3(0f, 0.5f, 0f), harness.Point(1).position, JiggleTestFactory.Tolerance);
    }

    [Test]
    public void RootParticle_StretchLimitFallsBackToItsBoneLength() {
        // distanceFromRoot is 0 at the root particle, so the limit rides desiredLengthToParent
        // instead of coming out as "unbounded".
        BuildChain(3, JiggleTestFactory.Params(rootElasticity: 1f));
        harness.SetGrab(1, new float3(0f, 5f, 0f), maxStretchFactor: 0.5f);

        harness.Step();

        JiggleAssert.AreEqual(new float3(0f, 0.5f, 0f), harness.Point(1).position, JiggleTestFactory.Tolerance);
    }

    [Test]
    public void Release_CarriesThePullVelocityForward() {
        BuildChain(3);
        var target = new float3(1f, 0.4f, 0f);
        harness.SetGrab(2, target);
        harness.Step();

        harness.ClearGrabs();
        harness.Step();

        JiggleAssert.AreEqual(new float3(1f, 0.8f, 0f), harness.Point(2).position, 1e-3f);
    }

    [Test]
    public void SubstepsTwo_HardPinStillLandsExactly() {
        BuildChain(3);
        harness.job.substeps = 2;
        var target = new float3(1f, 0.5f, 0f);
        harness.SetGrab(2, target);

        harness.Step();

        JiggleAssert.AreEqual(target, harness.Point(2).position, JiggleTestFactory.Tolerance);
    }

    [Test]
    public void MaxStretch_ClampsThePullToTheLimitAroundThePose() {
        BuildChain(3);
        // point 2 sits 1m along the chain, so a factor of 0.5 allows half a metre of travel.
        harness.SetGrab(2, new float3(1f, 5f, 0f), maxStretchFactor: 0.5f);

        harness.Step();

        JiggleAssert.AreEqual(new float3(1f, 0.5f, 0f), harness.Point(2).position, JiggleTestFactory.Tolerance);
    }

    [Test]
    public void MaxStretch_ScalesWithDistanceFromRoot() {
        BuildChain(3);
        // point 3 is twice as far along as point 2, so the same factor buys twice the travel.
        harness.SetGrab(3, new float3(2f, 5f, 0f), maxStretchFactor: 0.5f);

        harness.Step();

        JiggleAssert.AreEqual(new float3(2f, 1f, 0f), harness.Point(3).position, JiggleTestFactory.Tolerance);
    }

    [Test]
    public void MaxStretch_LeavesATargetInsideTheLimitAlone() {
        BuildChain(3);
        harness.SetGrab(2, new float3(1f, 0.25f, 0f), maxStretchFactor: 1f);

        harness.Step();

        JiggleAssert.AreEqual(new float3(1f, 0.25f, 0f), harness.Point(2).position, JiggleTestFactory.Tolerance);
    }

    [Test]
    public void MaxStretchZero_IsUnbounded() {
        BuildChain(3);
        harness.SetGrab(2, new float3(1f, 5f, 0f), maxStretchFactor: 0f);

        harness.Step();

        JiggleAssert.AreEqual(new float3(1f, 5f, 0f), harness.Point(2).position, JiggleTestFactory.Tolerance);
    }

    [Test]
    public void MaxStretch_ClampsTowardsTheTargetNotAlongAnAxis() {
        BuildChain(3);
        harness.SetGrab(2, new float3(4f, 3f, 0f), maxStretchFactor: 1f);

        harness.Step();

        // Target is 5m from the pose at (1,0,0) along (3,3,0)/5 — the clamp keeps the direction.
        float3 expected = new float3(1f, 0f, 0f) + math.normalize(new float3(3f, 3f, 0f)) * 1f;
        JiggleAssert.AreEqual(expected, harness.Point(2).position, JiggleTestFactory.Tolerance);
    }

    [Test]
    public void GrabsPerTree_CapAtFour() {
        BuildChain(6);
        for (int pointIndex = 2; pointIndex <= 6; pointIndex++) {
            harness.SetGrab(pointIndex, new float3(pointIndex - 1f, 0.5f, 0f));
        }

        harness.Step();

        var displaced = 0;
        for (int pointIndex = 2; pointIndex <= 6; pointIndex++) {
            if (math.abs(harness.Point(pointIndex).position.y - 0.5f) < JiggleTestFactory.Tolerance) {
                displaced++;
            }
        }
        Assert.AreEqual(4, displaced);
    }
}

}
