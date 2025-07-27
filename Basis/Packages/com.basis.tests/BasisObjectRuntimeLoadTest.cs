using Basis.Scripts.BasisSdk.Players;
using UnityEngine;
using static SerializableBasis;
public class BasisObjectRuntimeLoadTest : MonoBehaviour
{
    [Header("Load Settings")]
    public string Password;
    public string MetaUrl;
    public bool IsScene = false;
    public bool IsPersistent = false;
    [Header("Transform Settings")]
    public bool OverrideSpawnPosition = false;
    public Vector3 Position;
    public bool ModifyScale = false;
    private LocalLoadResource loadedResource;
    public Quaternion Rotation;
    public Vector3 Scale = Vector3.one;
    private void OnEnable()
    {
        Position = OverrideSpawnPosition ? transform.position : BasisLocalPlayer.Instance.transform.position;
        if (IsScene)
        {
            BasisNetworkSpawnItem.RequestSceneLoad(Password, MetaUrl, IsPersistent, out loadedResource);
        }
        else
        {
            BasisNetworkSpawnItem.RequestGameObjectLoad(Password, MetaUrl, Position, Rotation, Scale, IsPersistent, ModifyScale, out loadedResource);
        }
    }
    private void OnDisable()
    {
        if (IsScene)
        {
            BasisNetworkSpawnItem.RequestSceneUnLoad(loadedResource.LoadedNetID);
        }
        else
        {
            BasisNetworkSpawnItem.RequestGameObjectUnLoad(loadedResource.LoadedNetID);
        }
    }
}
