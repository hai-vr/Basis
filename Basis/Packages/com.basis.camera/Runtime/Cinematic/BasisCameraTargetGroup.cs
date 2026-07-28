using System.Collections.Generic;
using UnityEngine;

namespace Basis.Cinematics
{
    /// <summary>
    /// Collapses several subjects into the one the solvers work against, so a shot can frame a
    /// whole conversation rather than a single player. The bounding radius it produces is what
    /// Framing mode dollies against, which is how the group stays in shot as it spreads out.
    /// </summary>
    public static class BasisCameraTargetGroup
    {
        /// <summary>Roughly a player's width, used when a member carries no radius of its own.</summary>
        public const float DefaultMemberRadius = 0.4f;

        public static bool TryCombine(IReadOnlyList<BasisCameraSubject> members, IReadOnlyList<float> weights,
            out BasisCameraSubject combined)
        {
            combined = default;

            if (members == null || members.Count == 0)
            {
                return false;
            }

            int validCount = 0;
            int lastValid = -1;
            for (int Index = 0; Index < members.Count; Index++)
            {
                if (members[Index].Valid && Weight(weights, Index) > 0f)
                {
                    validCount++;
                    lastValid = Index;
                }
            }

            if (validCount == 0)
            {
                return false;
            }

            if (validCount == 1)
            {
                combined = members[lastValid];
                if (combined.Radius <= 0f)
                {
                    combined.Radius = DefaultMemberRadius;
                }
                return true;
            }

            Vector3 anchor = Vector3.zero;
            Vector3 look = Vector3.zero;
            Vector3 velocity = Vector3.zero;
            Vector3 facing = Vector3.zero;
            float scale = 0f;
            float ground = float.MaxValue;
            float total = 0f;

            for (int Index = 0; Index < members.Count; Index++)
            {
                BasisCameraSubject member = members[Index];
                float weight = Weight(weights, Index);
                if (!member.Valid || weight <= 0f)
                {
                    continue;
                }

                anchor += member.AnchorPos * weight;
                look += member.LookPoint * weight;
                velocity += member.Velocity * weight;
                facing += (member.Yaw * Vector3.forward) * weight;
                scale += (member.Scale > 1e-4f ? member.Scale : 1f) * weight;
                ground = Mathf.Min(ground, member.GroundPos.y);
                total += weight;
            }

            anchor /= total;
            look /= total;
            velocity /= total;
            scale /= total;

            List<Vector3> positions = new List<Vector3>(validCount);
            List<float> radii = new List<float>(validCount);
            List<float> memberWeights = new List<float>(validCount);
            for (int Index = 0; Index < members.Count; Index++)
            {
                BasisCameraSubject member = members[Index];
                float weight = Weight(weights, Index);
                if (!member.Valid || weight <= 0f)
                {
                    continue;
                }
                positions.Add(member.AnchorPos);
                radii.Add(member.Radius > 0f ? member.Radius : DefaultMemberRadius);
                memberWeights.Add(weight);
            }

            BasisCameraFraming.TryGetGroupBounds(positions, radii, memberWeights, out _, out float radius);

            combined = new BasisCameraSubject
            {
                Valid = true,
                AnchorPos = anchor,
                LookPoint = look,
                GroundPos = new Vector3(anchor.x, ground, anchor.z),
                Yaw = AverageYaw(facing),
                Scale = scale,
                Radius = Mathf.Max(radius, DefaultMemberRadius),
                Velocity = velocity,
            };
            return true;
        }

        /// <summary>
        /// Mean facing from summed direction vectors rather than averaged angles — averaging 350
        /// and 10 degrees numerically gives 180, the exact opposite of the answer.
        /// </summary>
        public static Quaternion AverageYaw(Vector3 summedForward)
        {
            summedForward.y = 0f;
            if (summedForward.sqrMagnitude < 1e-6f)
            {
                return Quaternion.identity;
            }
            return Quaternion.LookRotation(summedForward.normalized, Vector3.up);
        }

        private static float Weight(IReadOnlyList<float> weights, int index)
            => weights != null && index < weights.Count ? Mathf.Max(0f, weights[index]) : 1f;
    }
}
