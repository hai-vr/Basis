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
        private MeshRenderer _frontRenderer;
        private BasisImagePickupManager _manager;

        private bool _hidden;
        private bool _hasRemoteTarget;
        private Vector3 _targetPosition;
        private Quaternion _targetRotation;

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

            if (front.TryGetComponent(out MeshCollider meshCollider)) Destroy(meshCollider);

            pickup._frontRenderer = front.GetComponent<MeshRenderer>();
            pickup._material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            if (pickup._material.HasProperty(BaseMapId)) pickup._material.SetTexture(BaseMapId, texture);
            else pickup._material.mainTexture = texture;
            if (pickup._material.HasProperty(BaseColorId)) pickup._material.SetColor(BaseColorId, Color.white);
            pickup._frontRenderer.sharedMaterial = pickup._material;

            if (isOwner)
            {
                var box = front.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(1f, 1f, 0.02f);

                var body = root.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;

                var interactable = root.AddComponent<BasisPickupInteractable>();
                interactable.RigidRef = body;
                interactable.GenerateColliderMesh = false;
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
        }

        public void SetRemoteTarget(Vector3 position, Quaternion rotation)
        {
            _targetPosition = position;
            _targetRotation = rotation;
            if (!_hasRemoteTarget)
            {
                transform.SetPositionAndRotation(position, rotation);
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
            if (_texture != null) Destroy(_texture);
        }
    }
}
