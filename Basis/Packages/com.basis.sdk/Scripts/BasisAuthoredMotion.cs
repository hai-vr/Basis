using System;
using UnityEngine;

/// <summary>
/// Data-only avatar component declaring authored, deterministic dynamic motion on transforms
/// the humanoid rig and IK don't drive (tail/ear chains, accessories, etc.). It holds pure
/// serialized configuration and runs no per-instance per-frame <c>Update</c> — all runtime
/// evaluation happens in the batched <c>BasisAuthoredMotionSystem</c> job, which reads this
/// component at calibration. The config model mirrors <see cref="BasisParameterDriver"/>'s
/// <c>Operation[]</c> shape (an enum kind + per-kind fields).
///
/// Allow it onto an avatar by adding its type to the Content Police
/// (<c>ContentPoliceSelector.selectedTypes</c> in <c>AvatarContentPoliceSelector.asset</c>).
/// Group movements that toggle together into one component; an avatar may carry several.
/// </summary>
public class BasisAuthoredMotion : MonoBehaviour
{
    /// <summary>
    /// Raised on enable/disable so a registered motion system can flip this component's slice
    /// of its valid-mask without a per-frame poll. The system subscribes at registration; the
    /// component holds no reference to any toggle package, so any actuator that flips
    /// <see cref="Behaviour.enabled"/> (e.g. an HVR.Vixxy activation) drives it unchanged.
    /// </summary>
    public event Action<BasisAuthoredMotion, bool> EnabledStateChanged;

    public Movement[] movements = Array.Empty<Movement>();

    private void OnEnable() => EnabledStateChanged?.Invoke(this, true);
    private void OnDisable() => EnabledStateChanged?.Invoke(this, false);

    [Serializable]
    public class Movement
    {
        // Open, extensible set — new kinds slot into the system's evaluation routine without
        // disturbing registration / scheduling / culling / toggles.
        public enum Kind { Oscillate, Rotate, Orbit, RandomSelect, Sequence, Noise }
        public enum Channel { Rotation, Position, Scale }   // what Oscillate / Noise drive
        public enum Waveform { Sine, Triangle, Square, Pulse }

        public Kind kind = Kind.Oscillate;
        public string label;              // author-facing identifier only
        public bool enabled = true;       // author default; runtime toggle rides the component's own enabled
        public Vector3 axis = Vector3.up; // local axis the movement acts about

        // Oscillate — periodic motion on `channel`, optionally a travelling wave down a chain
        // (1 entry = simple sway). `waveform` selects sine (default) or triangle / square / pulse.
        public Channel channel = Channel.Rotation; // amplitude unit: deg | metres | scale-factor
        public Waveform waveform = Waveform.Sine;
        public float pulseWidth = 0.5f;   // square/pulse duty cycle (0–1)
        public Transform[] chain;
        public float amplitude = 15f;
        public float frequencyHz = 0.5f;
        public float phase = 0f;
        public float chainPhaseStep = 0f; // phase delay per element down the chain
        public float chainFalloff = 1f;   // amplitude scale per element down the chain

        // Rotate — constant angular velocity about `axis`, in place.
        public Transform target;
        public float speedDeg = 36f;      // deg/sec

        // Orbit — revolve `target` around `pivot` at `radius` (not a spin-in-place).
        public Transform pivot;
        public float radius = 0.1f;
        public float orbitSpeedDeg = 90f; // deg/sec around the pivot

        // RandomSelect — on a randomised interval pick one weighted option, ease in/out.
        public Transform selectTarget;
        public Option[] options = Array.Empty<Option>();
        public Vector2 intervalRange = new Vector2(2f, 6f);  // seconds between picks
        public float attack = 0.06f, release = 0.25f;        // ease in / out seconds
        public bool preventRepeats = true;
        public uint seed = 0;             // 0 = derive from registration index

        // Sequence — authored timeline of pose deltas; loop or one-shot. Short motion uses inline
        // keyframes; complex/converted clips reference a shared, read-only baked-curve asset.
        public Transform sequenceTarget;
        public Keyframe[] keyframes = Array.Empty<Keyframe>();
        public BasisMotionClip bakedClip; // shared baked curves; null when using inline keyframes
        public bool loop = true;

        // Noise — organic Perlin/simplex drift on `channel` about `axis`; reuses `amplitude`,
        // `chain`, `chainFalloff`, and `seed`. `noiseSpeed` sets how fast the field is sampled.
        public float noiseSpeed = 0.5f;
    }

    [Serializable]
    public class Option { public Vector3 axis; public float angleDeg; public float weight = 1f; }

    [Serializable]
    public class Keyframe { public float time; public Vector3 eulerDelta; public Vector3 positionDelta; public Vector3 scaleDelta; }
}
