using HVR.Basis.Comms;
using HVR.Vixxy.Editor;
using UnityEditor;
using UnityEngine;

namespace Systems.Editor
{
    [CustomEditor(typeof(HVRMeshVisibility))]
    public class HVRMeshVisibilityEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var my = (HVRMeshVisibility)target;
            var prioritiesSp = serializedObject.FindProperty(nameof(HVRMeshVisibility.priorities));
            for (var index = 0; index < my.priorities.Length; index++)
            {
                var prioritySp = prioritiesSp.GetArrayElementAtIndex(index);
                
                EditorGUILayout.BeginVertical(HVR_EditorHelpers.GroupBoxStyle);
                EditorGUILayout.BeginHorizontal();
                {
                    EditorGUILayout.LabelField("When", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(prioritySp.FindPropertyRelative(nameof(HVRMeshVisibilityPriority.condition)), GUIContent.none);

                    EditorGUI.BeginDisabledGroup(index == 0);
                    if (GUILayout.Button(HVR_EditorHelpers.ArrowUpSymbol, GUILayout.Width(HVR_EditorHelpers.SwapElementWidth)))
                    {
                        prioritiesSp.MoveArrayElement(index, index - 1);
                    }
                    EditorGUI.EndDisabledGroup();
                    EditorGUI.BeginDisabledGroup(index == prioritiesSp.arraySize - 1);
                    if (GUILayout.Button(HVR_EditorHelpers.ArrowDownSymbol, GUILayout.Width(HVR_EditorHelpers.SwapElementWidth)))
                    {
                        prioritiesSp.MoveArrayElement(index, index + 1);
                    }
                    EditorGUI.EndDisabledGroup();

                    EditorGUI.BeginDisabledGroup(my.priorities.Length == 1);
                    if (GUILayout.Button(HVR_EditorHelpers.CrossSymbol, GUILayout.Width(HVR_EditorHelpers.DeleteButtonWidth)))
                    {
                        prioritiesSp.DeleteArrayElementAtIndex(index);
                        serializedObject.ApplyModifiedProperties();
                        return;
                    }
                    EditorGUI.EndDisabledGroup();
                }
                EditorGUILayout.EndHorizontal();
                
                
                EditorGUILayout.PropertyField(prioritySp.FindPropertyRelative(nameof(HVRMeshVisibilityPriority.subjects)), new GUIContent("GameObjects or Renderers"));
                EditorGUILayout.BeginHorizontal();
                var inEffectSp = prioritySp.FindPropertyRelative(nameof(HVRMeshVisibilityPriority.effect));
                EditorGUILayout.PropertyField(inEffectSp, new GUIContent());
                if (inEffectSp.intValue == (int)HVRVixxyMeshVisibilityEffect.OverrideValue)
                {
                    EditorGUILayout.PropertyField(prioritySp.FindPropertyRelative(nameof(HVRMeshVisibilityPriority.output)), GUIContent.none);
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }
            
            if (GUILayout.Button($"{HVR_EditorHelpers.PlusSymbol} {HVRVixxyLocalizationPhrase.AddChoiceLabel}"))
            {
                prioritiesSp.arraySize += 1;
                var prioritySp = prioritiesSp.GetArrayElementAtIndex(prioritiesSp.arraySize - 1);
                prioritySp.FindPropertyRelative(nameof(HVRMeshVisibilityPriority.condition)).intValue = (int)HVRVixxyMeshVisibilityCondition.IsAnyVisible;
                prioritySp.FindPropertyRelative(nameof(HVRMeshVisibilityPriority.subjects)).arraySize = 0;
                if (prioritiesSp.arraySize > 1)
                {
                    prioritySp.FindPropertyRelative(nameof(HVRMeshVisibilityPriority.output)).floatValue = prioritiesSp.GetArrayElementAtIndex(prioritiesSp.arraySize - 1).FindPropertyRelative(nameof(HVRMeshVisibilityPriority.output)).floatValue + 1f;
                }
                else
                {
                    prioritySp.FindPropertyRelative(nameof(HVRMeshVisibilityPriority.output)).floatValue = 1;
                }
                serializedObject.ApplyModifiedProperties();
                return;
            }

            EditorGUILayout.BeginVertical(HVR_EditorHelpers.GroupBoxStyle);
            EditorGUILayout.LabelField("Otherwise", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            var effectSp = serializedObject.FindProperty(nameof(HVRMeshVisibility.fallbackEffect));
            EditorGUILayout.PropertyField(effectSp, new GUIContent());
            if (effectSp.intValue == (int)HVRVixxyMeshVisibilityEffect.OverrideValue)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRMeshVisibility.fallbackOutput)), GUIContent.none);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            
            serializedObject.ApplyModifiedProperties();
        }
    }
}