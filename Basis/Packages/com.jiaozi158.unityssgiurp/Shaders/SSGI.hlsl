#ifndef URP_SCREEN_SPACE_GLOBAL_ILLUMINATION_HLSL
#define URP_SCREEN_SPACE_GLOBAL_ILLUMINATION_HLSL

#include "./SSGIUtilities.hlsl"
#include "./SSGIDenoise.hlsl"

// Same mapping as ComputeNormalizedDeviceCoordinatesWithZ, for a position that is already in clip space.
float3 SSGIClipToNormalizedDeviceCoordinates(float4 positionCS)
{
#if UNITY_UV_STARTS_AT_TOP
    positionCS.y = -positionCS.y;
#endif
    positionCS *= rcp(positionCS.w);
    positionCS.xy = positionCS.xy * 0.5 + 0.5;
    return positionCS.xyz;
}

// If no intersection, "rayHit.distance" will remain "REAL_EPS".
RayHit RayMarching(Ray ray, float2 screenUV, half dither, half3 viewDirectionWS)
{
    RayHit rayHit = InitializeRayHit();

    // True:  The ray points to the scene objects.
    // False: The ray points to the camera plane.
    bool isFrontRay = (dot(ray.direction, viewDirectionWS) <= 0.0) ? true : false;

    // Store a frequently used material property
    half stepSize = STEP_SIZE;

    // Initialize small step ray marching settings
    half thickness = MARCHING_THICKNESS_SMALL_STEP;
    half currStepSize = SMALL_STEP_SIZE;

    // Minimum thickness of scene objects without backface depth
    half marchingThickness = MARCHING_THICKNESS;

    // The projection is linear in homogeneous coordinates: project the ray once, then every step is a multiply-add.
    // For a perspective projection the clip space w of a ray position is its linear eye depth.
    float4x4 worldToHClip = GetWorldToHClipMatrix();
    float4 rayOriginCS = mul(worldToHClip, float4(ray.position, 1.0));
    float4 rayDirectionCS = mul(worldToHClip, float4(ray.direction, 0.0));
    bool isPerspective = IsPerspectiveProjection();

    // Linearising a depth branches on the projection type. It is the same branch on every step of every ray, so the
    // two coefficients it selects between are resolved once here and the loop is left with a multiply-add and a
    // reciprocal (perspective) or a plain lerp (orthographic).
    SSGIDepthLinearizer linearizer = SSGIGetDepthLinearizer(isPerspective);

    // Distance travelled along the ray (also the hit distance).
    float rayDistance = 0.0;

    // Screen space tracing cannot resolve geometry finer than the pixel the depth came from, so a hit inside the
    // shading point's own pixel footprint is that same surface again rather than something lighting it. Thin, dense
    // geometry (foliage cards, hair, grass) hits this on the very first step and brings back its own colour, which
    // the colour history then feeds in again every frame until the surface blows out.
    half minHitDistance = _PixelSpreadAngleTangent * length(ray.position - GetCameraPositionWS()) * SELF_INTERSECTION_PIXELS;

    bool startBinarySearch = false;

    bool isBackBuffer = false;

    half3 nearHitRadiance = half3(0.0, 0.0, 0.0);

    UNITY_LOOP
    for (int i = 1; i <= MAX_STEP; i++)
    {
        // Adaptive Ray Marching
        // Near: Use smaller step size to improve accuracy.
        // Far:  Use larger step size to fill the scene.
        if (i > MAX_SMALL_STEP && i <= MAX_MEDIUM_STEP)
        {
            currStepSize = (startBinarySearch) ? currStepSize : MEDIUM_STEP_SIZE;
            thickness = (startBinarySearch) ? thickness : MARCHING_THICKNESS_MEDIUM_STEP;
            marchingThickness = MARCHING_THICKNESS;
        }
        else if (i > MAX_MEDIUM_STEP)
        {
            // [Far] Use a small step size only when objects are close to the camera.
            currStepSize = (startBinarySearch) ? currStepSize : stepSize;
            thickness = (startBinarySearch) ? thickness : MARCHING_THICKNESS;
            marchingThickness = MARCHING_THICKNESS;
        }

        // Update current ray position.
        rayDistance += currStepSize + currStepSize * dither;

        float4 rayPositionCS = rayOriginCS + rayDirectionCS * rayDistance;
        float3 rayPositionNDC = SSGIClipToNormalizedDeviceCoordinates(rayPositionCS);

    #if (UNITY_REVERSED_Z == 0) // OpenGL platforms
        rayPositionNDC.z = rayPositionNDC.z * 0.5 + 0.5; // -1..1 to 0..1
    #endif

        // Stop marching the ray when outside screen space.
        bool isScreenSpace = rayPositionNDC.x > 0.0 && rayPositionNDC.y > 0.0 && rayPositionNDC.x < 1.0 && rayPositionNDC.y < 1.0 ? true : false;
        if (!isScreenSpace)
            break;

        // Sample opaque front depth
        float deviceDepth = SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, my_point_clamp_sampler, rayPositionNDC.xy, 0).r;

        // Convert Z-Depth to Linear Eye Depth
        // Value Range: Camera Near Plane -> Camera Far Plane
        float sceneDepth = SSGILinearEyeDepth(linearizer, deviceDepth);
        float hitDepth = isPerspective ? rayPositionCS.w : SSGILinearEyeDepth(linearizer, rayPositionNDC.z);

        // Calculate (front) depth difference
        // Positive: ray is in front of the front-faces of object.
        // Negative: ray is behind the front-faces of object.
        float depthDiff = sceneDepth - hitDepth;

        // Initialize variables
        float deviceBackDepth = 0.0; // z buffer (back) depth
        float sceneBackDepth = 0.0;

        // Calculate (back) depth difference
        // Positive: ray is in front of the back-faces of object.
        // Negative: ray is behind the back-faces of object.
        float backDepthDiff = 0.0;

        // Avoid infinite thickness for objects with no thickness (ex. Plane).
        // 1. Back-face depth value is not from sky
        // 2. Back-faces should be behind front-faces.
        bool backDepthValid = false;
    #if defined(_BACKFACE_TEXTURES)
        deviceBackDepth = SAMPLE_TEXTURE2D_X_LOD(_CameraBackDepthTexture, my_point_clamp_sampler, rayPositionNDC.xy, 0).r;
        sceneBackDepth = SSGILinearEyeDepth(linearizer, deviceBackDepth);

        backDepthValid = (deviceBackDepth != UNITY_RAW_FAR_CLIP_VALUE) && (sceneBackDepth >= sceneDepth);
        backDepthDiff = backDepthValid ? (hitDepth - sceneBackDepth) : (depthDiff - marchingThickness);
    #endif

        // Binary Search Sign is used to flip the ray marching direction.
        // Sign is positive : ray is in front of the actual intersection.
        // Sign is negative : ray is behind the actual intersection.
        bool isBackSearch = (!isFrontRay && hitDepth > sceneBackDepth && backDepthValid);
        half Sign = isBackSearch ? FastSign(backDepthDiff) : FastSign(depthDiff);

        // Disable binary search:
        // 1. The ray points to the camera plane, but is in front of all objects.
        // 2. The ray leaves the camera plane, but is behind all objects.
        bool cannotBinarySearch = !startBinarySearch && (isFrontRay ? hitDepth > sceneBackDepth : hitDepth < sceneDepth);

        // Start binary search when the ray is behind the actual intersection.
        startBinarySearch = !cannotBinarySearch && (startBinarySearch || (Sign == -1)) ? true : false;

        // Half the step size each time when binary search starts.
        // If the ray passes through the intersection, we flip the sign of step size.
        if (startBinarySearch)
        {
            currStepSize *= (FastSign(currStepSize) == Sign) ? 0.5 : -0.5;
        }

        // Do not reflect sky, use reflection probe fallback.
        bool isSky = abs(deviceDepth - UNITY_RAW_FAR_CLIP_VALUE) < RAW_FAR_CLIP_THRESHOLD;

        // [No minimum step limit] The current implementation focuses on performance, so the ray will stop marching once it hits something.
        // Rules of ray hit:
        // 1. Ray is behind the front-faces of object. (sceneDepth <= hitDepth)
        // 2. Ray is in front of back-faces of object. (sceneBackDepth >= hitDepth) or (sceneDepth + marchingThickness >= hitDepth)
        // 3. Ray does not hit sky. (!isSky)
        bool hitSuccessful;

        // Ignore the incorrect "backDepthDiff" when objects (ex. Plane with front face only) has no thickness and blocks the backface depth rendering of objects behind it.
    #if defined(_BACKFACE_TEXTURES)
        if (backDepthValid)
        {
            // It's difficult to find the intersection of thin objects in several steps with large step sizes, so we add a minimum thickness to all objects to make it visually better.
            sceneBackDepth = max(sceneBackDepth, sceneDepth + thickness);
            hitSuccessful = ((depthDiff <= 0.0) && (hitDepth <= sceneBackDepth) && !isSky) ? true : false;
        }
        else
    #endif
        {
            hitSuccessful = ((depthDiff <= 0.0) && (depthDiff >= -marchingThickness) && !isSky) ? true : false;
        }

        // A hit inside the shading point's own pixel footprint cannot deliver reflected light: the colour history
        // there is this surface's own accumulated colour, and taking it feeds back and compounds every frame. Its
        // self-illumination is safe though, and it is the only way a surface right beside an emissive panel can catch
        // the glow, because this guard measures WORLD distance and an adjacent surface sits well inside it.
        bool nearHit = rayDistance <= minHitDistance;
        half nearHitWeight = 0.0;

        UNITY_BRANCH
        if (hitSuccessful && nearHit)
        {
            nearHitRadiance = SSGINearHitRadiance(rayPositionNDC.xy, nearHitWeight);
            hitSuccessful = nearHitWeight > 0.0;
        }

        // If we find the intersection.
        if (hitSuccessful)
        {
            rayHit.position = ray.position + ray.direction * rayDistance;
            rayHit.distance = rayDistance;

            UNITY_BRANCH
            if (nearHit)
                rayHit.emission = nearHitRadiance;
            else if (_BackDepthEnabled == 2.0 && isBackBuffer)
                rayHit.emission = SAMPLE_TEXTURE2D_X_LOD(_CameraBackOpaqueTexture, my_point_clamp_sampler, rayPositionNDC.xy, 0).rgb;
            else
                rayHit.emission = SSGIHitRadiance(SAMPLE_TEXTURE2D_X_LOD(_SSGIHistoryCameraColorTexture, my_point_clamp_sampler, rayPositionNDC.xy, 0).rgb, rayPositionNDC.xy);

            break;
        }
        // [Optimization] Exponentially increase the stepSize when the ray hasn't passed through the intersection.
        // From https://blog.voxagon.se/2018/01/03/screen-space-path-tracing-diffuse.html
        else if (!startBinarySearch)
        {
            // As the distance increases, the accuracy of ray intersection test becomes less important.
            currStepSize += currStepSize * 0.1;
            marchingThickness += _Thickness_Increment;
        }

        isBackBuffer = backDepthValid && _BackDepthEnabled == 2.0 ? backDepthDiff > 0.0 : false;
    }
    return rayHit;
}
#endif
