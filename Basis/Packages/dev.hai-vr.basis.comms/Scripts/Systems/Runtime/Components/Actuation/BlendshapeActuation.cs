using System;
using System.Collections.Generic;
using System.Linq;
using Basis.Scripts.BasisSdk;
using HVR.Basis.Comms.HVRUtility;
using UnityEngine;

namespace HVR.Basis.Comms
{
    [AddComponentMenu("HVR.Basis/Comms/Blendshape Actuation")]
    public class BlendshapeActuation : MonoBehaviour, IHVRInitializable
    {
        // This is a class originally created in September 2024, which as of 2026 sets the value of blendshapes based on addresses.
        // Originally, this class also took care of networking the addresses, but it is no longer the case since the addition of HVRVariableNetworking in April 2026 which now takes that responsibility.
        // There are still leftover traces of the old networking (e.g. range calculation, indexing) in this class, so this class could still be greatly simplified.

        private const float BlendshapeAtFullStrength = 100f;

        [SerializeField] private SkinnedMeshRenderer[] renderers = Array.Empty<SkinnedMeshRenderer>();
        [SerializeField] private BlendshapeActuationDefinitionFile[] definitionFiles = Array.Empty<BlendshapeActuationDefinitionFile>();
        [SerializeField] private BlendshapeActuationDefinition[] definitions = Array.Empty<BlendshapeActuationDefinition>();
        [SerializeField] private AddressOverride[] addressOverrides = Array.Empty<AddressOverride>();

        [HideInInspector] [SerializeField] private BasisAvatar avatar;

        private HVRAvatarComms comms;

        private readonly Dictionary<int, float> _latestAbsoluteByAddress = new();
        private ComputedActuator[] _computedActuators;
        private AddressOverride[] _defaultOverrides = Array.Empty<AddressOverride>();
        private FaceTrackingActivityRelay _activityRelay;
        private bool _isWearer;
        private bool _trackingActive;
        public bool IsTrackingActive => _trackingActive;

        public string[] debugAddresses;
        private Dictionary<int, List<ComputedActuator>> _addressIdToActuators;

        public void AutoDefine(BlendshapeActuationDefinitionFile[] providedDefinitionFiles, List<SkinnedMeshRenderer> providedSmrs)
        {
            definitionFiles = providedDefinitionFiles;
            renderers = providedSmrs.ToArray();
        }

        private void Awake()
        {
            if (avatar == null)
            {
                avatar = HVRCommsUtil.GetAvatar(this);
            }

            comms = HVRCommsUtil.GetComms(this);

            _activityRelay = FaceTrackingActivityRelay.GetOrCreate(avatar);
            renderers = HVRCommsUtil.SlowSanitizeEndUserProvidedObjectArray(renderers);
            definitionFiles = HVRCommsUtil.SlowSanitizeEndUserProvidedObjectArray(definitionFiles);
            definitions = HVRCommsUtil.SlowSanitizeEndUserProvidedStructArray(definitions);
        }

        private void OnAddressUpdated(int address, float inRange)
        {
            ApplyAddressValue(address, inRange);
        }

        private static void Actuate(ComputedActuator actuator, float inRange)
        {
            var intermediate01 = Mathf.InverseLerp(actuator.InStart, actuator.InEnd, inRange);
            if (actuator.UseCurve)
            {
                intermediate01 = actuator.Curve.Evaluate(intermediate01);
            }
            var outputWild = Mathf.Lerp(actuator.OutStart, actuator.OutEnd, intermediate01);
            var output01 = Mathf.Clamp01(outputWild);
            var output0100 = output01 * BlendshapeAtFullStrength;

            foreach (var target in actuator.Targets)
            {
                foreach (var blendshapeIndex in target.BlendshapeIndices)
                {
                    target.Renderer.SetBlendShapeWeight(blendshapeIndex, output0100);
                }
            }
        }

        public void OnHVRAvatarReady(bool isWearer)
        {
            _isWearer = isWearer;
            if (_activityRelay != null)
            {
                _activityRelay.OnTrackingActivityChanged -= OnTrackingActivityUpdated;
                _activityRelay.OnTrackingActivityChanged += OnTrackingActivityUpdated;
            }
            _trackingActive = _activityRelay != null && _activityRelay.IsTrackingActive;

            var allDefinitions = new List<BlendshapeActuationDefinition>();
            allDefinitions.AddRange(definitions);
            foreach (var definitionFile in definitionFiles)
            {
                allDefinitions.AddRange(definitionFile.definitions);
            }

            var smrToBlendshapeNames = ResolveSmrToBlendshapeNames(renderers);

            var addressToMinMax = new Dictionary<int, (float, float)>();
            foreach (var definition in allDefinitions)
            {
                var addressId = HVRAddress.AddressToId(definition.address);
                var min = definition.inStart < definition.inEnd ? definition.inStart : definition.inEnd;
                var max = definition.inStart > definition.inEnd ? definition.inStart : definition.inEnd;
                if (addressToMinMax.TryGetValue(addressId, out var existingMinMax))
                {
                    var (existingMin, existingMax) = existingMinMax;
                    addressToMinMax[addressId] = (Mathf.Min(existingMin, min), Mathf.Max(existingMax, max));
                }
                else
                {
                    addressToMinMax[addressId] = (min, max);
                }
            }

            _addressIdToActuators = new Dictionary<int, List<ComputedActuator>>();
            var tempActuatedAddress = new HashSet<string>();
            var tempActuatedAddressIds = new HashSet<int>();
            {
                var tempActuators = new List<ComputedActuator>();
                foreach (var definition in allDefinitions)
                {
                    var actuatorTargets = ComputeTargets(smrToBlendshapeNames, definition.blendshapes, definition.onlyFirstMatch);
                    if (actuatorTargets.Length == 0) continue;

                    var addressId = HVRAddress.AddressToId(definition.address);
                    var (min, max) = addressToMinMax[addressId];
                    tempActuatedAddress.Add(definition.address);
                    tempActuatedAddressIds.Add(addressId);

                    var newlyAdded = new ComputedActuator
                    {
                        InStart = definition.inStart,
                        InEnd = definition.inEnd,
                        OutStart = definition.outStart,
                        OutEnd = definition.outEnd,
                        UseCurve = definition.useCurve,
                        Curve = definition.curve,
                        Targets = actuatorTargets,
                        AddressId = addressId,
                        Min = min,
                        Max = max
                    };
                    tempActuators.Add(newlyAdded);

                    if (_addressIdToActuators.TryGetValue(addressId, out var existingActuators))
                    {
                        existingActuators.Add(newlyAdded);
                    }
                    else
                    {
                        _addressIdToActuators[addressId] = new List<ComputedActuator> { newlyAdded };
                    }
                }
                _computedActuators = tempActuators.ToArray();
            }
            debugAddresses = tempActuatedAddress.ToArray();

            var defaultOverridesList = new List<AddressOverride>();
            foreach (var file in definitionFiles)
            {
                foreach (var addressOverride in file.addressOverrides)
                {
                    if (addressOverride.overrideDefaultValue)
                    {
                        defaultOverridesList.Add(addressOverride);
                    }
                }
            }
            foreach (var addressOverride in addressOverrides)
            {
                if (addressOverride.overrideDefaultValue)
                {
                    defaultOverridesList.Add(addressOverride);
                }
            }
            _defaultOverrides = defaultOverridesList.ToArray();

            comms.VariableStore.RegisterAddresses(tempActuatedAddressIds.ToArray(), OnAddressUpdated);
        }

        public static Dictionary<SkinnedMeshRenderer, List<string>> ResolveSmrToBlendshapeNames(SkinnedMeshRenderer[] smrs)
        {
            var smrToBlendshapeNames = new Dictionary<SkinnedMeshRenderer, List<string>>();
            foreach (var smr in smrs)
            {
                var mesh = smr.sharedMesh;
                var blendshapeNames = new List<string>();
                for (var i = 0; i < mesh.blendShapeCount; i++)
                {
                    blendshapeNames.Add(mesh.GetBlendShapeName(i));
                }
                smrToBlendshapeNames.Add(smr, blendshapeNames);
            }

            return smrToBlendshapeNames;
        }

        public void OnHVRReadyBothAvatarAndNetwork(bool isLocallyOwned)
        {
            HVRLogging.ProtocolDebug("OnReadyBothAvatarAndNetwork called on BlendshapeActuation.");
            _isWearer = isLocallyOwned;

            var addressIdToDefault = new Dictionary<int, float>();
            foreach (var defaultOverride in _defaultOverrides)
            {
                addressIdToDefault[HVRAddress.AddressToId(defaultOverride.address)] = defaultOverride.defaultValue;
            }

            foreach (var actuator in _computedActuators)
            {
                comms.RequireVariable(new HVRVariable
                {
                    addressId = actuator.AddressId,
                    initialValue = addressIdToDefault.GetValueOrDefault(actuator.AddressId, 0f),
                    variableTypeCode = HVRVariableTypeCode.Float,
                    needsInterpolation = true,
                    min = actuator.Min,
                    max = actuator.Max,
                });
            }
        }

        private void OnDisable()
        {
            if (_computedActuators != null)
            {
                ResetAllBlendshapesToZero();
            }
        }

        private void OnDestroy()
        {
            if (avatar != null)
            {
                avatar.OnAvatarReady -= OnHVRAvatarReady;
            }

            if (_activityRelay != null)
            {
                _activityRelay.OnTrackingActivityChanged -= OnTrackingActivityUpdated;
            }

            if (_computedActuators != null)
            {
                var addressIdToListenTo = new HashSet<int>();
                foreach (var computedActuator in _computedActuators)
                {
                    addressIdToListenTo.Add(computedActuator.AddressId);
                }
                comms.VariableStore.UnregisterAddresses(addressIdToListenTo.ToArray(), OnAddressUpdated);
            }
        }

        private void OnTrackingActivityUpdated(bool isTrackingActive)
        {
            if (_trackingActive == isTrackingActive)
            {
                return;
            }

            _trackingActive = isTrackingActive;
            if (_trackingActive)
            {
                if (_isWearer)
                {
                    ApplyDefaultOverrides(); // 2026: This might not be necessary as the function called below will re-submit new values for the addresses, which will be carried by OnAddressUpdated. Still to be checked.
                    ReplayLatestTrackedValuesToNetwork();
                }
                return;
            }

            ResetAllBlendshapesToZero(); // 2026: This might not be necessary as the function called below will re-submit new values for the addresses, which will be carried by OnAddressUpdated. Still to be checked.
            _latestAbsoluteByAddress.Clear();
            if (_isWearer)
            {
                SubmitNeutralValuesToNetwork();
            }
        }

        private void ApplyAddressValue(int addressId, float inRange)
        {
            if (!_trackingActive || !_addressIdToActuators.TryGetValue(addressId, out var actuatorsForThisAddress))
            {
                return;
            }

            if (actuatorsForThisAddress == null)
            {
                return;
            }

            _latestAbsoluteByAddress[addressId] = inRange;
            foreach (var actuator in actuatorsForThisAddress)
            {
                Actuate(actuator, inRange);
            }
        }

        private void ApplyDefaultOverrides()
        {
            foreach (var addressOverride in _defaultOverrides)
            {
                ApplyAddressValue(HVRAddress.AddressToId(addressOverride.address), addressOverride.defaultValue);
            }
        }

        private void ReplayLatestTrackedValuesToNetwork()
        {
            if (!_isWearer)
            {
                return;
            }

            // We need to make a copy because comms.VariableStore.Submit will cause the data to be modified
            var copy = _latestAbsoluteByAddress.ToList();
            foreach (var pair in copy)
            {
                comms.VariableStore.SubmitOrDefineDefaultValue(pair.Key, pair.Value);
            }
        }

        private void SubmitDefaultOverridesToNetwork()
        {
            if (!_isWearer)
            {
                return;
            }

            foreach (var addressOverride in _defaultOverrides)
            {
                var addressId = HVRAddress.AddressToId(addressOverride.address);
                comms.VariableStore.SubmitOrDefineDefaultValue(addressId, addressOverride.defaultValue);
            }
        }

        private void SubmitNeutralValuesToNetwork()
        {
            if (!_isWearer)
            {
                return;
            }

            foreach (var addressId in _addressIdToActuators.Keys)
            {
                comms.VariableStore.SubmitOrDefineDefaultValue(addressId, 0f);
            }
        }

        private void ResetAllBlendshapesToZero()
        {
            if (_computedActuators == null)
            {
                return;
            }

            foreach (var computedActuator in _computedActuators)
            {
                foreach (var target in computedActuator.Targets)
                {
                    if (null != target.Renderer && null != target.Renderer.sharedMesh)
                    {
                        var blendshapeCount = target.Renderer.sharedMesh.blendShapeCount;
                        foreach (var blendshapeIndex in target.BlendshapeIndices)
                        {
                            if (blendshapeIndex < blendshapeCount)
                            {
                                target.Renderer.SetBlendShapeWeight(blendshapeIndex, 0);
                            }
                        }
                    }
                }
            }
        }

        public static ComputedActuatorTarget[] ComputeTargets(Dictionary<SkinnedMeshRenderer, List<string>> smrToBlendshapeNames, string[] definitionBlendshapes, bool onlyFirstMatch)
        {
            var actuatorTargets = new List<ComputedActuatorTarget>();
            foreach (var pair in smrToBlendshapeNames)
            {
                var indices = definitionBlendshapes
                    .Select(toFind => pair.Value.IndexOf(toFind))
                    .Where(i => i >= 0)
                    .ToArray();

                if (indices.Length > 0)
                {
                    if (onlyFirstMatch)
                    {
                        actuatorTargets.Add(new ComputedActuatorTarget
                        {
                            Renderer = pair.Key,
                            BlendshapeIndices = new[] { indices[0] }
                        });
                    }
                    else
                    {
                        actuatorTargets.Add(new ComputedActuatorTarget
                        {
                            Renderer = pair.Key,
                            BlendshapeIndices = indices
                        });
                    }
                }
            }

            return actuatorTargets.ToArray();
        }

        private class ComputedActuator
        {
            public int AddressIndex;
            public float InStart;
            public float InEnd;
            public float OutStart;
            public float OutEnd;
            public bool UseCurve;
            public AnimationCurve Curve;
            public ComputedActuatorTarget[] Targets;
            public int AddressId;
            public float Min;
            public float Max;
        }

        public class ComputedActuatorTarget
        {
            public SkinnedMeshRenderer Renderer;
            public int[] BlendshapeIndices;
        }
    }
}
