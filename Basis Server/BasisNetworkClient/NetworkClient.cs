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
        BNL.Log("Client Called Disconnect from server");
        NotifyServerOfDeparture();
        Shutdown();
        BNL.Log("Worker thread stopped.");
    }
    /// <summary>
    /// Tells the server this client is leaving, and does nothing else.
    ///
    /// <para>Split out from <see cref="Shutdown"/> because the two costs are nothing alike. This
    /// writes one datagram straight to the socket and returns; shutting the transport down closes
    /// the socket and joins its logic thread. Anything stopping a whole population has to get
    /// every one of these out before it starts paying for the teardown, or the last clients are
    /// still queued behind thread joins when the process is killed and the server is left to time
    /// them out one by one.</para>
    /// </summary>
    public void NotifyServerOfDeparture()
    {
        IsInUse = false;
        peer?.Disconnect();
    }
    /// <summary>Closes the socket and joins the transport's threads.</summary>
    public void Shutdown()
    {
        client?.Stop();
    }
}
