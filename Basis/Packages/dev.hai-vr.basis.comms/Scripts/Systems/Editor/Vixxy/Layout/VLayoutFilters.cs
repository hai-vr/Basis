using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace HVR.Vixxy.Editor
{
    public class VLayoutFilters
    {
        private readonly HVRVixxyControl my;
        private readonly SerializedObject serializedObject;
        private readonly ReorderableList filtersReorderableList;

        internal VLayoutFilters(HVRVixxyControlEditor editor)
        {
            my = (HVRVixxyControl)editor.target;
            serializedObject = editor.serializedObject;

            // reference: https://blog.terresquall.com/2020/03/creating-reorderable-lists-in-the-unity-inspector/
            filtersReorderableList = new ReorderableList(
                serializedObject,
                serializedObject.FindProperty(nameof(HVRVixxyControl.filters)),
                true, true, true, true
            );
            filtersReorderableList.drawElementCallback = FiltersListElement;
            filtersReorderableList.drawHeaderCallback =
                rect => EditorGUI.LabelField(rect, HVRVixxyLocalizationPhrase.ObjectGroupsLabel);
            filtersReorderableList.displayAdd = false;
            filtersReorderableList.elementHeight = EditorGUIUtility.singleLineHeight * 1;
        }

        public bool Layout()
        {
            EditorGUILayout.Separator();
            filtersReorderableList.DoLayoutList();
            EditorGUILayout.Separator();

            var selectedIndex = filtersReorderableList.index;
            if (selectedIndex != -1 && selectedIndex < filtersReorderableList.count)
            {
                var element = filtersReorderableList.serializedProperty.GetArrayElementAtIndex(selectedIndex);

                EditorGUILayout.BeginVertical(HVR_EditorHelpers.GroupBoxStyle);
                EditorGUILayout.LabelField($"Filter #{selectedIndex + 1} ({element.managedReferenceValue.GetType().Name})", EditorStyles.boldLabel);

                if (element.managedReferenceValue is HVRCurveVixxyFilter curve)
                {
                    EditorGUILayout.PropertyField(element.FindPropertyRelative(nameof(HVRCurveVixxyFilter.curve)));
                }
                else if (element.managedReferenceValue is HVRMoveTowardsVixxyFilter moveTowards)
                {
                    EditorGUILayout.PropertyField(element.FindPropertyRelative(nameof(HVRMoveTowardsVixxyFilter.secondsPerUnit)));
                }
                else if (element.managedReferenceValue is HVRSmoothVixxyFilter lerp)
                {
                    EditorGUILayout.PropertyField(element.FindPropertyRelative(nameof(HVRSmoothVixxyFilter.secondsPerUnit)));
                }
                EditorGUILayout.EndVertical();
            }

            if (GUILayout.Button("+ Add Curve filter"))
            {
                AddFilter(new HVRCurveVixxyFilter());
            }
            if (GUILayout.Button("+ Add MoveTowards filter"))
            {
                AddFilter(new HVRMoveTowardsVixxyFilter());
            }
            if (GUILayout.Button("+ Add Smooth filter"))
            {
                AddFilter(new HVRSmoothVixxyFilter());
            }
            EditorGUILayout.Separator();

            return false;
        }

        private void AddFilter(HVRVixxyFilterBase newInstance)
        {
            var filtersSp = serializedObject.FindProperty(nameof(HVRVixxyControl.filters));
            var indexToPutData = filtersSp.arraySize;
            filtersSp.arraySize = indexToPutData + 1;
            filtersSp.GetArrayElementAtIndex(indexToPutData).managedReferenceValue = newInstance;
        }

        private void FiltersListElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            var element = filtersReorderableList.serializedProperty.GetArrayElementAtIndex(index);
            EditorGUI.LabelField(rect, element.managedReferenceValue.GetType().FullName);
        }
    }
}
