using Basis.Scripts.BasisSdk.Helpers;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Device_Management;
using Basis.Scripts.TransformBinders;
using System.Collections;
using SteamAudio;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;
using Vector3 = UnityEngine.Vector3;

namespace Basis.Scripts.Drivers
{
    public class BasisLocalCameraDriver : MonoBehaviour
    {
        public static bool HasInstance;
        public static BasisLocalCameraDriver Instance;
        public Camera Camera;
        public static int CameraInstanceID;
        public AudioListener Listener;
        public UniversalAdditionalCameraData CameraData;
        public SteamAudio.SteamAudioListener SteamAudioListener;
        public BasisLocalPlayer LocalPlayer;
        public int DefaultCameraFov = 90;

        // Static event to notify when the instance exists
        public static event System.Action InstanceExists;

        public BasisLockToInput BasisLockToInput;
        public bool HasEvents = false;

        // Parent transform that holds the icon (world-space under the camera/player)
        public Transform ParentOfUI;

        // Icon bits
        public SpriteRenderer SpriteRendererIcon;
        public Transform SpriteRendererIconTransform;
        public Sprite SpriteMicrophoneOn;
        public Sprite SpriteMicrophoneOff;

        // ===== Unified viewport anchor & sizing =====
        [Header("Icon Placement")]
        [Tooltip("Normalized viewport anchor for the icon (x,y in [0..1]). z is legacy depth fallback.")]
        public Vector3 DesktopMicrophoneViewportPosition = new(0.2f, 0.15f, 1f); // x,y = anchor; z = legacy depth

        [Tooltip("Meters in front of the camera to place the icon. (world space depth)")]
        public float DepthFromCamera = 1.0f;

        [Tooltip("How tall the icon should appear as a fraction of the screen height (e.g., 0.06 = 6%).")]
        [Range(0.01f, 0.25f)]
        public float ScreenHeightFraction = 0.06f;

        [Tooltip("Extra clamp margin to keep the icon away from the very edge of the view, on top of its own half-size.")]
        [Range(0.0f, 0.2f)]
        public float ViewportMargin = 0.03f;

        [Header("Mode Offsets (Viewport Units)")]
        [Tooltip("Additional viewport offset for Desktop (x=right, y=up).")]
        public Vector2 DesktopOffsetViewport = Vector2.zero;

        [Tooltip("Additional viewport offset for VR/XR (x=right, y=up). Defaults to 0.15,0.15 to push into view on some HMDs.")]
        public Vector2 VROffsetViewport = new Vector2(0.15f, 0.15f);

        // (Deprecated) Kept for backwards compatibility but no longer used for placement.
        [HideInInspector] public Vector3 VRMicrophoneOffset = new Vector3(-0.0004f, -0.0015f, 2f);

        public AudioClip MuteSound;
        public AudioClip UnMuteSound;
        public AudioSource AudioSource;
        public float NearClip = 0.001f;
        private Coroutine scaleCoroutine;

        public Vector3 StartingScale = Vector3.zero;
        public float duration = 0.35f;
        public float halfDuration;
        public Vector3 largerScale;
        public static Vector3 LeftEye;
        public static Vector3 RightEye;

        public Color UnMutedMutedIconColorActive = Color.white;
        public Color UnMutedMutedIconColorInactive = Color.grey;
        public Color MutedColor = Color.grey;

        // Runtime flags
        public bool LocalIsTransmitting = false;

        // Cached sprite local size and aspect for scale math
        private float _spriteLocalHeight = 1f; // in local units of the sprite
        private float _spriteAspect = 1f;      // width / height

        private void OnValidate()
        {
            // Convenience: if dev set z in DesktopMicrophoneViewportPosition but forgot DepthFromCamera,
            // pick it up so we don't silently use 1.0f.
            if (DepthFromCamera <= 0.0001f && DesktopMicrophoneViewportPosition.z > 0.0001f)
            {
                DepthFromCamera = DesktopMicrophoneViewportPosition.z;
            }
        }

        public void OnEnable()
        {
            if (BasisHelpers.CheckInstance(Instance))
            {
                Instance = this;
                HasInstance = true;
            }

            Camera.nearClipPlane = NearClip;
            Camera.farClipPlane = 1500;
            CameraInstanceID = Camera.GetInstanceID();

            // Cache sprite local size and aspect (bounds are in local sprite space)
            if (SpriteRendererIcon != null && SpriteRendererIcon.sprite != null)
            {
                var sz = SpriteRendererIcon.sprite.bounds.size;
                _spriteLocalHeight = Mathf.Max(0.0001f, sz.y);
                _spriteAspect = Mathf.Clamp(sz.x / Mathf.Max(0.0001f, sz.y), 0.1f, 10f);
            }
            else
            {
                _spriteLocalHeight = 1f;
                _spriteAspect = 1f;
            }

            OnHeightChanged();

            if (HasEvents == false)
            {
                BasisLocalMicrophoneDriver.OnPausedAction += OnPausedEvent;
                BasisLocalMicrophoneDriver.MainThreadOnHasAudio += MicrophoneTransmitting;
                BasisLocalMicrophoneDriver.MainThreadOnHasSilence += MicrophoneNotTransmitting;
                RenderPipelineManager.beginCameraRendering += BeginCameraRendering;
                BasisDeviceManagement.OnBootModeChanged += OnModeSwitch;
                BasisLocalPlayer.OnPlayersHeightChangedNextFrame += OnHeightChanged;
                InstanceExists?.Invoke();
                HasEvents = true;
            }

            halfDuration = duration / 2f; // Time to scale up and down
            StartingScale = SpriteRendererIcon != null ? SpriteRendererIcon.transform.localScale : Vector3.one;
            largerScale = StartingScale * 1.2f;

            UpdateMicrophoneVisuals(BasisLocalMicrophoneDriver.isPaused, false);

#if STEAMAUDIO_ENABLED
            if (SteamAudioListener != null)
            {
                SteamAudioManager.NotifyAudioListenerChanged();
            }
#endif
            if (SpriteRendererIcon != null)
                SpriteRendererIcon.gameObject.SetActive(true);
        }

        public void MicrophoneTransmitting()
        {
            if (SpriteRendererIcon != null)
            {
                SpriteRendererIcon.color = UnMutedMutedIconColorActive;
            }
            if (SpriteRendererIconTransform != null)
                SpriteRendererIconTransform.localScale = largerScale;

            LocalIsTransmitting = true;
        }

        public void MicrophoneNotTransmitting()
        {
            if (SpriteRendererIcon != null)
            {
                SpriteRendererIcon.color = UnMutedMutedIconColorInactive;
            }
            if (SpriteRendererIconTransform != null)
                SpriteRendererIconTransform.localScale = StartingScale;

            LocalIsTransmitting = false;
        }

        private void OnPausedEvent(bool IsMuted)
        {
            UpdateMicrophoneVisuals(IsMuted, true);
        }

        public void UpdateMicrophoneVisuals(bool IsMuted, bool PlaySound)
        {
            if (scaleCoroutine != null)
            {
                StopCoroutine(scaleCoroutine);
            }

            if (SpriteRendererIcon == null) return;

            if (IsMuted)
            {
                SpriteRendererIcon.sprite = SpriteMicrophoneOff;
                if (PlaySound && AudioSource != null && MuteSound != null)
                    AudioSource.PlayOneShot(MuteSound);

                SpriteRendererIcon.color = MutedColor;
                scaleCoroutine = StartCoroutine(ScaleIcons(SpriteRendererIcon.gameObject));
            }
            else
            {
                SpriteRendererIcon.sprite = SpriteMicrophoneOn;
                if (PlaySound && AudioSource != null && UnMuteSound != null)
                    AudioSource.PlayOneShot(UnMuteSound);

                SpriteRendererIcon.color = LocalIsTransmitting ? UnMutedMutedIconColorActive : UnMutedMutedIconColorInactive;
                scaleCoroutine = StartCoroutine(ScaleIcons(SpriteRendererIconTransform.gameObject));
            }
        }

        private IEnumerator ScaleIcons(GameObject iconToScale)
        {
            if (iconToScale == null) yield break;

            float time = 0f;

            // Phase 1: Scale up
            while (time < halfDuration)
            {
                time += Time.deltaTime;
                float t = time / halfDuration;
                iconToScale.transform.localScale = Vector3.Lerp(StartingScale, largerScale, t);
                yield return null;
            }

            iconToScale.transform.localScale = largerScale;

            // Phase 2: Scale down
            time = 0f;
            while (time < halfDuration)
            {
                time += Time.deltaTime;
                float t = time / halfDuration;
                iconToScale.transform.localScale = Vector3.Lerp(largerScale, StartingScale, t);
                yield return null;
            }

            iconToScale.transform.localScale = StartingScale;
        }

        public void OnDestroy()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCameraRendering;
            BasisDeviceManagement.OnBootModeChanged -= OnModeSwitch;
            BasisLocalPlayer.OnPlayersHeightChangedNextFrame -= OnHeightChanged;
            BasisLocalMicrophoneDriver.OnPausedAction -= OnPausedEvent;
            HasEvents = false;
            HasInstance = false;
        }

        private void OnModeSwitch(string mode)
        {
            if (mode == BasisConstants.Desktop)
            {
                Camera.fieldOfView = DefaultCameraFov;
            }
            OnHeightChanged();
        }

        public static Vector3 Forward()
        {
            if (HasInstance) return Instance.transform.forward;
            return Vector3.zero;
        }

        public static Vector3 Up()
        {
            if (HasInstance) return Instance.transform.up;
            return Vector3.zero;
        }

        public static Vector3 Right()
        {
            if (HasInstance) return Instance.transform.right;
            return Vector3.zero;
        }

        public static Vector3 Position;
        public static Quaternion Rotation;

        public static Vector3 LeftEyePosition()
        {
            if (BasisDeviceManagement.IsUserInDesktop())
            {
                return Instance.transform.position;
            }
            else
            {
                return LeftEye;
            }
        }

        public static Vector3 RightEyePosition()
        {
            if (BasisDeviceManagement.IsUserInDesktop())
            {
                return Instance.transform.position;
            }
            else
            {
                return RightEye;
            }
        }

        public static void GetPositionAndRotation(out Vector3 Position, out Quaternion Rotation)
        {
            if (HasInstance)
            {
                Instance.transform.GetPositionAndRotation(out Position, out Rotation);
            }
            else
            {
                Position = Vector3.zero;
                Rotation = Quaternion.identity;
            }
        }

        public void OnHeightChanged()
        {
            // Keep avatar/camera scaling as your code intended
            this.transform.localScale = Vector3.one * LocalPlayer.CurrentHeight.SelectedAvatarToAvatarDefaultScale;

            // IMPORTANT:
            // Do NOT set ParentOfUI.localScale here based on parent scale.
            // We compute proper screen-constant scaling each frame in BeginCameraRendering.
        }

        public void OnDisable()
        {
            if (BasisLocalAvatarDriver.References != null && BasisLocalAvatarDriver.References.head != null)
            {
                BasisLocalAvatarDriver.References.head.localScale = BasisLocalAvatarDriver.HeadScale;
            }
            if (HasEvents)
            {
                RenderPipelineManager.beginCameraRendering -= BeginCameraRendering;
                BasisDeviceManagement.OnBootModeChanged -= OnModeSwitch;
                BasisLocalMicrophoneDriver.MainThreadOnHasAudio -= MicrophoneTransmitting;
                BasisLocalMicrophoneDriver.MainThreadOnHasSilence -= MicrophoneNotTransmitting;
                HasEvents = false;
            }
        }

        public void BeginCameraRendering(ScriptableRenderContext context, Camera cam)
        {
            if (!BasisLocalAvatarDriver.References.Hashead) return;

            if (cam.GetInstanceID() == CameraInstanceID)
            {
                this.transform.GetPositionAndRotation(out Position, out Rotation);
                BasisLocalAvatarDriver.ScaleheadToZero();

                // Unified placement for Desktop and XR using viewport coordinates
                UpdateIconPlacementAndScale(cam);
            }
            else
            {
                BasisLocalAvatarDriver.ScaleHeadToNormal();
            }
        }

        /// <summary>
        /// Places the icon at a viewport anchor and scales it to a constant on-screen size,
        /// independent of avatar scale, FOV, and distance. Clamps using the icon's viewport footprint
        /// so it stays fully inside the frustum on ultra-wide headsets (Index, Varjo, etc.).
        /// Supports per-mode (Desktop vs VR) viewport offsets.
        /// </summary>
        private void UpdateIconPlacementAndScale(Camera cam)
        {
            if (ParentOfUI == null || SpriteRendererIcon == null || cam == null) return;

            // Base anchor from inspector
            float baseX = Mathf.Clamp01(DesktopMicrophoneViewportPosition.x);
            float baseY = Mathf.Clamp01(DesktopMicrophoneViewportPosition.y);

            // Determine mode & choose offset
            bool xrActive = (CameraData != null && CameraData.allowXRRendering) ||
                            (XRSettings.enabled && XRSettings.isDeviceActive);

            Vector2 modeOffset = xrActive ? VROffsetViewport : DesktopOffsetViewport;

            // Apply mode-specific viewport offset BEFORE clamping
            float vx = baseX + modeOffset.x;
            float vy = baseY + modeOffset.y;

            // Depth: prefer explicit setting, otherwise legacy z
            float depth = DepthFromCamera > 0.0001f ? DepthFromCamera : Mathf.Max(0.05f, DesktopMicrophoneViewportPosition.z);

            // ----- Screen-constant scaling -----
            // Desired world-space height to occupy ScreenHeightFraction of the screen height:
            // H = 2 * d * tan(FOV/2) * fraction
            float clampedFrac = Mathf.Clamp(ScreenHeightFraction, 0.005f, 0.9f);
            float worldHeight = 2f * depth * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * clampedFrac;

            // Compensate ancestor scales & sprite local size to compute local scale
            float ancestorsScaleY = (ParentOfUI.parent != null) ? ParentOfUI.parent.lossyScale.y : 1f;
            float spriteLocalHeight = Mathf.Max(0.0001f, _spriteLocalHeight);
            float requiredLocalScaleY = worldHeight / (spriteLocalHeight * ancestorsScaleY);
            ParentOfUI.localScale = new Vector3(requiredLocalScaleY, requiredLocalScaleY, requiredLocalScaleY);

            // Billboard towards camera
            ParentOfUI.rotation = cam.transform.rotation;

            // ----- Clamp by the icon's footprint in viewport space -----
            // The icon’s on-screen height in viewport-y units is exactly clampedFrac.
            // Its width in viewport-x units depends on sprite aspect and camera aspect:
            // width_vp = height_vp * spriteAspect * (pixelHeight/pixelWidth)
            float heightVP = clampedFrac;
            float camPixelW = Mathf.Max(1f, (float)cam.pixelWidth);
            float camPixelH = Mathf.Max(1f, (float)cam.pixelHeight);
            float widthVP = heightVP * _spriteAspect * (camPixelH / camPixelW);

            float halfH = 0.5f * heightVP;
            float halfW = 0.5f * widthVP;

            float margin = Mathf.Max(0f, ViewportMargin);

            // Final clamped anchor (ensures full sprite stays on-screen)
            vx = Mathf.Clamp(vx, margin + halfW, 1f - margin - halfW);
            vy = Mathf.Clamp(vy, margin + halfH, 1f - margin - halfH);

            // World position at the clamped viewport anchor/depth
            Vector3 worldPoint = cam.ViewportToWorldPoint(new Vector3(vx, vy, depth));
            ParentOfUI.position = worldPoint;

            // Safety: if projection has lens shift/asymmetry, nudge back into bounds
            Vector3 vp = cam.WorldToViewportPoint(ParentOfUI.position);
            if (vp.z > 0f)
            {
                bool outX = (vp.x < (margin + halfW)) || (vp.x > (1f - margin - halfW));
                bool outY = (vp.y < (margin + halfH)) || (vp.y > (1f - margin - halfH));
                if (outX || outY)
                {
                    float targetX = Mathf.Clamp(vp.x, margin + halfW, 1f - margin - halfW);
                    float targetY = Mathf.Clamp(vp.y, margin + halfH, 1f - margin - halfH);
                    Vector3 corrected = cam.ViewportToWorldPoint(new Vector3(targetX, targetY, depth));
                    ParentOfUI.position = corrected;
                }
            }
        }

        public static void AllowXRRenderering(bool AllowXRRendering)
        {
            if (Instance != null)
            {
                Instance.CameraData.allowXRRendering = AllowXRRendering;
            }
            else
            {
                BasisDebug.LogError("Missing Instance of Local CameraDriver!", BasisDebug.LogTag.Camera);
            }
        }
    }
}
