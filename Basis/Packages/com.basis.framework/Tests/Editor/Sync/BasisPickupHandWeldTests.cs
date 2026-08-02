using System.Reflection;
using Basis.Scripts.BasisSdk.Interactions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Basis.Tests.Sync
{
    /// <summary>
    /// The local hand-weld grab: seat-point selection (ComputeClosestBoundsOffset) composed with the real
    /// BasisParentConstraint, driven the way OnInteractStart (:601-628) and UpdateHeldPoseFromInput
    /// (:1032-1034) drive it. WeldToHand makes the constraint source the post-IK wrist bone
    /// (IKWorldData) instead of the pre-IK hand target, so everything here is measured against that pose.
    /// </summary>
    public class BasisPickupHandWeldTests
    {
        const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
        static readonly MethodInfo ClosestOffsetM =
            typeof(BasisPickupInteractable).GetMethod("ComputeClosestBoundsOffset", NP);

        readonly System.Collections.Generic.List<GameObject> _cleanup = new System.Collections.Generic.List<GameObject>();

        [SetUp]
        public void SetUp() => Assert.IsNotNull(ClosestOffsetM, "ComputeClosestBoundsOffset moved");

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _cleanup)
            {
                if (go != null) Object.DestroyImmediate(go);
            }
            _cleanup.Clear();
        }

        // ── positive controls: what a healthy weld does ──

        [Test]
        public void Grab_SeatsNearestSurfacePointAtTheWeldPose()
        {
            BasisPickupInteractable prop = MakeProp("box", out Transform t);
            AddBox(prop, Vector3.one * 0.2f);
            t.SetPositionAndRotation(new Vector3(0f, 1f, 0f), Quaternion.identity);

            // Hand a clear distance outside the collider - the case LerpToHandOnPickup is designed for.
            Vector3 handPos = new Vector3(0f, 1f, -0.5f);
            Quaternion handRot = Quaternion.identity;

            Vector3 seated = Seat(prop, t, handPos, handRot);
            t.position = seated;

            Assert.LessOrEqual(Vector3.Distance(NearestSurfacePoint(prop, handPos), handPos), 0.01f,
                "after the grab lerp the nearest surface point must sit on the weld pose");
        }

        [Test]
        public void HeldProp_IsRigidUnderWeldMotion()
        {
            BasisPickupInteractable prop = MakeProp("rigid", out Transform t);
            AddBox(prop, Vector3.one * 0.2f);
            t.SetPositionAndRotation(new Vector3(0f, 1f, 0f), Quaternion.identity);

            Vector3 handPos = new Vector3(0f, 1f, -0.4f);
            var (constraint, _) = Grab(prop, t, handPos, Quaternion.identity);
            Assert.IsTrue(constraint.Evaluate(out Vector3 seatedPos, out Quaternion seatedRot));
            t.SetPositionAndRotation(seatedPos, seatedRot);
            Vector3 gripAtGrab = InWeldFrame(t, handPos, Quaternion.identity);

            // The solved wrist swings and rolls through a normal reach.
            Vector3 movedPos = new Vector3(0.7f, 1.6f, 0.3f);
            Quaternion movedRot = Quaternion.Euler(35f, -110f, 80f);
            constraint.UpdateSourcePositionAndRotation(0, movedPos, movedRot);
            Assert.IsTrue(constraint.Evaluate(out Vector3 pos, out Quaternion rot));
            t.SetPositionAndRotation(pos, rot);

            Vector3 gripNow = InWeldFrame(t, movedPos, movedRot);
            Assert.LessOrEqual(Vector3.Distance(gripNow, gripAtGrab), 0.001f,
                "a welded prop must hold a constant pose in the wrist frame as the wrist moves");
        }

        // ── seat-point selection failures ──

        [Test]
        public void WeldPoseInsideCollider_KeepsTheGrabTimePose()
        {
            BasisPickupInteractable prop = MakeProp("swallowed", out Transform t);
            AddBox(prop, Vector3.one * 0.4f);
            Vector3 start = new Vector3(0f, 1f, 0f);
            t.SetPositionAndRotation(start, Quaternion.identity);

            // Wrist already inside the prop: it is in the grip, so there is nothing to pull in and the
            // seat must not shove a collider face onto the wrist.
            Vector3 handPos = start + new Vector3(0.05f, 0f, 0.05f);
            Vector3 seated = Seat(prop, t, handPos, Quaternion.identity);

            Assert.LessOrEqual(Vector3.Distance(seated, start), 0.001f,
                "a wrist inside the collider must hold the grab-time pose rather than snapping the prop");
        }

        [Test]
        public void TriggerVolume_DoesNotHijackTheSeatPoint()
        {
            // Colliders live on children, so ScanColliders takes its GetComponentsInChildren<Collider>(true)
            // path - which sweeps in triggers and inactive colliders alongside the real surface.
            BasisPickupInteractable prop = MakeProp("with-trigger", out Transform t);
            Vector3 start = new Vector3(0f, 1f, 0f);
            t.SetPositionAndRotation(start, Quaternion.identity);

            var body = new GameObject("body");
            body.transform.SetParent(t, false);
            body.AddComponent<BoxCollider>().size = Vector3.one * 0.2f;

            var volume = new GameObject("hover-volume");
            volume.transform.SetParent(t, false);
            BoxCollider trigger = volume.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = Vector3.one * 3f;

            Vector3 handPos = start + new Vector3(0f, 0f, -0.5f);
            t.position = Seat(prop, t, handPos, Quaternion.identity);

            Assert.LessOrEqual(Vector3.Distance(NearestVisualSurfacePoint(prop, handPos, trigger), handPos), 0.05f,
                "the seat point must come from the prop's real collider, not from a trigger volume that " +
                "happens to enclose the hand");
        }

        [Test]
        public void NonConvexMeshCollider_StillSeatsViaBounds()
        {
            LogAssert.ignoreFailingMessages = true;
            try
            {
                BasisPickupInteractable prop = MakeProp("mesh-prop", out Transform t);
                MeshCollider mc = prop.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = Quad();
                mc.convex = false;

                Vector3 start = new Vector3(0f, 1f, 0f);
                t.SetPositionAndRotation(start, Quaternion.identity);
                Vector3 hand = start + new Vector3(0f, 0f, -0.5f);
                Vector3 seated = Seat(prop, t, hand, Quaternion.identity);

                // Collider.ClosestPoint is unsupported on a non-convex MeshCollider, so the seat has to
                // come off the bounds - otherwise a distance grab leaves the prop where it was.
                Assert.Greater(Vector3.Distance(seated, start), 0.02f,
                    "a mesh-collider prop grabbed at range must still be pulled to the hand");
                Assert.Less(Vector3.Distance(seated, hand), Vector3.Distance(start, hand),
                    "the seat must move the prop toward the hand, not away from it");
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }
        }

        // ── the cost of welding to the solved bone rather than the hand target ──

        [TestCase(0.02f)]
        [TestCase(0.05f)]
        [TestCase(0.12f)]
        public void WeldPoseDivergingFromHandTarget_CarriesThePropAwayFromTheTarget(float solveError)
        {
            BasisPickupInteractable prop = MakeProp("diverging", out Transform t);
            AddBox(prop, Vector3.one * 0.2f);
            t.SetPositionAndRotation(new Vector3(0f, 1f, 0f), Quaternion.identity);

            // Grab while the FBIK happens to be solving the wrist exactly onto its target.
            Vector3 handTarget = new Vector3(0f, 1f, -0.4f);
            var (constraint, _) = Grab(prop, t, handTarget, Quaternion.identity);

            // Reach out: the solve can no longer land the wrist on the target (arm length, shoulder and
            // wrist limits, per-group smoothing), so IKWorldData separates from OutgoingWorldData.
            Vector3 solvedWrist = handTarget + new Vector3(0f, 0f, -solveError);

            constraint.UpdateSourcePositionAndRotation(0, handTarget, Quaternion.identity);
            Assert.IsTrue(constraint.Evaluate(out Vector3 drivenByTarget, out _));
            constraint.UpdateSourcePositionAndRotation(0, solvedWrist, Quaternion.identity);
            Assert.IsTrue(constraint.Evaluate(out Vector3 drivenByWrist, out _));

            // Characterisation: the weld passes solve error straight through to the prop 1:1. Every
            // centimetre the FBIK cannot deliver is a centimetre between the prop and the real controller.
            Assert.AreEqual(solveError, Vector3.Distance(drivenByTarget, drivenByWrist), 0.001f,
                "welding to the solved wrist transfers the whole FBIK solve error onto the held prop");
        }

        // ── rig ──

        BasisPickupInteractable MakeProp(string name, out Transform t)
        {
            var go = new GameObject(name);
            _cleanup.Add(go);
            t = go.transform;
            return go.AddComponent<BasisPickupInteractable>();
        }

        static void AddBox(BasisPickupInteractable prop, Vector3 size)
        {
            BoxCollider box = prop.gameObject.AddComponent<BoxCollider>();
            box.size = size;
        }

        /// <summary>Mirrors OnInteractStart :615-628 for a VR weld grab (LerpToHandOnPickup on).</summary>
        (BasisParentConstraint constraint, Vector3 offsetPos) Grab(
            BasisPickupInteractable prop, Transform t, Vector3 weldPos, Quaternion weldRot)
        {
            t.GetPositionAndRotation(out Vector3 objectPos, out Quaternion objectRot);

            var constraint = new BasisParentConstraint
            {
                sources = new BasisConstraintSourceData[] { new() { weight = 1f } },
                Enabled = true,
            };
            constraint.SetRestPositionAndRotation(objectPos, objectRot);

            var offsetPos = (Vector3)ClosestOffsetM.Invoke(prop, new object[] { weldPos, weldRot, objectPos });
            Quaternion offsetRot = Quaternion.Inverse(weldRot) * objectRot;
            constraint.SetOffsetPositionAndRotation(0, offsetPos, offsetRot);
            constraint.UpdateSourcePositionAndRotation(0, weldPos, weldRot);
            constraint.GlobalWeight = 1f;   // the 0.05 s lerp has finished
            return (constraint, offsetPos);
        }

        /// <summary>Grab and settle, returning the prop's world position once the grab lerp has completed.</summary>
        Vector3 Seat(BasisPickupInteractable prop, Transform t, Vector3 weldPos, Quaternion weldRot)
        {
            var (constraint, _) = Grab(prop, t, weldPos, weldRot);
            Assert.IsTrue(constraint.Evaluate(out Vector3 pos, out _), "constraint must evaluate while held");
            return pos;
        }

        static Vector3 InWeldFrame(Transform t, Vector3 weldPos, Quaternion weldRot)
            => Quaternion.Inverse(weldRot) * (t.position - weldPos);

        static Vector3 NearestSurfacePoint(BasisPickupInteractable prop, Vector3 query)
        {
            Vector3 best = query;
            float bestSq = float.MaxValue;
            foreach (Collider c in prop.GetColliders())
            {
                if (c == null || !c.enabled) continue;
                Vector3 p = c.ClosestPoint(query);
                float d = (p - query).sqrMagnitude;
                if (d < bestSq) { bestSq = d; best = p; }
            }
            return best;
        }

        static Vector3 NearestVisualSurfacePoint(BasisPickupInteractable prop, Vector3 query, Collider ignore)
        {
            Vector3 best = query;
            float bestSq = float.MaxValue;
            foreach (Collider c in prop.GetColliders())
            {
                if (c == null || !c.enabled || c == ignore) continue;
                Vector3 p = c.ClosestPoint(query);
                float d = (p - query).sqrMagnitude;
                if (d < bestSq) { bestSq = d; best = p; }
            }
            return best;
        }

        static Mesh Quad()
        {
            var m = new Mesh
            {
                vertices = new[]
                {
                    new Vector3(-0.2f, -0.2f, 0f), new Vector3(0.2f, -0.2f, 0f),
                    new Vector3(0.2f, 0.2f, 0f), new Vector3(-0.2f, 0.2f, 0f),
                },
                triangles = new[] { 0, 1, 2, 0, 2, 3 },
            };
            m.RecalculateNormals();
            return m;
        }
    }
}
