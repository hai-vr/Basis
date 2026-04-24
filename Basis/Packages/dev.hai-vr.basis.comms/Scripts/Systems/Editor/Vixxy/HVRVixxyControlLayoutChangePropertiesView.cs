using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HVR.Vixxy.Editor
{
    internal class HVRVixxyLayoutChangePropertiesView
    {
        private const int MaxSearchQueryLength = 100;
        private const string MsgNetworkingUsesHighFrequency = "The option UpdatedExtremelyFrequently should only be used if you are using special external software to update this value all the time, multiple times per second, for example using a face tracking device.\nIt should almost never be used in other circumstances.";
        private static readonly Regex HasAnyNonLetterNonSpace = new Regex(@"[^A-Za-z\s]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly string[] CoordsSuffixes = {".x", ".y", ".z", ".w", ".r", ".g", ".b", ".a"};

        private readonly HVRVixxyControl my;
        private readonly SerializedObject serializedObject;
        private readonly ReorderableList subjectsReorderableList;

        private IGrouping<Type, EditorCurveBinding>[] _typeToBindings;
        private Object _previousRootObject;
        private readonly Dictionary<Type, int> _typeToWhichOpened = new();
        private string _addBlendshape = "";

        private string _search;
        private bool _focusNext;

        internal HVRVixxyLayoutChangePropertiesView(HVRVixxyControlEditor editor)
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
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRVixxyControl.address)));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRVixxyControl.networked)));
            if (my.networked)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRVixxyControl.advancedNetworking)));
                if (my.advancedNetworking == HVRVixxyNetworkingType.UpdatedExtremelyFrequently)
                {
                    EditorGUILayout.HelpBox(MsgNetworkingUsesHighFrequency, MessageType.Warning);
                }
            }
            EditorGUILayout.Separator();

            EditorGUILayout.LabelField(HVRVixxyLocalizationPhrase.MultiChoiceLabel, EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRVixxyControl.hasThreeOrMoreChoices)));
            if (my.hasThreeOrMoreChoices)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRVixxyControl.numberOfChoices)));
            }
            EditorGUILayout.Separator();

            if (false) {
                // EditorGUILayout.LabelField(HVRVixenLocalizationPhrase.InterpolationLabel, EditorStyles.boldLabel);
                // EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRVixxyControl.interpolationDurationSeconds)));
                // EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRVixxyControl.interpolationCurve)));
            }

            EditorGUILayout.Separator();

            return false;
        }

        public bool LayoutChangeProperties()
        {
            EditorGUILayout.Separator();
            LayoutChangePropertiesPart();
            EditorGUILayout.Separator();

            return false;
        }

        private void LayoutChangePropertiesPart()
        {
            subjectsReorderableList.DoLayoutList();
            var selectedIndex = subjectsReorderableList.index;
            if (selectedIndex != -1 && selectedIndex < subjectsReorderableList.count)
            {
                EditorGUILayout.BeginVertical(HVRUiHelpers.GroupBoxStyle);
                var selectedElementSp = subjectsReorderableList.serializedProperty.GetArrayElementAtIndex(selectedIndex);
                var mySelectedElement = my.subjects[selectedIndex];

                EditorGUILayout.LabelField($"{HVRVixxyLocalizationPhrase.ObjectGroupLabel} #{selectedIndex + 1}", EditorStyles.boldLabel);

                EditorGUILayout.PropertyField(selectedElementSp.FindPropertyRelative(nameof(HVRVixxySubject.selection)));
                if (mySelectedElement.selection == HVRVixxySelection.Normal)
                {
                    EditorGUILayout.LabelField(HVRVixxyLocalizationPhrase.ChangeTheseObjectsLabel);
                    CreateArrayAddition(selectedElementSp.FindPropertyRelative(nameof(HVRVixxySubject.targets)), typeof(GameObject));
                }
                else if (mySelectedElement.selection == HVRVixxySelection.RecursiveSearch)
                {
                    EditorGUILayout.LabelField(HVRVixxyLocalizationPhrase.ChangeTheseObjectsAndTheirChildrenLabel);
                    CreateArrayAddition(selectedElementSp.FindPropertyRelative(nameof(HVRVixxySubject.childrenOf)),
                        typeof(GameObject));

                    EditorGUILayout.LabelField(HVRVixxyLocalizationPhrase.DoNotChangeTheseObjectsLabel);
                    CreateArrayAddition(selectedElementSp.FindPropertyRelative(nameof(HVRVixxySubject.exceptions)),
                        typeof(GameObject));
                }
                else if (mySelectedElement.selection == HVRVixxySelection.Everything)
                {
                    EditorGUILayout.HelpBox(HVRVixxyLocalizationPhrase.MsgEverthingInContext, MessageType.Info);

                    EditorGUILayout.LabelField(HVRVixxyLocalizationPhrase.DoNotChangeTheseObjectsLabel);
                    CreateArrayAddition(selectedElementSp.FindPropertyRelative(nameof(HVRVixxySubject.exceptions)),
                        typeof(GameObject));
                }

                EditorGUILayout.Separator();

                EditorGUILayout.LabelField(HVRVixxyLocalizationPhrase.PropertiesLabel, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(HVRVixxyLocalizationPhrase.SampleFromLabel);
                EditorGUI.BeginDisabledGroup(mySelectedElement.selection == HVRVixxySelection.Normal);
                CreateArrayAddition(selectedElementSp.FindPropertyRelative(nameof(HVRVixxySubject.targets)), typeof(GameObject), true);
                EditorGUI.EndDisabledGroup();

                var validTargets = mySelectedElement.targets
                    .Where(o => o != null)
                    .Distinct()
                    .ToArray();

                if (validTargets.Length > 0)
                {
                    foreach (var targetObject in validTargets)
                    {
                        var rootObject = targetObject;

                        if (_typeToBindings == null || _previousRootObject != rootObject)
                        {
                            _previousRootObject = rootObject;
                            _typeToBindings =
                                AnimationUtility.GetAnimatableBindings(targetObject, rootObject)
                                    .GroupBy(binding => binding.type)
                                    .ToArray();
                            foreach (var typeToBinding in _typeToBindings)
                            {
                                _typeToWhichOpened.TryAdd(typeToBinding.Key, -1);
                            }
                        }

                        foreach (var typeToBinding in _typeToBindings
                                     .Where(bindings => HVRVixxyPermitted.IsPermitted(bindings.Key.FullName))
                                     .Where(bindings => bindings.Key != typeof(Transform)))
                        {
                            DisplayComponentBox(typeToBinding, targetObject, selectedElementSp, mySelectedElement, rootObject);
                        }

                        // Put the transform property editor at the bottom of the list. Caring about modifying the transform is way more rare than the others
                        foreach (var typeToBinding in _typeToBindings.Where(bindings => bindings.Key == typeof(Transform)))
                        {
                            DisplayComponentBox(typeToBinding, targetObject, selectedElementSp, mySelectedElement, rootObject);
                        }

                        var nonPermitted = _typeToBindings
                            .Where(bindings => !HVRVixxyPermitted.IsPermitted(bindings.Key.FullName))
                            .Where(bindings => bindings.Key != typeof(Transform))
                            .ToList();
                        if (nonPermitted.Any())
                        {
                            GUILayout.BeginVertical(HVRUiHelpers.GroupBoxStyle);
                            EditorGUILayout.HelpBox(HVRVixxyLocalizationPhrase.MsgMissingFromComponentTypes, MessageType.Warning);
                            EditorGUI.BeginDisabledGroup(true);
                            foreach (var typeToBinding in nonPermitted)
                            {
                                DisplayComponentObjectField(targetObject, typeToBinding.Key);
                                // DisplayComponentBox(typeToBinding, targetObject, selectedElementSp, mySelectedElement, rootObject);
                            }
                            EditorGUI.EndDisabledGroup();
                            GUILayout.EndVertical();
                        }

                        break;
                    }
                }

                EditorGUILayout.EndVertical();
            }
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

                if (GUILayout.Button(HVRUiHelpers.CrossSymbol, GUILayout.Width(25)))
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
                EditorGUILayout.LabelField(HVRUiHelpers.PlusSymbol, GUILayout.Width(15));
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

        private void DisplayComponentBox(IGrouping<Type, EditorCurveBinding> typeToBinding, GameObject targetObject,
            SerializedProperty selectedElementSp, HVRVixxySubject mySelectedElement, GameObject rootObject)
        {
            var targetedType = typeToBinding.Key;

            GUILayout.BeginVertical(HVRUiHelpers.GroupBoxStyle);
            EditorGUILayout.BeginHorizontal();

            EditorGUI.BeginDisabledGroup(true);
            var componentNullable = DisplayComponentObjectField(targetObject, targetedType);
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndHorizontal();

            var countMaterials = typeToBinding.Count(binding => binding.propertyName.StartsWith("material."));
            var countBlendShapes = typeToBinding.Count(binding => binding.propertyName.StartsWith("blendShape."));

            var label = countMaterials > 0 || countBlendShapes > 0 ? HVRVixxyLocalizationPhrase.OtherPropertiesLabel : HVRVixxyLocalizationPhrase.JustPropertiesLabel;

            var showMaterials = countMaterials > 0 && _typeToWhichOpened[typeToBinding.Key] == 0;
            var showBlendshapes = countBlendShapes > 0 && (countMaterials > 0 && _typeToWhichOpened[typeToBinding.Key] == 1 || countMaterials == 0 && _typeToWhichOpened[typeToBinding.Key] == 0);
            var showOther = countMaterials > 0 && countBlendShapes > 0 && _typeToWhichOpened[typeToBinding.Key] == 2 ||
                            countMaterials == 0 && countBlendShapes > 0 && _typeToWhichOpened[typeToBinding.Key] == 1 ||
                            countMaterials > 0 && countBlendShapes == 0 && _typeToWhichOpened[typeToBinding.Key] == 1 ||
                            countMaterials == 0 && countBlendShapes == 0 && _typeToWhichOpened[typeToBinding.Key] == 0;


            EditorGUILayout.BeginHorizontal();
            _typeToWhichOpened[typeToBinding.Key] = GUILayout.Toolbar(_typeToWhichOpened[typeToBinding.Key], new[]
            {
                countMaterials > 0 ? $"{HVRVixxyLocalizationPhrase.MaterialLabel}" : null,
                countBlendShapes > 0 ? $"{HVRVixxyLocalizationPhrase.BlendshapesLabel}" : null,
                $"{label}"
            }.Where(s => s != null).ToArray());
            EditorGUI.BeginDisabledGroup(_typeToWhichOpened[typeToBinding.Key] == -1);
            if (GUILayout.Button("_", GUILayout.Width(25)))
            {
                _typeToWhichOpened[typeToBinding.Key] = -1;
            }

            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            if (_typeToWhichOpened[typeToBinding.Key] != -1)
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
            if (hasSearch && _search.Length > MaxSearchQueryLength)
            {
                // Try to prevent the editor from hanging up if the user mistakenly pastes a page long of unrelated content (it happened)
                _search = _search.Substring(0, MaxSearchQueryLength);
            }

            if (hasSearch && (showMaterials || showBlendshapes || showOther))
            {
                EditorGUILayout.LabelField(HVRVixxyLocalizationPhrase.ResultsAreFilteredBySearchLabel);
            }

            if (showMaterials)
            {
                DisplayPropertyViewer(componentNullable, mySelectedElement, selectedElementSp, typeToBinding, hasSearch, rootObject,
                    binding => binding.propertyName.StartsWith("material.") || binding.propertyName.StartsWith("m_Materials."));
            }

            if (showBlendshapes)
            {
                DisplayPropertyViewer(componentNullable, mySelectedElement, selectedElementSp, typeToBinding, hasSearch, rootObject,
                    binding => binding.propertyName.StartsWith("blendShape."));
            }

            if (
                showOther
            )
            {
                DisplayPropertyViewer(componentNullable, mySelectedElement, selectedElementSp, typeToBinding, hasSearch, rootObject,
                    binding => !binding.propertyName.StartsWith("material.") &&
                               !binding.propertyName.StartsWith("m_Materials.") &&
                               !binding.propertyName.StartsWith("blendShape."));
            }

            // VIXXY TODO
            // DisplayAcquiredProperties(selectedElementSp, targetedType.FullName, rootObject, componentNullable, typeToBinding.First().path);

            GUILayout.EndVertical();
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

        private void DisplayPropertyViewer(Component componentNullable, HVRVixxySubject mySelectedElement,
            SerializedProperty selectedElementSp,
            IGrouping<Type, EditorCurveBinding> typeToBinding,
            bool hasSearch, GameObject rootObject, Func<EditorCurveBinding, bool> IsDirectMatch)
        {
            var allDirectMatches = typeToBinding.Where(IsDirectMatch).Where(IsMatch).ToArray();
            if (!hasSearch && allDirectMatches.Length > 100)
            {
                EditorGUILayout.HelpBox(HVRVixxyLocalizationPhrase.MsgTooManyResults, MessageType.Warning);
                var results = string.Join("\n", allDirectMatches.Select(binding => binding.propertyName));
                EditorGUILayout.HelpBox(results, MessageType.None);

                return;
            }

            foreach (var binding in allDirectMatches)
            {
                if (!hasSearch || IsMatch(binding))
                {
                    var property = binding.propertyName;
                    // TODO VIXXY

                    if (!CoordsSuffixes.Any(suffix => property.EndsWith(suffix)))
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.TextField(property);
                        EditorGUI.BeginDisabledGroup(mySelectedElement.properties.Any(prop =>
                        {
                            return prop.fullClassName == binding.type.FullName && prop.propertyName == property;
                        }));
                        if (GUILayout.Button(HVRVixxyLocalizationPhrase.AddLabel, GUILayout.Width(50)))
                        {
                            var isObjectReference = AnimationUtility.GetObjectReferenceValue(rootObject, binding, out var _);

                            // The last argument is zero, because normally, vectors are added from the color property viewer, not here in this single-value property viewer
                            // TODO VIXXY
                            // Insert(componentNullable, selectedElementSp, property, isObjectReference ? VixenValueType.Material : VixenValueType.Float, rootObject, binding, Vector4.zero);
                        }
                        EditorGUI.EndDisabledGroup();

                        EditorGUILayout.EndHorizontal();
                    }
                    else
                    {
                        var isXYZ = property.EndsWith(".x");
                        var isRGB = property.EndsWith(".r");

                        if (isXYZ || isRGB)
                        {
                            EditorGUILayout.BeginHorizontal();
                            EditorGUILayout.TextField(property.Substring(0, property.Length - ".x".Length));
                            EditorGUILayout.EndHorizontal();
                        }
                    }
                }
            }
        }

        private bool IsMatch(EditorCurveBinding editorCurveBinding)
        {
            var propertyName = editorCurveBinding.propertyName.ToLowerInvariant();
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
