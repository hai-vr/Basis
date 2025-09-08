using Basis.Scripts.BasisSdk.Helpers;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Device_Management;
using Basis.Scripts.TransformBinders;
using System.Collections;
using SteamAudio;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Vector3 = UnityEngine.Vector3;
using System;
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
        public static event Action InstanceExists;
        public BasisLockToInput BasisLockToInput;
        public bool HasEvents = false;
        public Transform ParentOfUI;
        public SpriteRenderer SpriteRendererIcon;
        public Transform SpriteRendererIconTransform;
        public Sprite SpriteMicrophoneOn;
        public Sprite SpriteMicrophoneOff;

        public Vector3 DesktopMicrophoneViewportPosition = new(0.2f, 0.15f, 1f); // Adjust as needed for canvas position and depth

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

        // Normalized [-1..1] desired position within the visible frustum where (0,0) is center.
        // (-1,-1)=bottom-left edge, (1,1)=top-right edge.
        // Think of it as "anchoring" inside the HMD view, independent of resolution.
        public Vector2 VRdesiredNormXY = new Vector2(-0.42f, -0.52f);

        // Optional additional normalized padding (percentage of frustum half-width/half-height)
        [Range(0f, 0.2f)]
        public float VRextraViewportPad = 0.022f;
        public Vector2 iconHalfRU;
        Vector3[] corners = new Vector3[4];
        Rect FrustumRequest = new Rect(0, 0, 1, 1);
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
            //fire static event that says the instance exists
            OnHeightChanged();
            if (HasEvents == false)
            {
                BasisLocalMicrophoneDriver.OnPausedAction += OnPausedEvent;
                BasisLocalMicrophoneDriver.MainThreadOnHasAudio += MicrophoneTransmitting;
                BasisLocalMicrophoneDriver.MainThreadOnHasSilence += MicrophoneNotTransmitting;
                RenderPipelineManager.beginCameraRendering += BeginCameraRendering;
                RenderPipelineManager.endCameraRendering += EndCameraRendering;
                BasisDeviceManagement.OnBootModeChanged += OnModeSwitch;
                BasisLocalPlayer.OnPlayersHeightChangedNextFrame += OnHeightChanged;
                InstanceExists?.Invoke();
                HasEvents = true;
            }
            halfDuration = duration / 2f; // Time to scale up and down
            StartingScale = SpriteRendererIcon.transform.localScale;
            // Target scale for the "bounce" effect (e.g., 1.2 times larger)
            largerScale = StartingScale * 1.2f;
            UpdateMicrophoneVisuals(BasisLocalMicrophoneDriver.isPaused, false);

#if STEAMAUDIO_ENABLED
            if (SteamAudioListener != null)
            {
                SteamAudioManager.NotifyAudioListenerChanged();
            }
#endif
            SpriteRendererIcon.gameObject.SetActive(true);

            // 2) Icon half-size in meters, in camera-local axes
            iconHalfRU = GetIconHalfSizeRUInCameraSpace(Camera, ParentOfUI);
        }

        public void MicrophoneTransmitting()
        {
            SpriteRendererIcon.color = UnMutedMutedIconColorActive;
            SpriteRendererIconTransform.localScale = largerScale;
            LocalIsTransmitting = true;
        }
        public void MicrophoneNotTransmitting()
        {
            SpriteRendererIcon.color = UnMutedMutedIconColorInactive;
            SpriteRendererIconTransform.localScale = StartingScale;
            LocalIsTransmitting = false;
        }
        public bool LocalIsTransmitting = false;
        private void OnPausedEvent(bool IsMuted)
        {
            UpdateMicrophoneVisuals(IsMuted, true);
        }
        public void UpdateMicrophoneVisuals(bool IsMuted, bool PlaySound)
        {
            // BasisDebug.Log(nameof(UpdateMicrophoneVisuals));
            // Cancel the current coroutine if it's running
            if (scaleCoroutine != null)
            {
                StopCoroutine(scaleCoroutine);
            }
            if (IsMuted)
            {
                // BasisDebug.Log(nameof(UpdateMicrophoneVisuals) + IsMuted);
                SpriteRendererIcon.sprite = SpriteMicrophoneOff;
                if (PlaySound)
                {
                    AudioSource.PlayOneShot(MuteSound);

                }
                SpriteRendererIcon.color = MutedColor;
                // Start a new coroutine for the scale animation
                scaleCoroutine = StartCoroutine(ScaleIcons(SpriteRendererIcon.gameObject));
            }
            else
            {
                // BasisDebug.Log(nameof(UpdateMicrophoneVisuals) + IsMuted);

                SpriteRendererIcon.sprite = SpriteMicrophoneOn;
                if (PlaySound)
                {
                    AudioSource.PlayOneShot(UnMuteSound);
                }
                if (LocalIsTransmitting)
                {
                    SpriteRendererIcon.color = UnMutedMutedIconColorActive;

                }
                else
                {
                    SpriteRendererIcon.color = UnMutedMutedIconColorInactive;
                }
                // Start a new coroutine for the scale animation
                scaleCoroutine = StartCoroutine(ScaleIcons(SpriteRendererIconTransform.gameObject));
            }
        }
        private IEnumerator ScaleIcons(GameObject iconToScale)
        {
            float time = 0f;

            // Phase 1: Scale up
            while (time < halfDuration)
            {
                time += Time.deltaTime;
                float t = time / halfDuration;

                // Scale the icon up
                iconToScale.transform.localScale = Vector3.Lerp(StartingScale, largerScale, t);
                yield return null; // Wait for the next frame
            }

            // Ensure the final scale at the end of phase 1 is set to largerScale
            iconToScale.transform.localScale = largerScale;

            // Reset time for the second phase
            time = 0f;

            // Phase 2: Scale down
            while (time < halfDuration)
            {
                time += Time.deltaTime;
                float t = time / halfDuration;

                // Scale the icon down back to the original scale
                iconToScale.transform.localScale = Vector3.Lerp(largerScale, StartingScale, t);
                yield return null; // Wait for the next frame
            }

            // Ensure the final scale at the end of phase 2 is set to originalScale
            iconToScale.transform.localScale = StartingScale;
        }
        public void OnDestroy()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= EndCameraRendering;
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
            if (HasInstance)
            {
                return Instance.transform.forward;
            }
            else
            {
                return Vector3.zero;
            }
        }
        public static Vector3 Up()
        {
            if (HasInstance)
            {
                return Instance.transform.up;
            }
            else
            {
                return Vector3.zero;
            }
        }
        public static Vector3 Right()
        {
            if (HasInstance)
            {
                return Instance.transform.right;
            }
            else
            {
                return Vector3.zero;
            }
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

            //the normal users scale is 1.6m
            //so a avatar the size of 
            this.transform.localScale = Vector3.one * LocalPlayer.CurrentHeight.SelectedAvatarToAvatarDefaultScale;
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
                RenderPipelineManager.endCameraRendering -= EndCameraRendering;
                BasisDeviceManagement.OnBootModeChanged -= OnModeSwitch;
                BasisLocalMicrophoneDriver.MainThreadOnHasAudio -= MicrophoneTransmitting;
                BasisLocalMicrophoneDriver.MainThreadOnHasSilence -= MicrophoneNotTransmitting;
                HasEvents = false;
            }
        }
        private void EndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (BasisLocalAvatarDriver.References.Hashead)
            {
                if (Camera.GetInstanceID() == CameraInstanceID)
                {
                    BasisLocalAvatarDriver.ScaleHeadToNormal();
                }
            }
        }
        public void BeginCameraRendering(ScriptableRenderContext context, Camera Camera)
        {
            if (BasisLocalAvatarDriver.References.Hashead)
            {
                if (Camera.GetInstanceID() == CameraInstanceID)
                {
                    this.transform.GetPositionAndRotation(out Position, out Rotation);
                    BasisLocalAvatarDriver.ScaleheadToZero();

                    if (CameraData.allowXRRendering)
                    {
                        ParentOfUI.localPosition = CalculateClampedLocal(Camera);

                    }
                    else
                    {
                        Vector3 worldPoint = Camera.ViewportToWorldPoint(DesktopMicrophoneViewportPosition);
                        Vector3 localPos = this.transform.InverseTransformPoint(worldPoint);//asume this transform is also camera position
                        ParentOfUI.localPosition = localPos;
                    }
                }
            }
        }
        /// <summary>
        /// Places ParentOfUI in front of the camera at depth, using desired normalized coords in [-1..1],
        /// and clamps so the entire object (bounds) stays visible. Returns camera-local position.
        /// </summary>
        private Vector3 CalculateClampedLocal(Camera cam)
        {
            // 1) Frustum size at 'depth'
            // Use frustum corners to be robust to per-eye projections/FOVs.
            cam.CalculateFrustumCorners(FrustumRequest, 1, Camera.MonoOrStereoscopicEye.Left, corners);
            // corners: BL, TL, TR, BR in camera-local space
            // We want width/height at 'depth'
            Vector3 BL = corners[0];
            Vector3 TL = corners[1];
            Vector3 TR = corners[2];
            // In camera-local, right vector is along TR - TL, up is TL - BL; center is (BL+TR)/2
            float frustumWidth = (TR - TL).magnitude;
            float frustumHeight = (TL - BL).magnitude;
            float halfW = frustumWidth * 0.5f;
            float halfH = frustumHeight * 0.5f;

            // 3) Convert desired normXY [-1..1] to clamped norm so icon stays fully inside
            // Compute normalized “margins” required by the icon extents
            float marginU = Mathf.Clamp01(iconHalfRU.x / Mathf.Max(halfW, 1e-4f)) + VRextraViewportPad;
            float marginV = Mathf.Clamp01(iconHalfRU.y / Mathf.Max(halfH, 1e-4f)) + VRextraViewportPad;

            float u = Mathf.Clamp(VRdesiredNormXY.x, -1f + marginU, 1f - marginU);
            float v = Mathf.Clamp(VRdesiredNormXY.y, -1f + marginV, 1f - marginV);

            // 4) Build the camera-local position at depth using clamped normalized coords
            // Center point at depth on camera forward
            Vector3 centerAtDepth = cam.transform.InverseTransformPoint(Position + cam.transform.forward * BasisLocalPlayer.Instance.CurrentHeight.SelectedAvatarToAvatarDefaultScale);

            // Get camera-local right/up from corner vectors
            Vector3 rightLocal = (TR - TL).normalized;
            Vector3 upLocal = (TL - BL).normalized;

            Vector3 localPos = centerAtDepth + rightLocal * (u * halfW) + upLocal * (v * halfH);

            return localPos;
        }

        /// <summary>
        /// Computes the icon's half-size in meters projected onto the camera's right/up axes.
        /// Works for both 3D Renderers and world-space UI (RectTransform).
        /// </summary>
        private Vector2 GetIconHalfSizeRUInCameraSpace(Camera cam, Transform uiRoot)
        {
            // Bounds are in world space; project extents to camera right/up
            Vector3 ext = SpriteRendererIcon.bounds.extents;
            // We approximate by projecting the oriented extents to RU; this is conservative.
            Vector3 right = cam.transform.right;
            Vector3 up = cam.transform.up;

            // Build an oriented bounding "radius" along RU by sampling the 3 axes of the object
            // (handles rotated meshes). extents in local axes:
            Vector3 ex = uiRoot.TransformVector(new Vector3(ext.x * 2f, 0, 0)) * 0.5f;
            Vector3 ey = uiRoot.TransformVector(new Vector3(0, ext.y * 2f, 0)) * 0.5f;
            Vector3 ez = uiRoot.TransformVector(new Vector3(0, 0, ext.z * 2f)) * 0.5f;

            float halfRight = ProjectHalfOnAxis(right, ex, ey, ez);
            float halfUp = ProjectHalfOnAxis(up, ex, ey, ez);

            return new Vector2(Mathf.Abs(halfRight), Mathf.Abs(halfUp));
        }

        /// <summary>
        /// Projects the sum of half-axes onto a given axis; conservative half-size projection.
        /// </summary>
        private static float ProjectHalfOnAxis(Vector3 axis, params Vector3[] halfAxes)
        {
            axis = axis.normalized;
            float sum = 0f;
            for (int Index = 0; Index < halfAxes.Length; Index++)
                sum += Mathf.Abs(Vector3.Dot(axis, halfAxes[Index]));
            return sum;
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
