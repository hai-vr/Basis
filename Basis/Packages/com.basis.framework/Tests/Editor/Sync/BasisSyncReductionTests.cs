using System;
using System.Collections.Generic;
using Basis.Scripts.Networking.Sync;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Basis.Tests.Sync
{
    /// <summary>
    /// Distance/relevance reduction, which decides how fast each locally-owned object sends and
    /// (when culling) who it sends to. This used to run per object inside TransmitIfDue, walking
    /// the whole receiver snapshot; it is now one batched Burst pass over every owned object.
    ///
    /// Covered here: the job's nearest-observer math, the relevance bitmask (including the two
    /// cases that are easy to get wrong — a radius of exactly zero, and stale bits surviving from
    /// a previous pass), mask indexing past the 64-player word boundary, parity against a plain
    /// managed reference implementation, parallel-write disjointness, the mask-to-player-id
    /// expansion, and the sentinel contract between BasisSyncedObject and the job.
    /// </summary>
    public class BasisSyncReductionTests
    {
        // ── helpers ──────────────────────────────────────────────────────────────

        sealed class Scene : IDisposable
        {
            public NativeArray<float3> ObjectPositions;
            public NativeArray<float> RadiusSq;
            public NativeArray<float3> PlayerPositions;
            public NativeArray<float> NearestSq;
            public NativeArray<ulong> Mask;
            public int MasksPerObject;
            public int PlayerCount;
            public int ObjectCount;

            public void Dispose()
            {
                if (ObjectPositions.IsCreated) ObjectPositions.Dispose();
                if (RadiusSq.IsCreated) RadiusSq.Dispose();
                if (PlayerPositions.IsCreated) PlayerPositions.Dispose();
                if (NearestSq.IsCreated) NearestSq.Dispose();
                if (Mask.IsCreated) Mask.Dispose();
            }
        }

        /// <summary>Builds a scene. radiusSq entries are the raw sentinel values (negative = not culling).</summary>
        static Scene Build(Vector3[] objects, float[] radiusSq, Vector3[] players)
        {
            var s = new Scene
            {
                ObjectCount = objects.Length,
                PlayerCount = players.Length,
                MasksPerObject = players.Length > 0 ? (players.Length + 63) / 64 : 1,
            };
            s.ObjectPositions = new NativeArray<float3>(Math.Max(1, objects.Length), Allocator.TempJob);
            s.RadiusSq = new NativeArray<float>(Math.Max(1, objects.Length), Allocator.TempJob);
            s.PlayerPositions = new NativeArray<float3>(Math.Max(1, players.Length), Allocator.TempJob);
            s.NearestSq = new NativeArray<float>(Math.Max(1, objects.Length), Allocator.TempJob);
            s.Mask = new NativeArray<ulong>(Math.Max(1, objects.Length * s.MasksPerObject), Allocator.TempJob);

            for (int i = 0; i < objects.Length; i++)
            {
                s.ObjectPositions[i] = objects[i];
                s.RadiusSq[i] = radiusSq[i];
            }
            for (int i = 0; i < players.Length; i++) s.PlayerPositions[i] = players[i];
            return s;
        }

        static void Run(Scene s)
        {
            var job = new BasisSyncReductionJob
            {
                ObjectPositions = s.ObjectPositions,
                RelevanceRadiusSq = s.RadiusSq,
                PlayerPositions = s.PlayerPositions,
                PlayerCount = s.PlayerCount,
                MasksPerObject = s.MasksPerObject,
                NearestSq = s.NearestSq,
                RelevanceMask = s.Mask,
            };
            job.Schedule(s.ObjectCount, 1).Complete();
        }

        /// <summary>Player slots flagged in-radius for one object, read back out of the bitmask.</summary>
        static List<int> Slots(Scene s, int objectIndex)
        {
            var slots = new List<int>();
            int maskBase = objectIndex * s.MasksPerObject;
            for (int m = 0; m < s.MasksPerObject; m++)
            {
                ulong bits = s.Mask[maskBase + m];
                for (int b = 0; b < 64; b++)
                {
                    if ((bits & (1ul << b)) != 0ul) slots.Add((m << 6) + b);
                }
            }
            return slots;
        }

        static float[] NotCulling(int n)
        {
            var r = new float[n];
            for (int i = 0; i < n; i++) r[i] = -1f;
            return r;
        }

        // ── nearest-observer distance ────────────────────────────────────────────

        [Test]
        public void Nearest_PicksTheClosestPlayer_NotTheFirstOrLast()
        {
            // Closest is the middle entry, so an implementation that grabbed players[0] or the
            // final one would still look plausible on a two-player scene.
            using var s = Build(
                new[] { new Vector3(0f, 0f, 0f) },
                NotCulling(1),
                new[] { new Vector3(10f, 0f, 0f), new Vector3(3f, 0f, 0f), new Vector3(7f, 0f, 0f) });

            Run(s);

            Assert.AreEqual(9f, s.NearestSq[0], 1e-4f, "nearest must be the 3m player, squared");
        }

        [Test]
        public void Nearest_IsSquaredDistance_InAllThreeAxes()
        {
            using var s = Build(
                new[] { new Vector3(1f, 2f, 3f) },
                NotCulling(1),
                new[] { new Vector3(4f, 6f, 3f) });

            Run(s);

            // (3,4,0) -> 9 + 16 + 0
            Assert.AreEqual(25f, s.NearestSq[0], 1e-4f);
        }

        [Test]
        public void Nearest_WithNoPlayers_StaysAtMaxValue()
        {
            // The job reports "nothing found"; the empty-instance rule (nearest = 0, i.e. full send
            // rate) is the driver's, applied after the join — see CompleteReductionPass.
            using var s = Build(new[] { Vector3.zero }, NotCulling(1), Array.Empty<Vector3>());

            Run(s);

            Assert.AreEqual(float.MaxValue, s.NearestSq[0], "no players must not read as distance zero");
        }

        [Test]
        public void Nearest_IsPerObject_NotShared()
        {
            using var s = Build(
                new[] { new Vector3(0f, 0f, 0f), new Vector3(100f, 0f, 0f) },
                NotCulling(2),
                new[] { new Vector3(2f, 0f, 0f) });

            Run(s);

            Assert.AreEqual(4f, s.NearestSq[0], 1e-3f);
            Assert.AreEqual(98f * 98f, s.NearestSq[1], 1e-1f);
        }

        // ── relevance mask ───────────────────────────────────────────────────────

        [Test]
        public void Mask_IncludesPlayersInsideRadius_AndExcludesThoseOutside()
        {
            using var s = Build(
                new[] { Vector3.zero },
                new[] { 25f }, // radius 5
                new[] { new Vector3(1f, 0f, 0f), new Vector3(50f, 0f, 0f), new Vector3(0f, 4f, 0f) });

            Run(s);

            CollectionAssert.AreEqual(new[] { 0, 2 }, Slots(s, 0));
        }

        [Test]
        public void Mask_BoundaryIsInclusive()
        {
            // The gate is `d2 <= radiusSq`; a player exactly on the radius is a recipient.
            using var s = Build(new[] { Vector3.zero }, new[] { 25f }, new[] { new Vector3(5f, 0f, 0f) });

            Run(s);

            CollectionAssert.AreEqual(new[] { 0 }, Slots(s, 0), "a player exactly on the radius is inside it");
        }

        [Test]
        public void Mask_RadiusZero_ClearsTheMaskAndIncludesNobody()
        {
            // Regression: "not culling" was first signalled by radiusSq == 0, which collided with a
            // legal culling radius of exactly 0 meaning "nobody". Such an object skipped the mask
            // clear and inherited the previous pass's bits. Not-culling is now negative.
            using var s = Build(new[] { Vector3.zero }, new[] { 0f }, new[] { new Vector3(1f, 0f, 0f) });
            s.Mask[0] = ulong.MaxValue; // stale bits from an earlier pass

            Run(s);

            CollectionAssert.IsEmpty(Slots(s, 0), "radius 0 means nobody, and must still clear the mask");
        }

        [Test]
        public void Mask_ClearsStaleBitsFromThePreviousPass()
        {
            // A player who walked out of radius must actually disappear from the recipient set,
            // rather than the new bits being OR'd onto the old ones.
            using var s = Build(new[] { Vector3.zero }, new[] { 4f }, new[] { new Vector3(1f, 0f, 0f), new Vector3(99f, 0f, 0f) });
            s.Mask[0] = ulong.MaxValue;

            Run(s);

            CollectionAssert.AreEqual(new[] { 0 }, Slots(s, 0));
        }

        [Test]
        public void Mask_IsUntouchedForNonCullingObjects()
        {
            // Non-culling objects never have their mask read, so the job must not spend writes on
            // them — and the driver must never expand one.
            using var s = Build(new[] { Vector3.zero }, new[] { -1f }, new[] { new Vector3(1f, 0f, 0f) });
            const ulong sentinel = 0xDEADBEEFul;
            s.Mask[0] = sentinel;

            Run(s);

            Assert.AreEqual(sentinel, s.Mask[0], "a non-culling object's mask span must be left alone");
            Assert.AreEqual(1f, s.NearestSq[0], 1e-4f, "...but its distance is still computed");
        }

        [Test]
        public void Mask_SpansMultipleWords_AtAndPastTheSixtyFourthPlayer()
        {
            // 100 players: slots 0..63 land in word 0, 64..99 in word 1. Only the far ones are
            // outside the radius, so the expected set straddles the boundary.
            var players = new Vector3[100];
            for (int i = 0; i < players.Length; i++)
            {
                // slots 60..70 inside a radius of 5, everything else far away
                players[i] = (i >= 60 && i <= 70) ? new Vector3(1f, 0f, 0f) : new Vector3(1000f + i, 0f, 0f);
            }

            using var s = Build(new[] { Vector3.zero }, new[] { 25f }, players);
            Assert.AreEqual(2, s.MasksPerObject, "100 players needs two mask words");

            Run(s);

            CollectionAssert.AreEqual(new[] { 60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70 }, Slots(s, 0));
        }

        [Test]
        public void Mask_EachObjectWritesOnlyItsOwnSpan()
        {
            // The job disables the parallel-for range check because index i writes
            // [i * MasksPerObject, +MasksPerObject) rather than i. That is only safe while the
            // spans stay disjoint, so pin it: culling and non-culling objects interleaved, and the
            // non-culling ones' sentinels must survive their neighbours' writes.
            const int objects = 64;
            var pos = new Vector3[objects];
            var radii = new float[objects];
            for (int i = 0; i < objects; i++)
            {
                pos[i] = Vector3.zero;
                radii[i] = (i % 2 == 0) ? 25f : -1f;
            }
            var players = new Vector3[70];
            for (int i = 0; i < players.Length; i++) players[i] = new Vector3(1f, 0f, 0f);

            using var s = Build(pos, radii, players);
            for (int i = 0; i < s.Mask.Length; i++) s.Mask[i] = 0xABCDul;

            Run(s);

            for (int i = 0; i < objects; i++)
            {
                if (i % 2 == 0)
                {
                    Assert.AreEqual(70, Slots(s, i).Count, $"culling object {i} should see every player");
                }
                else
                {
                    int b = i * s.MasksPerObject;
                    for (int m = 0; m < s.MasksPerObject; m++)
                    {
                        Assert.AreEqual(0xABCDul, s.Mask[b + m], $"non-culling object {i} word {m} was written by a neighbour");
                    }
                }
            }
        }

        // ── parity with a plain managed implementation ───────────────────────────

        [Test]
        public void MatchesManagedReference_OverARandomScene()
        {
            // Same shape as the receiver-side managed/Burst parity test: a fixed seed so failures
            // reproduce, and a straightforward reference loop the job must agree with exactly.
            var rng = new System.Random(20260802);
            const int objects = 200;
            const int players = 37;

            var objPos = new Vector3[objects];
            var radii = new float[objects];
            for (int i = 0; i < objects; i++)
            {
                objPos[i] = new Vector3(rng.Next(-500, 500), rng.Next(-50, 50), rng.Next(-500, 500));
                radii[i] = (i % 3 == 0) ? -1f : (float)(rng.NextDouble() * 40000.0);
            }
            var playerPos = new Vector3[players];
            for (int i = 0; i < players; i++)
            {
                playerPos[i] = new Vector3(rng.Next(-500, 500), rng.Next(-50, 50), rng.Next(-500, 500));
            }

            using var s = Build(objPos, radii, playerPos);
            Run(s);

            for (int i = 0; i < objects; i++)
            {
                float expectedNearest = float.MaxValue;
                var expectedSlots = new List<int>();
                for (int p = 0; p < players; p++)
                {
                    float d2 = (playerPos[p] - objPos[i]).sqrMagnitude;
                    if (d2 < expectedNearest) expectedNearest = d2;
                    if (radii[i] >= 0f && d2 <= radii[i]) expectedSlots.Add(p);
                }

                Assert.AreEqual(expectedNearest, s.NearestSq[i], expectedNearest * 1e-5f, $"nearest mismatch at object {i}");
                if (radii[i] >= 0f)
                {
                    CollectionAssert.AreEqual(expectedSlots, Slots(s, i), $"recipient mismatch at object {i}");
                }
            }
        }

        // ── mask -> player id expansion ──────────────────────────────────────────

        [Test]
        public void ExpandRecipientMask_MapsBitsToPlayerIdsAcrossWords()
        {
            var mask = new NativeArray<ulong>(2, Allocator.TempJob);
            try
            {
                var playerIds = new ushort[100];
                for (int i = 0; i < playerIds.Length; i++) playerIds[i] = (ushort)(500 + i);

                mask[0] = (1ul << 0) | (1ul << 63);   // slots 0 and 63
                mask[1] = (1ul << 0) | (1ul << 5);    // slots 64 and 69

                var dest = new ushort[playerIds.Length];
                int count = BasisSyncDriver.ExpandRecipientMask(mask, 0, 2, playerIds, dest);

                Assert.AreEqual(4, count);
                Assert.AreEqual(new ushort[] { 500, 563, 564, 569 }, new[] { dest[0], dest[1], dest[2], dest[3] });
            }
            finally
            {
                mask.Dispose();
            }
        }

        [Test]
        public void ExpandRecipientMask_HonoursTheObjectOffset_AndEmptyMasks()
        {
            var mask = new NativeArray<ulong>(4, Allocator.TempJob);
            try
            {
                var playerIds = new ushort[] { 10, 11, 12, 13 };
                mask[0] = 0b0001ul;  // object 0
                mask[1] = 0ul;
                mask[2] = 0b1010ul;  // object 1 -> slots 1 and 3
                mask[3] = 0ul;

                var dest = new ushort[8];

                int c0 = BasisSyncDriver.ExpandRecipientMask(mask, 0, 2, playerIds, dest);
                Assert.AreEqual(1, c0);
                Assert.AreEqual((ushort)10, dest[0]);

                int c1 = BasisSyncDriver.ExpandRecipientMask(mask, 2, 2, playerIds, dest);
                Assert.AreEqual(2, c1);
                Assert.AreEqual((ushort)11, dest[0]);
                Assert.AreEqual((ushort)13, dest[1]);

                mask[2] = 0ul;
                Assert.AreEqual(0, BasisSyncDriver.ExpandRecipientMask(mask, 2, 2, playerIds, dest), "an empty mask yields nobody");
            }
            finally
            {
                mask.Dispose();
            }
        }

        // ── the sentinel contract between the object and the job ─────────────────

        [Test]
        public void ReductionRadiusSq_UsesNegativeForNotCulling_SoRadiusZeroStillCulls()
        {
            // This is the coupling the radius-zero regression lived in: the job's `cull` test is
            // `radiusSq >= 0`, so BasisSyncedObject must never report "not culling" as 0.
            var go = new GameObject("reduction-sentinel");
            try
            {
                var o = go.AddComponent<BasisSyncedTransform>();

                o.RelevanceCulling = false;
                o.RelevanceRadius = 50f;
                Assert.Less(o.ReductionRadiusSq, 0f, "not culling must be negative, not zero");

                o.RelevanceCulling = true;
                o.RelevanceRadius = 0f;
                Assert.GreaterOrEqual(o.ReductionRadiusSq, 0f, "culling with radius 0 must stay a culling object");

                o.RelevanceCulling = true;
                o.RelevanceRadius = 7f;
                Assert.AreEqual(49f, o.ReductionRadiusSq, 1e-4f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void WantsReduction_TracksEitherFeature()
        {
            var go = new GameObject("reduction-wants");
            try
            {
                var o = go.AddComponent<BasisSyncedTransform>();

                o.DistanceReduction = false;
                o.RelevanceCulling = false;
                Assert.IsFalse(o.WantsReduction, "an object using neither feature must stay out of the pass");

                o.DistanceReduction = true;
                Assert.IsTrue(o.WantsReduction);

                o.DistanceReduction = false;
                o.RelevanceCulling = true;
                Assert.IsTrue(o.WantsReduction, "relevance culling alone still needs the pass");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // ── owned snapshot ───────────────────────────────────────────────────────

        [Test]
        public void OwnedSnapshot_TracksRegistrationInsteadOfRebuildingEveryFrame()
        {
            var a = new GameObject("owned-a");
            var b = new GameObject("owned-b");
            try
            {
                var oa = a.AddComponent<BasisSyncedTransform>();
                var ob = b.AddComponent<BasisSyncedTransform>();

                BasisSyncDriver.RegisterOwned(oa);
                BasisSyncDriver.RefreshOwnedSnapshotForTests();
                int afterFirst = BasisSyncDriver.OwnedSnapshotCount;

                // No change: a second refresh must be a no-op, which is the whole point of the
                // dirty flag replacing the per-frame HashSet copy.
                BasisSyncDriver.RefreshOwnedSnapshotForTests();
                Assert.AreEqual(afterFirst, BasisSyncDriver.OwnedSnapshotCount);

                BasisSyncDriver.RegisterOwned(ob);
                BasisSyncDriver.RefreshOwnedSnapshotForTests();
                Assert.AreEqual(afterFirst + 1, BasisSyncDriver.OwnedSnapshotCount, "a registration must reach the snapshot");

                BasisSyncDriver.UnregisterOwned(ob);
                BasisSyncDriver.RefreshOwnedSnapshotForTests();
                Assert.AreEqual(afterFirst, BasisSyncDriver.OwnedSnapshotCount, "an unregistration must reach the snapshot");

                // Re-registering the same object is a set no-op and must not duplicate it.
                BasisSyncDriver.RegisterOwned(oa);
                BasisSyncDriver.RefreshOwnedSnapshotForTests();
                Assert.AreEqual(afterFirst, BasisSyncDriver.OwnedSnapshotCount);
            }
            finally
            {
                foreach (var go in new[] { a, b })
                {
                    var o = go.GetComponent<BasisSyncedTransform>();
                    if (o != null) BasisSyncDriver.UnregisterOwned(o);
                    UnityEngine.Object.DestroyImmediate(go);
                }
                BasisSyncDriver.RefreshOwnedSnapshotForTests();
            }
        }
    }
}
