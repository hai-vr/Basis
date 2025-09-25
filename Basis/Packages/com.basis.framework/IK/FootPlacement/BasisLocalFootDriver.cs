using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Drivers;
using System;
using UnityEngine;
public class BasisLocalFootDriver
{
    private Animator playerAnimator = null;
    private Transform AvatarTransform = null;
    private Transform leftFootTransform = null;
    private Transform rightFootTransform = null;

    // Store the foot world rotations at Start(), used to compute rotation deltas later.
    private Quaternion leftFootStartRot, rightFootStartRot;

    // Cached forward reference from character at Start() (used as the sagittal forward basis)
    private Vector3 initialForwardVector = Vector3.forward;

    public float _WorldHeightOffset
    {
        get { return giveWorldHeightOffset ? worldHeightOffset : 0f; }
    }

    [SerializeField, Range(0, 0.25f)]
    private float lengthFromHeelToToes = 0.1f;

    [SerializeField, Range(0, 60)]
    private float maxRotationAngle = 45;

    [SerializeField, Range(-0.05f, 0.125f)]
    private float ankleHeightOffset = 0;

    [SerializeField] private bool enableIKPositioning = true;
    [SerializeField] private bool enableIKRotating = true;

    [SerializeField, Range(0, 1)] private float globalWeight = 1;
    [SerializeField, Range(0, 1)] private float leftFootWeight = 1;
    [SerializeField, Range(0, 1)] private float rightFootWeight = 1;

    [SerializeField, Range(0, 0.1f)] private float smoothTime = 0.075f;
    [SerializeField, Range(0.05f, 0.1f)] private float raySphereRadius = 0.05f;
    [SerializeField, Range(0.1f, 2)] private float rayCastRange = 2;
    [SerializeField] private LayerMask groundLayers = new LayerMask();
    [SerializeField] private bool ignoreTriggers = true;

    [SerializeField, Range(0.1f, 1)] private float leftFootRayStartHeight = 0.5f;
    [SerializeField, Range(0.1f, 1)] private float rightFootRayStartHeight = 0.5f;

    [SerializeField] private bool enableFootLifting = true;
    [SerializeField] private float floorRange = 0;

    [SerializeField] private bool enableBodyPositioning = true;
    [SerializeField] private float crouchRange = 0.25f;
    [SerializeField] private float stretchRange = 0;

    [SerializeField] private bool giveWorldHeightOffset = false;
    [SerializeField] private float worldHeightOffset = 0;

    private RaycastHit leftFootRayHitInfo = new RaycastHit();
    private RaycastHit rightFootRayHitInfo = new RaycastHit();

    private float leftFootRayHitHeight = 0;
    private float rightFootRayHitHeight = 0;

    private Vector3 leftFootRayStartPosition = Vector3.zero;
    private Vector3 rightFootRayStartPosition = Vector3.zero;

    private Vector3 leftFootDirectionVector = Vector3.forward;
    private Vector3 rightFootDirectionVector = Vector3.forward;

    private Vector3 leftFootProjectionVector = Vector3.forward;
    private Vector3 rightFootProjectionVector = Vector3.forward;

    // Sagittal plane normals (built each frame from foot direction/projection)
    private Vector3 leftSagittalNormal = Vector3.right;
    private Vector3 rightSagittalNormal = Vector3.right;

    private float leftFootProjectedAngle = 0;
    private float rightFootProjectedAngle = 0;

    private Vector3 leftFootRayHitProjectionVector = Vector3.up;
    private Vector3 rightFootRayHitProjectionVector = Vector3.up;

    private float leftFootRayHitProjectedAngle = 0;
    private float rightFootRayHitProjectedAngle = 0;

    private float leftFootHeightOffset = 0;
    private float rightFootHeightOffset = 0;

    private Vector3 leftFootIKPositionBuffer = Vector3.zero;
    private Vector3 rightFootIKPositionBuffer = Vector3.zero;

    private Vector3 leftFootIKPositionTarget = Vector3.zero;
    private Vector3 rightFootIKPositionTarget = Vector3.zero;

    private float leftFootHeightLerpVelocity = 0;
    private float rightFootHeightLerpVelocity = 0;

    private Vector3 leftFootIKRotationBuffer = Vector3.up;
    private Vector3 rightFootIKRotationBuffer = Vector3.up;

    private Vector3 leftFootIKRotationTarget = Vector3.up;
    private Vector3 rightFootIKRotationTarget = Vector3.up;

    private Vector3 leftFootRotationLerpVelocity = Vector3.zero;
    private Vector3 rightFootRotationLerpVelocity = Vector3.zero;
    private void Update()
    {
        UpdateFootProjection();
        UpdateRayHitInfo();
        UpdateIKPositionTarget();
        UpdateIKRotationTarget();
    }

    private void OnAnimatorIK(int layerIndex)
    {
        LerpIKBufferToTarget();
        ApplyFootIK();
        ApplyBodyIK();
    }

    public void InitializeVariables()
    {
        playerAnimator = BasisLocalPlayer.Instance.BasisAvatar.Animator;
        AvatarTransform = BasisLocalPlayer.Instance.AvatarTransform;
        leftFootTransform = BasisLocalAvatarDriver.References.leftFoot;
        rightFootTransform = BasisLocalAvatarDriver.References.rightFoot;
        groundLayers = LayerMask.GetMask("Default");

        initialForwardVector = BasisLocalAvatarDriver.References.Forwards;

        // Capture world-space foot rotations at Start
        leftFootStartRot = leftFootTransform.rotation;
        rightFootStartRot = rightFootTransform.rotation;

        // Initialize IK buffers around ankle height
        float baseY = AvatarTransform.position.y + GetAnkleHeight();
        leftFootIKPositionBuffer.y = baseY;
        rightFootIKPositionBuffer.y = baseY;
    }

    private void UpdateFootProjection()
    {
        // Rotation delta since Start(): inverse(start) * current
        Quaternion leftDelta = Quaternion.Inverse(leftFootStartRot) * leftFootTransform.rotation;
        Quaternion rightDelta = Quaternion.Inverse(rightFootStartRot) * rightFootTransform.rotation;

        // “Forward” in the foot’s sagittal frame
        leftFootDirectionVector = leftDelta * initialForwardVector;
        rightFootDirectionVector = rightDelta * initialForwardVector;

        // Project those onto ground (XZ) to get horizontal heading
        leftFootProjectionVector = Vector3.ProjectOnPlane(leftFootDirectionVector, Vector3.up).normalized;
        rightFootProjectionVector = Vector3.ProjectOnPlane(rightFootDirectionVector, Vector3.up).normalized;

        // Build sagittal plane normals (side axes) for each foot
        leftSagittalNormal = Vector3.Cross(leftFootDirectionVector, leftFootProjectionVector).normalized;
        rightSagittalNormal = Vector3.Cross(rightFootDirectionVector, rightFootProjectionVector).normalized;

        // Project foot "up" into sagittal planes and measure pitch vs world up
        Vector3 leftUpProj = Vector3.ProjectOnPlane(leftFootTransform.up, leftSagittalNormal).normalized;
        Vector3 rightUpProj = Vector3.ProjectOnPlane(rightFootTransform.up, rightSagittalNormal).normalized;

        leftFootProjectedAngle = Vector3.Angle(leftUpProj, Vector3.up);
        rightFootProjectedAngle = Vector3.Angle(rightUpProj, Vector3.up);
    }

    private void UpdateRayHitInfo()
    {
        // Ray starts above each foot
        leftFootRayStartPosition = leftFootTransform.position;
        leftFootRayStartPosition.y += leftFootRayStartHeight;

        rightFootRayStartPosition = rightFootTransform.position;
        rightFootRayStartPosition.y += rightFootRayStartHeight;

        // Sphere casts downward
        Physics.SphereCast(
            leftFootRayStartPosition,
            raySphereRadius,
            Vector3.down,
            out leftFootRayHitInfo,
            rayCastRange,
            groundLayers,
            (QueryTriggerInteraction)(2 - Convert.ToInt32(ignoreTriggers))
        );

        Physics.SphereCast(
            rightFootRayStartPosition,
            raySphereRadius,
            Vector3.down,
            out rightFootRayHitInfo,
            rayCastRange,
            groundLayers,
            (QueryTriggerInteraction)(2 - Convert.ToInt32(ignoreTriggers))
        );

        // Left foot hit handling
        if (leftFootRayHitInfo.collider != null)
        {
            leftFootRayHitHeight = leftFootRayHitInfo.point.y;

            // Project ground normal into the same sagittal plane we used for the foot's angle
            leftFootRayHitProjectionVector = Vector3.ProjectOnPlane(leftFootRayHitInfo.normal, leftSagittalNormal).normalized;
            leftFootRayHitProjectedAngle = Vector3.Angle(leftFootRayHitProjectionVector, Vector3.up);
        }
        else
        {
            leftFootRayHitHeight = AvatarTransform.position.y;
            leftFootRayHitProjectedAngle = 0f;
            leftFootRayHitProjectionVector = Vector3.up;
        }

        // Right foot hit handling
        if (rightFootRayHitInfo.collider != null)
        {
            rightFootRayHitHeight = rightFootRayHitInfo.point.y;
            rightFootRayHitProjectionVector = Vector3.ProjectOnPlane(rightFootRayHitInfo.normal, rightSagittalNormal).normalized;
            rightFootRayHitProjectedAngle = Vector3.Angle(rightFootRayHitProjectionVector, Vector3.up);
        }
        else
        {
            rightFootRayHitHeight = AvatarTransform.position.y;
            rightFootRayHitProjectedAngle = 0f;
            rightFootRayHitProjectionVector = Vector3.up;
        }
    }

    private void UpdateIKPositionTarget()
    {
        leftFootHeightOffset = 0;
        rightFootHeightOffset = 0;

        float trueLeftFootProjectedAngle = leftFootProjectedAngle - leftFootRayHitProjectedAngle;
        if (trueLeftFootProjectedAngle > 0f)
        {
            leftFootHeightOffset += Mathf.Abs(Mathf.Sin(Mathf.Deg2Rad * trueLeftFootProjectedAngle) * lengthFromHeelToToes);
            leftFootHeightOffset += Mathf.Cos(Mathf.Deg2Rad * trueLeftFootProjectedAngle) * GetAnkleHeight();
        }
        else
        {
            leftFootHeightOffset += GetAnkleHeight();
        }

        float trueRightFootProjectedAngle = rightFootProjectedAngle - rightFootRayHitProjectedAngle;
        if (trueRightFootProjectedAngle > 0f)
        {
            rightFootHeightOffset += Mathf.Abs(Mathf.Sin(Mathf.Deg2Rad * trueRightFootProjectedAngle) * lengthFromHeelToToes);
            rightFootHeightOffset += Mathf.Cos(Mathf.Deg2Rad * trueRightFootProjectedAngle) * GetAnkleHeight();
        }
        else
        {
            rightFootHeightOffset += GetAnkleHeight();
        }

        leftFootIKPositionTarget.y = leftFootRayHitHeight + leftFootHeightOffset + _WorldHeightOffset;
        rightFootIKPositionTarget.y = rightFootRayHitHeight + rightFootHeightOffset + _WorldHeightOffset;
    }

    private void UpdateIKRotationTarget()
    {
        if (leftFootRayHitInfo.collider != null)
        {
            float t = Mathf.Clamp(Vector3.Angle(AvatarTransform.up, leftFootRayHitInfo.normal), 0, maxRotationAngle);
            leftFootIKRotationTarget = Vector3.Slerp(AvatarTransform.up, leftFootRayHitInfo.normal, t / (Vector3.Angle(AvatarTransform.up, leftFootRayHitInfo.normal) + 1f));
        }
        else
        {
            leftFootIKRotationTarget = AvatarTransform.up;
        }

        if (rightFootRayHitInfo.collider != null)
        {
            float t = Mathf.Clamp(Vector3.Angle(AvatarTransform.up, rightFootRayHitInfo.normal), 0, maxRotationAngle);
            rightFootIKRotationTarget = Vector3.Slerp(AvatarTransform.up, rightFootRayHitInfo.normal, t / (Vector3.Angle(AvatarTransform.up, rightFootRayHitInfo.normal) + 1f));
        }
        else
        {
            rightFootIKRotationTarget = AvatarTransform.up;
        }
    }

    private void LerpIKBufferToTarget()
    {
        // Foot lifting logic (stick to current IK pos if the foot is still above the desired floor)
        if (enableFootLifting && playerAnimator.GetIKPosition(AvatarIKGoal.LeftFoot).y >= leftFootIKPositionTarget.y + floorRange)
        {
            leftFootIKPositionBuffer.y = Mathf.SmoothDamp(
                leftFootIKPositionBuffer.y,
                playerAnimator.GetIKPosition(AvatarIKGoal.LeftFoot).y,
                ref leftFootHeightLerpVelocity,
                smoothTime
            );
        }
        else
        {
            leftFootIKPositionBuffer.y = Mathf.SmoothDamp(
                leftFootIKPositionBuffer.y,
                leftFootIKPositionTarget.y,
                ref leftFootHeightLerpVelocity,
                smoothTime
            );
        }

        if (enableFootLifting && playerAnimator.GetIKPosition(AvatarIKGoal.RightFoot).y >= rightFootIKPositionTarget.y + floorRange)
        {
            rightFootIKPositionBuffer.y = Mathf.SmoothDamp(
                rightFootIKPositionBuffer.y,
                playerAnimator.GetIKPosition(AvatarIKGoal.RightFoot).y,
                ref rightFootHeightLerpVelocity,
                smoothTime
            );
        }
        else
        {
            rightFootIKPositionBuffer.y = Mathf.SmoothDamp(
                rightFootIKPositionBuffer.y,
                rightFootIKPositionTarget.y,
                ref rightFootHeightLerpVelocity,
                smoothTime
            );
        }

        leftFootIKRotationBuffer = Vector3.SmoothDamp(
            leftFootIKRotationBuffer,
            leftFootIKRotationTarget,
            ref leftFootRotationLerpVelocity,
            smoothTime
        );

        rightFootIKRotationBuffer = Vector3.SmoothDamp(
            rightFootIKRotationBuffer,
            rightFootIKRotationTarget,
            ref rightFootRotationLerpVelocity,
            smoothTime
        );
    }

    private void ApplyFootIK()
    {
        playerAnimator.SetIKPositionWeight(AvatarIKGoal.LeftFoot, globalWeight * leftFootWeight);
        playerAnimator.SetIKPositionWeight(AvatarIKGoal.RightFoot, globalWeight * rightFootWeight);

        playerAnimator.SetIKRotationWeight(AvatarIKGoal.LeftFoot, globalWeight * leftFootWeight);
        playerAnimator.SetIKRotationWeight(AvatarIKGoal.RightFoot, globalWeight * rightFootWeight);

        // Copy X/Z from current IK positions (so we only solve Y), keep buffers smooth
        CopyByAxis(ref leftFootIKPositionBuffer, playerAnimator.GetIKPosition(AvatarIKGoal.LeftFoot), true, false, true);
        CopyByAxis(ref rightFootIKPositionBuffer, playerAnimator.GetIKPosition(AvatarIKGoal.RightFoot), true, false, true);

        if (enableIKPositioning)
        {
            playerAnimator.SetIKPosition(AvatarIKGoal.LeftFoot, leftFootIKPositionBuffer);
            playerAnimator.SetIKPosition(AvatarIKGoal.RightFoot, rightFootIKPositionBuffer);
        }

        // Rotate foot to align its up with the smoothed target normal
        Quaternion leftFootRotation = Quaternion.FromToRotation(AvatarTransform.up, leftFootIKRotationBuffer) * playerAnimator.GetIKRotation(AvatarIKGoal.LeftFoot);
        Quaternion rightFootRotation = Quaternion.FromToRotation(AvatarTransform.up, rightFootIKRotationBuffer) * playerAnimator.GetIKRotation(AvatarIKGoal.RightFoot);

        if (enableIKRotating)
        {
            playerAnimator.SetIKRotation(AvatarIKGoal.LeftFoot, leftFootRotation);
            playerAnimator.SetIKRotation(AvatarIKGoal.RightFoot, rightFootRotation);
        }
    }

    private void ApplyBodyIK()
    {
        if (!enableBodyPositioning) return;

        float minFootHeight = Mathf.Min(
            playerAnimator.GetIKPosition(AvatarIKGoal.LeftFoot).y,
            playerAnimator.GetIKPosition(AvatarIKGoal.RightFoot).y
        );

        playerAnimator.bodyPosition = new Vector3(
            playerAnimator.bodyPosition.x,
            playerAnimator.bodyPosition.y + LimitValueByRange(minFootHeight - AvatarTransform.position.y, 0),
            playerAnimator.bodyPosition.z
        );
    }

    private float GetAnkleHeight()
    {
        return raySphereRadius + ankleHeightOffset;
    }

    private void CopyByAxis(ref Vector3 target, Vector3 source, bool copyX, bool copyY, bool copyZ)
    {
        target = new Vector3(
            Mathf.Lerp(target.x, source.x, copyX ? 1f : 0f),
            Mathf.Lerp(target.y, source.y, copyY ? 1f : 0f),
            Mathf.Lerp(target.z, source.z, copyZ ? 1f : 0f)
        );
    }

    private float LimitValueByRange(float value, float floor)
    {
        if (value < floor - stretchRange)
        {
            return value + stretchRange;
        }
        else if (value > floor + crouchRange)
        {
            return value - crouchRange;
        }
        else
        {
            return floor;
        }
    }
}
