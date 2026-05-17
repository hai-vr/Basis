using System.Collections.Generic;

namespace HVR.Basis.Comms
{
    public class HVRInterpolator
    {
        private readonly Queue<HVRInterpolationSnapshot> _snapshots = new();
        private readonly Dictionary<int, float> _memoryOfPreviousSnapshotValue = new();
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
                    result[addressId] = value;
                    _memoryOfPreviousSnapshotValue[addressId] = value;
                }
                _advanced -= _currentSnapshot.deltaTime;

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
                foreach (var (addressId, currentValue) in _currentSnapshot.addressIdsToValues)
                {
                    if (_memoryOfPreviousSnapshotValue.TryGetValue(addressId, out var previousValue))
                    {
                        result[addressId] = Lerp(previousValue, currentValue, _advanced / _currentSnapshot.deltaTime);
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
