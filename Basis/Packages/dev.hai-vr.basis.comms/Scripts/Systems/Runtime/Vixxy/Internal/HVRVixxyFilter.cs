using System;
using UnityEngine;

namespace HVR.Vixxy
{
    public interface IHVRVixxyFilter
    {
        public bool isTimeFilter { get; }
        float Filter(float value);
        HVRVixxyFilterResult TimeFilter(float previousValue, float objectiveValue, float deltaTime);
    }

    [Serializable]
    public class HVRVixxyFilterBase : IHVRVixxyFilter
    {
        public virtual bool isTimeFilter { get; }
        public virtual float Filter(float previousValue) { throw new NotImplementedException(); }
        public virtual HVRVixxyFilterResult TimeFilter(float previousValue, float objectiveValue, float deltaTime) { throw new NotImplementedException(); }
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

        public override bool isTimeFilter => true;

        public override HVRVixxyFilterResult TimeFilter(float previousValue, float objectiveValue, float deltaTime)
        {
            if (secondsPerUnit <= 0f)
            {
                return new HVRVixxyFilterResult { result = objectiveValue, needsCheckNextTick = false };
            }

            var result = Mathf.MoveTowards(previousValue, objectiveValue, deltaTime / secondsPerUnit);
            var needsUpdateNextFrame = !Mathf.Approximately(result, objectiveValue);

            return new HVRVixxyFilterResult
            {
                result = result,
                needsCheckNextTick = needsUpdateNextFrame
            };
        }
    }

    public struct HVRVixxyFilterResult
    {
        public float result;
        public bool needsCheckNextTick;
    }
}
