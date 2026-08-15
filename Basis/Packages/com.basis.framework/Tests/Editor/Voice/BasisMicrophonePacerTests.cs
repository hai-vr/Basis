using NUnit.Framework;
using System.Collections.Generic;

namespace Basis.Tests.Voice
{
    public class BasisMicrophonePacerTests
    {
        private const long FramePeriod = 200000;

        private sealed class Sim
        {
            public readonly List<long> Releases = new List<long>();
            public readonly List<long> Holds = new List<long>();
            public long MaxWait;
            public int Remaining;

            public long MaxHold
            {
                get
                {
                    long max = 0;
                    for (int i = 0; i < Holds.Count; i++)
                    {
                        if (Holds[i] > max) max = Holds[i];
                    }
                    return max;
                }
            }

            public long MinGap
            {
                get
                {
                    long min = long.MaxValue;
                    for (int i = 1; i < Releases.Count; i++)
                    {
                        long gap = Releases[i] - Releases[i - 1];
                        if (gap < min) min = gap;
                    }
                    return Releases.Count > 1 ? min : 0;
                }
            }

            public long MaxGap
            {
                get
                {
                    long max = 0;
                    for (int i = 1; i < Releases.Count; i++)
                    {
                        long gap = Releases[i] - Releases[i - 1];
                        if (gap > max) max = gap;
                    }
                    return max;
                }
            }

            public static Sim Run(long[] pumpTimes, long micPeriod, long processCost = 0)
            {
                Sim sim = new Sim();
                BasisMicrophonePacer pacer = new BasisMicrophonePacer();
                Queue<long> queued = new Queue<long>();

                long now = 0;
                long delivered = 0;
                int pumpIndex = 0;

                while (pumpIndex < pumpTimes.Length || queued.Count > 0)
                {
                    while (pumpIndex < pumpTimes.Length && pumpTimes[pumpIndex] <= now)
                    {
                        long pumpAt = pumpTimes[pumpIndex++];
                        long completed = pumpAt / micPeriod;
                        for (long frame = delivered; frame < completed; frame++)
                        {
                            queued.Enqueue(pumpAt);
                        }
                        delivered = completed;
                    }

                    if (pacer.TryRelease(now, queued.Count, FramePeriod, out long wait))
                    {
                        long deliveredAt = queued.Dequeue();
                        sim.Releases.Add(now);
                        sim.Holds.Add(now - deliveredAt);
                        now += processCost;
                        continue;
                    }

                    if (wait > sim.MaxWait) sim.MaxWait = wait;

                    long nextPump = pumpIndex < pumpTimes.Length ? pumpTimes[pumpIndex] : long.MaxValue;
                    long wake = wait > 0 ? now + wait : nextPump;
                    if (wake > nextPump) wake = nextPump;
                    if (wake == long.MaxValue) break;
                    now = wake;
                }

                sim.Remaining = queued.Count;
                return sim;
            }
        }

        private static long[] SteadyPumps(long interval, int count, long start = 0)
        {
            long[] times = new long[count];
            for (int i = 0; i < count; i++)
            {
                times[i] = start + interval * (i + 1);
            }
            return times;
        }

        [Test]
        public void EmptyRing_IsIdleAndResyncs()
        {
            BasisMicrophonePacer pacer = new BasisMicrophonePacer();
            pacer.TryRelease(500, 1, FramePeriod, out _);

            Assert.IsFalse(pacer.TryRelease(9000, 0, FramePeriod, out long wait));
            Assert.AreEqual(0, wait);
            Assert.AreEqual(9000, pacer.NextRelease);
        }

        [Test]
        public void BacklogAtBound_ReleasesWithoutHolding()
        {
            BasisMicrophonePacer pacer = new BasisMicrophonePacer();

            Assert.IsTrue(pacer.TryRelease(1000, BasisMicrophonePacer.MaxBacklogFrames, FramePeriod, out long wait));
            Assert.AreEqual(0, wait);
            Assert.AreEqual(1000, pacer.NextRelease);
            Assert.IsTrue(pacer.TryRelease(1000, BasisMicrophonePacer.MaxBacklogFrames, FramePeriod, out _));
        }

        [Test]
        public void StaleDeadline_DoesNotEmitDebtAsOneBurst()
        {
            BasisMicrophonePacer pacer = new BasisMicrophonePacer();
            Assert.IsTrue(pacer.TryRelease(0, 1, FramePeriod, out _));

            long resumed = FramePeriod * 40;
            Assert.IsTrue(pacer.TryRelease(resumed, 2, FramePeriod, out _));
            Assert.IsFalse(pacer.TryRelease(resumed, 2, FramePeriod, out long wait));
            Assert.AreEqual(FramePeriod, wait);
        }

        [Test]
        public void PumpAlignedToFramePeriod_AddsNoHold()
        {
            Sim sim = Sim.Run(SteadyPumps(FramePeriod, 200), FramePeriod);

            Assert.AreEqual(200, sim.Releases.Count);
            Assert.AreEqual(0, sim.MaxHold);
            Assert.AreEqual(0, sim.Remaining);
        }

        [Test]
        public void ThirtyFpsPump_SpreadsBurstsWithoutBunching()
        {
            long pumpInterval = FramePeriod * 5 / 3;
            Sim sim = Sim.Run(SteadyPumps(pumpInterval, 200), FramePeriod);

            Assert.Greater(sim.Releases.Count, 300);
            Assert.LessOrEqual(sim.MaxHold, FramePeriod);
            Assert.Greater(sim.MinGap, 0);
            Assert.LessOrEqual(sim.MaxGap, pumpInterval);
            Assert.AreEqual(0, sim.Remaining);
        }

        [Test]
        public void UnpacedThirtyFpsPump_WouldBunch()
        {
            long pumpInterval = FramePeriod * 5 / 3;
            long[] pumps = SteadyPumps(pumpInterval, 200);

            long delivered = 0;
            int bunched = 0;
            for (int i = 0; i < pumps.Length; i++)
            {
                long completed = pumps[i] / FramePeriod;
                if (completed - delivered > 1) bunched++;
                delivered = completed;
            }

            Assert.Greater(bunched, 0);
        }

        [Test]
        public void HitchRecovery_DrainsInsteadOfPacingTheWholeBacklog()
        {
            List<long> pumps = new List<long>(SteadyPumps(FramePeriod, 50));
            long stallEnd = pumps[pumps.Count - 1] + FramePeriod * 15;
            pumps.Add(stallEnd);
            pumps.AddRange(SteadyPumps(FramePeriod, 50, stallEnd));

            Sim sim = Sim.Run(pumps.ToArray(), FramePeriod);

            int simultaneous = 0;
            for (int i = 1; i < sim.Releases.Count; i++)
            {
                if (sim.Releases[i] == sim.Releases[i - 1]) simultaneous++;
            }

            Assert.AreEqual(0, sim.Remaining);
            Assert.Greater(simultaneous, 0);
            Assert.LessOrEqual(sim.MaxHold, FramePeriod * BasisMicrophonePacer.MaxBacklogFrames);
        }

        [Test]
        public void MicFasterThanPaceClock_DoesNotAccumulateBacklog()
        {
            long micPeriod = FramePeriod * 995 / 1000;
            Sim sim = Sim.Run(SteadyPumps(FramePeriod, 3000), micPeriod);

            Assert.LessOrEqual(sim.Remaining, BasisMicrophonePacer.MaxBacklogFrames);
            Assert.LessOrEqual(sim.MaxHold, FramePeriod * BasisMicrophonePacer.MaxBacklogFrames);
        }

        [Test]
        public void HoldNeverExceedsOneFramePeriod()
        {
            long pumpInterval = FramePeriod * 5 / 3;
            Sim sim = Sim.Run(SteadyPumps(pumpInterval, 300), FramePeriod, FramePeriod / 40);

            Assert.LessOrEqual(sim.MaxWait, FramePeriod);
        }
    }
}
