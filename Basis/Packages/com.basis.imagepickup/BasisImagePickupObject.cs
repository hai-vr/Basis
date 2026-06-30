using System;
using System.IO;
using Basis.Scripts.BasisSdk.Interactions;
using Basis.Scripts.Device_Management.Devices;
using TMPro;
using UnityEngine;

namespace Basis.ImagePickup
{
    /// <summary>
    /// A spawned image pickup. Front face shows the image on an unlit material; the back carries the
    /// Hide/Save/Delete controls and the spawner label. Any client can grab it; grabbing claims movement
    /// authority and that client broadcasts the transform until someone else grabs it.
    /// </summary>
    public class BasisImagePickupObject : MonoBehaviour
    {
        public Guid ImageId;
        public ushort OwnerId;
        public string OwnerName;
        public bool IsOwner;

        public TextMeshProUGUI HideLabel;
        public TextMeshProUGUI DeleteLabel;

        private Texture2D _texture;
        private byte[] _cleanPng;
        private Material _material;
        private MeshRenderer _frontRenderer;
        private BasisImagePickupManager _manager;

        private bool _hidden;
        private bool _deleteArmed;
        private bool _isController;
        private Rigidbody _body;
        private BasisPickupInteractable _interactable;
        private bool _hasRemoteTarget;
        private Vector3 _targetPosition;
        private Quaternion _targetRotation;
        private float _targetScale = 1f;

        public float LastSendTime;
        public Vector3 LastSentPosition;
        public Quaternion LastSentRotation = Quaternion.identity;
        public float LastSentScale = 1f;
        public bool IsController => _isController;

        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public byte[] CleanPng => _cleanPng;
        public bool IsHidden => _hidden;

        public static BasisImagePickupObject Build(BasisImagePickupManager manager, Guid id, ushort ownerId, string ownerName, bool isOwner, Texture2D texture, byte[] cleanPng, bool cutout, Vector3 position, Quaternion rotation)
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

            var card = new GameObject("Card");
            if (interactableLayer >= 0) card.layer = interactableLayer;
            card.transform.SetParent(root.transform, false);
            card.transform.localScale = new Vector3(panelWidth, panelHeight, 1f);
            card.AddComponent<MeshFilter>().sharedMesh = GetCardMesh();

            pickup._material = new Material(BundledContentHolder.Instance.UnlitUrpShader);
            if (pickup._material.HasProperty(BaseMapId)) pickup._material.SetTexture(BaseMapId, texture);
            else pickup._material.mainTexture = texture;
            if (pickup._material.HasProperty(BaseColorId)) pickup._material.SetColor(BaseColorId, Color.white);
            if (cutout) ConfigureCutout(pickup._material);

            pickup._frontRenderer = card.AddComponent<MeshRenderer>();
            pickup._frontRenderer.sharedMaterials = new[] { pickup._material, GetSharedBackMaterial() };
            pickup._frontRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pickup._frontRenderer.receiveShadows = false;

            pickup._isController = isOwner;

            var box = root.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(panelWidth, panelHeight, 0.02f);

            var body = root.AddComponent<Rigidbody>();
            body.isKinematic = !isOwner;
            body.useGravity = false;
            body.linearDamping = 1.5f;
            body.angularDamping = 2.5f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            pickup._body = body;

            var interactable = root.AddComponent<BasisPickupInteractable>();
            interactable.RigidRef = body;
            interactable.GenerateColliderMesh = false;
            interactable.enableScaleWithGesture = true;
            interactable.minScalePercent = 25f;
            interactable.maxScalePercent = 400f;
            pickup._interactable = interactable;
            interactable.OnInteractStartEvent.AddListener(pickup.OnLocalGrabbed);

            BasisImagePickupBackPanel.Build(root.transform, pickup, panelWidth, panelHeight);
            return pickup;
        }

        private void Update()
        {
            if (_isController || !_hasRemoteTarget) return;
            transform.GetPositionAndRotation(out Vector3 currentPos, out Quaternion currentRot);
            float t = Time.deltaTime * 12f;
            transform.SetPositionAndRotation(
                Vector3.Lerp(currentPos, _targetPosition, t),
                Quaternion.Slerp(currentRot, _targetRotation, t));
            float scale = Mathf.Lerp(transform.localScale.x, _targetScale, t);
            transform.localScale = new Vector3(scale, scale, scale);
        }

        private void OnLocalGrabbed(BasisInput input)
        {
            if (_interactable != null) _interactable._previousKinematicValue = false;
            if (!_isController && _manager != null) _manager.ClaimControl(ImageId);
        }

        public void SetController(bool value)
        {
            _isController = value;
            if (!value)
            {
                transform.GetPositionAndRotation(out _targetPosition, out _targetRotation);
                _targetScale = transform.localScale.x;
                if (_body != null && !_body.isKinematic)
                {
                    _body.linearVelocity = Vector3.zero;
                    _body.angularVelocity = Vector3.zero;
                    _body.isKinematic = true;
                }
            }
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
            if (!_deleteArmed)
            {
                _deleteArmed = true;
                if (DeleteLabel != null) DeleteLabel.text = "Confirm?";
                CancelInvoke(nameof(DisarmDelete));
                Invoke(nameof(DisarmDelete), 3f);
                return;
            }

            CancelInvoke(nameof(DisarmDelete));
            _deleteArmed = false;
            if (_manager != null) _manager.RequestDespawn(ImageId);
        }

        private void DisarmDelete()
        {
            _deleteArmed = false;
            if (DeleteLabel != null) DeleteLabel.text = "Delete";
        }

        private static string SaveFolder()
        {
#if UNITY_STANDALONE_WIN
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Basis");
#else
            return Path.Combine(Application.persistentDataPath, "Basis");
#endif
        }

        private static Mesh _cardMesh;
        private static Material _sharedBackMaterial;

        private static Mesh GetCardMesh()
        {
            if (_cardMesh != null) return _cardMesh;

            var temp = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Mesh quad = temp.GetComponent<MeshFilter>().sharedMesh;
            Vector3[] verts = quad.vertices;
            Vector2[] uv = quad.uv;
            int[] front = quad.triangles;
            DestroyImmediate(temp);

            int[] back = new int[front.Length];
            for (int i = 0; i < front.Length; i += 3)
            {
                back[i] = front[i];
                back[i + 1] = front[i + 2];
                back[i + 2] = front[i + 1];
            }

            _cardMesh = new Mesh { name = "BasisImagePickupCard" };
            _cardMesh.vertices = verts;
            _cardMesh.uv = uv;
            _cardMesh.subMeshCount = 2;
            _cardMesh.SetTriangles(front, 0);
            _cardMesh.SetTriangles(back, 1);
            _cardMesh.RecalculateBounds();
            return _cardMesh;
        }

        private static Material GetSharedBackMaterial()
        {
            if (_sharedBackMaterial != null) return _sharedBackMaterial;
            _sharedBackMaterial = new Material(BundledContentHolder.Instance.UnlitUrpShader) { name = "BasisImagePickupBack" };
            if (_sharedBackMaterial.HasProperty(BaseColorId)) _sharedBackMaterial.SetColor(BaseColorId, Color.white);
            else _sharedBackMaterial.color = Color.white;
            return _sharedBackMaterial;
        }

        private static void ConfigureCutout(Material material)
        {
            material.SetFloat("_Surface", 0f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
            material.SetFloat("_ZWrite", 1f);
            material.SetFloat("_AlphaClip", 1f);
            material.SetFloat("_Cutoff", 0.5f);
            material.EnableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            material.SetOverrideTag("RenderType", "TransparentCutout");
        }

        private static string ShortId(Guid id) => id.ToString("N").Substring(0, 8);

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
            if (_texture != null) Destroy(_texture);
        }
    }
}
