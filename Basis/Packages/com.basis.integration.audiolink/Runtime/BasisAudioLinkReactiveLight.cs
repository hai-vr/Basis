using UnityEngine;

namespace Basis.Integration.AudioLink
{
    /// <summary>
    /// Drives a Light's intensity and color from a CPU-readback AudioLink band.
    /// </summary>
    [RequireComponent(typeof(Light))]
    [AddComponentMenu("Basis/AudioLink/Reactive Light")]
    public class BasisAudioLinkReactiveLight : BasisAudioLinkReactiveBase
    {
        [Header("Intensity")]
        public bool DriveIntensity = true;
        public float MinIntensity = 0f;
        public float MaxIntensity = 2f;
        [Tooltip("Scales band amplitude before it is mapped into the intensity range.")]
        public float IntensityMultiplier = 1f;

        [Header("Color")]
        public bool DriveColor = true;

        private Light _light;
        private Color _baseColor = Color.white;

        private void Awake()
        {
            _light = GetComponent<Light>();
            if (_light != null)
            {
                _baseColor = _light.color;
            }
        }

        private void Update()
        {
            if (_light == null || !TryResolveAudioLink())
            {
                return;
            }

            float amplitude = ReadAmplitude();

            if (DriveIntensity)
            {
                _light.intensity = MinIntensity + (MaxIntensity - MinIntensity) * Mathf.Max(0f, amplitude * IntensityMultiplier);
            }
            if (DriveColor)
            {
                _light.color = EvaluateColor(amplitude, _baseColor);
            }
        }
    }
}
