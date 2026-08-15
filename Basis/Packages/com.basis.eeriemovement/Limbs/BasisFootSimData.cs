using Unity.Mathematics;

public struct BasisFootNativeState
{
    public int sideSign;
    public float thighLen, shinLen, legLength;

    public int phase;
    public float3 plantedPos;
    public quaternion plantedRot;
    public float3 plantedBodyFwd;
    public float3 stepStartPos, stepTargetPos;

    public quaternion stepStartRot;
    public float stepTimer, stepDur;

    public float stepArcScale;
    public float plantedTime;

    public float stepUrgency;

    public quaternion landRot;

    public float3 idealPos, filteredNormal;
    public float3 currentPos;
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
    public float3 smoothedVelocity;
    public float3 smoothedBodyFwd;
    public float3 smoothedBodyRight;
    public float3 prevBodyFwd;
    public float smoothedYawRateDeg;

    public float smoothedAccelMag;
    public float3 prevRootFwd;
    public bool wasAirborne;
}

public struct BasisFootSimInput
{
    public float dt;
    public float3 headPos;
    public float3 hipsPos;
    public quaternion hipsRot;
    public quaternion chestRot;
    public quaternion headRot;
    public float3 avatarForward;
    public float3 avatarRight;
    public bool hasChest;
    public bool groundHit;
    public float3 groundPoint;
    public float splayWhenCrouched;
    public float3 playerUp;
}

public struct BasisFootSimParams
{
    public float predictionFactor;
    public float velocityBiasFactor;
    public float leadOffsetFactor;
    public float maxVelocityOffsetFraction;
    public float maxPredictionFraction;

    public float plantedLerpSpeed;
    public float rotationLerpSpeed;
    public float velocitySmoothAccel;
    public float velocitySmoothDecel;
    public float bodyFwdRateMoving;
    public float bodyFwdRateStationary;
    public float kneeHintLerpSpeed;

    public float maxFootTiltDegrees;
    public float maxFootYawDegrees;

    public float stepArcLiftExp;
    public float stepArcDropExp;
    public float stepHeightMinFraction;
    public float stepHeightStrideRefFraction;

    public float idleSpeedThreshold;
    public float idleBoostFraction;
    public float maxPlantedYawDegrees;

    public float idealSideEnforceFraction;
    public float stepTargetSideFraction;
    public float footSideEnforceFraction;

    public float maxVerticalDriftFraction;

    public float kneeForwardPushFraction;
    public float kneeMinSideFraction;

    public float bodyFwdHipsWeight;
    public float bodyFwdChestWeight;
    public float bodyFwdHeadWeight;

    public float hipBobFraction;

    public quaternion footAlignLeft, footAlignRight;

    public float stanceWidth;
    public float hipToFoot;
    public float leftLegLen, rightLegLen;
    public float leftThighLen, leftShinLen;
    public float rightThighLen, rightShinLen;
    public float footLength;
    public float ankleHeight;

    public float stepTriggerDist;
    public float strideScale;
    public float stepHeightCalc;
    public float stepDurSlow;
    public float stepDurFast;
    public float raySphereRadius;
    public float footHeightOffset;
    public float fastSpeedRef;
    public float rayCastRange;
}

public struct BasisFootSimOutput
{
    public float hipBob;
    public float3 hipSway;
    public bool airborne;

    public quaternion pelvisDelta;
}
