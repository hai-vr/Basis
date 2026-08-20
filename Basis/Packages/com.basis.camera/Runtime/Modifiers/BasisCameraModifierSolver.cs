using System.Collections.Generic;
using UnityEngine;

namespace Basis.Cinematics
{
    /// <summary>
    /// Runs a <see cref="BasisCameraModifierStack"/> for one frame. Pure and static: given a stack,
    /// its state and a context it returns a pose, reaching into nothing else, which is what lets the
    /// whole of the camera's motion be asserted on without standing up a scene.
    /// </summary>
    public static class BasisCameraModifierSolver
    {
        /// <summary>Lateral speed above which the follow offset pulls in fully, in metres/second at default scale.</summary>
        public const float LateralTrackingReferenceSpeed = 1.5f;

        /// <summary>Low-pass rate for the measured lateral speed, so a single-frame spike cannot slam the offset.</summary>
        public const float LateralTrackingSpeedSmoothing = 8f;

        public static BasisCameraPose Solve(BasisCameraModifierStack stack, BasisCameraModifierState state,
            in BasisCameraSolveContext context)
        {
            if (stack == null || state == null)
            {
                return new BasisCameraPose
                {
                    Position = context.OperatorPosition,
                    Rotation = context.OperatorRotation,
                    Fov = context.Fov,
                };
            }

            float deltaTime = Mathf.Max(0f, context.DeltaTime);
            BasisCameraSubject subject = context.Subject;
            float scale = subject.Valid && subject.Scale > 1e-4f ? subject.Scale : 1f;

            if (!state.Initialized)
            {
                state.Seed(context.OperatorPosition, context.OperatorRotation, context.Fov);
            }

            Vector3 anchor = subject.AnchorPos;
            Vector3 lookPoint = subject.LookPoint;

            // Settle first and lead second: leading a jittery anchor and smoothing the result would
            // put the filter's lag on the lead as well, which is the one part of the shot that is
            // supposed to be ahead of the subject.
            if (subject.Valid && stack.HasEffect(BasisCameraEffectModifier.SteadySubject))
            {
                SteadySubject(stack, state, ref anchor, ref lookPoint, scale, deltaTime);
            }
            else
            {
                state.ResetSubjectSmoothing();
            }

            if (subject.Valid && stack.HasEffect(BasisCameraEffectModifier.LookAhead))
            {
                anchor = BasisCameraComposer.ApplyLookAhead(anchor, subject.Velocity, stack.lookAhead.time, stack.lookAhead.limit);
                lookPoint = BasisCameraComposer.ApplyLookAhead(lookPoint, subject.Velocity, stack.lookAhead.time, stack.lookAhead.limit);
            }

            float fov = context.Fov;

            SolvePosition(stack, state, context, subject, anchor, scale, deltaTime, ref fov);

            if (stack.HasEffect(BasisCameraEffectModifier.AvoidOcclusion) && subject.Valid)
            {
                state.Position = SolveOcclusion(stack, state, context, lookPoint, deltaTime);
            }
            else
            {
                state.HasOcclusionDistance = false;
            }

            // After occlusion, which moves the camera along the sight line and so has a path of its
            // own that wants sweeping.
            if (stack.HasEffect(BasisCameraEffectModifier.AvoidCollision))
            {
                state.Position = SolveCollision(stack, state, context);
            }

            SolveRotation(stack, state, context, subject, lookPoint, fov, deltaTime);

            if (stack.HasEffect(BasisCameraEffectModifier.LensOverride))
            {
                fov = stack.lens.fov;
            }

            // A vertigo move is the lens exactly cancelling the camera's own travel, so it publishes
            // the angle it worked out rather than easing toward it — damping here would let the
            // subject breathe by however far the lens was behind.
            bool dollyZoom = stack.HasEffect(BasisCameraEffectModifier.DollyZoom) && subject.Valid;
            if (dollyZoom)
            {
                state.Fov = SolveDollyZoom(stack, state, lookPoint, fov);
            }
            else
            {
                state.HasDollyZoomReference = false;
                state.Fov = stack.DrivesLens
                    ? BasisCameraDamping.Approach(state.Fov, fov, LensDamping(stack), deltaTime)
                    : fov;
            }

            state.PreviousPosition = state.Position;
            state.HasPreviousPosition = true;

            Vector3 position = state.Position;
            Quaternion rotation = state.Rotation;

            if (stack.HasEffect(BasisCameraEffectModifier.RigWeight))
            {
                rotation = ApplyRigWeight(stack, state, rotation, deltaTime);
            }
            else
            {
                state.HasRigWeight = false;
            }

            if (stack.HasEffect(BasisCameraEffectModifier.Shake))
            {
                Vector3 noisePosition = BasisCameraNoise.SamplePosition(context.Time, stack.shake);
                Vector3 noiseRotation = BasisCameraNoise.SampleRotation(context.Time, stack.shake);

                if (noisePosition != Vector3.zero)
                {
                    position += rotation * (noisePosition * scale);
                }
                if (noiseRotation != Vector3.zero)
                {
                    rotation *= Quaternion.Euler(noiseRotation);
                }
            }

            return new BasisCameraPose { Position = position, Rotation = rotation, Fov = state.Fov };
        }

        private static float LensDamping(BasisCameraModifierStack stack)
            => stack.HasEffect(BasisCameraEffectModifier.LensOverride) ? stack.lens.damping : stack.framing.damping.z;

        /// <summary>
        /// Corrects the subject toward a settled version of themselves and moves the aim point by the
        /// same amount, so where the camera stands and where it points stay the same distance apart.
        /// Filtering the two separately lets them disagree, which reads as the shot drifting off the
        /// subject as they move.
        /// </summary>
        private static void SteadySubject(BasisCameraModifierStack stack, BasisCameraModifierState state,
            ref Vector3 anchor, ref Vector3 lookPoint, float scale, float deltaTime)
        {
            Vector3 raw = anchor;

            if (!state.HasSteadyAnchor)
            {
                state.SteadyAnchor = raw;
                state.HasSteadyAnchor = true;
                return;
            }

            Vector3 held = state.SteadyAnchor;
            float smoothing = Mathf.Max(0f, stack.steady.smoothing);

            held.x = BasisCameraDamping.Approach(held.x, raw.x, smoothing, deltaTime);
            held.z = BasisCameraDamping.Approach(held.z, raw.z, smoothing, deltaTime);

            // The dead zone follows only the part of the movement that leaves it, so a subject who
            // jumps and lands back where they started never moves the shot at all.
            float deadZone = Mathf.Max(0f, stack.steady.verticalDeadZone) * scale;
            float verticalError = raw.y - held.y;
            if (Mathf.Abs(verticalError) > deadZone)
            {
                float target = raw.y - Mathf.Sign(verticalError) * deadZone;
                held.y = BasisCameraDamping.Approach(held.y, target, smoothing, deltaTime);
            }

            state.SteadyAnchor = held;

            Vector3 correction = held - raw;
            anchor += correction;
            lookPoint += correction;
        }

        /// <summary>
        /// Stops the camera short of anything solid on the path it took this frame. Nothing is eased:
        /// arriving late at a wall is still arriving at it, and the modifier that produced the
        /// movement is the one that owns how smoothly it happens.
        /// </summary>
        private static Vector3 SolveCollision(BasisCameraModifierStack stack, BasisCameraModifierState state,
            in BasisCameraSolveContext context)
        {
            if (context.SweepProbe == null || !state.HasPreviousPosition)
            {
                return state.Position;
            }

            Vector3 from = state.PreviousPosition;
            Vector3 delta = state.Position - from;
            float distance = delta.magnitude;
            if (distance <= 1e-4f)
            {
                return state.Position;
            }

            Vector3 direction = delta / distance;
            float radius = Mathf.Max(0.01f, stack.collision.radius);
            if (!context.SweepProbe(from, direction, distance, radius, out float freeDistance))
            {
                return state.Position;
            }

            // A sweep that begins already overlapping reports nothing free. Honouring that would pin
            // the camera wherever it was standing when the geometry arrived around it, with no
            // movement able to earn its way back out.
            if (freeDistance <= 1e-4f)
            {
                return state.Position;
            }

            float allowed = Mathf.Clamp(freeDistance - stack.collision.padding, 0f, distance);
            return from + direction * allowed;
        }

        /// <summary>
        /// Holds the subject at the size the shot had when the effect was fitted, letting the field
        /// of view make up whatever the camera's own travel changed.
        /// </summary>
        private static float SolveDollyZoom(BasisCameraModifierStack stack, BasisCameraModifierState state,
            Vector3 lookPoint, float fov)
        {
            float distance = Vector3.Distance(state.Position, lookPoint);
            if (distance <= 1e-3f)
            {
                return state.Fov;
            }

            if (!state.HasDollyZoomReference)
            {
                state.DollyZoomReference = distance * Mathf.Tan(Mathf.Clamp(fov, 1f, 179f) * 0.5f * Mathf.Deg2Rad);
                state.HasDollyZoomReference = true;
            }

            if (state.DollyZoomReference <= 1e-5f)
            {
                return fov;
            }

            float held = 2f * Mathf.Atan(state.DollyZoomReference / distance) * Mathf.Rad2Deg;
            return Mathf.Clamp(held, stack.dollyZoom.minFov, Mathf.Max(stack.dollyZoom.minFov, stack.dollyZoom.maxFov));
        }

        /// <summary>
        /// Springs the finished aim toward where the stack pointed it, under-damped so a fast move
        /// carries past the mark. Integrated in substeps: a stiff spring stepped once at a long frame
        /// gains energy instead of losing it, and the camera spirals rather than settling.
        /// </summary>
        private static Quaternion ApplyRigWeight(BasisCameraModifierStack stack, BasisCameraModifierState state,
            Quaternion target, float deltaTime)
        {
            if (!state.HasRigWeight)
            {
                state.RigWeightRotation = target;
                state.RigWeightVelocity = Vector3.zero;
                state.HasRigWeight = true;
                return target;
            }

            if (deltaTime <= 1e-5f)
            {
                return state.RigWeightRotation;
            }

            Quaternion error = target * BasisCameraDamping.Conjugate(state.RigWeightRotation);
            error.ToAngleAxis(out float angle, out Vector3 axis);
            angle = BasisCameraDamping.NormalizeAngle(angle);

            if (float.IsNaN(angle) || axis.sqrMagnitude < 1e-8f || Mathf.Abs(angle) < 1e-4f)
            {
                state.RigWeightVelocity = Vector3.zero;
                state.RigWeightRotation = target;
                return target;
            }

            // Past a certain distance this is no longer a move being followed, it is a cut. Springing
            // across it would swing the camera through everything in between.
            if (Mathf.Abs(angle) > RigWeightSnapAngle)
            {
                state.RigWeightVelocity = Vector3.zero;
                state.RigWeightRotation = target;
                return target;
            }

            Vector3 displacement = axis.normalized * angle;

            float omega = 2f * Mathf.PI * Mathf.Max(0.2f, stack.rigWeight.responsiveness);
            float zeta = Mathf.Lerp(1f, RigWeightLoosestDamping, Mathf.Clamp01(stack.rigWeight.bounce));

            int steps = Mathf.Clamp(Mathf.CeilToInt(omega * deltaTime / RigWeightMaxStep), 1, RigWeightMaxSubsteps);
            float step = deltaTime / steps;

            Vector3 remaining = displacement;
            Vector3 velocity = state.RigWeightVelocity;
            for (int Index = 0; Index < steps; Index++)
            {
                velocity += (omega * omega * remaining - 2f * zeta * omega * velocity) * step;
                remaining -= velocity * step;
            }

            state.RigWeightVelocity = velocity;

            Vector3 applied = displacement - remaining;
            if (applied.sqrMagnitude > 1e-10f)
            {
                state.RigWeightRotation = Quaternion.AngleAxis(applied.magnitude, applied.normalized) * state.RigWeightRotation;
            }
            return state.RigWeightRotation;
        }

        /// <summary>Beyond this the aim has cut rather than moved, and the spring is snapped instead.</summary>
        public const float RigWeightSnapAngle = 90f;

        /// <summary>Damping ratio at full bounce. Below 1 is what allows any overshoot at all.</summary>
        private const float RigWeightLoosestDamping = 0.32f;

        private const float RigWeightMaxStep = 0.3f;
        private const int RigWeightMaxSubsteps = 8;

        private static void SolvePosition(BasisCameraModifierStack stack, BasisCameraModifierState state,
            in BasisCameraSolveContext context, in BasisCameraSubject subject, Vector3 anchor, float scale,
            float deltaTime, ref float fov)
        {
            if (stack.positionModifier == BasisCameraPositionModifier.FreeFly)
            {
                state.Position = context.OperatorPosition;
                state.ResetSubjectHistory();
                return;
            }

            if (stack.positionModifier == BasisCameraPositionModifier.LockedOff)
            {
                state.ResetSubjectHistory();
                return;
            }

            bool needsSubject = BasisCameraModifiers.NeedsSubject(stack.positionModifier);
            if (needsSubject && !subject.Valid)
            {
                state.ResetSubjectHistory();
                return;
            }

            switch (stack.positionModifier)
            {
                case BasisCameraPositionModifier.FollowSubject:
                {
                    Vector3 offset = stack.follow.positionOffset;
                    offset.x *= 1f - MeasureLateralCloseIn(stack, state, subject, anchor, deltaTime);

                    Quaternion frame = ResolveBindingFrame(stack.follow.bindingMode, subject, state.Position, anchor, offset);
                    Vector3 target = anchor + frame * (offset * scale);

                    ApproachOrSnap(state, target, frame, stack.follow.damping, stack.follow.teleportDistance * scale, deltaTime);
                    break;
                }

                case BasisCameraPositionModifier.FrameSubject:
                {
                    Quaternion frame = ResolveBindingFrame(stack.framing.bindingMode, subject, state.Position, anchor,
                        stack.framing.directionOffset);
                    Vector3 direction = frame * (stack.framing.directionOffset * scale);
                    Vector3 target;

                    if (direction.sqrMagnitude < 1e-8f)
                    {
                        target = anchor;
                    }
                    else
                    {
                        float radius = Mathf.Max(0.05f, subject.Radius) * scale;
                        if (stack.framing.usesZoom)
                        {
                            float fitFov = BasisCameraFraming.FovToFit(radius, direction.magnitude, stack.framing.screenFraction);
                            if (fitFov > 0f)
                            {
                                fov = fitFov;
                            }
                            target = anchor + direction;
                        }
                        else
                        {
                            float fitDistance = BasisCameraFraming.DistanceToFit(radius, fov, context.Aspect, stack.framing.screenFraction);
                            target = fitDistance <= 0f
                                ? anchor + direction
                                : anchor + direction.normalized * Mathf.Clamp(fitDistance,
                                    stack.framing.minDistance * scale, stack.framing.maxDistance * scale);
                        }
                    }

                    state.ResetSubjectHistory();
                    ApproachOrSnap(state, target, frame, stack.framing.damping, stack.framing.teleportDistance * scale, deltaTime);
                    break;
                }

                case BasisCameraPositionModifier.Orbit:
                {
                    state.Heading = BasisCameraOrbital.DampHeading(state.Heading, stack.orbit.heading,
                        stack.orbit.headingDamping, deltaTime);
                    state.VerticalAxis = BasisCameraDamping.Approach(state.VerticalAxis, stack.orbit.verticalAxis,
                        stack.orbit.verticalDamping, deltaTime);

                    BasisCameraOrbitSettings orbit = stack.orbit;
                    orbit.heading = state.Heading;
                    orbit.verticalAxis = state.VerticalAxis;

                    state.Position = BasisCameraOrbital.SolvePosition(anchor, subject.Yaw, orbit, scale);
                    state.ResetSubjectHistory();
                    break;
                }

                case BasisCameraPositionModifier.DollyTrack:
                {
                    IReadOnlyList<Vector3> points = context.DollyPoints;
                    if (points == null || points.Count == 0)
                    {
                        state.ResetSubjectHistory();
                        return;
                    }

                    float maxPosition = BasisCameraSpline.MaxPosition(points.Count, context.DollyLooped);
                    float target;

                    switch (stack.dolly.mode)
                    {
                        case BasisCameraDollyMode.FollowSubject:
                            target = subject.Valid
                                ? BasisCameraSpline.FindClosestPosition(points, anchor, context.DollyLooped)
                                : state.DollyPosition;
                            break;

                        case BasisCameraDollyMode.Play:
                        {
                            if (!stack.dolly.playing)
                            {
                                target = state.DollyPosition;
                                break;
                            }

                            float length = BasisCameraSpline.ApproximateLength(points, context.DollyLooped);
                            float perMetre = length > 1e-4f ? maxPosition / length : 0f;
                            float normalized = maxPosition > 1e-4f ? state.DollyPosition / maxPosition : 0f;
                            float envelope = BasisCameraDollySpeed.Weight(stack.dolly, normalized, context.DollyLooped);
                            target = state.DollyPosition + stack.dolly.speed * envelope * perMetre * deltaTime;

                            // An open track has two ends and the move is over when it reaches one.
                            // A looped one never is, so it is left to wrap.
                            if (!context.DollyLooped && (target >= maxPosition || target <= 0f))
                            {
                                target = Mathf.Clamp(target, 0f, maxPosition);
                                state.DollyCompleted = true;
                            }
                            break;
                        }

                        default:
                            target = stack.dolly.position;
                            break;
                    }

                    state.DollyPosition = DampDollyPosition(state.DollyPosition, target, points.Count,
                        context.DollyLooped, stack.dolly.damping, deltaTime);

                    Vector3 onTrack = BasisCameraSpline.Evaluate(points, state.DollyPosition, context.DollyLooped);
                    if (stack.dolly.offset.sqrMagnitude > 1e-8f)
                    {
                        Vector3 tangent = BasisCameraSpline.EvaluateTangent(points, state.DollyPosition, context.DollyLooped);
                        onTrack += Quaternion.LookRotation(tangent, Vector3.up) * (stack.dolly.offset * scale);
                    }

                    state.Position = onTrack;
                    state.ResetSubjectHistory();
                    break;
                }
            }
        }

        /// <summary>
        /// How far the side offset is pulled in by the subject strafing, in 0..1. Standing still
        /// leaves the authored offset untouched, so the damping eases the camera back out when they
        /// stop. Also carries the strafe history forward, so it runs once per frame.
        /// </summary>
        private static float MeasureLateralCloseIn(BasisCameraModifierStack stack, BasisCameraModifierState state,
            in BasisCameraSubject subject, Vector3 anchor, float deltaTime)
        {
            float closeIn = 0f;

            if (stack.follow.lateralTracking > 0f && state.HasLastAnchor && deltaTime > 1e-5f)
            {
                Vector3 sideAxis = subject.Yaw * Vector3.right;
                float lateralSpeed = Vector3.Dot(anchor - state.LastAnchor, sideAxis) / deltaTime;

                state.SmoothedLateralSpeed = Mathf.Lerp(state.SmoothedLateralSpeed, lateralSpeed,
                    1f - Mathf.Exp(-LateralTrackingSpeedSmoothing * deltaTime));

                float scale = subject.Scale > 1e-4f ? subject.Scale : 1f;
                float referenceSpeed = LateralTrackingReferenceSpeed * scale;
                closeIn = stack.follow.lateralTracking * Mathf.Clamp01(Mathf.Abs(state.SmoothedLateralSpeed) / referenceSpeed);
            }
            else
            {
                state.SmoothedLateralSpeed = 0f;
            }

            state.LastAnchor = anchor;
            state.HasLastAnchor = true;
            return closeIn;
        }

        private static void ApproachOrSnap(BasisCameraModifierState state, Vector3 target, Quaternion frame,
            Vector3 damping, float snapDistance, float deltaTime)
        {
            state.Position = Vector3.Distance(state.Position, target) > snapDistance
                ? target
                : BasisCameraDamping.ApproachInFrame(state.Position, target, frame, damping, deltaTime);
        }

        public static Quaternion ResolveBindingFrame(BasisCameraBindingMode mode, in BasisCameraSubject subject,
            Vector3 cameraPosition, Vector3 anchor, Vector3 offset)
        {
            switch (mode)
            {
                case BasisCameraBindingMode.WorldSpace:
                    return Quaternion.identity;

                case BasisCameraBindingMode.SimpleFollow:
                {
                    Vector3 flat = cameraPosition - anchor;
                    flat.y = 0f;
                    if (flat.sqrMagnitude <= 1e-6f)
                    {
                        return subject.Yaw;
                    }

                    Quaternion heading = Quaternion.LookRotation(flat.normalized, Vector3.up);

                    Vector3 bearing = offset;
                    bearing.y = 0f;
                    if (bearing.sqrMagnitude <= 1e-6f)
                    {
                        return heading;
                    }

                    return heading * BasisCameraDamping.Yaw(-Mathf.Atan2(bearing.x, bearing.z) * Mathf.Rad2Deg);
                }

                case BasisCameraBindingMode.SubjectYaw:
                default:
                    return subject.Yaw;
            }
        }

        /// <summary>Damps a path position the short way round a looped track, so 0.1 to max-0.1 does not run the whole loop.</summary>
        public static float DampDollyPosition(float current, float target, int count, bool looped, float dampTime, float deltaTime)
        {
            float max = BasisCameraSpline.MaxPosition(count, looped);
            if (max <= 0f)
            {
                return 0f;
            }

            float delta = target - current;
            if (looped)
            {
                delta -= Mathf.Round(delta / max) * max;
            }

            return BasisCameraSpline.NormalizePosition(current + BasisCameraDamping.Damp(delta, dampTime, deltaTime), count, looped);
        }

        private static Vector3 SolveOcclusion(BasisCameraModifierStack stack, BasisCameraModifierState state,
            in BasisCameraSolveContext context, Vector3 lookPoint, float deltaTime)
        {
            if (context.OcclusionProbe == null)
            {
                state.HasOcclusionDistance = false;
                return state.Position;
            }

            Vector3 offset = state.Position - lookPoint;
            float desiredDistance = offset.magnitude;
            if (desiredDistance <= 1e-4f)
            {
                state.HasOcclusionDistance = false;
                return state.Position;
            }

            float allowed = desiredDistance;
            if (context.OcclusionProbe(lookPoint, state.Position, out float freeDistance))
            {
                allowed = Mathf.Clamp(freeDistance - stack.occlusion.padding, stack.occlusion.minDistance, desiredDistance);
            }

            if (!state.HasOcclusionDistance)
            {
                state.OcclusionDistance = allowed;
                state.HasOcclusionDistance = true;
            }
            else if (allowed < state.OcclusionDistance)
            {
                state.OcclusionDistance = allowed;
            }
            else
            {
                state.OcclusionDistance = BasisCameraDamping.Approach(state.OcclusionDistance, allowed,
                    stack.occlusion.returnDamping, deltaTime);
            }

            return lookPoint + offset / desiredDistance * state.OcclusionDistance;
        }

        private static void SolveRotation(BasisCameraModifierStack stack, BasisCameraModifierState state,
            in BasisCameraSolveContext context, in BasisCameraSubject subject, Vector3 lookPoint, float fov, float deltaTime)
        {
            switch (stack.rotationModifier)
            {
                case BasisCameraRotationModifier.FreeLook:
                    state.Rotation = context.OperatorRotation;
                    return;

                case BasisCameraRotationModifier.Hold:
                    return;
            }

            if (!subject.Valid)
            {
                return;
            }

            switch (stack.rotationModifier)
            {
                case BasisCameraRotationModifier.MatchSubject:
                {
                    Quaternion target = subject.Yaw * EulerOrIdentity(stack.matchSubject.rotationOffset);
                    state.Rotation = BasisCameraDamping.ApproachRotation(state.Rotation, target, stack.matchSubject.damping, deltaTime);
                    return;
                }

                case BasisCameraRotationModifier.LookAtSubject:
                {
                    Vector3 toTarget = lookPoint - state.Position;
                    if (toTarget.sqrMagnitude < 1e-8f)
                    {
                        return;
                    }
                    Quaternion target = Quaternion.LookRotation(toTarget.normalized, Vector3.up) *
                        EulerOrIdentity(stack.lookAt.rotationOffset);
                    state.Rotation = BasisCameraDamping.ApproachRotation(state.Rotation, target, stack.lookAt.damping, deltaTime);
                    return;
                }

                case BasisCameraRotationModifier.Compose:
                default:
                {
                    bool hasOffset = stack.compose.rotationOffset != Vector3.zero;
                    Quaternion extra = hasOffset ? Quaternion.Euler(stack.compose.rotationOffset) : Quaternion.identity;

                    Quaternion current = hasOffset ? state.Rotation * BasisCameraDamping.Conjugate(extra) : state.Rotation;
                    Quaternion composed = BasisCameraComposer.Solve(state.Position, current, lookPoint,
                        fov, context.Aspect, stack.compose.composer, Vector3.up, deltaTime);

                    state.Rotation = hasOffset ? composed * extra : composed;
                    return;
                }
            }
        }

        private static Quaternion EulerOrIdentity(Vector3 euler)
            => euler == Vector3.zero ? Quaternion.identity : Quaternion.Euler(euler);
    }
}
