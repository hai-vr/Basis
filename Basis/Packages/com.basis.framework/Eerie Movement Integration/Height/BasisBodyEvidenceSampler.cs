using Basis.IK;
using Basis.Scripts.Device_Management;
using Basis.Scripts.Device_Management.Devices;
using Basis.Scripts.Drivers;
using Basis.Scripts.TransformBinders.BoneControl;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

/// <summary>
/// Watches the player's real body while they use the world and keeps a running estimate of their eye
/// height and arm span, so calibration no longer depends on whatever pose they happened to be in on
/// the single frame an avatar loaded.
///
/// Both measurements under-measure constantly and cannot over-measure, so the estimator keeps a
/// high-water mark (see <see cref="BasisBodyEvidenceCore"/>). In practice the span settles the first
/// time the player gestures widely and the eye height the first time they stand still upright — both
/// of which happen within seconds of joining, and neither of which the old single-shot capture could
/// wait for.
///
/// The main thread only reads a handful of already-polled device positions; the floor estimate,
/// plausibility gating and statistics all run in a Burst job. Sampling every
/// <see cref="FrameInterval"/>'th frame keeps even that gather off the per-frame budget.
/// </summary>
public static class BasisBodyEvidenceSampler
{
    /// <summary>Sample one frame in this many. Body size does not move; more often buys nothing.</summary>
    public const int FrameInterval = 5;

    static NativeReference<BasisBodyEvidenceState> s_state;
    static JobHandle s_handle;
    static bool s_scheduled;
    static bool s_allocated;
    static int s_frameCounter;
    static float s_secondsSinceLastSample;

    static readonly System.Collections.Generic.List<float> s_trackerHeights = new(16);

    public static bool IsRunning => s_allocated;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Initialize()
    {
        Shutdown();
        s_state = new NativeReference<BasisBodyEvidenceState>(default, Allocator.Persistent);
        s_allocated = true;
        s_frameCounter = 0;
        s_secondsSinceLastSample = 0f;
        Application.quitting -= Shutdown;
        Application.quitting += Shutdown;
    }

    static void Shutdown()
    {
        if (!s_allocated)
        {
            return;
        }
        CompleteIfPending();
        if (s_state.IsCreated)
        {
            s_state.Dispose();
        }
        s_allocated = false;
    }

    /// <summary>
    /// Drops everything observed so far. Called when the evidence can no longer describe the player in
    /// front of us: an explicit recalibration (they are telling us to re-measure) or a switch into VR
    /// (desktop's eye height is a virtual number, never the player's body).
    /// </summary>
    public static void ResetEvidence()
    {
        if (!s_allocated)
        {
            return;
        }
        CompleteIfPending();
        s_state.Value = default;
        s_frameCounter = 0;
        s_secondsSinceLastSample = 0f;
        BasisDebug.Log("Body evidence reset; re-observing the player's size.", BasisDebug.LogTag.Avatar);
    }

    static void CompleteIfPending()
    {
        if (s_scheduled)
        {
            s_handle.Complete();
            s_scheduled = false;
        }
    }

    /// <summary>
    /// Per-frame entry from BasisEventDriver. Gathers on every <see cref="FrameInterval"/>'th frame and
    /// leaves the fold to a worker; the previous fold is joined first, which by then is long finished.
    /// </summary>
    public static void Simulate(float deltaTime)
    {
        if (!s_allocated)
        {
            return;
        }

        s_secondsSinceLastSample += deltaTime;

        // Desktop's "eye height" is a synthesized number and its hands are not the player's hands —
        // nothing here would describe a real body.
        if (BasisDeviceManagement.IsUserInDesktop())
        {
            return;
        }

        s_frameCounter++;
        if (s_frameCounter < FrameInterval)
        {
            return;
        }
        s_frameCounter = 0;

        CompleteIfPending();

        if (!TryGather(out BasisBodyEvidenceSample sample, out FixedList128Bytes<float> trackerHeights))
        {
            return;
        }

        s_secondsSinceLastSample = 0f;

        var job = new BasisBodyEvidenceJob
        {
            State = s_state,
            Sample = sample,
            TrackerHeights = trackerHeights,
            FootMountAllowance = BasisCalibrationMath.FootMountAllowanceMeters,
            FootBand = BasisCalibrationMath.FootBandMeters,
            MinFootBandTrackers = BasisCalibrationMath.MinFootBandTrackers,
            MinPlausible = BasisHeightDriver.MinPlausibleBodyMeasure,
            MaxPlausible = BasisHeightDriver.MaxPlausibleBodyMeasure,
        };
        s_handle = job.Schedule();
        s_scheduled = true;
    }

    /// <summary>
    /// Reads already-polled device poses into a blittable sample. Nothing here calls a Unity API or
    /// allocates; the cost is a walk of the device list and a few field reads.
    /// </summary>
    static bool TryGather(out BasisBodyEvidenceSample sample, out FixedList128Bytes<float> trackerHeights)
    {
        sample = default;
        trackerHeights = default;

        BasisDeviceManagement manager = BasisDeviceManagement.Instance;
        if (manager == null)
        {
            return false;
        }

        sample.DeltaSeconds = s_secondsSinceLastSample;

        // Eye height is only the player's body while they are actually standing in it. Seated mode
        // substitutes a virtual standing eye, so observing it would record the chair, not the player.
        BasisInput head = BasisLocalCameraDriver.Instance?.BasisLockToInput?.BasisInput;
        if (head != null && !SMModuleSitStand.IsSteatedMode)
        {
            Vector3 headPos = head.UnscaledDeviceCoord.position;
            if (headPos.sqrMagnitude > 1e-4f)
            {
                sample.HeadY = headPos.y;
                sample.HeadValid = true;
                sample.InjectedVerticalOffset = BasisLocalPlayspaceMover.VerticalOffset
                    + BasisHeightDriver.HeightModeGroundingOffset;
            }
        }

        // Arm span stays valid seated — the chair does not shorten your reach.
        if (manager.FindDevice(out BasisInput left, BasisBoneTrackedRole.LeftHand)
            && manager.FindDevice(out BasisInput right, BasisBoneTrackedRole.RightHand))
        {
            Vector3 l = HandSpanPoint(left);
            Vector3 r = HandSpanPoint(right);
            if (l.sqrMagnitude > 1e-4f && r.sqrMagnitude > 1e-4f)
            {
                // Horizontal only, matching the avatar-side span (CalculateAvatarArmSpan) so the two
                // sides of the ratio describe the same quantity.
                sample.HandSpan = Vector3.Distance(new Vector3(l.x, 0f, l.z), new Vector3(r.x, 0f, r.z));
                sample.HandsValid = sample.HandSpan > 0f;
            }
        }

        if (!sample.HeadValid && !sample.HandsValid)
        {
            return false;
        }

        // Same device filter as BasisLocalHeightCalculator.TryGetTrackedFloor: pinned devices are
        // head/hand evidence, never floor evidence, and a linked pair-half defers to its midpoint.
        s_trackerHeights.Clear();
        BasisObservableList<BasisInput> devices = manager.AllInputDevices;
        int count = devices.Count;
        for (int Index = 0; Index < count; Index++)
        {
            BasisInput input = devices[Index];
            if (input == null) continue;
            if (input is BasisTouchInputDevice) continue;
            if (input.IsLinked) continue;
            if (input.DeviceMatchSettings != null && input.DeviceMatchSettings.HasTrackedRole) continue;

            Vector3 unscaled = input.UnscaledDeviceCoord.position;
            if (unscaled.sqrMagnitude < 1e-4f) continue;
            s_trackerHeights.Add(unscaled.y);
            if (s_trackerHeights.Count >= trackerHeights.Capacity) break;
        }

        for (int Index = 0; Index < s_trackerHeights.Count; Index++)
        {
            trackerHeights.Add(s_trackerHeights[Index]);
        }
        return true;
    }

    /// <summary>
    /// The wrist the avatar's hand bone is driven to, matching
    /// <c>BasisLocalHeightCalculator.HandSpanPoint</c> — sampling the raw grip pose instead would
    /// over-read the span on backends that report one.
    /// </summary>
    static Vector3 HandSpanPoint(BasisInput input) =>
        input is BasisInputController controller ? controller.UnscaledHandTarget : input.UnscaledDeviceCoord.position;

    /// <summary>
    /// The observed standing eye height, once enough of the player has been seen to mean anything.
    /// </summary>
    public static bool TryGetEyeHeight(out float eyeHeight, out float confidence)
    {
        eyeHeight = 0f;
        confidence = 0f;
        if (!s_allocated)
        {
            return false;
        }
        CompleteIfPending();
        BasisBodyEvidenceState state = s_state.Value;
        return BasisBodyEvidenceCore.TryGetEstimate(state.Eye, out eyeHeight, out confidence);
    }

    /// <summary>
    /// The observed arm span, once enough of the player has been seen to mean anything.
    /// </summary>
    public static bool TryGetArmSpan(out float armSpan, out float confidence)
    {
        armSpan = 0f;
        confidence = 0f;
        if (!s_allocated)
        {
            return false;
        }
        CompleteIfPending();
        BasisBodyEvidenceState state = s_state.Value;
        return BasisBodyEvidenceCore.TryGetEstimate(state.ArmSpan, out armSpan, out confidence);
    }

    /// <summary>
    /// True when the player has looked persistently shorter than the size on record for long enough
    /// that posture cannot explain it — a shared headset that changed hands. The caller prompts rather
    /// than acting on it.
    /// </summary>
    public static bool LooksLikeADifferentPerson()
    {
        if (!s_allocated)
        {
            return false;
        }
        CompleteIfPending();
        BasisBodyEvidenceState state = s_state.Value;
        return BasisBodyEvidenceCore.LooksLikeADifferentPerson(state.Eye);
    }

    /// <summary>Sample counts for the calibration debug readout.</summary>
    public static void GetSampleCounts(out int eyeSamples, out int spanSamples)
    {
        eyeSamples = 0;
        spanSamples = 0;
        if (!s_allocated)
        {
            return;
        }
        CompleteIfPending();
        BasisBodyEvidenceState state = s_state.Value;
        eyeSamples = state.Eye.SampleCount;
        spanSamples = state.ArmSpan.SampleCount;
    }

    [BurstCompile]
    struct BasisBodyEvidenceJob : IJob
    {
        public NativeReference<BasisBodyEvidenceState> State;
        public BasisBodyEvidenceSample Sample;
        public FixedList128Bytes<float> TrackerHeights;
        public float FootMountAllowance;
        public float FootBand;
        public int MinFootBandTrackers;
        public float MinPlausible;
        public float MaxPlausible;

        public void Execute()
        {
            BasisBodyEvidenceState state = State.Value;
            bool hasFloor = BasisBodyEvidenceCore.TryEstimateFloor(
                TrackerHeights, Sample.HeadY,
                FootMountAllowance, FootBand, MinFootBandTrackers,
                MinPlausible, MaxPlausible,
                out float floorY);
            BasisBodyEvidenceCore.Fold(ref state, Sample, hasFloor, floorY, MinPlausible, MaxPlausible);
            State.Value = state;
        }
    }
}
