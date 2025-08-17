
using Basis.Scripts.Device_Management;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;
public class SMModuleRenderResolutionURP : BasisSettingsBase
{
    public float RenderScale = 1;
    private XRDisplaySubsystem xrDisplaySubsystem;
    public List<XRDisplaySubsystem> xrDisplays = new List<XRDisplaySubsystem>();
    public override void ValidSettingsChange(string matchedSettingName, string optionValue)
    {
        SetUpscaler(optionValue);
        HandleRenderResolution(0);
        HandleFoveatedRendering(0);
    }
    private void HandleRenderResolution(float Option)
    {
        if (!XRSettings.useOcclusionMesh)
        {
            XRSettings.useOcclusionMesh = true;
        }
        SetRenderResolution(Option);
    }
    private void HandleFoveatedRendering(float value)
    {
        SubsystemManager.GetSubsystems<XRDisplaySubsystem>(xrDisplays);

        if (xrDisplays.Count == 0)
        {
            // BasisDebug.LogError("No XR display subsystems found.");
            return;
        }

        foreach (var subsystem in xrDisplays)
        {
            if (subsystem.running)
            {
                xrDisplaySubsystem = subsystem;
                break;
            }
        }

        if (xrDisplaySubsystem == null) return;

        xrDisplaySubsystem.foveatedRenderingFlags = XRDisplaySubsystem.FoveatedRenderingFlags.GazeAllowed;

        xrDisplaySubsystem.foveatedRenderingLevel = value;
    }
    public void SetRenderResolution(float renderScale)
    {
#if UNITY_ANDROID
#else
        RenderScale = renderScale;
        if (BasisDeviceManagement.StaticCurrentMode == BasisConstants.Desktop)
        {
            UniversalRenderPipelineAsset Asset = (UniversalRenderPipelineAsset)QualitySettings.renderPipeline;
            if (Asset.renderScale != RenderScale)
            {
                Asset.renderScale = RenderScale;
            }
        }
        else
        {
            UniversalRenderPipelineAsset Asset = (UniversalRenderPipelineAsset)QualitySettings.renderPipeline;
            if (XRSettings.eyeTextureResolutionScale != renderScale)
            {
                XRSettings.eyeTextureResolutionScale = RenderScale;
            }
            /// the system allows us to scale the render resolution correctly, however gpu culling does not know about this
            if (Asset.renderScale != 1)
            {
                Asset.renderScale = 1;
            }
        }
#endif
    }
    public void SetUpscaler(string Using)
    {
#if UNITY_ANDROID
#else
        UniversalRenderPipelineAsset Asset = (UniversalRenderPipelineAsset)QualitySettings.renderPipeline;
        switch (Using)
        {
            case "auto":
                Asset.upscalingFilter = UpscalingFilterSelection.Auto;
                break;
            case "linear":
                Asset.upscalingFilter = UpscalingFilterSelection.Linear;
                break;
            case "point":
                Asset.upscalingFilter = UpscalingFilterSelection.Point;
                break;
            case "fsr":
                Asset.upscalingFilter = UpscalingFilterSelection.FSR;
                break;
            case "stp":
                Asset.upscalingFilter = UpscalingFilterSelection.STP;
                break;
        }
#endif
    }
}
