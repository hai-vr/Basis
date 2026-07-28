using Basis.Scripts.BasisSdk.Interactions;
using UnityEngine;

namespace Basis.Cinematics
{
    /// <summary>
    /// One placed point on the dolly track. Spawned as a real grabbable prop so a track is laid out
    /// by picking points up and putting them where the shot should pass, rather than typed in as
    /// coordinates.
    /// </summary>
    public class BasisCameraDollyWaypoint : MonoBehaviour
    {
        private const float BaseRadius = 0.06f;

        public BasisPickupInteractable Pickup { get; private set; }
        public bool IsGrabbed { get; private set; }

        /// <summary>1-based slot in the queue, shown on the marker label.</summary>
        public int DisplayIndex { get; set; }

        private Renderer markerRenderer;
        private MaterialPropertyBlock propertyBlock;
        private Color markerColor = Color.white;

        // The track redraws every frame, so all three of these are edge-triggered. Colour and scale
        // are only wasted native calls, but InteractableEnabled is not: its setter runs
        // ClearAllInfluencing() on every write of false, not just on a change, which would tear down
        // hover state each frame while the track is hidden.
        private bool hasAppliedColor;
        private float appliedScale = -1f;
        private bool appliedVisible = true;

        public Vector3 Position => transform.position;
        public Quaternion Rotation => transform.rotation;

        public static BasisCameraDollyWaypoint Spawn(Vector3 position, Quaternion rotation, float scale, bool gridSnap, float gridSize)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "CameraDollyWaypoint";
            marker.transform.SetPositionAndRotation(position, rotation);
            marker.transform.localScale = Vector3.one * (BaseRadius * 2f * Mathf.Max(0.05f, scale));

            int overlayUi = LayerMask.NameToLayer("OverlayUI");
            if (overlayUi >= 0)
            {
                marker.layer = overlayUi;
            }

            BasisCameraDollyWaypoint waypoint = marker.AddComponent<BasisCameraDollyWaypoint>();
            waypoint.markerRenderer = marker.GetComponent<Renderer>();
            waypoint.ApplyUrpMaterial();

            BasisPickupInteractable pickup = marker.AddComponent<BasisPickupInteractable>();
            pickup.GenerateColliderMesh = false;
            pickup.KinematicWhileInteracting = true;
            pickup.CanSelfSteal = false;
            pickup.enableGridSnap = gridSnap;
            pickup.gridSnapSize = gridSize;
            pickup.OnInteractStartEvent.AddListener(_ => waypoint.IsGrabbed = true);
            pickup.OnInteractEndEvent.AddListener(_ => waypoint.IsGrabbed = false);

            waypoint.Pickup = pickup;
            return waypoint;
        }

        public void SetScale(float scale)
        {
            scale = Mathf.Max(0.05f, scale);
            if (Mathf.Approximately(scale, appliedScale))
            {
                return;
            }
            appliedScale = scale;
            transform.localScale = Vector3.one * (BaseRadius * 2f * scale);
        }

        public void SetGridSnap(bool enabled, float size)
        {
            if (Pickup == null)
            {
                return;
            }
            Pickup.enableGridSnap = enabled;
            Pickup.gridSnapSize = size;
        }

        /// <summary>
        /// Tints the marker without touching the shared material, so every waypoint can carry its
        /// own colour off one material instance.
        /// </summary>
        public void SetColor(Color color)
        {
            if (hasAppliedColor && markerColor == color)
            {
                return;
            }

            markerColor = color;
            hasAppliedColor = true;
            if (markerRenderer == null)
            {
                return;
            }

            propertyBlock ??= new MaterialPropertyBlock();
            markerRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor(BaseColorId, color);
            propertyBlock.SetColor(LegacyColorId, color);
            markerRenderer.SetPropertyBlock(propertyBlock);
        }

        public Color Color => markerColor;

        public void SetVisible(bool visible)
        {
            if (visible == appliedVisible)
            {
                return;
            }
            appliedVisible = visible;

            if (markerRenderer != null)
            {
                markerRenderer.enabled = visible;
            }
            if (Pickup != null)
            {
                Pickup.InteractableEnabled = visible;
            }
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorId = Shader.PropertyToID("_Color");

        /// <summary>
        /// A primitive is created with the built-in default material, which does not render under
        /// URP. Swaps in the pipeline's Lit shader when it can be found and leaves the primitive
        /// alone when it cannot, so a missing shader degrades to a visible-but-untinted marker.
        /// </summary>
        private void ApplyUrpMaterial()
        {
            if (markerRenderer == null)
            {
                return;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }
            if (shader == null)
            {
                return;
            }

            markerRenderer.material = new Material(shader);
        }

        private void OnDestroy()
        {
            if (markerRenderer != null && markerRenderer.material != null)
            {
                Destroy(markerRenderer.material);
            }
        }
    }
}
