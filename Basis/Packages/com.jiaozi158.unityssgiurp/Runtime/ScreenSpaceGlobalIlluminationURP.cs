using System;
using System.Reflection;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.Experimental.Rendering;

#if UNITY_6000_0_OR_NEWER
using UnityEngine.Rendering.RenderGraphModule;
#endif

[DisallowMultipleRendererFeature("Screen Space Global Illumination")]
[Tooltip("The Screen Space Global Illumination uses the depth and color buffer of the screen to calculate diffuse light bounces.")]
[HelpURL("https://github.com/jiaozi158/UnitySSGIURP/blob/main")]
public class ScreenSpaceGlobalIlluminationURP : ScriptableRendererFeature
{
    private Material m_SSGIMaterial;

    [Header("Setup")]
    [Tooltip("The shader of screen space global illumination.")]
    [SerializeField] private Shader m_Shader;
    [Tooltip("Specifies if URP computes screen space global illumination in Rendering Debugger view. \nThis is disabled by default to avoid affecting the individual lighting previews.")]
    [SerializeField] private bool m_RenderingDebugger = false;

    [Header("Performance")]
    [Tooltip("Specifies if URP computes screen space global illumination in both real-time and baked reflection probes. \nScreen space global illumination in real-time reflection probes may reduce performace.")]
    [SerializeField] private bool m_ReflectionProbes = true;
    [Tooltip("Enables high-quality upscaling for screen space global illumination. \nThis may impact performance.")]
    [SerializeField] private bool m_HighQualityUpscaling = false;

    [Header("Lighting")]
    [Tooltip("Specifies if screen space global illumination overrides ambient lighting. \nThis ensures the accuracy of indirect lighting from SSGI.")]
    [SerializeField] private bool m_OverrideAmbientLighting = true;

    [Tooltip("Lets surfaces whose shader has no \"UniversalGBuffer\" pass receive global illumination, using a normal reconstructed from depth and an albedo implied by the pixel colour and the ambient light at it.")]
    [SerializeField] private bool m_GBufferFallback = true;
    [Tooltip("Albedo assumed for surfaces without a GBuffer pass where no ambient light reaches them, so nothing can be implied from their colour.")]
    [SerializeField, Range(0.0f, 1.0f)] private float m_FallbackAlbedo = 0.5f;
    [Tooltip("Renderers registered through RegisterRenderers whose shader has no GBuffer pass are drawn into the GBuffer with this shader, which reads the material's _MainTex, _Color and _BumpMap.")]
    [SerializeField] private Shader m_GBufferOverrideShader;
    [Tooltip("Rendering layer bit that marks the renderers drawn with the override shader.")]
    [SerializeField, Range(1, 31)] private int m_GBufferOverrideRenderingLayerBit = 31;

    [Header("Advanced")]
    [Tooltip("Renders back-face lighting when using automatic thickness mode. \nThis improves accuracy in some cases, but may severely impact performance.")]
    [SerializeField] private bool m_BackfaceLighting = false;

    /// <summary>
    /// Get the material of screen space global illumination shader.
    /// </summary>
    /// <value>
    /// The material of screen space global illumination shader.
    /// </value>
    public Material SSGIMaterial
    {
        get { return m_SSGIMaterial; }
    }

    /// <summary>
    /// Gets or sets the screen space global illumination shader.
    /// </summary>
    /// <value>
    /// The screen space global illumination shader.
    /// </value>
    public Shader SSGIShader
    {
        get { return m_Shader; }
        set { m_Shader = value == Shader.Find(m_SSGIShaderName) ? value : m_Shader; }
    }

    /// <summary>
    /// Gets or sets a value indicating whether to compute screen space global illumination in Rendering Debugger view.
    /// </summary>
    /// <remarks>
    /// This is disabled by default to avoid affecting the individual lighting previews.
    /// </remarks>
    public bool RenderingDebugger
    {
        get { return m_RenderingDebugger; }
        set { m_RenderingDebugger = value; }
    }

    /// <summary>
    /// Gets or sets a value indicating whether to compute screen space global illumination in both real-time and baked reflection probes.
    /// </summary>
    /// <remarks>
    /// Screen space global illumination in real-time reflection probes may reduce performace.
    /// </remarks>
    public bool ReflectionProbes
    {
        get { return m_ReflectionProbes; }
        set { m_ReflectionProbes = value; }
    }

    /// <summary>
    /// Gets or sets a value indicating whether to enable high-quality upscaling for screen space global illumination.
    /// </summary>
    public bool HighQualityUpscaling
    {
        get { return m_HighQualityUpscaling; }
        set { m_HighQualityUpscaling = value; }
    }

    /// <summary>
    /// Gets or sets a value indicating whether screen space global illumination overrides ambient lighting.
    /// </summary>
    /// <remarks>
    /// Enable this to ensure the accuracy of indirect lighting from SSGI.
    /// </remarks>
    public bool OverrideAmbientLighting
    {
        get { return m_OverrideAmbientLighting; }
        set { m_OverrideAmbientLighting = value; }
    }

    /// <summary>
    /// Gets or sets a value indicating whether surfaces without a "UniversalGBuffer" pass receive global illumination.
    /// </summary>
    public bool GBufferFallback
    {
        get { return m_GBufferFallback; }
        set { m_GBufferFallback = value; }
    }

    /// <summary>
    /// Albedo assumed for surfaces without a GBuffer pass that receive no ambient light, when <see cref="GBufferFallback"/> is enabled.
    /// Elsewhere the albedo is implied by the pixel colour and the ambient light at it.
    /// </summary>
    public float FallbackAlbedo
    {
        get { return m_FallbackAlbedo; }
        set { m_FallbackAlbedo = Mathf.Clamp01(value); }
    }

    /// <summary>
    /// Shader used to draw registered renderers without a GBuffer pass into the GBuffer.
    /// </summary>
    public Shader GBufferOverrideShader
    {
        get { return m_GBufferOverrideShader; }
        set { m_GBufferOverrideShader = value; }
    }

    /// <summary>
    /// Rendering layer mask that marks renderers drawn with <see cref="GBufferOverrideShader"/>. Mirrors the feature's setting.
    /// </summary>
    public static uint GBufferOverrideRenderingLayerMask { get; private set; } = 1u << 31;

    private static readonly Dictionary<Shader, bool> s_GBufferPassCache = new Dictionary<Shader, bool>();
    private static readonly List<Renderer> s_RendererBuffer = new List<Renderer>();

    // Created on first use: ShaderTagId calls Shader.TagToID, which Unity forbids while a ScriptableObject
    // (this feature) is being constructed, and static initializers run exactly then.
    private static bool s_GBufferTagsReady;
    private static ShaderTagId s_LightModeTag;
    private static ShaderTagId s_UniversalGBufferTag;
    private static ShaderTagId s_SSGIGBufferTag;

    private static void EnsureGBufferTags()
    {
        if (s_GBufferTagsReady)
            return;
        s_LightModeTag = new ShaderTagId("LightMode");
        s_UniversalGBufferTag = new ShaderTagId("UniversalGBuffer");
        s_SSGIGBufferTag = new ShaderTagId(SSGIGBufferLightMode);
        s_GBufferTagsReady = true;
    }

    /// <summary>
    /// True when the shader has a "UniversalGBuffer" or "SSGIGBuffer" pass, i.e. it can write the GBuffer itself.
    /// </summary>
    public static bool HasGBufferPass(Shader shader)
    {
        if (shader == null)
            return false;
        if (s_GBufferPassCache.TryGetValue(shader, out bool hasPass))
            return hasPass;

        EnsureGBufferTags();
        // Every subshader is checked: the active one is a fallback whenever the device lacks the shader model
        // (batch mode, null device), and that would hide the GBuffer pass of URP Lit.
        int subshaderCount = shader.subshaderCount;
        for (int subshader = 0; subshader < subshaderCount && !hasPass; subshader++)
        {
            int passCount = shader.GetPassCountInSubshader(subshader);
            for (int i = 0; i < passCount && !hasPass; i++)
            {
                ShaderTagId lightMode = shader.FindPassTagValue(subshader, i, s_LightModeTag);
                hasPass = lightMode == s_UniversalGBufferTag || lightMode == s_SSGIGBufferTag;
            }
        }
        s_GBufferPassCache[shader] = hasPass;
        return hasPass;
    }

    /// <summary>
    /// Marks the renderer for the override GBuffer shader when any of its materials has no GBuffer pass, and clears the
    /// mark otherwise. Call it when a renderer's materials are known (for example once an avatar has loaded).
    /// </summary>
    /// <returns>True when the renderer will be drawn with the override shader.</returns>
    public static bool RegisterRenderer(Renderer renderer)
    {
        if (renderer == null)
            return false;

        bool needsOverride = false;
        Material[] materials = renderer.sharedMaterials;
        for (int i = 0; i < materials.Length && !needsOverride; i++)
        {
            Material material = materials[i];
            needsOverride = material != null && !HasGBufferPass(material.shader);
        }

        uint mask = GBufferOverrideRenderingLayerMask;
        if (needsOverride)
            renderer.renderingLayerMask |= mask;
        else
            renderer.renderingLayerMask &= ~mask;
        return needsOverride;
    }

    /// <summary>
    /// Registers every renderer under the root, including inactive ones.
    /// </summary>
    /// <returns>The number of renderers that will be drawn with the override shader.</returns>
    public static int RegisterRenderers(GameObject root)
    {
        if (root == null)
            return 0;

        root.GetComponentsInChildren(true, s_RendererBuffer);
        int overridden = 0;
        for (int i = 0; i < s_RendererBuffer.Count; i++)
        {
            if (RegisterRenderer(s_RendererBuffer[i]))
                overridden++;
        }
        s_RendererBuffer.Clear();
        return overridden;
    }

    /// <summary>
    /// Renders back-face lighting when using automatic thickness mode.
    /// </summary>
    /// <remarks>
    /// This improves accuracy in some cases, but may severely impact performance.
    /// </remarks>
    public bool BackfaceLighting
    {
        get { return m_BackfaceLighting; }
        set { m_BackfaceLighting = value; }
    }

    /// <summary>
    /// Optional per-camera gate. When set, screen space global illumination is only rendered for cameras this callback accepts.
    /// </summary>
    /// <remarks>
    /// Mirrors, capture cameras and reflection probes are Game cameras too, so a host that only wants the effect on the
    /// player's own view should reject everything else here rather than relying on <see cref="CameraType"/>.
    /// </remarks>
    public static Func<Camera, bool> CameraFilter;

    /// <summary>
    /// Replaces the combine pass output with an intermediate buffer, for inspecting the effect at runtime.
    /// </summary>
    public enum DebugViewMode
    {
        None = 0,
        IndirectLight = 1,
        GlobalIlluminationContribution = 2,
        GBufferAlbedo = 3,
        GBufferNormals = 4,
    }

    /// <summary>
    /// Debug output of the combine pass. GBuffer views are black on surfaces whose shader has no "UniversalGBuffer" pass,
    /// which is exactly the geometry that cannot receive screen space global illumination.
    /// </summary>
    public static DebugViewMode DebugView = DebugViewMode.None;

    /// <summary>
    /// Keeps the effect running while the Rendering Debugger window is open, regardless of the renderer feature's own
    /// "Rendering Debugger" setting.
    /// </summary>
    public static bool KeepRenderingWithDebugger;

    /// <summary>
    /// Derives the adaptive ray marching step budget from the volume's maximum step count.
    /// Per 8 steps: 1 small step, 2 medium steps, 5 large steps.
    /// </summary>
    internal static void ComputeRayMarchingSteps(int maxRaySteps, out bool lowStepCount, out int smallSteps, out int mediumSteps)
    {
        lowStepCount = maxRaySteps <= 16;
        int groupsCount = maxRaySteps / 8;
        smallSteps = lowStepCount ? 0 : Mathf.Max(groupsCount, 4);
        mediumSteps = lowStepCount ? groupsCount + 2 : smallSteps + groupsCount * 2;
    }

    /// <summary>
    /// For high resolution: Use a lower accumulation factor to help reduce latency.
    /// For low resolution: Use a higher accumulation factor to improve denoising.
    /// </summary>
    internal static float ComputeTemporalIntensity(float denoiseIntensity, float resolutionScale)
    {
        return Mathf.Lerp(denoiseIntensity + 0.02f, denoiseIntensity - 0.04f, resolutionScale);
    }

    private const string m_SSGIShaderName = "Hidden/Lighting/ScreenSpaceGlobalIllumination";

    /// <summary>
    /// Shader passes the forward GBuffer pass draws. "SSGIGBuffer" is a pass shaders can add for this effect only
    /// (see the Poiyomi tool in Editor/), without exposing a "UniversalGBuffer" pass to the Deferred rendering path.
    /// </summary>
    public const string SSGIGBufferLightMode = "SSGIGBuffer";
    private readonly string[] m_GBufferPassNames = new string[] { "UniversalGBuffer", SSGIGBufferLightMode };
    private PreRenderScreenSpaceGlobalIlluminationPass m_PreRenderSSGIPass;
    private ScreenSpaceGlobalIlluminationPass m_SSGIPass;
    private BackfaceDataPass m_BackfaceDataPass;
    private ForwardGBufferPass m_ForwardGBufferPass;

    // Used in Forward GBuffer render pass
    private readonly static FieldInfo gBufferFieldInfo = typeof(UniversalRenderer).GetField("m_GBufferPass", BindingFlags.NonPublic | BindingFlags.Instance);

    // [Resolve Later] The "_CameraNormalsTexture" still exists after disabling DepthNormals Prepass, which may cause issue during rendering.
    // So instead of checking the RTHandle, we need to check if DepthNormals Prepass is enqueued.
    //private readonly static FieldInfo normalsTextureFieldInfo = typeof(UniversalRenderer).GetField("m_NormalsTexture", BindingFlags.NonPublic | BindingFlags.Instance);

    // Avoid printing messages every frame
    private bool isShaderMismatchLogPrinted = false;
    private bool isDebuggerLogPrinted = false;
    private bool isBackfaceLightingLogPrinted = false;

    // SSGI Shader Property IDs
    private static readonly int _MaxSteps = Shader.PropertyToID("_MaxSteps");
    private static readonly int _MaxSmallSteps = Shader.PropertyToID("_MaxSmallSteps");
    private static readonly int _MaxMediumSteps = Shader.PropertyToID("_MaxMediumSteps");
    private static readonly int _Thickness = Shader.PropertyToID("_Thickness");
    private static readonly int _Thickness_Increment = Shader.PropertyToID("_Thickness_Increment");
    private static readonly int _StepSize = Shader.PropertyToID("_StepSize");
    private static readonly int _SmallStepSize = Shader.PropertyToID("_SmallStepSize");
    private static readonly int _MediumStepSize = Shader.PropertyToID("_MediumStepSize");
    private static readonly int _RayCount = Shader.PropertyToID("_RayCount");
    private static readonly int _TemporalIntensity = Shader.PropertyToID("_TemporalIntensity");
    private static readonly int _MaxBrightness = Shader.PropertyToID("_MaxBrightness");
    private static readonly int _IsProbeCamera = Shader.PropertyToID("_IsProbeCamera");
    private static readonly int _BackDepthEnabled = Shader.PropertyToID("_BackDepthEnabled");
    private static readonly int _PrevInvViewProjMatrix = Shader.PropertyToID("_PrevInvViewProjMatrix");
    private static readonly int _PrevInvViewProjMatrixStereo = Shader.PropertyToID("_PrevInvViewProjMatrixStereo");
    private static readonly int _PrevCameraPositionWS = Shader.PropertyToID("_PrevCameraPositionWS");
    private static readonly int _PixelSpreadAngleTangent = Shader.PropertyToID("_PixelSpreadAngleTangent");
    private static readonly int _HistoryTextureValid = Shader.PropertyToID("_HistoryTextureValid");
    private static readonly int _IndirectDiffuseLightingMultiplier = Shader.PropertyToID("_IndirectDiffuseLightingMultiplier");
    private static readonly int _IndirectDiffuseRenderingLayers = Shader.PropertyToID("_IndirectDiffuseRenderingLayers");
    private static readonly int _AggressiveDenoise = Shader.PropertyToID("_AggressiveDenoise");
    private static readonly int _ReBlurBlurRotator = Shader.PropertyToID("_ReBlurBlurRotator");
    private static readonly int _ReBlurDenoiserRadius = Shader.PropertyToID("_ReBlurDenoiserRadius");
    private static readonly int _SSGIDebugView = Shader.PropertyToID("_SSGIDebugView");
    private static readonly int _OverrideAmbientLightingId = Shader.PropertyToID("_OverrideAmbientLighting");
    private static readonly int _SSGIGBufferFallback = Shader.PropertyToID("_SSGIGBufferFallback");
    private static readonly int _SSGIFallbackAlbedo = Shader.PropertyToID("_SSGIFallbackAlbedo");
    private static readonly int _SSGIBlueNoise = Shader.PropertyToID("_SSGIBlueNoise");

    private const string _CameraDepthTexture = "_CameraDepthTexture";
    private const string _IndirectDiffuseTexture = "_IndirectDiffuseTexture";
    private const string _IndirectDiffuseTexture0 = _IndirectDiffuseTexture + "0";
    private const string _IndirectDiffuseTexture1 = _IndirectDiffuseTexture + "1";
    private const string _IntermediateIndirectDiffuseTexture = "_IntermediateIndirectDiffuseTexture";
    private const string _IntermediateCameraColorTexture = "_IntermediateCameraColorTexture";
    private const string _SSGIDepthTexture = "_SSGIDepthTexture";
    private const string _SSGIDepthTexture0 = _SSGIDepthTexture + "0";
    private const string _SSGIDepthTexture1 = _SSGIDepthTexture + "1";
    private const string _SSGIHistoryDepthTexture = "_SSGIHistoryDepthTexture";
    private const string _SSGINormalTexture = "_SSGINormalTexture";
    private const string _CameraBackDepthTexture = "_CameraBackDepthTexture";
    private const string _CameraBackOpaqueTexture = "_CameraBackOpaqueTexture";
    private const string _HistoryIndirectDiffuseTexture = "_HistoryIndirectDiffuseTexture";
    private const string _SSGISampleTexture = "_SSGISampleTexture";
    private const string _SSGISampleTexture0 = _SSGISampleTexture + "0";
    private const string _SSGISampleTexture1 = _SSGISampleTexture + "1";
    private const string _SSGIHistorySampleTexture = "_SSGIHistorySampleTexture";
    private const string _SSGIHistoryCameraColorTexture = "_SSGIHistoryCameraColorTexture";
    private const string _SSGIAmbientLightingTexture = "_SSGIAmbientLightingTexture";

    private static readonly int cameraDepthTexture = Shader.PropertyToID(_CameraDepthTexture);
    private static readonly int indirectDiffuseTexture = Shader.PropertyToID(_IndirectDiffuseTexture);
    //private static readonly int intermediateIndirectDiffuseTexture = Shader.PropertyToID(_IntermediateIndirectDiffuseTexture);
    //private static readonly int intermediateCameraColorTexture = Shader.PropertyToID(_IntermediateCameraColorTexture);
    private static readonly int ssgiDepthTexture = Shader.PropertyToID(_SSGIDepthTexture);
    private static readonly int ssgiHistoryDepthTexture = Shader.PropertyToID(_SSGIHistoryDepthTexture);
    private static readonly int ssgiNormalTexture = Shader.PropertyToID(_SSGINormalTexture);
    private static readonly int cameraBackDepthTexture = Shader.PropertyToID(_CameraBackDepthTexture);
    private static readonly int cameraBackOpaqueTexture = Shader.PropertyToID(_CameraBackOpaqueTexture);
    private static readonly int historyIndirectDiffuseTexture = Shader.PropertyToID(_HistoryIndirectDiffuseTexture);
    private static readonly int ssgiHistorySampleTexture = Shader.PropertyToID(_SSGIHistorySampleTexture);
    private static readonly int ssgiHistoryCameraColorTexture = Shader.PropertyToID(_SSGIHistoryCameraColorTexture);
    private static readonly int ssgiAmbientLightingTexture = Shader.PropertyToID(_SSGIAmbientLightingTexture);

    private const string _GBuffer0 = "_GBuffer0";
    private const string _GBuffer1 = "_GBuffer1";
    private const string _GBuffer2 = "_GBuffer2";
    private const string _GBufferDepth = "_GBufferDepthTexture";

    private static readonly int gBuffer0 = Shader.PropertyToID(_GBuffer0);
    private static readonly int gBuffer1 = Shader.PropertyToID(_GBuffer1);
    private static readonly int gBuffer2 = Shader.PropertyToID(_GBuffer2);
    private static readonly int gBufferDepth = Shader.PropertyToID(_GBufferDepth);

    private static readonly int specCube0 = Shader.PropertyToID("_SpecCube0");
    private static readonly int specCube0_HDR = Shader.PropertyToID("_SpecCube0_HDR");
    private static readonly int specCube0_BoxMin = Shader.PropertyToID("_SpecCube0_BoxMin");
    private static readonly int specCube0_BoxMax = Shader.PropertyToID("_SpecCube0_BoxMax");
    private static readonly int specCube0_ProbePosition = Shader.PropertyToID("_SpecCube0_ProbePosition");
    private static readonly int probeWeight = Shader.PropertyToID("_ProbeWeight");
    private static readonly int probeSet = Shader.PropertyToID("_ProbeSet");

    private static readonly int downSample = Shader.PropertyToID("_DownSample");
    private static readonly int frameIndex = Shader.PropertyToID("_FrameIndex");

    // unity_SH is not available when performing full screen blit pass
    private static readonly int shAr = Shader.PropertyToID("ssgi_SHAr");
    private static readonly int shAg = Shader.PropertyToID("ssgi_SHAg");
    private static readonly int shAb = Shader.PropertyToID("ssgi_SHAb");
    private static readonly int shBr = Shader.PropertyToID("ssgi_SHBr");
    private static readonly int shBg = Shader.PropertyToID("ssgi_SHBg");
    private static readonly int shBb = Shader.PropertyToID("ssgi_SHBb");
    private static readonly int shC = Shader.PropertyToID("ssgi_SHC");

    // Local Keywords
    private const string _FP_REFL_PROBE_ATLAS = "_FP_REFL_PROBE_ATLAS";
    private const string _RAYMARCHING_FALLBACK_SKY = "_RAYMARCHING_FALLBACK_SKY";
    private const string _RAYMARCHING_FALLBACK_REFLECTION_PROBES = "_RAYMARCHING_FALLBACK_REFLECTION_PROBES";
    private const string _BACKFACE_TEXTURES = "_BACKFACE_TEXTURES";
    private const string _FORWARD_PLUS = "_FORWARD_PLUS";
#if UNITY_6000_1_OR_NEWER
    private const string _CLUSTER_LIGHT_LOOP = "_CLUSTER_LIGHT_LOOP";
    private const string _REFLECTION_PROBE_ATLAS = "_REFLECTION_PROBE_ATLAS";
#endif
    private const string _WRITE_RENDERING_LAYERS = "_WRITE_RENDERING_LAYERS";
    private const string _USE_RENDERING_LAYERS = "_USE_RENDERING_LAYERS";
    private const string _DEPTH_NORMALS_UPSCALE = "_DEPTH_NORMALS_UPSCALE";
    private const string PROBE_VOLUMES_L1 = "PROBE_VOLUMES_L1";
    private const string PROBE_VOLUMES_L2 = "PROBE_VOLUMES_L2";
    private const string _APV_LIGHTING_BUFFER = "_APV_LIGHTING_BUFFER";

    // Global Keywords
    private const string SSGI_RENDER_GBUFFER = "SSGI_RENDER_GBUFFER";
    private const string SSGI_RENDER_BACKFACE_DEPTH = "SSGI_RENDER_BACKFACE_DEPTH";
    private const string SSGI_RENDER_BACKFACE_COLOR = "SSGI_RENDER_BACKFACE_COLOR";

    // From "SSGIDenoise.hlsl"
    private const float k_BlurMaxRadius = 0.04f;

    private static readonly Vector4 m_ScaleBias = new Vector4(1.0f, 1.0f, 0.0f, 0.0f);

    public override void Create()
    {
        if (m_Shader != Shader.Find(m_SSGIShaderName))
        {
        #if UNITY_EDITOR || DEBUG
            Debug.LogErrorFormat("Screen Space Global Illumination URP: Material is not using {0} shader.", m_SSGIShaderName);
            isShaderMismatchLogPrinted = true;
        #endif
            return;
        }
        else
        {
            isShaderMismatchLogPrinted = false;
        }

        m_SSGIMaterial = CoreUtils.CreateEngineMaterial(m_Shader);
        m_SSGIMaterial.SetTexture(_SSGIBlueNoise, ScreenSpaceGlobalIlluminationBlueNoise.Texture);

        if (m_PreRenderSSGIPass == null)
        {
            m_PreRenderSSGIPass = new PreRenderScreenSpaceGlobalIlluminationPass();
        #if UNITY_6000_0_OR_NEWER
            m_PreRenderSSGIPass.renderPassEvent = RenderPassEvent.BeforeRenderingPrePasses;
        #else
            m_PreRenderSSGIPass.renderPassEvent = RenderPassEvent.BeforeRenderingTransparents - 1;
        #endif
        }

        if (m_SSGIPass == null)
        {
            m_SSGIPass = new ScreenSpaceGlobalIlluminationPass(m_SSGIMaterial);
        #if URP_RENDER_GRAPH_ONLY
            // The compatibility mode (and its settings) no longer exist.
            m_SSGIPass.renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
        #elif UNITY_6000_0_OR_NEWER
            bool enableRenderGraph = !GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>().enableRenderCompatibilityMode;
            m_SSGIPass.renderPassEvent = enableRenderGraph ? RenderPassEvent.AfterRenderingSkybox : RenderPassEvent.BeforeRenderingTransparents;
        #else
            m_SSGIPass.renderPassEvent = RenderPassEvent.BeforeRenderingTransparents; // We cannot move to after skybox because of the motion vectors issue
        #endif
        }
        m_SSGIPass.m_SSGIMaterial = m_SSGIMaterial;

        if (m_BackfaceDataPass == null)
        {
            m_BackfaceDataPass = new BackfaceDataPass();
            m_BackfaceDataPass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques - 1;
        }

        if (m_ForwardGBufferPass == null)
        {
            m_ForwardGBufferPass = new ForwardGBufferPass(m_GBufferPassNames);
            // Set this to "After Opaques" so that we can enable GBuffers Depth Priming on non-GL platforms.
            m_ForwardGBufferPass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
        }

        GBufferOverrideRenderingLayerMask = 1u << Mathf.Clamp(m_GBufferOverrideRenderingLayerBit, 1, 31);
        m_ForwardGBufferPass.overrideShader = m_GBufferOverrideShader;
        m_ForwardGBufferPass.overrideRenderingLayerMask = GBufferOverrideRenderingLayerMask;
    }

    protected override void Dispose(bool disposing)
    {
        if (m_PreRenderSSGIPass != null)
            m_PreRenderSSGIPass.Dispose();
        
        if (m_SSGIPass != null)
            m_SSGIPass.Dispose();

        if (m_BackfaceDataPass != null)
        {
            // Turn off accurate thickness since the render pass is disabled.
            if (m_SSGIMaterial != null) { m_SSGIMaterial.SetFloat(_BackDepthEnabled, 0.0f); }
            m_BackfaceDataPass.Dispose();
        }

        if (m_ForwardGBufferPass != null)
            m_ForwardGBufferPass.Dispose();

        if (m_SSGIMaterial != null)
            CoreUtils.Destroy(m_SSGIMaterial);

        DisableGlobalKeywords();
    }

    /// <summary>
    /// Clears the global keywords the effect turns on while it renders. They are only ever written from
    /// AddRenderPasses, so a feature that is switched off never reaches the branch that clears them and
    /// every shader in the scene keeps rendering its GBuffer and backface variants for nothing.
    /// </summary>
    public static void DisableGlobalKeywords()
    {
        Shader.DisableKeyword(SSGI_RENDER_GBUFFER);
        Shader.DisableKeyword(SSGI_RENDER_BACKFACE_DEPTH);
        Shader.DisableKeyword(SSGI_RENDER_BACKFACE_COLOR);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        // Do not add render passes if any error occurs.
        if (isShaderMismatchLogPrinted)
            return;

        Camera currentCamera = renderingData.cameraData.camera;

        if (currentCamera.cameraType == CameraType.Preview)
            return;

        if (CameraFilter != null && !CameraFilter(currentCamera))
            return;

        var stack = VolumeManager.instance.stack;
        ScreenSpaceGlobalIlluminationVolume ssgiVolume = stack.GetComponent<ScreenSpaceGlobalIlluminationVolume>();
        if (ssgiVolume == null || !ssgiVolume.IsActive())
            return;

        bool isDebugger = DebugManager.instance.isAnyDebugUIActive && !KeepRenderingWithDebugger;
        bool shouldDisable = !m_ReflectionProbes && currentCamera.cameraType == CameraType.Reflection;
        shouldDisable |= ssgiVolume.indirectDiffuseLightingMultiplier.value == 0.0f && !m_OverrideAmbientLighting;
        shouldDisable |= renderingData.cameraData.renderType == CameraRenderType.Overlay;

        if (shouldDisable)
            return;

    #if UNITY_EDITOR || DEBUG
        if (isDebugger && !m_RenderingDebugger)
        {
            if (!isDebuggerLogPrinted) { Debug.Log("Screen Space Global Illumination URP: Disable effect to avoid affecting rendering debugging."); isDebuggerLogPrinted = true; }
        }
        else
            isDebuggerLogPrinted = false;
    #endif

        ComputeRayMarchingSteps(ssgiVolume.maxRaySteps.value, out bool lowStepCount, out int smallSteps, out int mediumSteps);

        float resolutionScale = ssgiVolume.fullResolutionSS.value ? 1.0f : ssgiVolume.resolutionScaleSS.value;
        float temporalIntensity = ComputeTemporalIntensity(ssgiVolume.denoiseIntensitySS.value, resolutionScale);

        // TODO: Expose more settings
        m_SSGIMaterial.SetFloat(_MaxSteps, ssgiVolume.maxRaySteps.value);
        m_SSGIMaterial.SetFloat(_MaxSmallSteps, smallSteps);
        m_SSGIMaterial.SetFloat(_MaxMediumSteps, mediumSteps);
        m_SSGIMaterial.SetFloat(_StepSize, lowStepCount ? 0.5f : 0.4f);
        m_SSGIMaterial.SetFloat(_SmallStepSize, smallSteps < 4 ? 0.05f : 0.015f);
        m_SSGIMaterial.SetFloat(_MediumStepSize, lowStepCount ? 0.1f : 0.05f);
        m_SSGIMaterial.SetFloat(_Thickness, ssgiVolume.depthBufferThickness.value);
        m_SSGIMaterial.SetFloat(_Thickness_Increment, ssgiVolume.depthBufferThickness.value * 0.25f);
        m_SSGIMaterial.SetFloat(_RayCount, ssgiVolume.sampleCount.value);
        m_SSGIMaterial.SetFloat(_TemporalIntensity, temporalIntensity);
        m_SSGIMaterial.SetFloat(_ReBlurDenoiserRadius, ssgiVolume.denoiserRadiusSS.value * 2.0f * k_BlurMaxRadius); // Optimized for roughness = 1.0
        m_SSGIMaterial.SetFloat(_IndirectDiffuseLightingMultiplier, ssgiVolume.indirectDiffuseLightingMultiplier.value);
        m_SSGIMaterial.SetFloat(_MaxBrightness, 7.0f);
        m_SSGIMaterial.SetFloat(_AggressiveDenoise, ssgiVolume.denoiserAlgorithmSS.value == ScreenSpaceGlobalIlluminationVolume.DenoiserAlgorithm.Aggressive ? 1.0f : 0.0f);
        m_SSGIMaterial.SetFloat(_SSGIDebugView, (float)DebugView);
        m_SSGIMaterial.SetFloat(_OverrideAmbientLightingId, m_OverrideAmbientLighting ? 1.0f : 0.0f);
        m_SSGIMaterial.SetFloat(_SSGIGBufferFallback, m_GBufferFallback ? 1.0f : 0.0f);
        m_SSGIMaterial.SetFloat(_SSGIFallbackAlbedo, m_FallbackAlbedo);

        // Depth and motion vectors only. The GBuffer fallback reconstructs normals from depth on purpose: asking for the
        // normals texture switches URP to a depth normals prepass, which cannot target an MSAA depth attachment under
        // forced depth priming (Render Graph "Mismatch in number of MSAA samples" on '_CameraNormalsTexture').
        m_SSGIPass.ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Motion);

    #if UNITY_2023_3_OR_NEWER
        bool enableRenderingLayers = Shader.IsKeywordEnabled(_WRITE_RENDERING_LAYERS) && ssgiVolume.indirectDiffuseRenderingLayers.value.value != 0xFFFF;
        if (enableRenderingLayers)
        {
            m_SSGIMaterial.EnableKeyword(_USE_RENDERING_LAYERS);
            m_SSGIMaterial.SetInteger(_IndirectDiffuseRenderingLayers, (int)ssgiVolume.indirectDiffuseRenderingLayers.value.value);
        }
        else
        m_SSGIMaterial.DisableKeyword(_USE_RENDERING_LAYERS);
    #else
        bool enableRenderingLayers = false;
        m_SSGIMaterial.DisableKeyword(_USE_RENDERING_LAYERS);
    #endif

        m_SSGIPass.ssgiVolume = ssgiVolume;
        m_SSGIPass.enableRenderingLayers = enableRenderingLayers;

        bool skyFallback = ssgiVolume.IsFallbackSky();
        if (skyFallback) { m_SSGIMaterial.EnableKeyword(_RAYMARCHING_FALLBACK_SKY); }
        else { m_SSGIMaterial.DisableKeyword(_RAYMARCHING_FALLBACK_SKY); }

    #if UNITY_2023_1_OR_NEWER
        // Missed rays then read the per-pixel adaptive probe volume lighting the copy pass wrote instead of sampling the volume per ray.
        bool useAPVLightingBuffer = m_OverrideAmbientLighting && skyFallback && (Shader.IsKeywordEnabled(PROBE_VOLUMES_L1) || Shader.IsKeywordEnabled(PROBE_VOLUMES_L2));
        if (useAPVLightingBuffer) { m_SSGIMaterial.EnableKeyword(_APV_LIGHTING_BUFFER); }
        else { m_SSGIMaterial.DisableKeyword(_APV_LIGHTING_BUFFER); }
    #else
        // APV is not supported on URP 14
        m_SSGIMaterial.DisableKeyword(_APV_LIGHTING_BUFFER);
    #endif
        bool reflectionProbesFallback = ssgiVolume.IsFallbackReflectionProbes();
        if (reflectionProbesFallback){ m_SSGIMaterial.EnableKeyword(_RAYMARCHING_FALLBACK_REFLECTION_PROBES); }
        else { m_SSGIMaterial.DisableKeyword(_RAYMARCHING_FALLBACK_REFLECTION_PROBES); }
        
    #if UNITY_6000_1_OR_NEWER
        bool hasProbeAtlas = Shader.IsKeywordEnabled(_CLUSTER_LIGHT_LOOP) && Shader.IsKeywordEnabled(_REFLECTION_PROBE_ATLAS);
    #else
        bool hasProbeAtlas = Shader.IsKeywordEnabled(_FORWARD_PLUS);
    #endif
        if (hasProbeAtlas && reflectionProbesFallback) { m_SSGIMaterial.EnableKeyword(_FP_REFL_PROBE_ATLAS); } // TODO: change to URP's keyword
        else { m_SSGIMaterial.DisableKeyword(_FP_REFL_PROBE_ATLAS); }
        m_SSGIPass.hasProbeAtlas = hasProbeAtlas;

        bool isReflectionProbe = renderingData.cameraData.camera.cameraType == CameraType.Reflection;
        m_SSGIMaterial.SetFloat(_IsProbeCamera, isReflectionProbe ? 1.0f : 0.0f);

        if (m_HighQualityUpscaling)
            m_SSGIMaterial.EnableKeyword(_DEPTH_NORMALS_UPSCALE);
        else
            m_SSGIMaterial.DisableKeyword(_DEPTH_NORMALS_UPSCALE);

    #if UNITY_EDITOR
        // [Editor Only] Motion vectors in scene view don't get updated each frame when not entering play mode.
        // So we manually set them in a pass before rendering motion vectors
        if (renderingData.cameraData.camera.cameraType == CameraType.SceneView)
        {
            m_PreRenderSSGIPass.m_SSGIMaterial = m_SSGIMaterial;
            renderer.EnqueuePass(m_PreRenderSSGIPass);
        }
            
    #endif

        if (renderingData.cameraData.camera.cameraType != CameraType.Preview && (!isDebugger || m_RenderingDebugger))
            renderer.EnqueuePass(m_SSGIPass);

        // For Unity 6.1+:
        // TODO: the following code will cause issues when using "Deferred+" and disabling Render Graph (URP will fall back to "Forward+")
        // Solution: when using "Deferred+" & "RG Compatibility Mode", we should enqueue the Forward GBuffer pass

        // If GBuffer exists, URP is in Deferred path. (Actual rendering mode can be different from settings, such as URP forces Forward on OpenGL)
        bool isUsingDeferred = gBufferFieldInfo.GetValue(renderer) != null;
        // OpenGL won't use deferred path.
        isUsingDeferred &= (SystemInfo.graphicsDeviceType != GraphicsDeviceType.OpenGLES3) & (SystemInfo.graphicsDeviceType != GraphicsDeviceType.OpenGLCore);  // GLES 2 is deprecated.

        bool renderBackfaceData = ssgiVolume.thicknessMode.value != ScreenSpaceGlobalIlluminationVolume.ThicknessMode.Constant;
        if (renderBackfaceData)
        {
            // Backface lighting is only supported on Forward(+) rendering path.
            bool supportBackfaceLighting = m_BackfaceLighting && !isUsingDeferred;
            m_BackfaceDataPass.backfaceLighting = supportBackfaceLighting;

            renderer.EnqueuePass(m_BackfaceDataPass);

            m_SSGIMaterial.EnableKeyword(_BACKFACE_TEXTURES);
            Shader.EnableKeyword(SSGI_RENDER_BACKFACE_DEPTH);
            if (supportBackfaceLighting)
            {
                m_SSGIMaterial.SetFloat(_BackDepthEnabled, 2.0f); // Depth + Color
                Shader.EnableKeyword(SSGI_RENDER_BACKFACE_COLOR);
            }
            else
            {
                m_SSGIMaterial.SetFloat(_BackDepthEnabled, 1.0f); // Depth
                Shader.DisableKeyword(SSGI_RENDER_BACKFACE_COLOR);
            }
        }
        else
        {
            m_SSGIMaterial.DisableKeyword(_BACKFACE_TEXTURES);
            Shader.DisableKeyword(SSGI_RENDER_BACKFACE_DEPTH);
            Shader.DisableKeyword(SSGI_RENDER_BACKFACE_COLOR);
            m_SSGIMaterial.SetFloat(_BackDepthEnabled, 0.0f);
        }

    #if UNITY_EDITOR || DEBUG
        if (m_BackfaceLighting && isUsingDeferred)
        {
            if (!isBackfaceLightingLogPrinted) { Debug.LogError("Screen Space Global Illumination URP: Backface Lighting is only supported on Forward(+) rendering path."); isBackfaceLightingLogPrinted = true; }
        }
        else
            isBackfaceLightingLogPrinted = false;
    #endif

        // Render Forward GBuffer pass if the current device supports MRT.
        // Assuming the current device supports at least 4 MRTs since we require Unity shader model 3.5
        if (!isUsingDeferred)
        {
            renderer.EnqueuePass(m_ForwardGBufferPass);
            Shader.EnableKeyword(SSGI_RENDER_GBUFFER);
        }
        else
        {
            Shader.DisableKeyword(SSGI_RENDER_GBUFFER);
        }
    }
    public class PreRenderScreenSpaceGlobalIlluminationPass : ScriptableRenderPass
    {
        /// Motion vectors may not render correctly in the scene view
        /// This pass is used to "fix" camera motion vectors to improve scene view denoising

        private const string m_ProfilerTag = "Prepare Screen Space Global Illumination";

        public Material m_SSGIMaterial;

        private Matrix4x4 camVPMatrix;
        private Matrix4x4 prevCamVPMatrix;
        private bool hasPrevCamVPMatrix;

        // This pass is editor only
        const string _PrevViewProjMatrix = "_PrevViewProjMatrix";
        const string _NonJitteredViewProjMatrix = "_NonJitteredViewProjMatrix";
        public PreRenderScreenSpaceGlobalIlluminationPass() { }


    #if UNITY_6000_0_OR_NEWER
        #region Render Graph Pass
        // This class stores the data needed by the pass, passed as parameter to the delegate function that executes the pass
        private class PassData
        {
            internal Matrix4x4 prevCamVPMatrix;
            internal Matrix4x4 camVPMatrix;
        }

        // This static method is used to execute the pass and passed as the RenderFunc delegate to the RenderGraph render pass
        static void ExecutePass(PassData data, UnsafeGraphContext context)
        {
            CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

            // Fix scene view motion vectors
            cmd.SetGlobalMatrix(_PrevViewProjMatrix, data.prevCamVPMatrix);
            cmd.SetGlobalMatrix(_NonJitteredViewProjMatrix, data.camVPMatrix);
        }

        // This is where the renderGraph handle can be accessed.
        // Each ScriptableRenderPass can use the RenderGraph handle to add multiple render passes to the render graph
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            // add an unsafe render pass to the render graph, specifying the name and the data type that will be passed to the ExecutePass function
            using (var builder = renderGraph.AddUnsafePass<PassData>(m_ProfilerTag, out var passData))
            {
                // UniversalResourceData contains all the texture handles used by the renderer, including the active color and depth textures
                // The active color and depth textures are the main color and depth buffers that the camera renders into
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

                var camera = cameraData.camera;
                camVPMatrix = GL.GetGPUProjectionMatrix(camera.nonJitteredProjectionMatrix, true) * cameraData.GetViewMatrix();
                passData.camVPMatrix = camVPMatrix;
                passData.prevCamVPMatrix = hasPrevCamVPMatrix ? prevCamVPMatrix : camera.previousViewProjectionMatrix;
                prevCamVPMatrix = camVPMatrix;
                hasPrevCamVPMatrix = true;

                // This pass is editor only
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);

                // Assign the ExecutePass function to the render pass delegate, which will be called by the render graph when executing the pass
                builder.SetRenderFunc((PassData data, UnsafeGraphContext context) => ExecutePass(data, context));
            }
        }
        #endregion
    #endif

        #region Shared
        public void Dispose()
        {

        }
        #endregion
    }

    public class ScreenSpaceGlobalIlluminationPass : ScriptableRenderPass
    {
        private const string m_ProfilerTag = "Screen Space Global Illumination";

        public ScreenSpaceGlobalIlluminationVolume ssgiVolume;
        public bool enableRenderingLayers;
        public bool hasProbeAtlas;
        public Material m_SSGIMaterial;

        // The persistent history textures live in "CameraHistoryData" so every camera (and every eye in multi-pass XR) reprojects from its own history.
        // Each history is a pair of textures: the pass reads last frame's and writes this frame's, then the roles swap, so nothing is copied.

        private readonly RenderTargetIdentifier[] rTHandles = new RenderTargetIdentifier[2];
        private readonly RenderTargetIdentifier[] rTHandles3 = new RenderTargetIdentifier[3];
        private readonly Matrix4x4[] prevInvViewProjMatrices = new Matrix4x4[2];

        private bool enableDenoise;
        private int frameCount = 0;
        private float resolutionScale = 1.0f;

        // The blue noise sequence in the shader repeats after this many frames.
        internal const int NoiseFrameCount = 64;

        public static readonly float[] k_PreBlurRands = new float[] { 0.840188f, 0.394383f, 0.783099f, 0.79844f, 0.911647f, 0.197551f, 0.335223f, 0.76823f, 0.277775f, 0.55397f, 0.477397f, 0.628871f, 0.364784f, 0.513401f, 0.95223f, 0.916195f, 0.635712f, 0.717297f, 0.141603f, 0.606969f, 0.0163006f, 0.242887f, 0.137232f, 0.804177f, 0.156679f, 0.400944f, 0.12979f, 0.108809f, 0.998924f, 0.218257f, 0.512932f, 0.839112f };
        public static readonly float[] k_BlurRands = new float[] { 0.61264f, 0.296032f, 0.637552f, 0.524287f, 0.493583f, 0.972775f, 0.292517f, 0.771358f, 0.526745f, 0.769914f, 0.400229f, 0.891529f, 0.283315f, 0.352458f, 0.807725f, 0.919026f, 0.0697553f, 0.949327f, 0.525995f, 0.0860558f, 0.192214f, 0.663227f, 0.890233f, 0.348893f, 0.0641713f, 0.020023f, 0.457702f, 0.0630958f, 0.23828f, 0.970634f, 0.902208f, 0.85092f };
        public static readonly float[] k_PostBlurRands = new float[] { 0.266666f, 0.53976f, 0.375207f, 0.760249f, 0.512535f, 0.667724f, 0.531606f, 0.0392803f, 0.437638f, 0.931835f, 0.93081f, 0.720952f, 0.284293f, 0.738534f, 0.639979f, 0.354049f, 0.687861f, 0.165974f, 0.440105f, 0.880075f, 0.829201f, 0.330337f, 0.228968f, 0.893372f, 0.35036f, 0.68667f, 0.956468f, 0.58864f, 0.657304f, 0.858676f, 0.43956f, 0.92397f };

        public ScreenSpaceGlobalIlluminationPass(Material material)
        {
            m_SSGIMaterial = material;

            // URP gathers pass inputs before the first RecordRenderGraph / OnCameraSetup, so declare them up front.
            ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Motion);
        #if UNITY_6000_0_OR_NEWER
            requiresIntermediateTexture = true;
        #endif
        }


    #if UNITY_6000_0_OR_NEWER
        #region Render Graph Pass
        // This class stores the data needed by the pass, passed as parameter to the delegate function that executes the pass
        private class PassData
        {
            internal Material ssgiMaterial;

            internal RenderTargetIdentifier[] rTHandles;
            internal RenderTargetIdentifier[] rTHandles3;

            // Camera color & direct lighting color
            internal TextureHandle cameraColorTargetHandle;
            internal TextureHandle cameraDepthTextureHandle;

            internal TextureHandle intermediateCameraColorHandle;
            internal TextureHandle historyCameraColorHandle;
            internal TextureHandle ambientLightingHandle;

            // SSGI diffuse lighting: this frame's result, which is next frame's history
            internal TextureHandle diffuseHandle;
            internal TextureHandle intermediateDiffuseHandle;

            // Denoising
            internal TextureHandle historyDiffuseHandle;
            internal TextureHandle normalHandle;
            internal TextureHandle depthHandle;
            internal TextureHandle historyDepthHandle;
            internal TextureHandle accumulateSampleHandle;
            internal TextureHandle accumulateHistorySampleHandle;

            // GBuffers created by URP
            internal bool localGBuffers;
            internal TextureHandle gBuffer0Handle;
            internal TextureHandle gBuffer1Handle;
            internal TextureHandle gBuffer2Handle;

            internal bool denoise;
            internal bool secondDenoise;
            internal bool aggressiveDenoise;
            internal bool historyValid;
        }

        // This static method is used to execute the pass and passed as the RenderFunc delegate to the RenderGraph render pass
        static void ExecutePass(PassData data, UnsafeGraphContext context)
        {
            CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

            data.ssgiMaterial.SetTexture(cameraDepthTexture, data.cameraDepthTextureHandle);

            if (data.localGBuffers)
            {
                data.ssgiMaterial.SetTexture(gBuffer0, data.gBuffer0Handle);
                data.ssgiMaterial.SetTexture(gBuffer1, data.gBuffer1Handle);
                data.ssgiMaterial.SetTexture(gBuffer2, data.gBuffer2Handle);
            }
            else
            {
                // Global gbuffer textures
                data.ssgiMaterial.SetTexture(gBuffer0, null);
                data.ssgiMaterial.SetTexture(gBuffer1, null);
                data.ssgiMaterial.SetTexture(gBuffer2, null);
            }

            // Without a history, ray hits read this frame's colour and the temporal passes start from cleared data.
            if (!data.historyValid)
            {
                data.ssgiMaterial.SetTexture(ssgiHistoryCameraColorTexture, data.cameraColorTargetHandle);
                if (data.denoise)
                {
                    CoreUtils.SetRenderTarget(cmd, data.historyDiffuseHandle, ClearFlag.Color, Color.clear);
                    CoreUtils.SetRenderTarget(cmd, data.accumulateHistorySampleHandle, ClearFlag.Color, Color.clear);
                }
            }

            // RT-1: camera colour
            // RT-2: ambient lighting at each pixel
            data.rTHandles[0] = data.intermediateCameraColorHandle;
            data.rTHandles[1] = data.ambientLightingHandle;
            SetRenderTargets(cmd, data.rTHandles, data.intermediateCameraColorHandle);
            Blitter.BlitTexture(cmd, data.cameraColorTargetHandle, m_ScaleBias, data.ssgiMaterial, pass: 0);
            data.ssgiMaterial.SetTexture(ssgiAmbientLightingTexture, data.ambientLightingHandle);

            // Depth at the traced resolution: read by the denoisers this frame and by the reprojection next frame.
            Blitter.BlitCameraTexture(cmd, data.depthHandle, data.depthHandle, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, data.ssgiMaterial, pass: 5);

            if (data.denoise)
            {
                data.ssgiMaterial.SetTexture(ssgiNormalTexture, data.normalHandle);

                // Render SSGI
                Blitter.BlitCameraTexture(cmd, data.intermediateCameraColorHandle, data.intermediateDiffuseHandle, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, data.ssgiMaterial, pass: 1);

                // Reproject GI
                data.rTHandles3[0] = data.diffuseHandle;
                data.rTHandles3[1] = data.accumulateSampleHandle;
                data.rTHandles3[2] = data.normalHandle;
                // RT-1: accumulated results
                // RT-2: accumulated sample count
                // RT-3: normals at the traced resolution, for the denoisers
                SetRenderTargets(cmd, data.rTHandles3, data.accumulateSampleHandle);
                Blitter.BlitTexture(cmd, data.intermediateDiffuseHandle, m_ScaleBias, data.ssgiMaterial, pass: 2);

                if (data.aggressiveDenoise)
                {
                    Blitter.BlitCameraTexture(cmd, data.diffuseHandle, data.intermediateDiffuseHandle, data.ssgiMaterial, pass: 8);
                    Blitter.BlitCameraTexture(cmd, data.intermediateDiffuseHandle, data.diffuseHandle, data.ssgiMaterial, pass: 8);
                }

                if (data.secondDenoise)
                {
                    Blitter.BlitCameraTexture(cmd, data.diffuseHandle, data.intermediateDiffuseHandle, data.ssgiMaterial, pass: 3);
                    Blitter.BlitCameraTexture(cmd, data.intermediateDiffuseHandle, data.diffuseHandle, data.ssgiMaterial, pass: 4);
                }
            }
            else
            {
                // SSGI
                Blitter.BlitCameraTexture(cmd, data.intermediateCameraColorHandle, data.diffuseHandle, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, data.ssgiMaterial, pass: 1);
            }

            // Combine: multiply the camera colour by the ambient removal factor, then add the bounce.
            // Both are blended onto the camera target sample by sample, which keeps MSAA edges intact.
            Blitter.BlitCameraTexture(cmd, data.intermediateCameraColorHandle, data.cameraColorTargetHandle, data.ssgiMaterial, pass: 6);
            Blitter.BlitCameraTexture(cmd, data.intermediateCameraColorHandle, data.cameraColorTargetHandle, data.ssgiMaterial, pass: 10);

            // Copy History Scene Color
            Blitter.BlitCameraTexture(cmd, data.cameraColorTargetHandle, data.historyCameraColorHandle, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, data.ssgiMaterial, pass: 9);
        }

        // This is where the renderGraph handle can be accessed.
        // Each ScriptableRenderPass can use the RenderGraph handle to add multiple render passes to the render graph
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            // add an unsafe render pass to the render graph, specifying the name and the data type that will be passed to the ExecutePass function
            using (var builder = renderGraph.AddUnsafePass<PassData>(m_ProfilerTag, out var passData))
            {
                // UniversalResourceData contains all the texture handles used by the renderer, including the active color and depth textures
                // The active color and depth textures are the main color and depth buffers that the camera renders into
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();

                var visibleReflectionProbes = renderingData.cullResults.visibleReflectionProbes;
                var camera = cameraData.camera;

                int currentCameraHash = ComputeCameraHistoryHash(camera, cameraData.xr);
                int cameraHistoryIndex = GetCameraHistoryDataIndex(currentCameraHash);

                if (!hasProbeAtlas)
                    UpdateReflectionProbe(visibleReflectionProbes, camera.transform.position);
                else
                    m_SSGIMaterial.SetFloat(probeSet, 0.0f);

                m_SSGIMaterial.SetFloat(frameIndex, frameCount);
                m_SSGIMaterial.SetVector(_ReBlurBlurRotator, EvaluateRotator(k_BlurRands[frameCount % 32]));
                frameCount = (frameCount + 1) % NoiseFrameCount;

                // The camera target descriptor already includes the render scale and, in XR, the per-eye size and array layout.
                RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;
                int width = desc.width;
                int height = desc.height;

                bool denoiseStateChanged = ssgiVolume.denoiseSS.value != enableDenoise;
                bool resolutionStateChanged = ssgiVolume.fullResolutionSS.value ? resolutionScale != 1.0f : ssgiVolume.resolutionScaleSS.value != resolutionScale;
                bool cameraHasChanged = cameraHistoryIndex == -1;

                // Reorder the history data array when camera is new
                UpdateCameraHistoryData(cameraHasChanged);
                // Assign the data to index 0 for the new camera
                cameraHistoryIndex = cameraHasChanged ? 0 : cameraHistoryIndex;

                ref CameraHistoryData history = ref cameraHistoryData[cameraHistoryIndex];
                history.hash = currentCameraHash;

                bool xrEnabled = cameraData.xr != null && cameraData.xr.enabled;
                bool stereoInstanced = xrEnabled && cameraData.xr.singlePassEnabled && cameraData.xr.viewCount > 1;
                Matrix4x4 invViewProj0 = ComputeInverseViewProjection(cameraData.GetViewMatrix(0), xrEnabled ? cameraData.GetProjectionMatrix(0) : camera.projectionMatrix);
                Matrix4x4 invViewProj1 = stereoInstanced ? ComputeInverseViewProjection(cameraData.GetViewMatrix(1), cameraData.GetProjectionMatrix(1)) : invViewProj0;
                ApplyPreviousViewProjection(ref history, invViewProj0, invViewProj1, camera.transform.position);

                resolutionStateChanged |= (history.scaledWidth != width) || (history.scaledHeight != height);
                if (cameraHasChanged || denoiseStateChanged || resolutionStateChanged)
                    history.textureValid = false;

                history.scaledWidth = width;
                history.scaledHeight = height;

                resolutionScale = ssgiVolume.fullResolutionSS.value ? 1.0f : ssgiVolume.resolutionScaleSS.value;
                m_SSGIMaterial.SetFloat(downSample, resolutionScale);

                enableDenoise = ssgiVolume.denoiseSS.value;

                // The spread angle is used to compute the world space pixel footprint during denoising.
                // We use low FOV for orthographic cameras as a temporary solution.
                float fieldOfView = camera.orthographic ? 1.0f : (xrEnabled ? GetVerticalFieldOfView(cameraData.GetProjectionMatrix(0)) : camera.fieldOfView);
                m_SSGIMaterial.SetFloat(_PixelSpreadAngleTangent, ComputePixelSpreadAngleTangent(fieldOfView, width, height, resolutionScale));

                passData.denoise = enableDenoise;
                passData.secondDenoise = ssgiVolume.secondDenoiserPassSS.value;
                passData.aggressiveDenoise = ssgiVolume.denoiserAlgorithmSS.value == ScreenSpaceGlobalIlluminationVolume.DenoiserAlgorithm.Aggressive;

                // Avoid reprojecting from uninitialized history textures
                passData.historyValid = history.textureValid;
                m_SSGIMaterial.SetFloat(_HistoryTextureValid, history.textureValid ? 1.0f : 0.0f);
                history.textureValid = true;

                UploadAmbientProbe();

                desc.graphicsFormat = GraphicsFormat.B10G11R11_UFloatPack32;
                desc.depthBufferBits = 0; // Color and depth cannot be combined in RTHandles
                desc.stencilFormat = GraphicsFormat.None;
                desc.msaaSamples = 1;
                desc.bindMS = false;

                TextureHandle intermediateCameraColorHandle = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, name: _IntermediateCameraColorTexture, false, FilterMode.Point, TextureWrapMode.Clamp);
                TextureHandle ambientLightingHandle = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, name: _SSGIAmbientLightingTexture, false, FilterMode.Point, TextureWrapMode.Clamp);

                // Everything below is at the traced resolution.
                desc.width = Mathf.FloorToInt(desc.width * resolutionScale);
                desc.height = Mathf.FloorToInt(desc.height * resolutionScale);

                SelectHistory(ref history, out int write, out int read);

                desc.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
                TextureHandle intermediateDiffuseHandle = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, name: _IntermediateIndirectDiffuseTexture, false, FilterMode.Point, TextureWrapMode.Clamp);

                RenderingUtils.ReAllocateHandleIfNeeded(ref history.historyCameraColorHandle, desc, FilterMode.Point, TextureWrapMode.Clamp, name: _SSGIHistoryCameraColorTexture);
                m_SSGIMaterial.SetTexture(ssgiHistoryCameraColorTexture, history.historyCameraColorHandle);
                passData.historyCameraColorHandle = renderGraph.ImportTexture(history.historyCameraColorHandle);

                AllocateHistoryPair(ref history.indirectDiffuseHandle0, ref history.indirectDiffuseHandle1, desc, _IndirectDiffuseTexture0, _IndirectDiffuseTexture1);
                RTHandle diffuseWrite = write == 0 ? history.indirectDiffuseHandle0 : history.indirectDiffuseHandle1;
                RTHandle diffuseRead = read == 0 ? history.indirectDiffuseHandle0 : history.indirectDiffuseHandle1;
                m_SSGIMaterial.SetTexture(indirectDiffuseTexture, diffuseWrite);
                m_SSGIMaterial.SetTexture(historyIndirectDiffuseTexture, diffuseRead);
                passData.diffuseHandle = renderGraph.ImportTexture(diffuseWrite);
                passData.historyDiffuseHandle = renderGraph.ImportTexture(diffuseRead);

                desc.colorFormat = RenderTextureFormat.RFloat;
                AllocateHistoryPair(ref history.depthHandle0, ref history.depthHandle1, desc, _SSGIDepthTexture0, _SSGIDepthTexture1);
                RTHandle depthWrite = write == 0 ? history.depthHandle0 : history.depthHandle1;
                RTHandle depthRead = read == 0 ? history.depthHandle0 : history.depthHandle1;
                m_SSGIMaterial.SetTexture(ssgiDepthTexture, depthWrite);
                m_SSGIMaterial.SetTexture(ssgiHistoryDepthTexture, depthRead);
                passData.depthHandle = renderGraph.ImportTexture(depthWrite);
                passData.historyDepthHandle = renderGraph.ImportTexture(depthRead);

                desc.colorFormat = RenderTextureFormat.RHalf;
                AllocateHistoryPair(ref history.accumulateSampleHandle0, ref history.accumulateSampleHandle1, desc, _SSGISampleTexture0, _SSGISampleTexture1);
                RTHandle sampleWrite = write == 0 ? history.accumulateSampleHandle0 : history.accumulateSampleHandle1;
                RTHandle sampleRead = read == 0 ? history.accumulateSampleHandle0 : history.accumulateSampleHandle1;
                m_SSGIMaterial.SetTexture(ssgiHistorySampleTexture, sampleRead);
                passData.accumulateSampleHandle = renderGraph.ImportTexture(sampleWrite);
                passData.accumulateHistorySampleHandle = renderGraph.ImportTexture(sampleRead);

                if (enableDenoise)
                {
                    desc.graphicsFormat = ForwardGBufferPass.GBufferFormat(2);
                    passData.normalHandle = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, name: _SSGINormalTexture, false, FilterMode.Point, TextureWrapMode.Clamp);
                    builder.UseTexture(passData.normalHandle, AccessFlags.ReadWrite);
                }

                // Fill up the passData with the data needed by the pass
                passData.ssgiMaterial = m_SSGIMaterial;
                passData.cameraColorTargetHandle = resourceData.activeColorTexture;
                passData.cameraDepthTextureHandle = resourceData.cameraDepthTexture;
                passData.intermediateDiffuseHandle = intermediateDiffuseHandle;
                passData.intermediateCameraColorHandle = intermediateCameraColorHandle;
                passData.ambientLightingHandle = ambientLightingHandle;
                passData.rTHandles = rTHandles;
                passData.rTHandles3 = rTHandles3;

                // UnsafePasses don't setup the outputs using UseTextureFragment/UseTextureFragmentDepth, you should specify your writes with UseTexture instead
                builder.UseTexture(passData.cameraColorTargetHandle, AccessFlags.ReadWrite);
                builder.UseTexture(passData.cameraDepthTextureHandle, AccessFlags.Read);
                builder.UseTexture(passData.historyCameraColorHandle, AccessFlags.ReadWrite);
                builder.UseTexture(passData.diffuseHandle, AccessFlags.ReadWrite);
                builder.UseTexture(passData.historyDiffuseHandle, AccessFlags.ReadWrite);
                builder.UseTexture(passData.intermediateDiffuseHandle, AccessFlags.ReadWrite);
                builder.UseTexture(passData.depthHandle, AccessFlags.ReadWrite);
                builder.UseTexture(passData.historyDepthHandle, AccessFlags.Read);
                builder.UseTexture(passData.accumulateSampleHandle, AccessFlags.Write);
                builder.UseTexture(passData.accumulateHistorySampleHandle, AccessFlags.ReadWrite);
                builder.UseTexture(passData.intermediateCameraColorHandle, AccessFlags.ReadWrite);
                builder.UseTexture(passData.ambientLightingHandle, AccessFlags.ReadWrite);
                builder.UseTexture(resourceData.motionVectorColor, AccessFlags.Read);
                //if (enableRenderingLayers) { builder.UseTexture(resourceData.renderingLayersTexture, AccessFlags.Read); }

                passData.localGBuffers = resourceData.gBuffer[0].IsValid();

                if (passData.localGBuffers)
                {
                    passData.gBuffer0Handle = resourceData.gBuffer[0];
                    passData.gBuffer1Handle = resourceData.gBuffer[1];
                    passData.gBuffer2Handle = resourceData.gBuffer[2];

                    builder.UseTexture(passData.gBuffer0Handle, AccessFlags.Read);
                    builder.UseTexture(passData.gBuffer1Handle, AccessFlags.Read);
                    builder.UseTexture(passData.gBuffer2Handle, AccessFlags.Read);
                }

                // Assign the ExecutePass function to the render pass delegate, which will be called by the render graph when executing the pass
                builder.SetRenderFunc((PassData data, UnsafeGraphContext context) => ExecutePass(data, context));
            }
        }
        #endregion
    #endif

        #region Shared
        public void Dispose()
        {
            for (int i = 0; i < MAX_CAMERA_COUNT; i++)
                ReleaseHistory(ref cameraHistoryData[i]);
        }

        // The copy pass evaluates the ambient probe for every pixel, and the ray-miss fallback for every ray, whether or not
        // ambient lighting is overridden, so the coefficients are needed each frame.
        private void UploadAmbientProbe()
        {
            SphericalHarmonicsL2 ambientProbe = RenderSettings.ambientProbe;

            m_SSGIMaterial.SetVector(shAr, new Vector4(ambientProbe[0, 3], ambientProbe[0, 1], ambientProbe[0, 2], ambientProbe[0, 0] - ambientProbe[0, 6]));
            m_SSGIMaterial.SetVector(shAg, new Vector4(ambientProbe[1, 3], ambientProbe[1, 1], ambientProbe[1, 2], ambientProbe[1, 0] - ambientProbe[1, 6]));
            m_SSGIMaterial.SetVector(shAb, new Vector4(ambientProbe[2, 3], ambientProbe[2, 1], ambientProbe[2, 2], ambientProbe[2, 0] - ambientProbe[2, 6]));
            m_SSGIMaterial.SetVector(shBr, new Vector4(ambientProbe[0, 4], ambientProbe[0, 5], ambientProbe[0, 6] * 3, ambientProbe[0, 7]));
            m_SSGIMaterial.SetVector(shBg, new Vector4(ambientProbe[1, 4], ambientProbe[1, 5], ambientProbe[1, 6] * 3, ambientProbe[1, 7]));
            m_SSGIMaterial.SetVector(shBb, new Vector4(ambientProbe[2, 4], ambientProbe[2, 5], ambientProbe[2, 6] * 3, ambientProbe[2, 7]));
            m_SSGIMaterial.SetVector(shC, new Vector4(ambientProbe[0, 8], ambientProbe[1, 8], ambientProbe[2, 8], 1));
        }

        internal static Vector4 EvaluateRotator(float rand)
        {
            float ca = Mathf.Cos(rand);
            float sa = Mathf.Sin(rand);
            return new Vector4(ca, sa, -sa, ca);
        }

        // Per Camera History Data
        internal struct CameraHistoryData
        {
            public int hash;
            public bool hasMatrices;
            public bool textureValid;
            public Matrix4x4 prevCamInvVPMatrix0; // left eye, or the only view
            public Matrix4x4 prevCamInvVPMatrix1; // right eye (stereo instancing)
            public Vector3 prevCameraPositionWS;
            public float scaledWidth;
            public float scaledHeight;
            public int parity; // member of each texture pair written this frame

            public RTHandle historyCameraColorHandle;
            public RTHandle depthHandle0;
            public RTHandle depthHandle1;
            public RTHandle indirectDiffuseHandle0;
            public RTHandle indirectDiffuseHandle1;
            public RTHandle accumulateSampleHandle0;
            public RTHandle accumulateSampleHandle1;
        }

        internal const int MAX_CAMERA_COUNT = 4; // must be >= 2
        internal readonly CameraHistoryData[] cameraHistoryData = new CameraHistoryData[MAX_CAMERA_COUNT];

        internal int GetCameraHistoryDataIndex(int cameraHash)
        {
            // Unroll manually for MAX_CAMERA_COUNT = 4
            if (cameraHistoryData[0].hash == cameraHash) return 0;
            if (cameraHistoryData[1].hash == cameraHash) return 1;
            if (cameraHistoryData[2].hash == cameraHash) return 2;
            if (cameraHistoryData[3].hash == cameraHash) return 3;
            return -1; // new camera
        }

        internal void UpdateCameraHistoryData(bool cameraHashChanged)
        {
            if (cameraHashChanged)
            {
                const int lastIndex = MAX_CAMERA_COUNT - 1;

                // Release the persistent textures of the camera being evicted
                ReleaseHistory(ref cameraHistoryData[lastIndex]);

                // Shift the camera history data back by one
                Array.Copy(cameraHistoryData, 0, cameraHistoryData, 1, lastIndex);

                // The new camera starts without history (the shift left a copy of slot 1 here)
                cameraHistoryData[0] = default;
            }
        }

        internal static void ReleaseHistory(ref CameraHistoryData history)
        {
            history.historyCameraColorHandle?.Release();
            history.depthHandle0?.Release();
            history.depthHandle1?.Release();
            history.indirectDiffuseHandle0?.Release();
            history.indirectDiffuseHandle1?.Release();
            history.accumulateSampleHandle0?.Release();
            history.accumulateSampleHandle1?.Release();
            history = default;
        }

        // Picks which member of each texture pair is written this frame and which one holds last frame, then swaps them for the next frame.
        internal static void SelectHistory(ref CameraHistoryData history, out int write, out int read)
        {
            write = history.parity & 1;
            read = write ^ 1;
            history.parity = read;
        }

        private static void AllocateHistoryPair(ref RTHandle handle0, ref RTHandle handle1, in RenderTextureDescriptor desc, string name0, string name1)
        {
            RenderingUtils.ReAllocateHandleIfNeeded(ref handle0, desc, FilterMode.Point, TextureWrapMode.Clamp, name: name0);
            RenderingUtils.ReAllocateHandleIfNeeded(ref handle1, desc, FilterMode.Point, TextureWrapMode.Clamp, name: name1);
        }

        // Uploads last frame's inverse view-projection (per eye) and stores this frame's for the next one.
        private void ApplyPreviousViewProjection(ref CameraHistoryData history, Matrix4x4 invViewProj0, Matrix4x4 invViewProj1, Vector3 cameraPositionWS)
        {
            if (!history.hasMatrices)
            {
                // First frame of this camera: reproject onto the current view, so nothing moves.
                history.prevCamInvVPMatrix0 = invViewProj0;
                history.prevCamInvVPMatrix1 = invViewProj1;
                history.prevCameraPositionWS = cameraPositionWS;
                history.hasMatrices = true;
            }

            prevInvViewProjMatrices[0] = history.prevCamInvVPMatrix0;
            prevInvViewProjMatrices[1] = history.prevCamInvVPMatrix1;
            m_SSGIMaterial.SetMatrix(_PrevInvViewProjMatrix, history.prevCamInvVPMatrix0);
            m_SSGIMaterial.SetMatrixArray(_PrevInvViewProjMatrixStereo, prevInvViewProjMatrices);
            m_SSGIMaterial.SetVector(_PrevCameraPositionWS, history.prevCameraPositionWS);

            history.prevCamInvVPMatrix0 = invViewProj0;
            history.prevCamInvVPMatrix1 = invViewProj1;
            history.prevCameraPositionWS = cameraPositionWS;
        }

        // In multi-pass XR both eyes render through the same Camera, so each eye needs its own history slot.
        internal static int ComputeCameraHistoryHash(Camera camera, XRPass xr)
        {
            int hash = camera.GetHashCode();
            if (xr != null && xr.enabled && !xr.singlePassEnabled)
                hash = unchecked(hash * 397) ^ (xr.multipassId + 1);
            return hash;
        }

        internal static Matrix4x4 ComputeInverseViewProjection(Matrix4x4 viewMatrix, Matrix4x4 projectionMatrix)
        {
            return (GL.GetGPUProjectionMatrix(projectionMatrix, true) * viewMatrix).inverse;
        }

        // Vertical field of view (degrees) of a perspective projection, used for the per-eye XR projections.
        internal static float GetVerticalFieldOfView(Matrix4x4 projectionMatrix)
        {
            return 2.0f * Mathf.Atan(1.0f / projectionMatrix.m11) * Mathf.Rad2Deg;
        }

        internal static float ComputePixelSpreadAngleTangent(float fieldOfView, int width, int height, float resolutionScale)
        {
            int minSize = Mathf.Max(1, Mathf.Min(Mathf.FloorToInt(width * resolutionScale), Mathf.FloorToInt(height * resolutionScale)));
            return Mathf.Tan(fieldOfView * Mathf.Deg2Rad * 0.5f) * 2.0f / minSize;
        }

        // Binds every slice so stereo instancing writes both eyes of a texture array (the 2-argument overload binds slice 0 only).
        internal static void SetRenderTargets(CommandBuffer cmd, RenderTargetIdentifier[] colors, RenderTargetIdentifier depth)
        {
            cmd.SetRenderTarget(colors, depth, 0, CubemapFace.Unknown, -1);
        }

        private void UpdateReflectionProbe(NativeArray<VisibleReflectionProbe> visibleReflectionProbes, Vector3 cameraPosition)
        {
            if (ssgiVolume.IsFallbackReflectionProbes() && !Shader.IsKeywordEnabled(_FORWARD_PLUS))
            {
                var reflectionProbe = GetClosestProbe(visibleReflectionProbes, cameraPosition);
                if (reflectionProbe != null)
                {
                    m_SSGIMaterial.SetTexture(specCube0, reflectionProbe.texture);
                    m_SSGIMaterial.SetVector(specCube0_HDR, reflectionProbe.textureHDRDecodeValues);
                    bool isBoxProjected = reflectionProbe.boxProjection;
                    if (isBoxProjected)
                    {
                        Vector3 probe0Position = reflectionProbe.transform.position;
                        float probe0Mode = isBoxProjected ? 1.0f : 0.0f;
                        m_SSGIMaterial.SetVector(specCube0_BoxMin, reflectionProbe.bounds.min);
                        m_SSGIMaterial.SetVector(specCube0_BoxMax, reflectionProbe.bounds.max);
                        m_SSGIMaterial.SetVector(specCube0_ProbePosition, new Vector4(probe0Position.x, probe0Position.y, probe0Position.z, probe0Mode));
                    }
                    m_SSGIMaterial.SetFloat(probeWeight, 0.0f);
                    m_SSGIMaterial.SetFloat(probeSet, 1.0f);
                }
                else
                {
                    m_SSGIMaterial.SetFloat(probeSet, 0.0f);
                }
            }
            else
            {
                m_SSGIMaterial.SetFloat(probeSet, 0.0f);
            }
        }

        private static ReflectionProbe GetClosestProbe(NativeArray<VisibleReflectionProbe> visibleReflectionProbes, Vector3 cameraPosition)
        {
            ReflectionProbe closestProbe = null;
            float closestDistance = float.MaxValue;
            int highestImportance = int.MinValue;
            float smallestBoundsSize = float.MaxValue;

            foreach (var visibleProbe in visibleReflectionProbes)
            {
                ReflectionProbe probe = visibleProbe.reflectionProbe;
                Bounds probeBounds = probe.bounds;
                int probeImportance = probe.importance;
                float boundsSize = probeBounds.size.magnitude;

                if (probeBounds.Contains(cameraPosition))
                {
                    float distance = Vector3.Distance(cameraPosition, probe.transform.position);

                    bool isMoreImportant = probeImportance > highestImportance;
                    bool isSizeSmaller = probeImportance == highestImportance && boundsSize < smallestBoundsSize;
                    bool isDistanceCloser = boundsSize == smallestBoundsSize && distance < closestDistance;

                    // Rules:
                    // 1. Find the probe(s) with highest importance index
                    // 2. Find the probe(s) with a smallest box size
                    // 3. Find the probe(s) with a closer distance to the camera
                    bool isCloserProbe = isMoreImportant || isSizeSmaller || isDistanceCloser;

                    if (isCloserProbe)
                    {
                        closestDistance = distance;
                        highestImportance = probeImportance;
                        smallestBoundsSize = boundsSize;
                        closestProbe = probe;
                    }
                }
            }
            // Returns null if we cannot find a probe
            return closestProbe;
        }
        #endregion
    }

    public class BackfaceDataPass : ScriptableRenderPass
    {
        const string m_ProfilerTag = "Render Backface Data";

        public bool backfaceLighting;

        private RenderStateBlock m_DepthRenderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);
        private readonly ShaderTagId[] m_LitTags = new ShaderTagId[2];

        private const string k_DepthOnly = "DepthOnly";
        private const string k_UniversalForward = "UniversalForward";
        private const string k_UniversalForwardOnly = "UniversalForwardOnly";


    #if UNITY_6000_0_OR_NEWER
        #region Render Graph Pass
        // This class stores the data needed by the pass, passed as parameter to the delegate function that executes the pass
        private class PassData
        {
            internal RendererListHandle rendererListHandle;
        }

        // This static method is used to execute the pass and passed as the RenderFunc delegate to the RenderGraph render pass
        static void ExecutePass(PassData data, RasterGraphContext context)
        {
            context.cmd.DrawRendererList(data.rendererListHandle);
        }

        // This is where the renderGraph handle can be accessed.
        // Each ScriptableRenderPass can use the RenderGraph handle to add multiple render passes to the render graph
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            // add a raster render pass to the render graph, specifying the name and the data type that will be passed to the ExecutePass function
            using (var builder = renderGraph.AddRasterRenderPass<PassData>(m_ProfilerTag, out var passData))
            {
                // UniversalResourceData contains all the texture handles used by the renderer, including the active color and depth textures
                // The active color and depth textures are the main color and depth buffers that the camera renders into
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                UniversalRenderingData universalRenderingData = frameData.Get<UniversalRenderingData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalLightData lightData = frameData.Get<UniversalLightData>();

                //var depthDesc = cameraData.cameraTargetDescriptor;
                //depthDesc.msaaSamples = 1;
                //depthDesc.bindMS = false;
                //depthDesc.graphicsFormat = GraphicsFormat.None;

                TextureDesc depthDesc;
                if (!resourceData.isActiveTargetBackBuffer)
                {
                    depthDesc = resourceData.activeDepthTexture.GetDescriptor(renderGraph);
                }
                else
                {
                    depthDesc = resourceData.cameraDepthTexture.GetDescriptor(renderGraph);
                    var backBufferInfo = renderGraph.GetRenderTargetInfo(resourceData.backBufferDepth);
                    depthDesc.colorFormat = backBufferInfo.format;
                }
                depthDesc.name = _CameraBackDepthTexture;
                depthDesc.useMipMap = false;
                depthDesc.clearBuffer = true;
                depthDesc.msaaSamples = MSAASamples.None;
                depthDesc.bindTextureMS = false;
                depthDesc.filterMode = FilterMode.Point;
                depthDesc.wrapMode = TextureWrapMode.Clamp;


                //if (resourceData.activeDepthTexture.IsValid())
                //    depthDesc.depthBufferBits = (int)resourceData.activeDepthTexture.GetDescriptor(renderGraph).depthBufferBits;

                // Render backface depth
                if (!backfaceLighting)
                {
                    //TextureHandle backDepthHandle = UniversalRenderer.CreateRenderGraphTexture(renderGraph, depthDesc, name: _CameraBackDepthTexture, true, FilterMode.Point, TextureWrapMode.Clamp);
                    TextureHandle backDepthHandle = renderGraph.CreateTexture(depthDesc);

                    RendererListDesc rendererListDesc = new RendererListDesc(new ShaderTagId(k_DepthOnly), universalRenderingData.cullResults, cameraData.camera);
                    m_DepthRenderStateBlock.depthState = new DepthState(true, CompareFunction.LessEqual);
                    m_DepthRenderStateBlock.mask |= RenderStateMask.Depth;
                    m_DepthRenderStateBlock.rasterState = new RasterState(CullMode.Front);
                    m_DepthRenderStateBlock.mask |= RenderStateMask.Raster;
                    rendererListDesc.stateBlock = m_DepthRenderStateBlock;
                    rendererListDesc.sortingCriteria = cameraData.defaultOpaqueSortFlags;
                    rendererListDesc.renderQueueRange = RenderQueueRange.opaque;

                    passData.rendererListHandle = renderGraph.CreateRendererList(rendererListDesc);

                    // We declare the RendererList we just created as an input dependency to this pass, via UseRendererList()
                    builder.UseRendererList(passData.rendererListHandle);

                    // Set to read & write to avoid texture reusing, since this texture will be used by other passes later.
                    builder.SetRenderAttachmentDepth(backDepthHandle, AccessFlags.ReadWrite);

                    builder.SetGlobalTextureAfterPass(backDepthHandle, cameraBackDepthTexture);

                    // Assign the ExecutePass function to the render pass delegate, which will be called by the render graph when executing the pass
                    builder.SetRenderFunc((PassData data, RasterGraphContext context) => ExecutePass(data, context));
                }
                // Render backface depth + color
                else
                {
                    //TextureHandle backDepthHandle = UniversalRenderer.CreateRenderGraphTexture(renderGraph, depthDesc, name: _CameraBackDepthTexture, true, FilterMode.Point, TextureWrapMode.Clamp);
                    TextureHandle backDepthHandle = renderGraph.CreateTexture(depthDesc);

                    //var colorDesc = cameraData.cameraTargetDescriptor;
                    //colorDesc.msaaSamples = 1;
                    //colorDesc.bindMS = false;
                    //colorDesc.depthStencilFormat = GraphicsFormat.None;
                    //colorDesc.graphicsFormat = GraphicsFormat.B10G11R11_UFloatPack32;

                    var colorDesc = resourceData.cameraColor.GetDescriptor(renderGraph);
                    colorDesc.name = _CameraBackOpaqueTexture;
                    colorDesc.useMipMap = false;
                    colorDesc.clearBuffer = true;
                    colorDesc.msaaSamples = MSAASamples.None;
                    colorDesc.bindTextureMS = false;
                    colorDesc.filterMode = FilterMode.Point;
                    colorDesc.wrapMode = TextureWrapMode.Clamp;
                    colorDesc.colorFormat = GraphicsFormat.B10G11R11_UFloatPack32;

                    //TextureHandle backColorHandle = UniversalRenderer.CreateRenderGraphTexture(renderGraph, colorDesc, name: _CameraBackOpaqueTexture, true, FilterMode.Point, TextureWrapMode.Clamp);
                    TextureHandle backColorHandle = renderGraph.CreateTexture(colorDesc);

                    m_LitTags[0] = new ShaderTagId(k_UniversalForward);
                    m_LitTags[1] = new ShaderTagId(k_UniversalForwardOnly);

                    RendererListDesc rendererListDesc = new RendererListDesc(m_LitTags, universalRenderingData.cullResults, cameraData.camera);
                    m_DepthRenderStateBlock.depthState = new DepthState(true, CompareFunction.LessEqual);
                    m_DepthRenderStateBlock.mask |= RenderStateMask.Depth;
                    m_DepthRenderStateBlock.rasterState = new RasterState(CullMode.Front);
                    m_DepthRenderStateBlock.mask |= RenderStateMask.Raster;
                    rendererListDesc.stateBlock = m_DepthRenderStateBlock;
                    rendererListDesc.sortingCriteria = cameraData.defaultOpaqueSortFlags;
                    rendererListDesc.renderQueueRange = RenderQueueRange.opaque;

                    passData.rendererListHandle = renderGraph.CreateRendererList(rendererListDesc);

                    // We declare the RendererList we just created as an input dependency to this pass, via UseRendererList()
                    builder.UseRendererList(passData.rendererListHandle);

                    builder.SetRenderAttachment(backColorHandle, 0);
                    builder.SetRenderAttachmentDepth(backDepthHandle);

                    builder.SetGlobalTextureAfterPass(backColorHandle, cameraBackOpaqueTexture);
                    builder.SetGlobalTextureAfterPass(backDepthHandle, cameraBackDepthTexture);

                    // Assign the ExecutePass function to the render pass delegate, which will be called by the render graph when executing the pass
                    builder.SetRenderFunc((PassData data, RasterGraphContext context) => ExecutePass(data, context));
                }
            }
        }
        #endregion
    #endif

        #region Shared
        public void Dispose()
        {

        }
        #endregion
    }

    public class ForwardGBufferPass : ScriptableRenderPass
    {
        private const string m_ProfilerTag = "Render Forward GBuffer";

        private List<ShaderTagId> m_ShaderTagIdList = new List<ShaderTagId>();
        private ShaderTagId[] m_ShaderTagIds;
        private FilteringSettings m_filter;

        // Renderers marked with "overrideRenderingLayerMask" (see RegisterRenderer) are drawn with this shader instead,
        // selected through their forward pass. They are drawn first so a real GBuffer pass always wins.
        public Shader overrideShader;
        public uint overrideRenderingLayerMask;
        private readonly ShaderTagId[] m_OverrideShaderTagIds = new ShaderTagId[] { new ShaderTagId("UniversalForward") };

        // Materials tagged "UniversalMaterialType" = "Unlit" (URP Unlit, Shader Graph Unlit) keep their normal in the GBuffer
        // but write no albedo, so ambient removal and the bounce leave screens and emissive panels alone.
        internal const string UnlitMaterialType = "Unlit";
        private readonly ShaderTagId m_MaterialTypeTag = new ShaderTagId("UniversalMaterialType");
        // Every URP material type is listed, as URP's own GBuffer pass does, and the empty tag is the catch-all for shaders without the tag.
        private readonly ShaderTagId[] m_MaterialTypeValues = new ShaderTagId[] { new ShaderTagId("Lit"), new ShaderTagId("SimpleLit"), new ShaderTagId("ComplexLit"), new ShaderTagId("BakedLit"), new ShaderTagId(UnlitMaterialType), new ShaderTagId() };
        private const int k_UnlitMaterialTypeIndex = 4;

        // Depth Priming.
        private RenderStateBlock m_RenderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);

        internal static RenderStateBlock CreateUnlitStateBlock(RenderStateBlock baseBlock)
        {
            BlendState blendState = new BlendState(true, false);
            blendState.blendState0 = new RenderTargetBlendState((ColorWriteMask)0);
            baseBlock.blendState = blendState;
            baseBlock.mask |= RenderStateMask.Blend;
            return baseBlock;
        }

        public ForwardGBufferPass(string[] PassNames)
        {
            RenderQueueRange queue = RenderQueueRange.opaque;
            m_filter = new FilteringSettings(queue);
            if (PassNames != null && PassNames.Length > 0)
            {
                foreach (var passName in PassNames)
                    m_ShaderTagIdList.Add(new ShaderTagId(passName));
            }
            m_ShaderTagIds = m_ShaderTagIdList.ToArray();
        }

        // From "URP-Package/Runtime/DeferredLights.cs".
        public GraphicsFormat GetGBufferFormat(int index)
        {
            return GBufferFormat(index);
        }

        internal static GraphicsFormat GBufferFormat(int index)
        {
            if (index == 0) // sRGB albedo, materialFlags
                return QualitySettings.activeColorSpace == ColorSpace.Linear ? GraphicsFormat.R8G8B8A8_SRGB : GraphicsFormat.R8G8B8A8_UNorm;
            else if (index == 1) // sRGB specular, occlusion
                return GraphicsFormat.R8G8B8A8_UNorm;
            else if (index == 2) // normal normal normal packedSmoothness
                                 // NormalWS range is -1.0 to 1.0, so we need a signed render texture.
            #if UNITY_2023_2_OR_NEWER
                if (SystemInfo.IsFormatSupported(GraphicsFormat.R8G8B8A8_SNorm, GraphicsFormatUsage.Render))
            #else
                if (SystemInfo.IsFormatSupported(GraphicsFormat.R8G8B8A8_SNorm, FormatUsage.Render))
            #endif
                    return GraphicsFormat.R8G8B8A8_SNorm;
                else
                    return GraphicsFormat.R16G16B16A16_SFloat;
            else
                return GraphicsFormat.None;
        }


    #if UNITY_6000_0_OR_NEWER
        #region Render Graph Pass

        // Transient GBuffer target cleared to transparent black (UniversalRenderer.CreateRenderGraphTexture clears to opaque black).
        internal static TextureHandle CreateClearedTexture(RenderGraph renderGraph, in RenderTextureDescriptor desc, string name)
        {
            TextureDesc rgDesc = new TextureDesc(desc.width, desc.height);
            rgDesc.dimension = desc.dimension;
            rgDesc.slices = desc.volumeDepth;
            rgDesc.colorFormat = desc.graphicsFormat;
            rgDesc.msaaSamples = MSAASamples.None;
            rgDesc.name = name;
            rgDesc.clearBuffer = true;
            rgDesc.clearColor = Color.clear;
            rgDesc.filterMode = FilterMode.Point;
            rgDesc.wrapMode = TextureWrapMode.Clamp;
            rgDesc.useMipMap = false;
            return renderGraph.CreateTexture(rgDesc);
        }

        // This class stores the data needed by the pass, passed as parameter to the delegate function that executes the pass
        private class PassData
        {
            internal bool isOpenGL;

            internal RendererListHandle rendererListHandle;
            internal bool hasOverrideRendererList;
            internal RendererListHandle overrideRendererListHandle;
        }

        // This static method is used to execute the pass and passed as the RenderFunc delegate to the RenderGraph render pass
        static void ExecutePass(PassData data, RasterGraphContext context)
        {
            if (data.isOpenGL)
                context.cmd.ClearRenderTarget(true, true, Color.black);
            //else
                // We have to also clear previous color so that the "background" will remain empty (black) when moving the camera.
                //context.cmd.ClearRenderTarget(false, true, Color.clear);

            // Override shader first, so a renderer with a real GBuffer pass overwrites the approximation.
            if (data.hasOverrideRendererList)
                context.cmd.DrawRendererList(data.overrideRendererListHandle);

            context.cmd.DrawRendererList(data.rendererListHandle);
        }

        // This is where the renderGraph handle can be accessed.
        // Each ScriptableRenderPass can use the RenderGraph handle to add multiple render passes to the render graph
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            // add a raster render pass to the render graph, specifying the name and the data type that will be passed to the ExecutePass function
            using (var builder = renderGraph.AddRasterRenderPass<PassData>(m_ProfilerTag, out var passData))
            {
                // UniversalResourceData contains all the texture handles used by the renderer, including the active color and depth textures
                // The active color and depth textures are the main color and depth buffers that the camera renders into
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                UniversalRenderingData universalRenderingData = frameData.Get<UniversalRenderingData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalLightData lightData = frameData.Get<UniversalLightData>();

                RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;
                desc.msaaSamples = 1;
                desc.bindMS = false;
                desc.depthBufferBits = 0;

                // Cleared to zero: surfaces whose shader has no GBuffer pass read back all zeros, which the SSGI shader detects.
                // Albedo.rgb + MaterialFlags.a
                desc.graphicsFormat = GetGBufferFormat(0);
                TextureHandle gBuffer0Handle = CreateClearedTexture(renderGraph, desc, _GBuffer0);

                // Specular.rgb + Occlusion.a
                desc.graphicsFormat = GetGBufferFormat(1);
                TextureHandle gBuffer1Handle = CreateClearedTexture(renderGraph, desc, _GBuffer1);

                // [Resolve Later] The "_CameraNormalsTexture" still exists after disabling DepthNormals Prepass, which may cause issue during rendering.
                // So instead of checking the RTHandle, we need to check if DepthNormals Prepass is enqueued.

                /*
                TextureHandle gBuffer2Handle;
                // If "_CameraNormalsTexture" exists (lacking smoothness info), set the target to it instead of creating a new RT.
                if (normalsTextureFieldInfo.GetValue(cameraData.renderer) is not RTHandle normalsTextureHandle)
                {
                    // NormalWS.rgb + Smoothness.a
                    desc.graphicsFormat = GetGBufferFormat(2);
                    gBuffer2Handle = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, name: _GBuffer2, false, FilterMode.Point, TextureWrapMode.Clamp);
                }
                else
                {
                    gBuffer2Handle = resourceData.cameraNormalsTexture;
                }
                */

                // NormalWS.rgb + Smoothness.a
                desc.graphicsFormat = GetGBufferFormat(2);
                TextureHandle gBuffer2Handle = CreateClearedTexture(renderGraph, desc, _GBuffer2);

                // [OpenGL] Reusing the depth buffer seems to cause black glitching artifacts, so clear the existing depth.
                bool isOpenGL = (SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3) || (SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLCore); // GLES 2 is deprecated.

                // Disable depth priming if camera uses MSAA
                bool canDepthPriming = !isOpenGL && (cameraData.renderType == CameraRenderType.Base || cameraData.clearDepth) && cameraData.cameraTargetDescriptor.msaaSamples == desc.msaaSamples;

                //RenderTextureDescriptor depthDesc = cameraData.cameraTargetDescriptor;
                //depthDesc.msaaSamples = 1;
                //depthDesc.bindMS = false;
                //depthDesc.graphicsFormat = GraphicsFormat.None;
                //if (resourceData.activeDepthTexture.IsValid())
                //    depthDesc.depthBufferBits = (int)resourceData.activeDepthTexture.GetDescriptor(renderGraph).depthBufferBits;

                TextureDesc depthDesc;
                if (!resourceData.isActiveTargetBackBuffer)
                {
                    depthDesc = resourceData.activeDepthTexture.GetDescriptor(renderGraph);
                }
                else
                {
                    depthDesc = resourceData.cameraDepthTexture.GetDescriptor(renderGraph);
                    var backBufferInfo = renderGraph.GetRenderTargetInfo(resourceData.backBufferDepth);
                    depthDesc.colorFormat = backBufferInfo.format;
                }
                depthDesc.name = _GBufferDepth;
                depthDesc.useMipMap = false;
                // Without depth priming (MSAA cameras) this is a fresh transient depth buffer, so it must start cleared.
                depthDesc.clearBuffer = !canDepthPriming;
                depthDesc.msaaSamples = MSAASamples.None;
                depthDesc.bindTextureMS = false;
                depthDesc.filterMode = FilterMode.Point;
                depthDesc.wrapMode = TextureWrapMode.Clamp;

                TextureHandle depthHandle;
                if (canDepthPriming)
                    depthHandle = resourceData.activeDepthTexture; // Note: there was a problem that the RT format was R32 (not the depth buffer) instead of D32, but I cannot reproduce it again
                else
                    depthHandle = renderGraph.CreateTexture(depthDesc);
                    //depthHandle = UniversalRenderer.CreateRenderGraphTexture(renderGraph, depthDesc, name: _GBufferDepth, false, FilterMode.Point, TextureWrapMode.Clamp);

                // Reduce GBuffer overdraw using the depth from opaque pass. (excluding OpenGL platforms)
                if ( canDepthPriming)
                {
                    m_RenderStateBlock.depthState = new DepthState(false, CompareFunction.Equal);
                    m_RenderStateBlock.mask |= RenderStateMask.Depth;
                }
                else if (m_RenderStateBlock.depthState.compareFunction == CompareFunction.Equal)
                {
                    m_RenderStateBlock.depthState = new DepthState(true, CompareFunction.LessEqual);
                    m_RenderStateBlock.mask |= RenderStateMask.Depth;
                }

                // GBuffer cannot store surface data from transparent objects.
                SortingCriteria sortingCriteria = cameraData.defaultOpaqueSortFlags;
                DrawingSettings drawingSettings = RenderingUtils.CreateDrawingSettings(m_ShaderTagIdList, universalRenderingData, cameraData, lightData, sortingCriteria);
                drawingSettings.perObjectData = PerObjectData.None;
                NativeArray<ShaderTagId> materialTypeValues = new NativeArray<ShaderTagId>(m_MaterialTypeValues, Allocator.Temp);
                NativeArray<RenderStateBlock> materialTypeStates = new NativeArray<RenderStateBlock>(m_MaterialTypeValues.Length, Allocator.Temp);
                for (int i = 0; i < materialTypeStates.Length; i++)
                    materialTypeStates[i] = i == k_UnlitMaterialTypeIndex ? CreateUnlitStateBlock(m_RenderStateBlock) : m_RenderStateBlock;
                RendererListParams rendererListParams = new RendererListParams(universalRenderingData.cullResults, drawingSettings, m_filter)
                {
                    tagName = m_MaterialTypeTag,
                    tagValues = materialTypeValues,
                    stateBlocks = materialTypeStates,
                    isPassTagName = false
                };

                // Set pass data
                passData.isOpenGL = isOpenGL;
                passData.rendererListHandle = renderGraph.CreateRendererList(rendererListParams);
                materialTypeValues.Dispose();
                materialTypeStates.Dispose();

                // We declare the RendererList we just created as an input dependency to this pass, via UseRendererList()
                builder.UseRendererList(passData.rendererListHandle);

                passData.hasOverrideRendererList = overrideShader != null && overrideRenderingLayerMask != 0;
                if (passData.hasOverrideRendererList)
                {
                    RendererListDesc overrideListDesc = new RendererListDesc(m_OverrideShaderTagIds, universalRenderingData.cullResults, cameraData.camera);
                    overrideListDesc.overrideShader = overrideShader;
                    overrideListDesc.overrideShaderPassIndex = 0;
                    overrideListDesc.renderingLayerMask = overrideRenderingLayerMask;
                    overrideListDesc.stateBlock = m_RenderStateBlock;
                    overrideListDesc.sortingCriteria = sortingCriteria;
                    overrideListDesc.renderQueueRange = m_filter.renderQueueRange;
                    passData.overrideRendererListHandle = renderGraph.CreateRendererList(overrideListDesc);
                    builder.UseRendererList(passData.overrideRendererListHandle);
                }

                // Set render targets
                builder.SetRenderAttachment(gBuffer0Handle, 0);
                builder.SetRenderAttachment(gBuffer1Handle, 1);
                builder.SetRenderAttachment(gBuffer2Handle, 2);
                builder.SetRenderAttachmentDepth(depthHandle, AccessFlags.Write);

                // Set global textures after this pass
                builder.SetGlobalTextureAfterPass(gBuffer0Handle, gBuffer0);
                builder.SetGlobalTextureAfterPass(gBuffer1Handle, gBuffer1);
                builder.SetGlobalTextureAfterPass(gBuffer2Handle, gBuffer2);
                // The GBuffer's own depth tells the effect which pixels this pass actually wrote. A surface whose shader has no
                // GBuffer pass is skipped here, so without it the pixel silently reads the surface behind it.
                builder.SetGlobalTextureAfterPass(depthHandle, gBufferDepth);

                // We disable culling for this pass for the demonstrative purpose of this sample, as normally this pass would be culled,
                // since the destination texture is not used anywhere else
                //builder.AllowGlobalStateModification(true);
                //builder.AllowPassCulling(false);

                // Assign the ExecutePass function to the render pass delegate, which will be called by the render graph when executing the pass
                builder.SetRenderFunc((PassData data, RasterGraphContext context) => ExecutePass(data, context));
            }
        }

        #endregion
    #endif

        #region Shared
        public void Dispose()
        {

        }
        #endregion
    }
}
