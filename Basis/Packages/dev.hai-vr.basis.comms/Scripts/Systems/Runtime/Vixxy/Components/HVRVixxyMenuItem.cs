using System;
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

        [SerializeField] internal int numberOfChoices = 2;
        [SerializeField] internal float defaultValue = 0f;

        [SerializeField] internal string address = "";
        [SerializeField] internal HVRVixxyControl[] controls = Array.Empty<HVRVixxyControl>();

        [SerializeField] internal HVRVixxyOrchestrator orchestrator;

        private HVRSettableFloatElement GadgetElement;

        public void OnHVRAvatarReady(bool isWearer)
        {
            if (!isWearer) return;

            orchestrator = VixxySetup.EnsureInitialized(this);

            {
                GadgetElement = ScriptableObject.CreateInstance<HVRSettableFloatElement>();
                GadgetElement.localizedTitle = gameObject.name;
                GadgetElement.min = 0f;
                GadgetElement.max = numberOfChoices - 1f;
                GadgetElement.displayAs = HVRSettableFloatElement.HVRUnitDisplayKind.Toggle; // TODO: This depends on the type of control.
                GadgetElement.defaultValue = defaultValue;
                GadgetElement.storedValue = defaultValue;
            }
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

            var index = (int)GadgetElement.storedValue;
            if (index < 0 || index >= choices.Length) return title;

            return choices[index].title ?? title;
        }

        public void ButtonPressed()
        {
            var newValue = (GadgetElement.storedValue + 1) % numberOfChoices;
            GadgetElement.storedValue = newValue;
            AcquisitionService.SceneInstance.Submit(HVRAddress.AddressToId(address), newValue);
        }

        public void ApplyValue(float value)
        {
            GadgetElement.storedValue = value;
            AcquisitionService.SceneInstance.Submit(HVRAddress.AddressToId(address), value);
        }

        public float GetValue()
        {
            return GadgetElement.storedValue;
        }
    }
}
