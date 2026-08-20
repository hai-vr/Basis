using Unity.Mathematics;
public struct BasisFootNativeState
{
    public int sideSign;
    public float thighLen, shinLen, legLength;
    public int phase;
    public float3 plantedPos;
    public quaternion plantedRot;
    public float3 plantedBodyFwd, stepStartPos, stepTargetPos;
    public quaternion stepStartRot;
    public float stepTimer, stepDur, stepArcScale, plantedTime, stepUrgency;
    public quaternion landRot;
    public float3 idealPos, filteredNormal, currentPos;
    public quaternion currentRot;
    public float3 kneeHint;
    public float toeBendDeg;
    public float3 toeBendAxis;
    public bool wantsStep;
    public float3 predictedTargetXZ;
}
public struct BasisFootSimState
{
    public float3 prevHeadPos;
    public float prevHeadYaw;
    public float3 smoothedVelocity, smoothedBodyFwd, smoothedBodyRight, prevBodyFwd;
    public float smoothedYawRateDeg, smoothedAccelMag;
    public float3 prevRootFwd;
    public bool wasAirborne;
}
public struct BasisFootSimInput
{
    public float dt;
    public float3 headPos, hipsPos;
    public quaternion hipsRot, chestRot, headRot;
    public float3 avatarForward, avatarRight;
    public bool hasChest, groundHit;
    public float3 groundPoint;
    public bool leftGroundValid, rightGroundValid;
    public float leftGroundUp, rightGroundUp, splayWhenCrouched;
    public float3 playerUp;
}
public struct BasisFootSimParams
{
    public float predictionFactor, velocityBiasFactor, leadOffsetFactor, maxVelocityOffsetFraction;
    public float maxPredictionFraction, plantedLerpSpeed, rotationLerpSpeed, velocitySmoothAccel, velocitySmoothDecel;
    public float bodyFwdRateMoving, bodyFwdRateStationary, kneeHintLerpSpeed, maxFootTiltDegrees, maxFootYawDegrees;
    public float stepArcLiftExp, stepArcDropExp, stepHeightMinFraction, stepHeightStrideRefFraction, idleSpeedThreshold;
    public float idleBoostFraction, maxPlantedYawDegrees, idealSideEnforceFraction, stepTargetSideFraction;
    public float footSideEnforceFraction, maxVerticalDriftFraction, kneeForwardPushFraction, kneeMinSideFraction;
    public float bodyFwdHipsWeight, bodyFwdChestWeight, bodyFwdHeadWeight, hipBobFraction;
    public quaternion footAlignLeft, footAlignRight;
    public float stanceWidth, hipToFoot, leftLegLen, rightLegLen, leftThighLen, leftShinLen, rightThighLen;
    public float rightShinLen, footLength, ankleHeight, stepTriggerDist, strideScale, stepHeightCalc, stepDurSlow;
    public float stepDurFast, raySphereRadius, footHeightOffset, fastSpeedRef, rayCastRange;
}
public struct BasisFootSimOutput
{
    public float hipBob;
    public float3 hipSway;
    public bool airborne;
    public quaternion pelvisDelta;
}
