using Basis.IK;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
namespace Basis.Tests.IK
{
    public class BasisLegSwivelBodyFrameTests
    {
        const float dt = 1f / 90f, kneeForward = 0.0073f;
        GameObject root;
        BasisPoseSkeleton skeleton;
        NativeArray<BasisLegSlotState> legState;
        Transform hips, leftUpperLeg, leftLowerLeg, leftFoot, rightUpperLeg, rightLowerLeg, rightFoot;
        [TearDown]
        public void TearDown()
        {
            if (legState.IsCreated) legState.Dispose();
            skeleton?.Dispose();
            skeleton = null;
            if (root != null) Object.DestroyImmediate(root);
            root = null;
        }
        static Transform Bone(string name, Transform parent, Vector3 worldPosition)
        {
            var go = new GameObject(name);
            go.transform.SetPositionAndRotation(worldPosition, Quaternion.identity);
            go.transform.SetParent(parent, true);
            return go.transform;
        }
        void BuildRig(Quaternion hipsBoneRotation)
        {
            root = new GameObject("LegSwivelBodyFrameRig");
            hips = Bone("Hips", root.transform, new Vector3(0f, 0.95f, 0f));
            hips.rotation = hipsBoneRotation;
            leftUpperLeg = Bone("LeftUpperLeg", hips, new Vector3(-0.09f, 0.90f, 0f));
            leftLowerLeg = Bone("LeftLowerLeg", leftUpperLeg, new Vector3(-0.09f, 0.48f, kneeForward));
            leftFoot = Bone("LeftFoot", leftLowerLeg, new Vector3(-0.09f, 0.08f, 0f));
            rightUpperLeg = Bone("RightUpperLeg", hips, new Vector3(0.09f, 0.90f, 0f));
            rightLowerLeg = Bone("RightLowerLeg", rightUpperLeg, new Vector3(0.09f, 0.48f, kneeForward));
            rightFoot = Bone("RightFoot", rightLowerLeg, new Vector3(0.09f, 0.08f, 0f));
            Transform[] bones = { hips, leftUpperLeg, leftLowerLeg, leftFoot, rightUpperLeg, rightLowerLeg, rightFoot };
            skeleton = new BasisPoseSkeleton();
            skeleton.Build(hips, bones);
            skeleton.GatherNow();
        }
        BasisEerieMovement Job(Quaternion hipsBind)
        {
            legState = new NativeArray<BasisLegSlotState>(2, Allocator.Persistent);
            var job = new BasisEerieMovement
            {
                offsetRotationHips = hipsBind,
                offsetRotationLeftFoot = Quaternion.identity,
                offsetRotationRightFoot = Quaternion.identity,
                targetPositionLeftLowerLeg = new Vector3(-0.09f, 0.30f, 0.10f),
                targetRotationLeftLowerLeg = Quaternion.identity,
                hintPositionLeftLowerLeg = new Vector3(-0.09f, 0.50f, 0.60f),
                kneeBendPrefLeft = Vector3.right,
                kneeBendPrefRight = Vector3.right,
                kneeAnteriorRef = Vector3.right,
                playerUp = Vector3.up,
                legSwivelSmoothing = true,
                legState = legState,
            };
            job.handleHips = skeleton.Bind(hips);
            job.handleLeftUpperLeg = skeleton.Bind(leftUpperLeg);
            job.handleLeftLowerLeg = skeleton.Bind(leftLowerLeg);
            job.handleLeftFoot = skeleton.Bind(leftFoot);
            job.handleRightUpperLeg = skeleton.Bind(rightUpperLeg);
            job.handleRightLowerLeg = skeleton.Bind(rightLowerLeg);
            job.handleRightFoot = skeleton.Bind(rightFoot);
            BasisEeriePlanner.Bind(ref job);
            BasisEeriePlanner.Frame(ref job, new BasisEerieFrameFacts { hipsTracked = true, upright = true, footSimReady = true, leftFootSim = 1f, leftSimFootRotation = true, deltaTime = dt });
            job.poseStream = skeleton.Stream;
            job.poseStream.deltaTime = dt;
            return job;
        }
        Quaternion SolveFrames(Quaternion hipsBoneRotation, int frames, out Vector3 kneeDir)
        {
            BuildRig(hipsBoneRotation);
            var job = Job(hipsBoneRotation);
            Assert.IsTrue(job.plan.leftLeg.solve && job.plan.leftLeg.swivel, "the plan must solve the left leg with knee swivel smoothing on");
            for (int f = 0; f < frames; f++) job.SolveLeg(0);
            Vector3 hip = skeleton.Stream.GetPosition(job.handleLeftUpperLeg), knee = skeleton.Stream.GetPosition(job.handleLeftLowerLeg), foot = skeleton.Stream.GetPosition(job.handleLeftFoot);
            Vector3 axis = (foot - hip).normalized;
            kneeDir = Vector3.ProjectOnPlane(knee - hip, axis).normalized;
            return skeleton.Stream.GetRotation(job.handleLeftLowerLeg);
        }
        [TestCase(0f, 0f, 0f, TestName = "hips bone identity")]
        [TestCase(0f, 180f, 0f, TestName = "hips bone facing backward")]
        [TestCase(0f, 120f, 0f, TestName = "hips bone yawed 120 deg")]
        [TestCase(-90f, 0f, 0f, TestName = "hips bone Y-up convention")]
        [TestCase(0f, 0f, 90f, TestName = "hips bone rolled 90 deg")]
        public void KneeStaysAnterior_WhateverTheHipsBoneFrame(float x, float y, float z)
        {
            Quaternion reference = SolveFrames(Quaternion.identity, 3, out Vector3 referenceKnee);
            Assert.Greater(Vector3.Dot(referenceKnee, Vector3.forward), 0.9f, "the identity-hips reference must keep its knee in front");
            TearDown();
            Quaternion shin = SolveFrames(Quaternion.Euler(x, y, z), 3, out Vector3 kneeDir);
            Assert.Greater(Vector3.Dot(kneeDir, Vector3.forward), 0.9f, $"the knee swung to ({kneeDir.x:F2}, {kneeDir.y:F2}, {kneeDir.z:F2}); it must stay in front of the hip-foot axis");
            float drift = Quaternion.Angle(shin, reference);
            Assert.Less(drift, 3f, $"the shin rotation is {drift:F1} deg away from the identity-hips solve");
        }
    }
}
