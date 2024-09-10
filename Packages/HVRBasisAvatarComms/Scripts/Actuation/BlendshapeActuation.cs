using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HVR.Basis.Comms
{
    [AddComponentMenu("HVR.Basis/Comms/BlendshapeActuation")]
    public class BlendshapeActuation : MonoBehaviour
    {
        private const int MaxAddresses = 256;
        
        [SerializeField] private StreamedAvatarFeature feature;
        [SerializeField] private SkinnedMeshRenderer[] renderers;
        // TODO: This could probably be an asset so that it may be reused.
        [SerializeField] private BlendshapeActuationDefinition[] definitions;
        
        private List<string> _addressBase;
        private ComputedActuator[] _computedActuators;
        
        private void Update()
        {
            if (!feature.IsWrittenThisFrame()) return;

            var current = feature.ExposeCurrent();
            foreach (var actuator in _computedActuators)
            {
                var value01 = current[actuator.AddressIndex];
                
                var intermediate01 = Mathf.InverseLerp(actuator.InMin, actuator.InMax, value01);
                if (actuator.UseCurve)
                {
                    intermediate01 = actuator.Curve.Evaluate(intermediate01);
                }
                var outputWild = Mathf.Lerp(actuator.OutMin, actuator.OutMax, intermediate01);
                var output01 = Mathf.Clamp01(outputWild);
                var output0100 = output01 * 100;
                
                foreach (var target in actuator.Targets)
                {
                    foreach (var blendshapeIndex in target.BlendshapeIndices)
                    {
                        target.Renderer.SetBlendShapeWeight(blendshapeIndex, output0100);
                    }
                }
            }
        }

        private void OnEnable()
        {
            _addressBase = definitions.Select(definition => definition.address).Distinct().ToList();
            if (_addressBase.Count > MaxAddresses)
            {
                Debug.LogError($"Exceeded max {MaxAddresses} addresses allowed in an actuator.");
                enabled = false;
                return;
            }

            if (feature.valueArraySize < _addressBase.Count)
            {
                Debug.LogError($"Feature does not have a large enough array size {feature.valueArraySize} to accomodate our actuators {_addressBase.Count}");
                enabled = false;
                return;
            }

            var smrToBlendshapeNames = new Dictionary<SkinnedMeshRenderer, List<string>>();
            foreach (var smr in renderers)
            {
                var mesh = smr.sharedMesh;
                smrToBlendshapeNames.Add(smr, Enumerable.Range(0, mesh.blendShapeCount)
                    .Select(i => mesh.GetBlendShapeName(i))
                    .ToList());
            }

            _computedActuators = definitions.Select(definition =>
                {
                    var actuatorTargets = ComputeTargets(smrToBlendshapeNames, definition.blendshapes, definition.onlyFirstMatch);
                    if (actuatorTargets.Length == 0) return null;

                    return new ComputedActuator
                    {
                        AddressIndex = _addressBase.IndexOf(definition.address),
                        InMin = definition.inMin,
                        InMax = definition.inMax,
                        OutMin = definition.outMin,
                        OutMax = definition.outMax,
                        UseCurve = definition.useCurve,
                        Curve = definition.curve,
                        Targets = actuatorTargets
                    };
                })
                .ToArray();
        }

        private ComputedActuatorTarget[] ComputeTargets(Dictionary<SkinnedMeshRenderer, List<string>> smrToBlendshapeNames, string[] definitionBlendshapes, bool onlyFirstMatch)
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
            public float InMin;
            public float InMax;
            public float OutMin;
            public float OutMax;
            public bool UseCurve;
            public AnimationCurve Curve;
            public ComputedActuatorTarget[] Targets;
        }

        private class ComputedActuatorTarget
        {
            public SkinnedMeshRenderer Renderer;
            public int[] BlendshapeIndices;
        }
    }

    [Serializable]
    public struct BlendshapeActuationDefinition
    {
        public string address;
        public float inMin;
        public float inMax;
        public float outMin;
        public float outMax;
        public bool useCurve;
        public AnimationCurve curve;
        public string[] blendshapes;
        // If a blendshape actuator definition is searching for multiple naming conventions,
        // and several exist, we don't want to actuate all of them. In this case, use onlyFirstMatch = true
        public bool onlyFirstMatch;
    }
}