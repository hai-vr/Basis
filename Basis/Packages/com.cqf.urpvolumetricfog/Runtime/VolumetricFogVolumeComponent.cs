using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// The resolution at which the volumetric fog is rendered, relative to the camera resolution.
/// </summary>
public enum VolumetricFogResolution
{
	Half = 2,
	Quarter = 4
}

/// <summary>
/// A volume parameter that holds a VolumetricFogResolution value.
/// </summary>
[Serializable]
public sealed class VolumetricFogResolutionParameter : VolumeParameter<VolumetricFogResolution>
{
	/// <summary>
	/// Creates a new VolumetricFogResolutionParameter instance.
	/// </summary>
	/// <param name="value"></param>
	/// <param name="overrideState"></param>
	public VolumetricFogResolutionParameter(VolumetricFogResolution value, bool overrideState = false) : base(value, overrideState)
	{
	}
}

/// <summary>
/// How the adaptive probe volume (APV) lighting is sampled by the fog.
/// </summary>
public enum VolumetricFogAPVMode
{
	/// <summary>Sample Unity's live APV once per raymarch step. Dynamic, but the most expensive option.</summary>
	Live = 0,
	/// <summary>Sample a pre-baked world-space 3D texture of APV in-scatter. Static, but a single trilinear tap per step. Requires a bake.</summary>
	Baked = 1
}

/// <summary>
/// A volume parameter that holds a VolumetricFogAPVMode value.
/// </summary>
[Serializable]
public sealed class VolumetricFogAPVModeParameter : VolumeParameter<VolumetricFogAPVMode>
{
	/// <summary>
	/// Creates a new VolumetricFogAPVModeParameter instance.
	/// </summary>
	/// <param name="value"></param>
	/// <param name="overrideState"></param>
	public VolumetricFogAPVModeParameter(VolumetricFogAPVMode value, bool overrideState = false) : base(value, overrideState)
	{
	}
}

/// <summary>
/// Volume component for the volumetric fog.
/// </summary>
#if UNITY_2023_1_OR_NEWER
[VolumeComponentMenu("Custom/Volumetric Fog")]
#if UNITY_6000_0_OR_NEWER
[VolumeRequiresRendererFeatures(typeof(VolumetricFogRendererFeature))]
#endif
[SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
#else
[VolumeComponentMenuForRenderPipeline("Custom/Volumetric Fog", typeof(UniversalRenderPipeline))]
#endif
public sealed class VolumetricFogVolumeComponent : VolumeComponent, IPostProcessComponent
{
	#region Public Attributes

	[Header("Distances")]
	[Tooltip("The maximum distance from the camera that the fog will be rendered up to.")]
	public ClampedFloatParameter distance = new ClampedFloatParameter(64.0f, 0.0f, 512.0f);
	[Tooltip("The world height at which the fog will have the density specified in the volume.")]
	public FloatParameter baseHeight = new FloatParameter(0.0f, true);
	[Tooltip("The world height at which the fog will have no density at all.")]
	public FloatParameter maximumHeight = new FloatParameter(50.0f, true);

	[Header("Ground")]
	[Tooltip("When enabled, allows to define a world height. Below it, fog will have no density at all.")]
	public BoolParameter enableGround = new BoolParameter(false, BoolParameter.DisplayType.Checkbox, true);
	[Tooltip("Below this world height, fog will have no density at all.")]
	public FloatParameter groundHeight = new FloatParameter(0.0f);

	[Header("Lighting")]
	[Tooltip("How dense is the fog.")]
	public ClampedFloatParameter density = new ClampedFloatParameter(0.2f, 0.0f, 1.0f);
	[Tooltip("Value that defines how much the fog attenuates light as distance increases. Lesser values lead to a darker image.")]
	public MinFloatParameter attenuationDistance = new MinFloatParameter(128.0f, 0.05f);
#if UNITY_2023_1_OR_NEWER
	[Tooltip("When enabled, adaptive probe volumes (APV) will be sampled to contribute to fog.")]
	public BoolParameter enableAPVContribution = new BoolParameter(false, BoolParameter.DisplayType.Checkbox, true);
	[Tooltip("A weight factor for the light coming from adaptive probe volumes (APV) when the probe volume contribution is enabled.")]
	public ClampedFloatParameter APVContributionWeight = new ClampedFloatParameter(1.0f, 0.0f, 1.0f);
	[Tooltip("How APV lighting is sampled. Live evaluates Unity's APV every raymarch step (dynamic). Baked samples a pre-computed world-space 3D texture of APV in-scatter (static, much faster - needs a bake, and only engages once a bake exists).")]
	public VolumetricFogAPVModeParameter apvMode = new VolumetricFogAPVModeParameter(VolumetricFogAPVMode.Live, true);
#endif

	[Header("Main Light")]
	[Tooltip("Disabling this will avoid computing the main light contribution to fog, which in most cases will lead to better performance.")]
	public BoolParameter enableMainLightContribution = new BoolParameter(false, BoolParameter.DisplayType.Checkbox, true);
	[Tooltip("Higher positive values will make the fog affected by the main light to appear brighter when directly looking to it, while lower negative values will make the fog to appear brighter when looking away from it. The closer the value is closer to 1 or -1, the less the brightness will spread. Most times, positive values higher than 0 and lower than 1 should be used.")]
	public ClampedFloatParameter anisotropy = new ClampedFloatParameter(0.4f, -1.0f, 1.0f);
	[Tooltip("Higher values will make fog affected by the main light to appear brighter.")]
	public ClampedFloatParameter scattering = new ClampedFloatParameter(0.15f, 0.0f, 1.0f);
	[Tooltip("A multiplier color to tint the main light fog.")]
	public ColorParameter tint = new ColorParameter(Color.white, true, false, true);

	[Header("LTCGI")]
	[Tooltip("When enabled, LTCGI area lights (screens, video) contribute to fog. Requires the LTCGI package installed and a baked LTCGI controller in the scene; otherwise this has no effect.")]
	public BoolParameter enableLTCGIContribution = new BoolParameter(false, BoolParameter.DisplayType.Checkbox, true);
	[Tooltip("Higher values will make fog affected by LTCGI screens appear brighter.")]
	public ClampedFloatParameter LTCGIScattering = new ClampedFloatParameter(1.0f, 0.0f, 16.0f);

	[Header("Performance & Quality")]
	[Tooltip("The resolution at which the fog is rendered, relative to the camera. Quarter is much cheaper than Half but softer and leans harder on the upsample.")]
	public VolumetricFogResolutionParameter resolution = new VolumetricFogResolutionParameter(VolumetricFogResolution.Half, true);
	[Tooltip("Raymarching steps. Greater values will increase the fog quality at the expense of performance.")]
	public ClampedIntParameter maxSteps = new ClampedIntParameter(128, 8, 256);
	[Tooltip("The number of times that the fog texture will be blurred. Higher values lead to softer volumetric god rays at the cost of some performance. 0 disables the blur entirely, which is usually fine when the main light contribution is off.")]
	public ClampedIntParameter blurIterations = new ClampedIntParameter(1, 0, 4);
	[Tooltip("Disabling this will completely remove any feature from the volumetric fog from being rendered at all.")]
	public BoolParameter enabled = new BoolParameter(false, BoolParameter.DisplayType.Checkbox, true);

	[Header("Render Pass Event")]
	[Tooltip("The URP render pass event to render the volumetric fog.")]
	public VolumetricFogRenderPassEventParameter renderPassEvent = new VolumetricFogRenderPassEventParameter(VolumetricFogRenderPass.DefaultVolumetricFogRenderPassEvent);

	#endregion

	#region Initialization Methods

	public VolumetricFogVolumeComponent() : base()
	{
		displayName = "Volumetric Fog";
	}

	#endregion

	#region Volume Component Methods

	private void OnValidate()
	{
		maximumHeight.overrideState = baseHeight.overrideState;
		maximumHeight.value = Mathf.Max(baseHeight.value, maximumHeight.value);
		baseHeight.value = Mathf.Min(baseHeight.value, maximumHeight.value);
	}

	#endregion

	#region IPostProcessComponent Methods

#if !UNITY_2023_1_OR_NEWER

	/// <summary>
	/// <inheritdoc/>
	/// </summary>
	/// <returns></returns>
	public bool IsTileCompatible()
	{
		return true;
	}

#endif

	/// <summary>
	/// <inheritdoc/>
	/// </summary>
	/// <returns></returns>
	public bool IsActive()
	{
		return enabled.value && distance.value > 0.0f && groundHeight.value < maximumHeight.value && density.value > 0.0f;
	}

	#endregion
}