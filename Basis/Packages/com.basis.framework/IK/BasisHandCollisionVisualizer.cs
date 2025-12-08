using UnityEngine;
using UnityEngine.Animations.Rigging;

[ExecuteAlways]
public class BasisHandCollisionVisualizer : MonoBehaviour
{
    [Header("Source")]
    public BasisFullBodyIK constraint;   // hook your constraint here
    public bool showLeftHand = true;
    public bool showRightHand = true;

    [Header("Colors")]
    public Color chestColor = Color.green;
    public Color handColor = Color.cyan;
    public Color collisionColor = Color.red;
    public Color hintColor = new Color(1f, 0.5f, 0f);

    public bool drawCapsuleSurfaces = true;
    public int capsuleCircleSegments = 16;

    private void OnDrawGizmos()
    {
        if (constraint == null)
            return;

        var data = constraint.data;

        // chestStart = chest; chestEnd = neck (same as your SolveHand() usage)
        if (data.chest == null || data.neck == null)
            return;

        Vector3 chestA = data.chest.position;
        Vector3 chestB = data.neck.position;
        float chestR = Mathf.Max(0f, data.chestRadius + data.collisionSkin);

        // Draw chest capsule
        Gizmos.matrix = Matrix4x4.identity;
        DrawCapsule(chestA, chestB, chestR, chestColor);

        if (showLeftHand)
            VisualizeHand(data, chestA, chestB, chestR, left: true);

        if (showRightHand)
            VisualizeHand(data, chestA, chestB, chestR, left: false);
    }

    private void VisualizeHand(BasisFullBodyData data, Vector3 chestA, Vector3 chestB, float chestR, bool left)
    {
        // --- Pull hand target data the same way your manager feeds it into the job ---
        Vector3 tgtPos;
        Quaternion tgtRot;
        Vector3 hintPos;
        Quaternion hintRot;

        if (left)
        {
            tgtPos = data.PositionLeftHand;
            tgtRot = data.RotationLeftHand;
            hintPos = data.HintPositionLeftHand;
            hintRot = data.HintRotationLeftHand;
        }
        else
        {
            tgtPos = data.PositionRightHand;
            tgtRot = data.RotationRightHand;
            hintPos = data.HintPositionRightHand;
            hintRot = data.HintRotationRightHand;
        }

        // Use same local capsule + radii as in SolveHand
        Vector3 hsLocal = data.handLocalStart;  // same for left/right in your data
        Vector3 heLocal = data.handLocalEnd;
        float handR = Mathf.Max(0f, data.handRadius + data.handSkin);

        bool useCapsule = data.useHandCapsule;

        if (useCapsule)
        {
            Vector3 handA = tgtPos + (tgtRot * hsLocal);
            Vector3 handB = tgtPos + (tgtRot * heLocal);

            // Draw hand capsule
            DrawCapsule(handA, handB, handR, handColor);

            // Compute collision the same way SolveHand does
            Vector3 correction = BasisAnimationRuntimeUtils.CapsuleCapsuleResolve(
                handA, handB, handR,
                chestA, chestB, chestR
            );

            if (correction.sqrMagnitude > 0f)
            {
                Gizmos.color = collisionColor;

                // Show correction vector
                Gizmos.DrawLine(tgtPos, tgtPos + correction);
                Gizmos.DrawSphere(tgtPos + correction, handR * 0.25f);

                // Optional: visualize the two closest points between capsules
                BasisAnimationRuntimeUtils.SegmentSegmentClosestPoints(
                    handA, handB, chestA, chestB,
                    out _, out _, out Vector3 c1, out Vector3 c2);

                Gizmos.DrawSphere(c1, handR * 0.15f);
                Gizmos.DrawSphere(c2, chestR * 0.15f);
                Gizmos.DrawLine(c1, c2);
            }
        }
        else
        {
            // Point-vs-capsule path (PushOutFromCapsule)
            Vector3 p = tgtPos;
            Vector3 pushed = BasisAnimationRuntimeUtils.PushOutFromCapsule(p, chestA, chestB, chestR);

            // Draw original + pushed positions
            Gizmos.color = handColor;
            Gizmos.DrawSphere(p, handR * 0.3f);

            Gizmos.color = collisionColor;
            Gizmos.DrawSphere(pushed, handR * 0.3f);
            Gizmos.DrawLine(p, pushed);
        }

        // Visualize hint position too (helps understand elbow steering)
        Gizmos.color = hintColor;
        Gizmos.DrawSphere(hintPos, handR * 0.25f);
        Gizmos.DrawLine(tgtPos, hintPos);
    }

    private void DrawCapsule(Vector3 a, Vector3 b, float radius, Color color)
    {
        Gizmos.color = color;

        Vector3 up = (b - a);
        float height = up.magnitude;

        if (height <= 1e-6f)
        {
            Gizmos.DrawWireSphere(a, radius);
            return;
        }

        Vector3 dir = up / height;
        Vector3 ortho = Vector3.Cross(dir, Vector3.up);
        if (ortho.sqrMagnitude < 1e-6f)
            ortho = Vector3.Cross(dir, Vector3.right);
        ortho.Normalize();
        Vector3 ortho2 = Vector3.Cross(dir, ortho);

        // “Cylindrical” lines
        Vector3 p1 = a + ortho * radius;
        Vector3 p2 = a - ortho * radius;
        Vector3 p3 = a + ortho2 * radius;
        Vector3 p4 = a - ortho2 * radius;

        Vector3 q1 = b + ortho * radius;
        Vector3 q2 = b - ortho * radius;
        Vector3 q3 = b + ortho2 * radius;
        Vector3 q4 = b - ortho2 * radius;

        Gizmos.DrawLine(p1, q1);
        Gizmos.DrawLine(p2, q2);
        Gizmos.DrawLine(p3, q3);
        Gizmos.DrawLine(p4, q4);

        if (!drawCapsuleSurfaces)
            return;

        // Approximate circles at each end
        int seg = Mathf.Max(4, capsuleCircleSegments);
        float step = Mathf.PI * 2f / seg;
        for (int i = 0; i < seg; i++)
        {
            float a0 = step * i;
            float a1 = step * (i + 1);

            Vector3 c0 = ortho * Mathf.Cos(a0) + ortho2 * Mathf.Sin(a0);
            Vector3 c1 = ortho * Mathf.Cos(a1) + ortho2 * Mathf.Sin(a1);

            Gizmos.DrawLine(a + c0 * radius, a + c1 * radius);
            Gizmos.DrawLine(b + c0 * radius, b + c1 * radius);
        }
    }
}
