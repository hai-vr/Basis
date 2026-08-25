#ifndef URP_SCREEN_SPACE_GLOBAL_ILLUMINATION_CONFIG_HLSL
#define URP_SCREEN_SPACE_GLOBAL_ILLUMINATION_CONFIG_HLSL

#define MAX_STEP              _MaxSteps
#define MAX_SMALL_STEP        _MaxSmallSteps
#define MAX_MEDIUM_STEP       _MaxMediumSteps

#define STEP_SIZE             _StepSize
#define SMALL_STEP_SIZE		  _SmallStepSize
#define MEDIUM_STEP_SIZE	  _MediumStepSize

// Minimum thickness of scene objects (in meters)
#define MARCHING_THICKNESS				_Thickness
#define MARCHING_THICKNESS_SMALL_STEP   0.01
#define MARCHING_THICKNESS_MEDIUM_STEP  0.1

#define RAY_COUNT             _RayCount

// A ray hit within this many pixel footprints of its origin is the origin surface itself, not a light source.
#define SELF_INTERSECTION_PIXELS	2.0

// How far the depth may bend between adjacent pixels, relative to the eye depth, before a normal reconstructed
// from it is treated as noise rather than as a surface. A plane keeps this near zero at any inclination.
#define SSGI_NORMAL_MAX_CURVATURE	0.05

// Luminance at which a hit inside the ray's own pixel footprint counts fully as a light source. Below it the
// contribution ramps down to nothing, so the decision is never a per-frame switch.
#define SSGI_NEAR_HIT_EMISSIVE_LUMINANCE	0.25

// Temporal Accumulation maximum history samples
#define MAX_ACCUM_FRAME_NUM			8

// Temporal re-projection rejection threshold
#define MAX_REPROJECTION_DISTANCE	0.1
#define MAX_PIXEL_TOLERANCE			4
#define PROJECTION_EPSILON			0.000001

#define CLAMP_MAX       65472.0 // HALF_MAX minus one (2 - 2^-9) * 2^15

// It seems that some developers use shader graph to create the skybox, but cannot disable depth write due to Unity (shader graph) issue
// For better compatibility with different skybox shaders, we add a depth comparision threshold
#define RAW_FAR_CLIP_THRESHOLD 1e-7

#endif