using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace UnityEngine.Animations.Rigging
{
    /// <summary>
    /// The Spine IK constraint data based on PDO-IK distance optimization.
    /// </summary>
    [System.Serializable]
    public struct BasisSpineIKConstraintData : IAnimationJobData, BasisISpineIKConstraintData
    {
        [SerializeField] Transform m_Hips;
        [SerializeField] Transform[] m_SpineJoints;
        [SerializeField] Transform m_Head;

        [SyncSceneToStream, SerializeField]
        public Vector3 headTargetPosition;
        [SyncSceneToStream, SerializeField]
        public Vector3 headTargetRotation;
        [SyncSceneToStream, SerializeField]
        public Vector3 hipsTargetPosition;
        [SyncSceneToStream, SerializeField]
        public Vector3 hipsTargetRotation;

        [SyncSceneToStream, SerializeField]
        public float ChainWeight;

        [SyncSceneToStream, SerializeField]
        public bool MaintainSpineLength;

        // Interface implementation using head-specific naming
        Vector3 BasisISpineIKConstraintData.headTargetPosition => headTargetPosition;
        Vector3 BasisISpineIKConstraintData.headTargetRotation => headTargetRotation;
        Vector3 BasisISpineIKConstraintData.hipsTargetPosition => hipsTargetPosition;
        Vector3 BasisISpineIKConstraintData.hipsTargetRotation => hipsTargetRotation;
        float BasisISpineIKConstraintData.chainWeight => ChainWeight;
        Vector3[] BasisISpineIKConstraintData.originalDistances => m_OriginalDistances;
        Quaternion[] BasisISpineIKConstraintData.originalRelativeRotations => m_OriginalRelativeRotations;

        public Transform hips { get => m_Hips; set => m_Hips = value; }
        public Transform[] spineJoints { get => m_SpineJoints; set => m_SpineJoints = value; }
        public Transform head { get => m_Head; set => m_Head = value; }
        public float chainWeight { get => ChainWeight; set => ChainWeight = value; }
        public bool maintainSpineLength { get => MaintainSpineLength; set => MaintainSpineLength = value; }

        string BasisISpineIKConstraintData.hipsTargetPositionVector3Property => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(hipsTargetPosition));
        string BasisISpineIKConstraintData.hipsTargetRotationVector3Property => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(hipsTargetRotation));
        string BasisISpineIKConstraintData.chainWeightFloatProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(ChainWeight));
        string BasisISpineIKConstraintData.headTargetPositionVector3Property => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(headTargetPosition));
        string BasisISpineIKConstraintData.headTargetRotationVector3Property => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(headTargetRotation));

        [SerializeField] public Vector3[] m_OriginalDistances;           // length == (hips + joints + head) => debugCount
        [SerializeField] public Quaternion[] m_OriginalRelativeRotations; // length == (segments) => debugCount - 1

        public Vector3[] originalDistances => m_OriginalDistances;
        public Quaternion[] originalRelativeRotations => m_OriginalRelativeRotations;

        bool IAnimationJobData.IsValid() =>
            (m_Hips != null &&
             m_Head != null &&
             m_SpineJoints != null &&
             m_SpineJoints.Length > 0 &&
             IsValidSpineChain());

        private bool IsValidSpineChain()
        {
            if (m_SpineJoints == null || m_SpineJoints.Length == 0) return false;

            Transform current = m_Hips;
            for (int i = 0; i < m_SpineJoints.Length; i++)
            {
                if (m_SpineJoints[i] == null) return false;

                float distance = Vector3.Distance(current.position, m_SpineJoints[i].position);
                if (distance > 2.0f || distance < 0.01f)
                {
                    Debug.LogWarning($"Unusual spine segment length: {distance}m between {current.name} and {m_SpineJoints[i].name}");
                }
                current = m_SpineJoints[i];
            }

            float headDistance = Vector3.Distance(current.position, m_Head.position);
            if (headDistance > 1.0f || headDistance < 0.01f)
            {
                Debug.LogWarning($"Unusual head distance: {headDistance}m");
            }

            return true;
        }

        void IAnimationJobData.SetDefaultValues()
        {
            m_Hips = null;
            m_SpineJoints = new Transform[0];
            m_Head = null;
            ChainWeight = 1.0f;
            MaintainSpineLength = true;

            if (m_SpineJoints != null && m_SpineJoints.Length > 0)
            {
                CalibrateOriginalDistances();
            }
        }

        /// <summary>
        /// Calibrates original distances INCLUDING the final segment from last spine joint to head.
        /// Layout:
        /// - m_OriginalDistances[0] = Vector3.zero (root padding)
        /// - For each segment k (hips->spine0, spine0->spine1, ..., spineN-1->spineN, spineN->head):
        ///     m_OriginalDistances[k+1] = next.position - prev.position
        /// Thus, m_OriginalDistances.Length == (hips + spineCount + head) = debugCount
        /// and there are (debugCount - 1) segments total.
        /// m_OriginalRelativeRotations mirrors the segments count (debugCount - 1).
        /// </summary>
        private void CalibrateOriginalDistances()
        {
            if (m_SpineJoints == null || m_SpineJoints.Length == 0 || m_Hips == null || m_Head == null)
                return;

            // Build the full transform chain: hips, each spine joint, head
            int spineCount = m_SpineJoints.Length;
            int nodeCount = spineCount + 2; // hips + spineJoints + head
            Transform[] chain = new Transform[nodeCount];
            chain[0] = m_Hips;
            for (int i = 0; i < spineCount; i++)
                chain[i + 1] = m_SpineJoints[i];
            chain[nodeCount - 1] = m_Head;

            // Distances: root padding + one per segment => length == nodeCount
            m_OriginalDistances = new Vector3[nodeCount];
            m_OriginalDistances[0] = Vector3.zero;

            // Relative rotations: one per segment => length == nodeCount - 1
            m_OriginalRelativeRotations = new Quaternion[nodeCount - 1];

            for (int seg = 0; seg < nodeCount - 1; seg++)
            {
                Transform prev = chain[seg];
                Transform next = chain[seg + 1];

                Vector3 offset = next.position - prev.position;
                m_OriginalDistances[seg + 1] = offset;

                Vector3 parentForward = prev.rotation * Vector3.forward;
                Vector3 jointDirection = offset.normalized;

                m_OriginalRelativeRotations[seg] = (jointDirection.sqrMagnitude > 0.001f)
                    ? Quaternion.FromToRotation(parentForward, jointDirection)
                    : Quaternion.identity;
            }
        }
    }

    [DisallowMultipleComponent, AddComponentMenu("Animation Rigging/Spine IK Constraint")]
    [HelpURL("https://docs.unity3d.com/Packages/com.unity.animation.rigging@1.3/manual/constraints/SpineIKConstraint.html")]
    public class BasisSpineIKConstraint : RigConstraint<BasisSpineIKConstraintJob, BasisSpineIKConstraintData, BasisSpineIKConstraintJobBinder<BasisSpineIKConstraintData>>
    {
        // ===== Gizmo/Debug state shared with the job (allocated in the binder) =====
        // Layout: [0]=hips, [1..N]=spine joints, [last]=head
        internal NativeArray<Vector3> debugPositions;   // size = spineCount + 2
        internal NativeArray<float> debugLengths;       // size = spineCount + 1
        internal int debugCount;

        // ===== Gizmo options =====
        [Header("Gizmos")]
        [SerializeField] bool m_DrawGizmos = true;
        [SerializeField] float m_JointSphereSize = 0.02f;
        [SerializeField] bool m_DrawLengths = true;
        [SerializeField] bool m_DrawLabels = true;

        [Header("Directions / Axes")]
        [SerializeField] bool m_DrawSegmentArrows = true;
        [SerializeField] float m_ArrowSize = 0.06f;
        [SerializeField] bool m_DrawLocalAxes = true;
        [SerializeField] float m_AxisSize = 0.05f;

        [Header("Original vs Current")]
        [SerializeField] bool m_DrawOriginalDirections = true;
        [SerializeField] bool m_DrawLengthError = true;
        [SerializeField] bool m_DrawTotalLength = true;

        [Header("Misc")]
        [SerializeField] bool m_DrawCenterOfMass = false;

        protected override void OnValidate()
        {
            base.OnValidate();
            m_Data.chainWeight = Mathf.Clamp01(m_Data.chainWeight);
            m_JointSphereSize = Mathf.Max(0.0f, m_JointSphereSize);
            m_ArrowSize = Mathf.Max(0.0f, m_ArrowSize);
            m_AxisSize = Mathf.Max(0.0f, m_AxisSize);
        }

        void OnDestroy()
        {
            // Ownership is in the binder; just clear our views.
            if (debugPositions.IsCreated || debugLengths.IsCreated)
            {
                debugPositions = default;
                debugLengths = default;
                debugCount = 0;
            }
        }

        public void CalibrateSpine()
        {
            if (m_Data.spineJoints != null && m_Data.spineJoints.Length > 0)
            {
                ((IAnimationJobData)m_Data).SetDefaultValues();
            }
        }

        [ContextMenu("Debug Spine Setup")]
        public void DebugSpineSetup()
        {
            Debug.Log("=== SPINE DEBUG SETUP ===");
            Debug.Log($"Hips: {(m_Data.hips ? m_Data.hips.name : "NULL")}");
            Debug.Log($"Head: {(m_Data.head ? m_Data.head.name : "NULL")}");
            Debug.Log($"Spine joints count: {(m_Data.spineJoints?.Length ?? 0)}");

            if (m_Data.spineJoints != null)
            {
                for (int i = 0; i < m_Data.spineJoints.Length; i++)
                {
                    if (m_Data.spineJoints[i] != null)
                        Debug.Log($"Spine[{i}]: {m_Data.spineJoints[i].name} at {m_Data.spineJoints[i].position}");
                    else
                        Debug.Log($"Spine[{i}]: NULL");
                }
            }

            Debug.Log($"Original distances count: {(m_Data.originalDistances?.Length ?? 0)}");
            if (m_Data.originalDistances != null)
            {
                for (int i = 0; i < m_Data.originalDistances.Length; i++)
                    Debug.Log($"OriginalDistance[{i}]: {m_Data.originalDistances[i]} (magnitude: {m_Data.originalDistances[i].magnitude:F4})");
            }

            Debug.Log($"Original relative rotations count: {(m_Data.originalRelativeRotations?.Length ?? 0)}");
            if (m_Data.originalRelativeRotations != null)
            {
                for (int i = 0; i < m_Data.originalRelativeRotations.Length; i++)
                {
                    Vector3 eulerAngles = m_Data.originalRelativeRotations[i].eulerAngles;
                    Debug.Log($"OriginalRelativeRotation[{i}]: {m_Data.originalRelativeRotations[i]} (euler: {eulerAngles})");
                }
            }
        }

        void OnDrawGizmos()
        {
            if (!m_DrawGizmos) return;
            if (!Application.isPlaying) return;

            if (!debugPositions.IsCreated || !debugLengths.IsCreated || debugCount <= 0)
                return;

            Gizmos.matrix = Matrix4x4.identity;

            // Draw joints as spheres + labels
            for (int i = 0; i < debugCount; i++)
            {
                Vector3 p = debugPositions[i];
                Gizmos.DrawSphere(p, m_JointSphereSize);
#if UNITY_EDITOR
                if (m_DrawLabels)
                    UnityEditor.Handles.Label(p, i == 0 ? "Hips" : (i == debugCount - 1 ? "Head" : $"Spine[{i - 1}]"));
#endif
            }

            // Draw bone lines + (optionally) length labels
            for (int i = 0; i < debugCount - 1; i++)
            {
                Vector3 a = debugPositions[i];
                Vector3 b = debugPositions[i + 1];
                Gizmos.DrawLine(a, b);
#if UNITY_EDITOR
                if (m_DrawLengths)
                {
                    var mid = (a + b) * 0.5f;
                    UnityEditor.Handles.Label(mid, $"{debugLengths[i]:F3}m");
                }
#endif
            }

#if UNITY_EDITOR
            // === Editor-only arrows/axes/labels ===
            using (new UnityEditor.Handles.DrawingScope(Matrix4x4.identity))
            {
                // Segment direction arrows (current)
                if (m_DrawSegmentArrows)
                {
                    for (int i = 0; i < debugCount - 1; i++)
                    {
                        Vector3 a = debugPositions[i];
                        Vector3 b = debugPositions[i + 1];
                        Vector3 dir = (b - a);
                        if (dir.sqrMagnitude > 1e-8f)
                        {
                            dir.Normalize();
                            UnityEditor.Handles.ArrowHandleCap(0, a, Quaternion.LookRotation(dir), m_ArrowSize, EventType.Repaint);
                        }
                    }
                }

                // Local axes on each joint (from live transforms)
                if (m_DrawLocalAxes)
                {
                    for (int i = 0; i < debugCount; i++)
                    {
                        Transform t = null;
                        if (i == 0) t = m_Data.hips;
                        else if (i == debugCount - 1) t = m_Data.head;
                        else if (m_Data.spineJoints != null) t = m_Data.spineJoints[i - 1];

                        if (t == null) continue;

                        Vector3 p = t.position;
                        // X axis
                        UnityEditor.Handles.color = Color.red;
                        UnityEditor.Handles.DrawLine(p, p + t.right * m_AxisSize);
                        UnityEditor.Handles.ArrowHandleCap(0, p + t.right * m_AxisSize, Quaternion.LookRotation(t.right), m_AxisSize * 0.6f, EventType.Repaint);

                        // Y axis
                        UnityEditor.Handles.color = Color.green;
                        UnityEditor.Handles.DrawLine(p, p + t.up * m_AxisSize);
                        UnityEditor.Handles.ArrowHandleCap(0, p + t.up * m_AxisSize, Quaternion.LookRotation(t.up), m_AxisSize * 0.6f, EventType.Repaint);

                        // Z axis
                        UnityEditor.Handles.color = Color.blue;
                        UnityEditor.Handles.DrawLine(p, p + t.forward * m_AxisSize);
                        UnityEditor.Handles.ArrowHandleCap(0, p + t.forward * m_AxisSize, Quaternion.LookRotation(t.forward), m_AxisSize * 0.6f, EventType.Repaint);
                    }

                    UnityEditor.Handles.color = Color.white; // reset
                }

                // Original vs current comparisons + errors
                // With head included, originalDistances.Length == debugCount.
                if (m_Data.originalDistances != null && m_Data.originalDistances.Length == debugCount)
                {
                    float originalTotal = 0f;
                    float currentTotal = 0f;

                    for (int i = 0; i < debugCount - 1; i++)
                    {
                        Vector3 a = debugPositions[i];
                        Vector3 b = debugPositions[i + 1];
                        Vector3 curr = (b - a);
                        float currLen = curr.magnitude;
                        currentTotal += currLen;

                        Vector3 orig = m_Data.originalDistances[i + 1]; // index+1 due to root padding
                        float origLen = orig.magnitude;
                        originalTotal += origLen;

                        if (m_DrawOriginalDirections && origLen > 1e-5f)
                        {
                            Vector3 origDir = (orig / origLen);
                            UnityEditor.Handles.color = new Color(1f, 0.5f, 0f); // orange
                            UnityEditor.Handles.ArrowHandleCap(0, a, Quaternion.LookRotation(origDir), m_ArrowSize * 0.9f, EventType.Repaint);
                        }

                        if (m_DrawLengthError && (origLen > 1e-5f))
                        {
                            float err = currLen - origLen;
                            var mid = (a + b) * 0.5f;
                            UnityEditor.Handles.color = (Mathf.Abs(err) < 0.0025f) ? Color.gray : (err > 0f ? Color.magenta : Color.cyan);
                            UnityEditor.Handles.Label(mid + Vector3.up * (m_JointSphereSize * 2f),
                                $"ΔL: {err:+0.000;-0.000;0.000} m");
                        }
                    }

                    if (m_DrawTotalLength)
                    {
                        var at = debugPositions[0] + Vector3.up * (m_JointSphereSize * 4f);
                        UnityEditor.Handles.color = Color.white;
                        UnityEditor.Handles.Label(at,
                            $"Total L: curr {currentTotal:0.000} m / orig {originalTotal:0.000} m\n" +
                            $"MaintainLen: {m_Data.maintainSpineLength}\nChain Weight: {m_Data.chainWeight:0.00}");
                    }

                    UnityEditor.Handles.color = Color.white;
                }
                else
                {
                    // Optional: warn once in editor if calibration doesn't match runtime chain.
                    // UnityEditor.Handles.Label(debugPositions[0] + Vector3.up * (m_JointSphereSize * 6f), "CalibrateSpine() to see orig vs current.");
                }

                // Simple "center of mass" (average of points)
                if (m_DrawCenterOfMass && debugCount > 0)
                {
                    Vector3 com = Vector3.zero;
                    for (int i = 0; i < debugCount; i++) com += debugPositions[i];
                    com /= debugCount;

                    UnityEditor.Handles.color = Color.yellow;
                    float r = m_JointSphereSize * 1.5f;
                    UnityEditor.Handles.DrawWireDisc(com, Vector3.up, r);
                    UnityEditor.Handles.DrawWireDisc(com, Vector3.right, r);
                    UnityEditor.Handles.DrawWireDisc(com, Vector3.forward, r);
                    UnityEditor.Handles.Label(com + Vector3.up * (r * 0.5f), "COM");
                    UnityEditor.Handles.color = Color.white;
                }
            }
#endif
        }
    }

    [Unity.Burst.BurstCompile]
    public struct BasisSpineIKConstraintJob : IWeightedAnimationJob
    {
        public ReadWriteTransformHandle hips;
        // NativeArray for Burst
        public NativeArray<ReadWriteTransformHandle> spineJoints;
        public ReadWriteTransformHandle head;

        public Vector3Property headTargetPosition;
        public Vector3Property headTargetRotation;
        public Vector3Property hipsTargetPosition;
        public Vector3Property hipsTargetRotation;

        public FloatProperty chainWeight;
        public FloatProperty jobWeight { get; set; }

        // Distance-based optimization variables
        public NativeArray<Vector3> originalDistances;          // length == debugCount
        public NativeArray<Vector3> currentDistances;           // length == debugCount
        public NativeArray<Quaternion> originalRelativeRotations; // length == debugCount - 1
        public bool maintainSpineLength;

        // Debug export buffers (shared with the component)
        public NativeArray<Vector3> debugPositions;
        public NativeArray<float> debugLengths;

        public void ProcessRootMotion(AnimationStream stream) { }

        public void ProcessAnimation(AnimationStream stream)
        {
            if (!spineJoints.IsCreated || spineJoints.Length == 0)
                return;

            float w = jobWeight.Get(stream);
            if (w > 0f)
            {
                Vector3 headTargetPos = headTargetPosition.Get(stream);
                Quaternion headTargetRot = Quaternion.Euler(headTargetRotation.Get(stream));

                Vector3 hipsTargetPos = hipsTargetPosition.Get(stream);
                Quaternion hipsTargetRot = Quaternion.Euler(hipsTargetRotation.Get(stream));

                float weight = chainWeight.Get(stream);

                SolveSpineIKWithDistanceOptimization(stream, headTargetPos, headTargetRot, hipsTargetPos, hipsTargetRot, weight * w);

                WriteDebugBuffer(stream);
            }
            else
            {
                BasisAnimationRuntimeUtils.PassThrough(stream, hips);
                for (int i = 0; i < spineJoints.Length; i++)
                    BasisAnimationRuntimeUtils.PassThrough(stream, spineJoints[i]);
                BasisAnimationRuntimeUtils.PassThrough(stream, head);

                WriteDebugBuffer(stream);
            }
        }

        private void SolveSpineIKWithDistanceOptimization(AnimationStream stream, Vector3 headTargetPos, Quaternion headTargetRot, Vector3 hipsTargetPos, Quaternion hipsTargetRot, float weight)
        {
            SetHips(stream, hipsTargetPos, hipsTargetRot);
            SetHead(stream, headTargetPos, headTargetRot);
            SetSpine(stream);
        }

        private void SetHead(AnimationStream stream, Vector3 headTargetPos, Quaternion headTargetRot)
        {
            head.SetPosition(stream, headTargetPos);
            head.SetRotation(stream, headTargetRot);
        }

        private void SetHips(AnimationStream stream, Vector3 hipsTargetPos, Quaternion hipsTargetRot)
        {
            hips.SetPosition(stream, hipsTargetPos);
            // hips.SetRotation(stream, hipsTargetRot);
        }

        private void SetSpine(AnimationStream stream)
        {
            // TODO: implement spine segment solving
        }

        private void WriteDebugBuffer(AnimationStream stream)
        {
            if (!debugPositions.IsCreated || !debugLengths.IsCreated)
                return;

            int count = spineJoints.Length + 2; // hips + joints + head
            if (debugPositions.Length < count || debugLengths.Length < count - 1)
                return;

            debugPositions[0] = hips.GetPosition(stream);
            for (int i = 0; i < spineJoints.Length; i++)
                debugPositions[i + 1] = spineJoints[i].GetPosition(stream);
            debugPositions[count - 1] = head.GetPosition(stream);

            for (int i = 0; i < count - 1; i++)
                debugLengths[i] = Vector3.Distance(debugPositions[i], debugPositions[i + 1]);
        }
    }

    public interface BasisISpineIKConstraintData
    {
        Transform hips { get; }
        Transform[] spineJoints { get; }
        Transform head { get; }

        Vector3 headTargetPosition { get; }
        Vector3 headTargetRotation { get; }
        Vector3 hipsTargetPosition { get; }
        Vector3 hipsTargetRotation { get; }
        float chainWeight { get; }

        Vector3[] originalDistances { get; }
        Quaternion[] originalRelativeRotations { get; }

        string chainWeightFloatProperty { get; }
        string headTargetPositionVector3Property { get; }
        string headTargetRotationVector3Property { get; }
        string hipsTargetPositionVector3Property { get; }
        string hipsTargetRotationVector3Property { get; }
    }

    public class BasisSpineIKConstraintJobBinder<T> : AnimationJobBinder<BasisSpineIKConstraintJob, T>
        where T : struct, IAnimationJobData, BasisISpineIKConstraintData
    {
        public override BasisSpineIKConstraintJob Create(Animator animator, ref T data, Component component)
        {
            // Bind handles
            var spineHandles = new NativeArray<ReadWriteTransformHandle>(data.spineJoints.Length, Allocator.Persistent);
            for (int i = 0; i < data.spineJoints.Length; i++)
                spineHandles[i] = ReadWriteTransformHandle.Bind(animator, data.spineJoints[i]);

            // Distance optimization buffers
            var originalDistArray = new NativeArray<Vector3>(data.originalDistances.Length, Allocator.Persistent);
            var currentDistArray = new NativeArray<Vector3>(data.originalDistances.Length, Allocator.Persistent);
            var originalRotArray = new NativeArray<Quaternion>(data.originalRelativeRotations?.Length ?? 0, Allocator.Persistent);

            for (int i = 0; i < data.originalDistances.Length; i++)
                originalDistArray[i] = data.originalDistances[i];

            if (data.originalRelativeRotations != null)
                for (int i = 0; i < data.originalRelativeRotations.Length; i++)
                    originalRotArray[i] = data.originalRelativeRotations[i];

            // Debug export buffers
            int debugCount = data.spineJoints.Length + 2; // hips + joints + head
            var debugPositions = new NativeArray<Vector3>(debugCount, Allocator.Persistent);
            var debugLengths = new NativeArray<float>(debugCount - 1, Allocator.Persistent);

            // Share with component
            if (component is BasisSpineIKConstraint constraintComponent)
            {
                constraintComponent.debugPositions = debugPositions;
                constraintComponent.debugLengths = debugLengths;
                constraintComponent.debugCount = debugCount;
            }

            BasisSpineIKConstraintJob job = new BasisSpineIKConstraintJob
            {
                hips = ReadWriteTransformHandle.Bind(animator, data.hips),
                spineJoints = spineHandles,
                head = ReadWriteTransformHandle.Bind(animator, data.head),

                headTargetPosition = Vector3Property.Bind(animator, component, data.headTargetPositionVector3Property),
                headTargetRotation = Vector3Property.Bind(animator, component, data.headTargetRotationVector3Property),
                hipsTargetPosition = Vector3Property.Bind(animator, component, data.hipsTargetPositionVector3Property),
                hipsTargetRotation = Vector3Property.Bind(animator, component, data.hipsTargetRotationVector3Property),
                chainWeight = FloatProperty.Bind(animator, component, data.chainWeightFloatProperty),

                originalDistances = originalDistArray,
                currentDistances = currentDistArray,
                originalRelativeRotations = originalRotArray,
                maintainSpineLength = true,

                debugPositions = debugPositions,
                debugLengths = debugLengths
            };

            return job;
        }

        public override void Destroy(BasisSpineIKConstraintJob job)
        {
            if (job.spineJoints.IsCreated)
                job.spineJoints.Dispose();

            if (job.originalDistances.IsCreated)
                job.originalDistances.Dispose();
            if (job.currentDistances.IsCreated)
                job.currentDistances.Dispose();
            if (job.originalRelativeRotations.IsCreated)
                job.originalRelativeRotations.Dispose();

            if (job.debugPositions.IsCreated)
                job.debugPositions.Dispose();
            if (job.debugLengths.IsCreated)
                job.debugLengths.Dispose();
        }
    }
}
