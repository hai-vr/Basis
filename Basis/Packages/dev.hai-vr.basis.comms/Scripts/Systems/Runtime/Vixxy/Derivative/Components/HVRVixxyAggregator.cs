using System.Collections.Generic;
using HVR.Basis.Comms;
using HVR.Shared;
using UnityEngine;

namespace HVR.Vixxy.Runtime
{
    public class HVRVixxyAggregator : MonoBehaviour, IHVRVixxyAggregator
    {
        [SerializeField] private string addressA;
        [SerializeField] private string addressB;
        [SerializeField] private string outputAddress;
        [EarlyInjectable] public HVRVixxyOrchestrator orchestrator;
        [LateInjectable] public AcquisitionService acquisitionService;

        private readonly HashSet<HVRVixxyAggregator> _transformerResult = new();
        private readonly HashSet<IHVRVixxyActuator> _actuatorResult = new();

        private int _iddressA;
        private int _iddressB;
        private int _outputIddress;
        private float _activeResult = float.MinValue;
        private bool _hasNeverBeenAggregated = true;

        // FIXME: We need some way to initialize those values. It's probably the job of the orchestrator to do this.
        private float a = 0f;
        private float b = 0f;

        private void Awake()
        {
            acquisitionService = AcquisitionService.SceneInstance;
            _iddressA = HVRAddress.AddressToId(addressA);
            _iddressB = HVRAddress.AddressToId(addressB);
            _outputIddress = HVRAddress.AddressToId(outputAddress);

            if (string.IsNullOrEmpty(addressA) || string.IsNullOrEmpty(addressB) || string.IsNullOrEmpty(outputAddress))
            {
                HVR_VixxyUtil.LogUnusual(this, $"{nameof(HVRVixxyAggregator)} actuator named \"{name}\" has some missing addresses. It will be disabled.");
                enabled = false;
            }
        }

        private void OnEnable()
        {
            orchestrator.RegisterAggregator(_iddressA, this);
            orchestrator.RegisterAggregator(_iddressB, this);
            // FIXME: Address registration should be inside the orchestrator
            acquisitionService.RegisterAddresses(new []{ _iddressA, _iddressB }, OnAddressUpdated);
        }

        private void OnDisable()
        {
            orchestrator.UnregisterAggregator(_iddressA, this);
            orchestrator.UnregisterAggregator(_iddressB, this);
            // FIXME: Address registration should be inside the orchestrator
            acquisitionService.UnregisterAddresses(new []{ _iddressA, _iddressB }, OnAddressUpdated);
        }

        private void OnAddressUpdated(int inputIddress, float value)
        {
            if (inputIddress == _iddressA)
            {
                a = value;
            }
            else if (inputIddress == _iddressB)
            {
                b = value;
            }
            else return;
            
            orchestrator.PassAddressUpdated(inputIddress);
        }

        public bool TryAggregate(out IEnumerable<IHVRVixxyAggregator> aggregators, out IEnumerable<IHVRVixxyActuator> actuators)
        {
            var result = a * b;

            aggregators = _transformerResult;
            actuators = _actuatorResult;

            if (_hasNeverBeenAggregated || !Mathf.Approximately(_activeResult, result))
            {
                // First aggregation is always considered a successful aggregation, for initialization purposes.
                _hasNeverBeenAggregated = false;
                _activeResult = result;

                orchestrator.ProvideValue(_outputIddress, result);

                return true;
            }

            // Even if an input changes, if the output doesn't change, then it will not result in a change on the actuators.
            return false;
        }
    }
}
