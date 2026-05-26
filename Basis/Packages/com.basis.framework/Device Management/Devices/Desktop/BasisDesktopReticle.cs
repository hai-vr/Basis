using Basis.Scripts.Drivers;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Basis.Scripts.Device_Management.Devices.Desktop
{
    /// <summary>
    /// Screen-center aim reticle owned by <see cref="BasisDesktopEye"/>. Quad
    /// prefab parented to the local camera, drawn with the always-on-top
    /// <c>Custom/DesktopReticleOverlay</c> shader. Three concentric bands
    /// (dot, dark gap, outer ring) keep it visible against most backgrounds.
    ///
    /// Visibility is gated by two independent inputs. User setting
    /// (<see cref="SetEnabled"/>) and cursor focus (<see cref="SetFocused"/>,
    /// off while the cursor is unlocked). Quad is shown only when
    /// both are true.
    /// </summary>
    [System.Serializable]
    public class BasisDesktopReticle
    {
        public const string PrefabAddress = "DesktopReticle";

        public float DistanceFromCamera = 1f;
        public float SizeMeters = 0.05f;

        private GameObject _quadGO;
        private bool _userEnabled;
        private bool _focused = true;
        private bool _suppressed;

        public void Initialize()
        {
            if (_quadGO != null) return; // idempotent

            Transform camTransform = BasisLocalCameraDriver.Instance.transform;
            _quadGO = Addressables.InstantiateAsync(PrefabAddress, camTransform, false).WaitForCompletion();
            _quadGO.transform.SetLocalPositionAndRotation(Vector3.forward * DistanceFromCamera, Quaternion.identity);
            _quadGO.transform.localScale = Vector3.one * SizeMeters;
        }

        public void Destroy()
        {
            if (_quadGO != null)
            {
                Addressables.ReleaseInstance(_quadGO);
                _quadGO = null;
            }
        }

        public void SetEnabled(bool enabled)
        {
            _userEnabled = enabled;
            ApplyVisibility();
        }

        public void SetFocused(bool focused)
        {
            _focused = focused;
            ApplyVisibility();
        }

        /// <summary>
        /// Hard-suppresses the reticle while a feature needs the screen center clear
        /// (e.g. the handheld camera is out). Unlike <see cref="SetEnabled"/>/<see cref="SetFocused"/>
        /// this destroys the quad rather than deactivating it, and re-creates it on demand when
        /// suppression is lifted — provided the user setting and cursor focus still want it shown.
        /// </summary>
        public void SetSuppressed(bool suppressed)
        {
            if (_suppressed == suppressed) return;
            _suppressed = suppressed;
            if (suppressed)
            {
                Destroy();
            }
            else
            {
                ApplyVisibility();
            }
        }

        private void ApplyVisibility()
        {
            bool visible = _userEnabled && _focused && !_suppressed;
            // Lazy init
            if (visible && _quadGO == null)
            {
                Initialize();
            }
            if (_quadGO != null)
            {
                _quadGO.SetActive(visible);
            }
        }
    }
}
