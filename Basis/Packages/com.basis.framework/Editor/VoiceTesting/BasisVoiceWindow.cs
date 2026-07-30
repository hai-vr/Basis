using UnityEditor;
using UnityEngine;

namespace Basis.Scripts.Networking.Voice.EditorTools
{
    /// <summary>
    /// Everything voice, in one window.
    ///
    /// Four windows used to cover one pipeline: the offline pipeline matrix, the capture analyzer,
    /// the live remote receive view and the lipsync view. They are stages of the same path —
    /// microphone in, viseme and speaker out — so they are tabs of one window. The two offline tabs
    /// work without play mode; the two live tabs need it.
    /// </summary>
    public sealed class BasisVoiceWindow : BasisTabbedEditorWindow
    {
        protected override string HeaderTitle => "Voice";

        protected override string HeaderSubtitle =>
            "Microphone to speaker: offline harnesses and the live receive/lipsync views.";

        [MenuItem("Basis/Debug/Voice", false, 602)]
        public static void ShowWindow()
        {
            BasisVoiceWindow w = GetWindow<BasisVoiceWindow>();
            w.titleContent = new GUIContent("Voice");
            w.minSize = new Vector2(640, 560);
            w.Show();
        }

        protected override BasisEditorTabPage[] BuildPages() => new BasisEditorTabPage[]
        {
            new BasisVoicePipelineTab(),
            new BasisVoiceCaptureTab(),
            new BasisRemoteAudioTab(),
            new BasisOpenLipSyncTab(),
        };
    }
}
