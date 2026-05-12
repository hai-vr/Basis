using System;
using System.Collections.Generic;
using System.Linq;
using Basis.Network.Core;
using HVR.Basis.Comms.HVRUtility;
using UnityEngine;

namespace HVR.Basis.Comms
{
    public class HVRVariableNetworking : MonoBehaviour, IFeatureReceiver
    {
        private const bool PrintDebug = false;

        public HVRAvatarComms comms;
        public bool isWearer;
        public IHVRTransmitter transmitter;

        internal IHVRVariableBehaviour _behaviour;

        private void Awake() => _behaviour = isWearer ? new HVRVariableBehaviour_Wearer(this) : new HVRVariableBehaviour_Remote(this);
        public void Update() => _behaviour.Update();
        private void OnDestroy() => _behaviour.OnDestroy();

        public void RequireVariable(HVRVariable variable) => _behaviour.RequireVariable(variable);

        public void OnPacketReceived(byte localIdentifier, ArraySegment<byte> data) => _behaviour.OnPacketReceived(localIdentifier, data);
        public void OnResyncEveryoneRequested() => _behaviour.OnResyncEveryoneRequested();
        public void OnResyncRequested(ushort[] whoAsked) => _behaviour.OnResyncRequested(whoAsked);

        private const float TransmissionDeltaSeconds = 0.1f;
        private const float UpgradeAddressesDeltaSeconds = 5f;

        internal interface IHVRVariableBehaviour : IFeatureReceiver
        {
            public void Update();
            void RequireVariable(HVRVariable variable);
            void OnDestroy();
        }

        internal class HVRVariableBehaviour_Wearer : IHVRVariableBehaviour
        {
            private const bool UseInterpolationTape = false;

            private readonly HVRVariableNetworking _state;
            private readonly AcquisitionService _acquisitionService;

            internal readonly Dictionary<int, HVRVariableHolder> _addressIdToHolder = new();
            private readonly List<int> _newVariablesAddressIds = new();
            private readonly HashSet<int> _addressIdsWithNewValue = new();
            private readonly List<int> _highFrequencyAddressIds = new();
            private readonly HashSet<int> _highFrequencyAddressIdsHashSet = new();
            private ushort _networkId = 0;

            private float _timeLeftUpdateValues;
            private float _timeLeftUpgradeAddresses;

            public HVRVariableBehaviour_Wearer(HVRVariableNetworking state)
            {
                _state = state;
                _acquisitionService = AcquisitionService.SceneInstance;
            }

            public void OnPacketReceived(byte localIdentifier, ArraySegment<byte> data) { } // Not applicable

            public void OnResyncEveryoneRequested()
            {
                SubmitNewVariablesPacket(_addressIdToHolder.Keys.ToList(), "OnResyncEveryoneRequested");
            }

            public void OnResyncRequested(ushort[] whoAsked)
            {
                SubmitNewVariablesPacket(_addressIdToHolder.Keys.ToList(), "OnResyncRequested");
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

            public void OnDestroy()
            {
                _acquisitionService.UnregisterAddresses(_addressIdToHolder.Keys.ToArray(), OnAddressUpdated);
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
                    SubmitNewVariablesPacket(_newVariablesAddressIds, "Update");
                    _newVariablesAddressIds.Clear();
                }

                // This runs every 0.1 seconds in general.
                _timeLeftUpdateValues += Time.deltaTime;
                if (_timeLeftUpdateValues > TransmissionDeltaSeconds)
                {
                    DoTick(_timeLeftUpdateValues);
                    _timeLeftUpdateValues = 0;
                }

                // This runs every 5 seconds in general.
                _timeLeftUpgradeAddresses += Time.deltaTime;
                if (_timeLeftUpgradeAddresses > UpgradeAddressesDeltaSeconds)
                {
                    UpgradeOrDowngradeAddressesIfNecessary();
                }
            }

            private void SubmitNewVariablesPacket(List<int> addressIds, string hook)
            {
                if (addressIds.Count > 0)
                {
                    var groupSize = 10;
                    for (var i = 0; i < addressIds.Count; i += groupSize)
                    {
                        var group = addressIds.Skip(i).Take(groupSize).ToList();
                        var packet = BuildNewVariablesPacket(group);
                        _state.transmitter.NetworkMessageSend(packet, DeliveryMethod.ReliableSequenced);
                        if (PrintDebug)
                        {
                            HVRLogging.ProtocolDebug($"({hook}) Sending NewVariablesPacket (group {i / groupSize + 1}).");
                        }
                    }
                }
                else
                {
                    var packet = BuildNewVariablesPacket(addressIds);
                    _state.transmitter.NetworkMessageSend(packet, DeliveryMethod.ReliableSequenced);
                    if (PrintDebug)
                    {
                        HVRLogging.ProtocolDebug($"({hook}) Sending NewVariablesPacket (empty).");
                    }
                }
            }

            private void DoTick(float deltaTimeSinceLastTick)
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
                    var packet = BuildUpdatedVariablesPacket(addressIdsToValueToTransmit, deltaTimeSinceLastTick);
                    _state.transmitter.NetworkMessageSend(packet, DeliveryMethod.ReliableSequenced);
                    if (PrintDebug) HVRLogging.ProtocolDebug($"(Update) Sending UpdatedVariablesPacket (at T={Time.time:0.00}).");
                }

                _addressIdsWithNewValue.Clear();
                _addressIdsWithNewValue.UnionWith(addressIdsThatNeedToBeResentLater);
            }

            private void UpgradeOrDowngradeAddressesIfNecessary()
            {
                var addressIdsToUpgradeInOrder = new List<int>();

                // TODO: Decide what to upgrade.
                foreach (var addressIdToHolder in _addressIdToHolder)
                {
                    var addressId = addressIdToHolder.Key;
                    if (!_highFrequencyAddressIdsHashSet.Contains(addressId))
                    {
                        // TODO: Check the frequency of this addressId.
                        if (false)
                        {
                            HVRLogging.Debug($"Upgrading address {HVRAddress.ResolveKnownAddressFromId(addressId)} to high frequency.");
                            addressIdsToUpgradeInOrder.Add(addressId);
                        }
                    }
                }

                if (addressIdsToUpgradeInOrder.Count > 0)
                {
                    _highFrequencyAddressIds.AddRange(addressIdsToUpgradeInOrder);
                    _highFrequencyAddressIdsHashSet.UnionWith(addressIdsToUpgradeInOrder);

                    var upgradePacket = BuildUpgradePacket(addressIdsToUpgradeInOrder);
                    _state.transmitter.NetworkMessageSend(upgradePacket, DeliveryMethod.ReliableSequenced);
                    if (PrintDebug) HVRLogging.ProtocolDebug($"(Update) Sending UpgradeFloatToHighFrequencyPacket (at T={Time.time:0.00}).");
                }
            }

            private byte[] BuildUpgradePacket(List<int> addressIdsToUpgrade)
            {
                if (PrintDebug) HVRLogging.ProtocolDebug("(BuildUpgradePacket) Building an UpgradeFloatToHighFrequency packet.");

                return new HVRVariableNetworkingPacket_UpgradeFloatToHighFrequency
                {
                    items = addressIdsToUpgrade.Select(addressId =>
                    {
                        var holder = _addressIdToHolder[addressId];
                        return new HVRVariableNetworkingPacket_UpgradeFloatToHighFrequency.Inner_Item
                        {
                            networkId = holder.networkId,
                            min = holder.variable.min,
                            max = holder.variable.max,
                        };
                    }).ToList()
                }.Serialize();
            }

            private byte[] BuildUpdatedVariablesPacket(Dictionary<int, object> addressIdsToValueToTransmit, float deltaTimeSinceLastTick)
            {
                var deltaLocalIntToSeconds = (int)(deltaTimeSinceLastTick / StreamedAvatarFeature.DeltaLocalIntToSeconds);
                if (deltaLocalIntToSeconds > byte.MaxValue) deltaLocalIntToSeconds = byte.MaxValue;

                var timingSteps = (byte)deltaLocalIntToSeconds;

                // In our system, we currently only handle floats (this may change in the future to support strings and Color).
                // We do not handle booleans. Instead, this is what happens:
                // The float values of 0.0 and 1.0 are considered to be special. Instead of networking the value of 0.0 and 1.0,
                // we transmit a list of networkIds for those zeroes and ones and using four different packet types.
                // Only values that change are transmitted, so we do not deal with bitfields or anything like that.
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
                    if (PrintDebug) HVRLogging.ProtocolDebug("(BuildUpdatedVariablesPacket) Building a UpdatedVariables_Mixed packet.");
                    return new HVRVariableNetworkingPacket_UpdatedVariables_Mixed
                    {
                        timingSteps = timingSteps,
                        numberOfZeroes = (ushort)zeroesNetworkIds.Count,
                        networkIds = zeroesNetworkIds.Concat(onesNetworkIds).ToList(),
                        other = otherAddressIds.Select(addressId => new HVRVariableNetworkingPacket_UpdatedVariables_Mixed.Inner_UpdatedValue
                        {
                            networkId = _addressIdToHolder[addressId].networkId,
                            value = addressIdsToValueToTransmit[addressId]
                        }).ToList()
                    }.Serialize();
                }

                if (zeroesNetworkIds.Count > 0 && onesNetworkIds.Count > 0)
                {
                    if (PrintDebug) HVRLogging.ProtocolDebug("(BuildUpdatedVariablesPacket) Building a UpdatedVariables_ZeroesAndOnes packet.");
                    return new HVRVariableNetworkingPacket_UpdatedVariables_ZeroesAndOnes
                    {
                        timingSteps = timingSteps,
                        numberOfZeroes = (ushort)zeroesNetworkIds.Count,
                        networkIds = zeroesNetworkIds.Concat(onesNetworkIds).ToList()
                    }.Serialize();
                }

                if (PrintDebug) HVRLogging.ProtocolDebug($"(BuildUpdatedVariablesPacket) Building a {(zeroesNetworkIds.Count > 0 ? "UpdatedVariables_Zeroes" : "UpdatedVariables_Ones")} packet.");
                return new HVRVariableNetworkingPacket_UpdatedVariables_ZeroesOrOnes
                {
                    timingSteps = timingSteps,
                    packetType = zeroesNetworkIds.Count > 0 ? AvatarMessageProcessing.NewNet_WearerSubmitsUpdatedVariables_Zeroes : AvatarMessageProcessing.NewNet_WearerSubmitsUpdatedVariables_Ones,
                    networkIds = zeroesNetworkIds.Count > 0 ? zeroesNetworkIds : onesNetworkIds
                }.Serialize();
            }

            private byte[] BuildNewVariablesPacket(List<int> newVariablesAddressIds)
            {
                var allHolders = newVariablesAddressIds
                    .Select(addressId => _addressIdToHolder[addressId])
                    .ToList();

                var other = new List<HVRVariableNetworkingPacket_NewVariables.Inner_NewVariable>();
                var zeroes = new List<HVRVariableNetworkingPacket_NewVariables.Inner_NewQuickVariable>();
                var ones = new List<HVRVariableNetworkingPacket_NewVariables.Inner_NewQuickVariable>();

                foreach (var holder in allHolders)
                {
                    var isFloat = holder.variable.variableTypeCode == HVRVariableTypeCode.Float;
                    if (isFloat
                        && (Mathf.Approximately((float)holder.currentValue, 0f)
                            || Mathf.Approximately((float)holder.currentValue, 1f)))
                    {
                        var quickVar = new HVRVariableNetworkingPacket_NewVariables.Inner_NewQuickVariable
                        {
                            address = HVRAddress.ResolveKnownAddressFromId(holder.variable.addressId),
                            networkId = holder.networkId,
                        };
                        (Mathf.Approximately((float)holder.currentValue, 1f) ? ones : zeroes)
                            .Add(quickVar);
                    }
                    else
                    {
                        other.Add(new HVRVariableNetworkingPacket_NewVariables.Inner_NewVariable
                        {
                            address = HVRAddress.ResolveKnownAddressFromId(holder.variable.addressId),
                            networkId = holder.networkId,
                            variableTypeCode = (byte)holder.variable.variableTypeCode,
                            initialValue = holder.currentValue
                        });
                    }
                }

                return new HVRVariableNetworkingPacket_NewVariables
                {
                    newGeneralVariables = other,
                    floatZero = zeroes,
                    floatOne = ones,
                }.Serialize();
            }

            internal class HVRVariableHolder
            {
                public HVRVariable variable;
                public ushort networkId;
                public object currentValue;
                public object lastTransmittedValue;
                public object valueWithGreatestDeltaSinceLastTransmittedValue;
            }
        }

        internal class HVRVariableBehaviour_Remote : IHVRVariableBehaviour
        {
            private const bool UseInterpolationTape = true;

            private readonly HVRVariableNetworking _state;
            internal readonly Dictionary<int, HVRVariableHolder> _addressIdToHolder = new();
            private readonly Dictionary<ushort, int> _networkIdToAddressId = new();
            private readonly List<HVRVariableHighFrequency> _upgradedToHighFrequencyInOrder = new();

            private HVRInterpolationTimer _interpolationTimer;
            private HVRInterpolationData _interpolationDataThisFrame;

            public HVRVariableBehaviour_Remote(HVRVariableNetworking state)
            {
                _state = state;
            }

            private void WhenDataReceived(int addressId, float currentValue)
            {
                if (PrintDebug) HVRLogging.ProtocolDebug($"Received data for address {HVRAddress.ResolveKnownAddressFromId(addressId)} with value {currentValue}.");

                // TODO: IF APPLICABLE (address doesn't have a "no delay" flag + controls need to be able to define if it uses network interpolation), then:
                // - Put this data into the proper interpolation tape for that address, for that tick (we need to add the time delta inside the packet),
                // - then on the remote, play back the tape every frame on Update.
                // - QUESTION: Should the variable store be responsible for the interpolation tape?
                // --------------- NO. Interpolation is handled by the networking module.
                // :
                // -> When value is received, append to the tape with the delay.
                // -> The Variable Networking keeps tracks of the addresses that have a non-empty interpolation tape.
                // -> Every frame, Variable Networking advance the non-empty tapes by (delaySinceLastFrame)
                // -> the Variable Networking then emits Submit events with the new interpolated value to the Value Store.
                // :
                // If it's exposed as a slider in a MENU ITEM, then it MUST be interpolated
                // --> We need to mark the control itself as interpolated. Sliders must suggest to mark the control as interpolated.
                // Toggles SHOULD NOT BE interpolated.
                // Multiple choices SHOULD NOT BE interpolated.
                // --> Toggles and multiple choices should suggest to mark the control as non-interpolated.

                if (UseInterpolationTape)
                {
                    if (true
                        // _addressIdToHolder[addressId].needsInterpolation
                       )
                    {
                        var TODO_DeltaTimeInsidePacket = 0.1f; // TODO: Pass the delta time fractional inside the packet
                        if (_interpolationDataThisFrame == null)
                        {
                            _interpolationDataThisFrame = new HVRInterpolationData(TODO_DeltaTimeInsidePacket);
                        }

                        _interpolationDataThisFrame.Add(addressId, currentValue);
                    }
                    else
                    {
                        _state.comms.VariableStore.Submit(addressId, currentValue);
                    }
                }
                else
                {
                    _state.comms.VariableStore.Submit(addressId, currentValue);
                }
            }

            private void WhenDataReceived_BypassInterpolationTape(int addressId, float currentValue)
            {
                _state.comms.VariableStore.Submit(addressId, currentValue);
            }

            public void Update()
            {
                if (UseInterpolationTape)
                {
                    if (_interpolationDataThisFrame != null)
                    {
                        _interpolationTimer.Enqueue(_interpolationDataThisFrame);
                        _interpolationDataThisFrame = null;
                    }

                    _interpolationTimer.Advance(Time.deltaTime);
                }
            }

            public void OnPacketReceived(byte localIdentifier, ArraySegment<byte> data)
            {
                if (data.Count < 1) { HVRLogging.ProtocolError("Data buffer is empty."); return; }

                var packetType = data[0];
                switch (packetType)
                {
                    case AvatarMessageProcessing.NewNet_WearerSubmitsNewVariables:
                    {
                        if (!HVRVariableNetworkingPacket_NewVariables.TryDeserialize(data, out var packet))
                        {
                            HVRLogging.ProtocolError("Failed to deserialize NewVariables packet.");
                            return;
                        }

                        if (PrintDebug) HVRLogging.ProtocolDebug("(OnPacketReceived) Receiving NewVariables packet.");
                        WhenNewVariablesReceived(packet);
                        break;
                    }
                    case AvatarMessageProcessing.NewNet_WearerSubmitsUpdatedVariables_Zeroes:
                    {
                        if (!HVRVariableNetworkingPacket_UpdatedVariables_ZeroesOrOnes.TryDeserialize(data, packetType, out var packet))
                        {
                            HVRLogging.ProtocolError("Failed to deserialize UpdatedVariables_Zeroes packet.");
                            return;
                        }

                        if (PrintDebug) HVRLogging.ProtocolDebug("(OnPacketReceived) Receiving UpdatedVariables_Zeroes packet.");
                        foreach (var networkId in packet.networkIds)
                        {
                            if (_networkIdToAddressId.TryGetValue(networkId, out var addressId))
                            {
                                _addressIdToHolder[addressId].currentValue = 0f;
                                WhenDataReceived(addressId, 0f);
                            }
                        }

                        break;
                    }
                    case AvatarMessageProcessing.NewNet_WearerSubmitsUpdatedVariables_Ones:
                    {
                        if (!HVRVariableNetworkingPacket_UpdatedVariables_ZeroesOrOnes.TryDeserialize(data, packetType, out var packet))
                        {
                            HVRLogging.ProtocolError("Failed to deserialize UpdatedVariables_Ones packet.");
                            return;
                        }

                        if (PrintDebug) HVRLogging.ProtocolDebug("(OnPacketReceived) Receiving UpdatedVariables_Ones packet.");
                        foreach (var networkId in packet.networkIds)
                        {
                            if (_networkIdToAddressId.TryGetValue(networkId, out var addressId))
                            {
                                _addressIdToHolder[addressId].currentValue = 1f;
                                WhenDataReceived(addressId, 1f);
                            }
                        }

                        break;
                    }
                    case AvatarMessageProcessing.NewNet_WearerSubmitsUpdatedVariables_ZeroesAndOnes:
                    {
                        if (!HVRVariableNetworkingPacket_UpdatedVariables_ZeroesAndOnes.TryDeserialize(data, out var packet))
                        {
                            HVRLogging.ProtocolError("Failed to deserialize UpdatedVariables_ZeroesAndOnes packet.");
                            return;
                        }

                        if (PrintDebug) HVRLogging.ProtocolDebug("(OnPacketReceived) Receiving UpdatedVariables_ZeroesAndOnes packet.");
                        for (var index = 0; index < packet.networkIds.Count; index++)
                        {
                            if (_networkIdToAddressId.TryGetValue(packet.networkIds[index], out var addressId))
                            {
                                var isZero = index < packet.numberOfZeroes;
                                var value = isZero ? 0f : 1f;
                                _addressIdToHolder[addressId].currentValue = value;
                                WhenDataReceived(addressId, value);
                            }
                        }

                        break;
                    }
                    case AvatarMessageProcessing.NewNet_WearerSubmitsUpdatedVariables_Mixed:
                    {
                        if (!HVRVariableNetworkingPacket_UpdatedVariables_Mixed.TryDeserialize(data, out var packet))
                        {
                            HVRLogging.ProtocolError("Failed to deserialize UpdatedVariables_Mixed packet.");
                            return;
                        }

                        if (PrintDebug) HVRLogging.ProtocolDebug("(OnPacketReceived) Receiving UpdatedVariables_Mixed packet.");
                        for (var index = 0; index < packet.networkIds.Count; index++)
                        {
                            if (_networkIdToAddressId.TryGetValue(packet.networkIds[index], out var addressId))
                            {
                                var isZero = index < packet.numberOfZeroes;
                                var value = isZero ? 0f : 1f;
                                _addressIdToHolder[addressId].currentValue = value;
                                WhenDataReceived(addressId, value);
                            }
                        }
                        foreach (var other in packet.other)
                        {
                            if (_networkIdToAddressId.TryGetValue(other.networkId, out var addressId))
                            {
                                if (_addressIdToHolder[addressId].variable.variableTypeCode == HVRVariableTypeCode.Float && other.value is float f)
                                {
                                    _addressIdToHolder[addressId].currentValue = f;
                                    WhenDataReceived(addressId, f);
                                }
                            }
                        }

                        break;
                    }
                    case AvatarMessageProcessing.NewNet_WearerUpgradesFloatToHighFrequency:
                    {
                        if (!HVRVariableNetworkingPacket_UpgradeFloatToHighFrequency.TryDeserialize(data, out var packet))
                        {
                            HVRLogging.ProtocolError("Failed to deserialize UpgradeFloatToHighFrequency packet.");
                            return;
                        }

                        if (PrintDebug) HVRLogging.ProtocolDebug($"(Update) Received UpgradeFloatToHighFrequencyPacket (at T={Time.time:0.00}).");
                        var newlyAdded = packet.items.Select(item => new HVRVariableHighFrequency
                        {
                            networkId = item.networkId,
                            min = item.min,
                            max = item.max,
                        }).ToList();
                        foreach (var highFrequency in newlyAdded)
                        {
                            if (!_networkIdToAddressId.TryGetValue(highFrequency.networkId, out var addressId))
                            {
                                HVRLogging.ProtocolError($"Network ID {highFrequency.networkId} is not known. Reading from the server reduction will be mangled.");
                                continue;
                            }

                            _addressIdToHolder[addressId].variable.min = highFrequency.min;
                            _addressIdToHolder[addressId].variable.max = highFrequency.max;
                        }
                        _upgradedToHighFrequencyInOrder.AddRange(newlyAdded);

                        break;
                    }
                    default:
                        HVRLogging.ProtocolError($"Unknown packet type {packetType}.");
                        break;
                }
            }

            public void OnResyncEveryoneRequested() { } // Not applicable
            public void OnResyncRequested(ushort[] whoAsked) { } // Not applicable

            private void WhenNewVariablesReceived(HVRVariableNetworkingPacket_NewVariables packet)
            {
                var newlyAddedAddresses = new List<int>();

                foreach (var variable in packet.newGeneralVariables)
                {
                    var addressId = HVRAddress.AddressToId(variable.address);
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
                    var addressId = HVRAddress.AddressToId(variable.address);
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
                    var addressId = HVRAddress.AddressToId(variable.address);
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
                    WhenDataReceived_BypassInterpolationTape(newlyAddedAddress, (float)_addressIdToHolder[newlyAddedAddress].currentValue);
                }
            }

            public void RequireVariable(HVRVariable variable)
            {
                // Do nothing.
            }

            public void OnDestroy()
            {
            }

            internal class HVRVariableHolder
            {
                public HVRVariable variable;
                public ushort networkId;
                public object currentValue;
            }
        }
    }

    internal class HVRVariableHighFrequency
    {
        public ushort networkId;
        public float min;
        public float max;
    }

    internal class HVRInterpolationData
    {
        public float DeltaTime { get; }

        public HVRInterpolationData(float deltaTime)
        {
            DeltaTime = deltaTime;
        }

        public void Add(int addressId, float currentValue)
        {
            throw new NotImplementedException();
        }
    }

    internal class HVRInterpolationTimer
    {
        private const float DeltaTimeUsedForResyncs = 1 / 29f; // 29 is just a random number I picked. It really doesn't matter what value we're using for resyncs.

        private readonly Queue<HVRInterpolationData> _queue = new();
        private float _totalQueueSeconds;
        private int _numberOfEnqueues;

        private float _timeLeft;
        private bool _isOutOfTape;
        private bool _writtenThisFrame;
        private float _effectiveDeltaTime;

        public void Enqueue(HVRInterpolationData newData)
        {
            _queue.Enqueue(newData);
            _totalQueueSeconds += newData.DeltaTime;
            _numberOfEnqueues++;
            if (_numberOfEnqueues % 1_000 == 0)
            {
                // Recalculate the queue duration for precision loss concerns.
                _numberOfEnqueues = 0;
                _totalQueueSeconds = 0f;
                foreach (var data in _queue)
                {
                    _totalQueueSeconds += data.DeltaTime;
                }
            }
        }

        public void Advance(float deltaTime)
        {
            /*
            _timeLeft -= deltaTime;

            while (_timeLeft <= 0 && _queue.TryDequeue(out var eval))
            {
                _totalQueueSeconds -= eval.DeltaTime;
                if (_totalQueueSeconds < 0f) _totalQueueSeconds = 0f;

                // If the queue is small or the total queue duration is short, use the delta from the queue
                var effectiveDeltaTime = _queue.Count <= 5 || _totalQueueSeconds < 0.2f
                    ? eval.DeltaTime
                    // Otherwise, we fast-forward the queue.
                    // NOTE: I actually can't remember why the fast-forward is defined in this way. It may be complete nonsense.
                    : (eval.DeltaTime * Mathf.Lerp(0.66f, 0.05f, Mathf.InverseLerp(DeltaTimeUsedForResyncs, 4f, _totalQueueSeconds)));

                _timeLeft += effectiveDeltaTime;
                _previous = _target;
                _target = eval;
                _effectiveDeltaTime = effectiveDeltaTime;
                _isOutOfTape = false;
            }

            var isDepleted = _timeLeft <= 0;
            if (isDepleted)
            {
                if (!_isOutOfTape)
                {
                    _isOutOfTape = true;

                    _current = _target; // FIXME: INTERPOLABLE REFACTOR. Does this have side effects?
                    _writtenThisFrame = true;
                }
                else
                {
                    _writtenThisFrame = false;
                }
                _timeLeft = 0;
            }
            else
            {
                _isOutOfTape = false;

                var progression01 = 1 - Mathf.Clamp01(_timeLeft / _effectiveDeltaTime);
                _current.MutateLerp(_previous, _target, progression01);
                _writtenThisFrame = true;
            }
        */
        }
    }

    public enum HVRVariableTypeCode
    {
        Float = 1,
    }

    public class HVRVariable
    {
        public int addressId;
        public object initialValue; // This is not necessarily the default value, it is the value that was current when the variable was created on a specific remote; every user might have a different initialValue.
        public HVRVariableTypeCode variableTypeCode;

        // If float:
        public float min;
        public float max;
    }
}
