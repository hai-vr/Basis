using Unity.Burst;
using Unity.Mathematics;
namespace Basis.IK
{
    [BurstCompile]
    public static class BasisNodPivotEstimatorCore
    {
        private const float detEpsilon = 1e-12f, minVariance = 1e-8f;
        public static BasisNodPivotSettings Defaults()
        {
            BasisNodPivotSettings s;
            s.PriorWeight = 0.15f;
            s.MinPitchRangeDeg = 18f;
            s.MinFitQuality = 0.70f;
            s.MaxPivotSpreadMeters = 0.02f;
            s.MaxVerticalRangeMeters = 0.20f;

            s.MaxArm = new float3(0.05f, 0.12f, 0.20f);
            s.Scale = 1f;
            return s;
        }
        public static void Solve( float3[] positions, quaternion[] rotations, int count, float3 priorArm, in BasisNodPivotSettings settings, out BasisNodPivotResult result)
        {
            result = default;
            result.Arm = priorArm;

            if (positions == null || rotations == null || count < 4) return;
            if (count > positions.Length || count > rotations.Length) return;

            float minPitch = float.MaxValue, maxPitch = float.MinValue;
            for (int i = 0; i < count; i++)
            {
                float3 fwd = math.mul(rotations[i], new float3(0f, 0f, 1f));
                float horiz = math.sqrt(fwd.x * fwd.x + fwd.z * fwd.z), pitch = math.degrees(math.atan2(fwd.y, horiz));
                minPitch = math.min(minPitch, pitch);
                maxPitch = math.max(maxPitch, pitch);
            }
            result.PitchRangeDeg = maxPitch - minPitch;
            if (!(result.PitchRangeDeg >= settings.MinPitchRangeDeg)) return;

            float minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < count; i++)
            {
                minY = math.min(minY, positions[i].y);
                maxY = math.max(maxY, positions[i].y);
            }
            result.VerticalRangeMeters = maxY - minY;
            if (!(result.VerticalRangeMeters <= settings.MaxVerticalRangeMeters * math.max(1e-3f, settings.Scale))) return;

            float inv = 1f / count;
            float3 meanPos = float3.zero;
            float3x3 meanRot = float3x3.zero;
            for (int i = 0; i < count; i++)
            {
                meanPos += positions[i];
                meanRot += new float3x3(rotations[i]);
            }
            meanPos *= inv;
            meanRot = MulScalar(meanRot, inv);

            float3x3 M = float3x3.zero;
            float3 b = float3.zero;
            float totalVar = 0f;
            for (int i = 0; i < count; i++)
            {
                float3x3 D = new float3x3(rotations[i]) - meanRot;
                float3 dp = positions[i] - meanPos;
                M += math.mul(math.transpose(D), D);
                b += math.mul(math.transpose(D), dp);
                totalVar += math.lengthsq(dp);
            }

            if (!(totalVar > minVariance)) return;

            float lambda = math.max(0f, settings.PriorWeight) * ((M.c0.x + M.c1.y + M.c2.z) / 3f);
            if (lambda > 0f)
            {
                M.c0.x += lambda;
                M.c1.y += lambda;
                M.c2.z += lambda;
                b += lambda * priorArm;
            }

            if (!TrySolveSymmetric3(in M, in b, out float3 arm)) return;
            if (!math.all(math.isfinite(arm))) return;

            float residual = 0f;
            for (int i = 0; i < count; i++)
            {
                float3x3 D = new float3x3(rotations[i]) - meanRot;
                residual += math.lengthsq(math.mul(D, arm) - (positions[i] - meanPos));
            }
            result.FitQuality = 1f - (residual / totalVar);
            if (!(result.FitQuality >= settings.MinFitQuality)) return;

            float scale = math.max(1e-3f, settings.Scale);
            float3 pivotMean = float3.zero;
            for (int i = 0; i < count; i++) pivotMean += positions[i] - math.mul(rotations[i], arm);
            pivotMean *= inv;

            float pivotVar = 0f;
            for (int i = 0; i < count; i++)
            {
                pivotVar += math.lengthsq((positions[i] - math.mul(rotations[i], arm)) - pivotMean);
            }
            result.PivotSpreadMeters = math.sqrt(pivotVar * inv);
            if (!(result.PivotSpreadMeters <= settings.MaxPivotSpreadMeters * scale)) return;

            float3 box = settings.MaxArm * scale;

            arm = math.clamp(arm, new float3(-box.x, 0f, 0f), box);

            result.Arm = arm;
            result.Accepted = true;
        }
        private static float3x3 MulScalar(float3x3 m, float s)
        {
            return new float3x3(m.c0 * s, m.c1 * s, m.c2 * s);
        }
        private static bool TrySolveSymmetric3(in float3x3 M, in float3 b, out float3 x)
        {
            x = default;

            float a00 = M.c0.x, a01 = M.c1.x, a02 = M.c2.x;
            float a11 = M.c1.y, a12 = M.c2.y;
            float a22 = M.c2.z, c00 = a11 * a22 - a12 * a12, c01 = a02 * a12 - a01 * a22, c02 = a01 * a12 - a02 * a11;
            float det = a00 * c00 + a01 * c01 + a02 * c02;
            if (!(math.abs(det) > detEpsilon)) return false;

            float c11 = a00 * a22 - a02 * a02, c12 = a02 * a01 - a00 * a12, c22 = a00 * a11 - a01 * a01;
            float invDet = 1f / det;
            x = new float3( (c00 * b.x + c01 * b.y + c02 * b.z) * invDet, (c01 * b.x + c11 * b.y + c12 * b.z) * invDet, (c02 * b.x + c12 * b.y + c22 * b.z) * invDet);
            return true;
        }
    }
}
