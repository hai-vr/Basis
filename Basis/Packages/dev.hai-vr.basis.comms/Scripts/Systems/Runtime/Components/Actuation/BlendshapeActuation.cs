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

        private const int MaxAddresses = 256;
        private const float BlendshapeAtFullStrength = 100f;

        [SerializeField] private SkinnedMeshRenderer[] renderers = Array.Empty<SkinnedMeshRenderer>();
        [SerializeField] private BlendshapeActuationDefinitionFile[] definitionFiles = Array.Empty<BlendshapeActuationDefinitionFile>();
        [SerializeField] private BlendshapeActuationDefinition[] definitions = Array.Empty<BlendshapeActuationDefinition>();
        [SerializeField] private AddressOverride[] addressOverrides = Array.Empty<AddressOverride>();

        [HideInInspector] [SerializeField] private BasisAvatar avatar;

        private HVRAvatarComms comms;

        private Dictionary<int, int> _addessIdToBaseIndex = new();
        private readonly Dictionary<int, float> _latestAbsoluteByAddress = new();
        private ComputedActuator[] _computedActuators;
        private ComputedActuator[][] _addressBaseIndexToActuators;
        private Dictionary<int, (float, float)> _addressToStreamedLowerUpper;
        private AddressOverride[] _defaultOverrides = Array.Empty<AddressOverride>();
        private FaceTrackingActivityRelay _activityRelay;
        private bool _isWearer;
        private bool _trackingActive;
        public bool IsTrackingActive => _trackingActive;

        public string[] debugAddresses;

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

            var allDefinitions = definitions
                .Concat(definitionFiles.SelectMany(file => file.definitions))
                .ToArray();

            var smrToBlendshapeNames = ResolveSmrToBlendshapeNames(renderers);

            // All streamed avatar feature values are between 0 and 1.
            // If we want to stream values outside of this range (i.e. [-1; 1]), we need to collect all
            // possible InStart and InEnd values in order to lerp in that range.
            _addressToStreamedLowerUpper = allDefinitions
                .GroupBy(definition => HVRAddress.AddressToId(definition.address))
                .ToDictionary(grouping => grouping.Key, grouping =>
                {
                    var inValuesForThisAddress = grouping
                        // Reminder that InStart may be greater than InEnd.
                        // We want the lower bound, not the minimum of InStart.
                        .SelectMany(definition => new[] { definition.inStart, definition.inEnd })
                        .ToArray();
                    return (inValuesForThisAddress.Min(), inValuesForThisAddress.Max());
                });

            _computedActuators = allDefinitions.Select(definition =>
                {
                    var actuatorTargets = ComputeTargets(smrToBlendshapeNames, definition.blendshapes, definition.onlyFirstMatch);
                    if (actuatorTargets.Length == 0) return null;

                    var (lower, upper) = _addressToStreamedLowerUpper[HVRAddress.AddressToId(definition.address)];
                    return new ComputedActuator
                    {
                        // The AddressIndex field is filled later.
                        InStart = definition.inStart,
                        InEnd = definition.inEnd,
                        OutStart = definition.outStart,
                        OutEnd = definition.outEnd,
                        UseCurve = definition.useCurve,
                        Curve = definition.curve,
                        Targets = actuatorTargets,
                        RequestedFeature = new RequestedFeature
                        {
                            identifier = definition.address,
                            address = HVRAddress.AddressToId(definition.address),
                            lower = lower,
                            upper = upper
                        }
                    };
                })
                .Where(actuator => actuator != null)
                .ToArray();

            var allAddressesThatAreEffectivelyActuated = _computedActuators
                .Select(actuator => actuator.RequestedFeature.address)
                .Distinct()
                .ToArray();
            var allAddessesThatAreEffectivelyActuatedAsString = _computedActuators
                .Select(actuator => actuator.RequestedFeature.identifier)
                .Distinct()
                .ToArray();
            debugAddresses = allAddessesThatAreEffectivelyActuatedAsString;

            _addessIdToBaseIndex = MakeIndexDictionary(allAddressesThatAreEffectivelyActuated);
            if (_addessIdToBaseIndex.Count > MaxAddresses)
            {
                Debug.LogError($"Exceeded max {MaxAddresses} addresses allowed in an actuator.");
                enabled = false;
                return;
            }

            var addressIdToListenTo = new HashSet<int>();
            foreach (var computedActuator in _computedActuators)
            {
                computedActuator.AddressIndex = _addessIdToBaseIndex[computedActuator.RequestedFeature.address];
                addressIdToListenTo.Add(computedActuator.RequestedFeature.address);
            }

            _addressBaseIndexToActuators = new ComputedActuator[_addessIdToBaseIndex.Count][];
            foreach (var computedActuator in _computedActuators.GroupBy(actuator => actuator.AddressIndex, actuator => actuator))
            {
                _addressBaseIndexToActuators[computedActuator.Key] = computedActuator.ToArray();
            }

            _defaultOverrides = definitionFiles
                .SelectMany(file => file.addressOverrides)
                .Concat(addressOverrides)
                .Where(it => it.overrideDefaultValue)
                .ToArray();

            comms.VariableStore.RegisterAddresses(addressIdToListenTo.ToArray(), OnAddressUpdated);
        }

        public static Dictionary<SkinnedMeshRenderer, List<string>> ResolveSmrToBlendshapeNames(SkinnedMeshRenderer[] smrs)
        {
            var smrToBlendshapeNames = new Dictionary<SkinnedMeshRenderer, List<string>>();
            foreach (var smr in smrs)
            {
                var mesh = smr.sharedMesh;
                smrToBlendshapeNames.Add(smr, Enumerable.Range(0, mesh.blendShapeCount)
                    .Select(i => mesh.GetBlendShapeName(i))
                    .ToList());
            }

            return smrToBlendshapeNames;
        }

        public void OnHVRReadyBothAvatarAndNetwork(bool isLocallyOwned)
        {
            HVRLogging.ProtocolDebug("OnReadyBothAvatarAndNetwork called on BlendshapeActuation.");
            _isWearer = isLocallyOwned;
            // FIXME: We should be using the computed actuators instead of the address base, assuming that
            // the list of blendshapes is the same local and remote (no local-only or remote-only blendshapes).

            var addressIdToDefault = new Dictionary<int, float>();
            foreach (var defaultOverride in _defaultOverrides)
            {
                addressIdToDefault[HVRAddress.AddressToId(defaultOverride.address)] = defaultOverride.defaultValue;
            }

            foreach (var actuator in _computedActuators)
            {
                comms.RequireVariable(new HVRVariable
                {
                    addressId = actuator.RequestedFeature.address,
                    initialValue = addressIdToDefault.GetValueOrDefault(actuator.RequestedFeature.address, 0f),
                    variableTypeCode = HVRVariableTypeCode.Float,
                    needsInterpolation = true,
                    min = Mathf.Min(actuator.InStart, actuator.InEnd),
                    max = Mathf.Max(actuator.InStart, actuator.InEnd),
                });
            }
        }

        private Dictionary<int, int> MakeIndexDictionary(int[] addressBase)
        {
            var dictionary = new Dictionary<int, int>();
            for (var index = 0; index < addressBase.Length; index++)
            {
                var se = addressBase[index];
                dictionary[se] = index;
            }

            return dictionary;
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
                    addressIdToListenTo.Add(computedActuator.RequestedFeature.address);
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

        private void ApplyAddressValue(int address, float inRange)
        {
            if (!_trackingActive || !_addessIdToBaseIndex.TryGetValue(address, out var baseIndex))
            {
                return;
            }

            var actuatorsForThisAddress = _addressBaseIndexToActuators[baseIndex];
            if (actuatorsForThisAddress == null)
            {
                return;
            }

            _latestAbsoluteByAddress[address] = inRange;
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

            foreach (var addressId in _addessIdToBaseIndex.Keys)
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
            public RequestedFeature RequestedFeature;
        }

        public class ComputedActuatorTarget
        {
            public SkinnedMeshRenderer Renderer;
            public int[] BlendshapeIndices;
        }

        private class RequestedFeature
        {
            public string identifier;
            public int address;
            public float lower;
            public float upper;
        }
    }
}
