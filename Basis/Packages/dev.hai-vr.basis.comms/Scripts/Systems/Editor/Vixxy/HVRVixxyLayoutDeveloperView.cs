using System;
using HVR.Basis.Comms;
using HVR.Basis.Comms.Editor;
using UnityEditor;
using UnityEngine;

namespace HVR.Vixxy.Editor
{
    public class HVRVixxyLayoutDeveloperView
    {
        private const string AddPropertyOfTypeLabel = "+ Add Property of type {0}";
        private const string AddSubjectLabel = "+ Add Subject";
        private const string MsgAddressIsOptional = "Address is completely optional, we will generate one for you. If you need explicit control by external programs, then do specify one.";
        private const string MsgAvatarReadyNotApplied = "AvatarReady was not applied on the avatar of this component while we were listening.\nThis may be because this is a test scene and not a loaded avatar. If this isn't the case, this is a proper error.";
        private const string MsgPropertyFailedToResolve = "This property has failed to resolve. Reason: {0}";
        private const string RuntimeBakedDataLabel = "Runtime Baked Data";
        private const string SampleFromLabel = "Sample from";

        // ReSharper disable once InconsistentNaming
        private readonly HVRVixxyControl my;
        // ReSharper disable once InconsistentNaming
        private readonly SerializedObject serializedObject;

        public HVRVixxyLayoutDeveloperView(HVRVixxyControlEditor editor)
        {
            my = (HVRVixxyControl)editor.target;
            serializedObject = editor.serializedObject;
        }

        public bool Layout()
        {
            var isPlaying = Application.isPlaying;

            EditorGUI.BeginDisabledGroup(isPlaying);
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRVixxyControl.address)));
            if (string.IsNullOrWhiteSpace(my.address))
            {
                EditorGUILayout.HelpBox(MsgAddressIsOptional, MessageType.Info);
            }

            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRVixxyControl.hasThreeOrMoreChoices)));
            if (my.hasThreeOrMoreChoices)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRVixxyControl.numberOfChoices)));
            }
            EditorGUI.EndDisabledGroup();

            if (!isPlaying)
            {
                my.InterpolateFromChoiceApplies = false;
                my.InterpolateFromChoice = 0;
                my.InterpolateFromChoiceAmount01 = 0f;
            }

            if (isPlaying)
            {
                if (my.IsInitialized)
                {
                    HaiEFCommon.ColoredBackgroundVoid(true, HVRVixxyControlEditor.PreviewColor, () =>
                    {
                        EditorGUILayout.BeginVertical(HVRUiHelpers.GroupBoxStyle);
                        if (my.HasMoreThanTwoChoices)
                        {
                            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRVixxyControl.InterpolateFromChoiceApplies)));
                            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRVixxyControl.InterpolateFromChoice)));
                            EditorGUILayout.Slider(serializedObject.FindProperty(nameof(HVRVixxyControl.InterpolateFromChoiceAmount01)), 0f, 1f);
                        }
                        EditorGUI.BeginDisabledGroup(true);
                        EditorGUILayout.TextField(nameof(HVRVixxyControl.Address), my.Address);
                        EditorGUI.EndDisabledGroup();
                        if (my.HasMoreThanTwoChoices)
                        {
                            var slider = EditorGUILayout.IntSlider((int)my._value, 0, my.ActualNumberOfChoices - 1);
                            if (!Mathf.Approximately(slider, my._value))
                            {
                                AcquisitionService.SceneInstance.Submit(my.AddressIndex, slider);
                            }
                        }
                        else
                        {
                            var slider = EditorGUILayout.Slider(my._value, 0, my.ActualNumberOfChoices - 1);
                            if (!Mathf.Approximately(slider, my._value))
                            {
                                AcquisitionService.SceneInstance.Submit(my.AddressIndex, slider);
                            }
                        }
                        EditorGUILayout.EndVertical();
                    });
                }

                HaiEFCommon.ColoredBackgroundVoid(true, HVRVixxyControlEditor.RuntimeColorOK, () =>
                {
                    EditorGUILayout.BeginVertical(HVRUiHelpers.GroupBoxStyle);
                    EditorGUILayout.Toggle(nameof(HVRVixxyControl.IsInitialized), my.IsInitialized);
                    EditorGUILayout.Toggle(nameof(HVRVixxyControl.WasAvatarReadyApplied), my.WasAvatarReadyApplied);
                    if (!my.WasAvatarReadyApplied)
                    {
                        HaiEFCommon.ColoredBackgroundVoid(true, Color.white, () => { EditorGUILayout.HelpBox(MsgAvatarReadyNotApplied, MessageType.Error); });
                    }
                    EditorGUILayout.Toggle(nameof(HVRVixxyControl.IsWearer), my.IsWearer);
                    EditorGUILayout.TextField(nameof(HVRVixxyControl.Address), my.Address);
                    EditorGUILayout.Toggle(nameof(HVRVixxyControl.HasMoreThanTwoChoices), my.HasMoreThanTwoChoices);
                    EditorGUILayout.IntField(nameof(HVRVixxyControl.ActualNumberOfChoices), my.ActualNumberOfChoices);
                    EditorGUILayout.Toggle(nameof(HVRVixxyControl.AlsoExecutesWhenDisabled), my.AlsoExecutesWhenDisabled);
                    EditorGUILayout.EndVertical();
                });
            }

            var subjectsSp = serializedObject.FindProperty(nameof(HVRVixxyControl.subjects));
            for (var subjectIndex = 0; subjectIndex < subjectsSp.arraySize; subjectIndex++)
            {
                var subjectSp = subjectsSp.GetArrayElementAtIndex(subjectIndex);
                EditorGUILayout.BeginVertical(HVRUiHelpers.GroupBoxStyle);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"[{subjectIndex}] Subject", EditorStyles.boldLabel);
                if (HaiEFCommon.ColoredBackground(true, Color.red, () => GUILayout.Button($"{HVRUiHelpers.CrossSymbol}", GUILayout.Width(HVRVixxyControlEditor.DeleteButtonWidth))))
                {
                    subjectsSp.DeleteArrayElementAtIndex(subjectIndex);

                    serializedObject.ApplyModifiedProperties();
                    return true;
                }
                EditorGUILayout.EndHorizontal();

                var selectionSp = subjectSp.FindPropertyRelative(nameof(HVRVixxySubject.selection));
                EditorGUILayout.PropertyField(selectionSp);
                var selection = (HVRVixxySelection)selectionSp.intValue;
                if (selection == HVRVixxySelection.Normal)
                {
                    EditorGUILayout.PropertyField(subjectSp.FindPropertyRelative(nameof(HVRVixxySubject.targets)));
                }
                else if (selection == HVRVixxySelection.RecursiveSearch)
                {
                    EditorGUILayout.PropertyField(subjectSp.FindPropertyRelative(nameof(HVRVixxySubject.childrenOf)));
                    EditorGUILayout.PropertyField(subjectSp.FindPropertyRelative(nameof(HVRVixxySubject.exceptions)));
                }

                if (selection != HVRVixxySelection.Normal)
                {
                    EditorGUILayout.LabelField(SampleFromLabel);
                    EditorGUILayout.PropertyField(subjectSp.FindPropertyRelative(nameof(HVRVixxySubject.targets))); // TODO: Only show 0th value
                }

                if (isPlaying)
                {
                    var it = my.subjects[subjectIndex];
                    HaiEFCommon.ColoredBackgroundVoid(true, it.IsApplicable ? HVRVixxyControlEditor.RuntimeColorOK : HVRVixxyControlEditor.RuntimeColorKO, () =>
                    {
                        // var it = (HVRVixxySubject)subjectSp.boxedValue; // This doesn't work. It returns a default struct
                        EditorGUILayout.BeginVertical(HVRUiHelpers.GroupBoxStyle);
                        EditorGUILayout.LabelField(RuntimeBakedDataLabel, EditorStyles.boldLabel);
                        EditorGUILayout.Toggle(nameof(HVRVixxySubject.IsApplicable), it.IsApplicable);
                        EditorGUILayout.LabelField(nameof(HVRVixxySubject.BakedObjects));
                        if (it.IsApplicable)
                        {
                            foreach (var found in it.BakedObjects)
                            {
                                EditorGUILayout.ObjectField(found, found.GetType(), true);
                            }
                        }
                        EditorGUILayout.EndVertical();
                    });
                }

                var propertiesSp = subjectSp.FindPropertyRelative(nameof(HVRVixxySubject.properties));
                EditorGUILayout.LabelField($"Properties ({propertiesSp.arraySize})", EditorStyles.boldLabel);
                for (var propertyIndex = 0; propertyIndex < propertiesSp.arraySize; propertyIndex++)
                {
                    var propertySp = propertiesSp.GetArrayElementAtIndex(propertyIndex);
                    if (DrawPropertyOrReturn(propertySp, propertyIndex, propertiesSp, isPlaying)) return true;
                }

                ButtonToAddProperty(propertiesSp, "float", () => new HVRVixxyPropertyFloat());
                ButtonToAddProperty(propertiesSp, "bool", () => new HVRVixxyPropertyBool());
                ButtonToAddProperty(propertiesSp, nameof(Color), () => new HVRVixxyPropertyColor());
                ButtonToAddProperty(propertiesSp, nameof(Vector4), () => new HVRVixxyPropertyVector4());
                ButtonToAddProperty(propertiesSp, nameof(Vector3), () => new HVRVixxyPropertyVector3());
                ButtonToAddProperty(propertiesSp, nameof(Quaternion), () => new HVRVixxyPropertyQuaternion());
                ButtonToAddProperty(propertiesSp, nameof(Material), () => new HVRVixxyPropertyMaterial());
                ButtonToAddProperty(propertiesSp, nameof(Mesh), () => new HVRVixxyPropertyMesh());

                EditorGUILayout.EndVertical();
            }
            if (GUILayout.Button(AddSubjectLabel))
            {
                subjectsSp.arraySize += 1;
            }

            return false;
        }

        private static void ButtonToAddProperty(SerializedProperty propertiesSp, string name, Func<object> factoryFn)
        {
            if (GUILayout.Button(string.Format(AddPropertyOfTypeLabel, name)))
            {
                var indexToPutData = propertiesSp.arraySize;
                propertiesSp.arraySize = indexToPutData + 1;
                propertiesSp.GetArrayElementAtIndex(indexToPutData).managedReferenceValue = factoryFn.Invoke();
            }
        }

        private bool DrawPropertyOrReturn(SerializedProperty propertySp, int propertyIndex, SerializedProperty propertiesSp, bool isPlaying)
        {
            EditorGUILayout.BeginVertical(HVRUiHelpers.GroupBoxStyle);
            EditorGUILayout.BeginHorizontal();
            var managedReferenceValue = propertySp.managedReferenceValue;
            var managedReferenceValueType = managedReferenceValue.GetType();

            var inheritsFromVixxyProperty = false;
            Type genericType = null;
            {
                var currentType = managedReferenceValueType;
                while (currentType != null && currentType != typeof(object))
                {
                    if (currentType.IsGenericType && currentType.GetGenericTypeDefinition() == typeof(HVRVixxyProperty<>))
                    {
                        inheritsFromVixxyProperty = true;
                        if (genericType == null)
                        {
                            genericType = currentType.GetGenericArguments()[0];
                        }
                        break;
                    }
                    currentType = currentType.BaseType;
                }
            }

            if (inheritsFromVixxyProperty)
            {
                EditorGUILayout.LabelField($"[{propertyIndex}] Property of type {genericType.Name}", EditorStyles.boldLabel);
            }
            else if (managedReferenceValue is HVRVixxyPropertyBase)
            {
                EditorGUILayout.LabelField($"[{propertyIndex}] Specialized Property of type {managedReferenceValueType.Name}", EditorStyles.boldLabel);
            }
            else
            {
                EditorGUILayout.LabelField($"[{propertyIndex}] CAUTION: Not a HVRVixxyPropertyBase, type is {managedReferenceValueType.FullName}", EditorStyles.boldLabel);
            }

            if (HaiEFCommon.ColoredBackground(true, Color.red, () => GUILayout.Button($"{HVRUiHelpers.CrossSymbol}", GUILayout.Width(HVRVixxyControlEditor.DeleteButtonWidth))))
            {
                propertiesSp.DeleteArrayElementAtIndex(propertyIndex);

                serializedObject.ApplyModifiedProperties();
                return true; // Workaround array size change error
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.PropertyField(propertySp.FindPropertyRelative(nameof(HVRVixxyPropertyBase.fullClassName)));
            EditorGUILayout.PropertyField(propertySp.FindPropertyRelative(nameof(HVRVixxyPropertyBase.variant)));
            EditorGUILayout.PropertyField(propertySp.FindPropertyRelative(nameof(HVRVixxyPropertyBase.propertyName)));
            if (inheritsFromVixxyProperty)
            {
                var choicesSp = propertySp.FindPropertyRelative(nameof(HVRVixxyProperty<object>.choices));
                if (my.hasThreeOrMoreChoices)
                {
                    if (choicesSp.arraySize < my.numberOfChoices)
                    {
                        choicesSp.arraySize = my.numberOfChoices;
                    }
                    for (var choiceIndex = 0; choiceIndex < my.numberOfChoices; choiceIndex++)
                    {
                        EditorGUILayout.PropertyField(choicesSp.GetArrayElementAtIndex(choiceIndex), new GUIContent($"Value for #{choiceIndex}"));
                    }
                }
                else
                {
                    EditorGUILayout.PropertyField(choicesSp.GetArrayElementAtIndex(HVRVixxyPropertyBase.InactiveIndex), new GUIContent("Inactive"));
                    EditorGUILayout.PropertyField(choicesSp.GetArrayElementAtIndex(HVRVixxyPropertyBase.ActiveIndex), new GUIContent("Active"));
                }
            }

            if (managedReferenceValueType == typeof(HVRVixxyPropertyColor))
            {
                EditorGUILayout.PropertyField(propertySp.FindPropertyRelative(nameof(HVRVixxyPropertyColor.interpolation)));
            }

            if (isPlaying)
            {
                var it = (HVRVixxyPropertyBase)managedReferenceValue;
                HaiEFCommon.ColoredBackgroundVoid(true, it.IsApplicable ? HVRVixxyControlEditor.RuntimeColorOK : HVRVixxyControlEditor.RuntimeColorKO, () =>
                {
                    EditorGUILayout.BeginVertical(HVRUiHelpers.GroupBoxStyle);
                    EditorGUILayout.LabelField(RuntimeBakedDataLabel, EditorStyles.boldLabel);
                    EditorGUILayout.Toggle(nameof(HVRVixxyPropertyBase.IsApplicable), it.IsApplicable);
                    EditorGUILayout.EnumPopup(nameof(HVRVixxyPropertyBase.BakeResult), it.BakeResult);
                    if (it.IsApplicable)
                    {
                        EditorGUILayout.ObjectField(nameof(HVRVixxyPropertyBase.FoundType), null, it.FoundType, false);
                        EditorGUILayout.EnumPopup(nameof(HVRVixxyPropertyBase.KindMarker), it.KindMarker);
                        if (it.KindMarker == HVRKindMarker.BlendShape)
                        {
                            EditorGUILayout.LabelField(nameof(HVRVixxyPropertyBase.SmrToBlendshapeIndex), EditorStyles.boldLabel);
                            foreach (var smrToBlendshapeIndex in it.SmrToBlendshapeIndex)
                            {
                                EditorGUILayout.BeginHorizontal();
                                EditorGUILayout.ObjectField(smrToBlendshapeIndex.Key, typeof(SkinnedMeshRenderer), false);
                                EditorGUILayout.TextField($"{smrToBlendshapeIndex.Value}");
                                EditorGUILayout.EndHorizontal();
                            }
                        }
                        EditorGUILayout.LabelField(nameof(HVRVixxyPropertyBase.FoundComponents));
                        foreach (var found in it.FoundComponents)
                        {
                            EditorGUILayout.ObjectField(found, found.GetType(), true);
                        }
                    }
                    else
                    {
                        HaiEFCommon.ColoredBackgroundVoid(true, Color.white, () => { EditorGUILayout.HelpBox(string.Format(MsgPropertyFailedToResolve, it.BakeResult), MessageType.Error); });
                    }
                    EditorGUILayout.EndVertical();
                });
            }

            EditorGUILayout.EndVertical();
            return false;
        }
    }
}
