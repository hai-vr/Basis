#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HVR.Basis.Comms
{
    [AddComponentMenu("HVR.Basis/Comms/Acquisition Assist")]
    public class AcquisitionAssist : MonoBehaviour
    {
        public BlendshapeActuationDefinitionFile definitionFile;
        public AcquisitionService acquisitionService;
        internal Dictionary<string, float> memory = new Dictionary<string, float>();

        private void OnAddressUpdated(string address, float value)
        {
            if (!isActiveAndEnabled) return;
            
            acquisitionService.Submit(address, value);
        }
    }

    [CustomEditor(typeof(AcquisitionAssist))]
    public class AcquisitionAssistEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var assist = (AcquisitionAssist)target;
            foreach (var definition in assist.definitionFile.definitions)
            {
                var address = definition.address;
                assist.memory.TryGetValue(address, out var value);
                var newValue = EditorGUILayout.Slider(address, value, -1, 1);
                if (value != newValue)
                {
                    assist.memory[address] = newValue;
                    assist.acquisitionService.Submit(address, newValue);
                } 
            }
        }
    }
}
#endif