using UnityEditor;
using UnityEngine;

namespace HVR.Vixxy.Editor
{
    [CustomEditor(typeof(HVRGadgetRepository))]
    public class HVRGadgetRepositoryEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var my = (HVRGadgetRepository)target;
            foreach (var gadget in my.GadgetView())
            {
                if (gadget is HVRSettableFloatElement floatElement)
                {
                    var slider = EditorGUILayout.Slider(new GUIContent(floatElement.localizedTitle), floatElement.storedValue, floatElement.min, floatElement.max);
                    if (!Mathf.Approximately(slider, floatElement.storedValue))
                    {
                        floatElement.storedValue = slider;
                    }
                }
            }
        }
    }
}