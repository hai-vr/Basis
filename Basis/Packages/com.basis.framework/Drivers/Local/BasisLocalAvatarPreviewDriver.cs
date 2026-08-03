using Basis.BasisUI;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Device_Management;
using Basis.Scripts.TransformBinders;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Basis.Scripts.Drivers
{
    /// <summary>
    /// Creates a secondary camera that renders only the local avatar layer (layer 6)
    /// and displays the result on a HUD sprite spanning the bottom-right half of the screen.
    /// Off by default; toggled via the AvatarPreview setting.
    /// All objects are created at runtime and cleaned up on disable/destroy.
    /// </summary>
    [System.Serializable]
    public class BasisLocalAvatarPreviewDriver
    {
        [Header("Render Texture")]
        public static int TextureWidth = 768;
        public static int TextureHeight = 1024;

        /// <summary>On-screen size as a multiple of the original (1.0 = bottom-right quadrant height).</summary>
        public static float DisplaySizeScale = 1.1f;

        [Header("Preview Camera")]
        /// <summary>Vertical FOV in degrees. Camera distance is auto-computed each frame to fit the target framing.</summary>
        public float CameraFieldOfView = 40f;

        // Vertical framing target as fractions of player height: bottom of frame is just under
        // the hips, top is just above the head. Horizontal extent follows from the texture aspect.
        private const float FrameBottomFrac = 0.45f;
        private const float FrameTopFrac    = 1.15f;

        // When framing from avatar geometry: the frame bottom sits this fraction of the hip→crown
        // span below the hips bone, and the frame top this fraction above the crown.
        private const float HipDropFrac = 0.15f;
        private const float CrownHeadroomFrac = 0.08f;

        // Hand-immunity cap: the frame top never rises above the head bone by more than this
        // fraction of the hip→head-bone distance, so a raised arm — which also lands inside the
        // renderer bounds — can't blow out the shot. Generous for hair/most hats; very tall hats clip.
        private const float CrownCapRatio = 0.8f;

        [System.NonSerialized] public Camera PreviewCamera;
        [System.NonSerialized] public RenderTexture PreviewRT;

        private BasisLocalCameraDriver cachedDriver;
        private GameObject cameraGO;
        private GameObject parentOfUIGO;
        private GameObject displayGO;
        private SpriteRenderer displaySpriteRenderer;
        private MaterialPropertyBlock propertyBlock;
        private Texture2D dummyTexture;
        private readonly Vector3[] frustumCorners = new Vector3[4];
        private bool initialized;
        private bool active;
        private Basis.BasisRenderRateLimiter renderRateLimiter;

        /// <summary>
        /// Stores the driver reference and reads the saved setting.
        /// Only creates rendering objects if the setting is enabled.
        /// </summary>
        public void Initialize(BasisLocalCameraDriver cameraDriver)
        {
            cachedDriver = cameraDriver;

            // Apply the persisted setting
            bool enabled = BasisSettingsDefaults.AvatarPreview.RawValue;
            if (enabled)
            {
                CreateObjects();
            }
        }

        /// <summary>
        /// Enables or disables the avatar preview at runtime.
        /// Called by the settings module when the user toggles the setting.
        /// </summary>
        public void SetEnabled(bool enabled)
        {
            if (enabled && !active)
            {
                CreateObjects();
            }
            else if (!enabled && active)
            {
                DestroyObjects();
            }
        }

        public void SetMirror(bool mirrored)
        {
            if (displaySpriteRenderer != null)
            {
                displaySpriteRenderer.flipX = mirrored;
            }
        }

        private void CreateObjects()
        {
            if (initialized) return;
            if (cachedDriver == null) return;

            // --- Render Texture ---
            var desc = new RenderTextureDescriptor(TextureWidth, TextureHeight, RenderTextureFormat.ARGB32, 16)
            {
                msaaSamples = BasisCameraTargetMsaa.Clamp(2),
                useMipMap = false,
                autoGenerateMips = false,
                sRGB = QualitySettings.activeColorSpace == ColorSpace.Linear
            };
            PreviewRT = new RenderTexture(desc) { name = "AvatarPreviewRT" };
            PreviewRT.Create();

            // --- Camera (world-space, not parented to anything) ---
            cameraGO = new GameObject("BasisAvatarPreviewCamera");
            PreviewCamera = cameraGO.AddComponent<Camera>();
            PreviewCamera.cullingMask = 1 << BasisLayerMapper.LocalAvatarLayer;
            PreviewCamera.targetTexture = PreviewRT;
            PreviewCamera.clearFlags = CameraClearFlags.SolidColor;
            PreviewCamera.backgroundColor = Color.clear;
            PreviewCamera.depth = -10;
            PreviewCamera.fieldOfView = CameraFieldOfView;
            PreviewCamera.nearClipPlane = 0.01f;
            PreviewCamera.farClipPlane = 10f;
            PreviewCamera.allowHDR = false;
            PreviewCamera.allowMSAA = true;
            PreviewCamera.useOcclusionCulling = false;

            var urpData = cameraGO.GetComponent<UniversalAdditionalCameraData>();
            if (urpData != null)
            {
                urpData.allowXRRendering = false;
            }

            parentOfUIGO = new GameObject("AvatarPreviewParentOfUI");
            parentOfUIGO.layer = LayerMask.NameToLayer("UI");
            parentOfUIGO.transform.SetParent(cachedDriver.transform, false);

            displayGO = new GameObject("AvatarPreviewDisplay");
            displayGO.layer = LayerMask.NameToLayer("UI");
            displayGO.transform.SetParent(parentOfUIGO.transform, false);
            displayGO.transform.localRotation = Quaternion.identity;

            displaySpriteRenderer = displayGO.AddComponent<SpriteRenderer>();
            displaySpriteRenderer.sharedMaterial = new Material(Shader.Find("Basis/UI/Main"));
            displaySpriteRenderer.flipX = BasisSettingsDefaults.AvatarPreviewMirror.RawValue;

            // Create a dummy sprite for mesh/UV generation (full 0-1 UVs)
            dummyTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            dummyTexture.SetPixel(0, 0, Color.white);
            dummyTexture.Apply();
            displaySpriteRenderer.sprite = Sprite.Create(dummyTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);

            // MaterialPropertyBlock overrides SpriteRenderer's internal texture
            propertyBlock = new MaterialPropertyBlock();
            propertyBlock.SetTexture("_MainTex", PreviewRT);
            displaySpriteRenderer.SetPropertyBlock(propertyBlock);

            initialized = true;
            active = true;
        }

        private void DestroyObjects()
        {
            initialized = false;
            active = false;

            if (displaySpriteRenderer != null)
            {
                if (displaySpriteRenderer.sharedMaterial != null) Object.Destroy(displaySpriteRenderer.sharedMaterial);
                if (displaySpriteRenderer.sprite != null) Object.Destroy(displaySpriteRenderer.sprite);
            }

            if (cameraGO != null) { Object.Destroy(cameraGO); cameraGO = null; }
            if (displayGO != null) { Object.Destroy(displayGO); displayGO = null; }
            if (parentOfUIGO != null) { Object.Destroy(parentOfUIGO); parentOfUIGO = null; }

            if (PreviewRT != null)
            {
                PreviewRT.Release();
                Object.Destroy(PreviewRT);
                PreviewRT = null;
            }

            if (dummyTexture != null)
            {
                Object.Destroy(dummyTexture);
                dummyTexture = null;
            }

            displaySpriteRenderer = null;
            propertyBlock = null;
            PreviewCamera = null;
        }

        /// <summary>
        /// Positions the preview camera in front of the local avatar.
        /// The avatar head remains at normal scale during this camera's render
        /// because head-scaling is only applied to the main camera via entity ID checks.
        /// </summary>
        public void Simulate()
        {
            if (!active)
            {
                return;
            }
            Vector3 feetPos = BasisLocalPlayer.Instance.transform.position;
            float playerHeight = BasisHeightDriver.SelectedScaledPlayerHeight;

            // Frame from just under the hips to just above the crown using the avatar's own
            // geometry, so the shot fits each avatar's proportions and re-fits when swapped.
            // Falls back to player-height fractions while the avatar/bones aren't ready yet.
            if (!TryGetAvatarVerticalFraming(feetPos.y, playerHeight, out float frameBottomY, out float frameTopY))
            {
                frameBottomY = feetPos.y + playerHeight * FrameBottomFrac;
                frameTopY = feetPos.y + playerHeight * FrameTopFrac;
            }

            float verticalSpan = frameTopY - frameBottomY;
            Vector3 frameCenter = feetPos;
            frameCenter.y = (frameBottomY + frameTopY) * 0.5f;

            // Distance is bound by the vertical target (hips-to-above-head). Horizontal extent is
            // whatever the texture aspect allows at that distance — with the portrait texture this
            // is ~0.3× player height wide, enough for the body but not spread arms.
            float aspect = (float)TextureWidth / (float)TextureHeight;
            float halfFovTan = Mathf.Tan(CameraFieldOfView * 0.5f * Mathf.Deg2Rad);
            if (halfFovTan < 1e-4f) halfFovTan = 1e-4f;
            float cameraDistance = (verticalSpan * 0.5f) / halfFovTan;

            Vector3 forward = BasisLocalCameraDriver.HeadForward();
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
            forward.Normalize();

            Vector3 cameraPos = frameCenter + forward * cameraDistance;

            PreviewCamera.transform.SetPositionAndRotation(
                cameraPos,
                Quaternion.LookRotation(frameCenter - cameraPos));

            if (displayGO != null && parentOfUIGO != null && cachedDriver != null && cachedDriver.Camera != null)
            {
                Camera cam = cachedDriver.Camera;
                cam.CalculateFrustumCorners(new Rect(0, 0, 1, 1), 1f, Camera.MonoOrStereoscopicEye.Left, frustumCorners);
                float halfW = (frustumCorners[2] - frustumCorners[1]).magnitude * 0.5f;
                float halfH = (frustumCorners[1] - frustumCorners[0]).magnitude * 0.5f;

                Vector3 viewportPos = BasisDeviceManagement.IsMobileHardware()
                    ? cachedDriver.MobileMicrophoneViewportPosition
                    : cachedDriver.DesktopMicrophoneViewportPosition;
                viewportPos.x = 1f - viewportPos.x;
                Vector3 parentWorld = cam.ViewportToWorldPoint(viewportPos);
                parentOfUIGO.transform.localPosition = cam.transform.InverseTransformPoint(parentWorld);

                float displayHeight = halfH * DisplaySizeScale;
                float displayWidth = displayHeight * aspect;
                displayGO.transform.localScale = new Vector3(displayWidth, displayHeight, 1f);

                // Anchor the sprite's bottom-right corner to the frustum's bottom-right corner.
                Vector3 displayCameraLocal = new Vector3(halfW - displayWidth * 0.5f, -halfH + displayHeight * 0.5f, 1f);
                displayGO.transform.localPosition = displayCameraLocal - parentOfUIGO.transform.localPosition;
            }

            PreviewCamera.enabled = renderRateLimiter.AllowThisFrame(
                Time.unscaledDeltaTime, BasisSettingsDefaults.AvatarPreviewRenderHz.RawValue,
                BasisSettingsDefaults.LimitAvatarPreviewRate.RawValue);
        }

        /// <summary>
        /// Computes the world-space vertical frame bounds from the avatar's hips bone and the top
        /// of its renderer bounds (capped to a head-bone-relative ceiling so raised arms don't blow
        /// the framing out), so tall heads fit and the framing re-fits on avatar swap. Returns false
        /// when the avatar isn't ready.
        /// </summary>
        private bool TryGetAvatarVerticalFraming(float feetY, float playerHeight, out float bottomY, out float topY)
        {
            bottomY = 0f;
            topY = 0f;

            var localPlayer = BasisLocalPlayer.Instance;
            var avatar = localPlayer != null ? localPlayer.BasisAvatar : null;
            if (avatar == null)
            {
                return false;
            }

            var renderers = avatar.SkinnedMeshRenderers;
            if (renderers == null || renderers.Length == 0)
            {
                return false;
            }

            float boundsTop = float.NegativeInfinity;
            bool foundTop = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }
                float rendererTop = renderer.bounds.max.y;
                if (rendererTop > boundsTop)
                {
                    boundsTop = rendererTop;
                }
                foundTop = true;
            }
            if (!foundTop)
            {
                return false;
            }

            var mapping = BasisLocalAvatarDriver.Mapping;
            bool hasHips = mapping != null && mapping.HasHips && mapping.Hips != null;
            bool hasHead = mapping != null && mapping.Hashead && mapping.head != null;
            float hipY = hasHips ? mapping.Hips.position.y : feetY + playerHeight * FrameBottomFrac;

            // Renderer bounds give a tight fit to the real crown (hair/ears/hat), but a raised arm
            // also lands inside them — so clamp the top to one head-height above the head bone.
            float crownY = boundsTop;
            if (hasHead && hasHips)
            {
                float headY = mapping.head.position.y;
                float headReference = headY - hipY;
                if (headReference < 1e-3f)
                {
                    headReference = playerHeight * 0.5f;
                }
                float ceiling = headY + headReference * CrownCapRatio;
                if (crownY > ceiling)
                {
                    crownY = ceiling;
                }
                if (crownY < headY)
                {
                    crownY = headY;
                }
            }

            float span = crownY - hipY;
            if (span <= 1e-3f)
            {
                return false;
            }

            bottomY = hipY - span * HipDropFrac;
            topY = crownY + span * CrownHeadroomFrac;
            return true;
        }

        /// <summary>
        /// Destroys all runtime objects. Safe to call multiple times.
        /// </summary>
        public void Cleanup()
        {
            DestroyObjects();
            cachedDriver = null;
        }
    }
}
