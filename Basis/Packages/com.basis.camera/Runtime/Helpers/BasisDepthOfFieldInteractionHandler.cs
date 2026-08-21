using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;


/// <summary>
/// Handles Depth of Field (DoF) interaction and UI for a handheld camera.
/// Toggles DoF on/off, shows a focus cursor, and sets focus distance from raycasts.
/// </summary>
[System.Serializable]
public class BasisDepthOfFieldInteractionHandler : MonoBehaviour
{
    [Header("References")]
    /// <summary>
    /// Controller that owns the capture camera and DoF metadata/controls.
    /// </summary>
    public BasisHandHeldCamera cameraController;

    /// <summary>
    /// UI element shown at the current focus point within the preview.
    /// </summary>
    public RectTransform focusCursor;

    /// <summary>
    /// Toggle controlling whether DoF is active.
    /// </summary>
    public Toggle depthOfFieldToggle;

    [Header("Raycasting")]
    /// <summary>
    /// Maximum raycast distance when determining focus target.
    /// </summary>
    public float maxRaycastDistance = 1000f;

    /// <summary>
    /// Layers the focus ray is allowed to land on. Defaults to Unity's DefaultRaycastLayers —
    /// everything but Ignore Raycast — which is what the untargeted overload used.
    /// </summary>
    public LayerMask focusLayers = ~(1 << 2);

    /// <summary>
    /// Metres of slop added to every bone capsule when hit-testing a player, so a click that lands
    /// just off a thin limb still counts. Zero tests the body exactly.
    /// </summary>
    public float subjectPadding = 0.05f;

    /// <summary>
    /// Validates references and wires up the DoF toggle listener.
    /// </summary>
    private void Awake()
    {
        if (cameraController == null)
            BasisDebug.LogError("BasisDepthOfFieldInteractionHandler: cameraController must be assigned!");
        else if (cameraController.MetaData == null)
            BasisDebug.LogError("BasisDepthOfFieldInteractionHandler: cameraController.MetaData must be assigned!");
        else if (cameraController.MetaData.depthOfField == null)
            BasisDebug.LogError("BasisDepthOfFieldInteractionHandler: cameraController.MetaData.depthOfField must be assigned!");

        if (focusCursor == null)
            BasisDebug.LogError("BasisDepthOfFieldInteractionHandler: focusCursor must be assigned!");

        if (depthOfFieldToggle == null)
            BasisDebug.LogError("BasisDepthOfFieldInteractionHandler: depthOfFieldToggle must be assigned!");

        if (cameraController != null && cameraController.HandHeld == null)
            BasisDebug.LogError("BasisDepthOfFieldInteractionHandler: cameraController.HandHeld must be assigned!");
        else if (cameraController != null && cameraController.HandHeld != null)
        {
            if (cameraController.HandHeld.DepthFocusDistanceSlider == null)
                BasisDebug.LogError("BasisDepthOfFieldInteractionHandler: cameraController.HandHeld.DepthFocusDistanceSlider must be assigned!");
            if (cameraController.HandHeld.DOFFocusOutput == null)
                BasisDebug.LogError("BasisDepthOfFieldInteractionHandler: cameraController.HandHeld.DOFFocusOutput must be assigned!");
        }

        if (depthOfFieldToggle != null)
            depthOfFieldToggle.onValueChanged.AddListener(SetDoFState);
    }

    /// <summary>
    /// Enables/disables DoF and syncs UI + handheld mode when the toggle changes.
    /// </summary>
    /// <param name="enabled">Whether DoF should be active.</param>
    public void SetDoFState(bool enabled)
    {
        // Turning DoF on while the stored blur style is Off would render nothing and read as
        // "the toggle does nothing", so promote it to a real mode on the way in.
        if (enabled && cameraController.MetaData.depthOfField.mode.value == DepthOfFieldMode.Off)
        {
            cameraController.MetaData.depthOfField.mode.overrideState = true;
            cameraController.MetaData.depthOfField.mode.value = DepthOfFieldMode.Bokeh;
        }

        cameraController.MetaData.depthOfField.active = enabled;
        depthOfFieldToggle.SetIsOnWithoutNotify(enabled);
        SetCursorVisibility(enabled);
        cameraController.HandHeld?.SetDepthMode(cameraController.HandHeld.currentDepthMode);
    }

    /// <summary>
    /// Shows/hides the focus cursor and mirrors DoF active state for safety.
    /// </summary>
    /// <param name="enabled">Whether the cursor should be visible.</param>
    private void SetCursorVisibility(bool enabled)
    {
        focusCursor.gameObject.SetActive(enabled);
        cameraController.MetaData.depthOfField.active = enabled;
    }

    /// <summary>
    /// Picks what was clicked and pulls focus onto it. Players carry no colliders, so a plain
    /// raycast focuses on whatever is behind them; they are hit-tested against capsules fitted to
    /// their live skeleton instead, and only count when nothing solid stands between them and the
    /// lens. Anything else falls back to the world raycast, which skips the camera prop itself and
    /// every player's own colliders.
    /// </summary>
    /// <param name="ray">Ray from the preview/camera pixel into the world.</param>
    public void ApplyFocusFromRay(Ray ray)
    {
        if (cameraController == null) return;

        bool hasWorld = BasisCameraSubjectPicker.TryRaycastWorld(ray, maxRaycastDistance, focusLayers, cameraController, out RaycastHit worldHit, out float worldDistance);

        if (BasisCameraSubjectPicker.TryPickSubject(ray, maxRaycastDistance, hasWorld ? worldDistance : float.PositiveInfinity, subjectPadding, !cameraController.IsDetachedFromHand, out BasisCameraSubjectHit subject))
        {
            string who = subject.Player != null && !string.IsNullOrEmpty(subject.Player.DisplayName) ? subject.Player.DisplayName : "player";
            if (!subject.FromSkeleton) who += " (bounds)";
            RackFocusToPoint(subject.Point, who);
            return;
        }

        if (!hasWorld)
        {
            BasisDebug.Log("[DOF] Raycast missed");
            return;
        }

        RackFocusToPoint(worldHit.point, worldHit.collider != null ? worldHit.collider.name : "world");
    }

    private void RackFocusToPoint(Vector3 worldPoint, string label)
    {
        if (!cameraController.TryGetFocusDepth(worldPoint, out float depth))
        {
            BasisDebug.Log("[DOF] Hit is behind or inside the minimum focus distance — skipping");
            return;
        }

        cameraController.RackFocusTo(depth);

        if (focusCursor != null && !focusCursor.gameObject.activeSelf)
            focusCursor.gameObject.SetActive(true);

        BasisDebug.Log($"[DOF] Pulling focus to {depth:F2} units over {cameraController.focusRackSeconds:F2}s (hit {label})");
    }
}
