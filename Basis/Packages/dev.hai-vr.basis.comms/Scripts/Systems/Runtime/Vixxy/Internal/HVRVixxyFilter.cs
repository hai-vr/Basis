using System;
using UnityEngine;

namespace HVR.Vixxy
{
    public interface IHVRVixxyFilter
    {
        public bool isTimeFilter { get; }
        float Filter(float value);
        HVRVixxyFilterResult TimeFilter(float objectiveValue, float deltaTime);
        float PrimeFilter(float initialValue);
    }

    [Serializable]
    public class HVRVixxyFilterBase : IHVRVixxyFilter
    {
        public virtual bool isTimeFilter { get; }
        public virtual float Filter(float previousValue) { throw new NotImplementedException(); }
        public virtual HVRVixxyFilterResult TimeFilter(float objectiveValue, float deltaTime) { throw new NotImplementedException(); }
        public virtual float PrimeFilter(float initialValue) { throw new NotImplementedException(); }
    }

    [Serializable]
    public class HVRCurveVixxyFilter : HVRVixxyFilterBase
    {
        [SerializeField] public AnimationCurve curve = AnimationCurve.EaseInOut(0, 0, 1, 1);

        public override bool isTimeFilter => false;

        public override float Filter(float value)
        {
            return curve.Evaluate(value);
        }
    }

    [Serializable]
    public class HVRMoveTowardsVixxyFilter : HVRVixxyFilterBase
    {
        public float secondsPerUnit = 1f;

        private float previousValue;

        public override bool isTimeFilter => true;

        public override HVRVixxyFilterResult TimeFilter(float objectiveValue, float deltaTime)
        {
            if (secondsPerUnit <= 0f)
            {
                return new HVRVixxyFilterResult { result = objectiveValue, needsCheckNextTick = false };
            }

            var newValue = Mathf.MoveTowards(previousValue, objectiveValue, deltaTime / secondsPerUnit);
            var needsUpdateNextFrame = !Mathf.Approximately(newValue, objectiveValue);
            previousValue = newValue;

            return new HVRVixxyFilterResult
            {
                result = newValue,
                needsCheckNextTick = needsUpdateNextFrame
            };
        }

        public override float PrimeFilter(float initialValue)
        {
            previousValue = initialValue;
            return initialValue;
        }
    }

    [Serializable]
    public class HVRSmoothVixxyFilter : HVRVixxyFilterBase
    {
        public float secondsPerUnit = 1f;

        private float previousValue;

        public override bool isTimeFilter => true;

        public override HVRVixxyFilterResult TimeFilter(float objectiveValue, float deltaTime)
        {
            var unitsPerSecond = 1 / secondsPerUnit;

            var newValue = Mathf.Lerp(previousValue, objectiveValue, 1f - Mathf.Exp(-unitsPerSecond * deltaTime));
            if (Mathf.Abs(newValue - objectiveValue) < 0.00001f)
            {
                // Prevent endless actuation, as this filter is asymptotic
                newValue = objectiveValue;
            }
            var needsUpdateNextFrame = !Mathf.Approximately(newValue, objectiveValue);
            previousValue = newValue;

            return new HVRVixxyFilterResult
            {
                result = newValue,
                needsCheckNextTick = needsUpdateNextFrame
            };
        }

        public override float PrimeFilter(float initialValue)
        {
            previousValue = initialValue;
            return initialValue;
        }
    }

    public struct HVRVixxyFilterResult
    {
        public float result;
        public bool needsCheckNextTick;
    }
}
