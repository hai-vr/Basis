using Basis.Scripts.BasisSdk.Players;
using UnityEngine;
using UnityEngine.Serialization;

public class BasisOverridePlayerIKDestination : MonoBehaviour
{
    [FormerlySerializedAs("Overidenbone")]
    public HumanBodyBones Overriddenbone = HumanBodyBones.Hips;
    public void Update()
    {
        this.transform.GetPositionAndRotation(out Vector3 Position,out Quaternion Rotation);
        BasisLocalPlayer.Instance.LocalRigDriver.SetOverrideData(Overriddenbone, Position, Rotation);
    }
    public void OnEnable()
    {
        BasisLocalPlayer.Instance.LocalRigDriver.SetOverrideUsage(Overriddenbone, true);
    }
    public void OnDisable()
    {
        BasisLocalPlayer.Instance.LocalRigDriver.SetOverrideUsage(Overriddenbone, false);
    }
}
