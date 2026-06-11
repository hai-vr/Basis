using Basis.BasisUI;
using Basis.Scripts.BasisSdk.Interactions;
using Basis.Scripts.Device_Management;
using Basis.Scripts.Drivers;
using UnityEngine;

/// <summary>
/// Spawns a grabbable, resizable preview screen next to the handheld camera that mirrors the
/// camera's live feed. It exists only while the Camera HUD setting is on, the user is in VR, and
/// the camera is in direct-to-screen (recording-view) mode — in that mode the feed lives in
/// <see cref="CopyCameraColorToStaticRTFeature.OutputRT"/> and the camera is already force-rendering.
/// </summary>
public partial class BasisHandHeldCamera
{
    [Header("Preview Screen")]
    /// <summary>Sideways offset from the camera, in meters at default avatar scale, where the screen spawns.</summary>
    public float previewScreenSideOffset = 0.35f;

    /// <summary>Spawn width of the screen, in meters at default avatar scale, before the user resizes it.</summary>
    public float previewScreenWidth = 0.45f;

    /// <summary>Smallest the screen can be resized to, as a percent of its spawn size (two-hand gesture).</summary>
    public float previewScreenMinScalePercent = 40f;

    /// <summary>Largest the screen can be resized to, as a percent of its spawn size (two-hand gesture).</summary>
    public float previewScreenMaxScalePercent = 1200f;

    private GameObject previewScreenGO;
    private Material previewScreenMaterial;
    private bool previewScreenSubscribed;

    /// <summary>Subscribes to the Camera HUD setting so toggling it spawns/despawns the screen live.</summary>
    private void SubscribePreviewScreen()
    {
        if (previewScreenSubscribed) return;
        BasisSettingsDefaults.CameraHud.OnChanged += OnCameraHudSettingChanged;
        previewScreenSubscribed = true;
    }

    private void UnsubscribePreviewScreen()
    {
        if (!previewScreenSubscribed) return;
        BasisSettingsDefaults.CameraHud.OnChanged -= OnCameraHudSettingChanged;
        previewScreenSubscribed = false;
    }

    private void OnCameraHudSettingChanged(bool _) => UpdatePreviewScreen();

    /// <summary>
    /// Spawns or despawns the preview-screen pickup based on the gate: the Camera HUD setting on,
    /// the user in VR, and the camera in direct-to-screen mode.
    /// </summary>
    private void UpdatePreviewScreen()
    {
        bool shouldShow = BasisSettingsDefaults.CameraHud.RawValue
            && BasisDeviceManagement.IsCurrentModeVR()
            && IsOverridingDesktopView;

        if (shouldShow)
        {
            if (previewScreenGO == null)
            {
                SpawnPreviewScreen();
            }
        }
        else
        {
            DespawnPreviewScreen();
        }
    }

    private void SpawnPreviewScreen()
    {
        RenderTexture feed = CopyCameraColorToStaticRTFeature.OutputRT;
        float aspect = (feed != null && feed.height > 0) ? (float)feed.width / feed.height : 16f / 9f;

        previewScreenGO = GameObject.CreatePrimitive(PrimitiveType.Quad);
        previewScreenGO.name = "CameraPreviewScreen";
        previewScreenGO.layer = LayerMask.NameToLayer("OverlayUI");

        if (previewScreenGO.TryGetComponent(out MeshCollider meshCollider))
        {
            DestroyImmediate(meshCollider);
        }
        BoxCollider box = previewScreenGO.AddComponent<BoxCollider>();
        box.size = new Vector3(1f, 1f, 0.1f);

        previewScreenMaterial = Material != null ? Instantiate(Material) : new Material(Shader.Find("Unlit/Texture"));
        if (previewScreenMaterial.HasProperty("_Cull"))
        {
            previewScreenMaterial.SetFloat("_Cull", 0f);
        }
        BindPreviewScreenFeed(feed);
        if (previewScreenGO.TryGetComponent(out MeshRenderer meshRenderer))
        {
            meshRenderer.sharedMaterial = previewScreenMaterial;
        }

        float scale = BasisHeightDriver.AvatarToDefaultRatioScaledWithAvatarScale;
        float width = previewScreenWidth * scale;
        previewScreenGO.transform.localScale = new Vector3(width, width / aspect, 1f);

        Vector3 headPos = BasisLocalCameraDriver.HeadPosition;
        Quaternion headRot = BasisLocalCameraDriver.HeadRotation;
        Vector3 spawnPos = transform.position + (headRot * Vector3.right) * (previewScreenSideOffset * scale);
        Vector3 faceDir = spawnPos - headPos;
        Quaternion spawnRot = faceDir.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(faceDir, Vector3.up) : headRot;
        previewScreenGO.transform.SetPositionAndRotation(spawnPos, spawnRot);

        BasisPickupInteractable pickup = previewScreenGO.AddComponent<BasisPickupInteractable>();
        pickup.enableScaleWithGesture = true;
        pickup.minScalePercent = previewScreenMinScalePercent;
        pickup.maxScalePercent = previewScreenMaxScalePercent;
    }

    private void DespawnPreviewScreen()
    {
        if (previewScreenGO != null)
        {
            Destroy(previewScreenGO);
            previewScreenGO = null;
        }
        if (previewScreenMaterial != null)
        {
            Destroy(previewScreenMaterial);
            previewScreenMaterial = null;
        }
    }

    /// <summary>Keeps the screen bound to the current direct-to-screen feed (the static RT can change).</summary>
    private void UpdatePreviewScreenTexture()
    {
        if (previewScreenGO == null || previewScreenMaterial == null) return;
        BindPreviewScreenFeed(CopyCameraColorToStaticRTFeature.OutputRT);
    }

    private void BindPreviewScreenFeed(RenderTexture feed)
    {
        if (feed == null || previewScreenMaterial == null) return;
        if (previewScreenMaterial.mainTexture == feed) return;
        previewScreenMaterial.mainTexture = feed;
        previewScreenMaterial.SetTexture("_MainTex", feed);
    }
}
