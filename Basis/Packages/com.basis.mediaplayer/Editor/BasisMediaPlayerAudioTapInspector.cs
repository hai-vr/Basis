using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// The tap has no serialized fields, so this draws the filter-ordering note and,
// when filters have ended up above the tap, the offer to put it back on top.
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

    private const string Warning =
        "The filters above this component are being fed silence. Raise the tap above " +
        "them so they process the stream.";

    public override VisualElement CreateInspectorGUI()
    {
        var root = new VisualElement();
        root.Add(new HelpBox(Note, HelpBoxMessageType.Info));
        root.Add(new BasisMediaPlayerTapOrdering.Notice(Source, _ => Warning));
        return root;
    }

    // Resolved per call, never held: the AudioSource can be replaced on this object
    // while the notice is up.
    private AudioSource[] Source()
    {
        var tap = target as BasisMediaPlayerAudioTap;
        AudioSource src = tap != null ? tap.GetComponent<AudioSource>() : null;
        return src != null ? new[] { src } : Array.Empty<AudioSource>();
    }
}
