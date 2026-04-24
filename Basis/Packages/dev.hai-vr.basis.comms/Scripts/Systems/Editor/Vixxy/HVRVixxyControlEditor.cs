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

        public static bool _creatorViewFoldout;
        public static bool _toggleObjectsFoldout;
        public static bool _changePropertiesFoldout;
        public static bool _developerViewFoldout;

        private HVRVixxyLayoutMenuView _menuView;
        private HVRVixxyLayoutChangePropertiesView _changePropertiesView;
        private HVRVixxyLayoutToggleObjectsView _toggleObjectsView;
        private HVRVixxyLayoutDeveloperView _developerView;

        private void OnEnable()
        {
            _changePropertiesView = new HVRVixxyLayoutChangePropertiesView(this);
            _toggleObjectsView = new HVRVixxyLayoutToggleObjectsView(this);
            _developerView = new HVRVixxyLayoutDeveloperView(this);
        }

        public override void OnInspectorGUI()
        {
            var my = (HVRVixxyControl)target;
            HVRAvatarCommsEditor.EnsureAvatarHasPrefab(my.transform);

            var isPlaying = Application.isPlaying;
            if (isPlaying)
            {
                EditorGUILayout.HelpBox(VixenLocalizationPhrase.MsgCannotEditInPlayMode, MessageType.Warning);
            }

            var anyChanged = false;
            _creatorViewFoldout = HaiEFCommon.LilFoldout(VixenLocalizationPhrase.CreatorView, "", _creatorViewFoldout, ref anyChanged);
            if (_creatorViewFoldout)
            {
                if (_changePropertiesView.LayoutCreatorView()) return;
            }
            _toggleObjectsFoldout = HaiEFCommon.LilFoldout(VixenLocalizationPhrase.ToggleObjectsViewLabel, "", _toggleObjectsFoldout, ref anyChanged);
            if (_toggleObjectsFoldout)
            {
                if (_toggleObjectsView.LayoutToggleObjects()) return;
            }
            _changePropertiesFoldout = HaiEFCommon.LilFoldout(VixenLocalizationPhrase.ChangePropertiesViewLabel, "", _changePropertiesFoldout, ref anyChanged);
            if (_changePropertiesFoldout)
            {
                if (_changePropertiesView.LayoutChangeProperties()) return;
            }
            EditorGUILayout.Separator();

            EditorGUILayout.LabelField(VixenLocalizationPhrase.AdvancedLabel, EditorStyles.boldLabel);
            _developerViewFoldout = HaiEFCommon.LilFoldout(VixenLocalizationPhrase.DeveloperViewLabel, "", _developerViewFoldout, ref anyChanged);
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
    }
}
