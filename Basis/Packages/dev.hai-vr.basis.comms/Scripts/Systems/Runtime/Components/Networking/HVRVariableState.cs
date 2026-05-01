using System;
using System.Collections.Generic;
using System.Linq;
using Basis.Network.Core;
using HVR.Basis.Comms.HVRUtility;
using UnityEngine;

namespace HVR.Basis.Comms
{
    public class HVRVariableState : MonoBehaviour
    {
        public HVRAvatarComms comms;
        public bool isWearer;
        public ushort wearerNetId;
        public IHVRTransmitter transmitter;

        private IHVRVariableBehaviour _behaviour;

        public void RequireVariable(HVRVariable variable)
        {
            _behaviour.RequireVariable(variable);
        }

        private void Awake()
        {
            _behaviour = isWearer ? new HVRVariableState_Wearer(this) : new HVRVariableState_Remote(this);
        }

        public void Update()
        {
            _behaviour.Update();
        }

        public virtual void OnNetworkMessageReceived(ushort RemoteUser, byte[] unsafeBuffer, DeliveryMethod DeliveryMethod)
        {
            _behaviour.OnNetworkMessageReceived(RemoteUser, unsafeBuffer, DeliveryMethod);
        }

        public virtual void OnNetworkMessageServerReductionSystem(byte[] unsafeBuffer)
        {
            _behaviour.OnNetworkMessageServerReductionSystem(unsafeBuffer);
        }

        private interface IHVRVariableBehaviour
        {
            public void Awake();
            public void Update();
            void OnNetworkMessageReceived(ushort remoteUser, byte[] unsafeBuffer, DeliveryMethod deliveryMethod);
            void OnNetworkMessageServerReductionSystem(byte[] unsafeBuffer);
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
                    var packet = BuildNewVariablesPacket();
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
                    BuildUpdatedVariablesPacket(addressIdsToValueToTransmit);
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

            public void OnNetworkMessageReceived(ushort remoteUser, byte[] unsafeBuffer, DeliveryMethod deliveryMethod)
            {
                throw new NotImplementedException();
            }

            public void OnNetworkMessageServerReductionSystem(byte[] unsafeBuffer)
            {
                throw new NotImplementedException();
            }

            private byte[] BuildNewVariablesPacket()
            {
                var allHolders = _newVariablesAddressIds
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

            public void OnNetworkMessageReceived(ushort remoteUser, byte[] unsafeBuffer, DeliveryMethod deliveryMethod)
            {
                if (unsafeBuffer.Length < 1) return;

                var packetType = unsafeBuffer[0];
                if (packetType == AvatarMessageProcessing.NewNet_WearerSubmitsNewVariables)
                {
                    if (remoteUser != _state.wearerNetId) { HVRLogging.ProtocolError("Illegal sender."); return; }

                    var packet = HVR_VariableState_NewVariables.Deserialize(unsafeBuffer);
                    if (packet == null) return;

                    WhenNewVariablesReceived(packet);
                }
            }

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

            public void OnNetworkMessageServerReductionSystem(byte[] unsafeBuffer)
            {
                throw new NotImplementedException();
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

        private void WhenAddressUpdated(int addressId, float currentValue)
        {
            comms.DataProvider.Submit(addressId, currentValue);
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
