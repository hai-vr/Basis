using Basis.Network.Core;

using static Basis.Network.Core.Serializable.SerializableBasis;
using static SerializableBasis;
public class NetworkClient
{
    public  NetManager client;
    public EventBasedNetListener listener;
    private NetPeer peer;
    private bool IsInUse;
    /// <summary>
    /// initial data is typically the 
    /// </summary> 
    /// <param name="IP"></param>
    /// <param name="port"></param>
    /// <param name="ReadyMessage"></param>
    public NetPeer StartClient(string IP, int port, ReadyMessage ReadyMessage, byte[] AuthenticationMessage, Configuration Configuration, bool manualMode = false)
    {
        if (IsInUse == false)
        {
            listener = new EventBasedNetListener();
            client = BasisNetworkStackRegistry.Create(Configuration.NetworkStackId, listener, Configuration);
            if (manualMode)
                client.StartManual();
            else
                client.Start();
            NetDataWriter Writer = new NetDataWriter(true,12);
            //this is the only time we dont put key!
            Writer.Put(BasisNetworkVersion.ServerVersion);
            BytesMessage AuthBytes = new BytesMessage();
            AuthBytes.Serialize(Writer, AuthenticationMessage);
            ReadyMessage.Serialize(Writer);
            peer = client.Connect(IP, port, Writer);
            IsInUse = true;
            return peer;
        }
        else
        {
            BNL.LogError("Call Shutdown First!");
            return null;
        }
    }
    public void Poll()
    {
        client?.PollEvents();
    }
    public void Update(float elapsedMilliseconds)
    {
        client?.ManualUpdate(elapsedMilliseconds);
    }
    public void Disconnect()
    {
        IsInUse = false;
        BNL.Log("Client Called Disconnect from server");
        peer?.Disconnect();
        client?.Stop();

        BNL.Log("Worker thread stopped.");
    }
}
