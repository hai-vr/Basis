using Basis.BasisUI;
using Basis.Scripts.BasisSdk.Interactions;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Device_Management.Devices;
using Basis.Scripts.Networking;
using Basis.Scripts.TransformBinders.BoneControl;
using System;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using static SerializableBasis;

/// <summary>
/// Interactable content share sphere that can be picked up to load content.
/// Follows the BasisAvatarPedestal pattern for interaction and dialogue.
/// </summary>
public class BasisContentSphere : BasisInteractableObject
{
    public string SphereNetID { get; private set; }
    public string ContentURL { get; private set; }
    public string UnlockPassword { get; private set; }
    public ContentShareType ContentType { get; private set; }
    public ushort CreatorPlayerID { get; private set; }

    /// <summary>
    /// Fired when any content sphere is interacted with.
    /// </summary>
    public static Action<BasisContentSphere> OnSphereInteracted;

    private float _bobPhase;
    private Vector3 _restPosition;
    private bool _initialized;
    private bool _isInteracting;
    private CancellationTokenSource _metaLoadCts;
    private TextMeshPro _label;

    public void Initialize(string sphereNetID, string contentURL, string unlockPassword,
        ContentShareType contentType, ushort creatorPlayerID)
    {
        SphereNetID = sphereNetID;
        ContentURL = contentURL;
        UnlockPassword = unlockPassword;
        ContentType = contentType;
        CreatorPlayerID = creatorPlayerID;
        _initialized = true;

        InteractRange = 2f;

        _metaLoadCts = new CancellationTokenSource();
        _ = LoadMetadataImageAsync(_metaLoadCts.Token);

        CreateLabel();
    }

    private void CreateLabel()
    {
        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(transform, false);
        // Sphere scale is 0.3, so local offset needs inverse scale to get world-space height
        labelObj.transform.localPosition = new Vector3(0, 2f, 0);
        labelObj.transform.localRotation = Quaternion.Euler(0, 180, 0);
        // Counter the parent sphere scale so text is readable
        float invScale = 1f / 0.3f;
        labelObj.transform.localScale = Vector3.one * invScale * 0.1f;

        _label = labelObj.AddComponent<TextMeshPro>();
        _label.alignment = TextAlignmentOptions.Center;
        _label.fontSize = 6;
        _label.enableAutoSizing = true;
        _label.fontSizeMin = 3;
        _label.fontSizeMax = 6;
        _label.color = Color.white;
        _label.textWrappingMode = TextWrappingModes.Normal;
        _label.overflowMode = TextOverflowModes.Truncate;

        RectTransform rect = _label.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(10, 3);

        _label.text = GetContentTypeName();
    }

    private void Start()
    {
        _restPosition = transform.position;
        _bobPhase = UnityEngine.Random.value * Mathf.PI * 2f;
    }

    private void Update()
    {
        if (!_initialized) return;

        // Gentle hover/bob animation
        _bobPhase += Time.deltaTime * 1.5f;
        float bobOffset = Mathf.Sin(_bobPhase) * 0.05f;
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null && !rb.isKinematic)
        {
            if (rb.linearVelocity.sqrMagnitude < 0.01f && Time.timeSinceLevelLoad > 1f)
            {
                _restPosition = transform.position;
                rb.isKinematic = true;
            }
        }
        else
        {
            transform.position = _restPosition + Vector3.up * bobOffset;
        }

        // Slow rotation
        transform.Rotate(Vector3.up, 30f * Time.deltaTime, Space.World);
    }

    private async Task LoadMetadataImageAsync(CancellationToken cancellationToken)
    {
        try
        {
            BasisTrackedBundleWrapper wrapper = new BasisTrackedBundleWrapper
            {
                LoadableBundle = ToLoadableBundle()
            };

            BasisProgressReport report = new BasisProgressReport();
            await BasisBeeManagement.HandleMetaOnlyLoad(wrapper, report, cancellationToken);

            if (cancellationToken.IsCancellationRequested || this == null) return;

            if (wrapper.LoadableBundle.BasisBundleConnector.ImageBase64 != null)
            {
                Texture2D texture = BasisTextureCompression.FromPngBytes(wrapper.LoadableBundle.BasisBundleConnector.ImageBase64);

                // Blend 50% type color with 50% texture
                Color typeColor = GetTypeColor();
                Color[] pixels = texture.GetPixels();
                for (int i = 0; i < pixels.Length; i++)
                {
                    pixels[i] = Color.Lerp(typeColor, pixels[i], 0.5f);
                }
                texture.SetPixels(pixels);
                texture.Apply();

                Renderer renderer = GetComponent<Renderer>();
                if (renderer != null && renderer.material != null)
                {
                    renderer.material.mainTexture = texture;
                    renderer.material.color = Color.white;
                    renderer.material.SetTexture("_EmissionMap", texture);
                    renderer.material.SetColor("_EmissionColor", Color.white * 0.5f);
                }

                // Update label with bundle name if available
                string bundleName = wrapper.LoadableBundle.BasisBundleConnector.BasisBundleInformation?.AssetBundleName;
                if (!string.IsNullOrEmpty(bundleName) && _label != null)
                {
                    _label.text = $"{GetContentTypeName()}\n{bundleName}";
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            BasisDebug.LogError($"Failed to load metadata image for content sphere {SphereNetID}: {e.Message}");
        }
    }

    private void OnDestroy()
    {
        _metaLoadCts?.Cancel();
        _metaLoadCts?.Dispose();
    }

    /// <summary>
    /// Constructs a BasisLoadableBundle from this sphere's metadata.
    /// </summary>
    public BasisLoadableBundle ToLoadableBundle()
    {
        return new BasisLoadableBundle
        {
            BasisRemoteBundleEncrypted = new BasisRemoteEncyptedBundle
            {
                RemoteBeeFileLocation = ContentURL
            },
            UnlockPassword = UnlockPassword,
            BasisBundleConnector = new BasisBundleConnector(),
            BasisLocalEncryptedBundle = new BasisStoredEncryptedBundle()
        };
    }

    /// <summary>
    /// Called when the sphere is interacted with. Opens dialogue with load options.
    /// </summary>
    public void WasPressed()
    {
        if (_isInteracting) return;
        _isInteracting = true;

        OnSphereInteracted?.Invoke(this);

        string typeName = GetContentTypeName();
        string title = $"Shared {typeName}";

        // Build description based on content type
        string description;
        switch (ContentType)
        {
            case ContentShareType.Avatar:
                description = $"Load this shared avatar?";
                break;
            case ContentShareType.Prop:
                description = $"Spawn this shared prop?";
                break;
            case ContentShareType.World:
                description = $"Load this shared world?";
                break;
            default:
                description = $"Load this shared content?";
                break;
        }

        BasisMainMenu.Open();
        BasisMainMenu.Instance.OpenDialogue(title, description, "Load", "Delete", value =>
        {
            if (value)
            {
                HandleLoad();
            }
            else
            {
                RequestRemove();
            }
            _isInteracting = false;
        });
    }

    private async void HandleLoad()
    {
        switch (ContentType)
        {
            case ContentShareType.Avatar:
                await LoadAsAvatar();
                break;
            case ContentShareType.Prop:
                LoadAsProp();
                break;
            case ContentShareType.World:
                LoadAsWorld();
                break;
        }
    }

    public async Task LoadAsAvatar()
    {
        BasisDebug.Log($"Loading content sphere as avatar: {ContentURL}", BasisDebug.LogTag.Networking);
        BasisLoadableBundle bundle = ToLoadableBundle();
        await BasisLocalPlayer.Instance.CreateAvatar(0, bundle);
    }

    public void LoadAsProp()
    {
        BasisDebug.Log($"Loading content sphere as prop: {ContentURL}", BasisDebug.LogTag.Networking);

        BasisLocalPlayer.Instance.GetPositionAndRotation(out Vector3 playerPos, out Quaternion playerRot);
        Vector3 spawnPos = playerPos + playerRot * Vector3.forward * 2f + Vector3.up * 0.5f;

        BasisNetworkSpawnItem.RequestGameObjectLoad(
            UnlockPassword,
            ContentURL,
            spawnPos,
            Quaternion.identity,
            Vector3.one,
            false,
            false,
            false,
            out _
        );
    }

    public void LoadAsWorld()
    {
        BasisDebug.Log($"Loading content sphere as world: {ContentURL}", BasisDebug.LogTag.Networking);
        BasisNetworkSpawnItem.RequestSceneLoad(UnlockPassword, ContentURL, false, false, out _);
    }

    public bool IsLocalPlayerCreator()
    {
        if (BasisNetworkConnection.TryGetLocalPlayerID(out ushort localId))
        {
            return localId == CreatorPlayerID;
        }
        return false;
    }

    public void RequestRemove()
    {
        BasisContentShareManager.RequestRemoveSphere(SphereNetID);
    }

    public Color GetTypeColor()
    {
        switch (ContentType)
        {
            case ContentShareType.Avatar: return new Color(0.3f, 0.5f, 1.0f, 1f);
            case ContentShareType.Prop: return new Color(0.3f, 1.0f, 0.5f, 1f);
            case ContentShareType.World: return new Color(1.0f, 0.6f, 0.2f, 1f);
            default: return Color.white;
        }
    }

    public string GetContentTypeName()
    {
        switch (ContentType)
        {
            case ContentShareType.Avatar: return "Avatar";
            case ContentShareType.Prop: return "Prop";
            case ContentShareType.World: return "World";
            default: return "Unknown";
        }
    }

    #region BasisInteractableObject Implementation

    public override bool CanHover(BasisInput input)
    {
        return InteractableEnabled &&
            Inputs.IsInputAdded(input) &&
            input.TryGetRole(out BasisBoneTrackedRole role) &&
            Inputs.TryGetByRole(role, out BasisInputWrapper found) &&
            found.GetState() == BasisInteractInputState.Ignored &&
            IsWithinRange(found.BoneControl.OutgoingWorldData.position, InteractRange);
    }

    public override bool CanInteract(BasisInput input)
    {
        return InteractableEnabled &&
            Inputs.IsInputAdded(input) &&
            input.TryGetRole(out BasisBoneTrackedRole role) &&
            Inputs.TryGetByRole(role, out BasisInputWrapper found) &&
            found.GetState() == BasisInteractInputState.Hovering &&
            IsWithinRange(found.BoneControl.OutgoingWorldData.position, InteractRange);
    }

    public override void OnHoverStart(BasisInput input)
    {
        var found = Inputs.FindExcludeExtras(input);
        if (found != null && found.Value.GetState() != BasisInteractInputState.Ignored)
            BasisDebug.LogWarning("BasisContentSphere input state is not ignored OnHoverStart");
        Inputs.ChangeStateByRole(found.Value.Role, BasisInteractInputState.Hovering);
        OnHoverStartEvent?.Invoke(input);
    }

    public override void OnHoverEnd(BasisInput input, bool willInteract)
    {
        if (input.TryGetRole(out BasisBoneTrackedRole role) && Inputs.TryGetByRole(role, out _))
        {
            if (!willInteract)
            {
                Inputs.ChangeStateByRole(role, BasisInteractInputState.Ignored);
            }
            OnHoverEndEvent?.Invoke(input, willInteract);
        }
    }

    public override void OnInteractStart(BasisInput input)
    {
        if (input.TryGetRole(out BasisBoneTrackedRole role) && Inputs.TryGetByRole(role, out BasisInputWrapper wrapper))
        {
            if (wrapper.GetState() == BasisInteractInputState.Hovering)
            {
                WasPressed();
                OnInteractStartEvent?.Invoke(input);
            }
        }
    }

    public override void OnInteractEnd(BasisInput input)
    {
        if (input.TryGetRole(out BasisBoneTrackedRole role) && Inputs.TryGetByRole(role, out BasisInputWrapper wrapper))
        {
            if (wrapper.GetState() == BasisInteractInputState.Interacting)
            {
                Inputs.ChangeStateByRole(wrapper.Role, BasisInteractInputState.Ignored);
                OnInteractEndEvent?.Invoke(input);
            }
        }
    }

    public override bool IsInteractingWith(BasisInput input)
    {
        var found = Inputs.FindExcludeExtras(input);
        return found.HasValue && found.Value.GetState() == BasisInteractInputState.Interacting;
    }

    public override bool IsHoveredBy(BasisInput input)
    {
        var found = Inputs.FindExcludeExtras(input);
        return found.HasValue && found.Value.GetState() == BasisInteractInputState.Hovering;
    }

    public override void InputUpdate() { }

    public override bool IsInteractTriggered(BasisInput input)
    {
        return HasState(input.CurrentInputState, InputKey);
    }

    #endregion
}
