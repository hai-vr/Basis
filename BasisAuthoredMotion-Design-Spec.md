# `BasisAuthoredMotion` — native authored-motion driver (design spec)

**Problem.** Basis avatars commonly drive *cosmetic and secondary* motion — looping idles, tail and ear movement, spinning accessories — with a Unity Animator. Each Animator carries high *fixed* per-instance overhead (Playable-graph setup and evaluation, state-machine and transition processing, the per-Animator update pass) that is independent of how trivial the motion is, and it is paid on **every replicated copy of every avatar in the instance**. That overhead scales linearly with player count, so at high CCU it becomes a substantial share of frame time: a 1000-CCU load test measured a single avatar's cosmetic Animator at roughly **1/5 of scene frame time**. The motion itself is trivially simple — the cost is almost entirely Animator machinery, not animation. This doc proposes what to build instead. (Background measurement and the options weighed: see the analysis doc in the appendix.)

**Names are provisional** (`BasisAuthoredMotion`, `BasisAuthoredMotionSystem`) — final naming is the maintainer's call.

**Scope.** This is a general facility for **authored, deterministic dynamic movement on transforms the humanoid rig and IK don't drive** — non-humanoid *rigged* bones (tail and ear chains, extra bones) and standalone accessory objects. The boundary is the humanoid/IK-driven, networked skeleton — *not* bones in general, so rigged chains like tails are squarely in scope. It's a third category of avatar motion: the rigged skeleton is the primary networked/IK-driven pose, jiggle physics adds *emergent* physics-driven follow-through, and this is the *authored* motion the avatar (or prop) declares. The three **stack rather than compete** — authored motion supplies the animated base and **jiggle physics layers on top of it** (see *Jiggle physics ordering*). Cosmetic looping idle sequences or secondary animations (tail flicks, ears twitching, accessories spinning) are among the first use cases driving this, not the limit: any set-sequence or continuously-animated movement on those non-humanoid transforms belongs here.

---

## What we're building

**One deliverable**, in two co-shipped pieces:

1. A whitelisted, **data-only** SDK component (`BasisAuthoredMotion`) the avatar carries — pure serialized configuration, no per-instance runtime `Update`.
2. A **batched, Burst-compiled job system** (`BasisAuthoredMotionSystem`) that evaluates every registered avatar's movements in one parallel pass per frame and writes the target transforms directly — no Playable graph, no state machine, no per-instance dispatch.

All runtime work happens in the shared job; the component just declares what to do. It mirrors the existing `RemoteBoneJobSystem` (`Packages/com.basis.framework/Drivers/Remote/BasisRemoteBoneDriver.cs`), which already proves the batched-transform pattern at thousand-avatar scale. Optimization is a hard requirement, not a later pass (see *Performance requirements*).

---

## Authoring surface — the component

A whitelisted SDK component following the established `BasisParameterDriver` pattern (`Packages/com.basis.sdk/Scripts/BasisParameterDriver.cs`) — a serializable array of reusable, parameterised **movements**. These are general primitives, not avatar-specific behaviours: the same set drives a tail, hair, antennae, ear, cloth strip, halo, orbiting gem, blink, or ambient micro-gesture. Any avatar declares whatever combination it needs.

```csharp
public class BasisAuthoredMotion : MonoBehaviour   // names provisional
{
    public Movement[] movements = Array.Empty<Movement>();

    [Serializable]
    public class Movement
    {
        // Open, extensible set — new kinds slot in without changing the
        // registration / scheduling model (see "Extensibility").
        public enum Kind { Oscillate, Rotate, Orbit, RandomSelect, Sequence }
        public enum Channel { Rotation, Position, Scale }  // what Oscillate drives

        public Kind kind = Kind.Oscillate;
        public string label;              // author-facing identifier only
        public bool enabled = true;       // runtime-toggleable (see open question on toggles)
        public Vector3 axis = Vector3.up; // local axis the movement acts about

        // Oscillate — periodic (sine) motion on `channel`, optionally propagated
        // down a chain to form a travelling wave (1 entry = simple sway).
        public Channel channel = Channel.Rotation; // amplitude unit: deg | metres | scale-factor
        public Transform[] chain;
        public float amplitude      = 15f;
        public float frequencyHz    = 0.5f;
        public float phase          = 0f;
        public float chainPhaseStep = 0f; // phase delay per element down the chain
        public float chainFalloff   = 1f; // amplitude scale per element down the chain

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

        // Sequence — authored timeline of pose deltas; loop or one-shot. Short
        // motion uses inline keyframes; complex/converted clips reference a
        // shared, read-only baked-curve asset instead (see Migration).
        public Transform sequenceTarget;
        public Keyframe[] keyframes = Array.Empty<Keyframe>();
        public BasisMotionClip bakedClip; // shared baked curves; null when using inline keyframes
        public bool loop = true;
    }

    [Serializable]
    public class Option   { public Vector3 axis; public float angleDeg; public float weight = 1f; }
    [Serializable]
    public class Keyframe { public float time; public Vector3 eulerDelta; public Vector3 positionDelta; public Vector3 scaleDelta; }
}
```

As a concrete mapping, a common cosmetic Animator setup — a swaying tail, an orbiting accessory, and randomly twitching ears across three layers — reduces to three movements here: a tail bone chain → `Oscillate`, an accessory circling the body → `Orbit` (or `Rotate` if it spins in place), and each ear → a `RandomSelect` over a couple of pose options. The authored config is flattened into the job system's SoA at registration.

### Supported motion types

The initial supported set. Every type is a **delta from a captured rest pose** on its channel, evaluated in the batched job.

| Type | What it does | Channel(s) | Key parameters | Example uses |
|------|--------------|-----------|----------------|--------------|
| **Oscillate** | periodic (sine) motion, optionally a travelling wave down a chain | rotation / position / scale | `axis`, `amplitude`, `frequencyHz`, `phase`, `chainPhaseStep`, `chainFalloff` | tail / hair / ear sway, floating bob, breathing & glow pulse |
| **Rotate** | constant angular velocity, spinning in place | rotation | `axis`, `speedDeg` | halos, fans, spinning gems/coins |
| **Orbit** | revolve a transform around a pivot at a radius | position (+ optional facing) | `pivot`, `radius`, `orbitSpeedDeg`, `axis` | accessory circling the body/tail, orbiting orbs or particles |
| **RandomSelect** | stochastic weighted pick among pose options on a randomised interval | rotation (pose deltas) | `options` (+`weight`), `intervalRange`, `attack`/`release`, `preventRepeats`, `seed` | ear flicks, blinks, idle micro-gestures |
| **Sequence** | authored keyframed timeline of pose deltas, looping or one-shot; inline keys for short hand-authored motion, or a shared baked-curve asset for complex/converted clips | rotation + position + scale | `keyframes` (`time` + per-channel deltas) *or* baked-clip ref, `loop` | scripted flourishes, set-piece accessory animations, **any converted AnimationClip** (see Migration) |

The math, per type:

- **Oscillate** — `value = rest ⊕ (amplitude * sin(t*frequencyHz*2π + phase))` on `axis`, where `⊕` applies to the chosen channel (rotation = `AngleAxis`, position = offset along axis, scale = scalar along axis). For a chain, element *n* uses `phase + n*chainPhaseStep` and `amplitude * chainFalloffⁿ` → a wave travelling down the chain.
- **Rotate** — `localRotation = rest * AngleAxis((t * speedDeg) mod 360, axis)`.
- **Orbit** — `localPosition = pivotLocal + radius * (cos θ, sin θ)` in the plane normal to `axis`, `θ = t * orbitSpeedDeg`; optionally face the pivot.
- **RandomSelect** — deterministic, no managed callback and no animator parameter. A per-movement `Unity.Mathematics.Random` (seeded by `seed`, or registration index) schedules the next pick from `intervalRange`, chooses a weighted `Option` (honouring `preventRepeats`), and eases its pose delta `0 → angleDeg → 0` over `attack`/`release`. This replaces the `BasisParameterDriver` state-machine-behaviour + RNG int + threshold-transition machinery entirely.
- **Sequence** — sample the timeline at `t` (wrapping when `loop`), interpolate the per-channel deltas, apply over rest. A lightweight authored clip without an Animator/Playable graph. Short sequences use inline keyframes; complex/converted clips reference a shared, read-only baked curve buffer (see Migration).

All RNG and trig is `Unity.Mathematics`, so the routine is Burst-legal as written.

### Extensibility & candidate future kinds

The kind set is open. A new type = a new `Kind` enum value + a branch in the evaluation routine + its fields in the flattened struct; a kind with a very different data shape gets its own SoA array + sub-job. Registration, scheduling, culling, and toggles are all kind-agnostic, so adding a type never disturbs the system around it — which is what makes this a general avatar-motion facility rather than a fix for one avatar.

Kinds deliberately **out of the initial set**, listed so the trajectory is visible (build when a real consumer needs them):

- **Noise** — organic drift via Perlin/simplex (smoother than `RandomSelect`, more lifelike than pure sine).
- **Oscillate waveform variants** — triangle / square / pulse as an `Oscillate` option rather than separate kinds.
- **Reactive kinds** — audio- or velocity-driven motion (need an input feed; defer until that plumbing exists).
- **LookAt / aim** — track a target or the camera (overlaps with skeletal concerns; evaluate separately).

---

## The runtime — batched Burst job system

`BasisAuthoredMotionSystem`, a static orchestrator built as a sibling to `RemoteBoneJobSystem`:

| Concern | Reuse from `RemoteBoneJobSystem` |
|---|---|
| Per-entry config | SoA `NativeList<MovementData>` (the `Movement` flattened to a blittable struct), one slot per driven transform |
| Driven transforms | flat `TransformAccessArray` across all avatars |
| RNG / select state | read-write `NativeArray` indexed by the flat slot, advanced in-job |
| Registry | `Add`/`Remove` keyed by avatar id, dense swap-back, deferred adds committed at the frame sync |
| Per-frame work | `Schedule()` → one Burst `IJobParallelForTransform` computing delta-from-rest and writing the target channel (`localRotation` / `localPosition` / `localScale`), with a uniform `time` field |
| Cull / disable | `ValidMask`-style `NativeArray<byte>` — toggled movements and culled avatars become no-ops, no array churn |
| Threading | adaptive `innerloopBatchCount = min(maxBatch, ceil(count / workerCount))` |

**Registration** — co-locate with the bone system: `BasisRemoteAvatarDriver.RemoteCalibration` already calls `RemoteBoneJobSystem.AddRemotePlayer`; the local path (`BasisLocalAvatarDriver`) is the analogue. At calibration, if the avatar has a `BasisAuthoredMotion` (`TryGetComponent`), flatten its movements + targets into the SoA and register; deregister on teardown/recalibration (the bone system's synchronous-remove rationale applies — drop the TAA entry while the transform is alive). **Rest poses** are captured at registration and stored in the SoA; movements compose as deltas from rest, never baked absolutes. Registration is driven by the calibration hook — **no scene discovery** (`FindObjectsOfType` et al.).

**Frame schedule** — schedule in the same phase as `RemoteBoneJobSystem`, after the avatar pose apply, completing before render.

**A single compute-and-write job, deliberately simpler than the bone system.** `IJobParallelForTransform` serialises transforms per hierarchy root onto one worker — that's why the 51-bone skeleton needed a compute/apply split. A movement touches ~2–6 bones per avatar, so a single compute-and-write `IJobParallelForTransform` parallelises cleanly across avatars; no split needed.

**Variable-length data** (`RandomSelect` options, `Oscillate` chains, baked `Sequence` curves) flattens into shared buffers with per-movement start/count indices (or a fixed cap), the same way the bone system holds all avatars' bones in one flat array.

**Development practice** — build the job with Burst compilation *off* first (Jobs → Burst → Enable Compilation off). It then runs as plain managed C# — steppable, `Debug.Log` works — so the math is debugged directly. Validate it visually against the reference avatar's existing cosmetic Animator (the Animator is the correctness oracle), then enable Burst (the math is already Burst-legal: `Unity.Mathematics`, no managed types in the job).

**Logging** — `BasisDebug.Log*` with a new `BasisDebug.LogTag`, never `UnityEngine.Debug`.

---

## Performance requirements (the maintainer's bar)

Maintainer feedback: an SDK driver component is acceptable, **but it must be heavily optimized.** Concrete targets this design commits to:

- **Data-only component at runtime.** `BasisAuthoredMotion` holds serialized config and runs **no per-instance `Update`** — all runtime evaluation is the single batched Burst job. (An optional editor-only preview path may evaluate in edit mode; see open questions.)
- **One job set per frame for all avatars**, parallel across workers, cost scaling with total movement count — not a per-avatar dispatch. Mirrors `RemoteBoneJobSystem`.
- **Zero per-frame GC** — persistent SoA `NativeArray`/`NativeList` + `TransformAccessArray`; no managed allocation in the frame loop.
- **Burst with fast math** (`[BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Low)]`, as `BasisRemoteBoneJob` uses).
- **Shared read-only data** — config and baked `Sequence` curves shared per avatar/clip; per-instance state is minimal (playhead, RNG state, enabled mask) and cache-packed.
- **Visibility + distance LOD.** Off-screen avatars skip via a `ValidMask` (no array churn). Because this motion is *cosmetic and non-critical*, distant avatars can update at a reduced rate (every N frames) — a temporal-LOD lever the skeletal bone system can't take, and a cheap way to cut the on-screen-crowd cost the culling stop-gap doesn't.
- **Minimal writes** — only enabled movements write; driven-transform count kept modest; the `IJobParallelForTransform` write is the floor cost.
- **Batched structural changes** — registration deferred and committed at the frame sync; removal swap-back — no mid-frame job stalls (the bone system's exact approach).

**Acceptance bar:** at the 1000-CCU profile, the whole authored-motion pass is a small fraction of frame time and demonstrably far below the per-instance Animator it replaces (~1/5 of scene frame time at that scale). Profile against that baseline.

---

## Cross-cutting design points

- **Rest-pose capture** is the correctness linchpin — capture at registration, compose all motion as deltas, never bake absolute rotations.
- **Jiggle physics ordering.** Avatars frequently run `JiggleRig` physics on the same chains authored motion drives (a tail, say), wired up in the remote driver. The authored-motion write must land **before** jiggle samples its input pose, so the two compose (authored movement = animated base, jiggle = secondary motion on top), matching how jiggle previously layered on the Animator output. Confirm the job phase orders correctly against the jiggle update (and the editor-preview path, if present).
- **Non-humanoid targets.** Driven bones (tail, ears, accessories) aren't in the networked bone set (`BasisBoneRotationCompression.SyncBoneCount` / `RemoteBoneJobSystem`) and aren't touched by local IK — the job owns them outright, so there's no write contention with the skeletal pipeline.

---

## Migration & authoring journey

If we're pitching this as a replacement for traditional Animator-driven cosmetic and secondary motion, the on-ramp from an *existing* animation has to be easy. Not all motion is parametric, so the system needs both a describable path and a faithful fallback — plus a tool that picks between them.

**What moves here, and what doesn't.** This replaces *self-contained, continuously-running* dynamic motion — looping idles, ambient sway, spins, scripted flourishes — that runs independent of gameplay state. It does **not** replace parameter/gameplay-driven graphs (locomotion blend trees, gesture/expression layers, contact reactions); those stay in the Animator. This refines the Scope boundary: the motion must be on non-humanoid transforms *and* authored/self-contained — gameplay- or parameter-driven motion stays in the Animator even when it touches a non-humanoid bone.

**Route 1 — parametric (describable motion).** A sine sway, a constant spin, an orbit: author directly as `Oscillate` / `Rotate` / `Orbit`. A few numbers, tiny data, infinitely tweakable (frequency / amplitude / phase), cheapest at runtime.

**Route 2 — baked (arbitrary authored motion).** Complex hand-keyed motion a primitive can't describe — *e.g. a precise 15-second loop rotating a bone chain through a bespoke path* — is captured as a **`Sequence` backed by a baked clip asset**: the source curves sampled to a fixed-rate, blittable buffer (per driven transform, per channel). The baked data is a **shared, read-only asset** — every instance of the avatar references the same buffer; per-instance runtime state is just a playhead. The batched Burst job samples the shared curve at each instance's playhead and writes the channel. So even a faithful recording of a complex clip scales: the heavy data is shared once, per-instance cost is one interpolated sample per bone — none of the Animator's per-instance Playable-graph / state-machine / retarget overhead. (`AnimationCurve` isn't Burst-usable directly, hence the sampled buffer; rotations bake to quaternions and `nlerp`/`slerp` in-job.)

**The converter tool — the actual on-ramp.** An editor utility takes an existing `AnimationClip` (+ its root) and emits a populated `BasisAuthoredMotion`:
- For each animated curve, a cheap fit test: near-constant-rate rotation → offer `Rotate`; near-single-frequency sinusoid → offer `Oscillate`; otherwise **bake** into a shared `Sequence` asset.
- The result mixes compact primitives where motion is describable and faithful baked sequences where it isn't — all reviewable and tweakable afterward.
- Default is faithful (bake); primitive-fitting is an opt-in *suggestion*, so nothing is silently approximated.
- It's an editor-only tool independent of the runtime, so it can land alongside the core or immediately after.

**Worked example — the 15s bone-chain loop.** The converter bakes each bone's rotation curve into one shared `Sequence` asset (sampled at the clip's framerate, looped) and plays it back in the same batched job as every other movement. If some bones turn out near-sinusoidal on inspection, the tool can offer to swap those for `Oscillate` (lighter, tweakable) — but the default reproduces the original exactly.

**Expectation-setting tradeoff:** parametric = tiny, tweakable, infinite variation; baked = faithful to any motion, larger (but shared) data, a fixed recording. Both shed the per-instance Animator overhead, which is the 1000-CCU win — so "we can always bake it" means *no* existing cosmetic or secondary animation is un-portable.

---

## Open questions for the maintainer

1. **Whitelist — ✅ RESOLVED.** Maintainer confirmed an SDK driver component is acceptable, with a hard optimization bar (see *Performance requirements*).
2. **Package home — open** (the remaining gate before scaffolding). Three options:
   - **(A — recommended) Split across existing packages.** Authoring component in `com.basis.sdk` (beside `BasisParameterDriver`, the whitelisted-component precedent); batched system in `com.basis.framework` (beside `RemoteBoneJobSystem`). Matches both precedents and keeps registration co-located with the bone system. Relies on the existing direction where the framework runtime reads SDK-defined avatar components at calibration. Trade-off: the feature spans two packages.
   - **(B) One dedicated package** (e.g. `com.basis.authoredmotion`) holding component + system + converter together. Most cohesive, clearest home for the converter, easy to version or disable as a unit. Wrinkle: the calibration registration hook lives in `com.basis.framework`, which shouldn't depend on a feature package — so the package would register via a framework-exposed "avatar ready" event rather than an inline call (a clean seam to add, but one that doesn't exist yet).
   - **(C) Both in `com.basis.framework`.** Runtime and registration co-located, simplest layering. Trade-off: the authoring component diverges from the `com.basis.sdk` convention for whitelisted avatar components.
3. **Runtime toggles.** The original exposes per-movement on/off to the avatar menu via animator bools. How should menu/expression parameters toggle `Movement.enabled` (a thin parameter→component bridge, or the component reading avatar parameters)?
4. **Jiggle update ordering** relative to the authored-motion write (see above).
5. **Editor preview.** The runtime job runs at play time, but authors need to see motion in edit mode. Provide a lightweight editor-only evaluation path on the component for preview?
6. **Converter scope.** How much should the `AnimationClip` → component tool do for a first pass — bake-only (faithful, simplest), or also auto-fit primitives (`Rotate`/`Oscillate`) where curves allow? Bake-only is the smaller lift and still makes every clip portable.

---

## Build approach

One reviewable deliverable, not a staged rollout:

1. Confirm package home (open question 2) — the last gate.
2. Build the data-only `BasisAuthoredMotion` component, the `BasisAuthoredMotionSystem` batched Burst job, and the calibration-time registration **together**. Develop the job Burst-off for debuggability, validate against the reference avatar's current Animator, then enable Burst and load-test at 1000 CCU against the *Performance requirements* acceptance bar.
3. The `AnimationClip` → component converter (Migration) lands with or immediately after the core — it's an editor-only tool, independent of the runtime.

The validation target is the Animator baseline this replaces: visually equivalent on the reference avatar, and measurably far cheaper at 1000 CCU.

---

## Appendix — references

- Analysis / rationale: `Leona-Dynamics-Animator-Performance-Analysis.md`
- Burst batched-transform template: `Packages/com.basis.framework/Drivers/Remote/BasisRemoteBoneDriver.cs` (`RemoteBoneJobSystem`, the gather/compute/apply jobs)
- Registration precedent: `Packages/com.basis.framework/Drivers/Remote/BasisRemoteAvatarDriver.cs` (`RemoteCalibration` → `RemoteBoneJobSystem.AddRemotePlayer`)
- Whitelisted SDK component pattern: `Packages/com.basis.sdk/Scripts/BasisParameterDriver.cs`
- Already applied (stop-gap): cosmetic Animator `Culling Mode` set to `Cull Completely`.
