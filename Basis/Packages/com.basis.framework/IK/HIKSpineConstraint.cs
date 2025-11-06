// HIKSpineConstraint.cs
// Requires: com.unity.animation.rigging (Animation Rigging package)

using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Animations.Rigging;

[DisallowMultipleComponent]
[AddComponentMenu("Animation Rigging/HIK Spine Constraint")]
public class HIKSpineConstraint : RigConstraint<HIKSpineConstraint.Job, HIKSpineConstraint.Data, HIKSpineConstraint.Binder>
{
    [Serializable]
    public struct Data : IAnimationJobData
    {
        [Header("Chain")]
        public Transform hips;
        public Transform spine;
        public Transform chest;
        public Transform neck;
        public Transform head;

        [Header("Targets")]
        public Transform hipTarget;   // usually your hips goal (e.g., root/waist tracker)
        public Transform headTarget;  // usually your head/neck goal (e.g., HMD)
        public Transform chestTarget; // optional, used when Use Chest > 0

        [Header("Weights")]
        [Range(0, 1)] public float positionWeight;   // how much to follow head/hip target positions
        [Range(0, 1)] public float rotationWeight;   // how much to follow head/hip target rotations
        [Range(0, 1)] public float useChest;         // let chest follow chestTarget pos/rot
        [Range(0, 1)] public float alsoUseChestToMoveNeck;
        [Range(0, 1)] public float chestRotationUsesHead;
        [Range(0, 1)] public float improveSpineBuckling; // tension

        [Header("Behavior")]
        public bool headAlignmentMattersMore;     // when chain is too long, anchor at head
        public bool allowContortionist;           // disables limiting
        public bool doNotPreserveHipsToNeckCurvatureLimit;

        [Header("Reference (captured once)")]
        public float refPoseHipToHeadLength;
        public float refPoseHipToNeckLength;
        public float refPoseNeckLength;
        public Vector2 refPoseSpineVecForHipsRotation;//= new Vector2(1, 0.2f); // x=up, y=fwd contribution
        public Vector2 refPoseSpineVecForHeadRotation;// = new Vector2(1, 0.2f);
        public Vector2 refPoseChestRelation;// = new Vector2(0.33f, 0.1f);
        public Vector2 refPoseNeckRelation;// = new Vector2(0.75f, 0.05f);
        public float refPoseChestLength;// = 0.12f;  // meters
        public float refPoseSpineLength;// = 0.18f;
        public float refPoseNeckLength2;//= 0.10f;  // alias to avoid name clash
        public Vector3 capturedWithLossyScale;// = Vector3.one;

        [Header("Iterations")]
        [Range(1, 64)] public int fabrikIterations;

        public bool IsValid()
        {
            return hips && spine && chest && neck && head && headTarget && hipTarget && fabrikIterations > 0;
        }

        public void SetDefaultValues()
        {
            positionWeight = 1f;
            rotationWeight = 1f;
            useChest = 0.5f;
            alsoUseChestToMoveNeck = 0.35f;
            chestRotationUsesHead = 0.65f;
            improveSpineBuckling = 0.5f;
            headAlignmentMattersMore = true;
            allowContortionist = false;
            doNotPreserveHipsToNeckCurvatureLimit = false;
            fabrikIterations = 12;
            capturedWithLossyScale = Vector3.one;
            refPoseHipToHeadLength = 0.55f;
            refPoseHipToNeckLength = 0.45f;
            refPoseNeckLength = 0.10f;
            refPoseChestLength = 0.12f;
            refPoseSpineLength = 0.18f;
            refPoseNeckLength2 = 0.10f;
            refPoseSpineVecForHipsRotation = new Vector2(1f, 0.2f);
            refPoseSpineVecForHeadRotation = new Vector2(1f, 0.2f);
            refPoseChestRelation = new Vector2(0.33f, 0.1f);
            refPoseNeckRelation = new Vector2(0.75f, 0.05f);
        }
    }

    public struct Job : IWeightedAnimationJob
    {
        // bones
        public ReadWriteTransformHandle hips, spine, chest, neck, head;
        // targets
        public ReadOnlyTransformHandle hipTarget, headTarget, chestTarget;

        // job props
        public Vector3Property providedLossyScale;
        public FloatProperty positionWeight, rotationWeight, useChest, alsoUseChestToMoveNeck, chestRotationUsesHead, improveSpineBuckling;
        public BoolProperty headAlignmentMattersMore, allowContortionist, doNotPreserveHipsToNeckCurvatureLimit;

        public FloatProperty refPoseHipToHeadLength, refPoseHipToNeckLength, refPoseNeckLength;
        public FloatProperty refPoseChestLength, refPoseSpineLength, refPoseNeckLength2;
        public Vector2Property refPoseSpineVecForHipsRotation, refPoseSpineVecForHeadRotation, refPoseChestRelation, refPoseNeckRelation;

        public IntProperty fabrikIterations;
        public FloatProperty jobWeight { get; set; }

        public void ProcessRootMotion(AnimationStream stream) { }
        public void ProcessAnimation(AnimationStream stream)
        {
            var w = jobWeight.Get(stream);
            if (w <= 0f) return;

            // read current positions/rots
            var hipPos = hips.GetPosition(stream);
            var hipRot = hips.GetRotation(stream);
            var spinePos = spine.GetPosition(stream);
            var chestPos = chest.GetPosition(stream);
            var neckPos = neck.GetPosition(stream);
            var headPos = head.GetPosition(stream);
            var headRot = head.GetRotation(stream);

            var hipTPos = hipTarget.GetPosition(stream);
            var hipTRot = hipTarget.GetRotation(stream);
            var headTPos = headTarget.GetPosition(stream);
            var headTRot = headTarget.GetRotation(stream);

            bool chestTValid = chestTarget.IsValid(stream);
            var chestTPos = chestTValid ? chestTarget.GetPosition(stream) : chestPos;
            var chestTRot = chestTValid ? chestTarget.GetRotation(stream) : Quaternion.LookRotation((neckPos - chestPos).normalized, hipTRot * Vector3.up);

            // weights/refs
            var posW = positionWeight.Get(stream);
            var rotW = rotationWeight.Get(stream);
            var useChestW = useChest.Get(stream);
            var alsoUseNeck = alsoUseChestToMoveNeck.Get(stream);
            var chestUsesHead = chestRotationUsesHead.Get(stream);
            var buckle = improveSpineBuckling.Get(stream);
            var headMatters = headAlignmentMattersMore.Get(stream);
            var allowContortion = allowContortionist.Get(stream);
            var dontPreserveLimit = doNotPreserveHipsToNeckCurvatureLimit.Get(stream);

            var refHipHead = refPoseHipToHeadLength.Get(stream);
            var refHipNeck = refPoseHipToNeckLength.Get(stream);
            var refNeck = refPoseNeckLength.Get(stream);
            var refChestLen = refPoseChestLength.Get(stream);
            var refSpineLen = refPoseSpineLength.Get(stream);
            var refNeckLen2 = refPoseNeckLength2.Get(stream);

            var vSpineHips = refPoseSpineVecForHipsRotation.Get(stream);
            var vSpineHead = refPoseSpineVecForHeadRotation.Get(stream);
            var chestRel = refPoseChestRelation.Get(stream);
            var neckRel = refPoseNeckRelation.Get(stream);

            var providedScale = providedLossyScale.Get(stream);
            var capturedScale = Vector3.one; // if you want, bind your captured vector; here we keep 1 to avoid over-scaling
            var scale = providedScale.magnitude / Mathf.Max(capturedScale.magnitude, 1e-6f);

            // targets after weights
            var hipGoalPos = Vector3.Lerp(hipPos, hipTPos, posW);
            var hipGoalRot = Quaternion.Slerp(hipRot, hipTRot, rotW);
            var headGoalPos = Vector3.Lerp(headPos, headTPos, posW);
            var headGoalRot = Quaternion.Slerp(headRot, headTRot, rotW);

            // distance limiter (to avoid contortion beyond ref lengths)
            float maxLen = dontPreserveLimit
                ? (refSpineLen + refChestLen + refNeckLen2) * scale
                : (refHipNeck + refNeck) * scale;

            if (!allowContortion)
            {
                var dist = Vector3.Distance(hipGoalPos, headGoalPos);
                if (dist > maxLen && dist > 1e-6f)
                {
                    if (headMatters)
                    {
                        var dir = (hipGoalPos - headGoalPos).normalized;
                        hipGoalPos = headGoalPos + dir * maxLen;
                    }
                    else
                    {
                        var dir = (headGoalPos - hipGoalPos).normalized;
                        headGoalPos = hipGoalPos + dir * maxLen;
                    }
                }
            }

            // optional “tension” to reduce buckling when compressed
            if (buckle > 0f && !allowContortion)
            {
                var dist = Vector3.Distance(hipGoalPos, headGoalPos);
                var t = 1f - Mathf.Clamp01(dist / Mathf.Max(maxLen, 1e-6f));
                var hipsUp = hipGoalRot * Vector3.right;
                var tensionDir = (hipGoalPos - headGoalPos).normalized;
                var align = Mathf.InverseLerp(0.96f, 1f, Mathf.Clamp01(Vector3.Dot(-hipsUp.normalized, tensionDir)));
                var total = t * align * buckle * scale;
                if (total > 0f)
                {
                    var push = tensionDir * total;
                    hipGoalPos += push;

                    // re-apply limiter
                    var dist2 = Vector3.Distance(hipGoalPos, headGoalPos);
                    if (dist2 > maxLen && dist2 > 1e-6f)
                    {
                        var dir = (hipGoalPos - headGoalPos).normalized;
                        hipGoalPos = headGoalPos + dir * maxLen;
                    }
                }
            }

            // prime chain guesses (FABRIK seed)
            var spineToHead = headGoalPos - spinePos;
            var spineToHeadLen = spineToHead.magnitude;

            var hipsSpineUp = hipGoalRot * Vector3.right * vSpineHips.x;
            var hipsSpineFwd = hipGoalRot * Vector3.down * vSpineHips.y;
            var headSpineUp = headGoalRot * Vector3.right * vSpineHead.x;
            var headSpineFwd = headGoalRot * Vector3.down * vSpineHead.y;

            var chestBase = spinePos + hipsSpineUp * spineToHeadLen * chestRel.x
                                      + hipsSpineFwd * (chestRel.y * scale);
            var neckBase = headGoalPos - headSpineUp * spineToHeadLen * (1f - neckRel.x)
                                      + headSpineFwd * (neckRel.y * scale);

            var chestPrime = useChestW <= 0f ? chestBase : Vector3.Lerp(chestBase, chestTPos, useChestW);
            var neckPrime = (useChestW * alsoUseNeck) <= 0f
                ? neckBase
                : Vector3.Lerp(neckBase, chestTPos + (chestTRot * Vector3.right * refChestLen * scale), useChestW * alsoUseNeck);

            // tiny 3-bone FABRIK: spine -> chest -> neck -> headGoal
            var p0 = spinePos;
            var p1 = chestPrime;
            var p2 = neckPrime;
            var p3 = headGoalPos;

            float d01 = Vector3.Distance(p0, p1);
            float d12 = Vector3.Distance(p1, p2);
            float d23 = Vector3.Distance(p2, p3);

            int iters = Mathf.Max(1, fabrikIterations.Get(stream));
            for (int i = 0; i < iters; i++)
            {
                // forward: anchor to head
                p2 = p3 + (p2 - p3).normalized * Mathf.Max(d23, 1e-6f);
                p1 = p2 + (p1 - p2).normalized * Mathf.Max(d12, 1e-6f);
                p0 = p1 + (p0 - p1).normalized * Mathf.Max(d01, 1e-6f);
                // backward: anchor to spine
                p1 = p0 + (p1 - p0).normalized * Mathf.Max(d01, 1e-6f);
                p2 = p1 + (p2 - p1).normalized * Mathf.Max(d12, 1e-6f);
                p3 = p2 + (p3 - p2).normalized * Mathf.Max(d23, 1e-6f);
            }

            // rotations along chain
            Quaternion LookAlong(Vector3 from, Vector3 to, Vector3 upRef)
            {
                var dir = (to - from);
                if (dir.sqrMagnitude < 1e-10f) dir = Vector3.forward;
                return Quaternion.LookRotation(dir.normalized, upRef);
            }

            // “roll” helper: blend hip/head up vectors depending on section
            Vector3 spineRollRef = Vector3.Slerp(hipGoalRot * Vector3.down, headGoalRot * Vector3.down, 0.25f);
            Vector3 chestRollRef = Vector3.Slerp(hipGoalRot * Vector3.down, headGoalRot * Vector3.down, chestUsesHead);
            Vector3 neckRollRef = headGoalRot * Vector3.down;

            var hipsRotOut = hipGoalRot;
            var spineRotOut = LookAlong(p0, p1, spineRollRef);
            var chestRotOut = LookAlong(p1, p2, chestRollRef);
            var neckRotOut = LookAlong(p2, p3, neckRollRef);
            var headRotOut = headGoalRot;

            // optional head realign by shifting hips (position write)
            if (headMatters)
            {
                var realHeadPos = p3;
                var delta = headGoalPos - realHeadPos;
                hipGoalPos += delta;
                p0 += delta; p1 += delta; p2 += delta; p3 += delta;
            }

            // write back with overall constraint weight w
            // (blend rotations toward outputs; positions only for hips when realigning)
            hips.SetRotation(stream, Quaternion.Slerp(hips.GetRotation(stream), hipsRotOut, w));
            if (headMatters)
                hips.SetPosition(stream, Vector3.Lerp(hips.GetPosition(stream), hipGoalPos, w));

            spine.SetRotation(stream, Quaternion.Slerp(spine.GetRotation(stream), spineRotOut, w));
            chest.SetRotation(stream, Quaternion.Slerp(chest.GetRotation(stream), chestRotOut, w));
            neck.SetRotation(stream, Quaternion.Slerp(neck.GetRotation(stream), neckRotOut, w));
            head.SetRotation(stream, Quaternion.Slerp(head.GetRotation(stream), headRotOut, w));
        }

        public void SetWeight(AnimationStream stream, float weight) => jobWeight.Set(stream, weight);
        public float GetWeight(AnimationStream stream) => jobWeight.Get(stream);
    }

    public class Binder : AnimationJobBinder<Job, Data>
    {
        public override Job Create(Animator animator, ref Data data, Component component)
        {
            var job = new Job
            {
                hips = ReadWriteTransformHandle.Bind(animator, data.hips),
                spine = ReadWriteTransformHandle.Bind(animator, data.spine),
                chest = ReadWriteTransformHandle.Bind(animator, data.chest),
                neck = ReadWriteTransformHandle.Bind(animator, data.neck),
                head = ReadWriteTransformHandle.Bind(animator, data.head),

                hipTarget = ReadOnlyTransformHandle.Bind(animator, data.hipTarget),
                headTarget = ReadOnlyTransformHandle.Bind(animator, data.headTarget),
                chestTarget = (data.chestTarget ? ReadOnlyTransformHandle.Bind(animator, data.chestTarget) : ReadOnlyTransformHandle.Bind(animator, null)),

               // jobWeight = FloatProperty.Bind(animator, component,))
            };

            // bind all properties once (zero GC)
            job.providedLossyScale = Vector3Property.Bind(animator, component, nameof(Data.capturedWithLossyScale));

            job.positionWeight = FloatProperty.Bind(animator, component, nameof(Data.positionWeight));
            job.rotationWeight = FloatProperty.Bind(animator, component, nameof(Data.rotationWeight));
            job.useChest = FloatProperty.Bind(animator, component, nameof(Data.useChest));
            job.alsoUseChestToMoveNeck = FloatProperty.Bind(animator, component, nameof(Data.alsoUseChestToMoveNeck));
            job.chestRotationUsesHead = FloatProperty.Bind(animator, component, nameof(Data.chestRotationUsesHead));
            job.improveSpineBuckling = FloatProperty.Bind(animator, component, nameof(Data.improveSpineBuckling));

            job.headAlignmentMattersMore = BoolProperty.Bind(animator, component, nameof(Data.headAlignmentMattersMore));
            job.allowContortionist = BoolProperty.Bind(animator, component, nameof(Data.allowContortionist));
            job.doNotPreserveHipsToNeckCurvatureLimit = BoolProperty.Bind(animator, component, nameof(Data.doNotPreserveHipsToNeckCurvatureLimit));

            job.refPoseHipToHeadLength = FloatProperty.Bind(animator, component, nameof(Data.refPoseHipToHeadLength));
            job.refPoseHipToNeckLength = FloatProperty.Bind(animator, component, nameof(Data.refPoseHipToNeckLength));
            job.refPoseNeckLength = FloatProperty.Bind(animator, component, nameof(Data.refPoseNeckLength));
            job.refPoseChestLength = FloatProperty.Bind(animator, component, nameof(Data.refPoseChestLength));
            job.refPoseSpineLength = FloatProperty.Bind(animator, component, nameof(Data.refPoseSpineLength));
            job.refPoseNeckLength2 = FloatProperty.Bind(animator, component, nameof(Data.refPoseNeckLength2));

            job.refPoseSpineVecForHipsRotation = Vector2Property.Bind(animator, component, nameof(Data.refPoseSpineVecForHipsRotation));
            job.refPoseSpineVecForHeadRotation = Vector2Property.Bind(animator, component, nameof(Data.refPoseSpineVecForHeadRotation));
            job.refPoseChestRelation = Vector2Property.Bind(animator, component, nameof(Data.refPoseChestRelation));
            job.refPoseNeckRelation = Vector2Property.Bind(animator, component, nameof(Data.refPoseNeckRelation));

            job.fabrikIterations = IntProperty.Bind(animator, component, nameof(Data.fabrikIterations));

            return job;
        }

        public override void Destroy(Job job) { }
    }
}
