using System;
using System.Collections.Generic;
using Basis.Network.Core;
using Basis.Scripts.Networking;

namespace Basis.Shims
{
    /// <summary>
    /// Provides access to the custom data CustomServerData channel of the currently connected server.<br/>
    /// <br/>
    /// The custom server data channels are meant for the server to provide custom live data to props
    /// or other content that requests it, such as a list of players who recently joined the server,
    /// weather forecasts, integration with Discord, etc.<br/>
    /// <br/>
    /// The available capabilities are entirely dependent on the server.<br/>
    /// <br/>
    /// You should call UnsubscribeAll() in your OnDestroy() method.
    /// <br/>
    /// The custom server data channels are NOT designed for props or other content to communicate with each other:
    /// the clients cannot publish messages to the channels; only the server can send messages to the clients.
    /// </summary>
    public class BasisCustomServerDataSubscriberShim
    {
        private readonly Dictionary<string, Guid> _channelPatternToRequestIdDict = new();
        private readonly Dictionary<ushort, string> _idToChannelNameDict = new();
        private readonly Dictionary<Guid, HashSet<string>> _requestIdToChannelNames = new();
        private readonly Dictionary<string, int> _acceptedChannelNames = new();
        private bool _isHooked;

        public delegate void InitialStateReceivedDelegate(string channelName, byte[] data);
        public delegate void MessageReceivedDelegate(string channelName, byte[] data);

        public event InitialStateReceivedDelegate InitialStateReceived;
        public event MessageReceivedDelegate MessageReceived;
        
        ~BasisCustomServerDataSubscriberShim()
        {
            BasisNetworkHandleCustomServerData.OnCustomServerDataMessageReceived -= OnCustomServerDataMessageReceived;
        }
        
        private void OnCustomServerDataMessageReceived(byte[] buffer, DeliveryMethod deliveryMethod)
        {
            try
            {
                if (buffer == null || buffer.Length == 0) return;

                var reader = new NetDataReader(buffer);
                if (!reader.TryGetByte(out byte subType)) return;

                if (subType == BasisNetworkCommons.CustomServerData_ProvideChannelId)
                {
                    var provideId = new SerializableBasis.CustomServerDataProvideChannelId();
                    if (provideId.Deserialize(reader))
                    {
                        _idToChannelNameDict[provideId.ChannelId] = provideId.ChannelName;
                        if (_requestIdToChannelNames.ContainsKey(provideId.RequestID))
                        {
                            if (_requestIdToChannelNames.TryGetValue(provideId.RequestID, out HashSet<string> channelNames))
                            {
                                channelNames.Add(provideId.ChannelName);
                            }
                            else
                            {
                                _requestIdToChannelNames.Add(provideId.RequestID, new HashSet<string> { provideId.ChannelName });
                            }
                            
                            if (_acceptedChannelNames.TryGetValue(provideId.ChannelName, out int count))
                            {
                                _acceptedChannelNames[provideId.ChannelName] = count + 1;
                            }
                            else
                            {
                                _acceptedChannelNames.Add(provideId.ChannelName, 1);
                            }
                        }
                    }
                }
                else if (subType == BasisNetworkCommons.CustomServerData_Message)
                {
                    var msg = new SerializableBasis.CustomServerDataMessage();
                    if (msg.Deserialize(reader))
                    {
                        if (_idToChannelNameDict.TryGetValue(msg.ChannelId, out string channelName) && _acceptedChannelNames.ContainsKey(channelName))
                        {
                            MessageReceived?.Invoke(channelName, msg.Data);
                        }
                    }
                }
                else if (subType == BasisNetworkCommons.CustomServerData_InitialState)
                {
                    var initialState = new SerializableBasis.CustomServerDataInitialState();
                    if (initialState.Deserialize(reader))
                    {
                        if (_idToChannelNameDict.TryGetValue(initialState.ChannelId, out string channelName) && _acceptedChannelNames.ContainsKey(channelName))
                        {
                            InitialStateReceived?.Invoke(channelName, initialState.Data);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                BasisDebug.LogError($"[BasisCustomServerDataShim] Error while processing CustomServerData message, this exception will not be re-thrown: {e}");
                // Do not throw the exception, as we want to avoid Cilbox props disrupting the network message processing.
            }
        }

        /// <summary>
        /// Subscribes to a CustomServerData channel or pattern.<br/>
        /// <br/>
        /// Channels can only be subscribed to once per instance of the shim.<br/>
        /// When subscribing, the InitialStateReceived will trigger if that channel provides an initial state.<br/>
        /// Multiple props can subscribe to the same channel:<br/>
        /// - Each prop may receive a different initial state, depending on when that prop subscribes.<br/>
        /// - Non-initial state messages are sent from the server to the user once, and then dispatched to all the shims that require it.<br/>
        /// <br/>
        /// Patterns only work if the channel name starts with a forward slash (/), otherwise, it is not considered to be a pattern,
        /// and the wildcards will be treated as normal characters.
        /// </summary>
        /// <param name="channelPattern">Name of the CustomServerData channel</param>
        public void Subscribe(string channelPattern)
        {
            if (string.IsNullOrEmpty(channelPattern)) return;
            if (_channelPatternToRequestIdDict.TryGetValue(channelPattern, out _)) return;

            if (!_isHooked)
            {
                _isHooked = true;
                BasisNetworkHandleCustomServerData.OnCustomServerDataMessageReceived += OnCustomServerDataMessageReceived; 
            }

            Guid requestID = Guid.NewGuid();
            _channelPatternToRequestIdDict[channelPattern] = requestID;
            _requestIdToChannelNames[requestID] = new HashSet<string> { channelPattern };

            var request = new SerializableBasis.CustomServerDataSubscribeRequest
            {
                ChannelPattern = channelPattern,
                RequestID = requestID
            };

            NetDataWriter writer = new NetDataWriter();
            writer.Put(BasisNetworkCommons.CustomServerData_Subscribe);
            request.Serialize(writer);

            BasisNetworkConnection.LocalPlayerPeer?.Send(writer, BasisNetworkCommons.CustomServerDataChannel, DeliveryMethod.ReliableOrdered);
        }

        /// <summary>
        /// Unsubscribes from the exact channel or pattern originally subscribed to.<br/>
        /// <br/>
        /// If you have originally subscribed using a pattern, you cannot unsubscribe from its individual channels.
        /// </summary>
        /// <param name="channelPattern"></param>
        public void Unsubscribe(string channelPattern)
        {
            if (string.IsNullOrEmpty(channelPattern)) return;
            if (!_channelPatternToRequestIdDict.TryGetValue(channelPattern, out Guid requestID)) return;

            var request = new SerializableBasis.CustomServerDataUnsubscribeRequest
            {
                ChannelPattern = channelPattern,
                RequestID = requestID
            };

            NetDataWriter writer = new NetDataWriter();
            writer.Put(BasisNetworkCommons.CustomServerData_Unsubscribe);
            request.Serialize(writer);

            BasisNetworkConnection.LocalPlayerPeer?.Send(writer, BasisNetworkCommons.CustomServerDataChannel, DeliveryMethod.ReliableOrdered);
            _channelPatternToRequestIdDict.Remove(channelPattern);
            if (_requestIdToChannelNames.TryGetValue(requestID, out HashSet<string> channelNames))
            {
                foreach (var name in channelNames)
                {
                    if (_acceptedChannelNames.TryGetValue(name, out int count))
                    {
                        if (count <= 1)
                        {
                            _acceptedChannelNames.Remove(name);
                        }
                        else
                        {
                            _acceptedChannelNames[name] = count - 1;
                        }
                    }
                }
                _requestIdToChannelNames.Remove(requestID);
            }
            
            if (_channelPatternToRequestIdDict.Count == 0)
            {
                BasisNetworkHandleCustomServerData.OnCustomServerDataMessageReceived -= OnCustomServerDataMessageReceived;
                _isHooked = false;
            }
        }

        /// <summary>
        /// Unsubscribes from all CustomServerData channels.
        /// </summary>
        public void UnsubscribeAll()
        {
            List<string> channels = new List<string>(_channelPatternToRequestIdDict.Keys);
            foreach (var channel in channels)
            {
                Unsubscribe(channel);
            }

            BasisNetworkHandleCustomServerData.OnCustomServerDataMessageReceived -= OnCustomServerDataMessageReceived;
            _isHooked = false;
        }
    }
}
