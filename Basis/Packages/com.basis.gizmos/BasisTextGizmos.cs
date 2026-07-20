using TMPro;
using UnityEngine;

/// <summary>
/// World-space debug text label for a gizmo. Created in code (no prefab) so the
/// text system is self-contained; the underlying <see cref="TMP_Text"/> picks up
/// the project's default TMP font on AddComponent. Per-frame mutations are cached
/// so a label whose value hasn't changed does not re-tessellate the text mesh or
/// touch the color buffer; colors within ~1.5% per channel count as unchanged so a
/// continuously-lerped health tint doesn't dirty the mesh every frame. Labels are
/// pooled and visibility-capped by BasisGizmoManager, which toggles the renderer
/// through <see cref="SetVisible"/> rather than deactivating the GameObject.
/// </summary>
public class BasisTextGizmos : MonoBehaviour
{
    public TMP_Text Text;
    public MeshRenderer Renderer;

    /// <summary>
    /// The per-label clone TMP creates for the fontMaterial overlay-shader swap.
    /// Tracked so the pool can destroy it with the label instead of leaking one
    /// material per label ever created.
    /// </summary>
    public Material MaterialInstance;

    private string _lastText;
    private Color _lastColor = new Color(-1f, -1f, -1f, -1f);
    private bool _rendererEnabled = true;

    public void Initialize(string text, Color color)
    {
        _lastText = text;
        _lastColor = color;
    }

    /// <summary>
    /// Re-arms a pooled label with fresh content, bypassing the change caches so the
    /// text/color from its previous life can't be mistaken for current.
    /// </summary>
    public void ResetContent(string text, Color color)
    {
        if (Text != null)
        {
            Text.text = text;
            Text.color = color;
        }
        _lastText = text;
        _lastColor = color;
        SetVisible(true);
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
        if (Text == null)
        {
            return;
        }
        const float threshold = 1f / 64f;
        if (Mathf.Abs(color.r - _lastColor.r) < threshold &&
            Mathf.Abs(color.g - _lastColor.g) < threshold &&
            Mathf.Abs(color.b - _lastColor.b) < threshold &&
            Mathf.Abs(color.a - _lastColor.a) < threshold)
        {
            return;
        }
        Text.color = color;
        _lastColor = color;
    }

    /// <summary>Cheap show/hide via the renderer, diffed so steady state costs nothing.</summary>
    public void SetVisible(bool visible)
    {
        if (Renderer != null && _rendererEnabled != visible)
        {
            Renderer.enabled = visible;
            _rendererEnabled = visible;
        }
    }
}
