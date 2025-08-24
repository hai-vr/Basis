using Unity.Collections;

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
		Vector3 BasisISpineIKConstraintData.headTargetPosition { get => headTargetPosition; }
		Vector3 BasisISpineIKConstraintData.headTargetRotation { get => headTargetRotation; }
		Vector3 BasisISpineIKConstraintData.hipsTargetPosition { get => hipsTargetPosition; }
		Vector3 BasisISpineIKConstraintData.hipsTargetRotation { get => hipsTargetRotation; }
		float BasisISpineIKConstraintData.chainWeight { get => ChainWeight; }
		Vector3[] BasisISpineIKConstraintData.originalDistances { get => m_OriginalDistances; }
		Quaternion[] BasisISpineIKConstraintData.originalRelativeRotations { get => m_OriginalRelativeRotations; }

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

		[SerializeField]
		public Vector3[] m_OriginalDistances;

		[SerializeField]
		public Quaternion[] m_OriginalRelativeRotations;

		public Vector3[] originalDistances
		{
			get { return m_OriginalDistances; }
		}

		public Quaternion[] originalRelativeRotations
		{
			get { return m_OriginalRelativeRotations; }
		}

		bool IAnimationJobData.IsValid() =>
			(m_Hips != null && m_Head != null && m_SpineJoints != null && m_SpineJoints.Length > 0 &&
			 IsValidSpineChain());

		private bool IsValidSpineChain()
		{
			if (m_SpineJoints == null || m_SpineJoints.Length == 0) return false;

			// Check distances are reasonable
			Transform current = m_Hips;
			for (int i = 0; i < m_SpineJoints.Length; i++)
			{
				if (m_SpineJoints[i] == null) return false;

				float distance = Vector3.Distance(current.position, m_SpineJoints[i].position);
				if (distance > 2.0f || distance < 0.01f) // Reasonable spine segment lengths
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

			// Initialize original distances array
			if (m_SpineJoints != null && m_SpineJoints.Length > 0)
			{
				CalibrateOriginalDistances();
			}
		}

		private void CalibrateOriginalDistances()
		{
			if (m_SpineJoints == null || m_SpineJoints.Length == 0) return;

			int jointCount = m_SpineJoints.Length + 1; // Include root
			m_OriginalDistances = new Vector3[jointCount];
			m_OriginalRelativeRotations = new Quaternion[m_SpineJoints.Length];

			// Store original distances between consecutive joints
			m_OriginalDistances[0] = Vector3.zero; // Root has no previous joint

			Transform prev = m_Hips;
			for (int i = 0; i < m_SpineJoints.Length; i++)
			{
				if (m_SpineJoints[i] != null)
				{
					Vector3 offset = m_SpineJoints[i].position - prev.position;
					m_OriginalDistances[i + 1] = offset;
					
					// Store original relative rotation from parent's forward direction
					// This captures the natural spine curvature
					Vector3 parentForward = prev.rotation * Vector3.forward;
					Vector3 jointDirection = (m_SpineJoints[i].position - prev.position).normalized;
					
					if (jointDirection.sqrMagnitude > 0.001f)
					{
						// Calculate the rotation difference that represents the natural curve
						m_OriginalRelativeRotations[i] = Quaternion.FromToRotation(parentForward, jointDirection);
					}
					else
					{
						m_OriginalRelativeRotations[i] = Quaternion.identity;
					}
					
					prev = m_SpineJoints[i];
				}
			}
		}
	}

	[DisallowMultipleComponent, AddComponentMenu("Animation Rigging/Spine IK Constraint")]
	[HelpURL("https://docs.unity3d.com/Packages/com.unity.animation.rigging@1.3/manual/constraints/SpineIKConstraint.html")]
	public class BasisSpineIKConstraint : RigConstraint<BasisSpineIKConstraintJob, BasisSpineIKConstraintData, BasisSpineIKConstraintJobBinder<BasisSpineIKConstraintData>>
	{
		/// <inheritdoc />
		protected override void OnValidate()
		{
			base.OnValidate();
			m_Data.chainWeight = Mathf.Clamp01(m_Data.chainWeight);
		}

		/// <summary>
		/// Calibrate the original spine distances for natural pose restoration
		/// </summary>
		public void CalibrateSpine()
		{
			if (m_Data.spineJoints != null && m_Data.spineJoints.Length > 0)
			{
				// Force recalibration of distances
				((IAnimationJobData)m_Data).SetDefaultValues();
			}
		}
		
		/// <summary>
		/// Debug method to inspect current spine setup and distances
		/// </summary>
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
					{
						Debug.Log($"Spine[{i}]: {m_Data.spineJoints[i].name} at {m_Data.spineJoints[i].position}");
					}
					else
					{
						Debug.Log($"Spine[{i}]: NULL");
					}
				}
			}
			
			Debug.Log($"Original distances count: {(m_Data.originalDistances?.Length ?? 0)}");
			if (m_Data.originalDistances != null)
			{
				for (int i = 0; i < m_Data.originalDistances.Length; i++)
				{
					Debug.Log($"OriginalDistance[{i}]: {m_Data.originalDistances[i]} (magnitude: {m_Data.originalDistances[i].magnitude:F4})");
				}
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
			
			// Calculate actual current distances for comparison
			if (m_Data.hips && m_Data.head && m_Data.spineJoints != null)
			{
				Debug.Log("=== CURRENT ACTUAL DISTANCES ===");
				Transform prev = m_Data.hips;
				float totalActualLength = 0f;
				
				for (int i = 0; i < m_Data.spineJoints.Length; i++)
				{
					if (m_Data.spineJoints[i] != null)
					{
						float distance = Vector3.Distance(prev.position, m_Data.spineJoints[i].position);
						totalActualLength += distance;
						Debug.Log($"Actual distance {prev.name} -> {m_Data.spineJoints[i].name}: {distance:F4}m");
						prev = m_Data.spineJoints[i];
					}
				}
				
				float headDistance = Vector3.Distance(prev.position, m_Data.head.position);
				totalActualLength += headDistance;
				Debug.Log($"Actual distance {prev.name} -> {m_Data.head.name}: {headDistance:F4}m");
				Debug.Log($"Total actual spine length: {totalActualLength:F4}m");
				Debug.Log($"Direct hips-to-head distance: {Vector3.Distance(m_Data.hips.position, m_Data.head.position):F4}m");
				
				// Calculate natural curve preservation ratio
				if (totalActualLength > 0.001f)
				{
					float directDistance = Vector3.Distance(m_Data.hips.position, m_Data.head.position);
					float curveRatio = directDistance / totalActualLength;
					Debug.Log($"Spine compression ratio: {curveRatio:F3} (1.0 = fully extended, <1.0 = curved/compressed)");
				}
			}
		}
	}

	[Unity.Burst.BurstCompile]
	public struct BasisSpineIKConstraintJob : IWeightedAnimationJob
	{
		public ReadWriteTransformHandle hips;
		public ReadWriteTransformHandle[] spineJoints;
		public ReadWriteTransformHandle head;

		public Vector3Property headTargetPosition;
		public Vector3Property headTargetRotation;
		public Vector3Property hipsTargetPosition;
		public Vector3Property hipsTargetRotation;

		public FloatProperty chainWeight;
		public FloatProperty jobWeight { get; set; }

		// Distance-based optimization variables
		public NativeArray<Vector3> originalDistances;
		public NativeArray<Vector3> currentDistances;
		public NativeArray<Quaternion> originalRelativeRotations;
		public bool maintainSpineLength;

		public void ProcessRootMotion(AnimationStream stream) { }

		public void ProcessAnimation(AnimationStream stream)
		{
			float w = jobWeight.Get(stream);
			if (w > 0f && spineJoints.Length > 0)
			{
				Vector3 headTargetPos = headTargetPosition.Get(stream);
				Quaternion headTargetRot = Quaternion.Euler(headTargetRotation.Get(stream));

				Vector3 hipsTargetPos = hipsTargetPosition.Get(stream);
				Quaternion hipsTargetRot = Quaternion.Euler(hipsTargetPosition.Get(stream));

				float weight = chainWeight.Get(stream);

				// Get curvature array from transform handles
				Vector3[] curvature = new Vector3[spineJoints.Length];
				for (int i = 0; i < spineJoints.Length; i++)
				{
					curvature[i] = spineJoints[i].GetLocalPosition(stream);
				}

				// Apply PDO-IK style distance-based optimization
				SolveSpineIKWithDistanceOptimization(stream, headTargetPos, headTargetRot, hipsTargetPos, hipsTargetRot, curvature, w * weight);
			}
			else
			{
				// Pass through when weight is zero
				BasisAnimationRuntimeUtils.PassThrough(stream, hips);
				for (int i = 0; i < spineJoints.Length; i++)
				{
					BasisAnimationRuntimeUtils.PassThrough(stream, spineJoints[i]);
				}
				BasisAnimationRuntimeUtils.PassThrough(stream, head);
			}
		}

		private void SolveSpineIKWithDistanceOptimization(AnimationStream stream, Vector3 headTargetPos, Quaternion headTargetRot, Vector3 hipsTargetPos, Quaternion hipsTargetRot, Vector3[] curvature, float weight)
		{
			if (spineJoints.Length == 0) return;

			SetHips(stream, hipsTargetPos, hipsTargetRot);
			SetHead(stream, headTargetPos, headTargetRot);
			SetSpine(stream);
		}

		private void SetHead(AnimationStream stream, Vector3 headTargetPos, Quaternion headTargetRot)
		{
			// SET HEAD TO TARGET IMMEDIATELY - 1:1 positioning  
			head.SetPosition(stream, headTargetPos);
			head.SetRotation(stream, headTargetRot);
		}

		private void SetHips(AnimationStream stream, Vector3 hipsTargetPos, Quaternion hipsTargetRot)
		{
			// SET HIPS TO TARGET IMMEDIATELY - 1:1 positioning
			hips.SetPosition(stream, hipsTargetPos);
			//hips.SetRotation(stream, hipsTargetRot);
		}

		private void SetSpine(AnimationStream stream)
		{
			
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
			var spineHandles = new ReadWriteTransformHandle[data.spineJoints.Length];
			for (int i = 0; i < data.spineJoints.Length; i++)
			{
				spineHandles[i] = ReadWriteTransformHandle.Bind(animator, data.spineJoints[i]);
			}

			// Create native arrays for distance optimization
			var originalDistArray = new NativeArray<Vector3>(data.originalDistances.Length, Allocator.Persistent);
			var currentDistArray = new NativeArray<Vector3>(data.originalDistances.Length, Allocator.Persistent);
			var originalRotArray = new NativeArray<Quaternion>(data.originalRelativeRotations?.Length ?? 0, Allocator.Persistent);

			for (int i = 0; i < data.originalDistances.Length; i++)
			{
				originalDistArray[i] = data.originalDistances[i];
			}
			if (data.originalRelativeRotations != null)
			{
				for (int i = 0; i < data.originalRelativeRotations.Length; i++)
				{
					originalRotArray[i] = data.originalRelativeRotations[i];
				}
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
				maintainSpineLength = true
			};

			return job;
		}

		public override void Destroy(BasisSpineIKConstraintJob job)
		{
			if (job.originalDistances.IsCreated)
				job.originalDistances.Dispose();
			if (job.currentDistances.IsCreated)
				job.currentDistances.Dispose();
			if (job.originalRelativeRotations.IsCreated)
				job.originalRelativeRotations.Dispose();
		}
	}
}
