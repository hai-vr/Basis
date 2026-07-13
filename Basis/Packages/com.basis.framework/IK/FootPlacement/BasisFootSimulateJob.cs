using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile]
public struct BasisFootSimulateJob : IJob
{
    public BasisFootSimParams p;
    public NativeArray<BasisFootNativeState> feet;   // length 2: [0]=left, [1]=right
    public NativeArray<BasisFootSimState> simState;   // length 1
    public NativeArray<BasisFootSimInput> input;      // length 1
    public NativeArray<BasisFootSimOutput> output;    // length 1

    public void Execute()
    {
        var inp = input[0];
        var sim = simState[0];
        var left = feet[0];
        var right = feet[1];
        float dt = inp.dt;

        if (dt <= 0f) return;

        float3 up = inp.playerUp;
        if (math.lengthsq(up) < 0.001f) up = new float3(0, 1, 0);

        // ── Velocity from the HIPS ──
        // Was taken from the HEAD. The head is the worst available proxy for body translation: it bobs with every
        // step, sways, and swings a long lever arm whenever you look around, so "speed" carried gait-frequency
        // noise and look-around motion that has nothing to do with travelling. The hips ARE the body's
        // translation -- the pelvis is the COM proxy the whole of gait biomechanics is written against -- so the
        // feet now pace off the thing that is actually moving.
        //
        // prevHeadPos keeps its name/slot in the state struct (it is the previous SAMPLE position) so the layout
        // and the sweep's mirrored state stay put; it now holds the hips.
        float3 velSample = inp.hipsPos;
        float3 rawVel = (velSample - sim.prevHeadPos) / dt;
        rawVel -= up * math.dot(rawVel, up); // strip vertical component
        sim.prevHeadPos = velSample;

        bool decelerating = math.lengthsq(rawVel) < math.lengthsq(sim.smoothedVelocity);
        float vAlpha = 1f - math.exp(-(decelerating ? p.velocitySmoothDecel : p.velocitySmoothAccel) * dt);
        sim.smoothedVelocity = math.lerp(sim.smoothedVelocity, rawVel, vAlpha);

        // HeadYaw
        float3 headFwdFull = math.mul(inp.headRot, new float3(0, 0, 1));
        float3 headFwdFlat = headFwdFull - up * math.dot(headFwdFull, up);
        sim.prevHeadYaw = math.lengthsq(headFwdFlat) < 0.001f
            ? sim.prevHeadYaw
            : math.atan2(headFwdFlat.x, headFwdFlat.z) * math.TODEGREES;

        float speed = math.length(sim.smoothedVelocity);

        // ── Body forward ──
        float3 rawFwd = ComputeBodyForward(inp, sim, p, up);

        // Body yaw rate (deg/s), smoothed — paces stepping so feet keep up when turning / spinning.
        // Deliberately measured BEFORE the root carry below, so a character-controller turn still counts as yaw
        // rate and still paces the steps faster.
        float bodyYawRate = math.lengthsq(sim.prevBodyFwd) > 0.001f ? SignedAngle(sim.prevBodyFwd, rawFwd, up) / dt : 0f;
        sim.prevBodyFwd = rawFwd;
        sim.smoothedYawRateDeg = math.lerp(sim.smoothedYawRateDeg, bodyYawRate, 1f - math.exp(-p.bodyFwdRateMoving * dt));

        // ── Ride the root, THEN smooth ──
        // smoothedBodyFwd is a WORLD vector, but it is derived from the hips/chest/head bones, which are rigid
        // children of the player root. So a character-controller turn rotates all of them identically -- it is a
        // deliberate, known input, not noise -- yet the exponential filter treated it as something to chase, at
        // tau = 400 ms whenever translation speed was under 0.1 m/s. Turning in place IS speed ~0, so a turn got
        // the SLOWEST possible tracking: the step targets were built from a body forward that was ~0.4 s stale and
        // the feet floated after it. Same bug class as the world-referenced knee swivel.
        //
        // Rotating the filter's STATE by the root's own yaw delta first makes a root turn pass through with zero
        // filter error (instant), while the organic deviation -- hips/chest/head wobbling RELATIVE to the root --
        // is still damped exactly as before. The filter now smooths only what it was ever meant to smooth.
        float3 rootFwd = inp.avatarForward - up * math.dot(inp.avatarForward, up);
        if (math.lengthsq(rootFwd) > 1e-6f)
        {
            rootFwd = math.normalize(rootFwd);
            if (math.lengthsq(sim.prevRootFwd) > 1e-6f && math.lengthsq(sim.smoothedBodyFwd) > 1e-6f)
            {
                float rootYawDeg = SignedAngle(sim.prevRootFwd, rootFwd, up);
                quaternion rootDelta = quaternion.AxisAngle(up, math.radians(rootYawDeg));
                sim.smoothedBodyFwd = math.mul(rootDelta, sim.smoothedBodyFwd);
            }
            sim.prevRootFwd = rootFwd;
        }

        // Turning in place is speed ~0, so the speed test alone always picked the slow rate exactly when the body
        // was rotating fastest. Any real turn (physical or stick) gets the responsive rate.
        bool turning = math.abs(sim.smoothedYawRateDeg) > 20f;
        float fwdRate = (speed > 0.1f || turning) ? p.bodyFwdRateMoving : p.bodyFwdRateStationary;
        float fwdAlpha = 1f - math.exp(-fwdRate * dt);
        // Degeneracy checks run BEFORE normalize: normalizing a near-zero vector yields NaN, and
        // a NaN never compares < epsilon — with smoothedBodyFwd self-feeding, one bad frame would
        // otherwise corrupt the sim permanently.
        float3 blendedFwd = Slerp3(sim.smoothedBodyFwd, rawFwd, fwdAlpha);
        sim.smoothedBodyFwd = math.lengthsq(blendedFwd) < 0.001f ? rawFwd : math.normalize(blendedFwd);

        float3 rightCross = math.cross(up, sim.smoothedBodyFwd);
        sim.smoothedBodyRight = math.lengthsq(rightCross) < 0.001f ? inp.avatarRight : math.normalize(rightCross);

        float3 bodyFwd = sim.smoothedBodyFwd;
        float3 bodyRight = sim.smoothedBodyRight;

        float3 rawRightCross = math.cross(up, rawFwd);
        float3 rawRight = math.lengthsq(rawRightCross) < 0.001f ? bodyRight : math.normalize(rawRightCross);

        // ── Ground ──
        // Project hips onto the horizontal plane (perpendicular to player up)
        float hipsUpComponent = math.dot(inp.hipsPos, up);
        float3 hipsFlat = inp.hipsPos - up * hipsUpComponent;
        float3 velDir = sim.smoothedVelocity - up * math.dot(sim.smoothedVelocity, up);

        float groundUpComponent;
        bool airborne;
        if (inp.groundHit)
        {
            // Planted feet carry hit.point + footHeightOffset; the ideal/vertical-snap level must
            // share that convention or the drift snap buries a planted foot by the offset.
            groundUpComponent = math.dot(inp.groundPoint, up) + p.footHeightOffset;
            airborne = false;
        }
        else
        {
            // hipToFoot measures Hips→Foot bone, but the ground is ankleHeight
            // below the Foot bone.  Subtract the full distance so the fallback
            // ground level matches what a successful raycast would produce.
            // Then add footHeightOffset, just like raycasted positions do, so
            // the feet don't hover above the floor when the raycast misses.
            groundUpComponent = hipsUpComponent - p.hipToFoot - p.ankleHeight + p.footHeightOffset;
            airborne = true;
        }

        // ── Ideal positions ──
        float3 moveDir = math.lengthsq(velDir) > 0.01f ? math.normalize(velDir) : bodyFwd;
        float avgLeg = (p.leftLegLen + p.rightLegLen) * 0.5f;
        float maxOffset = avgLeg * p.maxVelocityOffsetFraction;
        float baseBias = math.min(speed * p.velocityBiasFactor, maxOffset);
        float3 center = hipsFlat + up * groundUpComponent + moveDir * baseBias;
        float halfStance = p.stanceWidth * 0.5f;

        float leadAmount = math.min(speed * p.velocityBiasFactor * p.leadOffsetFactor, maxOffset * 0.5f);
        float3 leadOffset = moveDir * leadAmount;

        float leftDist = HDist(left.plantedPos, center - bodyRight * halfStance, up);
        float rightDist = HDist(right.plantedPos, center + bodyRight * halfStance, up);

        if (leftDist >= rightDist)
        {
            left.idealPos = center - bodyRight * halfStance + leadOffset;
            right.idealPos = center + bodyRight * halfStance;
        }
        else
        {
            left.idealPos = center - bodyRight * halfStance;
            right.idealPos = center + bodyRight * halfStance + leadOffset;
        }

        // ── Enforce side on ideals ──
        float3 hipsGround = hipsFlat + up * groundUpComponent;
        EnforceSide(ref left.idealPos, hipsGround, rawRight, -1, halfStance * p.idealSideEnforceFraction);
        EnforceSide(ref right.idealPos, hipsGround, rawRight, +1, halfStance * p.idealSideEnforceFraction);

        // ── Step parameters ──
        // Pace steps by translation OR body rotation: a spin (high yaw rate, no translation) must step
        // as fast as a fast walk so the feet keep up and don't cross. fastYawRef is the yaw rate above
        // which even fast steps can't keep up; go full-fast at half that.
        float fastYawRef = math.max(1f, 0.5f * p.maxPlantedYawDegrees / math.max(0.01f, p.stepDurFast));
        float yawT = math.saturate(math.abs(sim.smoothedYawRateDeg) / fastYawRef);
        float speedT = math.max(math.saturate(speed / p.fastSpeedRef), yawT);
        // Idle boost only when genuinely stationary -- NOT spinning in place (which has speed ~0 but
        // a high yaw rate); boosting the trigger there steps late and the foot crosses.
        bool stationary = speed < p.idleSpeedThreshold && math.abs(sim.smoothedYawRateDeg) < 20f;
        float idleBoost = stationary ? p.stepTriggerDist * p.idleBoostFraction : 0f;

        // The trigger drift is CAPPED at what the leg can actually recover.
        //
        // It used to grow without bound as `stepTriggerDist + speed * strideScale` -- 59 cm of drag at 5 m/s,
        // 80 cm at 7 m/s. But the foot can only ever be PLANTED about 0.35*leg (~30 cm) ahead of the hips before
        // the leg over-extends, so past a point the foot is being asked to recover more ground than it can reach:
        // it stays glued while the body races past, the leg stretches, and the feet read as "too slow to keep up".
        // Beyond the cap the only way to hold speed is to step MORE OFTEN, which is exactly what a real human
        // does -- stride length saturates and cadence takes over. Capping the drift makes that happen.
        float avgLegT = (p.leftLegLen + p.rightLegLen) * 0.5f;
        float threshold = math.min(p.stepTriggerDist + speed * p.strideScale + idleBoost, avgLegT * 0.55f);
        float stepDur = math.lerp(p.stepDurSlow, p.stepDurFast, speedT);

        // ── Vertical correction ──
        if (airborne)
        {
            float airUpComp = groundUpComponent;
            if (left.phase == 0) { SetUpComponent(ref left.currentPos, airUpComp, up); SetUpComponent(ref left.plantedPos, airUpComp, up); }
            if (right.phase == 0) { SetUpComponent(ref right.currentPos, airUpComp, up); SetUpComponent(ref right.plantedPos, airUpComp, up); }
        }
        else
        {
            float maxVD = p.hipToFoot * p.maxVerticalDriftFraction;
            if (left.phase == 0 && math.abs(GetUpComponent(left.plantedPos, up) - GetUpComponent(left.idealPos, up)) > maxVD)
            {
                float idealUp = GetUpComponent(left.idealPos, up);
                SetUpComponent(ref left.currentPos, idealUp, up);
                SetUpComponent(ref left.plantedPos, idealUp, up);
            }
            if (right.phase == 0 && math.abs(GetUpComponent(right.plantedPos, up) - GetUpComponent(right.idealPos, up)) > maxVD)
            {
                float idealUp = GetUpComponent(right.idealPos, up);
                SetUpComponent(ref right.currentPos, idealUp, up);
                SetUpComponent(ref right.plantedPos, idealUp, up);
            }
        }

        // Idle leg stiffening: a world-locked foot + a head-driven hip force the knee to track ~half the body
        // sway (the leg tilts rigidly about the foot). When stationary, let each planted foot gently give toward
        // the body's drift so the leg tilts less and the knee stays stiller -- a direct trade (stiffer knee = more
        // idle foot give). Walking is untouched (idle-only). idleStiffenRate 0 = off (locked feet, knee tracks ~0.5x).
        const float idleStiffenRate = 0f;
        if (stationary && idleStiffenRate > 0f)
        {
            float g = 1f - math.exp(-idleStiffenRate * dt);
            if (left.phase == 0) left.plantedPos += ProjectOntoUpPlane(left.idealPos - left.plantedPos, up) * g;
            if (right.phase == 0) right.plantedPos += ProjectOntoUpPlane(right.idealPos - right.plantedPos, up) * g;
        }

        // ── Update feet ──
        UpdateFoot(ref left, ref right, rawFwd, sim.smoothedVelocity, speed, threshold, stepDur, dt, up);
        UpdateFoot(ref right, ref left, rawFwd, sim.smoothedVelocity, speed, threshold, stepDur, dt, up);

        // One foot stays grounded: if both planted feet want to step the same tick, keep the
        // more-urgent (farther-drifted) request and defer the other. Without this both can lift at
        // once (e.g. spinning in place), which reads as floating/moonwalking. The per-foot trigger
        // already requires the other foot planted, so this only catches the same-tick double-want.
        if (left.phase == 0 && right.phase == 0 && left.wantsStep && right.wantsStep)
        {
            float ld = HDist(left.plantedPos, left.idealPos, up);
            float rd = HDist(right.plantedPos, right.idealPos, up);
            if (ld >= rd) right.wantsStep = false; else left.wantsStep = false;
        }

        // ── Knee hints ──
        float avgThigh = (p.leftThighLen + p.rightThighLen) * 0.5f;
        float3 hp = inp.hipsPos;

        float hipFootUpDist = math.abs(GetUpComponent(hp, up) - GetUpComponent((left.currentPos + right.currentPos) * 0.5f, up));
        float avgLeg2 = (p.leftLegLen + p.rightLegLen) * 0.5f;
        float bendRatio = 1f - math.saturate(hipFootUpDist / avgLeg2);

        float3 leftKneeTarget = (hp + left.currentPos) * 0.5f + bodyFwd * (avgThigh * p.kneeForwardPushFraction);
        float3 rightKneeTarget = (hp + right.currentPos) * 0.5f + bodyFwd * (avgThigh * p.kneeForwardPushFraction);

        float kneeSplay = halfStance * inp.splayWhenCrouched * bendRatio;
        leftKneeTarget -= rawRight * kneeSplay;
        rightKneeTarget += rawRight * kneeSplay;

        float leftKneeUp = GetUpComponent(leftKneeTarget, up);
        float3 hipsGround2 = ProjectOntoUpPlane(hp, up) + up * leftKneeUp;
        float kneeMinSide = halfStance * p.kneeMinSideFraction;
        EnforceSide(ref leftKneeTarget, hipsGround2, rawRight, -1, kneeMinSide);
        float rightKneeUp = GetUpComponent(rightKneeTarget, up);
        EnforceSide(ref rightKneeTarget, ProjectOntoUpPlane(hp, up) + up * rightKneeUp, rawRight, +1, kneeMinSide);

        float kneeAlpha = 1f - math.exp(-p.kneeHintLerpSpeed * dt);
        left.kneeHint = math.lerp(left.kneeHint, leftKneeTarget, kneeAlpha);
        right.kneeHint = math.lerp(right.kneeHint, rightKneeTarget, kneeAlpha);

        // ── Final side enforcement ──
        // Only on stepping feet (anti-cross while in flight). A planted foot is world-locked;
        // enforcing side on it hard-snaps it sideways when the body drifts (a visible pop), and
        // the plant->ideal step trigger already re-steps a plant that has drifted too far.
        float leftFootUp = GetUpComponent(left.currentPos, up);
        float3 hipsGround3 = ProjectOntoUpPlane(hp, up) + up * leftFootUp;
        float footMinSide = halfStance * p.footSideEnforceFraction;
        if (left.phase == 1) EnforceSide(ref left.currentPos, hipsGround3, rawRight, -1, footMinSide);
        if (right.phase == 1) EnforceSide(ref right.currentPos, hipsGround3, rawRight, +1, footMinSide);

        // ── Hip bob ──
        var outp = output[0];
        outp.hipBob = ComputeHipBob(ref left, ref right, speed);
        output[0] = outp;

        // ── Write back ──
        feet[0] = left;
        feet[1] = right;
        simState[0] = sim;
    }

    private void UpdateFoot(ref BasisFootNativeState f, ref BasisFootNativeState other,
        float3 rawFwd, float3 smoothedVelocity, float speed, float threshold, float stepDur, float dt, float3 up)
    {
        if (f.phase == 0) // Planted
        {
            float a = 1f - math.exp(-p.plantedLerpSpeed * dt);
            f.currentPos = math.lerp(f.currentPos, f.plantedPos, a);

            float ra = 1f - math.exp(-p.rotationLerpSpeed * dt);
            f.currentRot = math.slerp(f.currentRot, f.plantedRot, ra);

            float dist = HDist(f.plantedPos, f.idealPos, up);

            // Also check yaw: if body has turned significantly from planted rotation, step
            float3 plantedFwd = math.mul(f.plantedRot, new float3(0, 0, 1));
            float3 pff = plantedFwd - up * math.dot(plantedFwd, up);
            float3 bff = rawFwd - up * math.dot(rawFwd, up);
            bool yawTrigger = false;
            if (math.lengthsq(pff) > 0.001f && math.lengthsq(bff) > 0.001f)
            {
                float yawDiff = math.abs(SignedAngle(math.normalize(pff), math.normalize(bff), up));
                yawTrigger = yawDiff > p.maxPlantedYawDegrees;
            }

            if ((dist > threshold || yawTrigger) && other.phase == 0)
            {
                f.wantsStep = true;
                f.predictedTargetXZ = ComputeStepPrediction(ref f, rawFwd, smoothedVelocity, speed, stepDur, up);
            }
            else
            {
                f.wantsStep = false;
            }
        }
        else // Stepping
        {
            f.wantsStep = false;
            f.stepTimer += dt;
            float t = math.saturate(f.stepTimer / f.stepDur);

            // Smoothstep, not a cubic ease-OUT. The old curve (1-(1-t)^3) has its MAXIMUM slope at t=0, so the
            // foot was flung off the ground at ~3.7 m/s on the first frame of the swing and then decelerated into
            // the landing -- backwards. A real swing leg is slow at toe-off, fastest at mid-swing, and slow again
            // at heel-strike. Smoothstep's derivative 6t(1-t) is exactly that: zero at both ends, peak in the
            // middle. This is the single most visible "the feet snap" artifact.
            float ease = t * t * (3f - 2f * t);

            float3 pos = math.lerp(f.stepStartPos, f.stepTargetPos, ease);

            // Lift scales with stride length (a short shuffle barely lifts; a full stride lifts fully),
            // floored so a tiny step still clears the ground and capped at the calibrated step height.
            // Scales with the avatar automatically (avgLeg and stepHeightCalc both scale).
            float avgLeg = (p.leftLegLen + p.rightLegLen) * 0.5f;
            float stepDist = HDist(f.stepStartPos, f.stepTargetPos, up);
            float strideFrac = math.saturate(stepDist / math.max(1e-3f, avgLeg * p.stepHeightStrideRefFraction));
            float dynamicHeight = p.stepHeightCalc * math.lerp(p.stepHeightMinFraction, 1.0f, strideFrac);

            // Normalise the arc by its OWN peak, derived from the exponents. The hardcoded 0.234 was wrong for the
            // default exponents (the true peak of t^0.6*(1-t)^1.4 is 0.2947 at t=0.3), so the expression peaked at
            // 1.26 and saturate() clipped it -- the foot sat at exactly max height, dead flat, for ~42% of the
            // swing. That plateau is the "robotic" read. Worse, the constant did not track the exponents: retuning
            // either one silently clipped harder or never reached the commanded height.
            //
            //   peak of t^a * (1-t)^b  is at  t* = a/(a+b),  value  t*^a * (1-t*)^b
            float a = p.stepArcLiftExp, b = p.stepArcDropExp;
            float tPeak = a / math.max(1e-4f, a + b);
            float arcPeak = math.max(1e-4f, math.pow(tPeak, a) * math.pow(1f - tPeak, b));

            float lift = math.pow(t, a) * math.pow(1f - t, b) / arcPeak;
            pos += up * (math.saturate(lift) * dynamicHeight);
            f.currentPos = pos;

            quaternion liveRot = FootRotation(rawFwd, f.filteredNormal, up);
            f.currentRot = math.slerp(f.currentRot, liveRot, ease);

            if (t >= 1f)
            {
                f.phase = 0; // Planted
                f.plantedPos = f.stepTargetPos;
                f.plantedRot = FootRotation(rawFwd, f.filteredNormal, up);
                f.currentPos = f.stepTargetPos;
            }
        }
    }

    private float3 ComputeStepPrediction(ref BasisFootNativeState f, float3 bodyFwd, float3 smoothedVelocity, float speed, float stepDur, float3 up)
    {
        // Uses the caller's live sim state — simState[0] still holds last frame's values here
        // (Execute writes back only at its end).
        float avgLeg = (p.leftLegLen + p.rightLegLen) * 0.5f;
        float3 svFlat = smoothedVelocity - up * math.dot(smoothedVelocity, up);
        float3 moveDir = math.lengthsq(svFlat) > 0.01f
            ? math.normalize(svFlat)
            : bodyFwd;
        float predAmount = math.min(speed * stepDur * p.predictionFactor, avgLeg * p.maxPredictionFraction);
        return f.idealPos + moveDir * predAmount;
    }

    // Vertical pelvis motion over the gait cycle. The PHASE here was inverted, which is why the walk read wrong
    // even though the frequency was right.
    //
    // While one foot is swinging, the OTHER is in mid-stance -- and mid-stance is where a human's centre of mass
    // is at its HIGHEST: you vault over the straight stance leg like an inverted pendulum. The COM is at its
    // LOWEST at double support, where both legs are splayed and the hips sit between them. The old code returned
    // -sin(pi*t) at mid-swing, pushing the pelvis DOWN exactly where it should rise, and 0 at double support
    // exactly where it should be lowest. Right frequency, opposite sign.
    //
    // Speed gate is relative, not an absolute 0.05 m/s: a small avatar's whole speed range scales as sqrt(g*L),
    // so a fixed m/s cutoff silently disables the bob for anything short.
    private float ComputeHipBob(ref BasisFootNativeState left, ref BasisFootNativeState right, float speed)
    {
        if (speed < 0.02f * p.fastSpeedRef) return 0f;

        float maxBob = p.hipToFoot * p.hipBobFraction;
        float speedScale = math.saturate(speed / p.fastSpeedRef);
        float amplitude = maxBob * speedScale;

        float leftRise = 0f, rightRise = 0f;
        if (left.phase == 1)
        {
            float t = math.saturate(left.stepTimer / left.stepDur);
            leftRise = math.sin(t * math.PI);
        }
        if (right.phase == 1)
        {
            float t = math.saturate(right.stepTimer / right.stepDur);
            rightRise = math.sin(t * math.PI);
        }

        return math.max(leftRise, rightRise) * amplitude;
    }

    // ── Helpers ──

    /// <summary>Horizontal distance (in the plane perpendicular to up).</summary>
    private static float HDist(float3 a, float3 b, float3 up)
    {
        float3 diff = a - b;
        diff -= up * math.dot(diff, up); // remove vertical component
        return math.length(diff);
    }

    /// <summary>Get the component of a position along the up axis.</summary>
    private static float GetUpComponent(float3 pos, float3 up)
    {
        return math.dot(pos, up);
    }

    /// <summary>Set the component of a position along the up axis.</summary>
    private static void SetUpComponent(ref float3 pos, float value, float3 up)
    {
        float current = math.dot(pos, up);
        pos += up * (value - current);
    }

    /// <summary>Project a position onto the plane perpendicular to up (remove up component).</summary>
    private static float3 ProjectOntoUpPlane(float3 pos, float3 up)
    {
        return pos - up * math.dot(pos, up);
    }

    private static void EnforceSide(ref float3 pos, float3 center, float3 bodyRight, int sideSign, float minDist)
    {
        float3 toPos = pos - center;
        float lateral = math.dot(toPos, bodyRight);

        if (sideSign > 0 && lateral < sideSign * minDist)
            pos += bodyRight * (sideSign * minDist - lateral);
        else if (sideSign < 0 && lateral > -minDist)
            pos -= bodyRight * (lateral + minDist);
    }

    private static float3 ComputeBodyForward(BasisFootSimInput inp, BasisFootSimState sim, BasisFootSimParams p, float3 up)
    {
        float3 accumulated = float3.zero;
        float totalWeight = 0f;

        float3 hipsFwd = math.mul(inp.hipsRot, new float3(0, 0, 1));
        hipsFwd -= up * math.dot(hipsFwd, up);
        if (math.lengthsq(hipsFwd) > 0.001f)
        {
            accumulated += math.normalize(hipsFwd) * p.bodyFwdHipsWeight;
            totalWeight += p.bodyFwdHipsWeight;
        }

        if (inp.hasChest)
        {
            float3 chestFwd = math.mul(inp.chestRot, new float3(0, 0, 1));
            chestFwd -= up * math.dot(chestFwd, up);
            if (math.lengthsq(chestFwd) > 0.001f)
            {
                accumulated += math.normalize(chestFwd) * p.bodyFwdChestWeight;
                totalWeight += p.bodyFwdChestWeight;
            }
        }

        float3 headFwd = math.mul(inp.headRot, new float3(0, 0, 1));
        float3 headFlat = headFwd - up * math.dot(headFwd, up);
        if (math.lengthsq(headFlat) > 0.1f)
        {
            accumulated += math.normalize(headFlat) * p.bodyFwdHeadWeight;
            totalWeight += p.bodyFwdHeadWeight;
        }

        if (totalWeight > 0f)
        {
            accumulated /= totalWeight;
            if (math.lengthsq(accumulated) > 0.001f)
                return math.normalize(accumulated);
        }

        return inp.avatarForward;
    }

    private quaternion FootRotation(float3 bodyFwd, float3 normal, float3 up)
    {
        if (math.lengthsq(normal) < 0.001f) normal = up;

        float3 fwd = ProjectOnPlane(bodyFwd, normal);
        if (math.lengthsq(fwd) < 1e-6f)
            fwd = ProjectOnPlane(new float3(0, 0, 1), normal);
        fwd = math.normalize(fwd);

        quaternion surfaceRot = quaternion.LookRotation(fwd, normal);
        quaternion uprightRot = quaternion.LookRotation(fwd, up);
        float tiltAngle = AngleBetween(uprightRot, surfaceRot);
        quaternion result = tiltAngle > 0.01f
            ? math.slerp(uprightRot, surfaceRot, math.saturate(p.maxFootTiltDegrees / tiltAngle))
            : uprightRot;

        float3 footFwd = math.mul(result, new float3(0, 0, 1));
        float3 footFwdFlat = footFwd - up * math.dot(footFwd, up);
        float3 bodyFwdFlat = bodyFwd - up * math.dot(bodyFwd, up);

        if (math.lengthsq(footFwdFlat) > 1e-6f && math.lengthsq(bodyFwdFlat) > 1e-6f)
        {
            footFwdFlat = math.normalize(footFwdFlat);
            bodyFwdFlat = math.normalize(bodyFwdFlat);

            float yawAngle = SignedAngle(bodyFwdFlat, footFwdFlat, up);
            if (math.abs(yawAngle) > p.maxFootYawDegrees)
            {
                float correction = math.clamp(yawAngle, -p.maxFootYawDegrees, p.maxFootYawDegrees) - yawAngle;
                result = math.mul(quaternion.AxisAngle(up, math.radians(correction)), result);
            }
        }

        return result;
    }

    private static float3 ProjectOnPlane(float3 v, float3 normal)
    {
        return v - math.dot(v, normal) * normal;
    }

    private static float AngleBetween(quaternion a, quaternion b)
    {
        float dot = math.abs(math.dot(a.value, b.value));
        return math.degrees(2f * math.acos(math.min(dot, 1f)));
    }

    private static float SignedAngle(float3 from, float3 to, float3 axis)
    {
        float angle = math.degrees(math.acos(math.clamp(math.dot(from, to), -1f, 1f)));
        float sign = math.sign(math.dot(axis, math.cross(from, to)));
        return angle * sign;
    }

    private static float3 Slerp3(float3 a, float3 b, float t)
    {
        float la = math.length(a);
        float lb = math.length(b);
        if (la < 0.001f || lb < 0.001f) return math.lerp(a, b, t);

        float3 na = a / la;
        float3 nb = b / lb;
        float dot = math.clamp(math.dot(na, nb), -1f, 1f);
        float theta = math.acos(dot);

        if (theta < 0.001f) return math.lerp(a, b, t);

        float sinTheta = math.sin(theta);
        float3 dir = (na * math.sin((1f - t) * theta) + nb * math.sin(t * theta)) / sinTheta;
        return dir * math.lerp(la, lb, t);
    }
}
