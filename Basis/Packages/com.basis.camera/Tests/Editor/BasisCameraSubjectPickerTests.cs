using NUnit.Framework;
using UnityEngine;

namespace Basis.Tests.Camera
{
    public class BasisCameraSubjectPickerTests
    {
        private static Bounds Box(Vector3 centre, Vector3 size) => new Bounds(centre, size);

        [Test]
        public void IntersectRayBounds_ReportsEntryAndExitAlongTheRay()
        {
            Ray ray = new Ray(Vector3.zero, Vector3.forward);

            Assert.That(BasisCameraSubjectPicker.IntersectRayBounds(ray, Box(new Vector3(0f, 0f, 5f), Vector3.one), out float entry, out float exit), Is.True);
            Assert.That(entry, Is.EqualTo(4.5f).Within(1e-4f));
            Assert.That(exit, Is.EqualTo(5.5f).Within(1e-4f));
        }

        [Test]
        public void IntersectRayBounds_MissesABoxBesideTheRay()
        {
            Ray ray = new Ray(Vector3.zero, Vector3.forward);

            Assert.That(BasisCameraSubjectPicker.IntersectRayBounds(ray, Box(new Vector3(4f, 0f, 5f), Vector3.one), out _, out _), Is.False);
        }

        [Test]
        public void IntersectRayBounds_RejectsABoxEntirelyBehindTheRay()
        {
            Ray ray = new Ray(Vector3.zero, Vector3.forward);

            Assert.That(BasisCameraSubjectPicker.IntersectRayBounds(ray, Box(new Vector3(0f, 0f, -5f), Vector3.one), out _, out _), Is.False,
                "Clicking forwards must never focus on somebody stood behind the lens.");
        }

        [Test]
        public void IntersectRayBounds_ReportsANegativeEntryWhenTheLensIsInsideTheBox()
        {
            // A hand-held camera sits well inside its own operator's T-pose bounds, so the pick has
            // to be able to tell "inside" from "in front" — inside is not a click target.
            Ray ray = new Ray(Vector3.zero, Vector3.forward);

            Assert.That(BasisCameraSubjectPicker.IntersectRayBounds(ray, Box(Vector3.zero, new Vector3(2f, 2f, 2f)), out float entry, out float exit), Is.True);
            Assert.That(entry, Is.LessThan(0f));
            Assert.That(exit, Is.GreaterThan(0f));
            Assert.That(entry, Is.LessThan(BasisCameraSubjectPicker.MinimumEntryDistance));
        }

        [Test]
        public void IntersectRayBounds_HandlesARayParallelToASlab()
        {
            Ray ray = new Ray(new Vector3(0f, 10f, 0f), Vector3.forward);

            Assert.That(BasisCameraSubjectPicker.IntersectRayBounds(ray, Box(new Vector3(0f, 0f, 5f), Vector3.one), out _, out _), Is.False);
            Assert.That(BasisCameraSubjectPicker.IntersectRayBounds(new Ray(Vector3.zero, Vector3.forward), Box(new Vector3(0f, 0f, 5f), new Vector3(1f, 1f, 1f)), out _, out _), Is.True);
        }

        [Test]
        public void IntersectRayBounds_IsIndependentOfDirectionMagnitude()
        {
            Bounds bounds = Box(new Vector3(0f, 0f, 5f), Vector3.one);

            BasisCameraSubjectPicker.IntersectRayBounds(new Ray(Vector3.zero, Vector3.forward), bounds, out float unit, out _);
            BasisCameraSubjectPicker.IntersectRayBounds(new Ray(Vector3.zero, Vector3.forward * 7f), bounds, out float scaled, out _);

            Assert.That(scaled, Is.EqualTo(unit).Within(1e-4f),
                "Entry is a distance in metres; a longer direction vector must not shrink it.");
        }

        [Test]
        public void ResolveFocusDepth_LandsOnTheBodyRatherThanItsFrontFace()
        {
            // The front face of an avatar's bounds can be an outstretched hand. Focusing there
            // leaves the face the shot is about outside the sharp band.
            Ray ray = new Ray(Vector3.zero, Vector3.forward);
            Bounds bounds = Box(new Vector3(0f, 0f, 15f), new Vector3(2f, 2f, 10f));

            BasisCameraSubjectPicker.IntersectRayBounds(ray, bounds, out float entry, out float exit);

            Assert.That(entry, Is.EqualTo(10f).Within(1e-4f));
            Assert.That(BasisCameraSubjectPicker.ResolveFocusDepth(ray, bounds, entry, exit), Is.EqualTo(15f).Within(1e-4f));
        }

        [Test]
        public void ResolveFocusDepth_StaysInsideTheSpanTheRayActuallyCrosses()
        {
            Ray ray = new Ray(Vector3.zero, Vector3.forward);
            Bounds bounds = Box(new Vector3(0f, 0f, 15f), new Vector3(2f, 2f, 10f));

            Assert.That(BasisCameraSubjectPicker.ResolveFocusDepth(ray, bounds, 16f, 18f), Is.EqualTo(16f).Within(1e-4f));
            Assert.That(BasisCameraSubjectPicker.ResolveFocusDepth(ray, bounds, 11f, 13f), Is.EqualTo(13f).Within(1e-4f));
        }

        [Test]
        public void ResolveFocusDepth_NeverGoesBehindTheLens()
        {
            Ray ray = new Ray(Vector3.zero, Vector3.forward);
            Bounds bounds = Box(new Vector3(0f, 0f, 0f), new Vector3(2f, 2f, 2f));

            Assert.That(BasisCameraSubjectPicker.ResolveFocusDepth(ray, bounds, -1f, 1f), Is.GreaterThanOrEqualTo(0f));
        }

        [Test]
        public void TryGetPlayerBounds_RejectsAPlayerWithNoAvatar()
        {
            Assert.That(BasisCameraSubjectPicker.TryGetPlayerBounds(null, out _), Is.False);
        }

        [Test]
        public void IntersectRayCapsule_HitsTheBodyOfTheCapsule()
        {
            Ray ray = new Ray(Vector3.zero, Vector3.forward);
            Vector3 a = new Vector3(0f, -1f, 5f), b = new Vector3(0f, 1f, 5f);

            Assert.That(BasisCameraSubjectPicker.IntersectRayCapsule(ray, a, b, 0.2f, out float entry, out float axis), Is.True);
            Assert.That(axis, Is.EqualTo(5f).Within(1e-4f), "The axis depth is where the ray passes through the middle of the limb.");
            Assert.That(entry, Is.EqualTo(4.8f).Within(1e-4f));
        }

        [Test]
        public void IntersectRayCapsule_MissesJustOutsideTheRadius()
        {
            Vector3 a = new Vector3(0f, -1f, 5f), b = new Vector3(0f, 1f, 5f);

            Assert.That(BasisCameraSubjectPicker.IntersectRayCapsule(new Ray(new Vector3(0.19f, 0f, 0f), Vector3.forward), a, b, 0.2f, out _, out _), Is.True);
            Assert.That(BasisCameraSubjectPicker.IntersectRayCapsule(new Ray(new Vector3(0.21f, 0f, 0f), Vector3.forward), a, b, 0.2f, out _, out _), Is.False,
                "An arm is thin; the pick must not claim everything within the avatar's bind-pose box.");
        }

        [Test]
        public void IntersectRayCapsule_CoversTheRoundedEndsButNotBeyondThem()
        {
            Vector3 a = new Vector3(0f, 0f, 5f), b = new Vector3(0f, 1f, 5f);

            Assert.That(BasisCameraSubjectPicker.IntersectRayCapsule(new Ray(new Vector3(0f, 1.15f, 0f), Vector3.forward), a, b, 0.2f, out _, out _), Is.True);
            Assert.That(BasisCameraSubjectPicker.IntersectRayCapsule(new Ray(new Vector3(0f, 1.25f, 0f), Vector3.forward), a, b, 0.2f, out _, out _), Is.False);
        }

        [Test]
        public void IntersectRayCapsule_TreatsAZeroLengthSegmentAsASphere()
        {
            Vector3 point = new Vector3(0f, 0f, 5f);

            Assert.That(BasisCameraSubjectPicker.IntersectRayCapsule(new Ray(Vector3.zero, Vector3.forward), point, point, 0.3f, out float entry, out _), Is.True);
            Assert.That(entry, Is.EqualTo(4.7f).Within(1e-4f));
            Assert.That(BasisCameraSubjectPicker.IntersectRayCapsule(new Ray(new Vector3(0.4f, 0f, 0f), Vector3.forward), point, point, 0.3f, out _, out _), Is.False);
        }

        [Test]
        public void RaySphereOverlaps_KeepsWhatIsUnderTheRayAndDropsTheRest()
        {
            // This is the broad phase every player in the room runs through, so it has to be a
            // handful of floats — walking each avatar's renderers instead does not survive a
            // thousand-player instance.
            Assert.That(BasisCameraSubjectPicker.RaySphereOverlaps(Vector3.zero, Vector3.forward, new Vector3(0f, 0f, 30f), 1.5f, 1000f), Is.True);
            Assert.That(BasisCameraSubjectPicker.RaySphereOverlaps(Vector3.zero, Vector3.forward, new Vector3(4f, 0f, 30f), 1.5f, 1000f), Is.False);
        }

        [Test]
        public void RaySphereOverlaps_DropsAnythingBehindTheLens()
        {
            Assert.That(BasisCameraSubjectPicker.RaySphereOverlaps(Vector3.zero, Vector3.forward, new Vector3(0f, 0f, -30f), 1.5f, 1000f), Is.False);
        }

        [Test]
        public void RaySphereOverlaps_DropsAnythingPastTheDistanceCap()
        {
            // The cap is the nearer of the max focus distance and the wall the ray already hit, so
            // an occluded room's worth of players never reaches the capsule stage at all.
            Assert.That(BasisCameraSubjectPicker.RaySphereOverlaps(Vector3.zero, Vector3.forward, new Vector3(0f, 0f, 300f), 1.5f, 50f), Is.False);
            Assert.That(BasisCameraSubjectPicker.RaySphereOverlaps(Vector3.zero, Vector3.forward, new Vector3(0f, 0f, 51f), 1.5f, 50f), Is.True,
                "A body straddling the cap is still partly in front of it.");
        }

        [Test]
        public void RaySphereOverlaps_RejectsAlmostEveryoneInACrowd()
        {
            const int population = 1000;
            uint seed = 12345u;
            float Next() { seed = seed * 1664525u + 1013904223u; return (seed >> 8) / 16777216f; }

            int survivors = 0;
            for (int index = 0; index < population; index++)
            {
                Vector3 centre = new Vector3(Next() * 200f - 100f, 1f, Next() * 200f - 100f);
                if (BasisCameraSubjectPicker.RaySphereOverlaps(Vector3.zero, Vector3.forward, centre, 1.55f, 1000f)) survivors++;
            }

            Assert.That(survivors, Is.LessThan(population / 20),
                "Only bodies the ray actually passes near may reach the per-bone stage.");
        }

        [Test]
        public void MinimumEntryDistance_ClearsTheHandThatIsHoldingTheCamera()
        {
            // The operator's own wrist sits about ten centimetres off the lens. At the old
            // five-centimetre floor it was the nearest capsule to nearly every ray, so every click
            // focused on it and the focus plane collapsed to the lens minimum.
            Ray ray = new Ray(Vector3.zero, Vector3.forward);
            Vector3 wrist = new Vector3(0f, -0.05f, 0.12f);

            Assert.That(BasisCameraSubjectPicker.IntersectRayCapsule(ray, wrist, wrist, 0.1f, out float entry, out _), Is.True,
                "The hand really is under the middle of the frame — the floor is what rejects it, not a miss.");
            Assert.That(entry, Is.LessThan(BasisCameraSubjectPicker.MinimumEntryDistance));
        }

        [Test]
        public void MinimumEntryDistance_StillAllowsAFaceAtArmsLength()
        {
            // The other side of the same floor: a selfie is a subject at roughly half a metre, and
            // raising the floor must not put that out of reach.
            Ray ray = new Ray(Vector3.zero, Vector3.forward);
            Vector3 face = new Vector3(0f, 0f, 0.45f);

            Assert.That(BasisCameraSubjectPicker.IntersectRayCapsule(ray, face, face, 0.11f, out float entry, out _), Is.True);
            Assert.That(entry, Is.GreaterThan(BasisCameraSubjectPicker.MinimumEntryDistance));
        }

        [Test]
        public void ClosestRayToSegment_HandlesARayParallelToTheSegment()
        {
            // Looking straight down an outstretched arm: the closest-approach solve divides by
            // zero here unless the parallel case is taken separately.
            Ray ray = new Ray(new Vector3(0.1f, 0f, 0f), Vector3.forward);

            BasisCameraSubjectPicker.ClosestRayToSegment(ray, new Vector3(0f, 0f, 4f), new Vector3(0f, 0f, 6f), out float axis, out float distanceSquared);

            Assert.That(float.IsNaN(axis), Is.False);
            Assert.That(Mathf.Sqrt(distanceSquared), Is.EqualTo(0.1f).Within(1e-4f));
        }
    }
}
