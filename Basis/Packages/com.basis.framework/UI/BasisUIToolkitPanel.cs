using Basis.Scripts.BasisSdk.Helpers;
using UnityEngine;
using UnityEngine.UIElements;

namespace Basis.Scripts.UI
{
    /// <summary>
    /// Makes a world-space <see cref="UIDocument"/> reachable by the Basis pointer.
    /// Keeps a <see cref="BoxCollider"/> matched to the panel so the existing physics
    /// raycast finds it, and converts a world ray into UI Toolkit panel coordinates.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class BasisUIToolkitPanel : MonoBehaviour
    {
        public UIDocument Document;
        public bool ManageCollider = true;
        public bool AssignUILayer = true;
        public float ColliderDepth = 0.02f;

        private BoxCollider PanelCollider;
        private Vector2 CachedWorldSize;
        private static PanelInputConfiguration InputConfiguration;

        public IPanel RuntimePanel => Document != null ? Document.runtimePanel : null;

        /// <summary>
        /// Normal of the pressable face. Unlike wild uGUI canvases (which
        /// <see cref="Basis.Scripts.BasisSdk.Interactions.BasisDirectTouch"/> has to majority-vote
        /// on), a panel's orientation is fixed by this component, so one convention holds.
        /// </summary>
        public Vector3 FrontNormal => transform.forward;

        /// <summary>
        /// Basis is the only thing allowed to drive panels. Unity's built-in world-space input
        /// casts its own rays from an event camera and a screen position, which on desktop would
        /// deliver a second, camera-derived pointer on top of the Basis one.
        /// </summary>
        private static void EnsureInputConfiguration()
        {
            if (InputConfiguration != null || !Application.isPlaying)
            {
                return;
            }

            GameObject holder = new GameObject(nameof(BasisUIToolkitPanel) + "_InputConfiguration");
            Object.DontDestroyOnLoad(holder);
            InputConfiguration = holder.AddComponent<PanelInputConfiguration>();
            InputConfiguration.processWorldSpaceInput = false;
            InputConfiguration.autoCreatePanelComponents = false;
        }

        private void OnValidate()
        {
            if (Document == null)
            {
                TryGetComponent(out Document);
            }
        }

        private void OnEnable()
        {
            if (Document == null)
            {
                TryGetComponent(out Document);
            }

            EnsureInputConfiguration();
            EnsureUILayer();
            RefreshCollider();
        }

        /// <summary>
        /// Moves the panel onto a layer the pointer's physics mask includes. A panel left on
        /// Default renders perfectly and is silently unclickable, which is a miserable thing to
        /// debug in a headset. An existing OverlayUI panel is left where the author put it.
        /// </summary>
        public void EnsureUILayer()
        {
            if (!AssignUILayer || BasisUIRaycast.IsUILayer(gameObject.layer))
            {
                return;
            }

            gameObject.layer = LayerMask.NameToLayer("UI");
        }

        /// <summary>
        /// Sizes the collider to the panel. Cheap enough to call whenever the document is resized.
        /// </summary>
        public void RefreshCollider()
        {
            if (!ManageCollider || Document == null)
            {
                return;
            }

            Vector2 size = Document.worldSpaceSize;
            if (size.x <= 0f || size.y <= 0f)
            {
                return;
            }

            if (PanelCollider == null)
            {
                PanelCollider = BasisHelpers.GetOrAddComponent<BoxCollider>(gameObject);
            }

            PanelCollider.size = new Vector3(size.x, size.y, ColliderDepth);
            PanelCollider.center = Vector3.zero;
            CachedWorldSize = size;
        }

        /// <summary>
        /// Projects a world ray onto the panel plane and converts the intersection to panel
        /// coordinates. Assumes the document uses the default centre pivot.
        /// </summary>
        /// <param name="requireInsideBounds">
        /// False while a press is captured, so a drag that leaves the panel keeps feeding
        /// coordinates instead of freezing (UI Toolkit clamps sliders itself).
        /// </param>
        public bool TryGetPanelPosition(Ray ray, bool requireInsideBounds, out Vector2 panelPosition, out Vector3 worldPosition, out float distance)
        {
            panelPosition = Vector2.zero;
            worldPosition = Vector3.zero;
            distance = 0f;

            IPanel panel = RuntimePanel;
            if (panel == null)
            {
                return false;
            }

            Vector2 worldSize = Document.worldSpaceSize;
            if (worldSize.x <= 0f || worldSize.y <= 0f)
            {
                return false;
            }

            if (CachedWorldSize != worldSize)
            {
                RefreshCollider();
            }

            Transform panelTransform = transform;
            Vector3 normal = panelTransform.forward;
            float denominator = Vector3.Dot(normal, ray.direction);
            if (Mathf.Abs(denominator) < 1e-6f)
            {
                return false;
            }

            // Signed solve rather than Plane.Raycast so a panel approached from behind still
            // resolves — the physics hit already decided this panel is the target.
            float enter = Vector3.Dot(normal, panelTransform.position - ray.origin) / denominator;
            if (enter < 0f)
            {
                return false;
            }

            worldPosition = ray.GetPoint(enter);
            distance = enter;
            return TryGetPanelPositionFromPoint(worldPosition, requireInsideBounds, out panelPosition);
        }

        /// <summary>
        /// Converts a world point on the panel plane into panel coordinates. The fingertip path
        /// uses this directly, having a contact point rather than a ray.
        /// </summary>
        public bool TryGetPanelPositionFromPoint(Vector3 worldPoint, bool requireInsideBounds, out Vector2 panelPosition)
        {
            panelPosition = Vector2.zero;

            IPanel panel = RuntimePanel;
            if (panel == null)
            {
                return false;
            }

            Vector2 worldSize = Document.worldSpaceSize;
            if (worldSize.x <= 0f || worldSize.y <= 0f)
            {
                return false;
            }

            Rect layout = panel.visualTree.layout;
            Vector3 local = transform.InverseTransformPoint(worldPoint);
            return TryConvertLocalPointToPanel(local, worldSize, layout.size, requireInsideBounds, out panelPosition);
        }

        /// <summary>
        /// Pure local-space to panel-space mapping, kept free of component state so the
        /// convention — which physical corner is panel (0,0) — is pinned by tests rather than
        /// discovered in a headset. Both the ray and the fingertip path convert through here,
        /// so a mirrored axis is a single sign change in one place.
        /// </summary>
        public static bool TryConvertLocalPointToPanel(Vector3 localPoint, Vector2 worldSize, Vector2 panelSize, bool requireInsideBounds, out Vector2 panelPosition)
        {
            panelPosition = Vector2.zero;
            if (worldSize.x <= 0f || worldSize.y <= 0f)
            {
                return false;
            }

            float u = (localPoint.x / worldSize.x) + 0.5f;
            float v = (localPoint.y / worldSize.y) + 0.5f;

            if (requireInsideBounds && (u < 0f || u > 1f || v < 0f || v > 1f))
            {
                return false;
            }

            // Panel space is top-left origin; local space is bottom-up, so v inverts.
            panelPosition = new Vector2(u * panelSize.x, (1f - v) * panelSize.y);
            return true;
        }
    }
}
