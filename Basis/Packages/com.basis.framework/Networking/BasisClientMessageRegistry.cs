using Basis.Network.Core;
using System.Collections.Concurrent;
using System.Collections.Generic;

public delegate void BasisClientMessageHandler(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod);

/// <summary>
/// Table-driven inbound dispatch for the client. Core messages bind to their dedicated
/// channel (0-59); multiplexed plugin messages bind to a ushort id read from the 61-63
/// channel payload. Lets handlers be added or removed without editing a shared constant table.
/// </summary>
public static class BasisClientMessageRegistry
{
    private static readonly BasisClientMessageHandler[] CoreHandlers = new BasisClientMessageHandler[BasisNetworkCommons.TotalChannels];
    private static readonly ConcurrentDictionary<ushort, BasisClientMessageHandler> PluginHandlers = new();

    /// <summary>The descriptors from the most recent server Supply, for introspection / plugin binding.</summary>
    public static SerializableBasis.BasisMessageDescriptor[] LastSupply { get; private set; }

    public static void RegisterCore(byte channel, BasisClientMessageHandler handler) => CoreHandlers[channel] = handler;

    public static BasisClientMessageHandler ResolveCore(byte channel) => CoreHandlers[channel];

    /// <summary>Bind a multiplexed plugin message id (carried on channels 61-63) to a handler.</summary>
    public static void RegisterPlugin(ushort id, BasisClientMessageHandler handler) => PluginHandlers[id] = handler;

    /// <summary>Remove a plugin message handler. Returns true if one was bound.</summary>
    public static bool UnregisterPlugin(ushort id) => PluginHandlers.TryRemove(id, out _);

    /// <summary>
    /// Reads the leading ushort message id from a plugin channel payload and dispatches it.
    /// Returns false (leaving the caller to recycle and log) when the id is unknown or the
    /// payload is too short to carry an id.
    /// </summary>
    public static bool DispatchPlugin(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
    {
        if (!reader.TryGetUShort(out ushort id))
        {
            return false;
        }
        if (PluginHandlers.TryGetValue(id, out BasisClientMessageHandler handler))
        {
            handler(peer, reader, channel, deliveryMethod);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Apply a server Supply manifest: record it, then reply with the message ids this client
    /// can actually handle (core ids with a bound handler, plus plugin ids it has registered).
    /// </summary>
    public static void ApplySupply(SerializableBasis.BasisMessageSupply supply, NetPeer peer)
    {
        LastSupply = supply.Descriptors ?? System.Array.Empty<SerializableBasis.BasisMessageDescriptor>();

        List<ushort> handled = new List<ushort>(LastSupply.Length);
        foreach (SerializableBasis.BasisMessageDescriptor descriptor in LastSupply)
        {
            bool canHandle = BasisNetworkCommons.IsPluginChannel(descriptor.Channel)
                ? PluginHandlers.ContainsKey(descriptor.Id)
                : ResolveCore(descriptor.Channel) != null;
            if (canHandle)
            {
                handled.Add(descriptor.Id);
            }
        }

        SerializableBasis.BasisMessageSubscribe subscribe = new SerializableBasis.BasisMessageSubscribe { Ids = handled.ToArray() };
        NetDataWriter writer = new NetDataWriter();
        writer.Put(BasisNetworkCommons.RegistrySub_Subscribe);
        subscribe.Serialize(writer);
        peer.Send(writer, BasisNetworkCommons.RegistryControlChannel, DeliveryMethod.ReliableOrdered);
    }
}
