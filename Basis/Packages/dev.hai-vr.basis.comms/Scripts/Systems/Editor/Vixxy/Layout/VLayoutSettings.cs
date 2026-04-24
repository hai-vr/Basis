using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace HVR.Vixxy.Editor
{
    internal class VLayoutSettings
    {
        private readonly HVRVixxyControl my;
        private readonly SerializedObject serializedObject;

        internal VLayoutSettings(HVRVixxyControlEditor editor)
        {
            my = (HVRVixxyControl)editor.target;
            serializedObject = editor.serializedObject;
        }

        public bool LayoutSettings()
        {
            EditorGUILayout.Separator();

            var menuItem = my.GetComponent<HVRVixxyMenuItem>();
            if (menuItem == null)
            {
                if (GUILayout.Button(HVRVixxyLocalizationPhrase.CreateMenuForThisControlLabel))
                {
                    var comp = Undo.AddComponent<HVRVixxyMenuItem>(my.gameObject);
                    ComponentUtility.MoveComponentUp(comp);
                }
            }

            EditorGUILayout.LabelField(HVRVixxyLocalizationPhrase.ControlLabel, EditorStyles.boldLabel);
            HVRVixxyControlEditor.LayoutAddressSelector(serializedObject.FindProperty(nameof(HVRVixxyControl.address)));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRVixxyControl.networked)));
            if (my.networked)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRVixxyControl.advancedNetworking)));
                if (my.advancedNetworking == HVRVixxyNetworkingType.UpdatedExtremelyFrequently)
                {
                    EditorGUILayout.HelpBox(HVRVixxyLocalizationPhrase.MsgNetworkingUsesHighFrequency, MessageType.Warning);
                }
            }
            EditorGUILayout.Separator();

            EditorGUILayout.LabelField(HVRVixxyLocalizationPhrase.ChoicesLabel, EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRVixxyControl.hasThreeOrMoreChoices)));
            if (my.hasThreeOrMoreChoices)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRVixxyControl.numberOfChoices)));
            }
            EditorGUILayout.Separator();

            return false;
        }
    }
}
