using Basis.Scripts.Drivers;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace Basis.IK
{
    /// <summary>
    /// Pure Burst half of the virtual spine: solver params/state, the per-frame job, and the static
    /// math. The managed gather/scatter shell is <see cref="BasisLocalVirtualSpineDriver"/>.
    /// </summary>
    [BurstCompile]
    public static class BasisVirtualSpineCore
    {
        // Hybrid hips XZ model — replaces the former HipsXZFollowBlend lerp with an anatomy-aware
        // counterbalance + foot-pendulum. See ComputeRealisticHipsXZBurst for details.

        /// <summary>How far the head can get from the support base without the user having stepped, as a
        /// fraction of their standing head height. No longer a threshold — it is the SCALE the follow law is
        /// written in, so "a stance radius behind" means the same thing on every avatar and at every crouch
        /// depth. See HeadBaselineFollowRateGain.</summary>
        private const float StanceRadiusFrac = 0.12f;

        /// <summary>Extra stance radius per metre the head has DESCENDED, so a crouch is not mistaken for a step.
        /// "A lean cannot reach past a stance radius" holds for a standing lean but NOT for a squat: a real one
        /// leans the trunk ~35 deg and carries the head ~0.16 of standing height forward with the feet planted,
        /// well past the 0.12 radius, so the base concluded "stepped", chased the head, and left the hips forward
        /// of the player once they stood back up. Widening the radius under a crouch now softens the follow law
        /// rather than deferring a switch, but it buys the same thing. Measured squat kinematics (Kasahara 2024 joint angles through sagittal FK) put the head's
        /// forward travel at 0.55-0.70x its vertical drop across the whole depth range, so allowing the larger of
        /// those covers a legitimate crouch at any depth. Zero at standing height, so ordinary stepping is
        /// completely unaffected.</summary>
        private const float CrouchLeanAllowanceFrac = 0.70f;

        /// <summary>Rate (s⁻¹) the support base follows the head when it is already sitting on it. This is the
        /// floor of the follow law, and it is what keeps the base from being dragged around by tracker noise
        /// and head micro-motion: at 2 s⁻¹ a stray centimetre takes about half a second to be adopted, so
        /// jitter averages out instead of walking the base. Low, but never zero — zero is a dead zone, and a
        /// dead zone is what stranded the pelvis in the first place.</summary>
        private const float HeadBaselineFollowRateRest = 2f;

        /// <summary>Extra follow rate (s⁻¹) per stance radius the base has fallen behind the head. THE knob for
        /// "the hips do not follow enough": raising it tightens the pelvis to the player everywhere at once,
        /// with no threshold introduced anywhere, because the lag it produces is continuous in the distance
        /// (it solves L·(rest + gain·L/radius) = v). Measured on the real job at 250: the pelvis trails a
        /// 1.2 m/s walk by 1.5 cm and is back under the player 178 ms after a step ends, while a 1 cm head
        /// nudge still only moves it 37% in the first frame — tight, but not welded. 120 gave 2.5 cm and
        /// 333 ms; 350 buys 1.2 cm and 133 ms and starts to read as a snap. Raise if it feels dragged, lower
        /// if the body feels stuck to the head.</summary>
        private const float HeadBaselineFollowRateGain = 250f;





        /// <summary>How much hips track the head's deviation from baseline. 0 = pure counterbalance
        /// (hips never move from baseline), 1 = legacy "follow head fully". 0.25 keeps a small forward
        /// translation while still reading as a real spine bend.</summary>
        private const float CounterbalanceFollowFrac = 0.25f;
        /// <summary>Lateral (body-right) counterpart to <see cref="CounterbalanceFollowFrac"/>. A sideways head
        /// shift is a WEIGHT SHIFT, not a bend, so the pelvis follows most of the way to stay stacked over the
        /// feet. Following it at the low sagittal rate is what left the hips lagging ~75% behind a left/right
        /// move and skewed the torso into a phantom rotation (headset-only, no foot roles). Set equal to
        /// CounterbalanceFollowFrac to restore the former isotropic behaviour exactly.</summary>
        private const float CounterbalanceLateralFollowFrac = 0.8f;
        /// <summary>When both feet are tracked, hips sit at feet-midpoint XZ + this fraction toward
        /// the head. Approximates an inverted-pendulum lean — small because legs are nearly vertical
        /// even under significant torso lean.</summary>
        private const float FootPendulumLeanFrac = 0.20f;

        /// <summary>Head yaw speed (deg/s) at or below which the torso yaw deadzone re-centers on the
        /// current heading. Above this the head counts as "still turning" so the cone stays open and the
        /// torso keeps catching up; at/below it the head is treated as stopped.</summary>
        private const float TorsoYawRelockSpeedDeg = 6f;

        /// <summary>Per-frame solver inputs packed on the main thread (settings, cues, calibration).</summary>
        public struct SpineSolveParams
        {
            public float Dt;
            public float Scale;
            /// <summary>
            /// The play-space mover's vertical offset expressed in scaled bone-sim space
            /// (VerticalOffset × DeviceScale). Every device pose — head included — carries this shift, so
            /// every "standing" reference measured against the floor-anchored T-pose (StandingHeadLocalY,
            /// StandingHipsLocalY, ChestTposeY/SpineTposeY, TposeHips) must be lifted by it too. Without it
            /// a space-dragged player reads as a phantom squat/bend: the pelvis posture law holds the hips
            /// (and the whole untracked leg chain hanging off them) up to half the offset away from the
            /// player's real body, and the chest/spine Y-pins miss by the full offset — which is what kept
            /// the calibration lock-in guides from ever latching legs/hips after a play-space drag.
            /// Body-size normalisations (posture d/f denominators, the stance radius) stay UN-lifted — the
            /// lift moves the body, it does not resize it.
            /// </summary>
            public float TrackingLiftY;
            public float4x4 ParentMatrix;
            public quaternion ParentRotation;
            public quaternion EyeRot;

            public float3 HeadTargetPos;
            public quaternion HeadTargetRot;
            public float3 NeckTargetPos;
            public quaternion NeckTargetRot;
            public float3 ChestTargetPos;
            public quaternion ChestTargetRot;
            public float3 SpineTargetPos;
            public quaternion SpineTargetRot;

            public float3 HeadScaledOffset;
            public float3 NeckScaledOffset;
            public float3 ChestScaledOffset;
            public float3 SpineScaledOffset;

            public float ChestTposeY;
            public float SpineTposeY;
            public float3 TposeHips;

            public float3 LeftFootPos;
            public float3 RightFootPos;
            public byte LeftFootTracked;
            public byte RightFootTracked;

            public float ChestPitchFrac;
            public float ChestRollFrac;
            public float SpinePitchFrac;
            public float SpineRollFrac;
            public float NeckRotationSpeed;
            public float ChestRotationSpeed;
            public float SpineRotationSpeed;
            public float HipsRotationSpeed;
            /// <summary>Authored T-pose eye-minus-HEAD lever; the arm the Eye->Head lock swings the eye
            /// around, and therefore the exact displacement that lock attributes to a gaze.</summary>
            public float3 EyeFromHeadTpose;
            /// <summary>How much of the gaze-induced eye swing to remove before the stance leash sees it.
            /// 0 = the old behaviour (the leash reads a nod as a step). See BasisHeadPitchSwingCore.</summary>
            public float GazeSwingRemoval;
            public float HipsForwardBias;
            /// <summary>How much of a look-UP's lever swing is removed when the head->neck lever is
            /// re-attached. 0 = the old rigid re-attachment. See BasisNeckCueCore.</summary>
            public float NeckExtensionDamp;
            public float TorsoYawDeadzoneDeg;
            public float TorsoYawBlendSpeed;

            public byte HipsFreeze;
            public byte IsLocomoting;

            public float LenTotal;
            public float TChest;
            public float TSpine;

            public float StandingHipsLocalY;
            public float StandingHeadLocalY;
            /// <summary>The eye device position (playspace-local) — the one point that does NOT orbit when
            /// the view yaws. The stance leash tracks this; every body-relative arm is added after it.</summary>
            public float3 EyePos;
            /// <summary>The avatar's authored eye→hips horizontal arm at T-pose, applied to the leashed eye
            /// baseline in the torso's yaw frame. Reproduces the avatar's own standing spine curve.</summary>
            public float3 HipsAnchorOffsetLocal;
            /// <summary>The avatar's authored eye→head horizontal arm at T-pose. Locates where the head
            /// RESTS over the support base so the posture model's lean measures travel, not authoring.</summary>
            public float3 HeadRestFromEyeLocal;
            // 1 = the fitted pelvis posture model (BasisPelvisPostureModel), 0 = the legacy exponential
            // saturation. A toggle and not a slider: these are two different laws, not two ends of one.
            public byte PostureModel;
            public float HipsCompressionStrength;
            public float HipsMaxDropMeters;
        }

        /// <summary>Persistent spine solver state carried across frames (low-pass + yaw deadzone).</summary>
        public struct SpineSolveState
        {
            public float3 HeadBaselineXZ;
            public byte HeadBaselineInitialized;
            /// <summary>The user's OWN resting head height, used as the reference the crouch allowance
            /// measures its drop against. Rise-only and capped at the avatar's standing head height — see
            /// where it is maintained in the job for why it is not simply the T-pose head.</summary>
            public float StandingHeadRefY;

            public byte TorsoYawInitialized;
            public float TorsoYawAnchorDeg;
            public float PrevHeadYawDeg;
            public byte TorsoYawBroken;
            public float TorsoFollow;
        }

        /// <summary>
        /// Burst spine solve. Aligns head/neck, synthesizes hips from neck + preserved length and bias,
        /// then fills chest/spine along the chain. Operates entirely on the IO buffer + params + state;
        /// no managed access. A faithful port of the former managed OnSimulate body.
        /// </summary>
        [BurstCompile]
        public struct BasisVirtualSpineSolveJob : IJob
        {
            [NativeDisableContainerSafetyRestriction]
            public NativeArray<BasisBoneSimState> States;
            public NativeArray<SpineSolveState> State;
            public SpineSolveParams P;
            public int IdxHead;
            public int IdxNeck;
            public int IdxChest;
            public int IdxSpine;
            public int IdxHips;

            public void Execute()
            {
                BasisBoneSimState head = States[IdxHead];
                BasisBoneSimState neck = States[IdxNeck];
                BasisBoneSimState chest = States[IdxChest];
                BasisBoneSimState spine = States[IdxSpine];
                BasisBoneSimState hips = States[IdxHips];
                SpineSolveState s = State[0];

                float dt = P.Dt;
                bool freeze = P.HipsFreeze != 0;

                // 1) HEAD & NECK (top cues)
                quaternion eyeRot = P.EyeRot;
                head.OutgoingRotation = eyeRot;

                quaternion neckCurrent = neck.OutgoingRotation;
                SmoothSlerpBurst(in neckCurrent, in eyeRot, P.NeckRotationSpeed, dt, out quaternion neckRot);
                neck.OutgoingRotation = neckRot;

                ComposePosition(in P.HeadTargetPos, in P.HeadTargetRot, in P.HeadScaledOffset, out float3 headPos);
                head.OutgoingPosition = headPos;
                ApplyWorldAndLastBurst(ref head, in P.ParentMatrix, in P.ParentRotation);

                float3 rawUp = math.mul(P.ParentMatrix, new float4(0f, 1f, 0f, 0f)).xyz;
                NormalizeSafeWithFallback(in rawUp, new float3(0f, 1f, 0f), out float3 worldUp);

                // The neck is the Head->Neck rotational lock: head position + head rotation * the T-pose
                // head->neck lever. That is the SAME estimate BasisEerieMovement's ComputeNeckCue makes, and
                // it carries the same defect -- swinging the lever by the whole gaze assumes the nod pivoted
                // at the neck bone, which a look-UP does not. Left alone it walks this neck (and with it the
                // chest/spine chord strung between neck and hips, and the chest IK target riding on it) out
                // in front of the body and up. Same core, so the two estimates cannot drift apart. Look-down
                // and pure yaw are bit-identical.
                float3 neckPos0 = BasisNeckCueCore.Solve(P.NeckTargetPos, P.NeckTargetRot, P.NeckScaledOffset,
                    worldUp, P.NeckExtensionDamp);
                neck.OutgoingPosition = neckPos0;
                ApplyWorldAndLastBurst(ref neck, in P.ParentMatrix, in P.ParentRotation);

                float3 neckPosWorld = neck.OutgoingPosition;

                // 2) HIPS: build from neck and preserved span

                ExtractYawBurst(in eyeRot, out quaternion headYawFromEye);

                bool isLocomoting = P.IsLocomoting != 0;
                quaternion torsoYawTarget = ComputeTorsoYawTargetBurst(ref s, in headYawFromEye, P.TorsoYawDeadzoneDeg, P.TorsoYawBlendSpeed, isLocomoting, dt);

                float3 tposeHips = P.TposeHips;
                float biasScale = P.HipsForwardBias * P.Scale;

                float3 headPosWorld = head.OutgoingPosition;
                // The hips anchor decomposes into a TRANSLATION-ONLY reference and a BODY-RELATIVE arm, and
                // the split is load-bearing:
                //   * The stance leash tracks the EYE DEVICE -- the only yaw-invariant point. Every derived
                //     bone (head = eye - R*eyeOffset, neck, any "anchor" built from them) ORBITS the eye
                //     when the view yaws (desktop pins the eye on the capsule axis, so turning swings the
                //     head/neck through circles of their authored eye-arm radii). Leashing an orbiting
                //     point made the hips wander with facing and settle differently per direction -- "the
                //     offset is changing as we rotate" -- and a 180-deg turn could exceed the stance radius
                //     outright, permanently re-seating the standing spot.
                //   * THE AVATAR'S OWN authored eye->hips horizontal arm is added AFTER the leash, rotated
                //     by the torso yaw. Turning in place rotates the arm smoothly and the leash never sees
                //     it; the standing chain lands in the avatar's authored rest shape (head/neck/hips in
                //     their authored relationships -- this rig authors its hips 1.7 cm ahead of its neck,
                //     its lumbar curve), so the FBIK solves it with no synthetic bend: a human spine,
                //     correct to the avatar used. Anchoring under the raw head bone (pelvis-leading
                //     recline) and plumb under the neck (forward bow on lordotic rigs) both leaned, because
                //     each ignored the avatar.
                float3 eyePosDevice = P.EyePos;
                // Crouch depth for the stance radius, measured off the HEAD (vertical drop is a head
                // quantity) and lifted into the tracked frame exactly as ComputeHipsPosition does --
                // otherwise a play-space lift reads as a permanent phantom crouch.
                //
                // The reference is the user's OWN resting head height, NOT the avatar's T-pose head. Measuring
                // straight against the T-pose conflated two different things: a live squat (transient, and the
                // reason CrouchLeanAllowanceFrac exists) and a CONSTANT gap between the tracked head and the
                // avatar's authored one -- eye-height calibration drift, or a developer sat in a real chair.
                // The constant gap is not a crouch, but it was read as one and inflated the stance radius by
                // 0.70x it FOREVER: a 45 cm sit turns a 19 cm radius into ~51 cm, the support base then never
                // concludes the user has stepped, and the pelvis is free to park anywhere inside half a metre.
                // Rise-only, and capped at the calibrated standing height: it climbs to wherever the head
                // actually rests (absorbing any constant offset within a frame) but can never exceed the T-pose
                // head, so a correctly-calibrated standing user is bit-identical to before and a real squat
                // still reads its full depth. Seeded with the baseline, so a height/scale change re-seeds it.
                float standingHeadLifted = P.StandingHeadLocalY + P.TrackingLiftY;
                float headRestCandidate = math.min(headPosWorld.y, standingHeadLifted);
                s.StandingHeadRefY = s.HeadBaselineInitialized == 0
                    ? headRestCandidate
                    : math.max(s.StandingHeadRefY, headRestCandidate);
                float stanceHeadDrop = math.max(0f, s.StandingHeadRefY - headPosWorld.y);
                // ⭐ THE EYE IS YAW-INVARIANT BUT NOT PITCH-INVARIANT, AND THE LEASH ONLY NEEDED THE FIRST.
                // The leash exists to estimate WHERE THE USER IS STANDING, and it tracks the eye because every
                // derived bone orbits the eye under view yaw. But a head does not pitch about itself: the HMD
                // rides the neck's lever arm, so a look-up carries it ~8 cm BACKWARD and a look-down carries it
                // further forward again -- with the player's feet welded to the floor. The follow law is fast by
                // design (rate 2 + 250*dist/radius adopts ~2/3 of an 8 cm move in a single frame at 90 Hz), so
                // the pelvis rode the gaze almost 1:1: look up, the pelvis walks backward out from under the
                // player, the trunk is left leaning forward to reach a head that has not moved, and once the
                // hips->head span passes the chain's reach the spine CCD projects the head onto its reach sphere
                // and the head comes off the HMD entirely. That is the "big bend forwards, and the head is
                // pulled away from the camera" report.
                //
                // So subtract the swing the gaze itself accounts for and leash what is left, which is the part
                // that really was a step. BasisHeadPitchSwingCore already owns this model -- the desktop eye ADDS
                // exactly this carry to a pinned eye, because there the HMD is not there to produce it. Same core,
                // same constants, opposite direction.
                float3 leashEyePos = eyePosDevice;
                if (P.GazeSwingRemoval > 0f)
                {
                    float3 gazeFwd = math.mul(eyeRot, new float3(0f, 0f, 1f));
                    float gazeHorizMag = math.sqrt(gazeFwd.x * gazeFwd.x + gazeFwd.z * gazeFwd.z);
                    // POSITIVE = looking down, matching the core's documented convention.
                    float gazePitchDeg = math.degrees(math.atan2(-gazeFwd.y, gazeHorizMag));
                    YawDegrees(in headYawFromEye, out float gazeYawDeg);

                    BasisHeadPitchSwingInput swing;
                    swing.PitchDeg = gazePitchDeg;
                    swing.YawDeg = gazeYawDeg;
                    swing.EyeFromNeck = P.EyeFromHeadTpose;
                    swing.Strength = P.GazeSwingRemoval;
                    // 1, not the desktop 0.35: that fudge exists because BasisDesktopEye predicts the swing
                    // off the NECK lever, where the raw geometry over-reports a look-up. Here the lever IS the
                    // lock's own, so the rigid value is the self-consistent one and scaling it would put the
                    // removal back out of step with the lock it is meant to cancel.
                    swing.BackwardScale = 1f;
                    BasisHeadPitchSwingCore.Solve(swing, out BasisHeadPitchSwingResult swingResult);
                    // The core emits Vector3.zero on every degenerate/NaN path, so this is a no-op there.
                    leashEyePos -= (float3)swingResult.Offset;
                }

                float3 desiredHipsXZ = ComputeRealisticHipsXZBurst(ref s, leashEyePos, dt, P.StandingHeadLocalY, stanceHeadDrop, in torsoYawTarget, P.LeftFootPos, P.RightFootPos, P.LeftFootTracked != 0, P.RightFootTracked != 0, out float3 supportXZ);
                float3 hipsArm = math.mul(torsoYawTarget, P.HipsAnchorOffsetLocal);
                desiredHipsXZ += new float3(hipsArm.x, 0f, hipsArm.z);
                // The posture model's lean must measure the head's TRAVEL, not the avatar's authoring or the
                // view yaw: offset the support base to where the head RESTS over it (the authored eye->head
                // arm in the torso frame). Without this the lean carried a facing-dependent phantom of up to
                // the head's eye-arm radius. Feet-tracked support (the pendulum branch) is left as-is -- a
                // measured feet midpoint matches the corpus convention the model was fitted in.
                if (!(P.LeftFootTracked != 0 && P.RightFootTracked != 0))
                {
                    float3 headRestArm = math.mul(torsoYawTarget, P.HeadRestFromEyeLocal);
                    supportXZ += new float3(headRestArm.x, 0f, headRestArm.z);
                }

                ComputeHipsPosition(
                    in neckPosWorld,
                    in headPosWorld,
                    in supportXZ,
                    in worldUp,
                    P.LenTotal,
                    in torsoYawTarget,
                    biasScale,
                    in desiredHipsXZ,
                    freeze,
                    in tposeHips,
                    P.StandingHipsLocalY,
                    P.StandingHeadLocalY,
                    P.TrackingLiftY,
                    P.PostureModel != 0,
                    P.HipsCompressionStrength,
                    P.HipsMaxDropMeters,
                    out float3 hipsPos);

                quaternion hipsRotTarget = freeze ? quaternion.identity : torsoYawTarget;
                quaternion hipsCurrent = hips.OutgoingRotation;
                SmoothSlerpBurst(in hipsCurrent, in hipsRotTarget, P.HipsRotationSpeed, dt, out quaternion hipsSmoothed);
                ExtractYawBurst(in hipsSmoothed, out quaternion hipsYaw);

                hips.OutgoingRotation = hipsYaw;
                hips.OutgoingPosition = hipsPos;
                ApplyWorldAndLastBurst(ref hips, in P.ParentMatrix, in P.ParentRotation);

                // 3) Fill the middle: chest & spine positions and rotations
                ExtractYawBurst(in neckRot, out quaternion neckYaw);

                float3 hipsPosReadback = hips.OutgoingPosition;
                float3 neckPos = neck.OutgoingPosition;
                float3 neckToHips = hipsPosReadback - neckPos;

                if (math.lengthsq(neckToHips) < 1e-10f)
                {
                    ApplyPositionControlTorsoLock(ref chest, in P.ChestTargetRot, in P.ChestTargetPos, in P.ChestScaledOffset, P.ChestTposeY + P.TrackingLiftY, in P.ParentMatrix, in P.ParentRotation);
                    ApplyPositionControlTorsoLock(ref spine, in P.SpineTargetRot, in P.SpineTargetPos, in P.SpineScaledOffset, P.SpineTposeY + P.TrackingLiftY, in P.ParentMatrix, in P.ParentRotation);
                }
                else
                {
                    quaternion chainTopYaw = freeze ? neckYaw : torsoYawTarget;
                    ComputeChainPlacement(
                        in neckPos, in hipsPosReadback,
                        P.TChest, P.TSpine,
                        in chainTopYaw, in hipsYaw,
                        out float3 chestPos, out float3 spinePos,
                        out quaternion chestYawTarget, out quaternion spineYawTarget);

                    quaternion chestTarget = ApplyPitchRollCascadeBurst(in chestYawTarget, in eyeRot, P.ChestPitchFrac, P.ChestRollFrac);
                    quaternion spineTarget = ApplyPitchRollCascadeBurst(in spineYawTarget, in eyeRot, P.SpinePitchFrac, P.SpineRollFrac);

                    quaternion chestCurrent = chest.OutgoingRotation;
                    quaternion spineCurrent = spine.OutgoingRotation;

                    SmoothSlerpBurst(in chestCurrent, in chestTarget, P.ChestRotationSpeed, dt, out quaternion chestSmoothed);
                    SmoothSlerpBurst(in spineCurrent, in spineTarget, P.SpineRotationSpeed, dt, out quaternion spineSmoothed);

                    chest.OutgoingRotation = chestSmoothed;
                    spine.OutgoingRotation = spineSmoothed;

                    ApplyPositionGivenBaseTorsoLock(ref chest, in chestPos, in P.ChestScaledOffset, P.ChestTposeY + P.TrackingLiftY, in P.ParentMatrix, in P.ParentRotation);
                    ApplyPositionGivenBaseTorsoLock(ref spine, in spinePos, in P.SpineScaledOffset, P.SpineTposeY + P.TrackingLiftY, in P.ParentMatrix, in P.ParentRotation);
                }

                States[IdxHead] = head;
                States[IdxNeck] = neck;
                States[IdxChest] = chest;
                States[IdxSpine] = spine;
                States[IdxHips] = hips;
                State[0] = s;
            }
        }

        [BurstCompile]
        private static void ApplyWorldAndLastBurst(ref BasisBoneSimState st, in float4x4 parentMatrix, in quaternion parentRotation)
        {
            st.LastRunPosition = st.OutgoingPosition;
            st.LastRunRotation = st.OutgoingRotation;

            float4 p = math.mul(parentMatrix, new float4(st.OutgoingPosition, 1f));
            st.OutgoingWorldPosition = p.xyz;
            st.OutgoingWorldRotation = math.mul(parentRotation, st.OutgoingRotation);
        }

        [BurstCompile]
        private static void ApplyPositionControlTorsoLock(ref BasisBoneSimState st, in quaternion targetRot, in float3 targetPos, in float3 scaledOffset, float tposeY, in float4x4 parentMatrix, in quaternion parentRotation)
        {
            ExtractYawBurst(in targetRot, out quaternion yawOnly);
            float3 localOffset = scaledOffset;
            localOffset.y = 0f;
            ComposePosition(in targetPos, in yawOnly, in localOffset, out float3 desired);
            desired.y = tposeY;
            st.OutgoingPosition = desired;
            ApplyWorldAndLastBurst(ref st, in parentMatrix, in parentRotation);
        }

        [BurstCompile]
        private static void ApplyPositionGivenBaseTorsoLock(ref BasisBoneSimState st, in float3 baseWorld, in float3 scaledOffset, float tposeY, in float4x4 parentMatrix, in quaternion parentRotation)
        {
            quaternion rot = st.OutgoingRotation;
            ExtractYawBurst(in rot, out quaternion yawOnly);
            float3 localOffset = scaledOffset;
            localOffset.y = 0f;
            ComposePosition(in baseWorld, in yawOnly, in localOffset, out float3 desired);
            desired.y = tposeY;
            st.OutgoingPosition = desired;
            ApplyWorldAndLastBurst(ref st, in parentMatrix, in parentRotation);
        }

        /// <summary>
        /// Yaw deadzone for the torso: holds the chest/spine/hips heading at an anchor until the head
        /// leaves the cone, then eases a 0..1 follow weight in so the chain blends into the catch-up
        /// (and back out) instead of snapping at the edge. Re-centers the anchor once fully engaged and
        /// the head stops, so reversing has to re-cross the cone. While locomoting the cone is bypassed
        /// so the torso re-aligns to the head. Head and neck are unaffected.
        /// </summary>
        private static quaternion ComputeTorsoYawTargetBurst(ref SpineSolveState s, in quaternion headYawOnly, float deadzoneDeg, float blendSpeed, bool moving, float dt)
        {
            YawDegrees(in headYawOnly, out float headYawDeg);

            if (s.TorsoYawInitialized == 0)
            {
                s.TorsoYawAnchorDeg = headYawDeg;
                s.PrevHeadYawDeg = headYawDeg;
                s.TorsoYawBroken = 0;
                s.TorsoFollow = 0f;
                s.TorsoYawInitialized = 1;
            }

            float headSpeedDeg = math.abs(DeltaAngleDeg(s.PrevHeadYawDeg, headYawDeg)) / math.max(dt, 1e-5f);
            s.PrevHeadYawDeg = headYawDeg;

            // Locomotion (keyboard / VR stick) re-aligns the torso to the head: engage follow so the body
            // eases to the move/look direction, then the normal relock re-centers the cone there on stop.
            if (moving)
            {
                s.TorsoYawBroken = 1;
            }

            if (s.TorsoYawBroken == 0 && math.abs(DeltaAngleDeg(s.TorsoYawAnchorDeg, headYawDeg)) > math.max(0f, deadzoneDeg))
            {
                s.TorsoYawBroken = 1;
            }

            float targetFollow = s.TorsoYawBroken != 0 ? 1f : 0f;
            // Framerate-independent, for the same reason as SmoothSlerpBurst: this blend gates how fast the
            // torso catches up to a broken yaw anchor, and it must not run at a different speed per headset.
            s.TorsoFollow = math.lerp(s.TorsoFollow, targetFollow, BasisSmoothingProfiles.FramerateIndependentAlpha(blendSpeed, dt));

            // Re-center only once the blend has fully engaged, so swapping the anchor can't jump the
            // blended target and bring back the click this easing removes.
            if (s.TorsoYawBroken != 0 && s.TorsoFollow >= 0.999f && headSpeedDeg <= TorsoYawRelockSpeedDeg)
            {
                s.TorsoYawBroken = 0;
                s.TorsoYawAnchorDeg = headYawDeg;
            }

            quaternion anchorYaw = quaternion.AxisAngle(new float3(0f, 1f, 0f), math.radians(s.TorsoYawAnchorDeg));
            return math.slerp(anchorYaw, headYawOnly, s.TorsoFollow);
        }

        /// <summary>
        /// Anatomy-aware hips XZ. Two layers:
        ///   (1) Counterbalance: a leashed head-XZ baseline estimates the user's support base (where they are
        ///       standing). Hips sit at baseline + an axis-dependent fraction of the head's deviation: little of
        ///       a FORWARD lean (a bend counter-balances — hips stay back) but most of a SIDEWAYS shift (a
        ///       weight shift stays stacked over the feet), while stepping drags the base along and the hips
        ///       follow. The base also settles onto the head over seconds, so the counterbalance is a response
        ///       to a lean rather than a permanent offset — a posture you HOLD becomes where you are standing.
        ///   (2) Foot pendulum: if both feet are tracked, override with feet-midpoint + a small lean
        ///       toward the head — closer to a real inverted-pendulum stance. With roles on the feet the
        ///       support base is known outright, so no estimate is used.
        /// </summary>
        // `supportXZ` is the SUPPORT BASE -- where the user is standing. It is already computed here (it is what
        // the pelvis is lerped away from), so it is handed back rather than recomputed: the posture model measures
        // the head's forward LEAN against it, and two copies of that definition is exactly the kind of quiet
        // disagreement that has bitten this codebase before.
        private static float3 ComputeRealisticHipsXZBurst(ref SpineSolveState s, float3 headPosWorld, float dt, float standingHeadY, float headDrop, in quaternion torsoYaw, float3 leftFootPos, float3 rightFootPos, bool leftFootTracked, bool rightFootTracked, out float3 supportXZ)
        {
            float3 headXZ = new float3(headPosWorld.x, 0f, headPosWorld.z);

            if (s.HeadBaselineInitialized == 0)
            {
                s.HeadBaselineXZ = headXZ;
                s.HeadBaselineInitialized = 1;
            }
            else
            {
                // ONE CONTINUOUS LAW. The support base tracks the head at a rate that rises smoothly with how
                // far it has been left behind -- no threshold, no latch, no dead zone, no mode.
                //
                // Every previous form here was a SWITCH, and that is what made the pelvis read as stop-start:
                // a deadband pull did nothing at all until the head crossed a stance radius, and the latch that
                // replaced it did nothing until the same crossing and then dragged the base over in two frames.
                // Both are bang-bang controllers. You stand still and the pelvis is inert; you drift one
                // centimetre past an invisible line and it lunges. There is no set of thresholds that makes a
                // switch feel continuous -- the discontinuity IS the design -- so the switch is gone.
                //
                // The distance term is what used to be the mode. Near zero the base is soft, so tracker noise
                // and micro-motion do not drag it around; the further behind it falls the harder it is pulled,
                // so a real step or a walk is followed instead of triggering a state change. Same behaviour at
                // both ends as the two-mode version, and everything in between is now defined rather than
                // being a cliff. `dist` is normalised by the stance radius so the whole law scales with the
                // avatar, and so a crouch (which widens the radius) softens the follow exactly as it should.
                //
                // ⚠️ The leash no longer holds a counterbalance offset -- it cannot, and follow this closely.
                // That is deliberate: the postural counterbalance is BasisTrunkCounterbalanceCore's job, it is
                // derived from segment masses, and it is driven by the gaze-invariant neck cue rather than by
                // "the head moved horizontally". The leash was a second, cruder copy of it that could not tell
                // a bend from a walk. This one estimates only WHERE THE USER IS STANDING.
                float safeDt = math.max(dt, 1e-6f);
                // The BASE radius stays un-lifted on purpose (see TrackingLiftY: the lift moves the body, it
                // does not resize it); only the crouch allowance uses the lifted drop, computed by the caller.
                float radius = math.max(StanceRadiusFrac * standingHeadY + CrouchLeanAllowanceFrac * headDrop, 1e-3f);

                float dist = math.length(headXZ - s.HeadBaselineXZ);
                float rate = HeadBaselineFollowRateRest + HeadBaselineFollowRateGain * (dist / radius);
                float alpha = 1f - math.exp(-rate * safeDt);
                s.HeadBaselineXZ = math.lerp(s.HeadBaselineXZ, headXZ, alpha);
            }

            if (leftFootTracked && rightFootTracked)
            {
                float3 feetMidXZ = new float3(
                    (leftFootPos.x + rightFootPos.x) * 0.5f,
                    0f,
                    (leftFootPos.z + rightFootPos.z) * 0.5f);
                supportXZ = feetMidXZ;
                return math.lerp(feetMidXZ, headXZ, FootPendulumLeanFrac);
            }

            // No feet with roles: the leashed head baseline IS the standing spot — the best support base
            // available when nothing is measuring the feet.
            supportXZ = s.HeadBaselineXZ;

            // Anisotropic follow. Split the head's horizontal deviation from the standing spot along the torso's
            // OWN facing and follow each axis at its own rate:
            //   • forward/back is a BEND — the pelvis counterbalances and barely moves, so it follows at
            //     CounterbalanceFollowFrac (the leash's original, corpus-checked purpose).
            //   • left/right is a WEIGHT SHIFT — a real pelvis stays stacked over the feet, so it follows most of
            //     the way (CounterbalanceLateralFollowFrac). Following it at the sagittal rate is what left the
            //     hips lagging ~75% behind a side-to-side move and skewed the torso into a phantom rotation.
            // With the two fracs equal this is bit-identical to the former lerp(baseline, headXZ, frac): fwd and
            // right are an orthonormal basis of the XZ plane, so the split reconstructs the same offset exactly.
            float3 dev = headXZ - s.HeadBaselineXZ;
            float3 fwd = math.mul(torsoYaw, new float3(0f, 0f, 1f));
            float3 right = math.mul(torsoYaw, new float3(1f, 0f, 0f));
            float devFwd = math.dot(dev, fwd);
            float devRight = math.dot(dev, right);
            return s.HeadBaselineXZ
                 + fwd * (devFwd * CounterbalanceFollowFrac)
                 + right * (devRight * CounterbalanceLateralFollowFrac);
        }

        // Adds a configurable fraction of head pitch and roll on top of a yaw-only base rotation.
        // Pitch and roll are extracted directly from the head's forward and right vectors instead of
        // via euler angles — the Euler path gimbal-locks at look-straight-up/down and emits phantom
        // ±180° "roll" values that, scaled by rollFrac, tilt the chest sideways into the body.
        private static quaternion ApplyPitchRollCascadeBurst(in quaternion yawBase, in quaternion eyeRot, float pitchFrac, float rollFrac)
        {
            if (pitchFrac <= 0f && rollFrac <= 0f)
            {
                return yawBase;
            }

            float3 headFwd = math.mul(eyeRot, new float3(0f, 0f, 1f));
            float3 headRight = math.mul(eyeRot, new float3(1f, 0f, 0f));

            // Pitch: angle of the head's forward away from horizontal. Positive = looking down,
            // matching Unity's Euler(x,0,0) sign convention so the round-trip is faithful.
            float horizMag = math.sqrt(headFwd.x * headFwd.x + headFwd.z * headFwd.z);
            float pitchDeg = math.degrees(math.atan2(-headFwd.y, horizMag));

            // Roll: tilt of the head's right vector out of horizontal. Defined everywhere — at the
            // look-straight-up pole, headRight stays in the horizontal plane (y=0), so roll = 0.
            float rollDeg = math.degrees(math.asin(math.clamp(-headRight.y, -1f, 1f)));

            quaternion swing = quaternion.EulerZXY(math.radians(new float3(pitchDeg * pitchFrac, 0f, rollDeg * rollFrac)));
            return math.mul(yawBase, swing);
        }

        private static float DeltaAngleDeg(float current, float target)
        {
            float delta = target - current;
            delta -= math.floor(delta / 360f) * 360f;
            if (delta > 180f) delta -= 360f;
            return delta;
        }

        // -----------------------------
        // Burst-compiled static helpers
        // -----------------------------

        [BurstCompile]
        private static void SmoothSlerpBurst(in quaternion current, in quaternion target, float speed, float dt, out quaternion result)
        {
            result = math.slerp(current, target, BasisSmoothingProfiles.FramerateIndependentAlpha(speed, dt));
        }

        // Yaw about world up, as the TWIST half of a swing-twist decomposition.
        //
        // Do NOT "simplify" this back to flattening head-forward into the horizontal plane and taking its
        // azimuth. Forward carries no azimuth at all once the gaze is vertical, and the azimuth's gain is
        // 1/cos(gazePitch) -- 4.7x at 80 degrees of look-down, 180x at 90 -- so sweeping the head across the
        // middle of the chest whipped the entire torso around (this yaw is what hips/spine/chest are aimed
        // by). Same defect at the look-up pole. The old 1e-12 guard was orders of magnitude too small to fire;
        // the damage is done long before the projection is literally zero.
        //
        // The twist has no projection to collapse. It is EXACTLY the old azimuth for any roll-free head
        // rotation, so ordinary look-around -- which is what every spine tuning was set against -- is
        // unchanged, and it stays continuous and bounded through both poles. Its own singularity is a
        // 180-degree pitch (upside down, facing backwards), which a head cannot reach.
        [BurstCompile]
        public static void ExtractYawBurst(in quaternion rotation, out quaternion result)
        {
            float4 q = rotation.value;
            float lenSq = q.y * q.y + q.w * q.w;
            if (lenSq < 1e-12f)
            {
                result = quaternion.identity;
                return;
            }
            float inv = math.rsqrt(lenSq);
            result = new quaternion(0f, q.y * inv, 0f, q.w * inv);
        }

        [BurstCompile]
        private static void NormalizeSafeWithFallback(in float3 v, in float3 fallback, out float3 result)
        {
            result = math.lengthsq(v) < 1e-6f ? fallback : math.normalize(v);
        }

        [BurstCompile]
        private static void ComposePosition(in float3 basePos, in quaternion rot, in float3 localOffset, out float3 result)
        {
            result = basePos + math.mul(rot, localOffset);
        }

        [BurstCompile]
        internal static void ComputeHipsPosition(
            in float3 neckPos,
            in float3 headPos,
            in float3 supportXZ,
            in float3 worldUp,
            float lenTotal,
            in quaternion headYaw,
            float biasScale,
            in float3 desiredHipsXZ,
            bool freezeToTpose,
            in float3 tposeHips,
            float standingHipsLocalY,
            float standingHeadLocalY,
            float trackingLiftY,
            bool usePostureModel,
            float compressionStrength,
            float maxDrop,
            out float3 result)
        {
            // The tracked body carries the play-space vertical offset; the T-pose-derived standing
            // references don't. Lift them into the same frame or the offset reads as a phantom squat.
            float standingHipsY = standingHipsLocalY + trackingLiftY;
            float standingHeadY = standingHeadLocalY + trackingLiftY;

            // Match original semantics: when frozen, bias direction is world-aligned (identity yaw),
            // not head yaw. Position base swaps to TPose but forward bias is still applied.
            float3 hipsBase = freezeToTpose ? tposeHips + new float3(0f, trackingLiftY, 0f) : neckPos - worldUp * lenTotal;
            quaternion biasYaw = freezeToTpose ? quaternion.identity : headYaw;
            float3 forwardBias = math.mul(biasYaw, new float3(0f, 0f, 1f)) * biasScale;

            if (freezeToTpose)
            {
                // T-pose freeze keeps the legacy XZ-from-tposeHips path; ignore the realistic XZ.
                result = hipsBase + forwardBias;
                return;
            }

            // `hipsBase.y` is the RIGID pelvis: a fixed spine-length below the neck. It is also the FLOOR of
            // what follows -- the pelvis may sit above it (the spine compresses, which is what a folding spine
            // does) but never below it, because that would mean the spine has STRETCHED.
            float rigidY = hipsBase.y;
            float headDrop = standingHeadY - headPos.y;

            if (usePostureModel && standingHeadLocalY > 1e-3f && headDrop > 0f)
            {
                // ------------------------------------------------------------------------------------------
                // THE PELVIS POSTURE MODEL. A low head is TWO different bodies -- a waist-bend (pelvis stays
                // high, spine folds) and a squat (pelvis rides the head down) -- and the old law below could
                // not tell them apart, because it only ever looked at HEIGHT. Fitted to 44 CMU clips, the real
                // coupling is 0.02-0.14 for the first and 0.78-0.99 for the second: not a spread, two
                // behaviours. The discriminator is FORWARD LEAN, and this is where it finally gets used.
                //
                // Everything is normalised by the user's own standing head height, so it is scale-free.
                // ------------------------------------------------------------------------------------------
                float3 headXZ = new float3(headPos.x, 0f, headPos.z);
                float lean = math.length(headXZ - new float3(supportXZ.x, 0f, supportXZ.z));

                float d = headDrop / standingHeadLocalY;
                float f = lean / standingHeadLocalY;

                float pelvisDrop = BasisPelvisPostureModel.PelvisDrop(d, f) * standingHeadLocalY;

                // The pelvis may rise above the rigid pose (spine compresses) but never sink below it (spine
                // cannot stretch). On a squat the model lands ON the rigid pose, which is the whole point --
                // that is the case the old saturation was wrongly holding 32.8 cm up.
                hipsBase.y = math.max(standingHipsY - pelvisDrop, rigidY);
            }
            else if (!usePostureModel)
            {
                // LEGACY: a single exponential saturation of the pelvis's downward travel. Right-ish for a
                // waist-bend (which is why it was added -- the rigid model buried the pelvis and folded the
                // knees), badly wrong for a squat. Kept behind the VSpinePostureModel toggle so the change is
                // one switch to A/B in a headset, and one switch to revert.
                float drop = standingHipsY - rigidY;
                if (drop > 0f && compressionStrength > 0f && maxDrop > 1e-4f)
                {
                    float softDrop = maxDrop * (1f - math.exp(-drop / maxDrop));
                    hipsBase.y = standingHipsY - math.lerp(drop, softDrop, math.saturate(compressionStrength));
                }
            }
            // headDrop <= 0 (head at or above standing height -- tiptoes, a jump) leaves the RIGID pose
            // untouched under either law: the pelvis rises with the neck, exactly as it always has.

            // Y from the posture law, XZ from the realistic model (deliberately UNCHANGED -- a fit of the
            // horizontal pelvis lost to this constant out-of-sample, so it does not ship), plus pelvic bias.
            result = new float3(desiredHipsXZ.x, hipsBase.y, desiredHipsXZ.z) + forwardBias;
        }

        [BurstCompile]
        internal static void ComputeChainPlacement(
            in float3 neckPos,
            in float3 hipsPos,
            float tChest,
            float tSpine,
            in quaternion neckYaw,
            in quaternion hipsYaw,
            out float3 chestPos,
            out float3 spinePos,
            out quaternion chestYawTarget,
            out quaternion spineYawTarget)
        {
            chestPos = math.lerp(neckPos, hipsPos, tChest);
            spinePos = math.lerp(neckPos, hipsPos, tSpine);
            chestYawTarget = math.slerp(neckYaw, hipsYaw, tChest);
            spineYawTarget = math.slerp(neckYaw, hipsYaw, tSpine);
        }

        [BurstCompile]
        public static void YawDegrees(in quaternion yawOnly, out float result)
        {
            float3 f = math.mul(yawOnly, new float3(0f, 0f, 1f));
            result = math.degrees(math.atan2(f.x, f.z));
        }
    }
}
