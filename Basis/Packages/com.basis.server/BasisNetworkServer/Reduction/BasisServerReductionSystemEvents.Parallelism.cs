using Basis.Network.Core;
using Basis.Network.Core.Compression;
using BasisNetworkServer.BasisNetworking;
using K4os.Compression.LZ4;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using static SerializableBasis;
using static Basis.Network.Core.Compression.BasisAvatarBitPacking;

namespace BasisNetworkServer.BasisNetworkingReductionSystem
{
    public partial class BasisServerReductionSystemEvents
    {
        private static readonly CancellationTokenSource cts = new();
        // Initial capacity for PeerTracking array on PlayerState.
        // Grows if a player ID exceeds this (doubling, lock-guarded, in the distance and send
        // loops). Peer ids are recycled so the high-water id tracks peak concurrent players: a
        // flat 2048 charged every player 64 KB of tracking up front regardless of population —
        // ~6 MB of dead weight at 100 players. 256 (8 KB) covers a small instance outright and a
        // growing one doubles a handful of times on its way up, which is a one-time copy of a
        // few-KB array per step.
        private const int InitialPlayerArrayCapacity = 256;

        // Sender/receiver pairs one worker gets through per millisecond the send pass is busy,
        // measured on this host. 0 until a pass has been timed. Pairs rather than players because
        // the pass visits pairs: its cost grows with the square of the population while a
        // per-player divisor grows linearly, which makes one constant two different policies at
        // 500 players and at 4000. Measuring folds in core speed, avatar size, bundling and
        // whatever the load controller is shedding, because all of those move this one number.
        private static double _pairsPerWorkerMs;

        // Share of the tick period the send pass is sized against. Not the whole period: the
        // drain, message processing, the distance slice and the transport kick share the same
        // tick, so sizing the send pass to fill it alone guarantees the overrun the load
        // controller sheds players on.
        //
        // What that remainder costs is a property of the host rather than of this code, so the
        // right split is too: 0.6 was fitted where the non-send phases came to about 30% of the
        // period, and a box whose distance sweep or message drain is dearer than that wants a
        // narrower send budget than one whose are cheaper. The server cannot close this loop from
        // the inside — widening the budget makes the send pass fit its budget and the tick overrun
        // anyway, which reads as success from within the send pass — so it is fitted offline by
        // the benchmark, from the measured cost of the phases this one has to share with, and
        // written into config.xml. See SetSendPhaseBudgetPercent.
        private const double DefaultSendPhaseBudgetShare = 0.6;
        private static double SendPhaseBudgetShare = DefaultSendPhaseBudgetShare;

        // Send pass duration over the budget above, smoothed; 1.0 means the pass exactly fills its
        // share of the period. Diagnostics — the width is sized from the measured rate, not
        // steered from this — but it says whether the sizing is working on a given host.
        private static double _sendBudgetDutyEma;

        // Shortest pass worth taking a rate from. Below roughly this, fork/join and timestamp
        // resolution are most of the sample, so a nearly empty server reads as a very slow pool
        // and would size the next population step several times too wide.
        private const double MinTimeableSendPassMs = 0.25;

        // Utilisation above which the machine is full and widening moves contention around rather
        // than work: "the pass is slow" does not mean "give it more workers" unless there are
        // cores to give.
        private const double WidenBelowUtilization = 0.70;

        // Workers the last send pass could actually run on — the configured degree, or the slice
        // if it was smaller. Dividing by the degree instead would read a slice too small to fill
        // the pool as a slow pool.
        private static int _lastSendWorkers;

        // Last time the worker count was allowed to move. See TuneParallelism.
        private static long _lastDegreeStepTick;

        private static int MaxAutoWorkers => BasisCpuBudget.ReductionSendCap;

        private static int _configuredDegree;

        // Width the next send pass wants, from what the last ones cost rather than from a
        // population constant. The pass is throughput-bound against a budget: it has
        // SendPhaseBudgetShare of the tick period to get through the pairs the slice puts in front
        // of it, and one worker gets through _pairsPerWorkerMs of them per millisecond on this
        // host. Estimating rather than reacting is the point: a population step raises the pair
        // count before the tick has overrun even once, so the pool widens ahead of the load
        // instead of after the load controller has started shedding players for it.
        // `current` is the width the pool is running at, which bounds how far one step moves.
        private static int DegreeFor(int playerCount, int current)
        {
            int cores = Environment.ProcessorCount;
            if (_configuredDegree > 0)
            {
                return Math.Max(1, Math.Min(_configuredDegree, cores));
            }

            int ceiling = Math.Min(MaxAutoWorkers, cores);
            if (ceiling < 1)
            {
                ceiling = 1;
            }

            int floor = Math.Min(BasisCpuBudget.MinWorkersPerPool, ceiling);
            if (floor < 1)
            {
                floor = 1;
            }

            double rate = _pairsPerWorkerMs;
            if (rate <= 0)
            {
                // Nothing timed yet, and the floor is the whole answer. A pass becomes timeable at
                // a couple of dozen players, while the population-derived seed this used to carry
                // — one worker per 128 players, fitted on a 32-thread box — could not exceed a
                // floor of 4 until 640 players, several hundred after the measured rate had taken
                // over. It was a constant that no host ever actually reached.
                return floor;
            }

            // What the next pass will actually do. Receivers are sliced; the roster each of them is
            // compared against is not, which is why pairs are the unit and players are not.
            int sliceCount = Math.Max(1, _sliceCount);
            double pairs = (double)((playerCount + sliceCount - 1) / sliceCount) * playerCount;
            double budgetMs = Math.Max(1.0, intervalMs) * SendPhaseBudgetShare;
            double needed = pairs / (rate * budgetMs);

            // Compared before the cast: on a host where the measured rate has collapsed this is a
            // number no int can hold, and the ceiling is the answer anyway.
            int target = needed >= ceiling ? ceiling : (int)Math.Ceiling(needed);
            if (target < floor)
            {
                target = floor;
            }

            if (current < floor)
            {
                current = floor;
            }
            if (current > ceiling)
            {
                current = ceiling;
            }

            if (target == current)
            {
                return current;
            }

            if (target < current)
            {
                // Give workers back one at a time. The estimate moves with the population and with
                // whatever the load controller is shedding, and dropping straight to a momentarily
                // low one turns a quiet second into a pool that has to climb again from the floor.
                return current - 1;
            }

            // Widen only where there are cores to widen into. On a host that is already full the
            // extra threads move contention around instead of doing work — measured at 2000 players
            // on a saturated 32-thread box, widening cost 16.2 to 25.2 cores and left the pass just
            // as slow. Unknown utilisation (0, a container that will not report it) is not treated
            // as full: the grant this is clamped to is already a machine-wide share.
            if (BasisCpuBudget.Utilization > WidenBelowUtilization)
            {
                return current;
            }

            // At most a doubling per step, so an estimate thrown off by one anomalous pass costs a
            // single step of scheduling rather than the whole pool, while a join storm still reaches
            // its width inside a few hundred milliseconds.
            return Math.Min(target, current * 2);
        }

        private static readonly ParallelOptions parallelOptions = new()
        {
            MaxDegreeOfParallelism = 4
        };

        // Retunes the worker count for what the next pass is expected to cost. Called once per tick
        // from the send phase, and moves on the core allocator's cadence rather than the tick's:
        // the grant it is clamped to only changes that often, and resizing a pool at ~275 Hz costs
        // more in thread-pool churn than the misallocation it would be correcting.
        private static void TuneParallelism(int playerCount)
        {
            long now = Stopwatch.GetTimestamp();
            if (_lastDegreeStepTick != 0 && now - _lastDegreeStepTick < RebalanceIntervalTicks)
            {
                return;
            }
            _lastDegreeStepTick = now;

            int current = parallelOptions.MaxDegreeOfParallelism;
            int desired = DegreeFor(playerCount, current);
            if (current != desired)
            {
                parallelOptions.MaxDegreeOfParallelism = desired;
            }
        }

        // Records what a send pass cost, in the unit the worker count is sized from: pairs per
        // millisecond the pass was busy, per worker that ran it. Per busy millisecond rather than
        // per second of wall clock — the second is set by the tick period and says nothing about
        // how wide the pool should be. Per worker, because a pass at twice the width is not
        // evidence about one worker until that is divided back out.
        private static void NoteSendPassCost(long pairs, double busyMs)
        {
            double budgetMs = Math.Max(1.0, intervalMs) * SendPhaseBudgetShare;
            double duty = busyMs / budgetMs;
            _sendBudgetDutyEma = _sendBudgetDutyEma <= 0 ? duty : _sendBudgetDutyEma * 0.9 + duty * 0.1;

            int workers = _lastSendWorkers;
            if (pairs <= 0 || workers <= 0 || busyMs < MinTimeableSendPassMs)
            {
                return;
            }

            double rate = pairs / (busyMs * workers);
            if (rate <= 0 || double.IsNaN(rate) || double.IsInfinity(rate))
            {
                return;
            }

            // Smoothed hard: one pass that straddled a GC pause is not a slower machine, and the
            // width it would ask for is one the pool then has to unwind a worker at a time.
            _pairsPerWorkerMs = _pairsPerWorkerMs <= 0 ? rate : _pairsPerWorkerMs * 0.9 + rate * 0.1;
        }

        // A dedicated worker pool was tried here in place of Parallel.For and did not pay for
        // itself: with the worker cap above already removing the oversubscription, what remained
        // of Parallel's overhead was smaller than the cost of waking a fixed set of threads on
        // every tick. Capping the degree is the win; replacing the scheduler is not.

        /// <summary>
        /// Sets the send pass's share of the tick period, as a percentage; 0 restores the fitted
        /// default. Clamped to 20..85 because both ends stop meaning anything: under 20 the pool is
        /// sized for a pass that would have to finish in a fifth of the period and asks for a width
        /// no machine has, and over 85 there is nothing left for the drain, the distance slice and
        /// the transport kick, which share the tick and are what a send budget is a share *of*.
        /// </summary>
        public static void SetSendPhaseBudgetPercent(int percent)
        {
            SendPhaseBudgetShare = percent <= 0
                ? DefaultSendPhaseBudgetShare
                : Math.Clamp(percent, 20, 85) / 100.0;
        }

        /// <summary>Workers the send pool is currently allowed to use.</summary>
        public static int SendWorkers => parallelOptions.MaxDegreeOfParallelism;

        /// <summary>Workers the core allocator currently grants the send pass.</summary>
        public static int SendWorkerCeiling => MaxAutoWorkers;

        /// <summary>
        /// Measured sender/receiver pairs one worker gets through per busy millisecond on this
        /// host. 0 until a pass has been timed. Published because it is the number the width is
        /// sized from, and a host reading far off its neighbours' is the first sign of why.
        /// </summary>
        public static double PairsPerWorkerMs => _pairsPerWorkerMs;

        /// <summary>
        /// Send pass duration over its budget, smoothed. 1.0 means the pass exactly fills its
        /// share of the period. Multiply by <see cref="SendPhaseBudgetPercent"/> to get the share
        /// of the whole tick the send pass is actually taking — which, subtracted from the tick's
        /// own duty, is what the rest of the tick costs and therefore what the budget share should
        /// have been. That subtraction is the one the benchmark fits this setting with.
        /// </summary>
        public static double SendBudgetDuty => _sendBudgetDutyEma;

        /// <summary>Share of the tick period the send pass is sized against, as a percentage.</summary>
        public static int SendPhaseBudgetPercent => (int)Math.Round(SendPhaseBudgetShare * 100);

        public static void SetMaxDegreeOfParallelism(int configured)
        {
            // The vector width every SIMD path in the server runs at is chosen by the JIT from the
            // host, so nothing in the build or the config says what it ended up being. It is the
            // first thing worth knowing when two machines disagree on throughput.
            BNL.Log($"[CPU] {BasisSimdCapabilities.Describe()}");

            _configuredDegree = configured;
            if (configured > 0)
            {
                int resolved = Math.Min(configured, Environment.ProcessorCount);
                parallelOptions.MaxDegreeOfParallelism = resolved;
                BNL.Log($"[BSR] Parallel worker cap pinned to {resolved} (of {Environment.ProcessorCount} cores).");
            }
            else
            {
                BNL.Log($"[CPU] {BasisCpuBudget.Describe()}");
                BNL.Log($"[BSR] Send workers sized from measured pass cost against {SendPhaseBudgetShare * 100:F0}% of the tick period" +
                        (SendPhaseBudgetShare == DefaultSendPhaseBudgetShare ? " (default)" : " (fitted, from config)") +
                        $", {BasisCpuBudget.MinWorkersPerPool} to {MaxAutoWorkers}; at the floor until this host has timed a pass.");
                // The memory-scaled ceilings, logged for the same reason the CPU ones are: they are
                // resolved from the box rather than read from the config file, so without this line
                // there is no way to see what a server actually chose. Quoted at a nominal 1000
                // players because the real values move with population as people join.
                BNL.Log($"[POP] at 1000 players this box would resolve: {BasisPopulationScale.Describe(1000, new LNLTransportConfig().PacketPoolSizePerPeer)}");
            }
        }
    }
}
