using UnityEngine;

namespace HVR.Vixxy
{
    [CreateAssetMenu(fileName = "HVRSettableFloatElement", menuName = "HVR.Basis/Project12/Settable Float Element")]
    public class HVRSettableFloatElement : ScriptableObject
    {
        public string locKey;
        public string localizedTitle;
        public float min = 0f;
        public float max = 1f;
        public HVRUnitDisplayKind displayAs = HVRUnitDisplayKind.ArbitraryFloat;

        public float defaultValue;

        private float _storedValue;
        public float storedValue
        {
            get => _storedValue;
            set
            {
                if (_storedValue != value)
                {
                    _storedValue = value;
                    OnValueChanged?.Invoke(_storedValue);
                }
            }
        }

        public event ValueChanged OnValueChanged;
        public delegate void ValueChanged(float newValue);

        private void OnEnable()
        {
            storedValue = defaultValue;
        }

        public enum HVRUnitDisplayKind
        {
            ArbitraryFloat,
            Percentage01,
            Percentage080,
            Percentage0100,
            AngleDegrees,
            InGameRangeUnityUnits,
            RealWorldPhysicalSpaceMetricUnits,
            RealWorldPhysicalSpaceImperialUnits,
            Toggle
        }
    }
}
