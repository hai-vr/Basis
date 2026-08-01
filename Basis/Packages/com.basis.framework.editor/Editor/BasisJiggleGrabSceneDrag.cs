using Basis.Scripts.BasisSdk.Interactions;
using GatorDragonGames.JigglePhysics;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Scene-view click-dragging of jiggle chains, shared by the Jiggle Grab Tester window and the
/// toggle appended to every JiggleRig inspector — so the drag works from either entry point, and
/// with neither of them open.
///
/// The drag drives the real constraint pipeline (driver applied-set → JigglePhysics.SetGrabConstraints
/// → the fenced drain → the pin in the Burst simulate job) but skips the half that needs a peer:
/// no hand bone, no permissions, nothing networked.
///
/// Play mode only. JiggleRig has no [ExecuteAlways] and the simulation is pumped by
/// BasisEventDriver's LateUpdate, so nothing moves in edit mode — the toggle stays visible and
/// says so rather than silently doing nothing.
/// </summary>
[InitializeOnLoad]
public static class BasisJiggleGrabSceneDrag
{
    private const string EnabledPref = "Basis.JiggleGrab.SceneDrag.Enabled";
    private const string RadiusPref = "Basis.JiggleGrab.SceneDrag.Radius";
    private const string LengthPref = "Basis.JiggleGrab.SceneDrag.RayLength";

    public const float MinRadius = 0.01f;
    public const float MaxRadius = 1f;

    private static Plane dragPlane;
    private static int dragControl;

    public static event System.Action EnabledChanged;

    static BasisJiggleGrabSceneDrag()
    {
        SceneView.duringSceneGui += OnSceneGUI;
        EditorApplication.playModeStateChanged += _ => BasisJiggleGrabDriver.EndEditorGrab();
        JiggleRigInspectorExtensions.DrawAdditionalGUI += AppendRigInspectorGUI;
    }

    public static bool Enabled
    {
        get => EditorPrefs.GetBool(EnabledPref, false);
        set
        {
            if (value == Enabled)
            {
                return;
            }
            EditorPrefs.SetBool(EnabledPref, value);
            if (!value)
            {
                BasisJiggleGrabDriver.EndEditorGrab();
            }
            EnabledChanged?.Invoke();
            SceneView.RepaintAll();
        }
    }

    public static float PickRadius
    {
        get => Mathf.Clamp(EditorPrefs.GetFloat(RadiusPref, 0.12f), MinRadius, MaxRadius);
        set => EditorPrefs.SetFloat(RadiusPref, Mathf.Clamp(value, MinRadius, MaxRadius));
    }

    public static float RayLength
    {
        get => Mathf.Clamp(EditorPrefs.GetFloat(LengthPref, 25f), 1f, 100f);
        set => EditorPrefs.SetFloat(LengthPref, Mathf.Clamp(value, 1f, 100f));
    }

    /// <summary>Appended to every JiggleRig inspector through the package's extension hook.</summary>
    private static void AppendRigInspectorGUI(Component rig, VisualElement container)
    {
        VisualElement group = new VisualElement();
        group.style.marginTop = 6;

        Toggle toggle = new Toggle("Scene Drag (test)")
        {
            value = Enabled,
            tooltip = "Left click and drag jiggle chains in the Scene view to test the pull. " +
                      "Takes over left click in the Scene view while on. Local only — nothing is networked.",
        };
        toggle.RegisterValueChangedCallback(evt => Enabled = evt.newValue);
        EnabledChanged += () => toggle.SetValueWithoutNotify(Enabled);
        group.Add(toggle);

        if (!Application.isPlaying)
        {
            group.Add(new HelpBox("Enter play mode to drag — jiggle only simulates while playing.",
                HelpBoxMessageType.Info));
        }

        Button openWindow = new Button(BasisJiggleGrabTesterWindow.ShowWindow) { text = "Open Jiggle Grab Tester" };
        group.Add(openWindow);

        container.Add(group);
    }

    private static void OnSceneGUI(SceneView sceneView)
    {
        if (!Enabled || !Application.isPlaying)
        {
            return;
        }

        Event current = Event.current;
        int controlId = GUIUtility.GetControlID(FocusType.Passive);
        Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);

        switch (current.GetTypeForControl(controlId))
        {
            case EventType.Layout:
                HandleUtility.AddDefaultControl(controlId);
                break;

            case EventType.MouseDown:
                if (current.button == 0 && !current.alt
                    && BasisJiggleGrabDriver.BeginEditorGrab(ray, RayLength, PickRadius))
                {
                    dragPlane = new Plane(-sceneView.camera.transform.forward, BasisJiggleGrabDriver.EditorGrabPoint);
                    dragControl = controlId;
                    GUIUtility.hotControl = controlId;
                    current.Use();
                }
                break;

            case EventType.MouseDrag:
                if (GUIUtility.hotControl == dragControl && BasisJiggleGrabDriver.HasEditorGrab)
                {
                    if (dragPlane.Raycast(ray, out float distance))
                    {
                        BasisJiggleGrabDriver.SetEditorGrabTarget(ray.GetPoint(distance));
                    }
                    current.Use();
                    sceneView.Repaint();
                }
                break;

            case EventType.MouseUp:
                if (GUIUtility.hotControl == dragControl)
                {
                    BasisJiggleGrabDriver.EndEditorGrab();
                    GUIUtility.hotControl = 0;
                    dragControl = 0;
                    current.Use();
                }
                break;

            case EventType.Repaint:
                DrawHandles(ray);
                break;
        }
    }

    private static void DrawHandles(Ray ray)
    {
        if (BasisJiggleGrabDriver.HasEditorGrab)
        {
            Vector3 target = BasisJiggleGrabDriver.EditorGrabTarget;
            Handles.color = new Color(1f, 0.55f, 0.1f);
            Handles.SphereHandleCap(0, target, Quaternion.identity, HandleUtility.GetHandleSize(target) * 0.06f, EventType.Repaint);
            Handles.DrawLine(BasisJiggleGrabDriver.EditorGrabPoint, target);
            return;
        }

        if (BasisJiggleGrabDriver.TryFindEditorGrabPoint(ray, RayLength, PickRadius, out Vector3 hover))
        {
            Handles.color = new Color(0.3f, 0.9f, 1f);
            Handles.SphereHandleCap(0, hover, Quaternion.identity, HandleUtility.GetHandleSize(hover) * 0.05f, EventType.Repaint);
        }
    }
}
