using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HVR.Vixxy.Editor
{
    internal class HVRVixxyLayoutToggleObjectsView
    {
        private const string ToggleLabel = "Toggle";
        private const string EnableTheseWhenActiveLabel = "Enable these when active";
        private const string DisableTheseWhenActiveLabel = "Disable these when active";
        private const string CurrentLabel = "(current)";

        private readonly HVRVixxyControl my;
        private readonly SerializedObject serializedObject;

        internal HVRVixxyLayoutToggleObjectsView(HVRVixxyControlEditor editor)
        {
            my = (HVRVixxyControl)editor.target;
            serializedObject = editor.serializedObject;
        }

        internal bool LayoutToggleObjects()
        {
            EditorGUILayout.Separator();

            EditorGUILayout.LabelField(ToggleLabel, EditorStyles.boldLabel);
            var activationsSp = serializedObject.FindProperty(nameof(HVRVixxyControl.activations));
            if (!my.hasThreeOrMoreChoices)
            {
                EditorGUILayout.LabelField(EnableTheseWhenActiveLabel);
                DisplayActivations(activationsSp, true);
                EditorGUILayout.LabelField(DisableTheseWhenActiveLabel);
                DisplayActivations(activationsSp, false);
            }
            else
            {
                DisplayActivations(activationsSp, false);
            }
            EditorGUILayout.Separator();

            return false;
        }

        private void DisplayActivations(SerializedProperty activationsSp, bool showThoseActive)
        {
            // showThoseActive must be ignored when we have three or more choices

            for (var i = 0; i < activationsSp.arraySize; i++)
            {
                if (my.hasThreeOrMoreChoices)
                {
                    EditorGUILayout.BeginVertical("GroupBox");
                }

                var elementSp = activationsSp.GetArrayElementAtIndex(i);
                var choicesSp = elementSp.FindPropertyRelative(nameof(HVRVixxyActivation.choices));
                var arrayElementAtIndex = choicesSp.GetArrayElementAtIndex(HVRVixxyPropertyBase.ActiveIndex);
                var isWhenActive = arrayElementAtIndex.boolValue;
                if (my.hasThreeOrMoreChoices || isWhenActive == showThoseActive)
                {
                    var componentSp = elementSp.FindPropertyRelative(nameof(HVRVixxyActivation.component));

                    EditorGUILayout.BeginHorizontal();
                    if (componentSp.objectReferenceValue != null && componentSp.objectReferenceValue.GetType() == typeof(Transform))
                    {
                        var t = (Transform)componentSp.objectReferenceValue;
                        var obj = EditorGUILayout.ObjectField(GUIContent.none, t.gameObject, typeof(GameObject));
                        if (obj != t.gameObject && obj != t)
                        {
                            componentSp.objectReferenceValue = obj;
                        }
                    }
                    else
                    {
                        EditorGUILayout.PropertyField(componentSp, GUIContent.none);
                    }
                    EditorGUILayout.PropertyField(elementSp.FindPropertyRelative(nameof(HVRVixxyActivation.threshold)), GUIContent.none, GUILayout.Width(70));

                    {
                        var allComponents = new []{ "Type..." }.Concat(((Component)(componentSp.objectReferenceValue)).gameObject
                                .GetComponents<Component>() // GetComponents may contain null values for unloadable MonoBehaviours
                                .Where(component => component != null)
                                // .Where(component => component.GetType() != element.objectReferenceValue.GetType())
                                .Select(component =>
                                {
                                    var name = component.GetType().Name;
                                    return (name == "Transform" ? "GameObject" : name) + (component.GetType() == componentSp.objectReferenceValue.GetType() ? $" {CurrentLabel}" : "");
                                })
                                .Distinct())
                            .ToArray();
                        var switching = EditorGUILayout.Popup(0, allComponents, GUILayout.Width(60));
                        if (switching > 0)
                        {
                            var components = ((Component)(componentSp.objectReferenceValue)).gameObject
                                .GetComponents<Component>() // GetComponents may contain null values for unloadable MonoBehaviours
                                .Where(component => component != null)
                                // .Where(component => component.GetType() != element.objectReferenceValue.GetType())
                                .Distinct()
                                .ToArray();
                            componentSp.objectReferenceValue = components[switching - 1];
                        }
                    }

                    if (!my.hasThreeOrMoreChoices && GUILayout.Button("⇅", GUILayout.Width(25)))
                    {
                        choicesSp.GetArrayElementAtIndex(HVRVixxyPropertyBase.InactiveIndex).boolValue = showThoseActive;
                        choicesSp.GetArrayElementAtIndex(HVRVixxyPropertyBase.ActiveIndex).boolValue = !showThoseActive;
                    }
                    if (GUILayout.Button(HVRUiHelpers.CrossSymbol, GUILayout.Width(25)))
                    {
                        activationsSp.GetArrayElementAtIndex(i).objectReferenceValue = null;
                        activationsSp.DeleteArrayElementAtIndex(i);
                        return; // Reason why the return is here: Please check the comment in VixenLayoutChangeProperties, look for a DeleteArrayElementAtIndex invocation.
                    }

                    EditorGUILayout.EndHorizontal();
                    if (my.hasThreeOrMoreChoices)
                    {
                        EditorGUILayout.PropertyField(choicesSp);
                    }
                }

                if (my.hasThreeOrMoreChoices)
                {
                    EditorGUILayout.EndVertical();
                }
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(HVRUiHelpers.PlusSymbol, GUILayout.Width(15));
            var newComponent = EditorGUILayout.ObjectField(null, typeof(Component), true);
            if (newComponent != null)
            {
                var newIndex = activationsSp.arraySize;
                activationsSp.InsertArrayElementAtIndex(newIndex);
                var newElementSp = activationsSp.GetArrayElementAtIndex(newIndex);
                newElementSp.FindPropertyRelative(nameof(HVRVixxyActivation.choices)).arraySize = my.numberOfChoices;
                newElementSp.FindPropertyRelative(nameof(HVRVixxyActivation.choices)).GetArrayElementAtIndex(HVRVixxyPropertyBase.ActiveIndex).boolValue = showThoseActive;
                newElementSp.FindPropertyRelative(nameof(HVRVixxyActivation.choices)).GetArrayElementAtIndex(HVRVixxyPropertyBase.InactiveIndex).boolValue = !showThoseActive;
                newElementSp.FindPropertyRelative(nameof(HVRVixxyActivation.component)).objectReferenceValue = newComponent;
            }
            EditorGUILayout.EndHorizontal();
        }
    }
}
