using System;
using System.Collections.Generic;
using Basis.Scripts.BasisSdk.Constraints;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;

namespace Basis.Scripts.Constraints
{
    /// <summary>
    /// Batched solver behind the <see cref="BasisConstraintBase"/> components. Every enabled
    /// constraint in the process — avatar or scene — is flattened into one set of native containers
    /// and solved by three jobs per frame (sample, solve, write), instead of each component paying
    /// for its own <c>Update</c> and its own scattered transform reads.
    ///
    /// Components announce themselves through the SDK-side static events rather than calling in
    /// directly, because the SDK assembly cannot reference this one. The subscription is installed
    /// once at <see cref="RuntimeInitializeLoadType.SubsystemRegistration"/>, before any scene
    /// loads, so no component can enable ahead of the listener.
    ///
    /// Structural changes (a component joining, leaving, or reshaping its source list) set
    /// <c>sDirty</c> and rebuild the whole table on the next <see cref="Schedule"/>. That is a
    /// deliberate simplification over the incremental append <c>BasisAuthoredMotionSystem</c> does:
    /// constraint counts are small and structural churn is rare, whereas the shared source buffer
    /// makes incremental compaction fiddly to get right. Scalar edits — weight, offsets, masks,
    /// rest pose, source weights — are re-read every frame and never trigger a rebuild, so
    /// animating a constraint's weight stays free.
    ///
    /// Reparenting a constrained transform at runtime does not invalidate anything on its own;
    /// call <see cref="BasisConstraintBase.SetDirty"/> after doing so, since the cached parent row
    /// is resolved at rebuild.
    /// </summary>
    public static class BasisConstraintSystem
    {
        private sealed class Registration
        {
            public BasisConstraintBase Component;
            public int SlotIndex;
            public int SourceStart;
            public int SourceCount;

            /// <summary>
            /// Flattened source i came from this index of the component's list. Sources with a null
            /// transform are dropped during flatten, so the two orderings are not interchangeable —
            /// the parent constraint's per-source offset arrays are indexed through this.
            /// </summary>
            public int[] SourceMap = Array.Empty<int>();

            /// <summary>
            /// The world-up reference this slot's WorldUpIndex was resolved against. Assigning or
            /// swapping worldUpObject at runtime is a structural change — the index is only
            /// resolvable while the intern table is being built — so it is diffed every frame.
            /// </summary>
            public Transform WorldUpObject;

            /// <summary>
            /// The damped kind's lag memory, parked here across rebuilds. Slot indices are reassigned
            /// every rebuild, so the native row cannot survive one; the registration can.
            /// </summary>
            public BasisConstraintDampState DampState;
        }

        private static readonly List<Registration> sRegistrations = new List<Registration>();
        private static readonly Dictionary<BasisConstraintBase, Registration> sLookup =
            new Dictionary<BasisConstraintBase, Registration>();

        /// <summary>Interning table for every transform the solve touches, mapped to its row index.</summary>
        private static readonly Dictionary<Transform, int> sTransformLookup = new Dictionary<Transform, int>();
        private static readonly List<Transform> sTrackedScratch = new List<Transform>();
        private static readonly List<Transform> sTargetScratch = new List<Transform>();
        private static readonly List<Transform> sChainScratch = new List<Transform>();
        /// <summary>Target transform to its row in the results/write arrays; deduplicates stacked constraints.</summary>
        private static readonly Dictionary<Transform, int> sTargetRowLookup = new Dictionary<Transform, int>();
        private static readonly List<int> sOrderScratch = new List<int>();
        private static readonly List<int> sDepthScratch = new List<int>();
        private static readonly List<int> sSourceMapScratch = new List<int>();
        /// <summary>Transform row → the slots that drive it, for the dependency sort.</summary>
        private static readonly Dictionary<int, List<int>> sProducers = new Dictionary<int, List<int>>();
        private static readonly List<List<int>> sConsumers = new List<List<int>>();
        private static readonly List<int> sInDegree = new List<int>();
        private static readonly List<bool> sEmitted = new List<bool>();
        /// <summary>Binary min-heap of slots whose dependencies are all met, ordered by depth.</summary>
        private static readonly List<int> sReadyHeap = new List<int>();
        private static readonly List<BasisConstraintSourceEntry> sSourceScratch =
            new List<BasisConstraintSourceEntry>();

        private static NativeList<BasisConstraintSlot> sSlots;
        private static NativeList<BasisConstraintSource> sSources;
        private static NativeList<BasisConstraintWorld> sWorld;
        private static NativeList<BasisConstraintTransform> sLocal;
        private static NativeList<BasisConstraintResult> sResults;
        private static NativeList<int> sOrder;
        private static NativeList<int> sTargetRow;
        private static NativeList<BasisConstraintDampState> sDampState;
        /// <summary>(sampled transform row, results row) per bone, for the chain-driving kinds.</summary>
        private static NativeList<int2> sChain;
        /// <summary>Each chain member's pose at capture, parallel to sChain. Referential holds its
        /// members in the arrangement these describe, whichever one is leading.</summary>
        private static NativeList<BasisConstraintWorld> sChainBind;
        private static NativeList<float3> sChainPositions;
        private static NativeList<float> sChainLengths;
        private static TransformAccessArray sTracked;
        private static TransformAccessArray sTargets;

        private static JobHandle sPending;
        private static bool sInitialized;
        private static bool sDirty;
        private static bool sSubscribed;

        /// <summary>Number of constraints currently solved each frame.</summary>
        public static int SlotCount => sInitialized ? sSlots.Length : 0;

        /// <summary>Distinct transforms sampled each frame (targets, sources, up objects, parents).</summary>
        public static int TrackedTransformCount => sInitialized ? sTracked.length : 0;

        /// <summary>
        /// Installs the cross-assembly subscription and clears anything a disabled domain reload
        /// left behind. Mandatory: with reload disabled, statics survive exiting play mode, and a
        /// leftover <see cref="NativeList{T}"/> would both leak and be handed to the next session.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Dispose();

            sRegistrations.Clear();
            sLookup.Clear();
            sTransformLookup.Clear();
            sDirty = false;
            sPending = default;

            if (!sSubscribed)
            {
                BasisConstraintBase.ActiveStateChanged += OnActiveStateChanged;
                BasisConstraintBase.StructureChanged += OnStructureChanged;
                sSubscribed = true;
            }
        }

        public static void Initialize(int initialCapacity = 0)
        {
            if (sInitialized)
            {
                return;
            }
            sSlots = new NativeList<BasisConstraintSlot>(initialCapacity, Allocator.Persistent);
            sSources = new NativeList<BasisConstraintSource>(initialCapacity, Allocator.Persistent);
            sWorld = new NativeList<BasisConstraintWorld>(initialCapacity, Allocator.Persistent);
            sLocal = new NativeList<BasisConstraintTransform>(initialCapacity, Allocator.Persistent);
            sResults = new NativeList<BasisConstraintResult>(initialCapacity, Allocator.Persistent);
            sOrder = new NativeList<int>(initialCapacity, Allocator.Persistent);
            sTargetRow = new NativeList<int>(initialCapacity, Allocator.Persistent);
            sDampState = new NativeList<BasisConstraintDampState>(initialCapacity, Allocator.Persistent);
            sChain = new NativeList<int2>(initialCapacity, Allocator.Persistent);
            sChainBind = new NativeList<BasisConstraintWorld>(initialCapacity, Allocator.Persistent);
            sChainPositions = new NativeList<float3>(initialCapacity, Allocator.Persistent);
            sChainLengths = new NativeList<float>(initialCapacity, Allocator.Persistent);
            sTracked = new TransformAccessArray(math.max(1, initialCapacity));
            sTargets = new TransformAccessArray(math.max(1, initialCapacity));
            sInitialized = true;
        }

        public static void Dispose()
        {
            if (!sInitialized)
            {
                return;
            }
            sPending.Complete();
            sPending = default;

            if (sSlots.IsCreated) sSlots.Dispose();
            if (sSources.IsCreated) sSources.Dispose();
            if (sWorld.IsCreated) sWorld.Dispose();
            if (sLocal.IsCreated) sLocal.Dispose();
            if (sResults.IsCreated) sResults.Dispose();
            if (sOrder.IsCreated) sOrder.Dispose();
            if (sTargetRow.IsCreated) sTargetRow.Dispose();
            if (sDampState.IsCreated) sDampState.Dispose();
            if (sChain.IsCreated) sChain.Dispose();
            if (sChainBind.IsCreated) sChainBind.Dispose();
            if (sChainPositions.IsCreated) sChainPositions.Dispose();
            if (sChainLengths.IsCreated) sChainLengths.Dispose();
            if (sTracked.isCreated) sTracked.Dispose();
            if (sTargets.isCreated) sTargets.Dispose();

            // Drop the managed side too. Otherwise a component enabling after teardown re-enters
            // Register, re-Initializes fresh persistent containers that nothing will ever schedule
            // or dispose, and holds references to destroyed objects until the next domain reset.
            sRegistrations.Clear();
            sLookup.Clear();
            sTransformLookup.Clear();
            sTargetRowLookup.Clear();
            sTrackedScratch.Clear();
            sTargetScratch.Clear();
            sOrderScratch.Clear();
            sDepthScratch.Clear();
            sSourceScratch.Clear();
            sProducers.Clear();
            sConsumers.Clear();
            sDirty = false;

            sInitialized = false;
        }

        private static void OnActiveStateChanged(BasisConstraintBase component, bool active)
        {
            if (active)
            {
                Register(component);
            }
            else
            {
                Unregister(component);
            }
        }

        private static void OnStructureChanged(BasisConstraintBase component)
        {
            if (component != null && sLookup.ContainsKey(component))
            {
                sDirty = true;
            }
        }

        public static void Register(BasisConstraintBase component)
        {
            if (component == null)
            {
                return;
            }
            if (!sInitialized)
            {
                Initialize();
            }
            if (sLookup.ContainsKey(component))
            {
                return;
            }

            Registration registration = new Registration { Component = component };
            sRegistrations.Add(registration);
            sLookup[component] = registration;
            sDirty = true;
        }

        public static void Unregister(BasisConstraintBase component)
        {
            if (component == null || !sLookup.TryGetValue(component, out Registration registration))
            {
                return;
            }
            sLookup.Remove(component);
            sRegistrations.Remove(registration);
            sDirty = true;
        }

        /// <summary>
        /// Samples, solves and writes every constraint. Call once per frame after the pose the
        /// constraints should read is final — after IK and authored motion, before jiggle samples
        /// the bones — and pair it with <see cref="Complete"/> in the same frame: the returned
        /// handle owns the transform arrays until then, so touching a constrained transform on the
        /// main thread in between is a race.
        /// </summary>
        public static JobHandle Schedule()
        {
            if (!sInitialized)
            {
                return default;
            }
            // Never mutate the containers under a live job: a frame that early-returns below would
            // otherwise leave sPending in flight while the next Rebuild clears everything.
            CompletePending();

            // A pending rebuild is honoured even with no registrations left, so unregistering the
            // last constraint actually clears the tables instead of stranding stale slots.
            // A component destroyed without an OnDisable (scene teardown, DestroyImmediate) has to
            // be caught here too: its transform would otherwise still be in the write array.
            if (sDirty || HasDestroyedRegistration() || HasDesyncedTransforms())
            {
                Rebuild();
            }
            if (sSlots.Length == 0)
            {
                return default;
            }

            RefreshDynamicState();

            JobHandle read = new BasisConstraintReadJob
            {
                World = sWorld.AsArray(),
                Local = sLocal.AsArray(),
            }.Schedule(sTracked);

            JobHandle solve = new BasisConstraintSolveJob
            {
                Slots = sSlots.AsArray(),
                Sources = sSources.AsArray(),
                Local = sLocal.AsArray(),
                Order = sOrder.AsArray(),
                TargetRow = sTargetRow.AsArray(),
                World = sWorld.AsArray(),
                Results = sResults.AsArray(),
                DampState = sDampState.AsArray(),
                Chain = sChain.AsArray(),
                ChainBind = sChainBind.AsArray(),
                ChainPositions = sChainPositions.AsArray(),
                ChainLengths = sChainLengths.AsArray(),
                DeltaTime = Time.unscaledDeltaTime,
            }.Schedule(read);

            sPending = new BasisConstraintWriteJob
            {
                Results = sResults.AsArray(),
            }.Schedule(sTargets, solve);

            return sPending;
        }

        public static void Complete(JobHandle handle)
        {
            handle.Complete();
            // Only retire the tracked handle if this is it; a caller completing some unrelated
            // handle must not convince the system that its own solve has landed.
            if (handle.Equals(sPending))
            {
                sPending = default;
            }
        }

        /// <summary>
        /// Cheap per-frame sweep for components destroyed behind the system's back. The Unity
        /// fake-null check is the whole cost, over a list that is tens of entries in practice.
        /// </summary>
        private static bool HasDestroyedRegistration()
        {
            for (int Index = 0; Index < sRegistrations.Count; Index++)
            {
                if (sRegistrations[Index].Component == null)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// A TransformAccessArray silently drops destroyed transforms and compacts, which desyncs it
        /// from the parallel native arrays — the read job would then sample shifted rows and the
        /// write job would push solved poses onto the wrong objects, with no exception to show for
        /// it. Length disagreement is the tell; rebuilding resyncs.
        /// </summary>
        private static bool HasDesyncedTransforms()
        {
            return sTracked.length != sWorld.Length || sTargets.length != sResults.Length;
        }

        /// <summary>Completes any in-flight solve. Safe to call when nothing is scheduled.</summary>
        public static void CompletePending()
        {
            sPending.Complete();
            sPending = default;
        }

        /// <summary>
        /// Rebuilds the flattened tables from the live component set. Prunes destroyed components,
        /// interns every transform once, and topologically orders the slots so whatever drives a
        /// constraint resolves before it.
        /// </summary>
        private static void Rebuild()
        {
            CompletePending();

            // The solve is finished, so the native lag memory is final. Park it on the registrations
            // before slot indices are reassigned, or every damped transform in the session would
            // snap back to its source whenever anything else registers.
            for (int Index = 0; Index < sRegistrations.Count; Index++)
            {
                Registration registration = sRegistrations[Index];
                if (registration.SlotIndex >= 0 && registration.SlotIndex < sDampState.Length)
                {
                    registration.DampState = sDampState[registration.SlotIndex];
                }
            }

            sSlots.Clear();
            sDampState.Clear();
            sChain.Clear();
            sChainBind.Clear();
            sSources.Clear();
            sWorld.Clear();
            sLocal.Clear();
            sOrder.Clear();
            sTargetRow.Clear();
            sTransformLookup.Clear();
            sTrackedScratch.Clear();
            sTargetScratch.Clear();
            sTargetRowLookup.Clear();
            sOrderScratch.Clear();
            sDepthScratch.Clear();

            for (int Index = sRegistrations.Count - 1; Index >= 0; Index--)
            {
                // Destroyed without an OnDisable (scene teardown) leaves a fake-null behind.
                if (sRegistrations[Index].Component == null)
                {
                    sRegistrations.RemoveAt(Index);
                }
            }

            for (int Index = 0; Index < sRegistrations.Count; Index++)
            {
                Registration registration = sRegistrations[Index];
                BasisConstraintBase component = registration.Component;
                Transform target = component.transform;

                int targetIndex = InternTransform(target);
                int parentIndex = target.parent != null ? InternTransform(target.parent) : -1;

                // ParentIndex rides in the local-pose row; the sample job preserves it.
                BasisConstraintTransform local = sLocal[targetIndex];
                local.ParentIndex = parentIndex;
                sLocal[targetIndex] = local;

                registration.SlotIndex = sSlots.Length;
                registration.SourceStart = sSources.Length;

                component.GetSources(sSourceScratch);
                sSourceMapScratch.Clear();
                for (int SourceIndex = 0; SourceIndex < sSourceScratch.Count; SourceIndex++)
                {
                    BasisConstraintSourceEntry entry = sSourceScratch[SourceIndex];
                    if (entry.sourceTransform == null)
                    {
                        continue;
                    }
                    sSources.Add(new BasisConstraintSource
                    {
                        TransformIndex = InternTransform(entry.sourceTransform),
                        Weight = math.max(0f, entry.weight),
                        PositionOffset = float3.zero,
                        RotationOffset = quaternion.identity,
                    });
                    sSourceMapScratch.Add(SourceIndex);
                }
                registration.SourceCount = sSourceMapScratch.Count;
                registration.SourceMap = sSourceMapScratch.ToArray();

                BasisConstraintSlot slot = BasisConstraintDefaults.Identity(ToKind(component.constraintType));
                slot.TargetIndex = targetIndex;
                slot.SourceStart = registration.SourceStart;
                slot.SourceCount = registration.SourceCount;
                slot.Depth = ToDepth(target);
                registration.WorldUpObject = GetWorldUpObject(component);
                slot.WorldUpIndex = registration.WorldUpObject != null
                    ? InternTransform(registration.WorldUpObject)
                    : -1;
                BuildChain(component, ref slot);
                FillScalarState(component, ref slot);
                sSlots.Add(slot);
                // Kept index-parallel with the slots, restoring whatever this registration was
                // carrying so a damped transform resumes its lag instead of snapping.
                sDampState.Add(registration.DampState);
                FillSourceState(component, registration);

                // Several constraints may share one target (position + rotation is routine); they
                // all write through a single row so the parallel write job never doubles up.
                sTargetRow.Add(InternTargetRow(target));

                sOrderScratch.Add(registration.SlotIndex);
                sDepthScratch.Add(slot.Depth);
            }

            BuildSolveOrder();
            for (int Index = 0; Index < sOrderScratch.Count; Index++)
            {
                sOrder.Add(sOrderScratch[Index]);
            }

            int longestChain = 0;
            for (int Index = 0; Index < sSlots.Length; Index++)
            {
                longestChain = math.max(longestChain, sSlots[Index].ChainCount);
            }
            sChainPositions.Resize(math.max(1, longestChain), NativeArrayOptions.ClearMemory);
            sChainLengths.Resize(math.max(1, longestChain), NativeArrayOptions.ClearMemory);

            sResults.Resize(sTargetScratch.Count, NativeArrayOptions.ClearMemory);
            sTracked.SetTransforms(sTrackedScratch.ToArray());
            sTargets.SetTransforms(sTargetScratch.ToArray());

            sDirty = false;
        }

        /// <summary>
        /// Orders the solve so a constraint always runs after whatever drives it. Hierarchy depth
        /// alone is not enough: a root-level prop constrained to a deep, itself-constrained hand
        /// bone is shallower than its own dependency, and would read a stale pose forever. So this
        /// is a real topological sort over source→target edges, with depth only as the tie-break
        /// among slots that are equally ready (which keeps sibling order stable and natural).
        ///
        /// A cycle has no valid order; rather than drop those constraints, it is broken at the
        /// shallowest member, which costs that one slot a frame of lag.
        /// </summary>
        private static void BuildSolveOrder()
        {
            int count = sSlots.Length;

            // Which slots drive each transform row. Several may drive one row (stacked constraints).
            sProducers.Clear();
            for (int Index = 0; Index < count; Index++)
            {
                int targetIndex = sSlots[Index].TargetIndex;
                if (targetIndex < 0)
                {
                    continue;
                }
                if (!sProducers.TryGetValue(targetIndex, out List<int> producers))
                {
                    producers = new List<int>();
                    sProducers[targetIndex] = producers;
                }
                producers.Add(Index);
            }

            sConsumers.Clear();
            sInDegree.Clear();
            sEmitted.Clear();
            for (int Index = 0; Index < count; Index++)
            {
                sConsumers.Add(new List<int>());
                sInDegree.Add(0);
                sEmitted.Add(false);
            }

            for (int Index = 0; Index < count; Index++)
            {
                BasisConstraintSlot slot = sSlots[Index];
                for (int SourceIndex = 0; SourceIndex < slot.SourceCount; SourceIndex++)
                {
                    int transformIndex = sSources[slot.SourceStart + SourceIndex].TransformIndex;
                    if (transformIndex < 0 || !sProducers.TryGetValue(transformIndex, out List<int> producers))
                    {
                        continue;
                    }
                    for (int ProducerIndex = 0; ProducerIndex < producers.Count; ProducerIndex++)
                    {
                        int producer = producers[ProducerIndex];
                        if (producer == Index)
                        {
                            // A constraint sourcing its own target imposes no ordering on itself.
                            continue;
                        }
                        sConsumers[producer].Add(Index);
                        sInDegree[Index]++;
                    }
                }
            }

            // Kahn's, draining a depth-ordered ready heap. Scanning every slot for the shallowest
            // ready one instead would be O(slots²) per rebuild, and the rebuild is global: one avatar
            // joining re-sorts every constraint in the session. The heap keeps that at O(E log V),
            // which is what makes a join storm affordable.
            sOrderScratch.Clear();
            sReadyHeap.Clear();
            int brokenCycles = 0;
            for (int Index = 0; Index < count; Index++)
            {
                if (sInDegree[Index] <= 0)
                {
                    PushReady(Index);
                }
            }

            while (sOrderScratch.Count < count)
            {
                int chosen = -1;
                while (sReadyHeap.Count > 0)
                {
                    int candidate = PopReady();
                    // A slot forced out of a cycle below can be queued again once its remaining
                    // dependencies land; it is already emitted, so drop the duplicate.
                    if (!sEmitted[candidate])
                    {
                        chosen = candidate;
                        break;
                    }
                }

                if (chosen < 0)
                {
                    // Nothing is ready and slots remain: a cycle. Break it at the shallowest member,
                    // which costs that one slot a frame of lag.
                    chosen = PickShallowest(count, readyOnly: false);
                    if (chosen < 0)
                    {
                        break;
                    }
                    brokenCycles++;
                }

                sEmitted[chosen] = true;
                sOrderScratch.Add(chosen);

                List<int> consumers = sConsumers[chosen];
                for (int ConsumerIndex = 0; ConsumerIndex < consumers.Count; ConsumerIndex++)
                {
                    int consumer = consumers[ConsumerIndex];
                    sInDegree[consumer] = sInDegree[consumer] - 1;
                    if (sInDegree[consumer] <= 0 && !sEmitted[consumer])
                    {
                        PushReady(consumer);
                    }
                }
            }

            // Once per rebuild, never per cycle and never per frame: a persistent cycle would
            // otherwise re-log on every registration change, and LogError here fans out to disk and
            // the network through the exception notifier.
            if (brokenCycles > 0)
            {
                BasisDebug.LogError(
                    $"Constraint cycle detected ({brokenCycles} broken): a constraint is driven, " +
                    "directly or through other constraints, by its own target. Each break leaves " +
                    "one constraint a frame behind until the cycle is removed.");
            }
        }

        /// <summary>
        /// Heap order: shallowest first, ties broken by the lower slot index so sibling order stays
        /// stable and matches authoring order.
        /// </summary>
        private static bool ReadyPrecedes(int left, int right)
        {
            int leftDepth = sDepthScratch[left];
            int rightDepth = sDepthScratch[right];
            return leftDepth != rightDepth ? leftDepth < rightDepth : left < right;
        }

        private static void PushReady(int slotIndex)
        {
            sReadyHeap.Add(slotIndex);
            int child = sReadyHeap.Count - 1;
            while (child > 0)
            {
                int parent = (child - 1) / 2;
                if (!ReadyPrecedes(sReadyHeap[child], sReadyHeap[parent]))
                {
                    break;
                }
                (sReadyHeap[child], sReadyHeap[parent]) = (sReadyHeap[parent], sReadyHeap[child]);
                child = parent;
            }
        }

        private static int PopReady()
        {
            int top = sReadyHeap[0];
            int last = sReadyHeap.Count - 1;
            sReadyHeap[0] = sReadyHeap[last];
            sReadyHeap.RemoveAt(last);

            int parent = 0;
            while (true)
            {
                int left = (parent * 2) + 1;
                int right = left + 1;
                int best = parent;
                if (left < sReadyHeap.Count && ReadyPrecedes(sReadyHeap[left], sReadyHeap[best]))
                {
                    best = left;
                }
                if (right < sReadyHeap.Count && ReadyPrecedes(sReadyHeap[right], sReadyHeap[best]))
                {
                    best = right;
                }
                if (best == parent)
                {
                    break;
                }
                (sReadyHeap[parent], sReadyHeap[best]) = (sReadyHeap[best], sReadyHeap[parent]);
                parent = best;
            }
            return top;
        }

        /// <summary>
        /// Shallowest un-emitted slot, optionally restricted to those with no unmet dependency.
        /// In-degree is compared with &gt; 0 rather than != 0 so a cycle broken above, which can push
        /// a counter negative, still leaves its dependents selectable.
        /// </summary>
        private static int PickShallowest(int count, bool readyOnly)
        {
            int chosen = -1;
            for (int Index = 0; Index < count; Index++)
            {
                if (sEmitted[Index] || (readyOnly && sInDegree[Index] > 0))
                {
                    continue;
                }
                if (chosen < 0 || sDepthScratch[Index] < sDepthScratch[chosen])
                {
                    chosen = Index;
                }
            }
            return chosen;
        }

        /// <summary>
        /// Interns a transform, growing the per-transform world/local rows to match. Returns the
        /// row index every slot and source refers to.
        /// </summary>
        private static int InternTransform(Transform transform)
        {
            if (sTransformLookup.TryGetValue(transform, out int existing))
            {
                return existing;
            }
            int index = sTrackedScratch.Count;
            sTransformLookup[transform] = index;
            sTrackedScratch.Add(transform);

            sWorld.Resize(index + 1, NativeArrayOptions.ClearMemory);
            sLocal.Resize(index + 1, NativeArrayOptions.ClearMemory);
            sLocal[index] = new BasisConstraintTransform
            {
                LocalPosition = float3.zero,
                LocalRotation = quaternion.identity,
                LocalScale = new float3(1f, 1f, 1f),
                ParentIndex = -1,
            };
            return index;
        }

        private static Transform GetWorldUpObject(BasisConstraintBase component)
        {
            return component switch
            {
                BasisAimConstraint aim => aim.worldUpObject,
                BasisLookAtConstraint lookAt => lookAt.worldUpObject,
                _ => null,
            };
        }

        /// <summary>
        /// Re-reads the cheap per-frame state — weights, offsets, masks, rest poses, source weights
        /// — straight off the components, so inspector and script edits take effect the same frame
        /// without a rebuild.
        /// </summary>
        private static void RefreshDynamicState()
        {
            for (int Index = 0; Index < sRegistrations.Count; Index++)
            {
                Registration registration = sRegistrations[Index];
                BasisConstraintBase component = registration.Component;
                if (component == null)
                {
                    sDirty = true;
                    continue;
                }

                // Swapping or assigning worldUpObject only takes effect through a rebuild, and
                // nothing on the components marks it dirty, so diff it here.
                if (GetWorldUpObject(component) != registration.WorldUpObject)
                {
                    sDirty = true;
                }

                BasisConstraintSlot slot = sSlots[registration.SlotIndex];
                FillScalarState(component, ref slot);
                sSlots[registration.SlotIndex] = slot;
                FillSourceState(component, registration);
            }
        }

        /// <summary>
        /// Copies every non-structural field off the component into its slot. Shared by the rebuild
        /// and the per-frame refresh so the two can never drift apart.
        /// </summary>
        private static void FillScalarState(BasisConstraintBase component, ref BasisConstraintSlot slot)
        {
            slot.Weight = Mathf.Clamp01(component.weight);
            slot.Active = (byte)(component.constraintActive ? 1 : 0);
            slot.Locked = (byte)(component.locked ? 1 : 0);

            switch (component)
            {
                case BasisPositionConstraint position:
                    slot.TranslationAtRest = position.translationAtRest;
                    slot.TranslationOffset = position.translationOffset;
                    slot.TranslationMask = (byte)position.translationAxis;
                    break;

                case BasisRotationConstraint rotation:
                    slot.RotationAtRest = ToQuaternion(rotation.rotationAtRest);
                    slot.RotationOffset = ToQuaternion(rotation.rotationOffset);
                    slot.RotationMask = (byte)rotation.rotationAxis;
                    break;

                case BasisScaleConstraint scale:
                    slot.ScaleAtRest = scale.scaleAtRest;
                    slot.ScaleOffset = scale.scaleOffset;
                    slot.ScaleMask = (byte)scale.scalingAxis;
                    break;

                case BasisParentConstraint parent:
                    slot.TranslationAtRest = parent.translationAtRest;
                    slot.RotationAtRest = ToQuaternion(parent.rotationAtRest);
                    slot.TranslationOffset = float3.zero;
                    slot.RotationOffset = quaternion.identity;
                    slot.TranslationMask = (byte)parent.translationAxis;
                    slot.RotationMask = (byte)parent.rotationAxis;
                    break;

                case BasisAimConstraint aim:
                    slot.RotationAtRest = ToQuaternion(aim.rotationAtRest);
                    slot.RotationOffset = ToQuaternion(aim.rotationOffset);
                    slot.RotationMask = (byte)aim.rotationAxis;
                    slot.AimVector = aim.aimVector;
                    slot.UpVector = aim.upVector;
                    slot.WorldUpVector = aim.worldUpVector;
                    slot.WorldUpKind = (BasisWorldUpKind)aim.worldUpType;
                    slot.UseUpObject = (byte)(aim.worldUpObject != null ? 1 : 0);
                    slot.Roll = 0f;
                    break;

                case BasisBlendConstraint blend:
                    slot.TranslationAtRest = blend.translationAtRest;
                    slot.RotationAtRest = ToQuaternion(blend.rotationAtRest);
                    slot.TranslationOffset = float3.zero;
                    slot.RotationOffset = quaternion.identity;
                    // Unity's blendPosition / blendRotation toggles ride the axis masks, so turning a
                    // channel off and masking out all three of its axes are the same thing here.
                    slot.TranslationMask = blend.blendPosition
                        ? (byte)blend.translationAxis
                        : (byte)BasisConstraintAxis.None;
                    slot.RotationMask = blend.blendRotation
                        ? (byte)blend.rotationAxis
                        : (byte)BasisConstraintAxis.None;
                    slot.PositionChannelWeight = Mathf.Clamp01(blend.positionWeight);
                    slot.RotationChannelWeight = Mathf.Clamp01(blend.rotationWeight);
                    break;

                case BasisChainIK chain:
                    slot.RotationChannelWeight = Mathf.Clamp01(chain.tipRotationWeight);
                    slot.PositionChannelWeight = Mathf.Clamp01(chain.chainRotationWeight);
                    slot.Tolerance = Mathf.Max(0f, chain.tolerance);
                    slot.MaxIterations = Mathf.Clamp(chain.maxIterations, 1, 50);
                    break;

                case BasisTwoBoneIK twoBone:
                    slot.PositionChannelWeight = Mathf.Clamp01(twoBone.targetPositionWeight);
                    slot.RotationChannelWeight = Mathf.Clamp01(twoBone.targetRotationWeight);
                    slot.HintWeight = Mathf.Clamp01(twoBone.hintWeight);
                    slot.BindPosition = twoBone.TargetOffsetPosition;
                    slot.BindRotation = twoBone.TargetOffsetRotation;
                    break;

                case BasisMultiReferential referential:
                    // Scalar, so handing leadership to another member takes effect next frame with
                    // no rebuild — which is what lets this follow a runtime-driven index at all.
                    slot.DriverIndex = referential.ResolvedDriver;
                    break;

                case BasisTwistChain twistChain:
                    slot.PositionChannelWeight = Mathf.Clamp01(twistChain.blend);
                    slot.BindRotation = twistChain.BindOffset;
                    slot.RotationAtRest = ToQuaternion(twistChain.rotationAtRest);
                    slot.RotationMask = (byte)BasisConstraintAxis.All;
                    break;

                case BasisTwistCorrection twist:
                    slot.BindRotation = twist.SourceInverseBindRotation;
                    slot.RotationAtRest = twist.TwistBindRotation;
                    slot.AimVector = twist.TwistAxisVector;
                    // Signed on purpose: a negative share counters the source's twist.
                    slot.PositionChannelWeight = Mathf.Clamp(twist.twistWeight, -1f, 1f);
                    break;

                case BasisDampedTransform damped:
                    // Damp values are resistance, not weight: 0 snaps onto the source, 1 never moves.
                    slot.PositionChannelWeight = Mathf.Clamp01(damped.dampPosition);
                    slot.RotationChannelWeight = Mathf.Clamp01(damped.dampRotation);
                    slot.BindPosition = damped.BindPosition;
                    slot.BindRotation = damped.BindRotation;
                    slot.AimVector = damped.AimBindAxis;
                    slot.MaintainAim = (byte)(damped.maintainAim ? 1 : 0);
                    break;

                case BasisOverrideTransform over:
                    slot.TranslationMask = (byte)over.translationAxis;
                    slot.RotationMask = (byte)over.rotationAxis;
                    slot.PositionChannelWeight = Mathf.Clamp01(over.positionWeight);
                    slot.RotationChannelWeight = Mathf.Clamp01(over.rotationWeight);
                    slot.OverridePosition = over.position;
                    slot.OverrideRotation = ToQuaternion(over.rotation);
                    slot.OverrideSpace = (BasisOverrideSpace)over.space;
                    slot.UseOverrideSource = (byte)(over.useSource ? 1 : 0);
                    slot.BindPosition = over.SourceInvBindPosition;
                    slot.BindRotation = over.SourceInvBindRotation;
                    slot.SourceToSpaceRotation = over.SourceToSpaceRotation;
                    break;

                case BasisLookAtConstraint lookAt:
                    slot.RotationAtRest = ToQuaternion(lookAt.rotationAtRest);
                    slot.RotationOffset = ToQuaternion(lookAt.rotationOffset);
                    slot.RotationMask = (byte)BasisConstraintAxis.All;
                    // Look-at is an aim constraint pinned to Unity's convention: +Z at the target,
                    // +Y rolled up, with roll layered on top of the resolved basis.
                    slot.AimVector = new float3(0f, 0f, 1f);
                    slot.UpVector = new float3(0f, 1f, 0f);
                    slot.WorldUpVector = new float3(0f, 1f, 0f);
                    slot.WorldUpKind = lookAt.useUpObject && lookAt.worldUpObject != null
                        ? BasisWorldUpKind.ObjectUp
                        : BasisWorldUpKind.SceneUp;
                    slot.UseUpObject = (byte)(lookAt.useUpObject && lookAt.worldUpObject != null ? 1 : 0);
                    slot.Roll = lookAt.roll;
                    break;
            }
        }

        /// <summary>
        /// Refreshes the flattened source rows: weights for every kind, plus the per-source pose
        /// offsets only the parent constraint carries.
        /// </summary>
        private static void FillSourceState(BasisConstraintBase component, Registration registration)
        {
            if (registration.SourceCount == 0)
            {
                return;
            }

            BasisParentConstraint parent = component as BasisParentConstraint;
            for (int Index = 0; Index < registration.SourceCount; Index++)
            {
                int flattened = registration.SourceStart + Index;
                int authored = registration.SourceMap[Index];

                BasisConstraintSource source = sSources[flattened];
                source.Weight = math.max(0f, component.GetSource(authored).weight);

                if (parent != null)
                {
                    source.PositionOffset = authored < parent.translationOffsets.Length
                        ? parent.translationOffsets[authored]
                        : Vector3.zero;
                    source.RotationOffset = authored < parent.rotationOffsets.Length
                        ? ToQuaternion(parent.rotationOffsets[authored])
                        : quaternion.identity;
                }

                sSources[flattened] = source;
            }
        }

        /// <summary>
        /// Records the bone chain a kind drives when it poses more than its own transform. Each bone
        /// is interned twice over: once in the sampled transform table so the solve can read its
        /// world pose, and once as a results row so the write job can pose it. Bones already claimed
        /// by another constraint reuse the same row, so stacked constraints still merge per channel
        /// instead of racing.
        /// </summary>
        private static void BuildChain(BasisConstraintBase component, ref BasisConstraintSlot slot)
        {
            slot.ChainStart = sChain.Length;
            slot.ChainCount = 0;

            if (component is BasisMultiReferential referential)
            {
                BuildReferentialChain(referential, ref slot);
                return;
            }

            if (component is BasisChainIK chainIK)
            {
                BuildBoneChain(chainIK, ref slot);
                return;
            }

            if (component is not BasisTwoBoneIK twoBone)
            {
                return;
            }

            Transform mid = twoBone.ResolveMid();
            Transform root = twoBone.ResolveRoot();
            if (mid == null || root == null)
            {
                // Too shallow to be a limb; leaving the chain empty makes the solve a no-op rather
                // than posing whatever happened to be nearby.
                return;
            }

            AppendChainBone(root);
            AppendChainBone(mid);
            AppendChainBone(component.transform);
            slot.ChainCount = sChain.Length - slot.ChainStart;
        }

        /// <summary>
        /// Walks up from the constrained tip to the declared root and records the bones root-first.
        /// A root that is not actually an ancestor leaves the chain empty rather than guessing.
        /// </summary>
        private static void BuildBoneChain(BasisChainIK chainIK, ref BasisConstraintSlot slot)
        {
            Transform root = chainIK.root;
            if (root == null)
            {
                return;
            }

            sChainScratch.Clear();
            Transform cursor = chainIK.transform;
            while (cursor != null)
            {
                sChainScratch.Add(cursor);
                if (cursor == root)
                {
                    break;
                }
                cursor = cursor.parent;
            }

            if (sChainScratch.Count < 2 || sChainScratch[sChainScratch.Count - 1] != root)
            {
                return;
            }

            for (int Index = sChainScratch.Count - 1; Index >= 0; Index--)
            {
                AppendChainBone(sChainScratch[Index]);
            }
            slot.ChainCount = sChain.Length - slot.ChainStart;
        }

        /// <summary>
        /// Records the referential members and the arrangement they were captured in. A member with
        /// no captured bind is skipped entirely rather than held at the origin.
        /// </summary>
        private static void BuildReferentialChain(BasisMultiReferential referential, ref BasisConstraintSlot slot)
        {
            List<Transform> members = referential.members;
            Vector3[] positions = referential.BindPositions;
            Quaternion[] rotations = referential.BindRotations;
            if (members == null || members.Count < 2
                || positions.Length != members.Count || rotations.Length != members.Count)
            {
                return;
            }

            for (int Index = 0; Index < members.Count; Index++)
            {
                Transform member = members[Index];
                if (member == null)
                {
                    return;
                }
                AppendChainBone(member);
                // AppendChainBone parks a placeholder so the two buffers stay index-parallel for
                // every chain kind; only this one has a meaningful bind to put there.
                sChainBind[sChainBind.Length - 1] = new BasisConstraintWorld
                {
                    Position = positions[Index],
                    Rotation = rotations[Index],
                    Scale = new float3(1f, 1f, 1f),
                };
            }
            slot.ChainCount = sChain.Length - slot.ChainStart;
        }

        private static void AppendChainBone(Transform bone)
        {
            int boneIndex = InternTransform(bone);

            // Resolve the bone's parent row too. Without it the solve would hand back each bone's
            // world rotation as though it were a local one, and the write would then compose it under
            // a parent that had already moved — the limb lands bent when it should be straight.
            int parentIndex = bone.parent != null ? InternTransform(bone.parent) : -1;
            BasisConstraintTransform local = sLocal[boneIndex];
            local.ParentIndex = parentIndex;
            sLocal[boneIndex] = local;

            sChain.Add(new int2(boneIndex, InternTargetRow(bone)));
            // Kept index-parallel with sChain for every kind, so the solve can index one from the
            // other without a second range on the slot.
            sChainBind.Add(default);
        }

        /// <summary>Row this transform is written through, claiming a new one the first time.</summary>
        private static int InternTargetRow(Transform target)
        {
            if (!sTargetRowLookup.TryGetValue(target, out int targetRow))
            {
                targetRow = sTargetScratch.Count;
                sTargetRowLookup[target] = targetRow;
                sTargetScratch.Add(target);
            }
            return targetRow;
        }

        /// <summary>
        /// The SDK and framework kind enums are declared in lockstep; the cast is the mapping.
        /// </summary>
        private static BasisConstraintKind ToKind(BasisConstraintType type) => (BasisConstraintKind)type;

        private static int ToDepth(Transform transform)
        {
            int depth = 0;
            Transform cursor = transform.parent;
            while (cursor != null)
            {
                depth++;
                cursor = cursor.parent;
            }
            return depth;
        }

        private static quaternion ToQuaternion(Vector3 eulerDegrees) => Quaternion.Euler(eulerDegrees);
    }
}
