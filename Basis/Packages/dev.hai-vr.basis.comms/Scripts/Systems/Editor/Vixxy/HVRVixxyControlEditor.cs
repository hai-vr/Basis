using System;
using System.Linq;
using HVR.Basis.Comms;
using HVR.Basis.Comms.Editor;
using UnityEditor;
using UnityEngine;

namespace HVR.Vixxy.Editor
{
    [CustomEditor(typeof(HVRVixxyControl))]
    public class HVRVixxyControlEditor : UnityEditor.Editor
    {
        internal static readonly Color PreviewColor = new Color(0.65f, 1f, 0.56f);
        internal static readonly Color RuntimeColorOK = Color.cyan;
        internal static readonly Color RuntimeColorKO = new Color(1f, 0.72f, 0f);

        internal const float DeleteButtonWidth = 40;

        public static bool _settingsFoldout;
        public static bool _toggleObjectsFoldout;
        public static bool _changePropertiesFoldout;
        public static bool _developerViewFoldout;

        private VMenuItem _menuItem;
        private VLayoutSettings _settings;
        private VLayoutChangeProperties _changeProperties;
        private VLayoutToggleObjects _toggleObjects;
        private VLayoutDeveloperView _developerView;

        private void OnEnable()
        {
            _settings = new VLayoutSettings(this);
            _changeProperties = new VLayoutChangeProperties(this);
            _toggleObjects = new VLayoutToggleObjects(this);
            _developerView = new VLayoutDeveloperView(this);
        }

        public override void OnInspectorGUI()
        {
            var my = (HVRVixxyControl)target;
            HVRAvatarCommsEditor.EnsureAvatarHasPrefab(my.transform);

            var isPlaying = Application.isPlaying;
            if (isPlaying)
            {
                EditorGUILayout.HelpBox(HVRVixxyLocalizationPhrase.MsgCannotEditInPlayMode, MessageType.Warning);
            }

            var anyChanged = false;
            _settingsFoldout = HaiEFCommon.LilFoldout(HVRVixxyLocalizationPhrase.SettingsLabel, "", _settingsFoldout, ref anyChanged);
            if (_settingsFoldout)
            {
                if (_settings.LayoutSettings()) return;
            }
            _toggleObjectsFoldout = HaiEFCommon.LilFoldout(HVRVixxyLocalizationPhrase.ToggleObjectsViewLabel, "", _toggleObjectsFoldout, ref anyChanged);
            if (_toggleObjectsFoldout)
            {
                if (_toggleObjects.LayoutToggleObjects()) return;
            }
            _changePropertiesFoldout = HaiEFCommon.LilFoldout(HVRVixxyLocalizationPhrase.ChangePropertiesViewLabel, "", _changePropertiesFoldout, ref anyChanged);
            if (_changePropertiesFoldout)
            {
                if (_changeProperties.LayoutChangeProperties()) return;
            }
            EditorGUILayout.Separator();

            EditorGUILayout.LabelField(HVRVixxyLocalizationPhrase.AdvancedLabel, EditorStyles.boldLabel);
            _developerViewFoldout = HaiEFCommon.LilFoldout(HVRVixxyLocalizationPhrase.DeveloperViewLabel, "", _developerViewFoldout, ref anyChanged);
            if (_developerViewFoldout)
            {
                if (_developerView.Layout()) return;
            }

            var wasModified = serializedObject.hasModifiedProperties;
            serializedObject.ApplyModifiedProperties();
            if (wasModified && Application.isPlaying)
            {
                my.DebugOnly_ReBakeControl();
            }

            if (_developerViewFoldout)
            {
                DrawDefaultInspector();
            }
        }

        public static void LayoutAddressSelector(SerializedProperty property)
        {
            var assetSp = property.FindPropertyRelative(nameof(HVRAddressSelector.asset));
            var pathSp = property.FindPropertyRelative(nameof(HVRAddressSelector.path));

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Address", GUILayout.Width(100));

            if (assetSp.objectReferenceValue != null)
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.TextField(((HVRAddress)assetSp.objectReferenceValue).AsPath());
                EditorGUI.EndDisabledGroup();
            }
            else
            {
                EditorGUILayout.PropertyField(pathSp, GUIContent.none);
            }

            EditorGUI.BeginDisabledGroup(!string.IsNullOrWhiteSpace(pathSp.stringValue));
            EditorGUILayout.PropertyField(assetSp, GUIContent.none);
            EditorGUI.EndDisabledGroup();

            EditorGUI.BeginDisabledGroup(string.IsNullOrWhiteSpace(pathSp.stringValue) && assetSp.objectReferenceValue == null);
            if (GUILayout.Button(HVRUiHelpers.CrossSymbol, GUILayout.Width(20)))
            {
                assetSp.objectReferenceValue = null;
                pathSp.stringValue = "";
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
        }

        public void AddChoice()
        {
            var my = (HVRVixxyControl)target;
            my.choices = my.choices.Concat(new[] { new HVRVixxyChoiceControl
            {
                value = my.choices.Length
            } }).ToArray();
            var newNumberOfChoices = my.choices.Length;

            foreach (var activation in my.activations)
            {
                var last = activation.choices.Last();
                activation.choices = activation.choices.Concat(new[] { last }).ToArray();
            }
            foreach (var subject in my.subjects)
            {
                foreach (var property in subject.properties)
                {
                    property.PruneArrays(newNumberOfChoices);
                }
            }
            Undo.RecordObject(my, HVRVixxyLocalizationPhrase.AddChoiceLabel);
        }

        public void RemoveChoice(int choiceIndex)
        {
            var my = (HVRVixxyControl)target;
            if (my.NumberOfChoices <= 2) return;

            foreach (var activation in my.activations)
            {
                activation.choices = activation.choices.Where((_, i) => i != choiceIndex).ToArray();
            }
            foreach (var subject in my.subjects)
            {
                foreach (var property in subject.properties)
                {
                    property.RemoveChoiceAtIndex(choiceIndex);
                }
            }
            my.choices = my.choices.Where((_, i) => i != choiceIndex).ToArray();
            Undo.RecordObject(my, HVRVixxyLocalizationPhrase.RemoveChoiceLabel);
        }
    }
}
