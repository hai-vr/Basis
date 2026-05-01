using System;
using System.Collections.Generic;
using System.Linq;
using Basis.Network.Core;
using HVR.Basis.Comms.HVRUtility;
using UnityEngine;

namespace HVR.Basis.Comms
{
    public class HVRVariableState : MonoBehaviour, IFeatureReceiver
    {
        public HVRAvatarComms comms;
        public bool isWearer;
        public IHVRTransmitter transmitter;

        private IHVRVariableBehaviour _behaviour;

        private void Awake() => _behaviour = isWearer ? new HVRVariableState_Wearer(this) : new HVRVariableState_Remote(this);
        public void Update() => _behaviour.Update();

        public void RequireVariable(HVRVariable variable) => _behaviour.RequireVariable(variable);

        public void OnPacketReceived(byte localIdentifier, ArraySegment<byte> data) => _behaviour.OnPacketReceived(localIdentifier, data);
        public void OnResyncEveryoneRequested() => _behaviour.OnResyncEveryoneRequested();
        public void OnResyncRequested(ushort[] whoAsked) => _behaviour.OnResyncRequested(whoAsked);

        private void WhenAddressUpdated(int addressId, float currentValue) => comms.DataProvider.Submit(addressId, currentValue);

        private interface IHVRVariableBehaviour : IFeatureReceiver
        {
            public void Awake();
            public void Update();
            void RequireVariable(HVRVariable variable);
        }

        private class HVRVariableState_Wearer : IHVRVariableBehaviour
        {
            private readonly HVRVariableState _state;
            private readonly AcquisitionService _acquisitionService;

            private readonly Dictionary<int, HVRVariableHolder> _addressIdToHolder = new();
            private readonly List<int> _newVariablesAddressIds = new();
            private readonly HashSet<int> _addressIdsWithNewValue = new();
            private ushort _networkId = 0;

            public HVRVariableState_Wearer(HVRVariableState state)
            {
                _state = state;
                _acquisitionService = AcquisitionService.SceneInstance;
            }

            public void Awake()
            {
            }

            public void OnPacketReceived(byte localIdentifier, ArraySegment<byte> data) { } // Not applicable

            public void OnResyncEveryoneRequested()
            {
                var packet = BuildNewVariablesPacket(_addressIdToHolder.Keys.ToList());
                _state.transmitter.NetworkMessageSend(packet, DeliveryMethod.ReliableSequenced);
            }

            public void OnResyncRequested(ushort[] whoAsked)
            {
                var packet = BuildNewVariablesPacket(_addressIdToHolder.Keys.ToList());
                _state.transmitter.NetworkMessageSend(packet, DeliveryMethod.ReliableSequenced, whoAsked);
            }

            public void RequireVariable(HVRVariable variable)
            {
                if (_addressIdToHolder.ContainsKey(variable.addressId)) return;

                if (variable.variableTypeCode == HVRVariableTypeCode.Float)
                {
                    if (variable.initialValue is not float) throw new InvalidOperationException("Initial value does not match variable type code.");
                }
                else
                {
                    throw new InvalidOperationException($"Unsupported variable type code {variable.variableTypeCode}.");
                }

                _acquisitionService.RegisterAddresses(new []{ variable.addressId }, OnAddressUpdated);

                _addressIdToHolder.Add(variable.addressId, new HVRVariableHolder
                {
                    variable = variable,
                    networkId = ++_networkId,
                    currentValue = variable.initialValue,
                    lastTransmittedValue = variable.initialValue,
                    valueWithGreatestDeltaSinceLastTransmittedValue = variable.initialValue
                });

                _newVariablesAddressIds.Add(variable.addressId);
            }

            private void OnAddressUpdated(int addressId, float value)
            {
                if (_addressIdToHolder.TryGetValue(addressId, out var holder))
                {
                    if (holder.variable.variableTypeCode == HVRVariableTypeCode.Float && !Mathf.Approximately((float)holder.currentValue, value))
                    {
                        _addressIdsWithNewValue.Add(addressId);

                        holder.currentValue = value;
                        if (Mathf.Abs((float)holder.lastTransmittedValue - value) > Mathf.Abs((float)holder.lastTransmittedValue - (float)holder.valueWithGreatestDeltaSinceLastTransmittedValue))
                        {
                            holder.valueWithGreatestDeltaSinceLastTransmittedValue = value;
                        }
                    }
                }
            }

            public void Update()
            {
                if (_newVariablesAddressIds.Count > 0)
                {
                    var packet = BuildNewVariablesPacket(_newVariablesAddressIds);
                    _state.transmitter.NetworkMessageSend(packet, DeliveryMethod.ReliableSequenced);

                    _newVariablesAddressIds.Clear();
                }

                DoTick();
            }

            private void DoTick()
            {
                if (_addressIdsWithNewValue.Count == 0) return;

                var addressIdsThatNeedToBeResentLater = new HashSet<int>();

                var addressIdsToValueToTransmit = new Dictionary<int, object>();
                foreach (var addressId in _addressIdsWithNewValue)
                {
                    var holder = _addressIdToHolder[addressId];
                    var currentValue = holder.currentValue;

                    // Reminder: We network the value with the greatest delta, which is not necessarily the current value.
                    // Networking the value with the greatest delta helps networking short-lived events such as the eyes blinking.
                    var valueToBeTransmitted = holder.valueWithGreatestDeltaSinceLastTransmittedValue;
                    addressIdsToValueToTransmit.Add(addressId, valueToBeTransmitted);

                    holder.lastTransmittedValue = valueToBeTransmitted;
                    holder.valueWithGreatestDeltaSinceLastTransmittedValue = currentValue;

                    if (holder.variable.variableTypeCode == HVRVariableTypeCode.Float && !Mathf.Approximately((float)currentValue, (float)valueToBeTransmitted))
                    {
                        // If the value with the greatest delta is not the current value, then we need to transmit the current value
                        // (which is now stored inside valueWithGreatestDeltaSinceLastTransmittedValue) next frame.
                        addressIdsThatNeedToBeResentLater.Add(addressId);
                    }
                }

                if (addressIdsToValueToTransmit.Count > 0)
                {
                    var packet = BuildUpdatedVariablesPacket(addressIdsToValueToTransmit);
                    _state.transmitter.NetworkMessageSend(packet, DeliveryMethod.ReliableSequenced);
                }

                _addressIdsWithNewValue.Clear();
                _addressIdsWithNewValue.UnionWith(addressIdsThatNeedToBeResentLater);
            }

            private byte[] BuildUpdatedVariablesPacket(Dictionary<int, object> addressIdsToValueToTransmit)
            {
                var zeroesNetworkIds = new List<ushort>();
                var onesNetworkIds = new List<ushort>();
                var otherAddressIds = new List<int>();

                foreach (var (addressId, value) in addressIdsToValueToTransmit)
                {
                    var networkId = _addressIdToHolder[addressId].networkId;
                    if (value is float f)
                    {
                        if (Mathf.Approximately(f, 0f))
                        {
                            zeroesNetworkIds.Add(networkId);
                        }
                        else if (Mathf.Approximately(f, 1f))
                        {
                            onesNetworkIds.Add(networkId);
                        }
                        else
                        {
                            otherAddressIds.Add(addressId);
                        }
                    }
                    else
                    {
                        otherAddressIds.Add(addressId);
                    }
                }

                if (otherAddressIds.Count > 0)
                {
                    return new HVR_VariableState_UpdatedVariables_Mixed
                    {
                        numberOfZeroes = (ushort)zeroesNetworkIds.Count,
                        networkIds = zeroesNetworkIds.Concat(onesNetworkIds).ToList(),
                        other = otherAddressIds.Select(addressId => new HVR_VariableState_UpdatedVariables_Mixed.HVR_VariableState_UpdatedValue
                        {
                            networkId = _addressIdToHolder[addressId].networkId,
                            value = addressIdsToValueToTransmit[addressId]
                        }).ToList()
                    }.Serialize();
                }

                if (zeroesNetworkIds.Count > 0 && onesNetworkIds.Count > 0)
                {
                    return new HVR_VariableState_UpdatedVariables_ZeroesAndOnes
                    {
                        numberOfZeroes = (ushort)zeroesNetworkIds.Count,
                        networkIds = zeroesNetworkIds.Concat(onesNetworkIds).ToList()
                    }.Serialize();
                }

                return new HVR_VariableState_UpdatedVariables_ZeroesOrOnes
                {
                    packetType = zeroesNetworkIds.Count > 0 ? AvatarMessageProcessing.NewNet_WearerSubmitsUpdatedVariables_Zeroes : AvatarMessageProcessing.NewNet_WearerSubmitsUpdatedVariables_Ones,
                    networkIds = zeroesNetworkIds
                }.Serialize();
            }

            private byte[] BuildNewVariablesPacket(List<int> newVariablesAddressIds)
            {
                var allHolders = newVariablesAddressIds
                    .Select(addressId => _addressIdToHolder[addressId])
                    .ToList();

                var other = new List<HVR_VariableState_NewVariables.HVR_VariableState_NewVariable>();
                var zeroes = new List<HVR_VariableState_NewVariables.HVR_VariableState_NewQuickVariable>();
                var ones = new List<HVR_VariableState_NewVariables.HVR_VariableState_NewQuickVariable>();

                foreach (var holder in allHolders)
                {
                    var isFloat = holder.variable.variableTypeCode == HVRVariableTypeCode.Float;
                    if (isFloat
                        && (Mathf.Approximately((float)holder.variable.initialValue, 0f)
                            || Mathf.Approximately((float)holder.variable.initialValue, 1f)))
                    {
                        var quickVar = new HVR_VariableState_NewVariables.HVR_VariableState_NewQuickVariable
                        {
                            address = HVRAddressRegistry.ResolveKnownAddressFromId(holder.variable.addressId),
                            networkId = holder.networkId,
                        };
                        (Mathf.Approximately((float)holder.variable.initialValue, 1f) ? ones : zeroes)
                            .Add(quickVar);
                    }
                    else
                    {
                        other.Add(new HVR_VariableState_NewVariables.HVR_VariableState_NewVariable
                        {
                            address = HVRAddressRegistry.ResolveKnownAddressFromId(holder.variable.addressId),
                            networkId = holder.networkId,
                            variableTypeCode = (byte)holder.variable.variableTypeCode,
                            initialValue = holder.currentValue
                        });
                    }
                }

                return new HVR_VariableState_NewVariables
                {
                    newGeneralVariables = other,
                    floatZero = zeroes,
                    floatOne = ones,
                }.Serialize();
            }

            private class HVRVariableHolder
            {
                public HVRVariable variable;
                public ushort networkId;
                public object currentValue;
                public object lastTransmittedValue;
                public object valueWithGreatestDeltaSinceLastTransmittedValue;
            }
        }

        private class HVRVariableState_Remote : IHVRVariableBehaviour
        {
            private readonly HVRVariableState _state;
            private readonly Dictionary<int, HVRVariableHolder> _addressIdToHolder = new();
            private readonly Dictionary<ushort, int> _networkIdToAddressId = new();

            public HVRVariableState_Remote(HVRVariableState state)
            {
                _state = state;
            }

            public void Awake()
            {
                _state.transmitter.NetworkMessageSend(new[] { AvatarMessageProcessing.NewNet_RemoteRequestsInitialization }, DeliveryMethod.ReliableSequenced);
            }

            public void Update()
            {
            }

            public void OnPacketReceived(byte localIdentifier, ArraySegment<byte> data)
            {
                if (data.Count < 1) { HVRLogging.ProtocolError("Data buffer is empty."); return; }

                var packetType = data[0];
                switch (packetType)
                {
                    case AvatarMessageProcessing.NewNet_WearerSubmitsNewVariables:
                    {
                        var packet = HVR_VariableState_NewVariables.Deserialize(data);
                        if (packet == null) { HVRLogging.ProtocolError("Failed to deserialize NewVariables packet."); return; }

                        WhenNewVariablesReceived(packet);
                        break;
                    }
                    case AvatarMessageProcessing.NewNet_WearerSubmitsUpdatedVariables_Zeroes:
                    {
                        var packet = HVR_VariableState_UpdatedVariables_ZeroesOrOnes.Deserialize(data, packetType);
                        if (packet == null) { HVRLogging.ProtocolError("Failed to deserialize UpdatedVariables_Zeroes packet."); return; }

                        foreach (var networkId in packet.networkIds)
                        {
                            if (_networkIdToAddressId.TryGetValue(networkId, out var addressId))
                            {
                                _state.WhenAddressUpdated(_networkIdToAddressId[networkId], 0f);
                            }
                        }

                        break;
                    }
                    case AvatarMessageProcessing.NewNet_WearerSubmitsUpdatedVariables_Ones:
                    {
                        var packet = HVR_VariableState_UpdatedVariables_ZeroesOrOnes.Deserialize(data, packetType);
                        if (packet == null) { HVRLogging.ProtocolError("Failed to deserialize UpdatedVariables_Ones packet."); return; }

                        foreach (var networkId in packet.networkIds)
                        {
                            if (_networkIdToAddressId.TryGetValue(networkId, out var addressId))
                            {
                                _state.WhenAddressUpdated(addressId, 1f);
                            }
                        }

                        break;
                    }
                    case AvatarMessageProcessing.NewNet_WearerSubmitsUpdatedVariables_ZeroesAndOnes:
                    {
                        var packet = HVR_VariableState_UpdatedVariables_ZeroesAndOnes.Deserialize(data);
                        if (packet == null) { HVRLogging.ProtocolError("Failed to deserialize UpdatedVariables_ZeroesAndOnes packet."); return; }

                        for (var index = 0; index < packet.networkIds.Count; index++)
                        {
                            if (_networkIdToAddressId.TryGetValue(packet.networkIds[index], out var addressId))
                            {
                                var isZero = index < packet.numberOfZeroes;
                                _state.WhenAddressUpdated(addressId, isZero ? 0f : 1f);
                            }
                        }

                        break;
                    }
                    case AvatarMessageProcessing.NewNet_WearerSubmitsUpdatedVariables_Mixed:
                    {
                        var packet = HVR_VariableState_UpdatedVariables_Mixed.Deserialize(data);
                        if (packet == null) { HVRLogging.ProtocolError("Failed to deserialize UpdatedVariables_Mixed packet."); return; }

                        for (var index = 0; index < packet.networkIds.Count; index++)
                        {
                            if (_networkIdToAddressId.TryGetValue(packet.networkIds[index], out var addressId))
                            {
                                var isZero = index < packet.numberOfZeroes;
                                _state.WhenAddressUpdated(addressId, isZero ? 0f : 1f);
                            }
                        }
                        foreach (var other in packet.other)
                        {
                            if (_networkIdToAddressId.TryGetValue(other.networkId, out var addressId))
                            {
                                if (_addressIdToHolder[addressId].variable.variableTypeCode == HVRVariableTypeCode.Float && other.value is float f)
                                {
                                    _state.WhenAddressUpdated(addressId, f);
                                }
                            }
                        }

                        break;
                    }
                    default:
                        HVRLogging.ProtocolError($"Unknown packet type {packetType}.");
                        break;
                }
            }

            public void OnResyncEveryoneRequested() { } // Not applicable
            public void OnResyncRequested(ushort[] whoAsked) { } // Not applicable

            private void WhenNewVariablesReceived(HVR_VariableState_NewVariables packet)
            {
                var newlyAddedAddresses = new List<int>();

                foreach (var variable in packet.newGeneralVariables)
                {
                    var addressId = HVRAddressRegistry.AddressToId(variable.address);
                    _addressIdToHolder[addressId] = new HVRVariableHolder
                    {
                        variable = new HVRVariable
                        {
                            addressId = addressId,
                            variableTypeCode = (HVRVariableTypeCode)variable.variableTypeCode,
                            initialValue = (float)variable.initialValue
                        },
                        networkId = variable.networkId,
                        currentValue = (float)variable.initialValue
                    };

                    _networkIdToAddressId[variable.networkId] = addressId;
                    newlyAddedAddresses.Add(addressId);
                }

                foreach (var variable in packet.floatZero)
                {
                    var addressId = HVRAddressRegistry.AddressToId(variable.address);
                    _addressIdToHolder[addressId] = new HVRVariableHolder
                    {
                        variable = new HVRVariable
                        {
                            addressId = addressId,
                            variableTypeCode = HVRVariableTypeCode.Float,
                            initialValue = 0f
                        },
                        networkId = variable.networkId,
                        currentValue = 0f
                    };

                    _networkIdToAddressId[variable.networkId] = addressId;
                    newlyAddedAddresses.Add(addressId);
                }

                foreach (var variable in packet.floatOne)
                {
                    var addressId = HVRAddressRegistry.AddressToId(variable.address);
                    _addressIdToHolder[addressId] = new HVRVariableHolder
                    {
                        variable = new HVRVariable
                        {
                            addressId = addressId,
                            variableTypeCode = HVRVariableTypeCode.Float,
                            initialValue = 1f
                        },
                        networkId = variable.networkId,
                        currentValue = 1f
                    };

                    _networkIdToAddressId[variable.networkId] = addressId;
                    newlyAddedAddresses.Add(addressId);
                }

                foreach (var newlyAddedAddress in newlyAddedAddresses)
                {
                    _state.WhenAddressUpdated(newlyAddedAddress, (float)_addressIdToHolder[newlyAddedAddress].currentValue);
                }
            }

            public void RequireVariable(HVRVariable variable)
            {
                // Do nothing.
            }

            private class HVRVariableHolder
            {
                public HVRVariable variable;
                public ushort networkId;
                public object currentValue;
            }
        }
    }

    public enum HVRVariableTypeCode
    {
        Float = 1,
    }

    public class HVRVariable
    {
        public int addressId;
        public object initialValue;
        public HVRVariableTypeCode variableTypeCode;

        // If float:
        public float min;
        public float max;
    }
}
