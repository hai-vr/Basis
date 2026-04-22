using HVR.Basis.Comms.Editor;
using UnityEditor;
using UnityEngine;

namespace HVR.Vixxy.Editor
{
    internal class HVRVixxyLayoutUserView
    {
        private const string ObjectNameLabel = "Title (Object Name)";
        private const string ActiveLabel = "Active";
        private const string InactiveLabel = "Inactive";
        private const string MsgControlTriggeredByVariable = "No user options. This control is triggered by a variable.";

        private readonly HVRVixxyControl my;
        private readonly SerializedObject serializedObject;

        internal HVRVixxyLayoutUserView(HVRVixxyControlEditor editor)
        {
            my = (HVRVixxyControl)editor.target;
            serializedObject = editor.serializedObject;
        }

        public bool Layout()
        {
            EditorGUILayout.Separator();
            if (my.controlType == HVRVixxyControlType.Menu)
            {
                LayoutMenu();
            }
            else
            {
                EditorGUILayout.HelpBox(MsgControlTriggeredByVariable, MessageType.Info);
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRVixxyControl.address)));
                EditorGUI.EndDisabledGroup();
            }
            EditorGUILayout.Separator();

            return false;
        }

        private void LayoutMenu()
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRVixxyControl.titleSelection)));
            if (my.titleSelection == HVRVixxyTitleSelection.UseObjectName)
            {
                var currentName = my.gameObject.name;
                var newName = EditorGUILayout.TextField(ObjectNameLabel, currentName);
                if (currentName != newName)
                {
                    var go = new SerializedObject(my.gameObject);
                    go.FindProperty("m_Name").stringValue = newName;
                    go.ApplyModifiedProperties();
                }
            }
            else if (my.titleSelection != HVRVixxyTitleSelection.UseChoicesOnly)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRVixxyControl.title)));
            }

            var defaultValueSp = serializedObject.FindProperty(nameof(HVRVixxyControl.defaultValue));
            var choicesSp = serializedObject.FindProperty(nameof(HVRVixxyControl.choices));
            if (my.hasThreeOrMoreChoices)
            {
                var currentValue = (int)defaultValueSp.floatValue;
                var newValue = EditorGUILayout.IntSlider(new GUIContent(ObjectNames.NicifyVariableName(nameof(HVRVixxyControl.defaultValue))), currentValue, 0, my.numberOfChoices - 1);
                if (currentValue != newValue)
                {
                    defaultValueSp.floatValue = newValue;
                }
                EditorGUILayout.PropertyField(choicesSp);
            }
            else
            {
                EditorGUILayout.Slider(defaultValueSp, 0f, 1f);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("", GUILayout.Width(50));
                if (HaiEFCommon.ColoredBackground(Mathf.Approximately(defaultValueSp.floatValue, 0f), Color.cyan, () => GUILayout.Button(InactiveLabel)))
                {
                    defaultValueSp.floatValue = 0f;
                }
                if (HaiEFCommon.ColoredBackground(Mathf.Approximately(defaultValueSp.floatValue, 1f), Color.cyan, () => GUILayout.Button(ActiveLabel)))
                {
                    defaultValueSp.floatValue = 1f;
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.LabelField(InactiveLabel, EditorStyles.boldLabel);
                DisplayChoiceInBistableToggle(choicesSp.GetArrayElementAtIndex(0));
                EditorGUILayout.LabelField(ActiveLabel, EditorStyles.boldLabel);
                DisplayChoiceInBistableToggle(choicesSp.GetArrayElementAtIndex(1));
            }
        }

        private void DisplayChoiceInBistableToggle(SerializedProperty choiceSp)
        {
            if (my.titleSelection == HVRVixxyTitleSelection.UseCustomTitleAndChoices || my.titleSelection == HVRVixxyTitleSelection.UseChoicesOnly || my.hasThreeOrMoreChoices)
            {
                EditorGUILayout.PropertyField(choiceSp.FindPropertyRelative(nameof(HVRVixxyChoice.title)));
            }
            EditorGUILayout.PropertyField(choiceSp.FindPropertyRelative(nameof(HVRVixxyChoice.icon)));
        }
    }
}
