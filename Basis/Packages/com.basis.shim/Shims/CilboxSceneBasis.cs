using UnityEngine;
using System.Collections.Generic;
using System;
using System.Collections.Specialized;
using System.Collections;
using System.Runtime.InteropServices;
using System.Reflection;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Networking.NetworkedAvatar;
using Cilbox;

namespace Cilbox
{
	[CilboxTarget]
	public class CilboxSceneBasis : Cilbox
	{
		static HashSet<String> whiteListType = new HashSet<String>(){
			// Text Mesh Pro types
			"TMPro.*",

			// Basis types
			"Basis.Scripts.BasisSdk.Interactions.BasisPickUpUseMode",
            "Basis.Scripts.Device_Management.Devices.BasisInput", // Restrictive, only used as a type.
			"Basis.Scripts.BasisSdk.Interactions.BasisPickupInteractable", // Restrictive (See below), only access field.
			"Basis.Scripts.BasisSdk.Interactions.BasisInteractableObject", // Restrictive (See below), only access field.
			"Basis.Scripts.BasisSdk.Interactions.BasisInteractableButton",
			"Basis.Scripts.BasisSdk.Interactions.BasisInteractableButton+ClickEvent",
			"Basis.BasisNetworkBehaviour",
            "Basis.BasisImageDownloader",
			"Basis.BasisNetworkBehaviour",
			"Basis.Network.Core.DeliveryMethod",
			"Basis.SafeUtil",
			"Basis.Scripts.BasisSdk.Players.BasisLocalPlayer",
			"Basis.Scripts.Networking.NetworkedAvatar.BasisNetworkPlayer",
			"Basis.BasisUrl",
			"Basis.IBasisImageDownload",
			"BasisNetworkCommon+EventTiming",
			"BasisSDKMirror",
			"BasisSDKMirror+MirrorClearFlags",
			"Basis.Shims.*",

			// Cilbox types
			"Cilbox.CilboxPublicUtils",

			// System types - primitives and core data
			"System.Action",
			"System.Action`*",
			"System.Array",
			"System.BitConverter", // HMMMMMMMMM SUSSY
			"System.Boolean",
			"System.Byte",
			"System.SByte",
			"System.Buffer",
			"System.Char",
			"System.Collections.Generic.*",
			"System.Collections.IEnumerable",
			"System.Collections.IEnumerator",
			"System.Comparison`1",
			"System.Convert", // HMMMMMMMMM SUSSY
			"System.DateTime",
			"System.DateTimeKind",
			"System.DateTimeOffset",
			"System.DayOfWeek",
			"System.Decimal",
			"System.Delegate",
			"System.Diagnostics.Stopwatch",
			"System.Double",
			"System.Enum",
			"System.EventArgs",
			"System.Exception",
			"System.Float",
			"System.Func`*",
			"System.Globalization.CompareOptions",
			"System.Globalization.CultureInfo",
			"System.Globalization.DateTimeStyles",
			"System.Globalization.NumberStyles",
			"System.Globalization.UnicodeCategory",
			"System.Guid",
			"System.IComparable",
			"System.IComparable`1",
			"System.IDisposable",
			"System.IEquatable`1",
			"System.IFormatProvider",
			"System.IFormattable",
			"System.Int*",
			"System.IO.BinaryReader",
			"System.IO.BinaryWriter",
			"System.IO.MemoryStream",
			"System.IO.Stream",
			"System.IO.SeekOrigin",
			"System.KeyValuePair`2",
			"System.Long",
			"System.ULong",
			"System.Math",
			"System.MathF",
			"System.Nullable`1",
			"System.Object",
			"System.Predicate`1",
			"System.Random",
			"System.RuntimeTypeHandle",
			"System.Short",
			"System.Ushort",
			"System.Single",
			"System.String",
			"System.StringComparer",
			"System.StringComparison",
			"System.StringSplitOptions",
			"System.Text.NormalizationForm",
			"System.Text.StringBuilder",
			"System.Text.Encoding",
			"System.TimeSpan",
			"System.TimeZoneInfo",
			"System.Tuple`*",
			"System.UInt*",
			"System.ValueTuple",
			"System.ValueTuple`*",
			"System.ValueType",
			"System.Void",
			"<PrivateImplementationDetails>", // Probably remove me? But we need a way to handle string hashing.  We can do it with our own function but that's slower.

			// TMPro types
			"TMPro.TextMeshPro",
			"TMPro.TextMeshProUGUI",
			"TMPro.TMP_Text",
			"TMPro.TMP_Dropdown",
			"TMPro.TMP_InputField",

			// Unity types - core
			"UnityEngine.Application",
			"UnityEngine.Application*",
			"UnityEngine.Behaviour",
			"UnityEngine.Color",
			"UnityEngine.Color32",
			"UnityEngine.Component",
			"UnityEngine.CullingGroup",
			"UnityEngine.CustomRenderTexture",
			"UnityEngine.Debug",
			"UnityEngine.DynamicGI",
			"UnityEngine.Events.UnityAction",
			"UnityEngine.Events.UnityAction`*",
			"UnityEngine.Events.UnityEvent",
			"UnityEngine.Events.UnityEvent`*",
			"UnityEngine.Events.UnityEventCallState",
			"UnityEngine.GameObject",     // Hyper restrictive.
			"UnityEngine.Gradient",
			"UnityEngine.GradientAlphaKey",
			"UnityEngine.GradientColorKey",
			"UnityEngine.GradientMode",
			"UnityEngine.HideFlags",
			"UnityEngine.KeyCode",
			"UnityEngine.LayerMask",
			"UnityEngine.Mathf",
			"UnityEngine.Matrix4x4",
			"UnityEngine.MonoBehaviour",   // Note this is needed for the 'ctor, but we can be very restrictive.
			"UnityEngine.Object",
			"UnityEngine.PrimitiveType",
			"UnityEngine.QualitySettings",
			"UnityEngine.Random",
			"UnityEngine.RuntimePlatform",
			"UnityEngine.RuntimePlatform*",
			"UnityEngine.Screen",
			"UnityEngine.ScriptableObject",
			"UnityEngine.SendMessageOptions",
			"UnityEngine.Space",
			"UnityEngine.SystemInfo",
			"UnityEngine.SystemLanguage",
			"UnityEngine.TextAsset",
			"UnityEngine.Time",
			"UnityEngine.Transform",
			"UnityEngine.Quaternion",
			"UnityEngine.Vector*",
			"UnityEngine.Vector2",
			"UnityEngine.Vector2Int",
			"UnityEngine.Vector3",
			"UnityEngine.Vector3Int",
			"UnityEngine.Vector4",

			// Unity types - math/spatial structs
			"UnityEngine.Bounds",
			"UnityEngine.BoundsInt",
			"UnityEngine.Plane",
			"UnityEngine.Ray",
			"UnityEngine.RaycastHit",
			"UnityEngine.RaycastHit2D",
			"UnityEngine.Rect",
			"UnityEngine.RectInt",
			"UnityEngine.RectOffset",
			"UnityEngine.Resolution",

			// Unity types - audio
			"UnityEngine.AudioClip",
			"UnityEngine.AudioClipLoadType",
			"UnityEngine.AudioDataLoadState",
			"UnityEngine.AudioRolloffMode",
			"UnityEngine.AudioSource",
			"UnityEngine.AudioSourceCurveType",
			"UnityEngine.AudioVelocityUpdateMode",
			"UnityEngine.AudioReverbZone",
			"UnityEngine.AudioReverbPreset",
			"UnityEngine.AudioListener",
			"UnityEngine.Audio.AudioMixer",
			"UnityEngine.Audio.AudioMixerGroup",
			"UnityEngine.Audio.AudioMixerSnapshot",
			"UnityEngine.FFTWindow",

			// Unity types - animation
			"UnityEngine.AnimationBlendMode",
			"UnityEngine.AnimationClip",
			"UnityEngine.AnimationCullingType",
			"UnityEngine.AnimationCurve",
			"UnityEngine.AnimationEvent",
			"UnityEngine.AnimationPlayMode",
			"UnityEngine.AnimationState",
			"UnityEngine.Animator",
			"UnityEngine.AnimatorClipInfo",
			"UnityEngine.AnimatorControllerParameter",
			"UnityEngine.AnimatorControllerParameterType",
			"UnityEngine.AnimatorCullingMode",
			"UnityEngine.AnimatorOverrideController",
			"UnityEngine.AnimatorRecorderMode",
			"UnityEngine.AnimatorStateInfo",
			"UnityEngine.AnimatorTransitionInfo",
			"UnityEngine.AnimatorUpdateMode",
			"UnityEngine.Avatar",
			"UnityEngine.AvatarIKGoal",
			"UnityEngine.AvatarIKHint",
			"UnityEngine.AvatarMask",
			"UnityEngine.AvatarMaskBodyPart",
			"UnityEngine.AvatarTarget",
			"UnityEngine.HumanBodyBones",
			"UnityEngine.HumanBone",
			"UnityEngine.HumanLimit",
			"UnityEngine.HumanPose",
			"UnityEngine.HumanPoseHandler",
			"UnityEngine.HumanTrait",
			"UnityEngine.Keyframe",
			"UnityEngine.MatchTargetWeightMask",
			"UnityEngine.PlayMode",
			"UnityEngine.QueueMode",
			"UnityEngine.RuntimeAnimatorController",
			"UnityEngine.SkeletonBone",
			"UnityEngine.WeightedMode",
			"UnityEngine.WrapMode",

			// Unity Animations namespace - constraints
			"UnityEngine.Animations.AimConstraint",
			"UnityEngine.Animations.AimConstraint+WorldUpType",
			"UnityEngine.Animations.Axis",
			"UnityEngine.Animations.ConstraintSource",
			"UnityEngine.Animations.IConstraint",
			"UnityEngine.Animations.LookAtConstraint",
			"UnityEngine.Animations.ParentConstraint",
			"UnityEngine.Animations.PositionConstraint",
			"UnityEngine.Animations.RotationConstraint",
			"UnityEngine.Animations.ScaleConstraint",

			// Unity types - rendering / materials / mesh
			"UnityEngine.BillboardRenderer",
			"UnityEngine.BoneWeight",
			"UnityEngine.IndexFormat",
			"UnityEngine.Material",
			"UnityEngine.MaterialGlobalIlluminationFlags",
			"UnityEngine.MaterialPropertyBlock",
			"UnityEngine.Mesh",
			"UnityEngine.MeshFilter",
			"UnityEngine.MeshRenderer",
			"UnityEngine.MeshTopology",
			"UnityEngine.MotionVectorGenerationMode",
			"UnityEngine.LineAlignment",
			"UnityEngine.LineRenderer",
			"UnityEngine.LineTextureMode",
			"UnityEngine.LODGroup",
			"UnityEngine.OcclusionArea",
			"UnityEngine.OcclusionPortal",
			"UnityEngine.ReflectionProbe",
			"UnityEngine.Renderer",
			"UnityEngine.Rendering.AmbientMode",
			"UnityEngine.Rendering.CameraEvent",
			"UnityEngine.Rendering.IndexFormat",
			"UnityEngine.Rendering.LightEvent",
			"UnityEngine.Rendering.LightProbeUsage",
			"UnityEngine.Rendering.LightShadowResolution",
			"UnityEngine.Rendering.OpaqueSortMode",
			"UnityEngine.Rendering.ReflectionProbeBlendInfo",
			"UnityEngine.Rendering.ReflectionProbeUsage",
			"UnityEngine.Rendering.ShadowCastingMode",
			"UnityEngine.Rendering.ShadowMapPass",
			"UnityEngine.Rendering.SphericalHarmonicsL2",
			"UnityEngine.Rendering.UVChannelFlags",
			"UnityEngine.RenderSettings",
			"UnityEngine.RenderTexture",
			"UnityEngine.RenderTextureFormat",
			"UnityEngine.RenderTextureReadWrite",
			"UnityEngine.Shader",
			"UnityEngine.ShadowCastingMode",
			"UnityEngine.SkinnedMeshRenderer",
			"UnityEngine.SkinQuality",
			"UnityEngine.Sprite",
			"UnityEngine.SpriteAlignment",
			"UnityEngine.SpriteDrawMode",
			"UnityEngine.SpriteMaskInteraction",
			"UnityEngine.SpriteMeshType",
			"UnityEngine.SpriteRenderer",
			"UnityEngine.SpriteSortPoint",
			"UnityEngine.SpriteTileMode",
			"UnityEngine.Texture",
			"UnityEngine.Texture2D",
			"UnityEngine.Texture2DArray",
			"UnityEngine.Texture3D",
			"UnityEngine.Cubemap",
			"UnityEngine.TextureFormat",
			"UnityEngine.TextureWrapMode",
			"UnityEngine.FilterMode",
			"UnityEngine.TrailRenderer",

			// Unity types - lighting
			"UnityEngine.Light",
			"UnityEngine.LightShadowCasterMode",
			"UnityEngine.LightShadows",
			"UnityEngine.LightType",
			"UnityEngine.LightRenderMode",
			"UnityEngine.LightProbeProxyVolume",
			"UnityEngine.LightmapBakeType",
			"UnityEngine.LightProbes",
			"UnityEngine.MixedLightingMode",
			"UnityEngine.ShadowQuality",
			"UnityEngine.ShadowResolution",
			"UnityEngine.ShadowProjection",
			"UnityEngine.ShadowmaskMode",

			// Unity types - camera (with method whitelist for safety)
			"UnityEngine.Camera",
			"UnityEngine.Camera+CameraCallback",
			"UnityEngine.Camera+GateFitMode",
			"UnityEngine.Camera+MonoOrStereoscopicEye",
			"UnityEngine.Camera+StereoscopicEye",
			"UnityEngine.CameraClearFlags",
			"UnityEngine.CameraType",
			"UnityEngine.DepthTextureMode",
			"UnityEngine.RenderingPath",
			"UnityEngine.StereoTargetEyeMask",
			"UnityEngine.TransparencySortMode",
			"UnityEngine.FogMode",
			"UnityEngine.ColorSpace",

			// Unity types - physics
			"UnityEngine.BoxCollider",
			"UnityEngine.CapsuleCollider",
			"UnityEngine.CharacterController",
			"UnityEngine.Collider",
			"UnityEngine.Collision",
			"UnityEngine.CollisionDetectionMode",
			"UnityEngine.ConfigurableJoint",
			"UnityEngine.ContactPoint",
			"UnityEngine.FixedJoint",
			"UnityEngine.ForceMode",
			"UnityEngine.HingeJoint",
			"UnityEngine.Joint",
			"UnityEngine.JointDrive",
			"UnityEngine.JointLimits",
			"UnityEngine.JointMotor",
			"UnityEngine.JointProjectionMode",
			"UnityEngine.JointSpring",
			"UnityEngine.MeshCollider",
			"UnityEngine.MeshColliderCookingOptions",
			"UnityEngine.PhysicMaterial",
			"UnityEngine.PhysicMaterialCombine",
			"UnityEngine.Physics",
			"UnityEngine.QueryTriggerInteraction",
			"UnityEngine.Rigidbody",
			"UnityEngine.RigidbodyConstraints",
			"UnityEngine.RigidbodyInterpolation",
			"UnityEngine.SphereCollider",
			"UnityEngine.SoftJointLimit",
			"UnityEngine.SoftJointLimitSpring",
			"UnityEngine.SpringJoint",
			"UnityEngine.WheelCollider",
			"UnityEngine.WheelFrictionCurve",
			"UnityEngine.WheelHit",

			// Unity types - particles
			"UnityEngine.ParticleSystem",
			"UnityEngine.ParticleSystem+*",
			"UnityEngine.ParticleSystemRenderer",
			"UnityEngine.ParticleSystemForceField",
			"UnityEngine.ParticleSystemSimulationSpace",
			"UnityEngine.ParticleSystemShapeType",
			"UnityEngine.ParticleSystemSortMode",
			"UnityEngine.ParticleSystemRenderMode",
			"UnityEngine.ParticleSystemStopBehavior",
			"UnityEngine.ParticleSystemEmissionType",

			// Unity AI - NavMesh
			"UnityEngine.AI.NavMesh",
			"UnityEngine.AI.NavMeshAgent",
			"UnityEngine.AI.NavMeshBuildSettings",
			"UnityEngine.AI.NavMeshHit",
			"UnityEngine.AI.NavMeshObstacle",
			"UnityEngine.AI.NavMeshObstacleShape",
			"UnityEngine.AI.NavMeshPath",
			"UnityEngine.AI.NavMeshPathStatus",
			"UnityEngine.AI.NavMeshQueryFilter",
			"UnityEngine.AI.NavMeshTriangulation",
			"UnityEngine.AI.OffMeshLink",
			"UnityEngine.AI.OffMeshLinkData",
			"UnityEngine.AI.ObstacleAvoidanceType",

			// Unity UI
			"UnityEngine.Canvas",
			"UnityEngine.CanvasGroup",
			"UnityEngine.CanvasRenderer",
			"UnityEngine.RectTransform",
			"UnityEngine.RectTransform+Axis",
			"UnityEngine.RectTransform+Edge",
			"UnityEngine.RenderMode",
			"UnityEngine.TextAnchor",
			"UnityEngine.FontStyle",
			"UnityEngine.HorizontalWrapMode",
			"UnityEngine.VerticalWrapMode",
			"UnityEngine.UI.*",

			// Unity Event Systems
			"UnityEngine.EventSystems.AxisEventData",
			"UnityEngine.EventSystems.BaseEventData",
			"UnityEngine.EventSystems.EventTrigger",
			"UnityEngine.EventSystems.EventTrigger+Entry",
			"UnityEngine.EventSystems.EventTrigger+TriggerEvent",
			"UnityEngine.EventSystems.EventTriggerType",
			"UnityEngine.EventSystems.PointerEventData",
			"UnityEngine.EventSystems.PointerEventData+InputButton",
			"UnityEngine.EventSystems.PointerEventData+FramePressState",
			"UnityEngine.EventSystems.RaycastResult",
        };

        static HashSet<String> whiteListFields = new HashSet<String>(){
			// Unity Vector / Quaternion math fields
			"UnityEngine.Vector*.x",
            "UnityEngine.Vector*.y",
            "UnityEngine.Vector*.z",
            "UnityEngine.Vector*.w",
            "UnityEngine.Quaternion*",

			// Unity Color fields (raw r/g/b/a access for both Color and Color32)
			"UnityEngine.Color.r",
			"UnityEngine.Color.g",
			"UnityEngine.Color.b",
			"UnityEngine.Color.a",
			"UnityEngine.Color32.r",
			"UnityEngine.Color32.g",
			"UnityEngine.Color32.b",
			"UnityEngine.Color32.a",

			// Unity math/spatial struct fields
			"UnityEngine.Bounds.*",
			"UnityEngine.BoundsInt.*",
			"UnityEngine.Plane.*",
			"UnityEngine.Ray.*",
			"UnityEngine.RaycastHit.*",
			"UnityEngine.RaycastHit2D.*",
			"UnityEngine.Rect.*",
			"UnityEngine.RectInt.*",
			"UnityEngine.Resolution.*",
			"UnityEngine.Matrix4x4.m*", // m00..m33 entries
			"UnityEngine.Keyframe.*",
			"UnityEngine.GradientAlphaKey.*",
			"UnityEngine.GradientColorKey.*",
			"UnityEngine.AnimatorClipInfo.*",
			"UnityEngine.AnimatorControllerParameter.*",
			"UnityEngine.HumanBone.*",
			"UnityEngine.HumanLimit.*",
			"UnityEngine.SkeletonBone.*",
			"UnityEngine.Animations.ConstraintSource.*",

			// Unity physics struct fields
			"UnityEngine.ContactPoint.*",
			"UnityEngine.JointDrive.*",
			"UnityEngine.JointLimits.*",
			"UnityEngine.JointMotor.*",
			"UnityEngine.JointSpring.*",
			"UnityEngine.SoftJointLimit.*",
			"UnityEngine.SoftJointLimitSpring.*",
			"UnityEngine.WheelFrictionCurve.*",
			"UnityEngine.WheelHit.*",

			// Unity AI struct fields
			"UnityEngine.AI.NavMeshHit.*",
			"UnityEngine.AI.OffMeshLinkData.*",
			"UnityEngine.AI.NavMeshTriangulation.*",

			// System fields
			"System.Array.*",
            "System.String.*",
			"System.DateTime.*",
			"System.TimeSpan.*",
			"System.Guid.*",
			"System.Collections.Generic.KeyValuePair*",
			"System.KeyValuePair*",

			// Basis types
			"Basis.Scripts.BasisSdk.Interactions.BasisPickupInteractable.OnPickupUse",
            "Basis.Scripts.BasisSdk.Interactions.BasisInteractableObject.OnInteractStartEvent",
            "Basis.Scripts.BasisSdk.Interactions.BasisInteractableObject.OnInteractEndEvent",
            "Basis.BasisNetworkBehaviour.CurrentOwnerId",
            "Basis.BasisNetworkBehaviour.IsOwnedLocallyOnServer",
            "Basis.Scripts.Networking.NetworkedAvatar.BasisNetworkPlayer.playerId",
        };

        static public HashSet<String> GetWhiteListTypes() { return whiteListType; }

		// This is called by CilboxUsage to decide of a type is allowed.
		// If a type is allowed, by defalt it is all allowed.
		override public bool CheckTypeAllowed( String sType )
		{
			if( whiteListType.Contains( sType ) ) return true;

			foreach( string allowedType in whiteListType )
			{
				if( !allowedType.Contains( '*' ) ) continue;

				string[] allowedPrefix = allowedType.Split( '*' );
				if( sType.StartsWith( allowedPrefix[0], StringComparison.Ordinal ) && sType.EndsWith( allowedPrefix[1], StringComparison.Ordinal ) ) return true;
			}

			return false;
		}

		override public bool CheckFieldAllowed( String sType, String sFieldName )
		{
			if( !CheckTypeAllowed( sType ) ) return false;
			string fullField = sType + "." + sFieldName;
			if( whiteListFields.Contains( fullField ) ) return true;
			foreach( string allowedType in whiteListFields )
			{
				if( !allowedType.Contains( '*' ) ) continue;

				string[] allowedPrefix = allowedType.Split( '*' );
				if( fullField.StartsWith( allowedPrefix[0], StringComparison.Ordinal ) && fullField.EndsWith( allowedPrefix[1], StringComparison.Ordinal ) ) return true;
			}
			
			return false;
		}

		// Whitelist methods on native types.
		// If a type is not in this dictionary, then all methods are allowed.
		static Dictionary<Type, HashSet<string>> whiteListMethods = new Dictionary<Type, HashSet<string>>()
		{
			{ typeof(UnityEngine.MonoBehaviour),       new HashSet<string>{ ".ctor" } },
			{ typeof(UnityEngine.ScriptableObject),    new HashSet<string>{ ".ctor" } },
			{ typeof(UnityEngine.Events.UnityAction),  new HashSet<string>{ ".ctor" } },
			{ typeof(UnityEngine.GameObject),          new HashSet<string>{ nameof(GameObject.SetActive), nameof(GameObject.GetComponents) } },
			{ typeof(System.Type),                     new HashSet<string>() }, // nothing allowed
			{ typeof(BasisNetworkPlayer), new HashSet<string> {
				typeof(BasisNetworkPlayer).GetProperty(nameof(BasisNetworkPlayer.playerId)).GetGetMethod().Name,
				typeof(BasisNetworkPlayer).GetProperty(nameof(BasisNetworkPlayer.LocalPlayer)).GetGetMethod().Name,
				typeof(BasisNetworkPlayer).GetProperty(nameof(BasisNetworkPlayer.Player)).GetGetMethod().Name,
				typeof(BasisNetworkPlayer).GetProperty(nameof(BasisNetworkPlayer.displayName)).GetGetMethod().Name,
				} },
			{ typeof(BasisLocalPlayer), new HashSet<string>{ nameof(BasisLocalPlayer.GetPositionAndRotation),
				nameof(BasisLocalPlayer.Teleport),
				nameof(BasisLocalPlayer.Respawn),
				$"get_{nameof(BasisLocalPlayer.Instance)}", }},
			{ typeof(Buffer), new HashSet<string>{ "BlockCopy" } },
			{ typeof(BitConverter), new HashSet<string>{
				"GetBytes", "ToBoolean", "ToChar", "ToDouble", "ToInt16", "ToInt32",
				"ToInt64", "ToSingle", "ToString", "ToUInt16", "ToUInt32", "ToUInt64",
				"DoubleToInt64Bits", "Int64BitsToDouble", "SingleToInt32Bits", "Int32BitsToSingle" } },
			{ typeof(Convert), new HashSet<string>{
				"ToInt16", "ToInt32", "ToInt64", "ToUInt16", "ToUInt32", "ToUInt64",
				"ToByte", "ToSByte", "ToBoolean", "ToChar", "ToSingle", "ToDouble",
				"ToString", "ToBase64String", "FromBase64String",
				"ToDateTime", "ToDecimal" } },
			// UnityEngine.Application is whitelisted only for harmless read-only platform info.
			// All other entrypoints (OpenURL, Quit, Unload, ExternalCall, LoadLevel*) are blocked
			// in CheckMethodAllowed below.
			{ typeof(UnityEngine.Application), new HashSet<string>{
				"get_companyName",
				"get_genuine",
				"get_genuineCheckAvailable",
				"get_identifier",
				"get_installerName",
				"get_installMode",
				"get_internetReachability",
				"get_isBatchMode",
				"get_isConsolePlatform",
				"get_isEditor",
				"get_isFocused",
				"get_isMobilePlatform",
				"get_isPlaying",
				"get_platform",
				"get_productName",
				"get_runInBackground",
				"get_sandboxType",
				"get_systemLanguage",
				"get_targetFrameRate",
				"get_unityVersion",
				"get_version",
				"IsPlaying",
				} },
			// UnityEngine.Camera: limit to query/transform helpers + property accessors.
			// Block Render(), CopyFrom(), SetTargetBuffers(), AddCommandBuffer*(),
			// RenderToCubemap(), SetReplacementShader(), SetStereoView/ProjectionMatrix.
			{ typeof(UnityEngine.Camera), new HashSet<string>{
				"CalculateFrustumCorners",
				"CalculateObliqueMatrix",
				"GetStereoNonJitteredProjectionMatrix",
				"GetStereoProjectionMatrix",
				"GetStereoViewMatrix",
				"ResetAspect",
				"ResetCullingMatrix",
				"ResetProjectionMatrix",
				"ResetReplacementShader",
				"ResetTransparencySortSettings",
				"ResetWorldToCameraMatrix",
				"ScreenPointToRay",
				"ScreenToViewportPoint",
				"ScreenToWorldPoint",
				"ViewportPointToRay",
				"ViewportToScreenPoint",
				"ViewportToWorldPoint",
				"WorldToScreenPoint",
				"WorldToViewportPoint",
				"GetAllCameras",
				"get_actualRenderingPath",
				"get_allCameras",
				"get_allCamerasCount",
				"get_allowDynamicResolution",
				"get_allowHDR",
				"get_allowMSAA",
				"get_aspect",
				"get_backgroundColor",
				"get_cameraToWorldMatrix",
				"get_cameraType",
				"get_clearFlags",
				"get_cullingMask",
				"get_current",
				"get_depth",
				"get_depthTextureMode",
				"get_eventMask",
				"get_farClipPlane",
				"get_fieldOfView",
				"get_focalLength",
				"get_forceIntoRenderTexture",
				"get_lensShift",
				"get_main",
				"get_nearClipPlane",
				"get_orthographic",
				"get_orthographicSize",
				"get_pixelHeight",
				"get_pixelRect",
				"get_pixelWidth",
				"get_projectionMatrix",
				"get_rect",
				"get_renderingPath",
				"get_scaledPixelHeight",
				"get_scaledPixelWidth",
				"get_sensorSize",
				"get_stereoActiveEye",
				"get_stereoConvergence",
				"get_stereoEnabled",
				"get_stereoSeparation",
				"get_stereoTargetEye",
				"get_targetDisplay",
				"get_transparencySortAxis",
				"get_transparencySortMode",
				"get_useJitteredProjectionMatrixForTransparentRendering",
				"get_useOcclusionCulling",
				"get_usePhysicalProperties",
				"get_velocity",
				"get_worldToCameraMatrix",
				"set_aspect",
				"set_backgroundColor",
				"set_clearFlags",
				"set_cullingMask",
				"set_depth",
				"set_depthTextureMode",
				"set_eventMask",
				"set_farClipPlane",
				"set_fieldOfView",
				"set_focalLength",
				"set_lensShift",
				"set_nearClipPlane",
				"set_orthographic",
				"set_orthographicSize",
				"set_rect",
				"set_renderingPath",
				"set_sensorSize",
				"set_stereoConvergence",
				"set_stereoSeparation",
				"set_stereoTargetEye",
				"set_targetDisplay",
				"set_transparencySortAxis",
				"set_transparencySortMode",
				"set_useJitteredProjectionMatrixForTransparentRendering",
				"set_useOcclusionCulling",
				"set_usePhysicalProperties",
				} },
			// UnityEngine.Light: getters/setters only, no command buffers. CommandBuffer
			// add/remove can hide arbitrary GPU-side work outside the sandbox surface.
			{ typeof(UnityEngine.Light), new HashSet<string>{
				"get_bakingOutput",
				"get_bounceIntensity",
				"get_color",
				"get_colorTemperature",
				"get_commandBufferCount",
				"get_cookie",
				"get_cookieSize",
				"get_cullingMask",
				"get_intensity",
				"get_lightShadowCasterMode",
				"get_range",
				"get_renderMode",
				"get_shadowBias",
				"get_shadowCustomResolution",
				"get_shadowNearPlane",
				"get_shadowNormalBias",
				"get_shadowResolution",
				"get_shadowStrength",
				"get_shadows",
				"get_spotAngle",
				"get_type",
				"get_useColorTemperature",
				"set_bounceIntensity",
				"set_color",
				"set_colorTemperature",
				"set_cookie",
				"set_cookieSize",
				"set_cullingMask",
				"set_intensity",
				"set_lightShadowCasterMode",
				"set_range",
				"set_renderMode",
				"set_shadowBias",
				"set_shadowCustomResolution",
				"set_shadowNearPlane",
				"set_shadowNormalBias",
				"set_shadowResolution",
				"set_shadowStrength",
				"set_shadows",
				"set_spotAngle",
				"set_type",
				"set_useColorTemperature",
				} },
			// UnityEngine.Physics static raycast/overlap surface. No CapsuleCast for now —
			// the per-call alloc overload list is huge; consumers can use Raycast/SphereCast.
			{ typeof(UnityEngine.Physics), new HashSet<string>{
				"BoxCast", "BoxCastAll", "BoxCastNonAlloc",
				"CapsuleCast", "CapsuleCastAll", "CapsuleCastNonAlloc",
				"CheckBox", "CheckCapsule", "CheckSphere",
				"ClosestPoint",
				"ComputePenetration",
				"GetIgnoreLayerCollision",
				"IgnoreCollision",
				"IgnoreLayerCollision",
				"Linecast",
				"OverlapBox", "OverlapBoxNonAlloc",
				"OverlapCapsule", "OverlapCapsuleNonAlloc",
				"OverlapSphere", "OverlapSphereNonAlloc",
				"Raycast", "RaycastAll", "RaycastNonAlloc",
				"SphereCast", "SphereCastAll", "SphereCastNonAlloc",
				"get_autoSimulation", "get_autoSyncTransforms", "get_gravity",
				"get_queriesHitBackfaces", "get_queriesHitTriggers",
				"get_sleepThreshold", "get_defaultContactOffset",
				} },
			// UnityEngine.Screen: read-only platform info.
			{ typeof(UnityEngine.Screen), new HashSet<string>{
				"get_currentResolution",
				"get_dpi",
				"get_fullScreen",
				"get_fullScreenMode",
				"get_height",
				"get_orientation",
				"get_resolutions",
				"get_safeArea",
				"get_width",
				} },
			// UnityEngine.SystemInfo: device fingerprinting is the privacy concern.
			// Allow a curated set of harmless capability queries.
			{ typeof(UnityEngine.SystemInfo), new HashSet<string>{
				"get_graphicsDeviceType",
				"get_graphicsMemorySize",
				"get_graphicsShaderLevel",
				"get_processorCount",
				"get_processorFrequency",
				"get_supportsAsyncCompute",
				"get_supportsAudio",
				"get_supportsComputeShaders",
				"get_supportsGyroscope",
				"get_supportsInstancing",
				"get_supportsMotionVectors",
				"get_supportsRawShadowDepthSampling",
				"get_supportsShadows",
				"get_supportsSparseTextures",
				"get_supportsVibration",
				"get_systemMemorySize",
				"IsFormatSupported",
				} },
			// UnityEngine.QualitySettings: keep read-only; deny SetQualityLevel which yanks the entire scene render path.
			{ typeof(UnityEngine.QualitySettings), new HashSet<string>{
				"GetQualityLevel",
				"GetQualitySettings",
				"get_activeColorSpace",
				"get_anisotropicFiltering",
				"get_antiAliasing",
				"get_desiredColorSpace",
				"get_lodBias",
				"get_masterTextureLimit",
				"get_maximumLODLevel",
				"get_names",
				"get_pixelLightCount",
				"get_realtimeReflectionProbes",
				"get_shadowCascade2Split",
				"get_shadowCascade4Split",
				"get_shadowCascades",
				"get_shadowDistance",
				"get_shadowProjection",
				"get_shadowResolution",
				"get_shadows",
				"get_softParticles",
				"get_softVegetation",
				"get_vSyncCount",
				} },
		};

		// After a type is allowed, this is called to see if the specific method is OK.
		override public bool CheckMethodAllowed( out MethodInfo mi, Type declaringType, String name, Serializee [] parametersIn, Serializee [] genericArgumentsIn, String fullSignature )
		{
			mi = null;

			if( name.Contains( "Invoke" ) ) return false;

			// UnityEngine.Application.OpenURL opens an arbitrary URL in the native
			// browser — the exact payload behind the reported prop exploit. Deny it
			// explicitly so this never works from cilbox regardless of how the
			// Application type ends up whitelisted. Same shape blocks Quit, Unload,
			// LoadLevel*, ExternalCall/Eval and other process-altering escapes.
			if( declaringType == typeof(UnityEngine.Application) && (
				name == "OpenURL" ||
				name == "Quit" ||
				name == "Unload" ||
				name == "CanStreamedLevelBeLoaded" ||
				name == "ExternalCall" ||
				name == "ExternalEval" ||
				name == "GetBuildTags" ||
				name == "RequestUserAuthorization" ||
				name == "SetBuildTags" ||
				name == "SetStackTraceLogType" ||
				name.StartsWith( "Load", StringComparison.Ordinal ) ) )
				return false;

			// Redirect every UnityEngine.Object.Instantiate variant through the
			// sanitizing shim so spawned prefabs are scrubbed (disallowed components
			// destroyed, persistent UnityEvent listeners killed) while parked under a
			// disabled host before they become active in hierarchy.
			if( declaringType == typeof(UnityEngine.Object) &&
				( name == "Instantiate" || name == "InstantiateAsync" ) )
			{
				mi = Basis.Shims.BasisCilboxInstantiateShim.ResolveShim(
					usage, name, parametersIn, genericArgumentsIn, fullSignature );
				return mi != null;
			}

			// SendMessage / BroadcastMessage / AddComponent / Animator behaviour fetches
			// reflectively reach unsanitised behaviours and bypass cilbox guardrails.
			if( declaringType == typeof(UnityEngine.GameObject) && (
				name == "AddComponent" ||
				name == "SendMessage" ||
				name == "SendMessageUpwards" ||
				name == "BroadcastMessage" ) )
				return false;
			if( declaringType == typeof(UnityEngine.Component) && (
				name == "SendMessage" ||
				name == "SendMessageUpwards" ||
				name == "BroadcastMessage" ) )
				return false;
			if( declaringType == typeof(UnityEngine.Animator) && (
				name == "GetBehaviour" ||
				name == "GetBehaviours" ) )
				return false;

			if( whiteListMethods.TryGetValue( declaringType, out var allowed ) )
			{
				if( !allowed.Contains( name ) ) return false;
			}

			return true;
		}

        public override bool GetTypeOverride(string sType, out Type t)
        {
            switch (sType)
            {
                case "UnityEngine.Video.VideoPlayer":
                    t = typeof(Basis.Shims.VideoPlayerShim);
                    return true;
                case "UnityEngine.Video.VideoPlayer+ErrorEventHandler":
                    t = typeof(Basis.Shims.VideoPlayerShim.ErrorEventHandlerShim);
                    return true;
                case "UnityEngine.Video.VideoPlayer+EventHandler":
                    t = typeof(Basis.Shims.VideoPlayerShim.EventHandlerShim);
                    return true;
                case "UnityEngine.Video.VideoPlayer+FrameReadyEventHandler":
                    t = typeof(Basis.Shims.VideoPlayerShim.FrameReadyEventHandlerShim);
                    return true;
                case "UnityEngine.Video.VideoPlayer+TimeEventHandler":
                    t = typeof(Basis.Shims.VideoPlayerShim.TimeEventHandlerShim);
                    return true;
                case "UnityEngine.Debug":
                    t = typeof(Basis.Shims.BasisDebugPropsShim);
                    return true;
                default:
                    t = null;
                    return false;
            }
        }
    }
}
