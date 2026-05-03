using System;
using HVR.Vixxy.Editor;
using UnityEditor;
using UnityEngine;

namespace HVR.Basis.Comms.Editor
{
    [CustomEditor(typeof(HVRMeasure))]
    public class HVRMeasureEditor : UnityEditor.Editor
    {
        private const string MeasurementLabel = "Measurement";
        private const string PostProcessingLabel = "Post-processing";
        private const string OutputLabel = "Output";
        private const string IrrelevantLabel = "Irrelevant";
        private const string AbsoluteValueLabel = "Absolute Value";
        private const string MsgRaycastIsBasedOnTargetPosition = "A target is defined, so raycast direction and maximum distance do not matter.";

        private HVRMeasure my;

        public override void OnInspectorGUI()
        {
            my = (HVRMeasure)target;
            HVRAvatarCommsEditor.EnsureAvatarHasPrefab(my.transform);

            var isRaycastOrSpherecast = my.measurementType is HVRMeasureType.Raycast or HVRMeasureType.Spherecast;

            EditorGUILayout.LabelField(MeasurementLabel, EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRMeasure.measurementType)));
            if (my.measurementType == HVRMeasureType.ComplexRotationAngle)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRMeasure.angleMeasurement)));
            }
            if (isRaycastOrSpherecast)
            {
                if (my.target == null)
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRMeasure.raycastDirection)));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRMeasure.raycastMaximumDistance)));
                }
                else
                {
                    EditorGUI.BeginDisabledGroup(true);
                    EditorGUILayout.TextField(ObjectNames.NicifyVariableName(nameof(HVRMeasure.raycastDirection)), IrrelevantLabel);
                    EditorGUILayout.TextField(ObjectNames.NicifyVariableName(nameof(HVRMeasure.raycastMaximumDistance)), IrrelevantLabel);
                    EditorGUI.EndDisabledGroup();
                }
            }

            if (my.measurementType == HVRMeasureType.Spherecast)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRMeasure.spherecastRadius)));
            }

            if (my.measurementType != HVRMeasureType.Angle)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRMeasure.source)));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRMeasure.target)));
            }
            else
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRMeasure.source)), new GUIContent("Origin"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRMeasure.target)), new GUIContent("Target A"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRMeasure.target2)), new GUIContent("Target B"));
            }

            if (isRaycastOrSpherecast && my.target != null)
            {
                EditorGUILayout.HelpBox(MsgRaycastIsBasedOnTargetPosition, MessageType.Info);
            }

            EditorGUILayout.Separator();

            EditorGUILayout.LabelField(PostProcessingLabel, EditorStyles.boldLabel);
            if (my.measurementType == HVRMeasureType.ComplexRotationAngle)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRMeasure.remapFrom)));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRMeasure.remapTo)));
            }
            else
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRMeasure.remapFrom)));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRMeasure.remapTo)));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRMeasure.clampToBounds)));
            }
            EditorGUILayout.Separator();

            EditorGUILayout.LabelField(OutputLabel, EditorStyles.boldLabel);

            if (isRaycastOrSpherecast)
            {
                LayoutAddressToggleSelector(serializedObject.FindProperty(nameof(HVRMeasure.hitAddress)), "Hit", () => {});
            }
            LayoutAddressToggleSelector(serializedObject.FindProperty(nameof(HVRMeasure.distanceAddress)), "Distance", () => {});
            LayoutAddressToggleSelector(serializedObject.FindProperty(nameof(HVRMeasure.changeOverTimeAddress)), "Change over time", () =>
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRMeasure.differenceAbsoluteValue)), new GUIContent(AbsoluteValueLabel));
            });

            if (Application.isPlaying)
            {
                EditorGUILayout.Separator();
                EditorGUILayout.LabelField(HVRVixxyLocalizationPhrase.DeveloperViewLabel, EditorStyles.boldLabel);
                EditorGUILayout.FloatField("Value before post-processing", my.LastIntermediateValue);
                EditorGUILayout.FloatField("Value", my.LastSentValue);
                if (my.changeOverTimeAddress.isActive)
                {
                    EditorGUILayout.FloatField("Change over time", my.LastChangeOverTime);
                }

                // This forces the inspector to re-draw every frame when the application is playing with this inspector open,
                // for easier debugging.
                Repaint();
                if (serializedObject.hasModifiedProperties)
                {
                    my.DebugForceUpdate = true;
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void LayoutAddressToggleSelector(SerializedProperty addressSelectorToggleSp, string label, Action additionalLayoutFn)
        {
            var isActiveSp = addressSelectorToggleSp.FindPropertyRelative(nameof(HVRAddressSelectorToggle.isActive));
            EditorGUILayout.PropertyField(isActiveSp, new GUIContent(label));
            if (isActiveSp.boolValue)
            {
                EditorGUILayout.BeginVertical(HVR_EditorHelpers.GroupBoxStyle);
                HVRVixxyControlEditor.LayoutAddressSelector(addressSelectorToggleSp.FindPropertyRelative(nameof(HVRAddressSelectorToggle.address)));
                additionalLayoutFn();
                EditorGUILayout.EndVertical();
            }
        }
    }
}
