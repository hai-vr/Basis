using TMPro;
using UnityEngine;

/// <summary>
/// World-space debug text label for a gizmo. Created in code (no prefab) so the
/// text system is self-contained; the underlying <see cref="TMP_Text"/> picks up
/// the project's default TMP font on AddComponent. Per-frame mutations are cached
/// so a label whose value hasn't changed does not re-tessellate the text mesh or
/// touch the color buffer.
/// </summary>
public class BasisTextGizmos : MonoBehaviour
{
    public TMP_Text Text;

    private string _lastText;
    private Color _lastColor = new Color(-1f, -1f, -1f, -1f);

    public void Initialize(string text, Color color)
    {
        _lastText = text;
        _lastColor = color;
    }

    /// <summary>Sets pose + uniform scale in a single transform write.</summary>
    public void Apply(Vector3 position, Quaternion rotation, float scale)
    {
        Transform t = transform;
        t.SetPositionAndRotation(position, rotation);
        t.localScale = new Vector3(scale, scale, scale);
    }

    /// <summary>Only re-assigns text when the string actually changed (TMP rebuilds the mesh on set).</summary>
    public void SetText(string text)
    {
        if (Text != null && text != _lastText)
        {
            Text.text = text;
            _lastText = text;
        }
    }

    public void SetColor(Color color)
    {
        if (Text != null && color != _lastColor)
        {
            Text.color = color;
            _lastColor = color;
        }
    }
}
