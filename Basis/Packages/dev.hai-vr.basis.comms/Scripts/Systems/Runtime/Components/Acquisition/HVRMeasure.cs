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

        // Spherecast
        public float spherecastRadius = 0.5f;

        // Common
        public Transform source;
        public Transform target;
        public Transform target2;

        // Post-processing
        public Vector2 remapFrom = new(0f, 1f);
        public Vector2 remapTo = new(0f, 1f);
        public bool clampToBounds;

        // Output
        public HVRAddressSelectorToggle distanceAddress;
        public HVRAddressSelectorToggle hitAddress;
        public HVRAddressSelectorToggle changeOverTimeAddress;
        public bool differenceAbsoluteValue;

        private HVRAvatarComms _comms;
        [NonSerialized] internal string DistanceAddress; [NonSerialized] internal int DistanceAddressId;
        [NonSerialized] internal string HitAddress; [NonSerialized] internal int HitAddressId;
        [NonSerialized] internal string DifferenceAddress; [NonSerialized] internal int DifferenceAddressId;
        [NonSerialized] internal float LastIntermediateValue;
        [NonSerialized] internal float LastSentValue;
        [NonSerialized] internal float LastChangeOverTime;
        private bool _needToEvaluateDifferenceNextFrame;
        [NonSerialized] internal bool DebugForceUpdate;

        public void PruneUnusedReferences()
        {
            if (measurementType != HVRMeasureType.Angle)
            {
                target2 = null;
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
                if (measurementType == HVRMeasureType.ComplexRotationAngle)
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
                else if (measurementType is HVRMeasureType.Raycast or HVRMeasureType.Spherecast)
                {
                    if (target == null)
                    {
                        Gizmos.color = Color.cyan;
                        Gizmos.DrawLine(from.position, from.position + from.TransformVector(raycastDirection).normalized * Distance);
                    }
                    else
                    {
                        Gizmos.color = Color.red;
                        Gizmos.DrawLine(from.position, to.position);
                    }

                    if (measurementType == HVRMeasureType.Spherecast)
                    {
                        var transformationVectorInWorldSpace = from.TransformVector(raycastDirection.normalized);
                        var transformerUnit = transformationVectorInWorldSpace.magnitude;
                        Gizmos.DrawWireSphere(from.position, CalculateSpherecastRadiusInWorldSpace(transformerUnit));
                        if (target != null)
                        {
                            Gizmos.DrawWireSphere(to.position, CalculateSpherecastRadiusInWorldSpace(transformerUnit));
                        }
                    }
                }
            }
        }

        private void Awake()
        {
            _comms = HVRCommsUtil.GetComms(this);
            if (_comms == null) return;

            PruneUnusedReferences();

            (DistanceAddress, DistanceAddressId) = distanceAddress.isActive ? distanceAddress.address.ResolvePathOrDefaultToAvatar(this) : ("", 0);
            (HitAddress, HitAddressId) = hitAddress.isActive ? hitAddress.address.ResolvePathOrDefaultToAvatar(this) : ("", 0);
            (DifferenceAddress, DifferenceAddressId) = changeOverTimeAddress.isActive ? changeOverTimeAddress.address.ResolvePathOrDefaultToAvatar(this) : ("", 0);
        }

        private void OnEnable()
        {
            LastSentValue = HVR_VixxyUtil.BogusInitializationNumber;
            LastIntermediateValue = HVR_VixxyUtil.BogusInitializationNumber;
        }

        private void Update()
        {
            if (_comms == null) return;

            var from = source != null ? source : transform;
            var to = target != null ? target : transform;

            if (measurementType == HVRMeasureType.Distance)
            {
                var vectorInLocalSpaceOfFrom = from.InverseTransformPoint(to.position);
                var intermediateValue = vectorInLocalSpaceOfFrom.magnitude;

                ProcessAndSubmit(intermediateValue, true);
            }
            else if (measurementType == HVRMeasureType.Angle)
            {
                var to2 = target2 != null ? target2 : transform;
                var angleDeg = Vector3.Angle(target.position - from.position, to2.position - from.position);
                var intermediateValue = angleDeg;

                ProcessAndSubmit(intermediateValue, true);

            }
            else if (measurementType == HVRMeasureType.ComplexRotationAngle)
            {
                if (angleMeasurement == HVRMeasureAngleKind.IncludeRoll)
                {
                    var angleDeg = Quaternion.Angle(from.rotation, to.rotation);
                    var intermediateValue = angleDeg;

                    ProcessAndSubmit(intermediateValue, true);
                }
                else
                {
                    var dot = Vector3.Dot(from.forward, to.forward);
                    var angleDeg = Mathf.Acos(dot) * Mathf.Rad2Deg;
                    var intermediateValue = angleDeg;

                    ProcessAndSubmit(intermediateValue, true);
                }
            }
            else if (measurementType is HVRMeasureType.Raycast or HVRMeasureType.Spherecast)
            {
                Vector3 transformationVectorInWorldSpace;
                float transformerUnit;
                float allowedMaximumDistanceInWorldSpace;

                if (target == null)
                {
                    transformationVectorInWorldSpace = from.TransformVector(raycastDirection.normalized);
                    transformerUnit = transformationVectorInWorldSpace.magnitude;
                    var requestedMaximumDistanceInWorldSpace = transformerUnit * raycastMaximumDistance;
                    allowedMaximumDistanceInWorldSpace = Mathf.Min(requestedMaximumDistanceInWorldSpace, MaximumRaycastDistanceInWorldSpace);
                }
                else
                {
                    var targetPosition = target.position;
                    transformationVectorInWorldSpace = from.position - targetPosition;
                    transformerUnit = transformationVectorInWorldSpace.magnitude;
                    allowedMaximumDistanceInWorldSpace = transformationVectorInWorldSpace.magnitude;
                }

                var needsActualRaycast = distanceAddress.isActive || changeOverTimeAddress.isActive;
                if (needsActualRaycast)
                {
                    var ray = new Ray(from.position, transformationVectorInWorldSpace);

                    RaycastHit hitInfo;
                    bool hit;
                    if (measurementType == HVRMeasureType.Raycast)
                    {
                        hit = Physics.Raycast(ray, out hitInfo, allowedMaximumDistanceInWorldSpace);
                    }
                    else // Spherecast
                    {
                        hit = Physics.SphereCast(ray, CalculateSpherecastRadiusInWorldSpace(transformerUnit), out hitInfo, allowedMaximumDistanceInWorldSpace);
                    }

                    if (hit)
                    {
                        var worldSpaceVector = raycastDirection.normalized * hitInfo.distance;
                        var localSpaceVector = from.InverseTransformVector(worldSpaceVector);
                        var intermediateValue = localSpaceVector.magnitude;

                        ProcessAndSubmit(intermediateValue, true);
                    }
                    else
                    {
                        // TODO: Behaviour on miss

                        ProcessAndSubmit(allowedMaximumDistanceInWorldSpace, false);
                    }
                }
                else
                {
                    // If we don't need an actual raycast, just use the physics intersection methods.
                    bool hit;
                    if (measurementType == HVRMeasureType.Raycast)
                    {
                        var endPosition = target != null
                            ? target.position
                            : CalculateEndPositionInWorldSpace(from, transformationVectorInWorldSpace, allowedMaximumDistanceInWorldSpace);
                        hit = Physics.Linecast(from.position, endPosition);
                    }
                    else // Spherecast
                    {
                        if (allowedMaximumDistanceInWorldSpace == 0f)
                        {
                            hit = Physics.CheckSphere(from.position, CalculateSpherecastRadiusInWorldSpace(transformerUnit));
                        }
                        else
                        {
                            var endPosition = target != null
                                ? target.position
                                : CalculateEndPositionInWorldSpace(from, transformationVectorInWorldSpace, allowedMaximumDistanceInWorldSpace);
                            hit = Physics.CheckCapsule(from.position, endPosition, CalculateSpherecastRadiusInWorldSpace(transformerUnit));
                        }
                    }

                    ProcessAndSubmit(hit ? 1f : 0f, hit);
                }
            }
        }

        private static Vector3 CalculateEndPositionInWorldSpace(Transform from, Vector3 transformationVectorInWorldSpace, float allowedMaximumDistanceInWorldSpace)
        {
            return from.position + transformationVectorInWorldSpace * allowedMaximumDistanceInWorldSpace;
        }

        private float CalculateSpherecastRadiusInWorldSpace(float transformerUnit)
        {
            return transformerUnit * spherecastRadius;
        }

        private void ProcessAndSubmit(float intermediateValue, bool hit)
        {
            if (DebugForceUpdate || !Mathf.Approximately(LastIntermediateValue, intermediateValue))
            {
                var finalValue = Remap(remapFrom.x, remapFrom.y, remapTo.x, remapTo.y, intermediateValue);
                if (clampToBounds)
                {
                    finalValue = Mathf.Clamp01(finalValue);
                }

                if (distanceAddress.isActive) FinallySubmit(DistanceAddressId, finalValue);
                if (hitAddress.isActive) FinallySubmit(HitAddressId, hit ? 1f : 0f);
                if (changeOverTimeAddress.isActive)
                {
                    var changeOverTime = (finalValue - LastSentValue) / Time.deltaTime;
                    LastChangeOverTime = differenceAbsoluteValue ? Mathf.Abs(changeOverTime) : changeOverTime;
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
    public struct HVRAddressSelectorToggle
    {
        public bool isActive;
        public HVRAddressSelector address;
    }

    [Serializable]
    public enum HVRMeasureType
    {
        /// Measures the distance from source to target. The distance is measured in source's local space.<br/>
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
        ComplexRotationAngle,
        /// Shoots a physics raycast from source, in a direction specified by the source's local space. The distance is measured in source's local space.<br/>
        /// The maximum distance is specified in source's local space.<br/>
        /// <br/>
        /// If a target is specified, the raycast is towards that target, up to the distance to that target.<br/>
        /// <br/>
        /// The minimum value is 0, the maximum value depends:<br/>
        /// - If no target is specified, the maximum distance is the value of the constant HVRMeasure.MaximumRaycastDistanceInWorldSpace, or the maximum distance of the raycast, whichever is smaller.<br/>
        /// - If a target is specified, the maximum distance is 1, where 1 corresponds to the distance between the source and the target.
        Raycast,
        /// Same as raycast, but it's a sphere. The sphere radius is in source's local space.
        Spherecast,
    }

    [Serializable]
    public enum HVRMeasureAngleKind
    {
        DoNotIncludeRoll,
        IncludeRoll,
    }
}
