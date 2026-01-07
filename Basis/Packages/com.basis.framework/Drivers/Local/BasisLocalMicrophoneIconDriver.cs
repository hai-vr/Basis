using UnityEngine;
using Vector3 = UnityEngine.Vector3;

namespace Basis.Scripts.Drivers
{
    [System.Serializable]
    public class BasisLocalMicrophoneIconDriver
    {
        public enum MicrophoneDisplayMode
        {
            Off,
            AlwaysVisible,
            ActivityDetection,
        }

        // --- Config / References ---
        public MicrophoneDisplayMode DisplayMode = MicrophoneDisplayMode.AlwaysVisible;
        public SpriteRenderer SpriteRendererIcon;
        public Transform SpriteRendererIconTransform;
        public Sprite SpriteMicrophoneOn;
        public Sprite SpriteMicrophoneOff;

        public Vector2 VRdesiredNormXY = new Vector2(-0.42f, -0.52f);

        [Range(0f, 0.2f)]
        public float VRextraViewportPad = 0.022f;

        public Vector2 iconHalfRU;
        private readonly Vector3[] corners = new Vector3[4];
        private Rect FrustumRequest = new Rect(0, 0, 1, 1);

        // --- State ---
        public bool LocalIsTransmitting;
        public bool IsCurrentlyMuted { get; private set; }

        // Colors
        public Color UnMutedMutedIconColorActive = Color.white;
        public Color UnMutedMutedIconColorInactive = Color.grey;
        public Color MutedColor = Color.grey;

        // Scale / FX
        public Vector3 StartingScale = Vector3.zero;
        public Vector3 largerScale;

        // Audio
        public AudioClip MuteSound;
        public AudioClip UnMuteSound;
        public AudioSource AudioSource;

        // Timing
        public float duration = 0.35f;
        public float halfDuration;

        // Owner
        public BasisLocalCameraDriver CameraDriver;

        // --- Scale animation state (Update/LateUpdate driven) ---
        private float scaleTime = 0f;
        private bool scalingUp = true;
        private bool isScaling = false;

        // ---------------- Initialization ----------------
        public void Initalize(BasisLocalCameraDriver CameraDriver)
        {
            this.CameraDriver = CameraDriver;

            halfDuration = duration / 2f;

            if (SpriteRendererIcon != null)
            {
                SpriteRendererIconTransform = SpriteRendererIcon.transform;
                StartingScale = SpriteRendererIconTransform.localScale;
                largerScale = StartingScale * 1.2f;
            }

            // Ensure initial visibility matches current mode/state
            ApplyDisplayModeVisibility(true);
        }

        // ---------------- Layout Helpers ----------------
        public Vector3 CalculateClampedLocal(Camera cam, Vector3 Position)
        {
            cam.CalculateFrustumCorners(FrustumRequest, 1, Camera.MonoOrStereoscopicEye.Left, corners);

            Vector3 BL = corners[0];
            Vector3 TL = corners[1];
            Vector3 TR = corners[2];

            float frustumWidth = (TR - TL).magnitude;
            float frustumHeight = (TL - BL).magnitude;
            float halfW = frustumWidth * 0.5f;
            float halfH = frustumHeight * 0.5f;

            float marginU = Mathf.Clamp01(iconHalfRU.x / Mathf.Max(halfW, 1e-4f)) + VRextraViewportPad;
            float marginV = Mathf.Clamp01(iconHalfRU.y / Mathf.Max(halfH, 1e-4f)) + VRextraViewportPad;

            float u = Mathf.Clamp(VRdesiredNormXY.x, -1f + marginU, 1f - marginU);
            float v = Mathf.Clamp(VRdesiredNormXY.y, -1f + marginV, 1f - marginV);

            Vector3 centerAtDepth = cam.transform.InverseTransformPoint(
                Position + cam.transform.forward * BasisHeightDriver.AvatarToPlayerScale
            );

            Vector3 rightLocal = (TR - TL).normalized;
            Vector3 upLocal = (TL - BL).normalized;

            Vector3 localPos = centerAtDepth + rightLocal * (u * halfW) + upLocal * (v * halfH);
            return localPos;
        }

        public Vector2 GetIconHalfSizeRUInCameraSpace(Camera cam, Transform uiRoot)
        {
            Vector3 ext = SpriteRendererIcon.bounds.extents;
            Vector3 right = cam.transform.right;
            Vector3 up = cam.transform.up;

            Vector3 ex = uiRoot.TransformVector(new Vector3(ext.x * 2f, 0, 0)) * 0.5f;
            Vector3 ey = uiRoot.TransformVector(new Vector3(0, ext.y * 2f, 0)) * 0.5f;
            Vector3 ez = uiRoot.TransformVector(new Vector3(0, 0, ext.z * 2f)) * 0.5f;

            float halfRight = ProjectHalfOnAxis(right, ex, ey, ez);
            float halfUp = ProjectHalfOnAxis(up, ex, ey, ez);

            return new Vector2(Mathf.Abs(halfRight), Mathf.Abs(halfUp));
        }

        public static float ProjectHalfOnAxis(Vector3 axis, params Vector3[] halfAxes)
        {
            axis = axis.normalized;
            float sum = 0f;
            for (int i = 0; i < halfAxes.Length; i++)
            {
                sum += Mathf.Abs(Vector3.Dot(axis, halfAxes[i]));
            }
            return sum;
        }

        // ---------------- Activity Hooks ----------------
        public void MicrophoneTransmitting()
        {
            LocalIsTransmitting = true;
            ApplyDisplayModeVisibility(false);

            SpriteRendererIcon.color = UnMutedMutedIconColorActive;
            SpriteRendererIconTransform.localScale = largerScale;
        }

        public void MicrophoneNotTransmitting()
        {
            LocalIsTransmitting = false;
            ApplyDisplayModeVisibility(false);

            SpriteRendererIcon.color = UnMutedMutedIconColorInactive;
            SpriteRendererIconTransform.localScale = StartingScale;
        }

        public void OnPausedEvent(bool IsMuted)
        {
            UpdateMicrophoneVisuals(IsMuted, true);
        }

        // ---------------- Visuals & Display Mode ----------------
        public void UpdateMicrophoneVisuals(bool IsMuted, bool PlaySound)
        {
            IsCurrentlyMuted = IsMuted;

            // Start a new bounce (replaces coroutine)
            StartScaleBounce();

            ApplyDisplayModeVisibility(false);

            if (IsMuted)
            {
                SpriteRendererIcon.sprite = SpriteMicrophoneOff;
                SpriteRendererIcon.color = MutedColor;

                if (PlaySound && AudioSource != null && MuteSound != null)
                {
                    AudioSource.PlayOneShot(MuteSound);
                }
            }
            else
            {
                SpriteRendererIcon.sprite = SpriteMicrophoneOn;
                SpriteRendererIcon.color = LocalIsTransmitting ? UnMutedMutedIconColorActive : UnMutedMutedIconColorInactive;

                if (PlaySound && AudioSource != null && UnMuteSound != null)
                {
                    AudioSource.PlayOneShot(UnMuteSound);
                }
            }
        }

        public void OnDisplayModeChanged(MicrophoneDisplayMode newMode)
        {
            DisplayMode = newMode;

            // If we're going to hide the icon, kill the bounce cleanly.
            if (DisplayMode == MicrophoneDisplayMode.Off)
            {
                StopScaleBounce(resetToStartScale: true);
            }

            ApplyDisplayModeVisibility(false);
        }

        private void ApplyDisplayModeVisibility(bool ForcedUpdate)
        {
            bool shouldShow;

            switch (DisplayMode)
            {
                case MicrophoneDisplayMode.Off:
                    shouldShow = false;
                    break;

                case MicrophoneDisplayMode.AlwaysVisible:
                    shouldShow = true;
                    break;

                case MicrophoneDisplayMode.ActivityDetection:
                    // Comment said: show when muted OR transmitting.
                    shouldShow = IsCurrentlyMuted || LocalIsTransmitting;
                    break;

                default:
                    shouldShow = true;
                    break;
            }

            SetIconVisible(shouldShow);
        }

        private void SetIconVisible(bool visible)
        {
            if (SpriteRendererIcon != null)
            {
                SpriteRendererIcon.enabled = visible;
            }
        }

        // ---------------- Bounce (LateUpdate-style) ----------------
        private void StartScaleBounce()
        {
            if (SpriteRendererIconTransform == null)
            {
                return;
            }

            scaleTime = 0f;
            scalingUp = true;
            isScaling = true;
        }

        private void StopScaleBounce(bool resetToStartScale)
        {
            isScaling = false;
            scalingUp = true;
            scaleTime = 0f;

            if (resetToStartScale && SpriteRendererIconTransform != null)
            {
                SpriteRendererIconTransform.localScale = StartingScale;
            }
        }

        /// <summary>
        /// Call this once per LateUpdate from your driver, passing Time.deltaTime.
        /// </summary>
        public void Simulate(float DeltaTime)
        {
            if (!isScaling)
            {
                return;
            }

            scaleTime += DeltaTime;
            float t = (halfDuration <= 1e-6f) ? 1f : (scaleTime / halfDuration);

            if (scalingUp)
            {
                SpriteRendererIconTransform.localScale = Vector3.Lerp(StartingScale, largerScale, t);

                if (t >= 1f)
                {
                    SpriteRendererIconTransform.localScale = largerScale;
                    scalingUp = false;
                    scaleTime = 0f;
                }
            }
            else
            {
                SpriteRendererIconTransform.localScale = Vector3.Lerp(largerScale, StartingScale, t);

                if (t >= 1f)
                {
                    SpriteRendererIconTransform.localScale = StartingScale;
                    isScaling = false;
                }
            }
        }
    }
}
