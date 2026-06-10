using Basis.BasisUI;
using UnityEngine;

namespace Basis.Scripts.Drivers
{
    /// <summary>
    /// Mirrors the active handheld camera's live preview onto a HUD sprite anchored to the
    /// top-right of the view, so the wearer can see what the camera sees without looking at
    /// the physical camera screen. Reuses that camera's existing preview render texture (no
    /// extra camera or render pass) and keeps it rendering while shown.
    /// Off by default; toggled via the CameraHud setting.
    /// </summary>
    [System.Serializable]
    public class BasisLocalCameraHudDriver
    {
        /// <summary>On-screen height as a fraction of half the view height (0.5 = quarter-screen tall).</summary>
        public static float DisplaySizeScale = 0.5f;

        private static readonly Rect FullRect = new Rect(0, 0, 1, 1);

        private BasisLocalCameraDriver cachedDriver;
        private GameObject displayGO;
        private SpriteRenderer displaySpriteRenderer;
        private MaterialPropertyBlock propertyBlock;
        private Texture2D dummyTexture;
        private RenderTexture boundTexture;
        private readonly Vector3[] frustumCorners = new Vector3[4];
        private bool active;
        private bool subscribed;

        /// <summary>Stores the driver reference and applies the persisted setting.</summary>
        public void Initialize(BasisLocalCameraDriver cameraDriver)
        {
            cachedDriver = cameraDriver;
            if (BasisSettingsDefaults.CameraHud.RawValue)
            {
                SetEnabled(true);
            }
        }

        /// <summary>Enables or disables the camera HUD at runtime (settings toggle).</summary>
        public void SetEnabled(bool enabled)
        {
            if (enabled == active)
            {
                return;
            }
            active = enabled;

            if (enabled)
            {
                if (!subscribed)
                {
                    BasisHandHeldCamera.ActiveChanged += OnActiveCameraChanged;
                    subscribed = true;
                }
                OnActiveCameraChanged();
            }
            else
            {
                if (subscribed)
                {
                    BasisHandHeldCamera.ActiveChanged -= OnActiveCameraChanged;
                    subscribed = false;
                }
                BasisHandHeldCamera.SetLivePreviewRequired(false);
                DestroyObjects();
            }
        }

        private void OnActiveCameraChanged()
        {
            bool hasCamera = active && BasisHandHeldCamera.Active != null;
            BasisHandHeldCamera.SetLivePreviewRequired(hasCamera);

            if (hasCamera)
            {
                CreateObjects();
            }
            else
            {
                DestroyObjects();
            }
        }

        private void CreateObjects()
        {
            if (displayGO != null) return;
            if (cachedDriver == null) return;

            displayGO = new GameObject("CameraHudDisplay");
            displayGO.layer = LayerMask.NameToLayer("UI");
            displayGO.transform.SetParent(cachedDriver.transform, false);
            displayGO.transform.localRotation = Quaternion.identity;

            displaySpriteRenderer = displayGO.AddComponent<SpriteRenderer>();
            displaySpriteRenderer.sharedMaterial = new Material(Shader.Find("Basis/UI/Main"));

            dummyTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            dummyTexture.SetPixel(0, 0, Color.white);
            dummyTexture.Apply();
            displaySpriteRenderer.sprite = Sprite.Create(dummyTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);

            propertyBlock = new MaterialPropertyBlock();
            boundTexture = null;
        }

        private void DestroyObjects()
        {
            if (displaySpriteRenderer != null)
            {
                if (displaySpriteRenderer.sharedMaterial != null) Object.Destroy(displaySpriteRenderer.sharedMaterial);
                if (displaySpriteRenderer.sprite != null) Object.Destroy(displaySpriteRenderer.sprite);
            }
            if (displayGO != null) { Object.Destroy(displayGO); displayGO = null; }
            if (dummyTexture != null) { Object.Destroy(dummyTexture); dummyTexture = null; }

            displaySpriteRenderer = null;
            propertyBlock = null;
            boundTexture = null;
        }

        /// <summary>
        /// Rebinds the active camera's current preview texture (it is swapped during capture) and
        /// anchors the sprite's top-right corner to the view's top-right corner, sized to the
        /// texture's aspect ratio.
        /// </summary>
        public void Simulate()
        {
            if (!active) return;

            BasisHandHeldCamera cam = BasisHandHeldCamera.Active;
            if (cam == null) return;

            if (displayGO == null)
            {
                CreateObjects();
                if (displayGO == null) return;
            }

            RenderTexture rt = cam.PreviewTexture;
            if (rt == null || !rt.IsCreated())
            {
                if (displayGO.activeSelf) displayGO.SetActive(false);
                return;
            }
            if (!displayGO.activeSelf) displayGO.SetActive(true);

            if (rt != boundTexture)
            {
                propertyBlock.SetTexture("_MainTex", rt);
                displaySpriteRenderer.SetPropertyBlock(propertyBlock);
                boundTexture = rt;
            }

            Camera viewCam = cachedDriver != null ? cachedDriver.Camera : null;
            if (viewCam == null) return;

            viewCam.CalculateFrustumCorners(FullRect, 1f, Camera.MonoOrStereoscopicEye.Left, frustumCorners);
            float halfW = (frustumCorners[2] - frustumCorners[1]).magnitude * 0.5f;
            float halfH = (frustumCorners[1] - frustumCorners[0]).magnitude * 0.5f;

            float aspect = (float)rt.width / rt.height;
            float displayHeight = halfH * DisplaySizeScale;
            float displayWidth = displayHeight * aspect;
            displayGO.transform.localScale = new Vector3(displayWidth, displayHeight, 1f);
            displayGO.transform.localPosition = new Vector3(halfW - displayWidth * 0.5f, halfH - displayHeight * 0.5f, 1f);
        }

        /// <summary>Destroys all runtime objects and unsubscribes. Safe to call multiple times.</summary>
        public void Cleanup()
        {
            SetEnabled(false);
            cachedDriver = null;
        }
    }
}
