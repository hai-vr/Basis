using Basis.Network.Core;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using static SerializableBasis;

public static class BasisNetworkResourceManagement
{
    public static ConcurrentDictionary<string, LocalLoadResource> UshortNetworkDatabase = new ConcurrentDictionary<string, LocalLoadResource>();
    public static void Reset()
    {
        LocalLoadResource[] resourceArray = UshortNetworkDatabase.Values.ToArray();
        int length = resourceArray.Length;

        for (int index = 0; index < length; index++)
        {
            LocalLoadResource llr = resourceArray[index];

            if (!llr.Persist)
            {
                // Prepare and send the unload resource message
                UnLoadResource unloadResource = new UnLoadResource
                {
                    Mode = llr.Mode,
                    LoadedNetID = llr.LoadedNetID
                };

                NetDataWriter writer = NetworkServer.RentWriter();
                unloadResource.Serialize(writer);
                NetworkServer.BroadcastMessageToClients(
                    writer,
                    BasisNetworkCommons.UnloadResourceChannel,
                    NetworkServer.PeerSnapshot,
                    DeliveryMethod.ReliableOrdered
                );
                NetworkServer.ReturnWriter(writer);

                // Remove the non-persistent resource from the database
                UshortNetworkDatabase.Remove(llr.LoadedNetID,out LocalLoadResource Resource);
            }
        }
    }
    public static void SendOutAllResources(NetPeer NewConnection)
    {
        LocalLoadResource[] Resource = UshortNetworkDatabase.Values.ToArray();
        if (Resource != null)
        {
            int length = Resource.Length;
            NetDataWriter Writer = NetworkServer.RentWriter();
            for (int Index = 0; Index < length; Index++)
            {
                Writer.Reset();
                LocalLoadResource LLR = Resource[Index];
                LLR.Serialize(Writer);
                NetworkServer.TrySend(NewConnection, Writer, BasisNetworkCommons.LoadResourceChannel, DeliveryMethod.ReliableOrdered);
            }
            NetworkServer.ReturnWriter(Writer);
        }
    }
    public static void LoadResource(LocalLoadResource LocalLoadResource)
    {
        if (UshortNetworkDatabase.ContainsKey(LocalLoadResource.LoadedNetID) == false)
        {
            NetDataWriter Writer = NetworkServer.RentWriter();
            LocalLoadResource.Serialize(Writer);
            if (UshortNetworkDatabase.TryAdd(LocalLoadResource.LoadedNetID, LocalLoadResource))
            {
                BNL.Log("Adding Object " + LocalLoadResource.LoadedNetID);
                NetworkServer.BroadcastMessageToClients(Writer, BasisNetworkCommons.LoadResourceChannel, NetworkServer.PeerSnapshot, DeliveryMethod.ReliableOrdered);
            }
            else
            {
                BNL.LogError("Try Add Failed Already have Object Loaded With " + LocalLoadResource.LoadedNetID);
            }
            NetworkServer.ReturnWriter(Writer);
        }
        else
        {
            BNL.LogError("Already have Object Loaded With " + LocalLoadResource.LoadedNetID);
        }
    }
    public static void UnloadResource(UnLoadResource unLoadResource, NetPeer peer)
    {
        if (!UshortNetworkDatabase.TryGetValue(unLoadResource.LoadedNetID, out LocalLoadResource resource))
        {
            BNL.LogError($"Trying to unload an object that does not exist! ID Provided was [{unLoadResource.LoadedNetID}]");
            return;
        }

        // Admin lock validation
        if (resource.IsAdminLocked)
        {
            if (!NetworkServer.AuthIdentity.NetIDToUUID(peer, out string uuid))
            {
                BNL.LogError($"User UUID not found for peer: {peer}");
                return;
            }

            if (!NetworkServer.AuthIdentity.IsNetPeerAdmin(uuid))
            {
                BNL.LogError($"User {uuid} tried to remove admin-only object");
                return;
            }
        }

        // Only remove AFTER validation
        if (!UshortNetworkDatabase.TryRemove(unLoadResource.LoadedNetID, out _))
        {
            BNL.LogError($"Failed to remove object [{unLoadResource.LoadedNetID}] after validation.");
            return;
        }

        NetDataWriter writer = NetworkServer.RentWriter();
        unLoadResource.Serialize(writer);

        BNL.Log("Removing Object " + unLoadResource.LoadedNetID);

        NetworkServer.BroadcastMessageToClients(
            writer,
            BasisNetworkCommons.UnloadResourceChannel,
            NetworkServer.PeerSnapshot,
            DeliveryMethod.ReliableOrdered
        );
        NetworkServer.ReturnWriter(writer);
    }
}
