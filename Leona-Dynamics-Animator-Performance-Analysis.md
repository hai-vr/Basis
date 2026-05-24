# LeonaDynamics cosmetic Animator — performance analysis

**Context:** During a 1000-CCU load test, the `LeonaDynamics` Animator on the Leona avatar was measured at roughly **1/5 of the scene's frame time**. This document explains what that animator does, why it costs what it does at scale, and three routes to bring the cost down — in increasing order of effort and payoff.

---

## TL;DR

- `LeonaDynamics` is a **second, non-humanoid Animator** on the avatar's `Armature` GameObject, separate from the humanoid `BasisAvatar.Animator`. It runs three cosmetic idle layers: tail wag, tail-adornment orbit, and random ear twitch.
- Basis's remote path **disables the humanoid animator** on every remote avatar and drives its bones via the networked job system (`BasisRemoteAvatarDriver.RemoteCalibration`, `Animator.enabled = false`). **It does not touch the secondary cosmetic animator.**
- That secondary animator is set to **`Culling Mode = Always Animate`**, so it runs full state-machine evaluation *and* transform writes every frame on **all ~1000 remote instances, on-screen or not**. That is the cost.
- **First pass (no code):** set its culling mode to *Cull Completely* and trim the layer/transition churn. Reclaims the cost of every off-screen instance immediately.
- **Real fix at scale:** the per-instance Unity Animator is the wrong tool for trivial procedural motion replicated 1000×. A **native, data-oriented batched driver** (config on the avatar, one Burst/Jobs pass over all instances) collapses N heavyweight graphs into one tight pass.
- **Cilbox was considered and rejected** for this: it's an interpreter, so a per-frame interpreted `Update` across 1000 avatars would likely be *worse* than the Animator, not better.

---

## What the animator does

`LeonaDynamics` is an `AnimatorController` with **3 layers, all weight 1, none masked** — so all three evaluate and blend every frame the animator ticks.

**Parameters:** `Settings/Idles/Tail Wag` (bool), `Settings/Idles/Tail Orbit` (bool), `Settings/Idles/Tail Wag Speed` (float), `Settings/Idles/Ear Twitch` (bool), `Settings/Idles/Ear Twitch RNG` (int).

| Layer | States | Behaviour |
|---|---|---|
| **Tail Wag** | `Wag On` / `Wag Off` | `Wag On` plays one clip with **both Speed and Time driven by `Tail Wag Speed`** — a looping back-and-forth. Toggled by the `Tail Wag` bool. |
| **Tail Orbit** | `Orbit` (default) / `Orbit On` | Slow spin of the tail adornment (`Orbit On` at speed 0.1). Toggled by `Tail Orbit`. |
| **Ear Twitch** | `Buffer` → `Interval` → `RNG` → `Twitch Left/Right` → `Reset` | The `RNG` state hosts a **`BasisParameterDriver`** StateMachineBehaviour (op type *Random*, `localOnly`) that writes a random `0–255` into `Ear Twitch RNG`. Int-threshold transitions then pick Left / Right / no-twitch. Most of the time it idles in `Buffer`/`Interval` playing a near-empty clip. |

The motion these layers produce is *procedurally trivial*: a periodic wag, a constant slow rotation, and an occasional random pose.

> Note: the `.controller` also carries **two orphaned `Locomotion` BlendTrees** not referenced by any layer — leftover sub-assets. They don't drive runtime cost but are dead weight in the asset and worth removing.

---

## Why it costs what it does at 1000 CCU

Unity's Animator is a fully-general evaluator with **high fixed per-instance overhead** — the Playable graph, `Animators.Update`, the parameter buffer, and per-frame transition evaluation (the Ear Twitch layer constantly re-checks several int thresholds). That fixed cost is paid **per Animator instance**, independent of how trivial the motion is.

The decisive detail is **where this animator sits in the Basis avatar lifecycle**:

- In `BasisRemoteAvatarDriver.RemoteCalibration` the **humanoid** animator is taken out of the loop on every remote avatar:
  ```csharp
  RemotePlayer.BasisAvatar.Animator.speed = 0;
  RemotePlayer.BasisAvatar.Animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
  ...
  RemotePlayer.BasisAvatar.Animator.enabled = false;   // bones driven by RemoteBoneJobSystem instead
  ```
  (`BasisRemoteAvatarDriver.cs`, lines ~109–112 and ~229.)

- **`LeonaDynamics` is a different component** — a second Animator on the `Armature` child — and the remote path only ever references `BasisAvatar.Animator` (the root humanoid one). So the cosmetic animator is **never disabled, never throttled, never culled** by the framework. It keeps running normally on every client's copy of every remote Leona.

- Its serialized culling mode is **`m_CullingMode: 0` = Always Animate** (verified in `LeonaDynamics.prefab` and in the avatar scene). Always Animate means the state machine evaluates **and** transforms are written **even when the avatar's renderers are off-screen**.

So at 1000 CCU you have up to ~1000 of these graphs each doing full heavyweight evaluation for near-zero motion, regardless of visibility. The cost scales with instance count, which is exactly the regime the load test exercises.

The tail and ear bones are **not** humanoid bones, so they are not part of the networked bone set — the cosmetic motion is produced *locally* on each client by this animator. That's why it has to run on remotes at all (otherwise everyone else's Leona has a frozen tail); it just shouldn't run the way it currently does.

---

## Option A — First pass: tune the existing animator (no code)

Lowest effort, immediate win, stays entirely in avatar content.

1. **Culling Mode: `Always Animate` → `Cull Completely`.**
   `Cull Completely` *fully* disables the animator when the avatar's renderers aren't visible (state machine included), versus `Cull Update Transforms`, which keeps the state machine running and only skips transform writes. For a purely cosmetic idle, `Cull Completely` is correct — off-screen instances stop costing anything, and motion resumes seamlessly when they come back on screen. In a 1000-CCU scene most instances are off-screen at any moment, so this reclaims the bulk of the cost on its own.

2. **Collapse the three layers where possible.** The layers touch disjoint bones (ears, tail, adornment), so much of the three-state-machine evaluation is avoidable. Fewer always-on layers = less per-frame evaluation for the instances that *are* on screen.

3. **Trim the Ear Twitch transition churn.** The RNG-threshold transitions are evaluated every frame while that layer is active; simplifying the graph reduces fixed per-frame work.

4. **Remove the two orphaned `Locomotion` BlendTrees** from the controller asset.

**Caveat:** culling only helps instances that are *off-screen*. A worst-case crowd test where everyone is visible at once still pays the on-screen cost — which is what Options B/C address.

---

## Option B — Native, data-oriented batched driver (the real fix at scale)

Replace the per-instance Animator graph with **one batched native system**.

- The avatar carries a small **config component** (the same way `BasisParameterDriver` is a whitelisted SDK component today): "this bone wags at frequency/amplitude X, this bone orbits at rate Y, these ear bones twitch on interval Z."
- A single Basis runtime system iterates a `NativeArray` of all registered cosmetic-idle avatars and computes every wag/orbit/twitch in **one Burst-compiled job**, writing the bone transforms in bulk — conceptually alongside the existing `RemoteBoneJobSystem` apply.
- This turns "1000 heavyweight Playable graphs" into "one tight job over 1000 transform targets," which is the only thing that meaningfully changes the slope of the cost curve at high CCU.

This is **SDK/engine-side** work, not avatar content. It's the largest effort of the three but the only one that scales to crowds. It also generalises: any avatar with simple procedural idles (tails, ears, floating accessories) could opt into the same batched path instead of shipping its own Animator.

---

## Option C — Native per-avatar driver component (middle ground)

A whitelisted **native** idle-driver MonoBehaviour doing the `sin`/rotate/interval math directly in `LateUpdate`.

- Still per-instance (no batching), but native trivial math has a tiny fraction of the Animator's fixed overhead, so it's a large win over the current setup even before culling.
- Whether avatars can carry a native whitelisted component for this depends on the avatar content whitelist — i.e. it would be an SDK-provided component the avatar references, not arbitrary user code.
- Good stepping stone if the full batched system (Option B) is more than is wanted right now; the config surface designed here could later be fed into the batched job without changing the avatar-side authoring.

---

## Why not a Cilbox avatar script

Cilbox is the right mechanism for *untrusted custom logic on an avatar* — it sandboxes arbitrary scripts. But it executes by **interpreting CIL bytecode** op-by-op: `CilboxProxy.Update()` calls `box.InterpretIID(...)` every frame, and the interpreter carries per-instruction timeout accounting. Running an interpreted per-frame `Update` across ~1000 avatars would very plausibly cost *more* than the native Animator it's meant to replace — the work simply moves from `Animators.Update` to `InterpretIID`, at a higher per-unit price. (It also exposes only `Update`/`FixedUpdate`, not `LateUpdate`, which is where bone writes ideally land relative to the rig.)

Cilbox is built for safety and flexibility of untrusted content, not for being a hot path replicated 1000×. It's the wrong tool for *this* goal.

---

## Recommended staged plan

1. **Now (Option A):** flip the cosmetic animator to `Cull Completely`, collapse layers, trim the Ear Twitch graph, drop the orphaned blend trees. Cheap, ships as avatar content, reclaims all off-screen cost.
2. **Next (Option B or C):** decide whether to invest in the batched native driver (B, scales to on-screen crowds) or a native per-avatar component (C, simpler, still a large per-instance win). The Option C config surface is forward-compatible with the Option B job, so starting at C and graduating to B is viable.

The framing worth landing with the maintainer: **the secondary cosmetic animator currently bypasses the same lifecycle management that already neutralises the humanoid animator on remotes.** Even just bringing it under that management (cull/disable when not needed) is most of the win; a batched driver is the principled long-term home for procedural avatar idles.

---

## Appendix — file references

- Animator controller: `Assets/_UserContent/Avatars/! E L I Z A/Leona/Controllers/Quest/LeonaDynamics.controller`
- Animator component (culling mode): `.../Controllers/Quest/LeonaDynamics.prefab` (`m_CullingMode: 0`) and the avatar scene `! LEONA - OPEN ME.unity`
- Remote lifecycle (humanoid animator disabled, bones job-driven): `Packages/com.basis.framework/Drivers/Remote/BasisRemoteAvatarDriver.cs` (~109–112, ~229)
- Existing whitelisted SDK StateMachineBehaviour pattern: `Packages/com.basis.sdk/Scripts/BasisParameterDriver.cs`
- Cilbox interpreter per-frame hook: `Packages/com.cnlohr.cilbox/CilboxProxy.cs` (`Update`/`FixedUpdate` → `box.InterpretIID`)
