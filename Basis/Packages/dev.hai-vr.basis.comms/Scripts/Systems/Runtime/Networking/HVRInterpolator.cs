using System.Collections.Generic;
using HVR.Basis.Comms.HVRUtility;

namespace HVR.Basis.Comms
{
    public class HVRInterpolator
    {
        private readonly Queue<HVRInterpolationSnapshot> _snapshots = new();
        private HVRInterpolationSnapshot _previousSnapshot;
        private HVRInterpolationSnapshot _currentSnapshot;
        private float _advanced = 0f;

        public void Add(HVRInterpolationSnapshot snapshot)
        {
            _snapshots.Enqueue(snapshot);
        }

        public Dictionary<int, float> Advance(float deltaTime)
        {
            Dictionary<int, float> result = new Dictionary<int, float>();
            _advanced += deltaTime;

            if (_currentSnapshot == null && _snapshots.Count > 0)
            {
                _currentSnapshot = _snapshots.Dequeue();
            }

            while (_currentSnapshot != null && _advanced >= _currentSnapshot.deltaTime)
            {
                foreach (var (addressId, value) in _currentSnapshot.addressIdsToValues)
                {
                    result.Add(addressId, value);
                }
                _advanced -= _currentSnapshot.deltaTime;
                _previousSnapshot = _currentSnapshot;

                if (_snapshots.Count > 0)
                {
                    _currentSnapshot = _snapshots.Dequeue();
                }
                else
                {
                    _currentSnapshot = null;
                }
            }

            if (_currentSnapshot != null)
            {
                if (_previousSnapshot != null)
                {
                    foreach (var (addressId, currentValue) in _currentSnapshot.addressIdsToValues)
                    {
                        var previousValue = _previousSnapshot.addressIdsToValues[addressId];
                        result[addressId] = Lerp(previousValue, currentValue, _advanced / _currentSnapshot.deltaTime);
                    }
                }
                else
                {
                    result = _currentSnapshot.addressIdsToValues;
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
