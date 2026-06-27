using System;
using System.IO;
using Basis.Scripts.BasisSdk.Interactions;
using TMPro;
using UnityEngine;

namespace Basis.ImagePickup
{
    /// <summary>
    /// A spawned image pickup. Front face shows the image on an unlit material; the back carries the
    /// Hide/Save/Delete controls and the spawner label. The owner instance is grabbable; remote instances
    /// are display only and follow transform updates from the owner.
    /// </summary>
    public class BasisImagePickupObject : MonoBehaviour
    {
        public Guid ImageId;
        public ushort OwnerId;
        public string OwnerName;
        public bool IsOwner;

        public TextMeshProUGUI HideLabel;

        private Texture2D _texture;
        private byte[] _cleanPng;
        private Material _material;
        private Material _backMaterial;
        private MeshRenderer _frontRenderer;
        private BasisImagePickupManager _manager;

        private bool _hidden;
        private bool _hasRemoteTarget;
        private Vector3 _targetPosition;
        private Quaternion _targetRotation;
        private float _targetScale = 1f;

        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public byte[] CleanPng => _cleanPng;
        public bool IsHidden => _hidden;

        public static BasisImagePickupObject Build(BasisImagePickupManager manager, Guid id, ushort ownerId, string ownerName, bool isOwner, Texture2D texture, byte[] cleanPng, Vector3 position, Quaternion rotation)
        {
            var root = new GameObject($"BasisImagePickup_{ShortId(id)}");
            root.transform.SetPositionAndRotation(position, rotation);

            int interactableLayer = LayerMask.NameToLayer("Interactable");
            if (interactableLayer >= 0) root.layer = interactableLayer;

            var pickup = root.AddComponent<BasisImagePickupObject>();
            pickup._manager = manager;
            pickup.ImageId = id;
            pickup.OwnerId = ownerId;
            pickup.OwnerName = string.IsNullOrEmpty(ownerName) ? "Unknown" : ownerName;
            pickup.IsOwner = isOwner;
            pickup._texture = texture;
            pickup._cleanPng = cleanPng;

            float aspect = texture.height > 0 ? (float)texture.width / texture.height : 1f;
            float panelHeight = BasisImagePickupSettings.BaseHeightMeters;
            float panelWidth = panelHeight * Mathf.Max(0.05f, aspect);

            var front = GameObject.CreatePrimitive(PrimitiveType.Quad);
            front.name = "Front";
            if (interactableLayer >= 0) front.layer = interactableLayer;
            front.transform.SetParent(root.transform, false);
            front.transform.localScale = new Vector3(panelWidth, panelHeight, 1f);

            if (front.TryGetComponent(out MeshCollider meshCollider)) DestroyImmediate(meshCollider);

            pickup._frontRenderer = front.GetComponent<MeshRenderer>();
            pickup._material = new Material(BundledContentHolder.Instance.UnlitUrpShader);
            if (pickup._material.HasProperty(BaseMapId)) pickup._material.SetTexture(BaseMapId, texture);
            else pickup._material.mainTexture = texture;
            if (pickup._material.HasProperty(BaseColorId)) pickup._material.SetColor(BaseColorId, Color.white);
            pickup._frontRenderer.sharedMaterial = pickup._material;

            var back = GameObject.CreatePrimitive(PrimitiveType.Quad);
            back.name = "Back";
            if (interactableLayer >= 0) back.layer = interactableLayer;
            back.transform.SetParent(root.transform, false);
            back.transform.localScale = new Vector3(panelWidth, panelHeight, 1f);
            back.transform.SetLocalPositionAndRotation(new Vector3(0f, 0f, 0.002f), Quaternion.Euler(0f, 180f, 0f));

            if (back.TryGetComponent(out MeshCollider backMeshCollider)) DestroyImmediate(backMeshCollider);

            pickup._backMaterial = new Material(BundledContentHolder.Instance.UnlitUrpShader);
            if (pickup._backMaterial.HasProperty(BaseColorId)) pickup._backMaterial.SetColor(BaseColorId, Color.white);
            else pickup._backMaterial.color = Color.white;
            back.GetComponent<MeshRenderer>().sharedMaterial = pickup._backMaterial;

            if (isOwner)
            {
                var box = root.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(panelWidth, panelHeight, 0.02f);

                var body = root.AddComponent<Rigidbody>();
                body.isKinematic = false;
                body.useGravity = false;
                body.linearDamping = 1.5f;
                body.angularDamping = 2.5f;
                body.interpolation = RigidbodyInterpolation.Interpolate;

                var interactable = root.AddComponent<BasisPickupInteractable>();
                interactable.RigidRef = body;
                interactable.GenerateColliderMesh = false;
                interactable.enableScaleWithGesture = true;
                interactable.minScalePercent = 25f;
                interactable.maxScalePercent = 400f;
            }

            BasisImagePickupBackPanel.Build(root.transform, pickup, panelWidth, panelHeight);
            return pickup;
        }

        private void Update()
        {
            if (IsOwner || !_hasRemoteTarget) return;
            transform.GetPositionAndRotation(out Vector3 currentPos, out Quaternion currentRot);
            float t = Time.deltaTime * 12f;
            transform.SetPositionAndRotation(
                Vector3.Lerp(currentPos, _targetPosition, t),
                Quaternion.Slerp(currentRot, _targetRotation, t));
            float scale = Mathf.Lerp(transform.localScale.x, _targetScale, t);
            transform.localScale = new Vector3(scale, scale, scale);
        }

        public void SetRemoteTarget(Vector3 position, Quaternion rotation, float scale)
        {
            _targetPosition = position;
            _targetRotation = rotation;
            _targetScale = scale;
            if (!_hasRemoteTarget)
            {
                transform.SetPositionAndRotation(position, rotation);
                transform.localScale = new Vector3(scale, scale, scale);
                _hasRemoteTarget = true;
            }
        }

        public void OnHidePressed()
        {
            _hidden = !_hidden;
            if (_frontRenderer != null) _frontRenderer.enabled = !_hidden;
            if (HideLabel != null) HideLabel.text = _hidden ? "Show" : "Hide";
        }

        public void OnSavePressed()
        {
            if (_cleanPng == null || _cleanPng.Length == 0) return;
            try
            {
                string folder = SaveFolder();
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, BasisImageSecurity.GenerateSafeFileName());
                File.WriteAllBytes(path, _cleanPng);
                BasisDebug.Log($"Image pickup saved to {path}");
            }
            catch (Exception e)
            {
                BasisDebug.LogError($"Image pickup save failed: {e.Message}");
            }
        }

        public void OnDeletePressed()
        {
            if (_manager != null) _manager.RequestDespawn(ImageId);
        }

        private static string SaveFolder()
        {
#if UNITY_STANDALONE_WIN
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Basis");
#else
            return Path.Combine(Application.persistentDataPath, "Basis");
#endif
        }

        private static string ShortId(Guid id) => id.ToString("N").Substring(0, 8);

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
            if (_backMaterial != null) Destroy(_backMaterial);
            if (_texture != null) Destroy(_texture);
        }
    }
}
