using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HVR.Basis.Comms;
using HVR.Basis.Comms.Editor;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HVR.Vixxy.Editor
{
    internal class VLayoutChangeProperties
    {
        private const int MaxSearchQueryLength = 100;
        private static readonly Regex HasAnyNonLetterNonSpace = new Regex(@"[^A-Za-z\s]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly string[] CoordsSuffixes = {".x", ".y", ".z", ".w", ".r", ".g", ".b", ".a"};

        private readonly HVRVixxyControl my;
        private readonly SerializedObject serializedObject;
        private readonly ReorderableList subjectsReorderableList;

        private Object _previousRootObject;
        private readonly Dictionary<Type, int> _typeToWhichOpened = new();
        private List<Type> _types;
        private List<object> _properties;
        private List<string> _blendshapes;
        private Dictionary<MaterialPropertyType, List<string>> _materialProperties;

        private string _search;
        private bool _focusNext;
        private List<Type> _nonTypes;

        internal VLayoutChangeProperties(HVRVixxyControlEditor editor)
        {
            my = (HVRVixxyControl)editor.target;
            serializedObject = editor.serializedObject;

            // reference: https://blog.terresquall.com/2020/03/creating-reorderable-lists-in-the-unity-inspector/
            subjectsReorderableList = new ReorderableList(
                serializedObject,
                serializedObject.FindProperty(nameof(HVRVixxyControl.subjects)),
                true, true, true, true
            );
            subjectsReorderableList.drawElementCallback = SubjectsListElement;
            subjectsReorderableList.drawHeaderCallback =
                rect => EditorGUI.LabelField(rect, HVRVixxyLocalizationPhrase.ObjectGroupsLabel);
            subjectsReorderableList.onAddCallback = list =>
            {
                ++list.serializedProperty.arraySize;
                var newIndex = list.serializedProperty.arraySize - 1;
                var element = list.serializedProperty.GetArrayElementAtIndex(newIndex);
                // element.FindPropertyRelative(nameof(HVRVixxySubject.selection)).intValue = (int)HVRVixxySelection.Normal;
                // element.FindPropertyRelative(nameof(HVRVixxySubject.targets)).arraySize = 0;
                // element.FindPropertyRelative(nameof(HVRVixxySubject.childrenOf)).arraySize = 0;
                // element.FindPropertyRelative(nameof(HVRVixxySubject.exceptions)).arraySize = 0;
                // element.FindPropertyRelative(nameof(HVRVixxySubject.properties)).arraySize = 0;
                serializedObject.ApplyModifiedProperties();
                subjectsReorderableList.index = newIndex;
            };
            subjectsReorderableList.elementHeight = EditorGUIUtility.singleLineHeight * 1;
        }

        public bool LayoutChangePropertiesPart()
        {
            EditorGUILayout.Separator();
            subjectsReorderableList.DoLayoutList();
            var selectedIndex = subjectsReorderableList.index;
            if (selectedIndex != -1 && selectedIndex < subjectsReorderableList.count)
            {
                EditorGUILayout.BeginVertical(HVREditorHelpers.GroupBoxStyle);
                var subjectSp = subjectsReorderableList.serializedProperty.GetArrayElementAtIndex(selectedIndex);
                var mySelectedElement = my.subjects[selectedIndex];

                EditorGUILayout.LabelField($"{HVRVixxyLocalizationPhrase.ObjectGroupLabel} #{selectedIndex + 1}", EditorStyles.boldLabel);

                EditorGUILayout.PropertyField(subjectSp.FindPropertyRelative(nameof(HVRVixxySubject.selection)));
                if (mySelectedElement.selection == HVRVixxySelection.Normal)
                {
                    EditorGUILayout.LabelField(HVRVixxyLocalizationPhrase.ChangeTheseObjectsLabel);
                    CreateArrayAddition(subjectSp.FindPropertyRelative(nameof(HVRVixxySubject.targets)), typeof(GameObject));
                }
                else if (mySelectedElement.selection == HVRVixxySelection.RecursiveSearch)
                {
                    EditorGUILayout.LabelField(HVRVixxyLocalizationPhrase.ChangeTheseObjectsAndTheirChildrenLabel);
                    CreateArrayAddition(subjectSp.FindPropertyRelative(nameof(HVRVixxySubject.childrenOf)),
                        typeof(GameObject));

                    EditorGUILayout.LabelField(HVRVixxyLocalizationPhrase.DoNotChangeTheseObjectsLabel);
                    CreateArrayAddition(subjectSp.FindPropertyRelative(nameof(HVRVixxySubject.exceptions)),
                        typeof(GameObject));
                }
                else if (mySelectedElement.selection == HVRVixxySelection.Everything)
                {
                    EditorGUILayout.HelpBox(HVRVixxyLocalizationPhrase.MsgEverthingInContext, MessageType.Info);

                    EditorGUILayout.LabelField(HVRVixxyLocalizationPhrase.DoNotChangeTheseObjectsLabel);
                    CreateArrayAddition(subjectSp.FindPropertyRelative(nameof(HVRVixxySubject.exceptions)),
                        typeof(GameObject));
                }

                EditorGUILayout.Separator();

                EditorGUILayout.LabelField(HVRVixxyLocalizationPhrase.PropertiesLabel, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(HVRVixxyLocalizationPhrase.SampleFromLabel);
                EditorGUI.BeginDisabledGroup(mySelectedElement.selection == HVRVixxySelection.Normal);
                CreateArrayAddition(subjectSp.FindPropertyRelative(nameof(HVRVixxySubject.targets)), typeof(GameObject), true);
                EditorGUI.EndDisabledGroup();

                var validTargets = mySelectedElement.targets
                    .Where(o => o != null)
                    .Distinct()
                    .ToArray();

                var propertiesSp = subjectSp.FindPropertyRelative(nameof(HVRVixxySubject.properties));
                if (validTargets.Length > 0)
                {
                    var targetObject = validTargets.First();
                    var rootObject = targetObject;

                    if (_previousRootObject != rootObject || _types == null)
                    {
                        _previousRootObject = rootObject;
                        (_types, _nonTypes) = targetObject.GetComponents<Component>()
                            .Where(component => component != null)
                            .Select(component => component.GetType())
                            .Distinct()
                            .Partition(type => HVRVixxyPermitted.IsPermitted(type.FullName));
                        _types.Remove(typeof(Transform));
                        _types.Add(typeof(Transform));

                        foreach (var type in _types)
                        {
                            _typeToWhichOpened.Add(type, -1);
                        }

                        if (targetObject.GetComponent<SkinnedMeshRenderer>() is { } smr)
                        {
                            _blendshapes = HVREditorHelpers.ListAllBlendshapes(smr);
                        }
                        else
                        {
                            _blendshapes = null;
                        }
                        if (targetObject.GetComponent<Renderer>() is { } renderer)
                        {
                            _materialProperties = HVREditorHelpers.ListMostMaterialProperties(renderer);
                        }
                        else
                        {
                            _materialProperties = null;
                        }
                    }

                    foreach (var type in _types)
                    {
                        GUILayout.BeginVertical(HVREditorHelpers.GroupBoxStyle);
                        DisplayComponentBox(type, targetObject, subjectSp, mySelectedElement, rootObject);

                        for (var propertyIndex = 0; propertyIndex < propertiesSp.arraySize; propertyIndex++)
                        {
                            var propertySp = propertiesSp.GetArrayElementAtIndex(propertyIndex);
                            var fullClassName = propertySp.FindPropertyRelative(nameof(HVRVixxyPropertyBase.fullClassName)).stringValue;

                            if (fullClassName != type.FullName) continue;
                            if (DrawPropertyOrReturn(propertySp, propertyIndex, propertiesSp)) return true;
                        }

                        GUILayout.EndVertical();
                    }

                    if (_nonTypes.Count > 0)
                    {
                        EditorGUILayout.HelpBox("The types of the following components are not in the list of allowed types, so they cannot be modified nor toggled.", MessageType.Info);
                        foreach (var nonType in _nonTypes)
                        {
                            EditorGUI.BeginDisabledGroup(true);
                            DisplayComponentObjectField(targetObject, nonType);
                            EditorGUI.EndDisabledGroup();
                        }
                    }
                }

                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.Separator();

            return false;
        }

        private bool DrawPropertyOrReturn(SerializedProperty propertySp, int propertyIndex, SerializedProperty propertiesSp)
        {
            EditorGUILayout.BeginVertical(HVREditorHelpers.GroupBoxStyle);
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

            if (HaiEFCommon.ColoredBackground(true, Color.red, () => GUILayout.Button($"{HVREditorHelpers.CrossSymbol}", GUILayout.Width(HVRVixxyControlEditor.DeleteButtonWidth))))
            {
                propertiesSp.DeleteArrayElementAtIndex(propertyIndex);

                serializedObject.ApplyModifiedProperties();
                return true; // Workaround array size change error
            }

            EditorGUILayout.EndHorizontal();

            if (managedReferenceValueType == typeof(HVRVixxyPropertyColor)
                && managedReferenceValue is HVRVixxyPropertyColor color
                && color.propertyName.ToLowerInvariant().Contains("hdr"))
            {
                EditorGUILayout.HelpBox("HDR is in the name of the property, but this is set up as a Color. You should probably delete this and select HDR instead when adding the property.", MessageType.Warning);
            }

            EditorGUI.BeginDisabledGroup(true);
            // EditorGUILayout.PropertyField(propertySp.FindPropertyRelative(nameof(HVRVixxyPropertyBase.fullClassName)));
            EditorGUILayout.PropertyField(propertySp.FindPropertyRelative(nameof(HVRVixxyPropertyBase.variant)));
            EditorGUI.EndDisabledGroup();

            EditorGUI.BeginDisabledGroup(managedReferenceValue is HVRVixxyPropertyBase { variant: HVRVixxyPropertyVariant.Standard });
            EditorGUILayout.PropertyField(propertySp.FindPropertyRelative(nameof(HVRVixxyPropertyBase.propertyName)));
            EditorGUI.EndDisabledGroup();

            if (inheritsFromVixxyProperty)
            {
                var choicesSp = propertySp.FindPropertyRelative(nameof(HVRVixxyProperty<object>.choices));
                if (my.IsRegularToggle)
                {
                    EditorGUILayout.PropertyField(choicesSp.GetArrayElementAtIndex(HVRVixxyPropertyBase.InactiveIndex), new GUIContent("Inactive"));
                    EditorGUILayout.PropertyField(choicesSp.GetArrayElementAtIndex(HVRVixxyPropertyBase.ActiveIndex), new GUIContent("Active"));
                }
                else
                {
                    var choices = my.choices;
                    if (choicesSp.arraySize < my.NumberOfChoices)
                    {
                        choicesSp.arraySize = my.NumberOfChoices;
                    }

                    for (var choiceIndex = 0; choiceIndex < my.NumberOfChoices; choiceIndex++)
                    {
                        var description = HVRVixxyControlEditor.EditorChoiceDescription(choiceIndex, choices);
                        EditorGUILayout.PropertyField(choicesSp.GetArrayElementAtIndex(choiceIndex), new GUIContent(description));
                    }
                }
            }

            if (managedReferenceValueType == typeof(HVRVixxyPropertyColor))
            {
                EditorGUILayout.PropertyField(propertySp.FindPropertyRelative(nameof(HVRVixxyPropertyColor.interpolation)));
            }

            EditorGUILayout.EndVertical();
            return false;
        }

        private static void CreateArrayAddition(SerializedProperty whichArrayProperty, Type arrayType, bool limitToOne = false)
        {
            var useComponentDropdown = arrayType == typeof(Component);
            for (var i = 0; i < (limitToOne ? Math.Min(1, whichArrayProperty.arraySize) : whichArrayProperty.arraySize); i++)
            {
                EditorGUILayout.BeginHorizontal();
                var element = whichArrayProperty.GetArrayElementAtIndex(i);

                if (useComponentDropdown && element.objectReferenceValue != null && element.objectReferenceValue.GetType() == typeof(Transform))
                {
                    var t = (Transform)element.objectReferenceValue;
                    var obj = EditorGUILayout.ObjectField(GUIContent.none, t.gameObject, typeof(GameObject));
                    if (obj != t.gameObject && obj != t)
                    {
                        element.objectReferenceValue = obj;
                    }
                }
                else
                {
                    EditorGUILayout.PropertyField(element, GUIContent.none);
                }

                if (useComponentDropdown && element.objectReferenceValue != null)
                {
                    var allComponents = new []{ "Type..." }.Concat(((Component)(element.objectReferenceValue)).gameObject
                        .GetComponents<Component>() // GetComponents may contain null values for unloadable MonoBehaviours
                        .Where(component => component != null)
                        // .Where(component => component.GetType() != element.objectReferenceValue.GetType())
                        .Select(component =>
                        {
                            var name = component.GetType().Name;
                            return (name == "Transform" ? "GameObject" : name) + (component.GetType() == element.objectReferenceValue.GetType() ? " (current)" : "");
                        })
                        .Distinct())
                        .ToArray();
                    var switching = EditorGUILayout.Popup(0, allComponents, GUILayout.Width(60));
                    if (switching > 0)
                    {
                        var components = ((Component)(element.objectReferenceValue)).gameObject
                            .GetComponents<Component>() // GetComponents may contain null values for unloadable MonoBehaviours
                            .Where(component => component != null)
                            // .Where(component => component.GetType() != element.objectReferenceValue.GetType())
                            .Distinct()
                            .ToArray();
                        element.objectReferenceValue = components[switching - 1];
                    }
                }

                if (GUILayout.Button(HVREditorHelpers.CrossSymbol, GUILayout.Width(25)))
                {
                    whichArrayProperty.GetArrayElementAtIndex(i).objectReferenceValue = null;
                    whichArrayProperty.DeleteArrayElementAtIndex(i);
                    return; // Reason why the return is here: Please check the comment in VixenLayoutChangeProperties, look for a DeleteArrayElementAtIndex invocation.
                }

                EditorGUILayout.EndHorizontal();
            }

            if (!limitToOne || whichArrayProperty.arraySize == 0)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(HVREditorHelpers.PlusSymbol, GUILayout.Width(15));
                var newAddition = EditorGUILayout.ObjectField(GUIContent.none, null, arrayType);
                EditorGUILayout.LabelField(GUIContent.none, GUILayout.Width(25));
                EditorGUILayout.EndHorizontal();
                if (newAddition != null)
                {
                    var newIndex = whichArrayProperty.arraySize;
                    whichArrayProperty.InsertArrayElementAtIndex(newIndex);
                    whichArrayProperty.GetArrayElementAtIndex(newIndex).objectReferenceValue = newAddition;
                }
            }
        }

        private void DisplayComponentBox(Type targetedType, GameObject targetObject,
            SerializedProperty selectedElementSp, HVRVixxySubject mySelectedElement, GameObject rootObject)
        {
            EditorGUILayout.BeginHorizontal();

            EditorGUI.BeginDisabledGroup(true);
            var componentNullable = DisplayComponentObjectField(targetObject, targetedType);
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndHorizontal();

            NEW(targetedType, selectedElementSp, mySelectedElement, rootObject, componentNullable);
        }

        private void NEW(Type targetedType, SerializedProperty selectedElementSp, HVRVixxySubject mySelectedElement, GameObject rootObject, Component componentNullable)
        {
            var isRenderer = typeof(Renderer).IsAssignableFrom(targetedType);
            var isSkinnedMeshRenderer = targetedType == typeof(SkinnedMeshRenderer);

            EditorGUILayout.BeginHorizontal();
            _typeToWhichOpened[targetedType] = GUILayout.Toolbar(_typeToWhichOpened[targetedType], new[]
            {
                HVRVixxyLocalizationPhrase.JustPropertiesLabel,
                isRenderer ? HVRVixxyLocalizationPhrase.MaterialLabel : null,
                isSkinnedMeshRenderer ? HVRVixxyLocalizationPhrase.BlendshapesLabel : null,
            }.Where(s => s != null).ToArray());

            EditorGUI.BeginDisabledGroup(_typeToWhichOpened[targetedType] == -1);
            if (GUILayout.Button("_", GUILayout.Width(25)))
            {
                _typeToWhichOpened[targetedType] = -1;
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            var showProperties = _typeToWhichOpened[targetedType] == 0;
            var showMaterials = isRenderer && _typeToWhichOpened[targetedType] == 1;
            var showBlendshapes = isSkinnedMeshRenderer && _typeToWhichOpened[targetedType] == 2;

            if (_typeToWhichOpened[targetedType] != -1)
            {
                GUI.SetNextControlName("search");
                _search = EditorGUILayout.TextField(HVRVixxyLocalizationPhrase.SearchLabel, _search);
                if (_focusNext)
                {
                    _focusNext = false;
                    EditorGUI.FocusTextInControl("search");
                }
            }

            var hasSearch = !string.IsNullOrEmpty(_search) && (_search.Length >= 3 || (_search.Length == 2 && HasAnyNonLetterNonSpace.IsMatch(_search)) || _search.StartsWith(" "));

            if (showBlendshapes && _blendshapes != null)
            {
                foreach (var blendshape in _blendshapes)
                {
                    if (hasSearch && !IsSearchMatch(blendshape)) continue;

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.TextField(blendshape);
                    if (GUILayout.Button("Add", GUILayout.Width(60)))
                    {
                        var propertiesSp = selectedElementSp.FindPropertyRelative(nameof(HVRVixxySubject.properties));

                        var indexToPutData = propertiesSp.arraySize;
                        propertiesSp.arraySize = indexToPutData + 1;
                        propertiesSp.GetArrayElementAtIndex(indexToPutData).managedReferenceValue = new HVRVixxyPropertyFloat
                        {
                            fullClassName = targetedType.FullName,
                            variant = HVRVixxyPropertyVariant.BlendShape,
                            propertyName = blendshape,
                        };
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }

            if (showMaterials && _materialProperties != null)
            {
                foreach (var mptypeToProperties in _materialProperties)
                {
                    if (mptypeToProperties.Value.Count <= 0) continue;

                    EditorGUILayout.LabelField(mptypeToProperties.Key.ToString(), EditorStyles.boldLabel);

                    foreach (var materialProperty in mptypeToProperties.Value)
                    {
                        if (hasSearch && !IsSearchMatch(materialProperty)) continue;

                        var mptype = mptypeToProperties.Key;

                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.TextField(materialProperty);
                        if (GUILayout.Button(mptype == MaterialPropertyType.Vector ? "Vector4" : "Add", GUILayout.Width(55)))
                        {
                            var propertiesSp = selectedElementSp.FindPropertyRelative(nameof(HVRVixxySubject.properties));

                            var indexToPutData = propertiesSp.arraySize;
                            propertiesSp.arraySize = indexToPutData + 1;
                            propertiesSp.GetArrayElementAtIndex(indexToPutData).managedReferenceValue = GenerateProperty(mptype, targetedType, materialProperty);
                        }
                        if (mptype == MaterialPropertyType.Vector
                            && GUILayout.Button("HDR", GUILayout.Width(55)))
                        {
                            var propertiesSp = selectedElementSp.FindPropertyRelative(nameof(HVRVixxySubject.properties));

                            var indexToPutData = propertiesSp.arraySize;
                            propertiesSp.arraySize = indexToPutData + 1;
                            propertiesSp.GetArrayElementAtIndex(indexToPutData).managedReferenceValue = new HVRVixxyPropertyColor32
                            {
                                fullClassName = targetedType.FullName,
                                variant = HVRVixxyPropertyVariant.MaterialProperty,
                                propertyName = materialProperty,
                            };
                        }
                        if (mptype == MaterialPropertyType.Vector
                            && GUILayout.Button("Color", GUILayout.Width(55)))
                        {
                            var propertiesSp = selectedElementSp.FindPropertyRelative(nameof(HVRVixxySubject.properties));

                            var indexToPutData = propertiesSp.arraySize;
                            propertiesSp.arraySize = indexToPutData + 1;
                            propertiesSp.GetArrayElementAtIndex(indexToPutData).managedReferenceValue = new HVRVixxyPropertyColor
                            {
                                fullClassName = targetedType.FullName,
                                variant = HVRVixxyPropertyVariant.MaterialProperty,
                                propertyName = materialProperty,
                            };
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                }
            }
        }

        private static HVRVixxyPropertyBase GenerateProperty(MaterialPropertyType mptype, Type targetedType, string materialProperty)
        {
            switch (mptype)
            {
                case MaterialPropertyType.Float:
                    return new HVRVixxyPropertyFloat
                    {
                        fullClassName = targetedType.FullName,
                        variant = HVRVixxyPropertyVariant.MaterialProperty,
                        propertyName = materialProperty,
                    };
                case MaterialPropertyType.Int:
                    return new HVRVixxyPropertyInt
                    {
                        fullClassName = targetedType.FullName,
                        variant = HVRVixxyPropertyVariant.MaterialProperty,
                        propertyName = materialProperty,
                    };
                case MaterialPropertyType.Vector:
                    return new HVRVixxyPropertyVector4
                    {
                        fullClassName = targetedType.FullName,
                        variant = HVRVixxyPropertyVariant.MaterialProperty,
                        propertyName = materialProperty,
                    };
                case MaterialPropertyType.Texture:
                    return new HVRVixxyPropertyTexture
                    {
                        fullClassName = targetedType.FullName,
                        variant = HVRVixxyPropertyVariant.MaterialProperty,
                        propertyName = materialProperty,
                    };
                case MaterialPropertyType.Matrix:
                case MaterialPropertyType.ConstantBuffer:
                case MaterialPropertyType.ComputeBuffer:
                default:
                    throw new ArgumentOutOfRangeException(nameof(mptype), mptype, null);
            }
        }

        private static Component DisplayComponentObjectField(GameObject targetObject, Type targetedType)
        {
            Component componentNullable = null;
            if (typeof(Component).IsAssignableFrom(targetedType))
            {
                var foundComp = targetObject.GetComponent(targetedType);
                componentNullable = foundComp;
                EditorGUILayout.ObjectField(foundComp, targetedType);
            }
            else if (targetedType == typeof(GameObject))
            {
                EditorGUILayout.ObjectField(targetObject, typeof(GameObject));
            }
            else
            {
                EditorGUILayout.TextField(targetedType.Name);
            }

            return componentNullable;
        }

        private bool IsSearchMatch(string isPropertyNameMatch)
        {
            var propertyName = isPropertyNameMatch.ToLowerInvariant();
            return (_search ?? "").ToLowerInvariant().Split(' ').All(needle => propertyName.Contains(needle));
        }

        private void SubjectsListElement(Rect rect, int index, bool isactive, bool isfocused)
        {
            var element = subjectsReorderableList.serializedProperty.GetArrayElementAtIndex(index);

            // EditorGUI.PropertyField(
            // new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight),
            // element.FindPropertyRelative(nameof(HVRVixxySubject.targets))
            // );
            var subject = my.subjects[index];
            EditorGUI.LabelField(
                new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight),
                ComputeLabel(index, subject)
            );
        }

        private static string ComputeLabel(int index, HVRVixxySubject subject)
        {
            // FIXME: This is an expensive way to avoid showing ghost classes
            var mainTargetFullClassNames = subject.targets != null && subject.targets.Length > 0 && subject.targets[0] != null ? subject.targets[0].GetComponents<Component>()
                // GetComponents may contain null values for unloadable MonoBehaviours
                .Where(component => component != null)
                .Select(component => component.GetType().FullName)
                .Distinct()
                .ToArray() : Array.Empty<string>();

            var classNames = subject.properties
                .Select(property => property.fullClassName)
                .Distinct()
                .Where(fullClassName => mainTargetFullClassNames.Any(existingFullClassNamesInTarget => fullClassName == existingFullClassNamesInTarget))
                .Select(s => s.LastIndexOf('.') != -1 ? s.Substring(s.LastIndexOf('.') + 1) : s)
                .ToArray();

            if (subject.selection == HVRVixxySelection.Normal)
            {
                var targetNames = subject.targets.Where(o => o != null).Select(o => o.name).ToArray();
                return
                    $"#{index + 1} {(targetNames.Length > 1 ? $"[{targetNames.Length} objects] " : "")}{string.Join(", ", targetNames)} ({string.Join(", ", classNames)})";
            }
            else
            {
                var label = subject.selection == HVRVixxySelection.RecursiveSearch ? HVRVixxyLocalizationPhrase.RecursiveSearchLabel : HVRVixxyLocalizationPhrase.EverythingLabel;
                return $"#{index + 1} {label}: {(classNames.Length > 1 ? $"[{classNames.Length} types] " : "")}{string.Join(", ", classNames)}";
            }
        }
    }
}
