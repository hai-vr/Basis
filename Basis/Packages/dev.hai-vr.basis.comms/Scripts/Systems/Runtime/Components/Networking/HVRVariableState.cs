using System;
using System.Collections.Generic;
using System.Text;
using Basis.Network.Core;
using HVR.Basis.Comms.HVRUtility;
using UnityEngine;

namespace HVR.Basis.Comms
{
    public class HVRVariableState : MonoBehaviour
    {
        public bool isWearer;
        public ushort wearerNetId;
        public IHVRTransmitter transmitter;

        private readonly List<int> _declaredAddressesInOrder = new();

        private IHVRVariableBehaviour _behaviour;

        public void RequireVariable(HVRVariable variable)
        {
            if (isWearer)
            {
                _behaviour.RequireVariable(variable);
            }
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

                    _state._declaredAddressesInOrder.AddRange(_newVariablesAddressIds);
                    _newVariablesAddressIds.Clear();
                }

                DoTick();
            }

            private void DoTick()
            {
                if (_addressIdsWithNewValue.Count == 0) return;

                var addressIdsThatNeedToBeResentLater = new HashSet<int>();

                var valuesToTransmit = new Dictionary<int, object>();
                foreach (var addressId in _addressIdsWithNewValue)
                {
                    var holder = _addressIdToHolder[addressId];
                    var currentValue = holder.currentValue;

                    // Reminder: We network the value with the greatest delta, which is not necessarily the current value.
                    // Networking the value with the greatest delta helps networking short-lived events such as the eyes blinking.
                    var valueToBeTransmitted = holder.valueWithGreatestDeltaSinceLastTransmittedValue;
                    valuesToTransmit.Add(addressId, valueToBeTransmitted);

                    holder.lastTransmittedValue = valueToBeTransmitted;
                    holder.valueWithGreatestDeltaSinceLastTransmittedValue = currentValue;

                    if (holder.variable.variableTypeCode == HVRVariableTypeCode.Float && !Mathf.Approximately((float)currentValue, (float)valueToBeTransmitted))
                    {
                        // If the value with the greatest delta is not the current value, then we need to transmit the current value
                        // (which is now stored inside valueWithGreatestDeltaSinceLastTransmittedValue) next frame.
                        addressIdsThatNeedToBeResentLater.Add(addressId);
                    }
                }

                BuildUpdatedVariablesPacket(valuesToTransmit);

                _addressIdsWithNewValue.Clear();
                _addressIdsWithNewValue.UnionWith(addressIdsThatNeedToBeResentLater);
            }

            private void BuildUpdatedVariablesPacket(Dictionary<int, object> valuesToTransmit)
            {
                var packetType = AvatarMessageProcessing.NewNet_WearerSubmitsUpdatedVariables;
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
                var packetType = AvatarMessageProcessing.NewNet_WearerSubmitsNewVariables;

                var totalLength = 1 + 2; // packetType + count
                var addressBytesList = new List<byte[]>(_newVariablesAddressIds.Count);
                foreach (var newVariableAddressId in _newVariablesAddressIds)
                {
                    var address = HVRAddressRegistry.ResolveKnownAddressFromId(newVariableAddressId);
                    var addressBytes = Encoding.UTF8.GetBytes(address);
                    addressBytesList.Add(addressBytes);

                    totalLength += 2 + addressBytes.Length + 1 + 4;
                }

                var result = new byte[totalLength];
                result[0] = packetType;
                var count = (ushort)_newVariablesAddressIds.Count;
                result[1] = (byte)(count & 0xFF);
                result[2] = (byte)((count >> 8) & 0xFF);
                var offset = 3;

                for (var i = 0; i < _newVariablesAddressIds.Count; i++)
                {
                    var newVariableAddressId = _newVariablesAddressIds[i];
                    var holder = _addressIdToHolder[newVariableAddressId];
                    var addressBytes = addressBytesList[i];

                    var m0_addressLength = (ushort)addressBytes.Length;
                    var m1_addressBytes = addressBytes;
                    var m2_variableTypeCode = (byte)holder.variable.variableTypeCode;
                    var m3_initialValue = (float)holder.currentValue;

                    // Address length (ushort - 2 bytes)
                    result[offset++] = (byte)(m0_addressLength & 0xFF);
                    result[offset++] = (byte)((m0_addressLength >> 8) & 0xFF);

                    // Address bytes
                    Buffer.BlockCopy(m1_addressBytes, 0, result, offset, m1_addressBytes.Length);
                    offset += m1_addressBytes.Length;

                    // Variable type code (byte - 1 byte)
                    result[offset++] = m2_variableTypeCode;

                    // Initial value (float - 4 bytes)
                    // (Assuming we are in a Little Endian environment as common for Unity platforms)
                    var valueBytes = BitConverter.GetBytes(m3_initialValue);
                    Buffer.BlockCopy(valueBytes, 0, result, offset, 4);
                    offset += 4;
                }

                return result;
            }
        }

        private class HVRVariableState_Remote : IHVRVariableBehaviour
        {
            private readonly HVRVariableState _state;

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

                    if (unsafeBuffer.Length < 3) { HVRLogging.ProtocolError("Unexpected end of packet (count)."); return; }
                    var count = (ushort)(unsafeBuffer[1] | (unsafeBuffer[2] << 8));
                    var offset = 3;
                    for (var i = 0; i < count; i++)
                    {
                        if (offset + 2 > unsafeBuffer.Length) { HVRLogging.ProtocolError("Unexpected end of packet (address length)."); return; }
                        var addressLength = (ushort)(unsafeBuffer[offset] | (unsafeBuffer[offset + 1] << 8));
                        offset += 2;

                        if (offset + addressLength > unsafeBuffer.Length) { HVRLogging.ProtocolError("Unexpected end of packet (address)."); return; }
                        var address = Encoding.UTF8.GetString(unsafeBuffer, offset, addressLength);
                        offset += addressLength;

                        if (offset + 1 > unsafeBuffer.Length) { HVRLogging.ProtocolError("Unexpected end of packet (variable type)."); return; }
                        var variableTypeCode = (HVRVariableTypeCode)unsafeBuffer[offset++];

                        if (offset + 4 > unsafeBuffer.Length) { HVRLogging.ProtocolError("Unexpected end of packet (initial value)."); return; }
                        var initialValue = BitConverter.ToSingle(unsafeBuffer, offset);
                        offset += 4;

                        var addressId = HVRAddressRegistry.AddressToId(address);
                        _state.RequireVariable(new HVRVariable
                        {
                            addressId = addressId,
                            variableTypeCode = variableTypeCode,
                            initialValue = initialValue
                        });

                        _state._declaredAddressesInOrder.Add(addressId);
                    }

                    if (offset != unsafeBuffer.Length)
                    {
                        HVRLogging.ProtocolError("Packet length mismatch.");
                    }
                }
            }

            public void OnNetworkMessageServerReductionSystem(byte[] unsafeBuffer)
            {
                throw new NotImplementedException();
            }

            public void RequireVariable(HVRVariable variable)
            {
                throw new NotImplementedException();
            }
        }
    }

    public enum HVRVariableTypeCode
    {
        Float = 0,
    }

    public class HVRVariableHolder
    {
        public HVRVariable variable;
        public object currentValue;
        public object lastTransmittedValue;
        public object valueWithGreatestDeltaSinceLastTransmittedValue;
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
