using UnityEngine;

namespace Basis.Integration.AudioLink
{
    /// <summary>
    /// Drives a renderer's emission (and optionally base) color from a CPU-readback AudioLink band via a MaterialPropertyBlock, so shared materials are not instanced.
    /// </summary>
    [AddComponentMenu("Basis/AudioLink/Reactive Material")]
    public class BasisAudioLinkReactiveMaterial : BasisAudioLinkReactiveBase
    {
        [Header("Target")]
        [Tooltip("Renderer to drive. Defaults to the Renderer on this GameObject.")]
        public Renderer TargetRenderer;
        [Tooltip("Material slot to drive, or -1 for every slot on the renderer.")]
        public int MaterialIndex = -1;

        [Header("Emission")]
        [Tooltip("The material must already have Emission enabled; the keyword cannot be toggled per-instance.")]
        public string EmissionColorProperty = "_EmissionColor";
        public float MinEmission = 0f;
        public float MaxEmission = 4f;
        [Tooltip("Scales band amplitude before it is mapped into the emission range.")]
        public float EmissionMultiplier = 1f;

        [Header("Base Color")]
        public bool DriveBaseColor = false;
        public string BaseColorProperty = "_BaseColor";

        private MaterialPropertyBlock _block;
        private int _emissionId;
        private int _baseColorId;
        private Color _baseEmission = Color.white;

        private void Awake()
        {
            if (TargetRenderer == null)
            {
                TargetRenderer = GetComponent<Renderer>();
            }
            _block = new MaterialPropertyBlock();
            _emissionId = Shader.PropertyToID(EmissionColorProperty);
            _baseColorId = Shader.PropertyToID(BaseColorProperty);
            CacheBaseEmission();
        }

        private void CacheBaseEmission()
        {
            if (TargetRenderer == null)
            {
                return;
            }
            Material[] shared = TargetRenderer.sharedMaterials;
            Material source = MaterialIndex >= 0 && MaterialIndex < shared.Length ? shared[MaterialIndex] : TargetRenderer.sharedMaterial;
            if (source != null && source.HasProperty(_emissionId))
            {
                _baseEmission = source.GetColor(_emissionId);
            }
        }

        private void Update()
        {
            if (TargetRenderer == null || !TryResolveAudioLink())
            {
                return;
            }

            float amplitude = ReadAmplitude();
            Color color = EvaluateColor(amplitude, _baseEmission);
            float emission = MinEmission + (MaxEmission - MinEmission) * Mathf.Max(0f, amplitude * EmissionMultiplier);
            Color emissionColor = color * emission;

            if (MaterialIndex >= 0)
            {
                TargetRenderer.GetPropertyBlock(_block, MaterialIndex);
                _block.SetColor(_emissionId, emissionColor);
                if (DriveBaseColor)
                {
                    _block.SetColor(_baseColorId, color);
                }
                TargetRenderer.SetPropertyBlock(_block, MaterialIndex);
            }
            else
            {
                TargetRenderer.GetPropertyBlock(_block);
                _block.SetColor(_emissionId, emissionColor);
                if (DriveBaseColor)
                {
                    _block.SetColor(_baseColorId, color);
                }
                TargetRenderer.SetPropertyBlock(_block);
            }
        }
    }
}
