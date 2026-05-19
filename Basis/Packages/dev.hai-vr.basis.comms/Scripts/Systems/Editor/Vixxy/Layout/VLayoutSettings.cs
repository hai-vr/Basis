using System.Collections.Generic;
using System.Linq;
using HVR.Basis.Comms;
using HVR.Basis.Comms.Editor;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace HVR.Vixxy.Editor
{
    internal class VLayoutSettings
    {
        private static readonly string[] Visemes = { "sil", "PP", "FF", "TH", "DD", "kk", "CH", "SS", "nn", "RR", "aa", "E", "ih", "oh", "ou", };
        private readonly HVRVixxyControl my;
        private readonly SerializedObject serializedObject;

        private readonly HVRVixxyControlEditor _editor;
        private readonly List<HVRVixxyMenuItem> _outsideMenus = new();

        internal VLayoutSettings(HVRVixxyControlEditor editor)
        {
            _editor = editor;
            my = (HVRVixxyControl)editor.target;
            serializedObject = editor.serializedObject;

            if (my.GetComponent<HVRVixxyMenuItem>() == null)
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
            if (!_editor.IsSystemAddress())
            {
                var menuNullable = my.GetComponent<HVRVixxyMenuItem>();
                if (menuNullable != null || _outsideMenus.Count != 0)
                {
                    EditorGUILayout.LabelField("This control is activated by a menu.", EditorStyles.boldLabel);
                }
                EditorGUI.BeginDisabledGroup(true);
                foreach (var outsideMenu in _outsideMenus)
                {
                    EditorGUILayout.ObjectField(outsideMenu, typeof(HVRVixxyMenuItem), true);
                }

                if (menuNullable != null)
                {
                    EditorGUILayout.ObjectField(menuNullable, typeof(HVRVixxyMenuItem), true);
                }
                EditorGUI.EndDisabledGroup();

                var isControlNotDrivenByAnything = _outsideMenus.Count == 0 && menuNullable == null && (!my.address.TryResolvePath(out var actualAddress) || HVRAddress.IsSystemAddressName(actualAddress));
                if (isControlNotDrivenByAnything)
                {
                    if (GUILayout.Button(HVRVixxyLocalizationPhrase.CreateMenuOnThisControlLabel))
                    {
                        var comp = Undo.AddComponent<HVRVixxyMenuItem>(my.gameObject);
                        ComponentUtility.MoveComponentUp(comp);
                    }
                    if (GUILayout.Button(HVRVixxyLocalizationPhrase.CreateMenuInASeparateGameObjectLabel))
                    {
                        var go = new GameObject($"{my.gameObject.name} Menu");
                        Undo.RegisterCreatedObjectUndo(go, HVRVixxyLocalizationPhrase.CreateMenuInASeparateGameObjectLabel);
                        go.transform.SetParent(my.transform);
                        go.transform.localPosition = Vector3.zero;
                        go.transform.localRotation = Quaternion.identity;
                        go.transform.localScale = Vector3.one;

                        var comp = Undo.AddComponent<HVRVixxyMenuItem>(go);
                        comp.control = my;

                        _outsideMenus.Add(comp);
                    }
                    LayoutSystemAddressSelector();
                }
            }
            else
            {
                EditorGUILayout.LabelField("This control is activated by a special input.", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(my.address.TryResolvePath(out var actualAddress) ? actualAddress : "???");
                LayoutSystemAddressSelector();
            }
            EditorGUILayout.Separator();

            return false;
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

        private void LayoutSystemAddressSelector()
        {
            var bindAddress = my.address.TryResolvePath(out var actualAddress) ? actualAddress : "";

            EditorGUILayout.LabelField("Viseme", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            for (var index = 0; index < Visemes.Length; index++)
            {
                if (index % 5 == 0 && index != 0)
                {
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal();
                }

                var viseme = Visemes[index];
                LayoutButtonForAddress(viseme, HVRAddress.System.User.Viseme.VisemeAddressPrefix + viseme, bindAddress);
            }
            EditorGUILayout.EndHorizontal();
            LayoutButtonForAddress("Voice Gain", HVRAddress.System.User.VoiceGain.address, bindAddress);
        }

        private void LayoutButtonForAddress(string viseme, string address, string bindAddress)
        {
            if (HaiEFCommon.ColoredBackground(bindAddress == address, HVRVixxyControlEditor.FilledColor, () => GUILayout.Button(viseme)))
            {
                var prop = serializedObject.FindProperty(nameof(HVRVixxyControl.address));
                prop.FindPropertyRelative(nameof(HVRAddressSelector.path)).stringValue = address;
                prop.FindPropertyRelative(nameof(HVRAddressSelector.asset)).objectReferenceValue = null;
            }
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
}
