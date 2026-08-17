using Basis.IK;
using Basis.Scripts.Drivers;
using NUnit.Framework;
using System.Text;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Basis.Tests.IK
{
    /// <summary>
    /// "IN HALF BODY THE HIPS JUT OUT LEFT AND RIGHT INSTEAD OF ACTING MORE LIKE A SPINE."
    ///
    /// ================================================================================================
    /// THE SUSPECT, in one line. The pelvis's horizontal position is built from TWO yaw references that
    /// are allowed to disagree:
    ///
    ///     supportBase  tracks  P.EyePos                      -- the HMD, which ORBITS the neck on a turn
    ///     pelvis       =  supportBase + R(torsoYaw) * (hips - eye)_tpose
    ///
    /// The eye is not on the neck's yaw axis: it sits ~9 cm in front of it. So turning your head sweeps
    /// the HMD sideways through an arc, the stance leash follows that arc within a frame or two
    /// (HeadBaselineFollowRateGain = 250), and the pelvis is carried along with it -- while the arm that
    /// is supposed to put the pelvis BACK under the body is rotated by the TORSO yaw, which deliberately
    /// lags the head (VSpineTorsoYawBlendSpeed = 8, and a 45 deg deadzone on desktop).
    ///
    /// The two only cancel when torsoYaw == headYaw. Every time they differ the pelvis is displaced by
    ///
    ///     |R(headYaw) - R(torsoYaw)| * |eye - neck|_xz  =  2 * 0.09 m * sin(lag / 2)
    ///
    /// which is centimetres of pure fiction, pointing sideways, for a user who has not moved.
    /// ================================================================================================
    ///
    /// This is the same class of bug -- and takes the same shape of fix -- as the phantom forward lean in
    /// BasisSpineGazeContaminationTests: a cue read off a point that ORBITS the joint it is meant to
    /// describe. There the answer was to reconstruct the NECK rigidly off the head so the lever arms
    /// cancel algebraically. Here it is the same reconstruction, applied to the stance leash: leash the
    /// yaw pivot, and measure the pelvis anchor arm from that same pivot. Both halves must move together
    /// or the cancellation is lost, which is why one strength (VSpineGazeSwingRemoval) drives both.
    ///
    /// ⚠️ PREMISE, stated up front: these gates model a head turn as an EXACT rigid orbit of the neck
    /// bone, the same premise BasisSpineGazeContaminationTests.GazeDown makes for a nod. A real turn is
    /// distributed through the cervical spine, so the true pivot sits somewhere between the neck and the
    /// skull and the real orbit is a little smaller than modelled. That makes these numbers an UPPER
    /// bound on the artefact -- it does not make the artefact conditional, because no pivot choice puts
    /// the HMD on the axis.
    ///
    /// ⚠️ VR ONLY, and the reason is worth knowing before changing the driver: BasisDesktopEye already
    /// pins its simulated eye onto the yaw axis on purpose ("this is what stops the eye's static forward
    /// offset ORBITING the neck every time you turn"), so a desktop eye has no arc to remove and
    /// de-orbiting one would INVENT the very slide these gates forbid. The driver therefore passes
    /// YawPivotFromEyeLocal only in VR; every gate here sets it, i.e. every gate here is the VR rig.
    ///
    /// House rule (inherited from BasisHipsSlideProbeTests): every gate asserting the fix is correct is
    /// PAIRED with one that drives the OLD form and asserts it FAILS.
    /// </summary>
    public sealed class BasisHipsYawOrbitTests
    {
        const int Head = 0, Neck = 1, Chest = 2, Spine = 3, Hips = 4;
        const int BoneCount = 5;

        // A T-posed adult, in player-local space. The only number that drives this whole file is the
        // 9 cm of FORWARD offset between the eye and the neck -- that is the lever the turn sweeps.
        static readonly float3 k_Hips = new float3(0f, 0.95f, 0f);
        static readonly float3 k_Spine = new float3(0f, 1.05f, 0f);
        static readonly float3 k_Chest = new float3(0f, 1.25f, 0f);
        static readonly float3 k_Neck = new float3(0f, 1.45f, 0f);
        static readonly float3 k_Head = new float3(0f, 1.52f, 0f);
        static readonly float3 k_Eye = new float3(0f, 1.60f, 0.09f);

        /// <summary>The lever, in the XZ plane: how far in front of the yaw axis the HMD sits.</summary>
        static readonly float k_EyeLeverXZ = math.length(new float2(k_Eye.x - k_Neck.x, k_Eye.z - k_Neck.z));

        /// <summary>
        /// A head that has ONLY turned. The body has not moved by one float -- the eye rides a rigid arc
        /// about the neck, which is what an HMD physically does when you look over your shoulder.
        /// </summary>
        static void HeadTurn(float yawDeg, out float3 eyePos, out quaternion eyeRot)
        {
            eyeRot = quaternion.AxisAngle(new float3(0f, 1f, 0f), math.radians(yawDeg));
            eyePos = k_Neck + math.mul(eyeRot, k_Eye - k_Neck);
        }

        /// <summary>Ticks the REAL BasisVirtualSpineSolveJob. Returns the solved hips position per frame.</summary>
        static float3[] RunSpine(float dt, int frames, System.Func<int, float> yawAt, float deOrbit, float deadzoneDeg)
        {
            var states = new NativeArray<BasisBoneSimState>(BoneCount, Allocator.Temp);
            var solve = new NativeArray<BasisVirtualSpineCore.SpineSolveState>(1, Allocator.Temp);
            try
            {
                for (int i = 0; i < BoneCount; i++)
                    states[i] = new BasisBoneSimState { OutgoingRotation = quaternion.identity, LastRunRotation = quaternion.identity };
                solve[0] = default;

                var hipsTrack = new float3[frames];
                for (int i = 0; i < frames; i++)
                {
                    HeadTurn(yawAt(i), out float3 eyePos, out quaternion eyeRot);

                    new BasisVirtualSpineCore.BasisVirtualSpineSolveJob
                    {
                        States = states,
                        State = solve,
                        P = MakeParams(dt, eyePos, eyeRot, deOrbit, deadzoneDeg),
                        IdxHead = Head,
                        IdxNeck = Neck,
                        IdxChest = Chest,
                        IdxSpine = Spine,
                        IdxHips = Hips,
                    }.Execute();

                    hipsTrack[i] = states[Hips].OutgoingPosition;
                }
                return hipsTrack;
            }
            finally
            {
                states.Dispose();
                solve.Dispose();
            }
        }

        /// <summary>The same run, with the whole player also travelling forward -- for the invariance gate.</summary>
        static float3[] RunSpineWalking(float dt, int frames, float yawDeg, float speed, float deOrbit)
        {
            var states = new NativeArray<BasisBoneSimState>(BoneCount, Allocator.Temp);
            var solve = new NativeArray<BasisVirtualSpineCore.SpineSolveState>(1, Allocator.Temp);
            try
            {
                for (int i = 0; i < BoneCount; i++)
                    states[i] = new BasisBoneSimState { OutgoingRotation = quaternion.identity, LastRunRotation = quaternion.identity };
                solve[0] = default;

                var hipsTrack = new float3[frames];
                for (int i = 0; i < frames; i++)
                {
                    HeadTurn(yawDeg, out float3 eyePos, out quaternion eyeRot);
                    eyePos += new float3(0f, 0f, speed * i * dt);

                    new BasisVirtualSpineCore.BasisVirtualSpineSolveJob
                    {
                        States = states,
                        State = solve,
                        P = MakeParams(dt, eyePos, eyeRot, deOrbit, 0f),
                        IdxHead = Head,
                        IdxNeck = Neck,
                        IdxChest = Chest,
                        IdxSpine = Spine,
                        IdxHips = Hips,
                    }.Execute();

                    hipsTrack[i] = states[Hips].OutgoingPosition;
                }
                return hipsTrack;
            }
            finally
            {
                states.Dispose();
                solve.Dispose();
            }
        }

        static BasisVirtualSpineCore.SpineSolveParams MakeParams(float dt, float3 eyePos, quaternion eyeRot, float deOrbit, float deadzoneDeg)
        {
            float lenTotal = k_Neck.y - k_Hips.y;

            return new BasisVirtualSpineCore.SpineSolveParams
            {
                Dt = dt,
                Scale = 1f,
                ParentMatrix = float4x4.identity,
                ParentRotation = quaternion.identity,
                EyeRot = eyeRot,
                EyePos = eyePos,

                // Every torso target resolves to the eye in the shipping driver, so the whole chain is
                // composed off this one pose plus the T-pose offsets below.
                HeadTargetPos = eyePos,
                HeadTargetRot = eyeRot,
                NeckTargetPos = eyePos,
                NeckTargetRot = eyeRot,
                ChestTargetPos = eyePos,
                ChestTargetRot = eyeRot,
                SpineTargetPos = eyePos,
                SpineTargetRot = eyeRot,

                HeadScaledOffset = k_Head - k_Eye,
                NeckScaledOffset = k_Neck - k_Eye,
                ChestScaledOffset = k_Chest - k_Eye,
                SpineScaledOffset = k_Spine - k_Eye,

                ChestTposeY = k_Chest.y,
                SpineTposeY = k_Spine.y,
                TposeHips = k_Hips,

                LeftFootTracked = 0,
                RightFootTracked = 0,

                // Shipping defaults (BasisSettingsDefaults.VSpine*).
                ChestPitchFrac = 0.30f,
                ChestRollFrac = 0.30f,
                SpinePitchFrac = 0.10f,
                SpineRollFrac = 0.10f,
                NeckRotationSpeed = 40f,
                ChestRotationSpeed = 25f,
                SpineRotationSpeed = 30f,
                HipsRotationSpeed = 20f,
                HipsForwardBias = 0f,
                NeckExtensionDamp = 0.65f,
                TorsoYawDeadzoneDeg = deadzoneDeg,
                TorsoYawBlendSpeed = 8f,

                HipsFreeze = 0,
                IsLocomoting = 0,

                LenTotal = lenTotal,
                TChest = (k_Neck.y - k_Chest.y) / lenTotal,
                TSpine = (k_Neck.y - k_Spine.y) / lenTotal,

                StandingHipsLocalY = k_Neck.y - lenTotal,
                StandingHeadLocalY = k_Head.y,

                // The three arms the pelvis placement is built from, exactly as the driver bakes them.
                EyeFromHeadTpose = k_Eye - k_Head,
                GazeSwingRemoval = deOrbit,
                HipsAnchorOffsetLocal = new float3(k_Hips.x - k_Eye.x, 0f, k_Hips.z - k_Eye.z),
                HeadRestFromEyeLocal = new float3(k_Head.x - k_Eye.x, 0f, k_Head.z - k_Eye.z),
                YawPivotFromEyeLocal = new float3(k_Neck.x - k_Eye.x, 0f, k_Neck.z - k_Eye.z),

                PostureModel = 1,
                HipsCompressionStrength = 0.85f,
                HipsMaxDropMeters = 0.30f,
            };
        }

        /// <summary>Worst horizontal distance the pelvis ever travels from where it started.</summary>
        static float WorstSlide(float3[] hips)
        {
            float worst = 0f;
            for (int i = 0; i < hips.Length; i++)
            {
                float2 d = new float2(hips[i].x - hips[0].x, hips[i].z - hips[0].z);
                worst = math.max(worst, math.length(d));
            }
            return worst;
        }

        /// <summary>Turn to `deg` over `turnSecs`, then hold.</summary>
        static System.Func<int, float> Turn(float dt, float turnSecs, float deg) =>
            i => deg * Mathf.Clamp01(i * dt / turnSecs);

        // ------------------------------------------------------------------ the gates

        /// <summary>
        /// ⭐ THE HEADLINE GATE, and it is the report verbatim: turn your head, and the pelvis must stay
        /// where your feet are.
        ///
        /// VR default -- VSpineTorsoYawPlayInVR is OFF, so the deadzone is forced to 0 and the torso
        /// follows the head continuously at blend speed 8 (tau = 125 ms). That is not a bug; a torso
        /// SHOULD lag a head. The bug is that the pelvis POSITION is built from the lagging yaw while its
        /// support base is built from the instantaneous one, so the lag is paid out as sideways travel:
        /// out on the turn, back when the torso catches up. Out and back, every glance. "Jutting."
        /// </summary>
        [Test]
        public void APureHeadTurn_DoesNotSlideThePelvisSideways()
        {
            const int fps = 90;
            const float dt = 1f / fps, holdSecs = 0.70f;

            var report = new StringBuilder();
            report.AppendLine("PURE HEAD TURN -> PELVIS SLIDE (the body is byte-identical on every row)");
            report.AppendLine($"  eye sits {k_EyeLeverXZ * 100f:F1} cm in front of the neck's yaw axis; the artefact is");
            report.AppendLine("  2 * lever * sin(yaw lag / 2), so it scales with BOTH how far and how FAST you turn");
            report.AppendLine();
            report.AppendLine($"{"turn deg",10} {"over",8} {"de-orbited",12} {"legacy",12}");
            report.AppendLine(new string('-', 44));

            float worstFixed = 0f, bestLegacy = 0f;
            foreach (float turnSecs in new[] { 0.15f, 0.30f, 0.60f })
            {
                int frames = Mathf.RoundToInt((turnSecs + holdSecs) * fps);
                foreach (float deg in new[] { 45f, 90f, 120f })
                {
                    float slideFixed = WorstSlide(RunSpine(dt, frames, Turn(dt, turnSecs, deg), 1f, 0f));
                    float slideLegacy = WorstSlide(RunSpine(dt, frames, Turn(dt, turnSecs, deg), 0f, 0f));

                    worstFixed = math.max(worstFixed, slideFixed);
                    bestLegacy = math.max(bestLegacy, slideLegacy);
                    report.AppendLine($"{deg,10:F0} {turnSecs,7:F2}s {slideFixed * 100f,10:F2} cm {slideLegacy * 100f,10:F2} cm");
                }
            }
            Debug.Log(report.ToString());

            Assert.Less(worstFixed, 0.005f,
                $"a head turn slid the pelvis {worstFixed * 100f:F2} cm sideways. The body did not move: every "
                + "millimetre of that is the HMD's orbit about the neck leaking into the stance leash.");

            // PAIRED NEGATIVE: GazeSwingRemoval = 0 is the pre-fix law exactly (leash on the raw eye, anchor
            // arm measured from the eye). It must still jut, or this gate is measuring nothing.
            Assert.Greater(bestLegacy, 0.02f,
                $"the pre-fix leash is supposed to carry the pelvis sideways on a head turn (worst "
                + $"{bestLegacy * 100f:F2} cm) -- if it no longer does, this gate is testing nothing");
        }

        /// <summary>
        /// THE HELD-TURN GATE -- the same artefact with the transient taken out of it, so it cannot be
        /// dismissed as a settling wobble.
        ///
        /// With a yaw deadzone (45 deg: the desktop default, and VR when VSpineTorsoYawPlayInVR is on) the
        /// torso does not follow AT ALL inside the cone. So a head held at 40 deg holds the disagreement
        /// open forever, and the pre-fix pelvis simply sits ~6 cm to one side for as long as you look that
        /// way. Nothing settles it.
        /// </summary>
        [Test]
        public void AHeadHeldInsideTheYawDeadzone_LeavesThePelvisStandingWhereItWas()
        {
            const int fps = 90;
            const float dt = 1f / fps, turnSecs = 0.30f, holdSecs = 2.0f;
            const float held = 40f;   // inside the 45 deg cone, so the torso never breaks and never relocks
            int frames = Mathf.RoundToInt((turnSecs + holdSecs) * fps);

            float3[] fixedHips = RunSpine(dt, frames, Turn(dt, turnSecs, held), 1f, 45f);
            float3[] legacyHips = RunSpine(dt, frames, Turn(dt, turnSecs, held), 0f, 45f);

            float settledFixed = math.length(new float2(fixedHips[frames - 1].x - fixedHips[0].x, fixedHips[frames - 1].z - fixedHips[0].z));
            float settledLegacy = math.length(new float2(legacyHips[frames - 1].x - legacyHips[0].x, legacyHips[frames - 1].z - legacyHips[0].z));

            Debug.Log($"head held at {held:F0} deg inside the deadzone for {holdSecs:F1} s -- pelvis offset: "
                + $"de-orbited {settledFixed * 100f:F2} cm, legacy {settledLegacy * 100f:F2} cm "
                + $"(geometry says 2*{k_EyeLeverXZ * 100f:F1}*sin({held / 2f:F0} deg) = {2f * k_EyeLeverXZ * math.sin(math.radians(held * 0.5f)) * 100f:F2} cm)");

            Assert.Less(settledFixed, 0.005f,
                $"the pelvis settled {settledFixed * 100f:F2} cm from where the user is standing while they simply "
                + "looked to one side. A deadzone that holds the torso still must not also hold the pelvis out.");

            Assert.Greater(settledLegacy, 0.03f,
                $"the pre-fix law is supposed to park the pelvis to one side of a held turn (it parked it "
                + $"{settledLegacy * 100f:F2} cm) -- if it no longer does, this gate is testing nothing");
        }

        /// <summary>
        /// ⭐ THE INVARIANCE GATE, and the reason this change is safe to make at all.
        ///
        /// The de-orbit moves the leash reference by R(headYaw) * (neck - eye) and subtracts the SAME
        /// vector from the pelvis anchor arm, which is rotated by R(torsoYaw). When those two yaws agree
        /// the pair cancels EXACTLY -- so on any motion where the head yaw is not changing (walking,
        /// leaning, stepping, standing), this is a bit-for-bit no-op and every gate in
        /// BasisHipsSlideProbeTests is untouched by construction.
        ///
        /// Driven here with the head held at a constant 30 deg -- off-axis on purpose, so a broken
        /// implementation that only cancels at yaw 0 cannot pass.
        /// </summary>
        [Test]
        public void WithAConstantHeadYaw_TheDeOrbitIsExactlyANoOp()
        {
            const int fps = 90;
            const float dt = 1f / fps, speed = 1.2f;
            int frames = Mathf.RoundToInt(2.0f * fps);

            float3[] deOrbited = RunSpineWalking(dt, frames, 30f, speed, 1f);
            float3[] legacy = RunSpineWalking(dt, frames, 30f, speed, 0f);

            float worst = 0f;
            for (int i = 0; i < frames; i++)
            {
                worst = math.max(worst, math.length(deOrbited[i] - legacy[i]));
            }

            Debug.Log($"walking 2 s at {speed:F1} m/s with the head held 30 deg off-axis: worst pelvis difference "
                + $"between the de-orbited and legacy laws = {worst * 1000f:F4} mm");

            Assert.Less(worst, 1e-5f,
                $"the de-orbit moved the pelvis by {worst * 1000f:F4} mm on a motion with no head-yaw change. "
                + "It is supposed to cancel exactly there -- if it does not, it is not a pure de-orbit and the "
                + "existing stance-leash gates are no longer covering the shipping law.");
        }

        /// <summary>
        /// THE NO-REGRESSION GATE: real travel must still be followed. The de-orbit removes an ARC, not a
        /// walk, so a user who physically walks 40 cm must still take their pelvis with them.
        ///
        /// This is the gate that stops the fix from being implemented as "leash more slowly" -- which
        /// would silence the jutting and hand back both of the reports that produced the current follow
        /// law ("the hips stay in the middle of the play space", "it's stop start").
        /// </summary>
        [Test]
        public void RealTravelIsStillFollowed_TheDeOrbitRemovesAnArcNotAWalk()
        {
            const int fps = 90;
            const float dt = 1f / fps, moveSecs = 0.40f, holdSecs = 0.60f, dist = 0.40f;
            int frames = Mathf.RoundToInt((moveSecs + holdSecs) * fps);

            var states = new NativeArray<BasisBoneSimState>(BoneCount, Allocator.Temp);
            var solve = new NativeArray<BasisVirtualSpineCore.SpineSolveState>(1, Allocator.Temp);
            float carried;
            try
            {
                for (int i = 0; i < BoneCount; i++)
                    states[i] = new BasisBoneSimState { OutgoingRotation = quaternion.identity, LastRunRotation = quaternion.identity };
                solve[0] = default;

                float first = 0f, last = 0f;
                for (int i = 0; i < frames; i++)
                {
                    HeadTurn(0f, out float3 eyePos, out quaternion eyeRot);
                    eyePos += new float3(dist * Mathf.Clamp01(i * dt / moveSecs), 0f, 0f);   // a sideways STEP

                    new BasisVirtualSpineCore.BasisVirtualSpineSolveJob
                    {
                        States = states,
                        State = solve,
                        P = MakeParams(dt, eyePos, eyeRot, 1f, 0f),
                        IdxHead = Head,
                        IdxNeck = Neck,
                        IdxChest = Chest,
                        IdxSpine = Spine,
                        IdxHips = Hips,
                    }.Execute();

                    if (i == 0) first = states[Hips].OutgoingPosition.x;
                    last = states[Hips].OutgoingPosition.x;
                }
                carried = last - first;
            }
            finally
            {
                states.Dispose();
                solve.Dispose();
            }

            Assert.Greater(carried, 0.9f * dist,
                $"the pelvis carried only {carried * 100f:F1} cm of a {dist * 100f:F0} cm sideways step. The "
                + "de-orbit must remove the head's ARC and nothing else -- a user who steps must keep their hips.");
        }
    }
}
