using HVR.Basis.Comms;
using UnityEngine;

namespace HVR.Vixxy
{
    [HelpURL("https://docs.hai-vr.dev/docs/basis/avatar-customization/vixxy")]
    public class HVRVixxyMenuItem : MonoBehaviour, IHVRInitializable
    {
        [SerializeField] [Multiline] internal string title;
        [SerializeField] internal HVRVixxyTitleSelection titleSelection = HVRVixxyTitleSelection.UseObjectName;
        [SerializeField] internal HVRVixxyChoice[] choices = new HVRVixxyChoice[2];
        [SerializeField] internal HVRVixxyControlPresentation presentation;

        [SerializeField] internal int numberOfChoices = 2;
        [SerializeField] internal float defaultValue;

        [SerializeField] internal string address = "";
        [SerializeField] internal HVRVixxyControl control;

        private float _value;

        public void OnHVRAvatarReady(bool isWearer)
        {
            if (!isWearer) return;

            if (control == null && string.IsNullOrWhiteSpace(address))
            {
                control = GetComponent<HVRVixxyControl>(); // This may return null.
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
            AcquisitionService.SceneInstance.SubmitOrDefineDefaultValue(HVRAddress.AddressToId(address), _value);
        }
    }
}
