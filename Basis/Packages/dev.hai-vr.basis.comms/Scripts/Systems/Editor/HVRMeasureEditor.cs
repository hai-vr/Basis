using System;
using HVR.Vixxy.Editor;
using UnityEditor;
using UnityEngine;

namespace HVR.Basis.Comms.Editor
{
    [CustomEditor(typeof(HVRMeasure))]
    public class HVRMeasureEditor : UnityEditor.Editor
    {
        private const string AbsoluteValueLabel = "Absolute Value";
        private const string AngleLabel = "Angle";
        private const string ChangeOverTimeLabel = "Change over time";
        private const string ConvertRangeLabel = "Convert range";
        private const string DistanceLabel = "Distance";
        private const string HitLabel = "Hit";
        private const string IsSpherecastLabel = "Is Spherecast";
        private const string MeasurementLabel = "Measurement";
        private const string MsgDescribeAngle = "Angle that separates Target A and Target B measured at the Origin object, in degrees.";
        private const string MsgDescribeDistance = "Distance between two objects.";
        private const string MsgDescribeRaycast = "Raycast from a source object.";
        private const string MsgDescribeRotation = "Compares the rotation of two objects, in degrees.\nIf roll is not included, it uses the forward direction of each object to measure the angle.";
        private const string MsgDescribeSpeed = "Speed of an object.";
        private const string MsgRaycastIsBasedOnTargetPosition = "A target is defined, so raycast direction and maximum distance do not matter.";
        private const string OriginLabel = "Origin";
        private const string OutputLabel = "Output";
        private const string RateOfChangeOfSpeedLabel = "Rate of change of speed";
        private const string SpeedLabel = "Speed";
        private const string SpherecastRadiusLabel = "Spherecast Radius";
        private const string TargetALabel = "Target A";
        private const string TargetBLabel = "Target B";
        private const string ValueBeforeRangeConversionLabel = "Value before range conversion";

        private HVRMeasure my;

        public override void OnInspectorGUI()
        {
            my = (HVRMeasure)target;
            HVRAvatarCommsEditor.EnsureAvatarHasPrefab(my.transform);

            EditorGUILayout.LabelField(MeasurementLabel, EditorStyles.boldLabel);

            // We're limiting the types because Speed and Raycast are not tested enough for the first release.
            var measurementTypeProp = serializedObject.FindProperty(nameof(HVRMeasure.measurementType));
            measurementTypeProp.enumValueIndex = EditorGUILayout.Popup(measurementTypeProp.displayName, measurementTypeProp.enumValueIndex, new string[] { "Distance", "Angle", "Rotation Difference" });

            var description = my.measurementType switch
            {
                HVRMeasureType.Distance => MsgDescribeDistance,
                HVRMeasureType.Angle => MsgDescribeAngle,
                HVRMeasureType.RotationDifference => MsgDescribeRotation,
                HVRMeasureType.Raycast => MsgDescribeRaycast,
                HVRMeasureType.Speed => MsgDescribeSpeed,
                _ => throw new ArgumentOutOfRangeException()
            };
            EditorGUILayout.HelpBox(description, MessageType.None);
            if (my.measurementType == HVRMeasureType.RotationDifference)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRMeasure.angleMeasurement)));
            }
            if (my.measurementType == HVRMeasureType.Raycast)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRMeasure.raycastDirection)));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRMeasure.raycastMaximumDistance)));

                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRMeasure.raycastIsSpherecast)), new GUIContent(IsSpherecastLabel));
                if (my.raycastIsSpherecast)
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRMeasure.physicsSphereRadius)), new GUIContent(SpherecastRadiusLabel));
                }
            }

            if (my.measurementType == HVRMeasureType.Speed)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRMeasure.speedMeasurement)));
                if (my.speedMeasurement != HVRMeasureSpeedKind.ThreeDimensional)
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRMeasure.speedProjection)));
                }
            }

            if (my.measurementType == HVRMeasureType.Speed || my.measurementType == HVRMeasureType.Raycast)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRMeasure.source)));
            }
            else if (my.measurementType == HVRMeasureType.Angle)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRMeasure.source)), new GUIContent(OriginLabel));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRMeasure.target)), new GUIContent(TargetALabel));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRMeasure.target2)), new GUIContent(TargetBLabel));
            }
            else
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRMeasure.source)));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRMeasure.target)));
            }

            if (my.measurementType != HVRMeasureType.Angle || my.measurementType != HVRMeasureType.RotationDifference)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRMeasure.space)));
                if (my.space == HVRMeasureSpace.Custom)
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRMeasure.spaceReference)));
                }
            }

            if (my.measurementType == HVRMeasureType.Raycast && my.target != null)
            {
                EditorGUILayout.HelpBox(MsgRaycastIsBasedOnTargetPosition, MessageType.Info);
            }

            EditorGUILayout.Separator();

            EditorGUILayout.LabelField(ConvertRangeLabel, EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRMeasure.remapFrom)));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRMeasure.remapTo)));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRMeasure.clampToBounds)));
            EditorGUILayout.Separator();

            EditorGUILayout.LabelField(OutputLabel, EditorStyles.boldLabel);

            if (my.measurementType == HVRMeasureType.Raycast)
            {
                LayoutAddressToggleSelector(serializedObject.FindProperty(nameof(HVRMeasure.hitAddress)), HitLabel, () => {});
            }

            var valueAddressLabel = my.measurementType == HVRMeasureType.Speed
                ? SpeedLabel
                : my.measurementType is HVRMeasureType.Angle or HVRMeasureType.RotationDifference
                ? AngleLabel
                : DistanceLabel;
            // NOTE: The rate of change of speed is not the acceleration in 3D, as the acceleration can be nonzero on a curved path of constant speed,
            // so do not call this acceleration.
            var changeOverTimeAddressLabel = my.measurementType == HVRMeasureType.Speed ? RateOfChangeOfSpeedLabel : ChangeOverTimeLabel;
            LayoutAddressToggleSelector(serializedObject.FindProperty(nameof(HVRMeasure.valueAddress)), valueAddressLabel, () => {});
            LayoutAddressToggleSelector(serializedObject.FindProperty(nameof(HVRMeasure.changeOverTimeAddress)), changeOverTimeAddressLabel, () =>
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRMeasure.differenceAbsoluteValue)), new GUIContent(AbsoluteValueLabel));
            });

            if (Application.isPlaying)
            {
                EditorGUILayout.Separator();
                EditorGUILayout.LabelField(HVRVixxyLocalizationPhrase.DeveloperViewLabel, EditorStyles.boldLabel);
                EditorGUILayout.FloatField(ValueBeforeRangeConversionLabel, my.LastIntermediateValue);
                EditorGUILayout.FloatField(valueAddressLabel, my.LastSentValue);
                if (my.clampToBounds)
                {
                    EditorGUILayout.Slider(my.LastSentValue, Mathf.Min(my.remapTo.x, my.remapTo.y), Mathf.Max(my.remapTo.x, my.remapTo.y));
                }
                if (my.changeOverTimeAddress.isActive)
                {
                    EditorGUILayout.FloatField(changeOverTimeAddressLabel, my.LastChangeOverTime);
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
