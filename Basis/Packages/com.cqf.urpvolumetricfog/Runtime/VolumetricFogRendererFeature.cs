using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// The volumetric fog renderer feature.
/// </summary>
[Tooltip("Adds support to render volumetric fog.")]
[DisallowMultipleRendererFeature("Volumetric Fog")]
public sealed class VolumetricFogRendererFeature : ScriptableRendererFeature
{
	#region Private Attributes

	[HideInInspector]
	[SerializeField] private Shader downsampleDepthShader;
	[HideInInspector]
	[SerializeField] private Shader volumetricFogShader;

	[Tooltip("Optional blue-noise texture used for raymarch jitter. When assigned it replaces interleaved gradient noise, giving less visible banding at the same step count. A small (e.g. 64x64 or 128x128) single-channel blue-noise texture works well. Leave empty to use the built-in fallback.")]
	[SerializeField] private Texture2D blueNoiseTexture;

	[Tooltip("How many raymarch steps between adaptive probe volume (APV) evaluations. 1 = sample every step (highest quality, slowest); higher trades the along-depth APV gradient for fewer APV texture fetches. APV is smooth, so 8 is usually invisible.")]
	[Range(1, 32)]
	[SerializeField] private int apvSampleStride = 8;

	private Material downsampleDepthMaterial;
	private Material volumetricFogMaterial;

	private VolumetricFogRenderPass volumetricFogRenderPass;

	#endregion

	#region Scriptable Renderer Feature Methods

	/// <summary>
	/// <inheritdoc/>
	/// </summary>
	public override void Create()
	{
		ValidateResourcesForVolumetricFogRenderPass(true);

		volumetricFogRenderPass = new VolumetricFogRenderPass(downsampleDepthMaterial, volumetricFogMaterial, VolumetricFogRenderPass.DefaultRenderPassEvent);
	}

	/// <summary>
	/// <inheritdoc/>
	/// </summary>
	/// <param name="renderer"></param>
	/// <param name="renderingData"></param>
	public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
	{
		bool isPostProcessEnabled = renderingData.postProcessingEnabled && renderingData.cameraData.postProcessEnabled;
		bool shouldAddVolumetricFogRenderPass = isPostProcessEnabled && ShouldAddVolumetricFogRenderPass(renderingData.cameraData.cameraType);
		
		if (shouldAddVolumetricFogRenderPass)
		{
			volumetricFogRenderPass.blueNoiseTexture = blueNoiseTexture;
			volumetricFogRenderPass.apvStride = apvSampleStride;
			volumetricFogRenderPass.renderPassEvent = GetRenderPassEvent();
			volumetricFogRenderPass.ConfigureInput(ScriptableRenderPassInput.Depth);
			renderer.EnqueuePass(volumetricFogRenderPass);
		}
	}

	/// <summary>
	/// <inheritdoc/>
	/// </summary>
	/// <param name="disposing"></param>
	protected override void Dispose(bool disposing)
	{
		base.Dispose(disposing);

		volumetricFogRenderPass?.Dispose();

		CoreUtils.Destroy(downsampleDepthMaterial);
		CoreUtils.Destroy(volumetricFogMaterial);
	}

	#endregion

	#region Methods

	/// <summary>
	/// Validates the resources used by the volumetric fog render pass.
	/// </summary>
	/// <param name="forceRefresh"></param>
	/// <returns></returns>
	private bool ValidateResourcesForVolumetricFogRenderPass(bool forceRefresh)
	{
		if (forceRefresh)
		{
#if UNITY_EDITOR
			downsampleDepthShader = Shader.Find("Hidden/DownsampleDepth");
			volumetricFogShader = Shader.Find("Hidden/VolumetricFog");

			if (blueNoiseTexture == null)
			{
				string[] blueNoiseGuids = UnityEditor.AssetDatabase.FindAssets("VolumetricFogBlueNoise t:Texture2D");
				if (blueNoiseGuids.Length > 0)
					blueNoiseTexture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(UnityEditor.AssetDatabase.GUIDToAssetPath(blueNoiseGuids[0]));
			}
#endif
			CoreUtils.Destroy(downsampleDepthMaterial);
			downsampleDepthMaterial = CoreUtils.CreateEngineMaterial(downsampleDepthShader);

			CoreUtils.Destroy(volumetricFogMaterial);
			volumetricFogMaterial = CoreUtils.CreateEngineMaterial(volumetricFogShader);
		}

		bool okDepth = downsampleDepthShader != null && downsampleDepthMaterial != null;
		bool okVolumetric = volumetricFogShader != null && volumetricFogMaterial != null;
		
		return okDepth && okVolumetric;
	}

	/// <summary>
	/// Gets whether the volumetric fog render pass should be enqueued to the renderer.
	/// </summary>
	/// <param name="cameraType"></param>
	/// <returns></returns>
	private bool ShouldAddVolumetricFogRenderPass(CameraType cameraType)
	{
		VolumetricFogVolumeComponent fogVolume = VolumeManager.instance.stack.GetComponent<VolumetricFogVolumeComponent>();

		bool isVolumeOk = fogVolume != null && fogVolume.IsActive();
		bool isCameraOk = cameraType != CameraType.Preview && cameraType != CameraType.Reflection;
		bool areResourcesOk = ValidateResourcesForVolumetricFogRenderPass(false);

		return isActive && isVolumeOk && isCameraOk && areResourcesOk;
	}

	/// <summary>
	/// Returns the render pass event for the volumetric fog.
	/// </summary>
	/// <returns></returns>
	private RenderPassEvent GetRenderPassEvent()
	{
		VolumetricFogVolumeComponent fogVolume = VolumeManager.instance.stack.GetComponent<VolumetricFogVolumeComponent>();
		
		return (RenderPassEvent)fogVolume.renderPassEvent.value;
	}

	#endregion
}