using Unity.Mathematics;
namespace Basis.IK
{
    public sealed class BasisNodPivotSampler
    {
        private readonly float3[] samplePositions;
        private readonly quaternion[] sampleRotations;
        private int filled, write;
        private float sampleAccum, solveAccum;
        private float3 armEstimate;
        private bool hasArmEstimate;
        public float SampleIntervalSeconds = 1f / 30f, SolveIntervalSeconds = 0.25f, BlendPerAcceptance = 0.15f;
        public BasisNodPivotResult LastResult;
        public bool HasEstimate => hasArmEstimate;
        public BasisNodPivotSampler(int capacity = 30)
        {
            if (capacity < 4) capacity = 4;
            samplePositions = new float3[capacity];
            sampleRotations = new quaternion[capacity];
        }
        public void Reset()
        {
            filled = 0;
            write = 0;
            sampleAccum = 0f;
            solveAccum = 0f;
            hasArmEstimate = false;
            armEstimate = default;
            LastResult = default;
        }
        public float3 Update(float3 eyePos, quaternion eyeRot, float dt, float3 priorArm, in BasisNodPivotSettings settings)
        {
            if (!(dt > 0f) || !math.all(math.isfinite(eyePos)))
            {
                return hasArmEstimate ? armEstimate : priorArm;
            }

            sampleAccum += dt;
            if (sampleAccum >= SampleIntervalSeconds)
            {
                sampleAccum = 0f;
                samplePositions[write] = eyePos;
                sampleRotations[write] = eyeRot;
                write = (write + 1) % samplePositions.Length;
                if (filled < samplePositions.Length) filled++;
            }

            solveAccum += dt;
            if (solveAccum >= SolveIntervalSeconds && filled >= 4)
            {
                solveAccum = 0f;
                BasisNodPivotEstimatorCore.Solve(samplePositions, sampleRotations, filled, hasArmEstimate ? armEstimate : priorArm, in settings, out LastResult);

                if (LastResult.Accepted)
                {
                    if (!hasArmEstimate)
                    {
                        armEstimate = priorArm;
                        hasArmEstimate = true;
                    }
                    armEstimate = math.lerp(armEstimate, LastResult.Arm, math.saturate(BlendPerAcceptance));
                }
            }

            return hasArmEstimate ? armEstimate : priorArm;
        }
    }
}
