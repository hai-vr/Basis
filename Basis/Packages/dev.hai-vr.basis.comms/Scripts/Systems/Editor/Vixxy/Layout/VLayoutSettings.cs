using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace HVR.Vixxy.Editor
{
    internal class VLayoutSettings
    {
        private readonly HVRVixxyControl my;
        private readonly SerializedObject serializedObject;

        private readonly HVRVixxyControlEditor _editor;

        internal VLayoutSettings(HVRVixxyControlEditor editor)
        {
            _editor = editor;
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

            {
                EditorGUILayout.LabelField($"{HVRVixxyLocalizationPhrase.ChoicesLabel} ({my.NumberOfChoices})", EditorStyles.boldLabel);
                var choicesSp = serializedObject.FindProperty(nameof(HVRVixxyControl.choices));

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("", GUILayout.Width(30));
                EditorGUILayout.LabelField("Description / Icon / Value");
                EditorGUILayout.LabelField("", GUILayout.Width(20));
                EditorGUILayout.EndHorizontal();

                for (var choiceIndex = 0; choiceIndex < choicesSp.arraySize; choiceIndex++)
                {
                    var choiceSp = choicesSp.GetArrayElementAtIndex(choiceIndex);

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField($"#{choiceIndex + 1}", GUILayout.Width(30));

                    EditorGUILayout.PropertyField(choiceSp.FindPropertyRelative(nameof(HVRVixxyChoiceControl.title)), GUIContent.none);
                    EditorGUILayout.PropertyField(choiceSp.FindPropertyRelative(nameof(HVRVixxyChoiceControl.icon)), GUIContent.none);
                    EditorGUILayout.PropertyField(choiceSp.FindPropertyRelative(nameof(HVRVixxyChoiceControl.value)), GUIContent.none, GUILayout.Width(50));

                    EditorGUI.BeginDisabledGroup(!my.HasThreeOrMoreChoices);
                    if (GUILayout.Button(HVRUiHelpers.CrossSymbol, GUILayout.Width(20)))
                    {
                        _editor.RemoveChoice(choiceIndex);
                        return true;
                    }
                    EditorGUI.EndDisabledGroup();

                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("", GUILayout.Width(30));
                if (GUILayout.Button($"{HVRUiHelpers.PlusSymbol} {HVRVixxyLocalizationPhrase.AddChoiceLabel}"))
                {
                    _editor.AddChoice();
                    return true;
                }
                EditorGUILayout.LabelField("", GUILayout.Width(20));
                EditorGUILayout.EndHorizontal();

            }
            EditorGUILayout.Separator();

            return false;
        }
    }
}
