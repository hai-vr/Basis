using Basis.Network.Core;
using BasisNetworking.InitalData;
using static SerializableBasis;

public static class BasisNetworkServerLibrary
{
    public static void SendLibraryToPeer(NetPeer peer)
    {
        NetDataWriter writer = NetworkServer.RentWriter();
        BuildMessage().Serialize(writer);
        NetworkServer.TrySend(peer, writer, BasisNetworkCommons.ServerLibraryChannel, DeliveryMethod.ReliableOrdered);
        NetworkServer.ReturnWriter(writer);
    }

    public static void BroadcastLibraryToAll()
    {
        NetDataWriter writer = NetworkServer.RentWriter();
        BuildMessage().Serialize(writer);
        NetworkServer.BroadcastMessageToClients(
            writer,
            BasisNetworkCommons.ServerLibraryChannel,
            NetworkServer.PeerSnapshot,
            DeliveryMethod.ReliableOrdered);
        NetworkServer.ReturnWriter(writer);
    }

    private static ServerLibraryMessage BuildMessage()
    {
        var loaded = BasisDefaultLibraryLoader.LoadedItems;
        int count = loaded?.Count ?? 0;

        ServerLibraryMessage message = new ServerLibraryMessage
        {
            Items = new ServerLibraryItem[count]
        };

        for (int i = 0; i < count; i++)
        {
            var src = loaded[i];
            message.Items[i] = new ServerLibraryItem
            {
                Mode = src.Mode,
                Url = src.Url,
                Password = src.Password,
            };
        }

        return message;
    }
}
