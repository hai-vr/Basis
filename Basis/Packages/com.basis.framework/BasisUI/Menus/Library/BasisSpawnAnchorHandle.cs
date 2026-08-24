using System;
using Basis.Scripts.BasisSdk.Interactions;
using Basis.Scripts.Device_Management.Devices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Basis.BasisUI
{
    public class BasisSpawnAnchorHandle : BasisPickupInteractable
    {
        public const float BodyDiameter = 0.12f;
        public const float ArrowLength = 0.18f;
        public const float ArrowThickness = 0.02f;
        public const float LabelGap = 0.06f;
        public const float LabelScale = 0.02f;
        public const float GestureStep = 1.02f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorId = Shader.PropertyToID("_Color");
        private static readonly Color SelectedColor = new Color(1f, 0.65f, 0.1f, 1f);
        private static readonly Color IdleColor = new Color(0.85f, 0.85f, 0.9f, 1f);
        private static readonly Color ForwardColor = new Color(0.25f, 0.55f, 1f, 1f);
        private static readonly Color UpColor = new Color(0.35f, 0.9f, 0.4f, 1f);
        private static MaterialPropertyBlock block;

        public bool IsGrabbed { get; private set; }
        public Action<BasisSpawnAnchorHandle> OnGrabbed;
        public Action<BasisSpawnAnchorHandle> OnReleased;
        public Action<BasisSpawnAnchorHandle, float> OnScaleGesture;

        private Transform body;
        private Renderer bodyRenderer;
        private Material material;
        private int grabCount;
        private float anchorScale = 1f;
        private string labelText = string.Empty;
        private Color labelColor = IdleColor;
        private int labelId = -1;

        public static BasisSpawnAnchorHandle Spawn(Transform parent)
        {
            GameObject root = new GameObject("SpawnAnchorHandle");
            root.transform.SetParent(parent, false);
            int layer = LayerMask.NameToLayer("OverlayUI");
            if (layer < 0)
            {
                layer = 0;
            }
            root.layer = layer;

            Material material = CreateMaterial();
            Renderer bodyRenderer = BuildPart(PrimitiveType.Sphere, "Body", root.transform, Vector3.zero, Vector3.one * BodyDiameter, layer, material, IdleColor, true);
            BuildPart(PrimitiveType.Cube, "Forward", root.transform, new Vector3(0f, 0f, BodyDiameter * 0.5f + ArrowLength * 0.5f), new Vector3(ArrowThickness, ArrowThickness, ArrowLength), layer, material, ForwardColor, false);
            BuildPart(PrimitiveType.Cube, "Up", root.transform, new Vector3(0f, BodyDiameter * 0.5f + ArrowLength * 0.25f, 0f), new Vector3(ArrowThickness, ArrowLength * 0.5f, ArrowThickness), layer, material, UpColor, false);

            BasisSpawnAnchorHandle handle = root.AddComponent<BasisSpawnAnchorHandle>();
            handle.body = bodyRenderer.transform;
            handle.bodyRenderer = bodyRenderer;
            handle.material = material;
            handle.GenerateColliderMesh = false;
            handle.KinematicWhileInteracting = true;
            handle.CanSelfSteal = false;
            handle.enableScaleWithGesture = true;
            handle.minScalePercent = BasisSpawnAnchors.MinScale * 100f;
            handle.maxScalePercent = BasisSpawnAnchors.MaxScale * 100f;
            handle.OnInteractStartEvent.AddListener(handle.Grabbed);
            handle.OnInteractEndEvent.AddListener(handle.Released);
            return handle;
        }

        private static Material CreateMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }
            return shader == null ? null : new Material(shader);
        }

        private static Renderer BuildPart(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 localScale, int layer, Material material, Color color, bool keepCollider)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = name;
            part.layer = layer;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
            if (!keepCollider && part.TryGetComponent(out Collider collider))
            {
                DestroyImmediate(collider);
            }
            Renderer renderer = part.GetComponent<Renderer>();
            if (material != null)
            {
                renderer.sharedMaterial = material;
            }
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            Tint(renderer, color);
            return renderer;
        }

        private static void Tint(Renderer renderer, Color color)
        {
            if (renderer == null)
            {
                return;
            }
            block ??= new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, color);
            block.SetColor(LegacyColorId, color);
            renderer.SetPropertyBlock(block);
        }

        public void Apply(BasisSpawnAnchors.SpawnAnchor anchor, bool selected)
        {
            enableGridSnap = BasisSettingsDefaults.SpawnAnchorPositionSnap.RawValue;
            gridSnapSize = BasisSettingsDefaults.SpawnAnchorPositionSnapSize.RawValue;
            enableRotationSnap = BasisSettingsDefaults.SpawnAnchorRotationSnap.RawValue;
            rotationSnapDegrees = BasisSettingsDefaults.SpawnAnchorRotationSnapDegrees.RawValue;
            anchorScale = anchor.OverrideScale ? anchor.Scale : 1f;
            if (!IsGrabbed)
            {
                transform.SetPositionAndRotation(anchor.Position, anchor.Rotation);
            }
            if (body != null)
            {
                body.localScale = Vector3.one * (BodyDiameter * Mathf.Clamp(anchorScale, 0.5f, 2.5f));
            }
            labelColor = selected ? SelectedColor : IdleColor;
            Tint(bodyRenderer, labelColor);
            labelText = anchor.OverrideScale ? $"{anchor.Name}\n×{anchor.Scale:0.00}" : anchor.Name;
        }

        public void Tick(Vector3 cameraPosition, float scale)
        {
            float bodyHeight = body != null ? body.localScale.y * 0.5f : BodyDiameter * 0.5f;
            Vector3 position = transform.position + Vector3.up * (bodyHeight + LabelGap * scale);
            if (labelId <= 0 && !BasisGizmoManager.CreateTextGizmo("SpawnAnchor", out labelId, position, labelText, labelColor))
            {
                return;
            }
            BasisGizmoManager.UpdateTextGizmo(labelId, position, BasisGizmoManager.BillboardRotation(position, cameraPosition), LabelScale * scale, labelText, labelColor);
        }

        public void ForgetLabel()
        {
            labelId = -1;
        }

        public void Despawn()
        {
            DestroyLabel();
            Destroy(gameObject);
        }

        private void DestroyLabel()
        {
            if (labelId > 0)
            {
                BasisGizmoManager.DestroyGizmo(labelId);
            }
            labelId = -1;
        }

        private void Grabbed(BasisInput input)
        {
            grabCount++;
            if (grabCount != 1)
            {
                return;
            }
            IsGrabbed = true;
            OnGrabbed?.Invoke(this);
        }

        private void Released(BasisInput input)
        {
            grabCount = Mathf.Max(0, grabCount - 1);
            if (grabCount != 0)
            {
                return;
            }
            IsGrabbed = false;
            OnReleased?.Invoke(this);
        }

        protected override float GestureScaleReference => 1f;

        protected override void ApplyGestureScaleStep(BasisTransform.Direction scaleDirection, float stepSize, float minScale, float maxScale)
        {
            float next = Mathf.Clamp(anchorScale * (scaleDirection == BasisTransform.Direction.Embiggen ? GestureStep : 1f / GestureStep), minScale, maxScale);
            if (Mathf.Approximately(next, anchorScale))
            {
                return;
            }
            anchorScale = next;
            OnScaleGesture?.Invoke(this, next);
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            DestroyLabel();
            if (material != null)
            {
                Destroy(material);
                material = null;
            }
        }
    }
}
