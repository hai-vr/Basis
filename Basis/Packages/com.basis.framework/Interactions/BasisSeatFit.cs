using UnityEngine;

namespace Basis.Scripts.BasisSdk.Interactions
{
    /// <summary>
    /// The seat-local geometry a fit is solved against: the three authored control points plus the
    /// helper vectors <see cref="BasisSeat"/> derives from them. Snapshot it with
    /// <see cref="BasisSeat.GetFitFrame"/>.
    /// </summary>
    public struct BasisSeatFitFrame
    {
        public Vector3 Back;
        public Vector3 Knee;
        public Vector3 Foot;
        public Vector3 UpperLegDir;
        public Vector3 UpperLegPerp;
        public Vector3 LowerLegDir;
        public Vector3 LowerLegPerp;
        public Quaternion SpineRotation;
        public float SpineAngleDegrees;
        public float LegAngleDegrees;
    }

    /// <summary>
    /// The only avatar measurements a seat fit needs, in metres at the avatar's applied scale.
    /// </summary>
    public struct BasisSeatFitLegs
    {
        public float UpperLegLength;
        public float LowerLegLength;
        public float FootThickness;

        public static BasisSeatFitLegs FromBones(Vector3 upperLeg, Vector3 lowerLeg, Vector3 foot, Vector3 toe)
        {
            return new BasisSeatFitLegs
            {
                UpperLegLength = (lowerLeg - upperLeg).magnitude,
                LowerLegLength = (foot - lowerLeg).magnitude,
                FootThickness = Mathf.Max(foot.y, toe.y),
            };
        }
    }

    /// <summary>Seat-local pelvis/knee/foot targets produced by <see cref="BasisSeatFit.Solve"/>.</summary>
    public struct BasisSeatFitResult
    {
        public Vector3 Back;
        public Vector3 Knee;
        public Vector3 Foot;
    }

    /// <summary>
    /// How far an occupant may turn themselves on a seat, and in what steps — a stool they can spin on,
    /// a bench seat that only lets them glance either way, or a chair that holds them facing forward.
    /// Authored per seat on <see cref="BasisSeat"/>.
    /// </summary>
    public struct BasisSeatRotationLimits
    {
        /// <summary>
        /// Total sweep the occupant may cover, centred on the seat's own forward. Zero or less holds them
        /// facing forward (the default, and what seats did before this existed); 360 or more is a free spin.
        /// </summary>
        public float RangeDegrees;

        /// <summary>
        /// Step size the yaw is quantised to. Zero turns continuously. A step larger than the range leaves
        /// only the centre reachable.
        /// </summary>
        public float SnapDegrees;

        public static BasisSeatRotationLimits Held => new BasisSeatRotationLimits();

        public bool AllowsRotation => RangeDegrees > 0f;
        public bool IsFullCircle => RangeDegrees >= 360f;

        /// <summary>Largest yaw either way. 180 for a full circle.</summary>
        public float HalfRangeDegrees => IsFullCircle ? 180f : RangeDegrees * 0.5f;

        public BasisSeatRotationLimits(float rangeDegrees, float snapDegrees)
        {
            RangeDegrees = rangeDegrees;
            SnapDegrees = snapDegrees;
        }
    }

    /// <summary>
    /// The single seat-fit solve shared by the local seat driver and the remote seat pin, so an avatar
    /// lands on a seat in exactly the same place for its owner and for everyone else. Pure math: no
    /// avatar rig, no player, no scene lookups.
    /// </summary>
    public static class BasisSeatFit
    {
        public const float MinTravelDot = 0.05f;
        public const float MaxBackShift = 0.25f;
        public const float SphereSnapEpsilon = 0.005f;

        public const float SpineBackThicknessRatio = 0.14f;
        public const float UpperLegBackRadiusRatio = 0.14f;
        public const float UpperLegKneeRadiusRatio = 0.08f;
        public const float LowerLegKneeRadiusRatio = 0.10f;
        public const float LowerLegFootRadiusRatio = 0.06f;

        /// <summary>
        /// Derives a seat's helper vectors from its three authored control points and spine angle.
        /// Returns false when the control points are collinear, which leaves no seat plane to solve in.
        /// </summary>
        public static bool BuildFrame(Vector3 back, Vector3 foot, Vector3 knee, float spineAngleDegrees, out BasisSeatFitFrame frame)
        {
            frame = new BasisSeatFitFrame
            {
                Back = back,
                Knee = knee,
                Foot = foot,
                SpineAngleDegrees = spineAngleDegrees,
                UpperLegDir = (knee - back).normalized,
                LowerLegDir = (foot - knee).normalized,
            };

            Vector3 left = Vector3.Cross(frame.LowerLegDir, frame.UpperLegDir).normalized;
            if (left == Vector3.zero)
            {
                return false;
            }

            Vector3 spineDir = Quaternion.AngleAxis(spineAngleDegrees, left) * frame.UpperLegDir;
            frame.SpineRotation = Quaternion.LookRotation(Vector3.Cross(spineDir, left), spineDir);
            frame.UpperLegPerp = Vector3.Cross(left, frame.UpperLegDir);
            frame.LowerLegPerp = Vector3.Cross(left, frame.LowerLegDir);
            frame.LegAngleDegrees = Vector3.Angle(frame.UpperLegDir, frame.LowerLegDir);
            return true;
        }

        public static BasisSeatFitResult Solve(in BasisSeatFitFrame seat, in BasisSeatFitLegs legs)
        {
            float upperLegLength = Mathf.Max(legs.UpperLegLength, 1e-6f);
            float lowerLegLength = Mathf.Max(legs.LowerLegLength, 1e-6f);
            float totalLegLength = upperLegLength + lowerLegLength;

            float spineBackThickness = totalLegLength * SpineBackThicknessRatio;
            float upperLegBackRadius = totalLegLength * UpperLegBackRadiusRatio;
            float upperLegKneeRadius = totalLegLength * UpperLegKneeRadiusRatio;
            float lowerLegKneeRadius = totalLegLength * LowerLegKneeRadiusRatio;
            float lowerLegFootRadius = totalLegLength * LowerLegFootRadiusRatio;

            float upperArg = (upperLegBackRadius - upperLegKneeRadius) / upperLegLength;
            float lowerArg = (lowerLegKneeRadius - lowerLegFootRadius) / lowerLegLength;
            float upperLegAngleVsSeatRadians = Mathf.Asin(Mathf.Clamp(upperArg, -0.9999f, 0.9999f));
            float lowerLegAngleVsSeatRadians = Mathf.Asin(Mathf.Clamp(lowerArg, -0.9999f, 0.9999f));

            Vector3 targetFoot = seat.Foot
                                 + (seat.LowerLegPerp * lowerLegFootRadius)
                                 - (seat.LowerLegDir * legs.FootThickness);

            Vector3 targetKnee = seat.Knee
                                 + (seat.UpperLegPerp * upperLegKneeRadius)
                                 + (seat.UpperLegDir * BasisSeat.GetAdjustmentScalar(
                                     Mathf.Clamp(seat.LegAngleDegrees, 10f, 170f),
                                     lowerLegKneeRadius,
                                     upperLegKneeRadius,
                                     upperLegLength));

            Vector3 targetBack = seat.Back
                                 + (seat.UpperLegPerp * upperLegBackRadius)
                                 + (seat.UpperLegDir * BasisSeat.GetAdjustmentScalar(
                                     Mathf.Clamp(180f - seat.SpineAngleDegrees, 10f, 170f),
                                     spineBackThickness,
                                     upperLegBackRadius,
                                     upperLegLength));

            Vector3 preferredBack = targetBack;

            float upperLegAngleVsSpineRadians = upperLegAngleVsSeatRadians + Mathf.Deg2Rad * seat.SpineAngleDegrees;
            Vector3 thighDirSeatLocal = seat.SpineRotation * new Vector3(
                0f,
                Mathf.Cos(upperLegAngleVsSpineRadians),
                Mathf.Sin(upperLegAngleVsSpineRadians));

            float upperLegHorizontalTravelRatio = Mathf.Max(
                MinTravelDot,
                Mathf.Abs(Vector3.Dot(seat.UpperLegDir, thighDirSeatLocal)));

            float availableUpperLegHorizontalTravel = Vector3.Distance(
                targetKnee - seat.UpperLegPerp * upperLegKneeRadius,
                targetBack - seat.UpperLegPerp * upperLegBackRadius);

            float characterUpperLegHorizontalTravel = upperLegLength * upperLegHorizontalTravelRatio;

            if (characterUpperLegHorizontalTravel < availableUpperLegHorizontalTravel)
            {
                float delta = availableUpperLegHorizontalTravel - characterUpperLegHorizontalTravel;
                delta = Mathf.Min(delta, MaxBackShift);
                targetBack += seat.UpperLegDir * delta;
            }
            else
            {
                targetKnee += seat.UpperLegDir * (characterUpperLegHorizontalTravel - availableUpperLegHorizontalTravel);
            }

            targetBack = preferredBack + Vector3.ClampMagnitude(targetBack - preferredBack, MaxBackShift);

            float lowerLegAngleVsSpineRadians = lowerLegAngleVsSeatRadians
                                                - Mathf.Deg2Rad * (seat.SpineAngleDegrees + seat.LegAngleDegrees);
            Vector3 shinDirSeatLocal = seat.SpineRotation * new Vector3(
                0f,
                Mathf.Cos(lowerLegAngleVsSpineRadians),
                -Mathf.Sin(lowerLegAngleVsSpineRadians));

            float lowerLegVerticalTravelRatio = Mathf.Max(
                MinTravelDot,
                Mathf.Abs(Vector3.Dot(seat.LowerLegDir, shinDirSeatLocal)));

            float availableLowerLegVerticalTravel = Vector3.Distance(
                targetFoot + seat.LowerLegDir * lowerLegFootRadius,
                targetKnee + seat.LowerLegDir * lowerLegKneeRadius);

            float characterLowerLegVerticalTravel = lowerLegLength * lowerLegVerticalTravelRatio;

            if (characterLowerLegVerticalTravel < availableLowerLegVerticalTravel)
            {
                targetFoot += seat.LowerLegDir * (characterLowerLegVerticalTravel - availableLowerLegVerticalTravel);
            }
            else
            {
                targetKnee += seat.LowerLegDir * (availableLowerLegVerticalTravel - characterLowerLegVerticalTravel);

                if (characterUpperLegHorizontalTravel > availableUpperLegHorizontalTravel)
                {
                    float calfErr = Mathf.Abs(Vector3.Distance(targetKnee, targetFoot) - lowerLegLength);
                    if (calfErr > SphereSnapEpsilon)
                    {
                        targetKnee = BasisSeat.ClosestPointOnSphere(targetKnee, targetFoot, lowerLegLength);
                    }
                }

                float thighErr = Mathf.Abs(Vector3.Distance(targetBack, targetKnee) - upperLegLength);
                if (thighErr > SphereSnapEpsilon)
                {
                    Vector3 snappedBack = BasisSeat.ClosestPointOnSphere(targetBack, targetKnee, upperLegLength);
                    targetBack = preferredBack + Vector3.ClampMagnitude(snappedBack - preferredBack, MaxBackShift);

                    float thighErrAfterClamp = Mathf.Abs(Vector3.Distance(targetBack, targetKnee) - upperLegLength);
                    if (thighErrAfterClamp > (SphereSnapEpsilon * 4f))
                    {
                        targetBack = snappedBack;
                    }
                }
            }

            return new BasisSeatFitResult
            {
                Back = targetBack,
                Knee = targetKnee,
                Foot = targetFoot,
            };
        }

        /// <summary>
        /// Folds a requested occupant yaw into what the seat actually permits: wrapped, clamped to the
        /// authored range, and quantised to the authored step. A step that would land outside the range
        /// falls back to the outermost step still inside it, so the result is always both a legal step and
        /// inside the range.
        ///
        /// Only the occupant's own client resolves; the yaw it settles on is what goes on the wire and what
        /// every other client applies verbatim, so a seat whose limits are changed mid-session (or whose
        /// step does not divide 360) can never make two clients disagree about where the avatar is facing.
        /// </summary>
        public static float ResolveOccupantYaw(float requestedDegrees, in BasisSeatRotationLimits limits)
        {
            if (limits.AllowsRotation == false || float.IsNaN(requestedDegrees) || float.IsInfinity(requestedDegrees))
            {
                return 0f;
            }

            float limit = limits.HalfRangeDegrees;
            float yaw = WrapDegrees(requestedDegrees);
            if (limits.IsFullCircle == false)
            {
                yaw = Mathf.Clamp(yaw, -limit, limit);
            }

            float snap = limits.SnapDegrees;
            if (snap <= 0f)
            {
                return yaw;
            }

            float snapped = Mathf.Round(yaw / snap) * snap;
            if (limits.IsFullCircle)
            {
                return WrapDegrees(snapped);
            }

            if (Mathf.Abs(snapped) > limit)
            {
                float steps = Mathf.Floor((limit + 1e-4f) / snap);
                snapped = Mathf.Sign(snapped) * steps * snap;
            }
            return snapped;
        }

        /// <summary>
        /// Adds a turn to an occupant's accumulated request and resolves it. The raw accumulation is kept
        /// separate from the resolved yaw so a smooth turn axis still crosses snap thresholds properly —
        /// resolving in place every frame would round each tiny delta back to where it started and the
        /// occupant would never move.
        /// </summary>
        public static float AddOccupantYaw(float rawDegrees, float deltaDegrees, in BasisSeatRotationLimits limits, out float newRawDegrees)
        {
            if (limits.AllowsRotation == false)
            {
                newRawDegrees = 0f;
                return 0f;
            }

            if (float.IsNaN(deltaDegrees) || float.IsInfinity(deltaDegrees))
            {
                deltaDegrees = 0f;
            }

            float raw = rawDegrees + deltaDegrees;
            newRawDegrees = limits.IsFullCircle
                ? WrapDegrees(raw)
                : Mathf.Clamp(raw, -limits.HalfRangeDegrees, limits.HalfRangeDegrees);

            return ResolveOccupantYaw(newRawDegrees, limits);
        }

        /// <summary>Signed degrees in (-180, 180].</summary>
        public static float WrapDegrees(float degrees) => Mathf.DeltaAngle(0f, degrees);

        /// <summary>
        /// Maps a solved seat-local pelvis target onto the world hips pose that both the local rig
        /// override and the remote hips pin are driven to.
        /// </summary>
        public static void ComposeHipsWorld(
            Matrix4x4 seatLocalToWorld,
            Quaternion seatWorldRotation,
            Quaternion spineRotation,
            Vector3 seatLocalBack,
            out Vector3 hipsWorldPosition,
            out Quaternion hipsWorldRotation)
        {
            ComposeHipsWorld(seatLocalToWorld, seatWorldRotation, spineRotation, seatLocalBack, 0f,
                out hipsWorldPosition, out hipsWorldRotation, out _);
        }

        /// <summary>
        /// As above, with the occupant turned <paramref name="occupantYawDegrees"/> about their own spine
        /// axis. The pelvis is the pivot, so turning on a stool spins the occupant in place rather than
        /// sweeping them around the seat's origin. <paramref name="occupantPivot"/> carries the same
        /// rotation for anything else still expressed in seat space — the foot targets — via
        /// <see cref="RotateAboutPivot"/>.
        /// </summary>
        public static void ComposeHipsWorld(
            Matrix4x4 seatLocalToWorld,
            Quaternion seatWorldRotation,
            Quaternion spineRotation,
            Vector3 seatLocalBack,
            float occupantYawDegrees,
            out Vector3 hipsWorldPosition,
            out Quaternion hipsWorldRotation,
            out Quaternion occupantPivot)
        {
            hipsWorldPosition = seatLocalToWorld.MultiplyPoint3x4(seatLocalBack);
            Quaternion seatedRotation = seatWorldRotation * spineRotation;

            if (occupantYawDegrees == 0f)
            {
                occupantPivot = Quaternion.identity;
                hipsWorldRotation = seatedRotation;
                return;
            }

            occupantPivot = Quaternion.AngleAxis(occupantYawDegrees, seatedRotation * Vector3.up);
            hipsWorldRotation = occupantPivot * seatedRotation;
        }

        /// <summary>Turns a seat-space world point around the occupant's pelvis pivot.</summary>
        public static Vector3 RotateAboutPivot(Vector3 worldPoint, Vector3 pivot, Quaternion pivotRotation)
        {
            return pivot + pivotRotation * (worldPoint - pivot);
        }

        /// <summary>
        /// The rigid play-space offset held while seated (<c>BasisInput.OffsetCoords</c>): yaw the whole
        /// tracking space onto the seat's facing and drop the occupant's head onto the play-space origin,
        /// so sitting lands the avatar on the seat from wherever in the room the player happens to be.
        ///
        /// The vertical term anchors the head to the avatar's OWN T-pose eye height rather than leaving
        /// it riding the raw device Y. Every vertical tracking-space shift Basis injects — the play-space
        /// mover's drag, seated mode's missing-height lift, the height-mode grounding offset — lands in
        /// that same device Y, so without the anchor each one silently moved the seated torso.
        /// </summary>
        /// <param name="recentreTrackingSpace">
        /// VR only. Desktop synthesizes its eye pose from the avatar, so it keeps a zero translation and
        /// takes the yaw alignment alone.
        /// </param>
        public static void ComposePlayspaceOffset(
            Vector3 unscaledEyePosition,
            Quaternion unscaledEyeRotation,
            float deviceScale,
            float eyeTposeHeight,
            bool recentreTrackingSpace,
            out Vector3 offsetPosition,
            out Quaternion offsetRotation)
        {
            offsetRotation = Quaternion.Inverse(YawOnly(unscaledEyeRotation));

            if (recentreTrackingSpace == false)
            {
                offsetPosition = Vector3.zero;
                return;
            }

            Vector3 eyePlayspace = offsetRotation * (unscaledEyePosition * deviceScale);
            offsetPosition = -eyePlayspace;
            offsetPosition.y = eyeTposeHeight - eyePlayspace.y;
        }

        /// <summary>
        /// The vertical term of <see cref="ComposePlayspaceOffset"/> on its own, for re-anchoring after a
        /// height/scale change without re-centring a player who has since moved around the room.
        /// </summary>
        public static float ComposePlayspaceHeightOffset(
            Vector3 unscaledEyePosition,
            Quaternion offsetRotation,
            float deviceScale,
            float eyeTposeHeight)
        {
            return eyeTposeHeight - (offsetRotation * (unscaledEyePosition * deviceScale)).y;
        }

        public static Quaternion YawOnly(Quaternion rotation)
        {
            Vector3 euler = rotation.eulerAngles;
            return Quaternion.Euler(0f, euler.y, 0f);
        }

        /// <summary>
        /// Places the local avatar root so its T-pose hips land on the solved world hips pose. The
        /// remote reaches the same pose from the other end, deriving root from the pinned hips.
        /// </summary>
        public static void ComposeSeatedRoot(
            Vector3 hipsWorldPosition,
            Quaternion hipsWorldRotation,
            Quaternion avatarHipsBasis,
            Vector3 hipsTposeLocal,
            out Vector3 rootPosition,
            out Quaternion rootRotation)
        {
            rootRotation = hipsWorldRotation * Quaternion.Inverse(avatarHipsBasis);
            rootPosition = hipsWorldPosition - (rootRotation * hipsTposeLocal);
        }
    }
}
