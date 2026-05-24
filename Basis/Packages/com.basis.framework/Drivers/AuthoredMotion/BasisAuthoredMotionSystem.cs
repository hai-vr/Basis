using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;
using Movement = BasisAuthoredMotion.Movement;
using Kind = BasisAuthoredMotion.Movement.Kind;
using Channel = BasisAuthoredMotion.Movement.Channel;
using Waveform = BasisAuthoredMotion.Movement.Waveform;

/// <summary>
/// Blittable, flattened <see cref="BasisAuthoredMotion.Movement"/> plus the captured rest pose.
/// One slot per driven transform — a chain element is its own slot, with its element phase /
/// falloff baked in at registration. Parallel to the system's <c>TransformAccessArray</c>.
/// </summary>
public struct AuthoredMovementData
{
    public int kind;       // (int)Movement.Kind
    public int channel;    // (int)Movement.Channel
    public int waveform;   // (int)Movement.Waveform

    public float3 axis;
    public float amplitude;
    public float frequencyHz;
    public float phase;
    public float pulseWidth;

    public float speedDeg;       // Rotate
    public float radius;         // Orbit
    public float orbitSpeedDeg;  // Orbit
    public float3 pivotLocal;    // Orbit pivot, in the target's parent space
    public float noiseSpeed;     // Noise
    public uint seed;            // Noise / RandomSelect

    // Captured rest pose (local space); motion composes as a delta from this.
    public quaternion restRotation;
    public float3 restPosition;
    public float3 restScale;
}

/// <summary>
/// Single compute-and-write pass for all avatars' authored movements. One movement touches only
/// a few transforms, so a single <see cref="IJobParallelForTransform"/> parallelises cleanly
/// across avatars (no compute/apply split like the 51-bone skeleton needs). Every kind is a delta
/// from the captured rest pose. All math is <c>Unity.Mathematics</c>, so the routine is Burst-legal.
/// </summary>
[BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Low)]
public struct AuthoredMotionJob : IJobParallelForTransform
{
    [ReadOnly] public NativeArray<AuthoredMovementData> Movements;
    [ReadOnly] public NativeArray<byte> ValidMask;
    public float Time;

    public void Execute(int index, TransformAccess transform)
    {
        if (ValidMask[index] == 0) return;

        AuthoredMovementData m = Movements[index];
        float t = Time;

        switch ((Kind)m.kind)
        {
            case Kind.Oscillate:
            {
                float w = Wave(m.waveform, t * m.frequencyHz * 2f * math.PI + m.phase, m.pulseWidth);
                ApplyChannel(transform, m, m.amplitude * w);
                break;
            }
            case Kind.Noise:
            {
                float n = noise.snoise(new float2(t * m.noiseSpeed, m.seed * 0.7283f));
                ApplyChannel(transform, m, m.amplitude * n);
                break;
            }
            case Kind.Rotate:
            {
                float angle = math.radians((t * m.speedDeg) % 360f);
                transform.localRotation = math.mul(m.restRotation, quaternion.AxisAngle(math.normalizesafe(m.axis), angle));
                break;
            }
            case Kind.Orbit:
            {
                float theta = math.radians(t * m.orbitSpeedDeg);
                float3 n = math.normalizesafe(m.axis, new float3(0f, 1f, 0f));
                float3 u = math.cross(n, new float3(0f, 1f, 0f));
                if (math.lengthsq(u) < 1e-6f) u = math.cross(n, new float3(1f, 0f, 0f));
                u = math.normalizesafe(u);
                float3 v = math.cross(n, u);
                transform.localPosition = m.pivotLocal + m.radius * (math.cos(theta) * u + math.sin(theta) * v);
                break;
            }

            // TODO (iterative Burst-off pass): RandomSelect needs a per-slot RNG + interval/ease
            // state buffer; Sequence needs the shared baked-curve buffer + a per-instance playhead.
            // Both add a side NativeArray the job reads — wired once the deterministic kinds are
            // validated against the reference avatar. No-op until then.
            case Kind.RandomSelect:
            case Kind.Sequence:
            default:
                break;
        }
    }

    static void ApplyChannel(TransformAccess transform, in AuthoredMovementData m, float value)
    {
        float3 axis = math.normalizesafe(m.axis, new float3(0f, 1f, 0f));
        switch ((Channel)m.channel)
        {
            case Channel.Rotation:
                transform.localRotation = math.mul(m.restRotation, quaternion.AxisAngle(axis, math.radians(value)));
                break;
            case Channel.Position:
                transform.localPosition = m.restPosition + axis * value;
                break;
            case Channel.Scale:
                transform.localScale = m.restScale + axis * value;
                break;
        }
    }

    // phase is in radians; pulseWidth is the square/pulse duty cycle (0–1).
    static float Wave(int waveform, float phase, float pulseWidth)
    {
        switch ((Waveform)waveform)
        {
            case Waveform.Sine: return math.sin(phase);
            case Waveform.Triangle: { float x = math.frac(phase / (2f * math.PI)); return 4f * math.abs(x - 0.5f) - 1f; }
            case Waveform.Square: { float x = math.frac(phase / (2f * math.PI)); return x < pulseWidth ? 1f : -1f; }
            case Waveform.Pulse: { float x = math.frac(phase / (2f * math.PI)); return x < pulseWidth ? 1f : 0f; }
            default: return math.sin(phase);
        }
    }
}

/// <summary>
/// Static orchestrator for authored-motion evaluation — a sibling to <c>RemoteBoneJobSystem</c>.
/// Holds the persistent SoA + <see cref="TransformAccessArray"/> for every registered avatar's
/// driven transforms and schedules one batched Burst pass per frame. Drives transforms outside the
/// networked humanoid skeleton, so there's no write contention with the bone pipeline.
///
/// <para>Registration is calibration-driven (no scene discovery): the local/remote avatar drivers
/// call <see cref="Register"/> for each <see cref="BasisAuthoredMotion"/> on the avatar and
/// <see cref="Unregister"/> on teardown/recalibration. Authoritative state lives in managed
/// <c>Registration</c> records (rest poses captured once at registration); a structural change
/// rebuilds the native containers from them.</para>
/// </summary>
public static class BasisAuthoredMotionSystem
{
    sealed class Registration
    {
        public BasisAuthoredMotion Component;
        public Transform[] Targets;          // one per slot
        public AuthoredMovementData[] Data;  // parallel to Targets
        public bool[] MovementEnabled;       // per-slot author default (Movement.enabled)
        public bool ComponentEnabled;        // mirrors the component's runtime enabled state
        public int Offset;                   // current start index in the native containers
    }

    // Persistent SoA, parallel to sTargets.
    static NativeList<AuthoredMovementData> sMovements;
    static NativeList<byte> sValidMask;
    static TransformAccessArray sTargets;

    static readonly List<Registration> sRegistrations = new List<Registration>();
    static readonly Dictionary<BasisAuthoredMotion, Registration> sLookup = new Dictionary<BasisAuthoredMotion, Registration>();

    static JobHandle sPending;
    static bool sInitialized;

    public static int SlotCount => sInitialized ? sMovements.Length : 0;

    public static void Initialize(int initialCapacity = 0)
    {
        if (sInitialized) return;
        sMovements = new NativeList<AuthoredMovementData>(initialCapacity, Allocator.Persistent);
        sValidMask = new NativeList<byte>(initialCapacity, Allocator.Persistent);
        sTargets = new TransformAccessArray(math.max(1, initialCapacity));
        sRegistrations.Clear();
        sLookup.Clear();
        sInitialized = true;
    }

    public static void Dispose()
    {
        if (!sInitialized) return;
        CompletePending();
        if (sMovements.IsCreated) sMovements.Dispose();
        if (sValidMask.IsCreated) sValidMask.Dispose();
        if (sTargets.isCreated) sTargets.Dispose();
        sRegistrations.Clear();
        sLookup.Clear();
        sInitialized = false;
    }

    /// <summary>
    /// Registers every movement on <paramref name="component"/>, capturing rest poses from the
    /// avatar's current (calibration TPose) state. Re-registering an already-known component
    /// refreshes it. Safe to call with a null/empty component.
    /// </summary>
    public static void Register(BasisAuthoredMotion component)
    {
        if (component == null) return;
        if (!sInitialized) Initialize();
        if (sLookup.ContainsKey(component)) Unregister(component);

        Registration reg = Build(component);
        if (reg == null || reg.Data.Length == 0) return;

        sRegistrations.Add(reg);
        sLookup[component] = reg;
        component.EnabledStateChanged += OnEnabledStateChanged;
        Rebuild();
    }

    public static void Unregister(BasisAuthoredMotion component)
    {
        if (!sInitialized || component == null) return;
        if (!sLookup.TryGetValue(component, out Registration reg)) return;
        component.EnabledStateChanged -= OnEnabledStateChanged;
        sRegistrations.Remove(reg);
        sLookup.Remove(component);
        Rebuild();
    }

    /// <summary>
    /// Schedules the single compute-and-write pass for all registered movements. Call once per
    /// frame, before the jiggle updater's LateUpdate samples the bones (so authored motion is the
    /// animated base and jiggle layers on top). Returns the handle for dependency chaining;
    /// the caller (or the next <see cref="Schedule"/>) completes it via <see cref="Complete"/>.
    /// </summary>
    public static JobHandle Schedule()
    {
        if (!sInitialized || sMovements.Length == 0) return default;

        // Complete the previous frame's writes before rescheduling over the same containers.
        CompletePending();

        // A destroyed driven transform is auto-removed from the TransformAccessArray, desyncing it
        // from the parallel SoA — rebuild (pruning dead entries) to resync before scheduling.
        if (sTargets.length != sMovements.Length)
        {
            Rebuild();
            if (sMovements.Length == 0) return default;
        }

        // TODO: a shared/networked clock would keep remote copies bit-identical; Time.timeAsDouble
        // is adequate while validating the deterministic kinds locally.
        float time = (float)Time.timeAsDouble;

        sPending = new AuthoredMotionJob
        {
            Movements = sMovements.AsDeferredJobArray(),
            ValidMask = sValidMask.AsDeferredJobArray(),
            Time = time,
        }.Schedule(sTargets);

        return sPending;
    }

    public static void Complete(JobHandle handle)
    {
        handle.Complete();
        CompletePending();
    }

    static void CompletePending()
    {
        sPending.Complete();
        sPending = default;
    }

    // ── Internals ──────────────────────────────────────────────────────────────

    static Registration Build(BasisAuthoredMotion component)
    {
        var data = new List<AuthoredMovementData>();
        var targets = new List<Transform>();
        var movementEnabled = new List<bool>();

        Movement[] movements = component.movements;
        for (int i = 0; i < movements.Length; i++)
        {
            Movement mv = movements[i];
            uint seed = mv.seed != 0 ? mv.seed : (uint)(i + 1);

            switch (mv.kind)
            {
                case Kind.Oscillate:
                case Kind.Noise:
                    // Chain kinds: one slot per element, element phase/falloff baked in.
                    if (mv.chain != null)
                    {
                        for (int n = 0; n < mv.chain.Length; n++)
                        {
                            Transform tf = mv.chain[n];
                            if (tf == null) continue;
                            AuthoredMovementData d = Base(mv, seed, tf);
                            d.phase = mv.phase + n * mv.chainPhaseStep;
                            d.amplitude = mv.amplitude * Mathf.Pow(mv.chainFalloff, n);
                            AddSlot(data, targets, movementEnabled, d, tf, mv.enabled);
                        }
                    }
                    break;

                case Kind.Rotate:
                    if (mv.target != null)
                        AddSlot(data, targets, movementEnabled, Base(mv, seed, mv.target), mv.target, mv.enabled);
                    break;

                case Kind.Orbit:
                    if (mv.target != null)
                    {
                        AuthoredMovementData d = Base(mv, seed, mv.target);
                        Vector3 pivotWorld = mv.pivot != null ? mv.pivot.position : mv.target.position;
                        d.pivotLocal = mv.target.parent != null
                            ? (float3)mv.target.parent.InverseTransformPoint(pivotWorld)
                            : (float3)pivotWorld;
                        AddSlot(data, targets, movementEnabled, d, mv.target, mv.enabled);
                    }
                    break;

                case Kind.RandomSelect:
                    // TODO: side state buffer (see job). Slot reserved so the target is tracked.
                    if (mv.selectTarget != null)
                        AddSlot(data, targets, movementEnabled, Base(mv, seed, mv.selectTarget), mv.selectTarget, mv.enabled);
                    break;

                case Kind.Sequence:
                    // TODO: shared baked-curve buffer + per-instance playhead (see job).
                    if (mv.sequenceTarget != null)
                        AddSlot(data, targets, movementEnabled, Base(mv, seed, mv.sequenceTarget), mv.sequenceTarget, mv.enabled);
                    break;
            }
        }

        if (data.Count == 0) return null;

        return new Registration
        {
            Component = component,
            Targets = targets.ToArray(),
            Data = data.ToArray(),
            MovementEnabled = movementEnabled.ToArray(),
            ComponentEnabled = component.isActiveAndEnabled,
            Offset = 0,
        };
    }

    // Builds the common fields and captures the rest pose from the driven transform.
    static AuthoredMovementData Base(Movement mv, uint seed, Transform tf)
    {
        return new AuthoredMovementData
        {
            kind = (int)mv.kind,
            channel = (int)mv.channel,
            waveform = (int)mv.waveform,
            axis = mv.axis,
            amplitude = mv.amplitude,
            frequencyHz = mv.frequencyHz,
            phase = mv.phase,
            pulseWidth = mv.pulseWidth,
            speedDeg = mv.speedDeg,
            radius = mv.radius,
            orbitSpeedDeg = mv.orbitSpeedDeg,
            noiseSpeed = mv.noiseSpeed,
            seed = seed,
            restRotation = tf.localRotation,
            restPosition = tf.localPosition,
            restScale = tf.localScale,
        };
    }

    static void AddSlot(List<AuthoredMovementData> data, List<Transform> targets, List<bool> enabledList,
        AuthoredMovementData d, Transform tf, bool movementEnabled)
    {
        data.Add(d);
        targets.Add(tf);
        enabledList.Add(movementEnabled);
    }

    // Rebuilds the native containers from the managed registrations after a structural change.
    // O(total slots), only on register/unregister (rare, calibration-time) — the per-frame
    // Schedule never rebuilds.
    static void Rebuild()
    {
        CompletePending();

        // Prune registrations whose component was destroyed (Unity fake-null) without an
        // explicit Unregister — keeps the containers from carrying dead avatars.
        for (int i = sRegistrations.Count - 1; i >= 0; i--)
        {
            if (sRegistrations[i].Component == null)
            {
                sLookup.Remove(sRegistrations[i].Component);
                sRegistrations.RemoveAt(i);
            }
        }

        int total = 0;
        for (int i = 0; i < sRegistrations.Count; i++) total += sRegistrations[i].Data.Length;

        sMovements.Clear();
        sValidMask.Clear();
        if (sTargets.isCreated) sTargets.Dispose();
        sTargets = new TransformAccessArray(math.max(1, total));

        for (int r = 0; r < sRegistrations.Count; r++)
        {
            Registration reg = sRegistrations[r];
            reg.Offset = sMovements.Length;
            for (int i = 0; i < reg.Data.Length; i++)
            {
                Transform tf = reg.Targets[i];
                if (tf == null) continue; // UGC: a target may have been destroyed
                sMovements.Add(reg.Data[i]);
                sTargets.Add(tf);
                sValidMask.Add((byte)(reg.ComponentEnabled && reg.MovementEnabled[i] ? 1 : 0));
            }
        }
    }

    // Toggle-system-agnostic: any actuator that flips the component's enabled state lands here
    // (via BasisAuthoredMotion.EnabledStateChanged), and we patch just this component's mask slice.
    static void OnEnabledStateChanged(BasisAuthoredMotion component, bool enabled)
    {
        if (!sLookup.TryGetValue(component, out Registration reg)) return;
        reg.ComponentEnabled = enabled;

        CompletePending(); // the job reads ValidMask
        for (int i = 0; i < reg.Data.Length; i++)
        {
            int slot = reg.Offset + i;
            if (slot < sValidMask.Length)
                sValidMask[slot] = (byte)(enabled && reg.MovementEnabled[i] ? 1 : 0);
        }
    }
}
