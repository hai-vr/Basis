using UnityEditor;
using UnityEngine.UIElements;

// The tap has no serialized fields, so this draws only a note on filter ordering.
// It also has to be a UIElements inspector: Unity's default MonoBehaviour inspector
// draws a level meter for any script with an OnAudioFilterRead, and that IMGUI path
// dereferences GUIView.current without a null check, which throws whenever the
// inspector redraws outside a GUIView repaint (adding a component, for instance).
[CustomEditor(typeof(BasisMediaPlayerAudioTap))]
public class BasisMediaPlayerAudioTapInspector : Editor
{
    private const string Note =
        "Generates this AudioSource's audio from the media player's decoded stream. " +
        "Unity applies audio filters in component order, so a Low Pass / Reverb / " +
        "Chorus filter must sit BELOW this component to hear the stream. Anything " +
        "above it is fed silence.";

    public override VisualElement CreateInspectorGUI()
    {
        var root = new VisualElement();
        root.Add(new HelpBox(Note, HelpBoxMessageType.Info));
        return root;
    }
}
