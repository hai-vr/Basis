using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
public class SendWorldPosToUIShader : MonoBehaviour
{
    [Header("Object whose world position we want to send")]
    public Transform sourceObject;

    [Header("UI element with the shader material")]
    public Graphic targetUI;   // Image, RawImage, etc.

    private Material _runtimeMaterial;

    void OnEnable()
    {
        if (targetUI != null)
        {
            // Ensure UI material is instanced (required for edit mode)
            _runtimeMaterial = targetUI.material;
        }
    }

    void Update()
    {
        if (sourceObject == null || targetUI == null)
            return;

        // In edit mode, Unity may recreate materials frequently.
        if (!Application.isPlaying)
            _runtimeMaterial = targetUI.material;

        _runtimeMaterial.SetVector("_CursorPos", sourceObject.position);
    }
}
