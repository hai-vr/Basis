using Basis.Scripts.Drivers;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Basis.Scripts.BasisSdk.Highlight
{
    /// <summary>
    /// URP renderer feature that draws an outline + glow around any renderer
    /// registered with <see cref="BasisHighlightManager"/>.
    ///
    /// Pipeline: mask → separable dilation (defines the ring) → optional
    /// separable gaussian (softens the dilated field into a glow halo) →
    /// composite over camera color.
    ///
    /// All tunables live on a shared <see cref="BasisHighlightSettings"/>
    /// ScriptableObject so Desktop and Quest renderers can point at the same
    /// configuration without duplicate values.
    /// </summary>
    [DisallowMultipleRendererFeature("BasisHighlight")]
    public class BasisHighlightFeature : ScriptableRendererFeature
    {
        [SerializeField] private BasisHighlightSettings settings;

        private BasisHighlightPass _pass;

        public override void Create()
        {
            if (settings == null)
            {
                return;
            }

            if (_pass != null)
            {
                BasisHighlightPass.Live.Remove(_pass);
            }

            _pass = new BasisHighlightPass(settings)
            {
                renderPassEvent = settings.injectionPoint,
            };

            BasisHighlightPass.Live.Add(_pass);
            BasisHighlightConfigOverride.ApplyCurrentTo(_pass);
        }

        protected override void Dispose(bool disposing)
        {
            if (_pass != null)
            {
                BasisHighlightPass.Live.Remove(_pass);
                _pass = null;
            }
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (_pass == null || settings == null)
            {
                return;
            }

            if (!ReferenceEquals(renderingData.cameraData.camera, BasisLocalCameraDriver.CameraInstance))
            {
                return;
            }

            // Early-out when nothing is highlighted — render graph schedules nothing.
            if (BasisHighlightManager.ActiveCount == 0)
            {
                return;
            }

            if (settings.defaultMaskMaterial == null)
            {
                return;
            }

            if (settings.compositeMaterial == null)
            {
                return;
            }

            if (settings.blurMaterial == null)
            {
                return;
            }

            _pass.renderPassEvent = settings.injectionPoint;
            renderer.EnqueuePass(_pass);
        }
    }
}
