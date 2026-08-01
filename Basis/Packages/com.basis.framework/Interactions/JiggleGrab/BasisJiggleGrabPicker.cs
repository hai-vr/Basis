using UnityEngine;

namespace Basis.Scripts.BasisSdk.Interactions
{
    public enum BasisJiggleTouchEdge : byte
    {
        None = 0,
        Began = 1,
        Ended = 2,
    }

    /// <summary>
    /// Turns a per-frame "is this hand on the chain" answer into begin and end events.
    ///
    /// A hand resting on a chain sits right at the edge of the grip volume and drops contact for the
    /// odd frame, so raw contact would fire a callback storm into sandboxed script. The two edges are
    /// deliberately not symmetric: a touch <b>begins the instant contact is made</b>, because a
    /// delayed reaction feels broken, while it only <b>ends once the loss of contact has persisted</b>
    /// for the dwell. Any contact inside that window cancels the pending release, so flicker collapses
    /// to a single begin and a single end.
    ///
    /// Rate-limiting the edges instead — refusing to change state within the dwell of the last change
    /// — reads as equivalent and is not: with contact alternating every frame it still flips as soon
    /// as enough time has accrued, which is a storm at half the frame rate.
    /// </summary>
    public struct BasisJiggleTouchLatch
    {
        /// <summary>The committed state, which is what begin and end are reported against.</summary>
        public bool Touching;
        /// <summary>When contact was first lost while still committed to touching.</summary>
        public float LostSince;

        public static BasisJiggleTouchLatch Fresh => new BasisJiggleTouchLatch { Touching = false, LostSince = float.NegativeInfinity };

        public BasisJiggleTouchEdge Update(bool contact, float now, float dwellSeconds)
        {
            if (contact)
            {
                LostSince = float.NegativeInfinity;
                if (Touching)
                {
                    return BasisJiggleTouchEdge.None;
                }
                Touching = true;
                return BasisJiggleTouchEdge.Began;
            }

            if (!Touching)
            {
                return BasisJiggleTouchEdge.None;
            }
            if (LostSince == float.NegativeInfinity)
            {
                LostSince = now;
                return BasisJiggleTouchEdge.None;
            }
            if (now - LostSince < dwellSeconds)
            {
                return BasisJiggleTouchEdge.None;
            }
            Touching = false;
            LostSince = float.NegativeInfinity;
            return BasisJiggleTouchEdge.Ended;
        }
    }

    /// <summary>
    /// Chooses which jiggle point a grab press takes. Kept as pure geometry with no scene, player
    /// or network dependency so the selection rules can be tested directly — the accuracy problems
    /// worth catching here are all "it picked the wrong point", which is a maths question.
    ///
    /// Two shapes of query, deliberately scored differently:
    ///
    /// • <b>Grasp</b> — a capsule from the palm to the fingertips rather than a sphere at the palm,
    ///   because a hand closes around what lies across its fingers. A sphere centred on the palm
    ///   both misses a strand resting on the fingers and grabs one floating behind the knuckles.
    ///   Nearest to the segment wins, so among several strands in the hand you get the one you are
    ///   actually touching.
    ///
    /// • <b>Point</b> — an exact perpendicular distance to the aim ray, not a march of overlapping
    ///   spheres. Marching made the result depend on the step size and let a point that merely sat
    ///   near an early sample beat one dead on the axis. Distance along the ray dominates the score
    ///   so the nearest thing you point at wins, with off-axis distance as the tiebreak.
    /// </summary>
    public static class BasisJiggleGrabPicker
    {
        /// <summary>
        /// How much a candidate's off-axis distance counts against it relative to its distance along
        /// the ray. Below 1 so "nearest along the ray" leads and being on-axis only breaks ties.
        /// </summary>
        public const float PointingOffAxisWeight = 0.5f;

        /// <summary>Shortest distance from a point to a finite segment. Degenerate segment = a point.</summary>
        public static float DistanceToSegment(Vector3 point, Vector3 segmentStart, Vector3 segmentEnd)
        {
            Vector3 along = segmentEnd - segmentStart;
            float lengthSquared = along.sqrMagnitude;
            if (lengthSquared <= 1e-10f)
            {
                return Vector3.Distance(point, segmentStart);
            }
            float t = Mathf.Clamp01(Vector3.Dot(point - segmentStart, along) / lengthSquared);
            return Vector3.Distance(point, segmentStart + along * t);
        }

        /// <summary>
        /// Scores a candidate against the closing hand. Lower is better; false means out of reach.
        /// </summary>
        public static bool TryScoreGrasp(Vector3 candidate, Vector3 palm, Vector3 fingerTip, float radius, out float score)
        {
            score = DistanceToSegment(candidate, palm, fingerTip);
            return score <= radius;
        }

        /// <summary>
        /// Scores a candidate against an aim ray. Lower is better; false means off-axis, behind the
        /// hand, or beyond the reach of the point.
        /// </summary>
        public static bool TryScorePointing(Vector3 candidate, Vector3 rayOrigin, Vector3 rayDirection,
            float maxDistance, float radius, out float score)
        {
            score = float.MaxValue;
            Vector3 direction = rayDirection.sqrMagnitude > 1e-10f ? rayDirection.normalized : Vector3.forward;
            Vector3 toCandidate = candidate - rayOrigin;

            float alongRay = Vector3.Dot(toCandidate, direction);
            if (alongRay < 0f || alongRay > maxDistance)
            {
                return false;
            }

            float offAxis = Vector3.Distance(toCandidate, direction * alongRay);
            if (offAxis > radius)
            {
                return false;
            }

            score = alongRay + offAxis * PointingOffAxisWeight;
            return true;
        }
    }
}
