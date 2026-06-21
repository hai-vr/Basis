using UnityEditor;
using UnityEditor.Rendering;

/// <summary>
/// Custom editor for the volumetric fog volume component.
/// </summary>
[CustomEditor(typeof(VolumetricFogVolumeComponent))]
public sealed class VolumetricFogVolumeComponentEditor : VolumeComponentEditor
{
	#region Private Attributes

	private SerializedDataParameter distance;
	private SerializedDataParameter baseHeight;
	private SerializedDataParameter maximumHeight;

	private SerializedDataParameter enableGround;
	private SerializedDataParameter groundHeight;

	private SerializedDataParameter density;
	private SerializedDataParameter attenuationDistance;
#if UNITY_2023_1_OR_NEWER
	private SerializedDataParameter enableAPVContribution;
	private SerializedDataParameter APVContributionWeight;
#endif

	private SerializedDataParameter enableMainLightContribution;
	private SerializedDataParameter anisotropy;
	private SerializedDataParameter scattering;
	private SerializedDataParameter tint;

	private SerializedDataParameter enableAdditionalLightsContribution;

	private SerializedDataParameter enableLTCGIContribution;
	private SerializedDataParameter LTCGIScattering;

	private SerializedDataParameter enableVRSLContribution;
	private SerializedDataParameter VRSLScattering;
	private SerializedDataParameter VRSLAnisotropy;
	private SerializedDataParameter VRSLSourceDistance;
	private SerializedDataParameter VRSLSpotConeSharpness;
	private SerializedDataParameter VRSLPointConeSharpness;
	private SerializedDataParameter VRSLPointBeamAxis;

	private SerializedDataParameter maxSteps;
	private SerializedDataParameter blurIterations;
	private SerializedDataParameter enabled;
	
	private SerializedDataParameter renderPassEvent;

	#endregion

	#region VolumeComponentEditor Methods

	/// <summary>
	/// <inheritdoc/>
	/// </summary>
	public override void OnEnable()
	{
		PropertyFetcher<VolumetricFogVolumeComponent> pf = new PropertyFetcher<VolumetricFogVolumeComponent>(serializedObject);

		distance = Unpack(pf.Find(x => x.distance));
		baseHeight = Unpack(pf.Find(x => x.baseHeight));
		maximumHeight = Unpack(pf.Find(x => x.maximumHeight));

		enableGround = Unpack(pf.Find(x => x.enableGround));
		groundHeight = Unpack(pf.Find(x => x.groundHeight));

		density = Unpack(pf.Find(x => x.density));
		attenuationDistance = Unpack(pf.Find(x => x.attenuationDistance));
#if UNITY_2023_1_OR_NEWER
		enableAPVContribution = Unpack(pf.Find(x => x.enableAPVContribution));
		APVContributionWeight = Unpack(pf.Find(x => x.APVContributionWeight));
#endif

		enableMainLightContribution = Unpack(pf.Find(x => x.enableMainLightContribution));
		anisotropy = Unpack(pf.Find(x => x.anisotropy));
		scattering = Unpack(pf.Find(x => x.scattering));
		tint = Unpack(pf.Find(x => x.tint));

		enableAdditionalLightsContribution = Unpack(pf.Find(x => x.enableAdditionalLightsContribution));

		enableLTCGIContribution = Unpack(pf.Find(x => x.enableLTCGIContribution));
		LTCGIScattering = Unpack(pf.Find(x => x.LTCGIScattering));

		enableVRSLContribution = Unpack(pf.Find(x => x.enableVRSLContribution));
		VRSLScattering = Unpack(pf.Find(x => x.VRSLScattering));
		VRSLAnisotropy = Unpack(pf.Find(x => x.VRSLAnisotropy));
		VRSLSourceDistance = Unpack(pf.Find(x => x.VRSLSourceDistance));
		VRSLSpotConeSharpness = Unpack(pf.Find(x => x.VRSLSpotConeSharpness));
		VRSLPointConeSharpness = Unpack(pf.Find(x => x.VRSLPointConeSharpness));
		VRSLPointBeamAxis = Unpack(pf.Find(x => x.VRSLPointBeamAxis));

		maxSteps = Unpack(pf.Find(x => x.maxSteps));
		blurIterations = Unpack(pf.Find(x => x.blurIterations));
		enabled = Unpack(pf.Find(x => x.enabled));
		
		renderPassEvent = Unpack(pf.Find(x => x.renderPassEvent));
	}

	/// <summary>
	/// <inheritdoc/>
	/// </summary>
	public override void OnInspectorGUI()
	{
		bool isEnabled = enabled.overrideState.boolValue && enabled.value.boolValue;

		if (!isEnabled)
		{
			PropertyField(enabled);
			return;
		}

		bool enabledGround = enableGround.overrideState.boolValue && enableGround.value.boolValue;
		bool enabledMainLightContribution = enableMainLightContribution.overrideState.boolValue && enableMainLightContribution.value.boolValue;
		bool enabledAdditionalLightsContribution = enableAdditionalLightsContribution.overrideState.boolValue && enableAdditionalLightsContribution.value.boolValue;

		PropertyField(distance);
		PropertyField(baseHeight);
		PropertyField(maximumHeight);

		PropertyField(enableGround);
		if (enabledGround)
			PropertyField(groundHeight);

		PropertyField(density);
		PropertyField(attenuationDistance);
#if UNITY_2023_1_OR_NEWER
		bool enabledAPVContribution = enableAPVContribution.overrideState.boolValue && enableAPVContribution.value.boolValue;
		PropertyField(enableAPVContribution);
		if (enabledAPVContribution)
			PropertyField(APVContributionWeight);
#endif

		PropertyField(enableMainLightContribution);
		if (enabledMainLightContribution)
		{
			PropertyField(anisotropy);
			PropertyField(scattering);
			PropertyField(tint);
		}

		PropertyField(enableAdditionalLightsContribution);

		bool enabledLTCGIContribution = enableLTCGIContribution.overrideState.boolValue && enableLTCGIContribution.value.boolValue;
		PropertyField(enableLTCGIContribution);
		if (enabledLTCGIContribution)
			PropertyField(LTCGIScattering);

		bool enabledVRSLContribution = enableVRSLContribution.overrideState.boolValue && enableVRSLContribution.value.boolValue;
		PropertyField(enableVRSLContribution);
		if (enabledVRSLContribution)
		{
			PropertyField(VRSLScattering);
			PropertyField(VRSLAnisotropy);
			PropertyField(VRSLSourceDistance);
			PropertyField(VRSLSpotConeSharpness);
			PropertyField(VRSLPointConeSharpness);
			PropertyField(VRSLPointBeamAxis);
		}

		PropertyField(maxSteps);
		PropertyField(blurIterations);
		PropertyField(enabled);
		
		PropertyField(renderPassEvent);
	}

	#endregion
}