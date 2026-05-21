using System;
using HVR.Vixxy;
using UnityEngine;

namespace HVR.Basis.Comms
{
    [HelpURL("https://docs.hai-vr.dev/docs/basis/avatar-customization/measure")]
    [AddComponentMenu("HVR.Basis/HVR Measure")]
    public class HVRMeasure : MonoBehaviour
    {
        private const int MaximumRaycastDistanceInWorldSpace = 10_000;
        private const float Distance = 0.5f;
        public HVRMeasureType measurementType;

        // Angle
        public HVRMeasureAngleKind angleMeasurement;

        // Raycast
        public Vector3 raycastDirection = Vector3.forward;
        public float raycastMaximumDistance = 100f;
        public bool raycastIsSpherecast;

        // Raycast/Spherecast
        public float physicsSphereRadius = 0.5f;

        // Speed
        public HVRMeasureSpeedKind speedMeasurement;
        public Vector3 speedProjection = Vector3.up;

        // Common
        public Transform source;
        public Transform target;
        public Transform target2;

        public HVRMeasureSpace space;
        public Transform spaceReference;

        // Convert Range
        public Vector2 remapFrom = new(0f, 1f);
        public Vector2 remapTo = new(0f, 1f);
        public bool clampToBounds = true;

        // Output
        public HVRAddressSelectorToggle valueAddress;
        public HVRAddressSelectorToggle hitAddress;
        public HVRAddressSelectorToggle changeOverTimeAddress;
        public bool differenceAbsoluteValue;

        private HVRAvatarComms _comms;
        private Transform _contextObject;
        [NonSerialized] internal string DistanceAddress; [NonSerialized] internal int DistanceAddressId;
        [NonSerialized] internal string HitAddress; [NonSerialized] internal int HitAddressId;
        [NonSerialized] internal string DifferenceAddress; [NonSerialized] internal int DifferenceAddressId;
        [NonSerialized] internal float LastIntermediateValue;
        [NonSerialized] internal float LastSentValue;
        [NonSerialized] internal float LastChangeOverTime;
        private bool _needToEvaluateDifferenceNextFrame;
        [NonSerialized] internal bool DebugForceUpdate;
        private bool _isFirstDerivative;
        private Vector3 _previousVectorInAnySpace;

        public void PruneUnusedReferences()
        {
            if (space != HVRMeasureSpace.Custom)
            {
                spaceReference = null;
            }

            if (measurementType != HVRMeasureType.Angle && measurementType != HVRMeasureType.RotationDifference)
            {
                // Angles do not differ based on space.
                spaceReference = null;
            }

            if (measurementType != HVRMeasureType.Angle)
            {
                // Only Angle has target2
                target2 = null;
            }

            if (measurementType == HVRMeasureType.Speed)
            {
                // Speed has no target
                target = null;
            }

            if (measurementType != HVRMeasureType.Raycast)
            {
                // Only Raycast has hitAddress
                hitAddress.isActive = false;
            }
            else
            {
                // Raycast does not have target
                target = null;
            }
        }

        private void OnDrawGizmos()
        {
            var from = source != null ? source : transform;
            var to = target != null ? target : transform;

            if (measurementType == HVRMeasureType.Distance)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawLine(from.position, to.position);
            }
            else
            {
                if (measurementType == HVRMeasureType.RotationDifference)
                {
                    Gizmos.color = Color.red;
                    Gizmos.DrawLine(from.position, from.position + from.forward * Distance);
                    Gizmos.DrawLine(from.position, from.position + to.forward * Distance);
                }
                else if (measurementType == HVRMeasureType.Angle)
                {
                    var to2 = target2 != null ? target2 : transform;
                    Gizmos.color = Color.red;
                    Gizmos.DrawLine(from.position, to.position);
                    Gizmos.DrawLine(from.position, to2.position);
                }
                else if (measurementType == HVRMeasureType.Raycast)
                {
                    Gizmos.color = Color.red;
                    Gizmos.DrawLine(from.position, to.position);

                    if (raycastIsSpherecast)
                    {
                        var transformationVectorInWorldSpace = from.TransformVector(raycastDirection.normalized);
                        var transformerUnit = transformationVectorInWorldSpace.magnitude;
                        Gizmos.DrawWireSphere(from.position, CalculateSpherecastRadiusInWorldSpace(transformerUnit));
                        Gizmos.DrawWireSphere(to.position, CalculateSpherecastRadiusInWorldSpace(transformerUnit));
                    }
                }
            }
        }

        private void Awake()
        {
            _comms = HVRCommsUtil.GetComms(this);
            _contextObject = HVRCommsUtil.GetAvatar(this).transform;
            if (_comms == null) return;

            PruneUnusedReferences();

            (DistanceAddress, DistanceAddressId) = valueAddress.isActive ? valueAddress.address.ResolvePathOrDefaultToAvatar(this) : ("", 0);
            (HitAddress, HitAddressId) = hitAddress.isActive ? hitAddress.address.ResolvePathOrDefaultToAvatar(this) : ("", 0);
            (DifferenceAddress, DifferenceAddressId) = changeOverTimeAddress.isActive ? changeOverTimeAddress.address.ResolvePathOrDefaultToAvatar(this) : ("", 0);
        }

        private void OnEnable()
        {
            LastSentValue = HVR_VixxyUtil.BogusInitializationNumber;
            LastIntermediateValue = HVR_VixxyUtil.BogusInitializationNumber;
            _isFirstDerivative = true;
        }

        private void Update()
        {
            if (_comms == null) return;

            var actualMeasurementSpaceOrNull = space == HVRMeasureSpace.Custom && spaceReference != null
                ? spaceReference
                : space == HVRMeasureSpace.ContextSpace
                ? _contextObject
                : null;

            var from = source != null ? source : transform;
            var to = target != null ? target : transform;

            if (measurementType == HVRMeasureType.Distance)
            {
                Vector3 vectorInMeasurementSpace;
                if (actualMeasurementSpaceOrNull != null)
                {
                    var fromInLocalSpaceOfSpace = actualMeasurementSpaceOrNull.InverseTransformPoint(from.position);
                    var toInLocalSpaceOfSpace = actualMeasurementSpaceOrNull.InverseTransformPoint(to.position);
                    vectorInMeasurementSpace = fromInLocalSpaceOfSpace - toInLocalSpaceOfSpace;
                }
                else
                {
                    vectorInMeasurementSpace = from.position - to.position;
                }

                var intermediateValue = vectorInMeasurementSpace.magnitude;

                ProcessAndSubmit(intermediateValue, true);
            }
            else if (measurementType == HVRMeasureType.Angle)
            {
                var to2 = target2 != null ? target2 : transform;
                var angleDeg = Vector3.Angle(target.position - from.position, to2.position - from.position);
                var intermediateValue = angleDeg;

                ProcessAndSubmit(intermediateValue, true);
            }
            else if (measurementType == HVRMeasureType.RotationDifference)
            {
                if (angleMeasurement == HVRMeasureAngleKind.IncludeRoll)
                {
                    var angleDeg = Quaternion.Angle(from.rotation, to.rotation);
                    var intermediateValue = angleDeg;

                    ProcessAndSubmit(intermediateValue, true);
                }
                else
                {
                    var angleDeg = Vector3.Angle(from.forward, to.forward);
                    var intermediateValue = angleDeg;

                    ProcessAndSubmit(intermediateValue, true);
                }
            }
            else if (measurementType == HVRMeasureType.Speed)
            {
                var vectorInAnySpace = actualMeasurementSpaceOrNull != null
                    ? actualMeasurementSpaceOrNull.InverseTransformPoint(from.position)
                    : from.position;

                if (speedMeasurement == HVRMeasureSpeedKind.ProjectOnPlane2D)
                {
                    vectorInAnySpace = Vector3.ProjectOnPlane(vectorInAnySpace, speedProjection);
                }
                else if (speedMeasurement == HVRMeasureSpeedKind.ProjectOnLine1D)
                {
                    vectorInAnySpace = Vector3.Project(vectorInAnySpace, speedProjection);
                }

                if (_isFirstDerivative)
                {
                    ProcessAndSubmit(0f, true);
                }
                else
                {
                    var speed = (vectorInAnySpace - _previousVectorInAnySpace).magnitude / Time.deltaTime;
                    ProcessAndSubmit(speed, true);
                }
                _previousVectorInAnySpace = vectorInAnySpace;
            }
            else if (measurementType == HVRMeasureType.Raycast)
            {
                var raycastDirectionInWorldSpace = from.TransformVector(raycastDirection.normalized);
                var unit = actualMeasurementSpaceOrNull != null ? actualMeasurementSpaceOrNull.TransformVector(raycastDirection.normalized).magnitude : 1f;
                var requestedMaximumDistanceInWorldSpace = unit * raycastMaximumDistance;
                var allowedMaximumDistanceInWorldSpace = Mathf.Min(requestedMaximumDistanceInWorldSpace, MaximumRaycastDistanceInWorldSpace);

                var needsActualRaycast = valueAddress.isActive || changeOverTimeAddress.isActive;
                if (needsActualRaycast)
                {
                    var ray = new Ray(from.position, raycastDirectionInWorldSpace);

                    RaycastHit hitInfo;
                    bool hit;
                    if (!raycastIsSpherecast || physicsSphereRadius <= 0f)
                    {
                        hit = Physics.Raycast(ray, out hitInfo, allowedMaximumDistanceInWorldSpace);
                    }
                    else
                    {
                        hit = Physics.SphereCast(ray, CalculateSpherecastRadiusInWorldSpace(unit), out hitInfo, allowedMaximumDistanceInWorldSpace);
                    }

                    if (hit)
                    {
                        var intermediateValue = hitInfo.distance / unit;
                        ProcessAndSubmit(intermediateValue, true);
                    }
                    else
                    {
                        // TODO: Behaviour on miss
                        ProcessAndSubmit(1f, false);
                    }
                }
                else
                {
                    // If we don't need an actual raycast, just use the physics intersection methods.
                    bool hit;
                    if (!raycastIsSpherecast || physicsSphereRadius <= 0f)
                    {
                        var endPosition = CalculateEndPositionInWorldSpace(from, raycastDirectionInWorldSpace, allowedMaximumDistanceInWorldSpace);
                        hit = Physics.Linecast(from.position, endPosition);
                    }
                    else
                    {
                        if (allowedMaximumDistanceInWorldSpace == 0f)
                        {
                            hit = Physics.CheckSphere(from.position, CalculateSpherecastRadiusInWorldSpace(unit));
                        }
                        else
                        {
                            var endPosition = CalculateEndPositionInWorldSpace(from, raycastDirectionInWorldSpace, allowedMaximumDistanceInWorldSpace);
                            hit = Physics.CheckCapsule(from.position, endPosition, CalculateSpherecastRadiusInWorldSpace(unit));
                        }
                    }

                    ProcessAndSubmit(hit ? 1f : 0f, hit);
                }
            }

            if (_isFirstDerivative) _isFirstDerivative = false;
        }

        private static Vector3 CalculateEndPositionInWorldSpace(Transform from, Vector3 transformationVectorInWorldSpace, float allowedMaximumDistanceInWorldSpace)
        {
            return from.position + transformationVectorInWorldSpace * allowedMaximumDistanceInWorldSpace;
        }

        private float CalculateSpherecastRadiusInWorldSpace(float transformerUnit)
        {
            return transformerUnit * physicsSphereRadius;
        }

        private void ProcessAndSubmit(float intermediateValue, bool hit)
        {
            if (DebugForceUpdate || !Mathf.Approximately(LastIntermediateValue, intermediateValue))
            {
                var finalValue = Remap(remapFrom.x, remapFrom.y, remapTo.x, remapTo.y, intermediateValue);
                if (clampToBounds)
                {
                    finalValue = remapTo.x < remapTo.y
                        ? Mathf.Clamp(finalValue, remapTo.x, remapTo.y)
                        : Mathf.Clamp(finalValue, remapTo.y, remapTo.x);
                }

                if (valueAddress.isActive) FinallySubmit(DistanceAddressId, finalValue);
                if (hitAddress.isActive) FinallySubmit(HitAddressId, hit ? 1f : 0f);
                if (changeOverTimeAddress.isActive)
                {
                    if (!_isFirstDerivative)
                    {
                        var changeOverTime = (finalValue - LastSentValue) / Time.deltaTime;
                        LastChangeOverTime = differenceAbsoluteValue ? Mathf.Abs(changeOverTime) : changeOverTime;
                    }
                    else
                    {
                        LastChangeOverTime = 0f;
                    }

                    FinallySubmit(DifferenceAddressId, LastChangeOverTime);
                }

                LastIntermediateValue = intermediateValue;
                LastSentValue = finalValue;
                if (changeOverTimeAddress.isActive)
                {
                    _needToEvaluateDifferenceNextFrame = true;
                }
            }
            else if (_needToEvaluateDifferenceNextFrame)
            {
                LastChangeOverTime = 0f;
                if (changeOverTimeAddress.isActive) FinallySubmit(DifferenceAddressId, LastChangeOverTime);
                _needToEvaluateDifferenceNextFrame = false;
            }
        }

        private void FinallySubmit(int addressId, float value)
        {
            // This shouldn't be null at runtime, but it helps with testing the measurement in Play Mode without needing to load into the avatar,
            // as the VariableStore is only created when OnAvatarReady is called.
            if (_comms.VariableStore != null)
            {
                _comms.VariableStore.SubmitOrDefineDefaultValue(addressId, value);
            }
        }

        private static float InverseLerpUnclamped(float a, float b, float value)
        {
            return (value - a) / (b - a);
        }

        private static float Remap(float startA, float endA, float startB, float endB, float value)
        {
            return startB + (value - startA) * (endB - startB) / (endA - startA);
        }
    }

    [Serializable]
    public enum HVRMeasureSpace
    {
        ContextSpace,
        WorldSpace,
        Custom,
    }

    [Serializable]
    public struct HVRAddressSelectorToggle
    {
        public bool isActive;
        public HVRAddressSelector address;
    }

    [Serializable]
    public enum HVRMeasureType
    {
        /// Measures the distance from source to target.<br/>
        /// <br/>
        /// The minimum value is 0, and there is no maximum value except game engine limits.
        Distance,
        /// Measures the angle between (origin, target A), and (origin, target B).<br/>
        /// <br/>
        /// The minimum value is 0, the maximum value is 180.
        Angle,
        /// Measures the angle between source and target transform rotations, or the angle between source and target's transform forward direction in world space.<br/>
        /// <br/>
        /// The minimum value is 0, the maximum value is 180.
        RotationDifference,
        /// Shoots a physics raycast from source, in a direction specified by the source's local space.<br/>
        /// <br/>
        /// If a target is specified, the raycast is towards that target, up to the distance to that target.<br/>
        /// <br/>
        /// The minimum value is 0, the maximum value depends:<br/>
        /// - If no target is specified, the maximum distance is the value of the constant HVRMeasure.MaximumRaycastDistanceInWorldSpace, or the maximum distance of the raycast, whichever is smaller.<br/>
        /// - If a target is specified, the maximum distance is 1, where 1 corresponds to the distance between the source and the target.
        Raycast,
        /// Measures the speed of the object.
        Speed,
        //UnityColliderTrigger,
        //UnityColliderPhysics,
        //ParticleCollision,
    }

    [Serializable]
    public enum HVRMeasureAngleKind
    {
        DoNotIncludeRoll,
        IncludeRoll,
    }

    [Serializable]
    public enum HVRMeasureSpeedKind
    {
        ThreeDimensional,
        ProjectOnPlane2D,
        ProjectOnLine1D,
    }
}
