using HVR.Basis.Comms;
using UnityEditor;
using UnityEngine;

namespace HVR.Vixxy.Editor
{
    [CustomEditor(typeof(AcquisitionService))]
    public class AcquisitionServiceEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            if (Application.isPlaying)
            {
                var my = (AcquisitionService)target;

                EditorGUILayout.LabelField("Registered addresses / listeners", EditorStyles.boldLabel);
                foreach (var pair in my._addressUpdated)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.TextField(HVRAddress.ResolveKnownAddressFromId(pair.Key));
                    EditorGUILayout.LabelField($"{pair.Value.GetListenersCount()} listeners", GUILayout.Width(80));
                    var currentValue = pair.Value.lastValueForDebugPurposesOnly;
                    var newValue = EditorGUILayout.FloatField(currentValue, GUILayout.Width(50));
                    if (!Mathf.Approximately(currentValue, newValue))
                    {
                        my.Submit(pair.Key, newValue);
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }
        }
    }
}
