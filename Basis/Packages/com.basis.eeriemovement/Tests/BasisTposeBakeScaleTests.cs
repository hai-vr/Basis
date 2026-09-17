using Basis.IK;
using Basis.Scripts.Common;
using Basis.Scripts.Drivers;
using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;
namespace Basis.Tests.IK
{
    public class BasisTposeBakeScaleTests
    {
        const float tolerance = 1e-5f;
        float savedRatio;
        [SetUp]
        public void SaveRatio() => savedRatio = BasisHeightDriver.AvatarToDefaultRatioScaledWithAvatarScale;
        [TearDown]
        public void RestoreRatio() => BasisHeightDriver.AvatarToDefaultRatioScaledWithAvatarScale = savedRatio;
        sealed class Rig : System.IDisposable
        {
            public GameObject Root;
            public BasisTransformMapping Mapping = new BasisTransformMapping();
            public BasisPoseSkeleton Skeleton = new BasisPoseSkeleton();
            public BasisEerieMovement Job;
            public float NeckToHips, ShoulderToHandLeft, ShoulderToHandRight, HeadToNeck;
            public void Dispose()
            {
                Job.Destroy();
                Skeleton.Dispose();
                if (Root != null) Object.DestroyImmediate(Root);
            }
        }
        static Transform Bone(string name, Transform parent, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            return go.transform;
        }
        static Rig BuildRig(float previousAvatarRatio)
        {
            var rig = new Rig { Root = new GameObject("TposeBakeScaleRig") };
            Transform root = rig.Root.transform;
            BasisTransformMapping m = rig.Mapping;
            m.Hips = Bone("Hips", root, new Vector3(0f, 0.95f, 0f));
            m.spine = Bone("Spine", m.Hips, new Vector3(0f, 0.11f, 0f));
            m.chest = Bone("Chest", m.spine, new Vector3(0f, 0.15f, 0f));
            m.Upperchest = Bone("UpperChest", m.chest, new Vector3(0f, 0.12f, 0f));
            m.neck = Bone("Neck", m.Upperchest, new Vector3(0f, 0.12f, 0f));
            m.head = Bone("Head", m.neck, new Vector3(0f, 0.12f, 0f));
            m.leftShoulder = Bone("LeftShoulder", m.Upperchest, new Vector3(0.04f, 0.08f, 0f));
            m.leftUpperArm = Bone("LeftUpperArm", m.leftShoulder, new Vector3(0.12f, 0f, 0f));
            m.leftLowerArm = Bone("LeftLowerArm", m.leftUpperArm, new Vector3(0.27f, 0f, 0f));
            m.leftHand = Bone("LeftHand", m.leftLowerArm, new Vector3(0.26f, 0f, 0f));
            m.RightShoulder = Bone("RightShoulder", m.Upperchest, new Vector3(-0.04f, 0.08f, 0f));
            m.RightUpperArm = Bone("RightUpperArm", m.RightShoulder, new Vector3(-0.12f, 0f, 0f));
            m.RightLowerArm = Bone("RightLowerArm", m.RightUpperArm, new Vector3(-0.27f, 0f, 0f));
            m.rightHand = Bone("RightHand", m.RightLowerArm, new Vector3(-0.26f, 0f, 0f));
            m.LeftUpperLeg = Bone("LeftUpperLeg", m.Hips, new Vector3(0.09f, -0.05f, 0f));
            m.LeftLowerLeg = Bone("LeftLowerLeg", m.LeftUpperLeg, new Vector3(0f, -0.43f, 0f));
            m.leftFoot = Bone("LeftFoot", m.LeftLowerLeg, new Vector3(0f, -0.44f, 0f));
            m.RightUpperLeg = Bone("RightUpperLeg", m.Hips, new Vector3(-0.09f, -0.05f, 0f));
            m.RightLowerLeg = Bone("RightLowerLeg", m.RightUpperLeg, new Vector3(0f, -0.43f, 0f));
            m.rightFoot = Bone("RightFoot", m.RightLowerLeg, new Vector3(0f, -0.44f, 0f));
            var bones = new List<Transform> { m.Hips, m.spine, m.chest, m.Upperchest, m.neck, m.head, m.leftShoulder, m.leftUpperArm, m.leftLowerArm, m.leftHand, m.RightShoulder, m.RightUpperArm, m.RightLowerArm, m.rightHand, m.LeftUpperLeg, m.LeftLowerLeg, m.leftFoot, m.RightUpperLeg, m.RightLowerLeg, m.rightFoot };
            rig.Skeleton.Build(root, bones);
            rig.NeckToHips = Vector3.Distance(m.neck.position, m.Hips.position);
            rig.HeadToNeck = Vector3.Distance(m.head.position, m.neck.position);
            rig.ShoulderToHandLeft = Vector3.Distance(m.leftShoulder.position, m.leftHand.position);
            rig.ShoulderToHandRight = Vector3.Distance(m.RightShoulder.position, m.rightHand.position);
            BasisHeightDriver.AvatarToDefaultRatioScaledWithAvatarScale = previousAvatarRatio;
            BasisEerieMovementSetup.Create(ref rig.Job, rig.Skeleton, m);
            return rig;
        }
        static void AssertMeasuredLengths(Rig rig)
        {
            Assert.AreEqual(rig.NeckToHips, rig.Job.tposeLengthNeckToHips.magnitude, tolerance);
            Assert.AreEqual(rig.HeadToNeck, rig.Job.tposeHeadToNeckLocal.magnitude, tolerance);
            Assert.AreEqual(rig.ShoulderToHandLeft, rig.Job.tposeShoulderToHandLeft, tolerance);
            Assert.AreEqual(rig.ShoulderToHandRight, rig.Job.tposeShoulderToHandRight, tolerance);
        }
        [Test]
        public void Bake_TagsTheLengthsWithTheAppliedAvatarScale_NotThePreviousAvatarsHeightRatio()
        {
            using var rig = BuildRig(0.7f);
            Assert.AreEqual(BasisEerieMovementSetup.AppliedAvatarScale(), rig.Job.tposeBakeScale, tolerance);
            AssertMeasuredLengths(rig);
        }
        [Test]
        public void FirstHeightCallbackAfterAnAvatarSwap_LeavesTheMeasuredLengthsAlone()
        {
            using var rig = BuildRig(0.7f);
            BasisHeightDriver.AvatarToDefaultRatioScaledWithAvatarScale = 1.3f;
            rig.Job.RescaleTposeScalars(BasisEerieMovementSetup.AppliedAvatarScale());
            AssertMeasuredLengths(rig);
        }
        [Test]
        public void RepeatCallbacks_AreIdempotent()
        {
            using var rig = BuildRig(0.9f);
            float scale = BasisEerieMovementSetup.AppliedAvatarScale();
            rig.Job.RescaleTposeScalars(scale);
            rig.Job.RescaleTposeScalars(scale);
            AssertMeasuredLengths(rig);
        }
        [Test]
        public void AvatarScaleChange_StillRescalesTheLengths()
        {
            using var rig = BuildRig(0.7f);
            float scale = BasisEerieMovementSetup.AppliedAvatarScale();
            rig.Job.RescaleTposeScalars(scale * 1.25f);
            Assert.AreEqual(rig.NeckToHips * 1.25f, rig.Job.tposeLengthNeckToHips.magnitude, tolerance);
            Assert.AreEqual(rig.ShoulderToHandLeft * 1.25f, rig.Job.tposeShoulderToHandLeft, tolerance);
        }
    }
}
