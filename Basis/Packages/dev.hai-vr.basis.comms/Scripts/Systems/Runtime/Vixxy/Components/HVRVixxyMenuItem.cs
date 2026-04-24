using HVR.Basis.Comms;
using UnityEngine;

namespace HVR.Vixxy
{
    [HelpURL("https://docs.hai-vr.dev/docs/basis/avatar-customization/vixxy")]
    [AddComponentMenu("HVR.Basis/Vixxy Menu Item")]
    public class HVRVixxyMenuItem : MonoBehaviour, IHVRInitializable
    {
        [SerializeField] [Multiline] internal string title;
        [SerializeField] internal HVRVixxyTitleSelection titleSelection = HVRVixxyTitleSelection.UseObjectName;
        [SerializeField] internal HVRVixxyControlPresentation presentation;

        [SerializeField] internal float defaultValue;

        [SerializeField] internal HVRAddressSelector address;
        [SerializeField] internal HVRVixxyControl control;

        // [SerializeField] internal HVRVixxyRememberScope remember = HVRVixxyRememberScope.RememberAcrossAvatars;
        // [SerializeField] internal string rememberTag = "";

        private float _value;
        private bool _hasAddress;
        private int _iddressPath;

        public bool TryResolveActualControl(out HVRVixxyControl result)
        {
            var controlsOnThis = GetComponents<HVRVixxyControl>(); // This may return 0 elements.
            if (controlsOnThis.Length == 1)
            {
                result = controlsOnThis[0];
                return true;
            }

            if (control != null)
            {
                result = control;
                return true;
            }

            result = null;
            return false;
        }

        public void OnHVRAvatarReady(bool isWearer)
        {
            if (!isWearer) return;

            control = TryResolveActualControl(out var actualControl) ? actualControl : null;

            var intermediateAddress = control != null ? control.address : address;
            _hasAddress = intermediateAddress.TryResolvePath(out var addressPath);
            if (_hasAddress)
            {
                _iddressPath = HVRAddressRegistry.AddressToId(addressPath);
            }

            // TODO: We would have to load the actual default value from memory somehow.
            _value = defaultValue;
            SubmitValue();
            BasisDebug.Log($"Initialized {GetType().Name} {address} with default value {_value}");
        }

        public void OnHVRReadyBothAvatarAndNetwork(bool isWearer)
        {
            if (!isWearer) return;
        }

        public string ResolveTitle()
        {
            return titleSelection switch
            {
                HVRVixxyTitleSelection.UseObjectName => gameObject.name,
                HVRVixxyTitleSelection.UseCustomTitle => title,
                HVRVixxyTitleSelection.UseCustomTitleAndChoices => title,
                HVRVixxyTitleSelection.UseChoicesOnly => ResolveComplexTitle(),
                _ => title
            };
        }

        public string ResolveDescription()
        {
            return titleSelection switch
            {
                HVRVixxyTitleSelection.UseObjectName => "",
                HVRVixxyTitleSelection.UseCustomTitle => "",
                HVRVixxyTitleSelection.UseCustomTitleAndChoices => ResolveComplexTitle(),
                HVRVixxyTitleSelection.UseChoicesOnly => "",
                _ => title
            };
        }

        private string ResolveComplexTitle()
        {
            if (!TryResolveActualControl(out var actualControl)) return title;

            var choices = actualControl.choices;
            if (choices == null) return title;

            var index = (int)_value;
            if (index < 0 || index >= choices.Length) return title;

            return choices[index].title ?? title;
        }

        public void ApplyValue(float value)
        {
            _value = value;
            SubmitValue();
        }

        public float GetValue()
        {
            return _value;
        }

        private void SubmitValue()
        {
            if (_hasAddress)
            {
                AcquisitionService.SceneInstance.SubmitOrDefineDefaultValue(_iddressPath, _value);
            }
        }
    }
}
