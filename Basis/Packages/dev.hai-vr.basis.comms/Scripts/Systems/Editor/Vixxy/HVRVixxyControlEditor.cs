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
        internal const string MsgCannotEditInPlayMode = "Editing this component during Play Mode can lead to different visual and scene results than editing the component in Edit Mode.";

        internal const float DeleteButtonWidth = 40;

        internal const string UserViewLabel = "User View";
        internal const string CreatorViewLabel = "Creator View";
        private const string DeveloperViewLabel = "Developer View";

        public static bool _creatorViewFoldout;
        public static bool _developerViewFoldout;

        private HVRVixxyLayoutMenuView _menuView;
        private HVRVixxyLayoutCreatorView _creatorView;
        private HVRVixxyLayoutDeveloperView _developerView;

        private void OnEnable()
        {
            _creatorView = new HVRVixxyLayoutCreatorView(this);
            _developerView = new HVRVixxyLayoutDeveloperView(this);
        }

        public override void OnInspectorGUI()
        {
            var my = (HVRVixxyControl)target;
            HVRAvatarCommsEditor.EnsureAvatarHasPrefab(my.transform);

            var isPlaying = Application.isPlaying;
            if (isPlaying)
            {
                EditorGUILayout.HelpBox(MsgCannotEditInPlayMode, MessageType.Warning);
            }

            var anyChanged = false;
            _creatorViewFoldout = HaiEFCommon.LilFoldout(CreatorViewLabel, "", _creatorViewFoldout, ref anyChanged);
            if (_creatorViewFoldout)
            {
                if (_creatorView.Layout()) return;
            }
            _developerViewFoldout = HaiEFCommon.LilFoldout(DeveloperViewLabel, "", _developerViewFoldout, ref anyChanged);
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
