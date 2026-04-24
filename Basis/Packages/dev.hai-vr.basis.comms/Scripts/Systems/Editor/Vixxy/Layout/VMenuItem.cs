using HVR.Basis.Comms.Editor;
using UnityEditor;
using UnityEngine;

namespace HVR.Vixxy.Editor
{
    internal class VMenuItem
    {
        private readonly HVRVixxyMenuItem my;
        private readonly SerializedObject serializedObject;

        internal VMenuItem(HVRVixxyMenuItemEditor editor)
        {
            my = (HVRVixxyMenuItem)editor.target;
            serializedObject = editor.serializedObject;
        }

        public bool LayoutUserView()
        {
            EditorGUILayout.Separator();
            LayoutMenu();
            EditorGUILayout.Separator();

            return false;
        }

        public bool LayoutCreatorView()
        {
            var controlsOnThis = my.GetComponents<HVRVixxyControl>();

            EditorGUILayout.Separator();

            if (controlsOnThis.Length == 1)
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.ObjectField(new GUIContent(HVRVixxyLocalizationPhrase.ControlLabel), controlsOnThis[0], typeof(HVRVixxyControl), true);
                EditorGUI.EndDisabledGroup();
            }
            else
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRVixxyMenuItem.control)));
                HVRVixxyControlEditor.LayoutAddressSelector(serializedObject.FindProperty(nameof(HVRVixxyMenuItem.address)));
            }

            EditorGUILayout.Separator();

            return false;
        }

        private void LayoutMenu()
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRVixxyMenuItem.titleSelection)));
            if (my.titleSelection == HVRVixxyTitleSelection.UseObjectName)
            {
                var currentName = my.gameObject.name;
                var newName = EditorGUILayout.TextField(HVRVixxyLocalizationPhrase.ObjectNameLabel, currentName);
                if (currentName != newName)
                {
                    var go = new SerializedObject(my.gameObject);
                    go.FindProperty("m_Name").stringValue = newName;
                    go.ApplyModifiedProperties();
                }
            }
            else if (my.titleSelection != HVRVixxyTitleSelection.UseChoicesOnly)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRVixxyMenuItem.title)));
            }

            var hasControl = my.TryResolveActualControl(out var control);
            if (!hasControl)
            {
                EditorGUILayout.HelpBox("Cannot display choices because no control is assigned to this menu item.", MessageType.Error);
                return;
            }

            var hasMoreThanThreeChoices = hasControl && control.NumberOfChoices > 2;
            if (!hasMoreThanThreeChoices)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRVixxyMenuItem.presentation)));
            }

            if (hasControl)
            {
                var defaultValueSp = serializedObject.FindProperty(nameof(HVRVixxyMenuItem.defaultValue));
                if (hasMoreThanThreeChoices)
                {
                    var currentValue = (int)defaultValueSp.floatValue;
                    var newValue = EditorGUILayout.IntSlider(new GUIContent(ObjectNames.NicifyVariableName(nameof(HVRVixxyMenuItem.defaultValue))), currentValue, (int)control.Min(), (int)control.Max());
                    if (currentValue != newValue)
                    {
                        defaultValueSp.floatValue = newValue;
                    }
                }
                else
                {
                    EditorGUILayout.Slider(defaultValueSp, 0f, 1f);
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("", GUILayout.Width(50));
                    if (HaiEFCommon.ColoredBackground(Mathf.Approximately(defaultValueSp.floatValue, 0f), Color.cyan, () => GUILayout.Button(HVRVixxyLocalizationPhrase.InactiveLabel)))
                    {
                        defaultValueSp.floatValue = 0f;
                    }
                    if (HaiEFCommon.ColoredBackground(Mathf.Approximately(defaultValueSp.floatValue, 1f), Color.cyan, () => GUILayout.Button(HVRVixxyLocalizationPhrase.ActiveLabel)))
                    {
                        defaultValueSp.floatValue = 1f;
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUILayout.Separator();
        }
    }
}
