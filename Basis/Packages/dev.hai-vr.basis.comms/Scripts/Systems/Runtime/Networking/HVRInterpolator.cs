using System.Collections.Generic;
using HVR.Basis.Comms.HVRUtility;
using UnityEngine;

namespace HVR.Basis.Comms
{
    public class HVRInterpolator
    {
        private const float MinimumDurationInQueueToCatchUp = 0.2f;
        private const float MaximumDurationInQueueToCatchUp = 4f;
        private const float SlowCatchUpMultiplier = 0.66f;
        private const float FastCatchUpMultiplier = 0.05f;

        private readonly Queue<HVRInterpolationSnapshot> _snapshots = new();
        private readonly Dictionary<int, float> _memoryOfPreviousSnapshotValue = new();
        private HVRInterpolationSnapshot _currentSnapshot;
        private float _advanced;

        private float _currentAdjustedDeltaTime;
        private bool _doCatchUp = false;

        public HVRInterpolator(bool doCatchUp)
        {
            _doCatchUp = doCatchUp;
        }

        public void Add(HVRInterpolationSnapshot snapshot)
        {
            _snapshots.Enqueue(snapshot);
        }

        public void SetCatchUp(bool doCatchUp)
        {
            _doCatchUp = doCatchUp;
        }

        private void TryDequeue()
        {
            if (_snapshots.Count > 0)
            {
                var totalQueueSeconds = 0f;
                foreach (var snapshot in _snapshots)
                {
                    totalQueueSeconds += snapshot.deltaTime;
                }

                _currentSnapshot = _snapshots.Dequeue();

                var needToCatchUp = _doCatchUp && totalQueueSeconds >= MinimumDurationInQueueToCatchUp;
                if (needToCatchUp)
                {
                    var howFastToRecover01 = Mathf.InverseLerp(MinimumDurationInQueueToCatchUp, MaximumDurationInQueueToCatchUp, totalQueueSeconds);
                    var multiplierToCatchUp = Mathf.Lerp(SlowCatchUpMultiplier, FastCatchUpMultiplier, howFastToRecover01);
                    _currentAdjustedDeltaTime = _currentSnapshot.deltaTime * multiplierToCatchUp;
                }
                else
                {
                    _currentAdjustedDeltaTime = _currentSnapshot.deltaTime;
                }

                HVRLogging.Debug($"Adjusted delta time is {_currentAdjustedDeltaTime}");
            }
            else
            {
                _currentSnapshot = null;
            }
        }

        public Dictionary<int, float> Advance(float deltaTime)
        {
            Dictionary<int, float> result = new Dictionary<int, float>();
            _advanced += deltaTime;

            if (_currentSnapshot == null)
            {
                TryDequeue();
            }

            while (_currentSnapshot != null && _advanced >= _currentAdjustedDeltaTime)
            {
                foreach (var (addressId, value) in _currentSnapshot.addressIdsToValues)
                {
                    result[addressId] = value;
                    _memoryOfPreviousSnapshotValue[addressId] = value;
                }
                _advanced -= _currentAdjustedDeltaTime;

                TryDequeue();
            }

            if (_currentSnapshot != null)
            {
                foreach (var (addressId, currentValue) in _currentSnapshot.addressIdsToValues)
                {
                    if (_memoryOfPreviousSnapshotValue.TryGetValue(addressId, out var previousValue))
                    {
                        result[addressId] = Lerp(previousValue, currentValue, _advanced / _currentAdjustedDeltaTime);
                    }
                    else
                    {
                        result[addressId] = currentValue;
                    }
                }
            }

            if (result.Count == 0)
            {
                _advanced = 0f;
            }

            return result;
        }

        private float Lerp(float from, float to, float amount01)
        {
            return from + (to - from) * amount01;
        }
    }

    public class HVRInterpolationSnapshot
    {
        public float deltaTime;
        public Dictionary<int, float> addressIdsToValues = new();
    }
}
