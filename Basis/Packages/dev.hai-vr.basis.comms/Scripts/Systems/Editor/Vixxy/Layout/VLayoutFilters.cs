using System;
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
                rect => EditorGUI.LabelField(rect, HVRVixxyLocalizationPhrase.FiltersLabel);
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
                EditorGUILayout.LabelField($"{HVRVixxyLocalizationPhrase.FilterLabel} #{selectedIndex + 1} ({DisplayNameOf(element.managedReferenceValue.GetType())})", EditorStyles.boldLabel);

                if (element.managedReferenceValue is HVRCurveVixxyFilter)
                {
                    EditorGUILayout.PropertyField(element.FindPropertyRelative(nameof(HVRCurveVixxyFilter.curve)));
                }
                else if (element.managedReferenceValue is HVRMoveTowardsVixxyFilter)
                {
                    EditorGUILayout.PropertyField(element.FindPropertyRelative(nameof(HVRMoveTowardsVixxyFilter.secondsPerUnit)));
                }
                else if (element.managedReferenceValue is HVRSmoothVixxyFilter)
                {
                    EditorGUILayout.PropertyField(element.FindPropertyRelative(nameof(HVRSmoothVixxyFilter.secondsPerUnit)));
                }
                EditorGUILayout.EndVertical();
            }

            AddFilterButton<HVRCurveVixxyFilter>();
            AddFilterButton<HVRMoveTowardsVixxyFilter>();
            AddFilterButton<HVRSmoothVixxyFilter>();
            EditorGUILayout.Separator();

            return false;
        }

        private void AddFilterButton<TFilter>() where TFilter : HVRVixxyFilterBase, new()
        {
            if (GUILayout.Button(string.Format(HVRVixxyLocalizationPhrase.AddFilterOfTypeLabel, DisplayNameOf<TFilter>())))
            {
                AddFilter(new TFilter());
            }
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
            EditorGUI.LabelField(rect, DisplayNameOf(element.managedReferenceValue.GetType(), element.managedReferenceValue));
        }

        private string DisplayNameOf<TFilter>() where TFilter : HVRVixxyFilterBase
        {
            return DisplayNameOf(typeof(TFilter));
        }

        private static string DisplayNameOf(Type type, object instance = null)
        {
            if (instance != null)
            {
                var prefix = DisplayNameOf(type, null);
                if (instance is HVRMoveTowardsVixxyFilter moveTowards) { return $"{prefix} ({moveTowards.secondsPerUnit:0.##}s per unit)"; }
                if (instance is HVRSmoothVixxyFilter smooth) { return $"{prefix} ({smooth.secondsPerUnit:0.##}s per unit)"; }
                return prefix;
            }

            return type switch
            {
                _ when type == typeof(HVRCurveVixxyFilter) => HVRVixxyLocalizationPhrase.CurveLabel,
                _ when type == typeof(HVRMoveTowardsVixxyFilter) => HVRVixxyLocalizationPhrase.LinearMoveTowardsValueLabel,
                _ when type == typeof(HVRSmoothVixxyFilter) => HVRVixxyLocalizationPhrase.SmoothTowardsValueLabel,
                _ => type.Name
            };
        }
    }
}
