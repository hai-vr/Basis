using Basis.Scripts.Networking.NetworkedAvatar;
using UnityEngine;

public class BasisUnInitializedPlayer : BasisNetworkPlayer
{
    public BasisUnInitializedPlayer(ushort PlayerID)
    {
        playerId = PlayerID;
        hasID = true;
    }
    public override void DeInitialize()
    {
    }

    public override void Initialize()
    {

    }
}
