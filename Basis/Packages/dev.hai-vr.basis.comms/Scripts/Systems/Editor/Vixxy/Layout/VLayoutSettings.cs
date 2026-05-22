using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HVR.Basis.Comms;
using HVR.Basis.Comms.Editor;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditorInternal;
using UnityEngine;

namespace HVR.Vixxy.Editor
{
    internal class VLayoutSettings
    {
        private readonly HVRVixxyControl my;
        private readonly SerializedObject serializedObject;

        private readonly HVRVixxyControlEditor _editor;
        private readonly List<HVRVixxyMenuItem> _outsideMenus = new();

        internal VLayoutSettings(HVRVixxyControlEditor editor)
        {
            _editor = editor;
            my = (HVRVixxyControl)editor.target;
            serializedObject = editor.serializedObject;

            var foundMenuItem = my.TryGetComponent<HVRVixxyMenuItem>(out _);
            if (!foundMenuItem)
            {
                var avatar = HVRCommsUtil.GetAvatar(my);
                if (avatar != null)
                {
                    _outsideMenus = avatar.GetComponentsInChildren<HVRVixxyMenuItem>(true)
                        .Where(menu => menu.control == my)
                        .ToList();
                }
            }
        }

        public bool LayoutSettings()
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRVixxyControl.transition)));
            if (my.transition == HVRVixxyTransitionMode.Simplified)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRVixxyControl.transitionDuration)), new GUIContent(HVRVixxyLocalizationPhrase.TransitionDurationLabel));
            }
            EditorGUILayout.Separator();

            if (!_editor.IsSystemAddress())
            {
                var foundMenu = my.TryGetComponent<HVRVixxyMenuItem>(out var menu);
                var isMenuDriven = foundMenu || _outsideMenus.Count != 0;
                if (isMenuDriven)
                {
                    EditorGUILayout.LabelField("This control is activated by a menu.", EditorStyles.boldLabel);
                }
                EditorGUI.BeginDisabledGroup(true);
                foreach (var outsideMenu in _outsideMenus)
                {
                    EditorGUILayout.ObjectField(outsideMenu, typeof(HVRVixxyMenuItem), true);
                }

                if (foundMenu)
                {
                    EditorGUILayout.ObjectField(menu, typeof(HVRVixxyMenuItem), true);
                }
                EditorGUI.EndDisabledGroup();

                var isControlNotDrivenByAnything = _outsideMenus.Count == 0 && !foundMenu && (!my.address.TryResolvePath(out var actualAddress) || HVRAddress.IsSystemAddressName(actualAddress));
                if (isControlNotDrivenByAnything)
                {
                    EditorGUILayout.LabelField("What activates this control?", EditorStyles.boldLabel);
                    LayoutSystemAddressSelector(null, "Select...");
                    EditorGUILayout.HelpBox("This control is not activated by anything. Make sure you select one using the button above.", MessageType.Warning);
                }
                else if (!isMenuDriven)
                {
                    EditorGUILayout.LabelField("This control is activated by an input.", EditorStyles.boldLabel);
                    LayoutExistingAddress();
                }
            }
            else
            {
                EditorGUILayout.LabelField("This control is activated by a special input.", EditorStyles.boldLabel);
                LayoutExistingAddress();
            }
            EditorGUILayout.Separator();

            return false;
        }

        private void LayoutExistingAddress()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(my.address.TryResolvePath(out var actualAddress) ? actualAddress : "???");
            LayoutSystemAddressSelector(GUILayout.Width(100), "Select...");
            if (GUILayout.Button(HVR_EditorHelpers.CrossSymbol, GUILayout.Width(HVR_EditorHelpers.DeleteButtonWidth)))
            {
                var prop = serializedObject.FindProperty(nameof(HVRVixxyControl.address));
                prop.FindPropertyRelative(nameof(HVRAddressSelector.path)).stringValue = "";
                prop.FindPropertyRelative(nameof(HVRAddressSelector.asset)).objectReferenceValue = null;
            }
            EditorGUILayout.EndHorizontal();
        }

        internal bool LayoutChoices()
        {
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
                var valueSp = choiceSp.FindPropertyRelative(nameof(HVRVixxyChoiceControl.value));
                EditorGUILayout.PropertyField(valueSp, GUIContent.none, GUILayout.Width(30));
                var choiceValue = valueSp.floatValue;
                if (HaiEFCommon.ColoredBackground(Mathf.Approximately(my.defaultValue, choiceValue), HVRVixxyControlEditor.FilledColor,
                        () => GUILayout.Button(HVRVixxyLocalizationPhrase.DefaultLabel, GUILayout.Width(60))))
                {
                    var defaultValueSp = serializedObject.FindProperty(nameof(HVRVixxyControl.defaultValue));
                    defaultValueSp.floatValue = choiceValue;
                }
                EditorGUI.BeginDisabledGroup(choiceIndex == 0);
                if (GUILayout.Button(HVR_EditorHelpers.ArrowUpSymbol, GUILayout.Width(HVR_EditorHelpers.SwapElementWidth)))
                {
                    _editor.MoveChoiceUp(choiceIndex);
                }
                EditorGUI.EndDisabledGroup();
                EditorGUI.BeginDisabledGroup(choiceIndex == choicesSp.arraySize - 1);
                if (GUILayout.Button(HVR_EditorHelpers.ArrowDownSymbol, GUILayout.Width(HVR_EditorHelpers.SwapElementWidth)))
                {
                    _editor.MoveChoiceDown(choiceIndex);
                }
                EditorGUI.EndDisabledGroup();

                EditorGUI.BeginDisabledGroup(!my.HasThreeOrMoreChoices);
                if (GUILayout.Button(HVR_EditorHelpers.CrossSymbol, GUILayout.Width(HVR_EditorHelpers.DeleteButtonWidth)))
                {
                    _editor.RemoveChoice(choiceIndex);
                    return true;
                }
                EditorGUI.EndDisabledGroup();

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("", GUILayout.Width(30));
            if (GUILayout.Button($"{HVR_EditorHelpers.PlusSymbol} {HVRVixxyLocalizationPhrase.AddChoiceLabel}"))
            {
                _editor.AddChoice();
                return true;
            }
            EditorGUILayout.LabelField("", GUILayout.Width(20));
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Separator();
            return false;
        }

        private void LayoutSystemAddressSelector(GUILayoutOption layoutOption, string label)
        {
            var buttonRect = layoutOption != null ? EditorGUILayout.GetControlRect(layoutOption) : EditorGUILayout.GetControlRect();
            if (GUI.Button(buttonRect, label))
            {
                VAddressSelection.Show(buttonRect, HVRCommsUtil.GetAvatar(my), selected =>
                {
                    if (selected == VAddressSelection.MakeMenu)
                    {
                        var comp = Undo.AddComponent<HVRVixxyMenuItem>(my.gameObject);
                        ComponentUtility.MoveComponentUp(comp);
                    }
                    else if (selected == VAddressSelection.MakeMenuNewGameObject)
                    {
                        var go = new GameObject($"{my.gameObject.name} Menu");
                        Undo.RegisterCreatedObjectUndo(go, HVRVixxyLocalizationPhrase.CreateMenuLabel);
                        go.transform.SetParent(my.transform);
                        go.transform.localPosition = Vector3.zero;
                        go.transform.localRotation = Quaternion.identity;
                        go.transform.localScale = Vector3.one;

                        var comp = Undo.AddComponent<HVRVixxyMenuItem>(go);
                        comp.control = my;

                        _outsideMenus.Add(comp);
                        EditorApplication.delayCall += () =>
                        {
                            EditorGUIUtility.PingObject(comp.gameObject);
                        };
                    }
                    else
                    {
                        var prop = serializedObject.FindProperty(nameof(HVRVixxyControl.address));
                        prop.FindPropertyRelative(nameof(HVRAddressSelector.path)).stringValue = selected;
                        prop.FindPropertyRelative(nameof(HVRAddressSelector.asset)).objectReferenceValue = null;
                        serializedObject.ApplyModifiedProperties();
                        _editor.Repaint();
                    }
                });
            }

            var bindAddress = my.address.TryResolvePath(out var actualAddress) ? actualAddress : "";
        }

        public bool LayoutAdvancedSettings()
        {
            EditorGUILayout.HelpBox("These settings usually do not need to be changed, unless you are specifically instructed to do so.\nThe Address field can be left empty.", MessageType.Warning);
            EditorGUILayout.Separator();

            EditorGUILayout.LabelField(HVRVixxyLocalizationPhrase.ControlLabel, EditorStyles.boldLabel);
            HVRVixxyControlEditor.LayoutAddressSelector(serializedObject.FindProperty(nameof(HVRVixxyControl.address)));

            if (_editor.IsSystemAddress())
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUI.showMixedValue = true;
                EditorGUILayout.Toggle(new GUIContent(serializedObject.FindProperty(nameof(HVRVixxyControl.networked)).displayName), false);
                EditorGUI.showMixedValue = false;
                EditorGUI.EndDisabledGroup();

                EditorGUILayout.HelpBox("Networking options are irrelevant for this system address.", MessageType.Info);
            }
            else
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRVixxyControl.networked)));
                if (my.networked)
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(HVRVixxyControl.advancedNetworking)));
                    if (my.advancedNetworking == HVRVixxyNetworkingType.UpdatedExtremelyFrequently)
                    {
                        EditorGUILayout.HelpBox(HVRVixxyLocalizationPhrase.MsgNetworkingUsesHighFrequency, MessageType.Warning);
                    }
                }
            }
            EditorGUILayout.Separator();

            return false;
        }
    }

    internal class VAddressSelection : AdvancedDropdown
    {
        public const string MakeMenu = "make.menu";
        public const string MakeMenuNewGameObject = "make.menu_new_gameobject";

        private static readonly string[] Visemes = { "sil", "PP", "FF", "TH", "DD", "kk", "CH", "SS", "nn", "RR", "aa", "E", "ih", "oh", "ou", };
        private static readonly string[] FaceTracking = { "BrowDownLeft", "BrowDownRight", "BrowInnerUp", "BrowInnerUpLeft", "BrowInnerUpRight", "BrowLowererLeft", "BrowLowererRight", "BrowOuterUpLeft", "BrowOuterUpRight", "BrowPinchLeft", "BrowPinchRight", "CheekPuffSuck", "CheekPuffSuckLeft", "CheekPuffSuckRight", "CheekSquintLeft", "CheekSquintRight", "EyeLeftX", "EyeLidLeft", "EyeLidRight", "EyeRightX", "EyeSquintLeft", "EyeSquintRight", "EyeY", "JawClench", "JawMandibleRaise", "JawOpen", "JawX", "JawZ", "LipFunnel", "LipFunnelLowerLeft", "LipFunnelLowerRight", "LipFunnelUpperLeft", "LipFunnelUpperRight", "LipPucker", "LipPuckerLowerLeft", "LipPuckerLowerRight", "LipPuckerUpperLeft", "LipPuckerUpperRight", "LipSuckCornerLeft", "LipSuckCornerRight", "LipSuckLower", "LipSuckLowerLeft", "LipSuckLowerRight", "LipSuckUpper", "LipSuckUpperLeft", "LipSuckUpperRight", "MouthClosed", "MouthCornerPullLeft", "MouthCornerPullRight", "MouthCornerSlantLeft", "MouthCornerSlantRight", "MouthDimpleLeft", "MouthDimpleRight", "MouthFrownLeft", "MouthFrownRight", "MouthLowerDownLeft", "MouthLowerDownRight", "MouthLowerX", "MouthPressLeft", "MouthPressRight", "MouthRaiserLower", "MouthRaiserUpper", "MouthSmileLeft", "MouthSmileRight", "MouthStretchLeft", "MouthStretchRight", "MouthTightenerLeft", "MouthTightenerRight", "MouthUpperDeepenLeft", "MouthUpperDeepenRight", "MouthUpperUpLeft", "MouthUpperUpRight", "MouthUpperX", "NasalConstrictLeft", "NasalConstrictRight", "NasalDilationLeft", "NasalDilationRight", "NeckFlexLeft", "NeckFlexRight", "NoseSneerLeft", "NoseSneerRight", "SoftPalateClose", "ThroatSwallow", "TongueArchY", "TongueOut", "TongueRoll", "TongueShape", "TongueTwistLeft", "TongueTwistRight", "TongueX", "TongueY" };
        private static readonly string[] EyeTracking = { "EyeLeftX", "EyeRightX", "EyeY", "EyeLidLeft", "EyeLidRight", "EyeSquintLeft", "EyeSquintRight" };

        private readonly Component _contextNullable;
        private readonly Action<string> _onSelected;

        public VAddressSelection(AdvancedDropdownState state, Component contextNullable, Action<string> onSelected) : base(state)
        {
            _contextNullable = contextNullable;
            _onSelected = onSelected;
            minimumSize = new Vector2(200, 300);
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            var root = new AdvancedDropdownItem(HVRVixxyLocalizationPhrase.AddressesLabel);

            root.AddChild(NewAdvancedDropdownItem(MakeMenu, HVRVixxyLocalizationPhrase.MenuItemLabel));
            root.AddChild(NewAdvancedDropdownItem(MakeMenuNewGameObject, HVRVixxyLocalizationPhrase.MenuItemNewGameObjectLabel));
            root.AddChild(CreateVisemeDropdown());
            root.AddChild(NewAdvancedDropdownItem(HVRAddress.System.User.VoiceGain.address, HVRVixxyLocalizationPhrase.VoiceGainLabel));
            root.AddChild(CreateFaceTrackingDropdown(HVRVixxyLocalizationPhrase.FaceTrackingLabel, FaceTracking));
            root.AddChild(CreateFaceTrackingDropdown(HVRVixxyLocalizationPhrase.EyeTrackingLabel, EyeTracking));
            if (_contextNullable != null)
            {
                var allApplicableTransforms = HVR_EditorHelpers.CollectAllNonEditorOnlyTransforms(_contextNullable.transform);

                var allMeasurements = allApplicableTransforms
                    .SelectMany(transform => transform.GetComponents<HVRMeasure>())
                    .ToList();

                var addresses = HVR_VixxyUtil.FindAllMeasurementAddresses(allMeasurements);
                if (addresses.Count > 0)
                {
                    addresses.Sort();

                    var parent = new AdvancedDropdownItem(HVRVixxyLocalizationPhrase.Measurements);
                    foreach (var address in addresses)
                    {
                        parent.AddChild(NewAdvancedDropdownItem(address, address));
                    }
                    root.AddChild(parent);
                }
            }

            return root;
        }

        private static AdvancedDropdownItem CreateVisemeDropdown()
        {
            var parent = new AdvancedDropdownItem(HVRVixxyLocalizationPhrase.VisemeLabel);
            foreach (var viseme in Visemes)
            {
                parent.AddChild(NewAdvancedDropdownItem(HVRAddress.System.User.Viseme.VisemeAddressPrefix + viseme, viseme));
            }

            return parent;
        }

        private static AdvancedDropdownItem CreateFaceTrackingDropdown(string label, string[] array)
        {
            var parent = new AdvancedDropdownItem(label);
            foreach (var ft in array)
            {
                parent.AddChild(NewAdvancedDropdownItem($"FT/v2/{ft}", ft));
            }

            return parent;
        }

        private static AdvancedDropdownItem NewAdvancedDropdownItem(string name, string displayName)
        {
            var constructor = typeof(AdvancedDropdownItem).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(string), typeof(string) },
                null);
            return (AdvancedDropdownItem)constructor.Invoke(new object[] { name, displayName });
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            _onSelected?.Invoke(item.name);
        }

        public static void Show(Rect buttonRect, Component contextNullable, Action<string> onSelected)
        {
            var dropdown = new VAddressSelection(new AdvancedDropdownState(), contextNullable, onSelected);
            dropdown.Show(buttonRect);
        }
    }
}
