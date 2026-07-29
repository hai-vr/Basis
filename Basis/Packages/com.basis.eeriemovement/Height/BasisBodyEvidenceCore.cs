using Unity.Collections;
using UnityEngine;

namespace Basis.IK
{
    /// <summary>
    /// Running "largest credible value seen" for one body measurement.
    ///
    /// Body measurements under-measure constantly and cannot over-measure: a slouch, a crouch or a
    /// mid-step reads the eye short, arms at your sides read the span short, but nothing short of a
    /// tracking glitch reads either LONGER than the body actually is. So the trustworthy estimate is
    /// the high-water mark — not the value that happened to be true on the one frame an avatar loaded.
    ///
    /// Kept as the top <see cref="BasisBodyEvidenceCore.Capacity"/> samples rather than a bare maximum
    /// so a handful of glitched frames cannot pin the estimate: the reported value is the
    /// <see cref="BasisBodyEvidenceCore.OutlierRejection"/>+1'th largest, which survives that many bad
    /// readings intact.
    /// </summary>
    public struct BasisBodyEvidenceTrack
    {
        /// <summary>Highest accepted samples, descending. Index 0 is the largest seen.</summary>
        public FixedList64Bytes<float> Top;
        /// <summary>Samples accepted since the last reset — the confidence signal.</summary>
        public int SampleCount;
        /// <summary>Previous accepted raw value, for the quasi-static gate.</summary>
        public float Previous;
        public bool HasPrevious;

        /// <summary>
        /// Consecutive accepted samples sitting far below the settled estimate. The high-water mark can
        /// only ever rise, which is right for one unchanging body and wrong the moment a shorter person
        /// picks up the headset — they would inherit the previous player's size with no way out. A long
        /// run of low readings is the only evidence that distinguishes "someone else" from "slouching",
        /// and it takes far longer than any posture to accumulate.
        /// </summary>
        public int LowStreak;
    }

    /// <summary>Everything the sampler folds in one tick. Positions arrive already unscaled.</summary>
    public struct BasisBodyEvidenceSample
    {
        /// <summary>Head Y in unscaled device space.</summary>
        public float HeadY;
        /// <summary>Horizontal hand-to-hand distance; 0 when both hands are not tracked.</summary>
        public float HandSpan;
        /// <summary>Vertical shift Basis itself injected into device Y (play-space mover + grounding
        /// lift), subtracted when no tracked floor is available.</summary>
        public float InjectedVerticalOffset;
        /// <summary>Seconds since the previous sample, for the quasi-static gate.</summary>
        public float DeltaSeconds;
        public bool HeadValid;
        public bool HandsValid;
    }

    public struct BasisBodyEvidenceState
    {
        public BasisBodyEvidenceTrack Eye;
        public BasisBodyEvidenceTrack ArmSpan;
    }

    /// <summary>
    /// Folds live head/hand samples into robust body-size estimates. Pure and Burst-compatible: the
    /// runtime sampler gathers poses on the main thread and runs this in a job.
    /// </summary>
    public static class BasisBodyEvidenceCore
    {
        /// <summary>How many high samples to retain per measurement.</summary>
        public const int Capacity = 8;
        /// <summary>How many of the very highest samples to discard as possible glitches. The estimate
        /// is the next one down, so it survives this many bad readings.</summary>
        public const int OutlierRejection = 2;
        /// <summary>Accepted samples needed before an estimate is offered at all.</summary>
        public const int MinSamplesForConfidence = 24;
        /// <summary>Accepted samples at which the estimate is considered fully settled.</summary>
        public const int SamplesForFullConfidence = 120;

        /// <summary>Head vertical speed above which a sample is motion, not a stance — rejects jumps,
        /// which are the one thing that reads the eye height LONGER than the body.</summary>
        public const float MaxEyeSettleSpeed = 0.35f;
        /// <summary>Hand separation speed above which the span reading is mid-swing. Reaching out
        /// decelerates to zero at full extension, so the pose that matters still lands samples.</summary>
        public const float MaxSpanSettleSpeed = 0.8f;

        /// <summary>How far below the settled estimate a sample must sit to count toward the
        /// different-person streak. Slouching and a relaxed stance live well inside this.</summary>
        public const float DifferentPersonDrop = 0.12f;
        /// <summary>Consecutive low samples before we conclude it is a different body. At the sampler's
        /// cadence this is minutes of never once standing to the recorded height — no posture lasts
        /// that long, but a shorter person's entire session does.</summary>
        public const int DifferentPersonStreak = 900;

        public static void Reset(ref BasisBodyEvidenceState state)
        {
            state = default;
        }

        /// <summary>
        /// Folds one sample into both tracks. <paramref name="minPlausible"/>/<paramref name="maxPlausible"/>
        /// are the caller's body-measurement band; <paramref name="floorY"/> is the floor the head is
        /// measured against when <paramref name="hasFloor"/>, which cancels any play-space shift.
        /// </summary>
        public static void Fold(
            ref BasisBodyEvidenceState state,
            in BasisBodyEvidenceSample sample,
            bool hasFloor,
            float floorY,
            float minPlausible,
            float maxPlausible)
        {
            if (sample.HeadValid)
            {
                // Prefer the floor the player's own low trackers imply: head and trackers carry the same
                // vertical shift, so measuring between them cancels every offset without bookkeeping.
                float eye = hasFloor
                    ? sample.HeadY - floorY
                    : sample.HeadY - sample.InjectedVerticalOffset;
                FoldOne(ref state.Eye, eye, sample.DeltaSeconds, MaxEyeSettleSpeed, minPlausible, maxPlausible);
            }

            if (sample.HandsValid)
            {
                FoldOne(ref state.ArmSpan, sample.HandSpan, sample.DeltaSeconds, MaxSpanSettleSpeed, minPlausible, maxPlausible);
            }
        }

        static void FoldOne(ref BasisBodyEvidenceTrack track, float value, float deltaSeconds, float maxSpeed, float minPlausible, float maxPlausible)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f || value > maxPlausible)
            {
                // A value past the plausibility ceiling is a glitch, not a very tall player. Values BELOW
                // the floor of the band are kept: they are simply low samples, and a high-water estimate
                // discards them for free — rejecting them would bias the sample count.
                track.HasPrevious = false;
                return;
            }

            bool settled = true;
            if (track.HasPrevious && deltaSeconds > 0f)
            {
                settled = Mathf.Abs(value - track.Previous) / deltaSeconds <= maxSpeed;
            }
            track.Previous = value;
            track.HasPrevious = true;

            if (!settled)
            {
                return;
            }

            track.SampleCount++;

            // Watch for a body that is persistently smaller than the one on record before folding the
            // sample in, so the streak is measured against the estimate as it stood.
            if (TryGetEstimate(track, out float onRecord, out _))
            {
                if (value < onRecord * (1f - DifferentPersonDrop))
                {
                    if (track.LowStreak < int.MaxValue) track.LowStreak++;
                }
                else
                {
                    track.LowStreak = 0;
                }
            }

            if (value < minPlausible)
            {
                // Counted (it is real evidence the player is being observed) but never a candidate for
                // the body size, so a session spent crouching cannot shrink the estimate.
                return;
            }
            InsertDescending(ref track.Top, value);
        }

        /// <summary>
        /// True when the observations have looked like a different, smaller body for long enough that
        /// no posture explains it. The caller prompts rather than acting: guessing wrong and silently
        /// shrinking someone would be worse than asking.
        /// </summary>
        public static bool LooksLikeADifferentPerson(in BasisBodyEvidenceTrack track)
        {
            return track.LowStreak >= DifferentPersonStreak;
        }

        static void InsertDescending(ref FixedList64Bytes<float> top, float value)
        {
            int length = top.Length;
            int index = length;
            for (int i = 0; i < length; i++)
            {
                if (value > top[i])
                {
                    index = i;
                    break;
                }
            }

            if (index >= Capacity)
            {
                return; // smaller than everything retained, and the list is full
            }

            if (length < Capacity)
            {
                top.Add(value); // extended; the shift below overwrites this slot
            }

            for (int i = top.Length - 1; i > index; i--)
            {
                top[i] = top[i - 1];
            }
            top[index] = value;
        }

        /// <summary>
        /// The track's estimate: the <see cref="OutlierRejection"/>+1'th largest sample retained, or
        /// the smallest retained while fewer than that many exist. False until enough samples have
        /// accumulated to mean anything.
        /// </summary>
        public static bool TryGetEstimate(in BasisBodyEvidenceTrack track, out float estimate, out float confidence)
        {
            estimate = 0f;
            confidence = 0f;
            int length = track.Top.Length;
            if (length == 0 || track.SampleCount < MinSamplesForConfidence)
            {
                return false;
            }

            int index = OutlierRejection < length ? OutlierRejection : length - 1;
            estimate = track.Top[index];

            // Confidence ramps with both the number of samples seen and how many of the retained slots
            // are filled: an estimate resting on three readings should not outvote a settled one.
            float bySamples = Mathf.InverseLerp(MinSamplesForConfidence, SamplesForFullConfidence, track.SampleCount);
            float byDepth = Mathf.Clamp01((float)length / Capacity);
            confidence = Mathf.Clamp01(Mathf.Min(bySamples, byDepth));
            return estimate > 0f;
        }

        /// <summary>
        /// Floor under the player's feet implied by their own low trackers: the lowest tracker minus a
        /// mount allowance, provided enough trackers cluster in the foot band and the implied eye height
        /// is a plausible human measurement. Burst-compatible twin of the managed list version in
        /// BasisCalibrationMath; the caller passes that class's constants so there is one set of values.
        /// </summary>
        public static bool TryEstimateFloor(
            in FixedList128Bytes<float> trackerHeights,
            float headY,
            float footMountAllowance,
            float footBand,
            int minFootBandTrackers,
            float minPlausible,
            float maxPlausible,
            out float floorY)
        {
            floorY = 0f;
            int count = trackerHeights.Length;
            if (count < minFootBandTrackers)
            {
                return false;
            }

            float lowest = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (trackerHeights[i] < lowest) lowest = trackerHeights[i];
            }

            int inFootBand = 0;
            for (int i = 0; i < count; i++)
            {
                if (trackerHeights[i] <= lowest + footBand) inFootBand++;
            }
            if (inFootBand < minFootBandTrackers)
            {
                return false;
            }

            floorY = lowest - footMountAllowance;
            float impliedEye = headY - floorY;
            return impliedEye >= minPlausible && impliedEye <= maxPlausible;
        }
    }
}
