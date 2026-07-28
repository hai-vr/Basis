using System.Collections.Generic;
using UnityEngine;

namespace Basis.Cinematics
{
    /// <summary>
    /// Whatever the camera is filming, reduced to the handful of facts every solver needs.
    /// Resolved by the caller so the director never reaches into player or network state.
    /// </summary>
    public struct BasisCameraSubject
    {
        public bool Valid;
        public Vector3 AnchorPos;
        public Vector3 LookPoint;
        public Vector3 GroundPos;
        public Quaternion Yaw;
        public float Scale;

        /// <summary>Bounding radius used by Framing mode. One subject is roughly shoulder width.</summary>
        public float Radius;

        /// <summary>Pre-smoothed. A raw frame delta makes look-ahead jitter.</summary>
        public Vector3 Velocity;
    }

    /// <summary>
    /// Reports how far the shot is clear from the subject outward. Returning false means nothing is
    /// in the way. Supplied by the caller so the solvers stay free of <c>Physics</c> and stay testable.
    /// </summary>
    public delegate bool BasisCameraOcclusionProbe(Vector3 target, Vector3 desiredCameraPos, out float freeDistanceFromTarget);

    public struct BasisCameraSolveContext
    {
        public BasisCameraSubject Subject;
        public float Fov;
        public float Aspect;
        public float DeltaTime;
        public float Time;

        public IReadOnlyList<Vector3> DollyPoints;
        public bool DollyLooped;

        /// <summary>Drives <see cref="BasisCameraAimMode.Manual"/>, in degrees.</summary>
        public Quaternion ManualRotation;

        public BasisCameraOcclusionProbe OcclusionProbe;
    }

    public struct BasisCameraPose
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public float Fov;

        public static BasisCameraPose Lerp(in BasisCameraPose a, in BasisCameraPose b, float t)
            => new BasisCameraPose
            {
                Position = Vector3.Lerp(a.Position, b.Position, t),
                Rotation = Quaternion.Slerp(a.Rotation, b.Rotation, t),
                Fov = Mathf.Lerp(a.Fov, b.Fov, t),
            };
    }

    /// <summary>Per-shot solver memory. Kept by shot id so reordering the list cannot scramble it.</summary>
    public sealed class BasisCameraShotState
    {
        public bool Initialized;
        public Vector3 Position;
        public Quaternion Rotation = Quaternion.identity;
        public float Fov = 40f;
        public float Heading;
        public float DollyPosition;
        public float OcclusionDistance;
        public bool HasOcclusionDistance;

        public void Seed(Vector3 position, Quaternion rotation, float fov)
        {
            Position = position;
            Rotation = rotation;
            Fov = fov;
            Initialized = true;
            HasOcclusionDistance = false;
        }
    }

    /// <summary>
    /// Holds the shot list, decides which is live, and blends between them. One physical camera
    /// driven by a rig of authored setups — the same split Cinemachine makes between a brain and
    /// its virtual cameras.
    /// </summary>
    public sealed class BasisCameraDirector
    {
        private readonly List<BasisCameraShot> shots = new List<BasisCameraShot>();
        private readonly Dictionary<int, BasisCameraShotState> states = new Dictionary<int, BasisCameraShotState>();

        private int nextId = 1;
        private int liveShotId = -1;
        private int outgoingShotId = -1;

        private float blendElapsed;
        private float blendDuration;
        private BasisCameraBlendStyle blendStyle = BasisCameraBlendStyle.EaseInOut;

        private BasisCameraPose output;
        private bool hasOutput;

        public IReadOnlyList<BasisCameraShot> Shots => shots;
        public int Count => shots.Count;

        /// <summary>Shot to make live regardless of priority. -1 hands control back to priority.</summary>
        public int SelectedShotId { get; set; } = -1;

        public int LiveShotId => liveShotId;
        public bool IsBlending => blendDuration > 0f && blendElapsed < blendDuration;
        public float BlendProgress => blendDuration <= 0f ? 1f : Mathf.Clamp01(blendElapsed / blendDuration);

        public BasisCameraShot AddShot(BasisCameraShot template = null)
        {
            BasisCameraShot shot = template != null ? template.Clone() : new BasisCameraShot();
            shot.id = nextId++;
            if (string.IsNullOrEmpty(shot.name))
            {
                shot.name = $"Shot {shots.Count + 1}";
            }
            shots.Add(shot);
            states[shot.id] = new BasisCameraShotState();
            return shot;
        }

        public bool RemoveShot(int id)
        {
            int index = IndexOf(id);
            if (index < 0)
            {
                return false;
            }

            shots.RemoveAt(index);
            states.Remove(id);

            if (SelectedShotId == id)
            {
                SelectedShotId = -1;
            }
            if (liveShotId == id)
            {
                liveShotId = -1;
            }
            if (outgoingShotId == id)
            {
                outgoingShotId = -1;
                blendDuration = 0f;
            }
            return true;
        }

        public void Clear()
        {
            shots.Clear();
            states.Clear();
            liveShotId = -1;
            outgoingShotId = -1;
            blendDuration = 0f;
            SelectedShotId = -1;
        }

        /// <summary>Moves a shot to a new slot in the list, clamping out-of-range requests.</summary>
        public bool MoveShot(int id, int newIndex)
        {
            int index = IndexOf(id);
            if (index < 0 || shots.Count == 0)
            {
                return false;
            }

            newIndex = Mathf.Clamp(newIndex, 0, shots.Count - 1);
            if (newIndex == index)
            {
                return false;
            }

            BasisCameraShot shot = shots[index];
            shots.RemoveAt(index);
            shots.Insert(newIndex, shot);
            return true;
        }

        public int IndexOf(int id)
        {
            for (int Index = 0; Index < shots.Count; Index++)
            {
                if (shots[Index].id == id)
                {
                    return Index;
                }
            }
            return -1;
        }

        public BasisCameraShot GetShot(int id)
        {
            int index = IndexOf(id);
            return index >= 0 ? shots[index] : null;
        }

        public BasisCameraShotState GetState(int id)
        {
            if (!states.TryGetValue(id, out BasisCameraShotState state))
            {
                state = new BasisCameraShotState();
                states[id] = state;
            }
            return state;
        }

        /// <summary>
        /// The shot that should be live: the explicit selection when it is enabled, otherwise the
        /// highest-priority enabled shot, ties broken by list order.
        /// </summary>
        public BasisCameraShot ResolveLiveShot()
        {
            if (SelectedShotId >= 0)
            {
                BasisCameraShot selected = GetShot(SelectedShotId);
                if (selected != null && selected.enabled)
                {
                    return selected;
                }
            }

            BasisCameraShot best = null;
            for (int Index = 0; Index < shots.Count; Index++)
            {
                BasisCameraShot shot = shots[Index];
                if (!shot.enabled)
                {
                    continue;
                }
                if (best == null || shot.priority > best.priority)
                {
                    best = shot;
                }
            }
            return best;
        }

        /// <summary>
        /// Places every shot at an explicit pose and continues from there, for a hand-off — the rig
        /// being switched on, or the camera being taken back from something else. The shots then
        /// ease from where the camera actually is rather than cutting to wherever they were last.
        /// </summary>
        public void SnapTo(Vector3 position, Quaternion rotation, float fov)
        {
            output = new BasisCameraPose { Position = position, Rotation = rotation, Fov = fov };
            hasOutput = true;
            blendDuration = 0f;
            blendElapsed = 0f;
            outgoingShotId = -1;

            foreach (KeyValuePair<int, BasisCameraShotState> entry in states)
            {
                entry.Value.Seed(position, rotation, fov);
            }
        }

        /// <summary>
        /// Drops every shot's accumulated state so each re-derives its own pose from the subject on
        /// the next solve. This is the teleport case, and it is the opposite of <see cref="SnapTo"/>:
        /// easing from the old pose after the subject jumps a hundred metres is the sweep being
        /// avoided, not the behaviour wanted.
        /// </summary>
        public void ReseedShots()
        {
            blendDuration = 0f;
            blendElapsed = 0f;
            outgoingShotId = -1;

            foreach (KeyValuePair<int, BasisCameraShotState> entry in states)
            {
                entry.Value.Initialized = false;
            }
        }

        public BasisCameraPose Solve(in BasisCameraSolveContext context)
        {
            BasisCameraShot live = ResolveLiveShot();
            if (live == null)
            {
                return hasOutput ? output : DefaultPose(context);
            }

            if (!hasOutput)
            {
                output = DefaultPose(context);
                hasOutput = true;
            }

            if (live.id != liveShotId)
            {
                StartBlend(live);
            }

            BasisCameraPose target = SolveShot(live, GetState(live.id), context);

            if (blendDuration > 0f && blendElapsed < blendDuration)
            {
                blendElapsed += Mathf.Max(0f, context.DeltaTime);
                float t = BasisCameraBlend.Evaluate(blendStyle, Mathf.Clamp01(blendElapsed / blendDuration));

                BasisCameraPose from = output;
                BasisCameraShot outgoing = GetShot(outgoingShotId);
                if (outgoing != null)
                {
                    from = SolveShot(outgoing, GetState(outgoing.id), context);
                }

                output = BasisCameraPose.Lerp(from, target, t);

                if (blendElapsed >= blendDuration)
                {
                    blendDuration = 0f;
                    outgoingShotId = -1;
                    output = target;
                }
            }
            else
            {
                output = target;
            }

            return output;
        }

        private void StartBlend(BasisCameraShot incoming)
        {
            bool hadLiveShot = liveShotId >= 0;
            outgoingShotId = liveShotId;
            liveShotId = incoming.id;

            BasisCameraShotState state = GetState(incoming.id);
            if (!state.Initialized)
            {
                state.Seed(output.Position, output.Rotation, output.Fov);
            }

            blendStyle = incoming.blendStyle;
            blendDuration = !hadLiveShot || incoming.blendStyle == BasisCameraBlendStyle.Cut
                ? 0f
                : Mathf.Max(0f, incoming.blendTime);
            blendElapsed = 0f;

            if (blendDuration <= 0f)
            {
                outgoingShotId = -1;
            }
        }

        private BasisCameraPose DefaultPose(in BasisCameraSolveContext context)
        {
            BasisCameraSubject subject = context.Subject;
            Vector3 position = subject.Valid ? subject.AnchorPos + subject.Yaw * new Vector3(0f, 0f, 1.4f) : Vector3.zero;
            Quaternion rotation = subject.Valid
                ? Quaternion.LookRotation((subject.LookPoint - position).normalized, Vector3.up)
                : Quaternion.identity;
            return new BasisCameraPose { Position = position, Rotation = rotation, Fov = context.Fov };
        }

        /// <summary>
        /// Runs one shot's body, aim, lens and noise stages. Public so a single shot can be solved
        /// and asserted on without standing up a director.
        /// </summary>
        public static BasisCameraPose SolveShot(BasisCameraShot shot, BasisCameraShotState state, in BasisCameraSolveContext context)
        {
            BasisCameraSubject subject = context.Subject;
            float deltaTime = Mathf.Max(0f, context.DeltaTime);
            float scale = subject.Scale > 1e-4f ? subject.Scale : 1f;

            if (!state.Initialized)
            {
                Vector3 seed = subject.Valid ? subject.AnchorPos + subject.Yaw * (shot.positionOffset * scale) : Vector3.zero;
                state.Seed(seed, Quaternion.identity, shot.overrideLens ? shot.lensFov : context.Fov);
            }

            if (!subject.Valid)
            {
                return new BasisCameraPose { Position = state.Position, Rotation = state.Rotation, Fov = state.Fov };
            }

            Vector3 anchor = BasisCameraComposer.ApplyLookAhead(subject.AnchorPos, subject.Velocity, shot.lookAheadTime, shot.lookAheadLimit);
            Vector3 lookPoint = BasisCameraComposer.ApplyLookAhead(subject.LookPoint, subject.Velocity, shot.lookAheadTime, shot.lookAheadLimit);

            float fov = shot.overrideLens ? shot.lensFov : context.Fov;

            Quaternion bindingFrame = ResolveBindingFrame(shot, subject, state.Position, anchor);
            Vector3 targetPosition = SolveBody(shot, state, context, anchor, lookPoint, bindingFrame, scale, ref fov);

            state.Position = shot.bodyMode == BasisCameraBodyMode.HardLock
                ? state.Position
                : BasisCameraDamping.ApproachInFrame(state.Position, targetPosition, bindingFrame, shot.positionDamping, deltaTime);

            state.Position = SolveOcclusion(shot, state, context, lookPoint, deltaTime);

            state.Rotation = SolveAim(shot, state, context, lookPoint, subject, fov, deltaTime);

            state.Fov = shot.DrivesLens
                ? BasisCameraDamping.Approach(state.Fov, fov, shot.positionDamping.z, deltaTime)
                : fov;

            Vector3 noisePosition = BasisCameraNoise.SamplePosition(context.Time, shot.noise);
            Vector3 noiseRotation = BasisCameraNoise.SampleRotation(context.Time, shot.noise);

            return new BasisCameraPose
            {
                Position = noisePosition == Vector3.zero
                    ? state.Position
                    : state.Position + state.Rotation * (noisePosition * scale),
                Rotation = noiseRotation == Vector3.zero
                    ? state.Rotation
                    : state.Rotation * Quaternion.Euler(noiseRotation),
                Fov = state.Fov,
            };
        }

        private static Quaternion ResolveBindingFrame(BasisCameraShot shot, in BasisCameraSubject subject, Vector3 cameraPosition, Vector3 anchor)
        {
            switch (shot.bindingMode)
            {
                case BasisCameraBindingMode.WorldSpace:
                    return Quaternion.identity;

                case BasisCameraBindingMode.SimpleFollow:
                    Vector3 flat = cameraPosition - anchor;
                    flat.y = 0f;
                    return flat.sqrMagnitude > 1e-6f
                        ? Quaternion.LookRotation(flat.normalized, Vector3.up)
                        : subject.Yaw;

                case BasisCameraBindingMode.SubjectYaw:
                default:
                    return subject.Yaw;
            }
        }

        private static Vector3 SolveBody(BasisCameraShot shot, BasisCameraShotState state, in BasisCameraSolveContext context,
            Vector3 anchor, Vector3 lookPoint, Quaternion bindingFrame, float scale, ref float fov)
        {
            float deltaTime = Mathf.Max(0f, context.DeltaTime);

            switch (shot.bodyMode)
            {
                case BasisCameraBodyMode.HardLock:
                    return state.Position;

                case BasisCameraBodyMode.Orbital:
                {
                    float targetHeading = shot.orbit.heading;
                    state.Heading = BasisCameraOrbital.DampHeading(state.Heading, targetHeading, shot.orbit.headingDamping, deltaTime);

                    BasisCameraOrbitSettings orbit = shot.orbit;
                    orbit.heading = state.Heading;
                    return BasisCameraOrbital.SolvePosition(anchor, context.Subject.Yaw, orbit, scale);
                }

                case BasisCameraBodyMode.Dolly:
                {
                    IReadOnlyList<Vector3> points = context.DollyPoints;
                    if (points == null || points.Count == 0)
                    {
                        return anchor + bindingFrame * (shot.positionOffset * scale);
                    }

                    float target;
                    if (shot.dollyAutoTrack)
                    {
                        target = BasisCameraSpline.FindClosestPosition(points, anchor, context.DollyLooped);
                    }
                    else if (shot.dollySpeed != 0f)
                    {
                        float length = BasisCameraSpline.ApproximateLength(points, context.DollyLooped);
                        float segments = BasisCameraSpline.MaxPosition(points.Count, context.DollyLooped);
                        float perMetre = length > 1e-4f ? segments / length : 0f;
                        target = state.DollyPosition + shot.dollySpeed * perMetre * deltaTime;
                    }
                    else
                    {
                        target = shot.dollyPosition;
                    }

                    state.DollyPosition = DampDollyPosition(state.DollyPosition, target, points.Count,
                        context.DollyLooped, shot.dollyDamping, deltaTime);

                    Vector3 onTrack = BasisCameraSpline.Evaluate(points, state.DollyPosition, context.DollyLooped);
                    if (shot.dollyOffset.sqrMagnitude > 1e-8f)
                    {
                        Vector3 tangent = BasisCameraSpline.EvaluateTangent(points, state.DollyPosition, context.DollyLooped);
                        onTrack += Quaternion.LookRotation(tangent, Vector3.up) * (shot.dollyOffset * scale);
                    }
                    return onTrack;
                }

                case BasisCameraBodyMode.Framing:
                {
                    Vector3 offset = shot.positionOffset * scale;
                    Vector3 direction = bindingFrame * offset;
                    if (direction.sqrMagnitude < 1e-8f)
                    {
                        return anchor;
                    }

                    float radius = Mathf.Max(0.05f, context.Subject.Radius) * scale;
                    if (shot.framingUsesZoom)
                    {
                        float distance = direction.magnitude;
                        float fitFov = BasisCameraFraming.FovToFit(radius, distance, shot.framingScreenFraction);
                        if (fitFov > 0f)
                        {
                            fov = fitFov;
                        }
                        return anchor + direction;
                    }

                    float fitDistance = BasisCameraFraming.DistanceToFit(radius, fov, context.Aspect, shot.framingScreenFraction);
                    if (fitDistance <= 0f)
                    {
                        return anchor + direction;
                    }

                    fitDistance = Mathf.Clamp(fitDistance, shot.framingMinDistance * scale, shot.framingMaxDistance * scale);
                    return anchor + direction.normalized * fitDistance;
                }

                case BasisCameraBodyMode.Transposer:
                default:
                    return anchor + bindingFrame * (shot.positionOffset * scale);
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

        private static Vector3 SolveOcclusion(BasisCameraShot shot, BasisCameraShotState state, in BasisCameraSolveContext context,
            Vector3 lookPoint, float deltaTime)
        {
            if (!shot.avoidOcclusion || context.OcclusionProbe == null)
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
                allowed = Mathf.Clamp(freeDistance - shot.occlusionPadding, shot.occlusionMinDistance, desiredDistance);
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
                state.OcclusionDistance = BasisCameraDamping.Approach(state.OcclusionDistance, allowed, shot.occlusionReturnDamping, deltaTime);
            }

            return lookPoint + offset / desiredDistance * state.OcclusionDistance;
        }

        private static Quaternion SolveAim(BasisCameraShot shot, BasisCameraShotState state, in BasisCameraSolveContext context,
            Vector3 lookPoint, in BasisCameraSubject subject, float fov, float deltaTime)
        {
            if (shot.aimMode == BasisCameraAimMode.None)
            {
                return state.Rotation;
            }

            bool hasOffset = shot.rotationOffset != Vector3.zero;
            Quaternion extra = hasOffset ? Quaternion.Euler(shot.rotationOffset) : Quaternion.identity;

            switch (shot.aimMode)
            {
                case BasisCameraAimMode.Manual:
                    return BasisCameraDamping.ApproachRotation(state.Rotation, context.ManualRotation * extra, shot.rotationDamping, deltaTime);

                case BasisCameraAimMode.MatchSubject:
                    return BasisCameraDamping.ApproachRotation(state.Rotation, subject.Yaw * extra, shot.rotationDamping, deltaTime);

                case BasisCameraAimMode.HardLookAt:
                {
                    Vector3 toTarget = lookPoint - state.Position;
                    if (toTarget.sqrMagnitude < 1e-8f)
                    {
                        return state.Rotation;
                    }
                    Quaternion target = Quaternion.LookRotation(toTarget.normalized, Vector3.up) * extra;
                    return BasisCameraDamping.ApproachRotation(state.Rotation, target, shot.rotationDamping, deltaTime);
                }

                case BasisCameraAimMode.Composer:
                default:
                {
                    // The offset has to come off before measuring where the subject sits in frame,
                    // or it is counted again every frame and the aim walks away.
                    Quaternion current = hasOffset ? state.Rotation * BasisCameraDamping.Conjugate(extra) : state.Rotation;
                    Quaternion composed = BasisCameraComposer.Solve(state.Position, current, lookPoint,
                        fov, context.Aspect, shot.composer, Vector3.up, deltaTime);
                    return hasOffset ? composed * extra : composed;
                }
            }
        }
    }
}
