# BasisServerBenchmark

Fits the server's settings to the machine it will actually run on, so they can be baked rather
than guessed.

## Why this exists

The server already resolves most of its own numbers at runtime — `BasisCpuBudget` hands out core
leases and measures its own ceilings, `BasisPopulationScale` sizes the queue and pool bounds from
population and memory, and the send-socket growth loop has to earn each socket it adds. None of
that needs replacing, and replacing it would be a downgrade: a value that adapts beats an offline
number that was right on the day it was measured.

What this tool covers is the remainder — the settings that *cannot* self-tune:

| Why it can't self-tune | Settings |
|---|---|
| Read once at boot, before any load exists to learn from | `MultiSocketCount` |
| A constant fitted on one machine and shipped to every other | `PeerUpdatePeersPerWorker`, `PeerUpdateParallelism` |
| A trade-off with no in-process feedback signal | `MergeHoldMs`, the bundle codec settings, the BSR rate constants |
| Outside the process entirely | `net.core.rmem_max` / `wmem_max` |

`MultiSocketCount` is the one worth reading twice. SO_REUSEPORT must be set on the primary socket
before bind, so it is fixed at `NetManager.Start()` and can never be raised later — and at its
default of `1` the *entire* `MaxSendSockets` growth path silently no-ops. A server in that state
can sit at 15% CPU with the reduction system maximally degraded while the kernel discards hundreds
of thousands of datagrams per 10s, and nothing in the logs names the cause.

## Modes

```
BasisServerBenchmark profile     # machine facts + offline microbenchmarks, ~2 min, no server
BasisServerBenchmark autotune    # find the capacity knee, sweep, write a fitted config, ~30-60 min
BasisServerBenchmark sweep       # the full research pass, hours
BasisServerBenchmark measure     # one operating point, N windows, printed
```

Typical operator run:

```
BasisServerBenchmark autotune \
  --server BasisServerConsole/bin/Release/net10.0 \
  --client BasisNetworkClientConsole/BasisNetworkClientConsole/bin/Release/net10.0 \
  --apply
```

Start the server and the load client once each beforehand, so both have written their default
config files. Configs are backed up and restored on exit, including on Ctrl-C.

## What it optimises, and what it refuses to

**Delivered receiver visits per second — never CPU.** The two disagree at exactly the moment it
matters. Past capacity the server sheds avatar updates at the queue bound; shedding is cheaper than
sending, so CPU comes back *down* while quality collapses. Measured on one box, enqueued-send drops
ran ~0% at 500 players, 0.2% at 1000 and 30% at 2000 — while CPU at 2000 read *lower* than at 1000.
A tuner scored on CPU picks that configuration and reports it as an improvement.

The objective is `(tick rate / slice count) × delivery ratio`, which no single lever can game:
lengthening the tick lowers it, slicing the roster lowers it, and shedding lowers it. It is an
upper bound on the true per-pair rate rather than the rate itself — the per-pair interval also
widens with distance — so it is only ever compared between arms at the same population and spawn
radius, where the distance distribution cancels.

## Three traps it is built around

**The control loop oscillates.** The slicing counter was measured swinging across 4/5/6 at a fixed
2000-player load, with CPU tracking it inversely across a 2.2× range. So: a 60-second warmup rather
than 30, at least five windows before any verdict, instantaneous fields averaged across every
sample in a window rather than read off its edges, and medians with a rank test rather than means.
One earlier run reported 7.8 cores for a workload that averages 10.9 by getting this wrong.

**`ms/tick` is not comparable across runs.** The tick rate adapts, so a cheaper tick just means
more ticks. Every rate here is per second of wall time. An optimisation once "improved" the update
phase from 4.556 to 3.340 ms/tick while doing identical work per second.

**Loopback lies, selectively.** The kernel does receive-side processing inline inside the sender
and charges for bytes rather than datagrams, so a change removing 45% of datagrams at constant
bytes measures as ~zero on one box and is a real win over a NIC. CPU-side findings — parallel
widths, codec cost, allocation behaviour — measure honestly. Each setting carries a
`LoopbackConfidence`, and untrusted ones are measured and reported but never written.

## Discipline

- **No difference is a result.** `Verdict.NoDifference` keeps the incumbent. A tuner that resolves
  every comparison into a winner bakes the run's noise into the config and calls it a measurement.
- **The combination is confirmed.** Settings are swept one at a time, then the accepted set is
  re-run together against the original baseline. If the combination loses, the whole set is
  withdrawn rather than partly kept — greedy single-factor sweeping is wrong exactly when settings
  interact, and this is what catches it.
- **Preconditions are checked, not assumed.** Sweeping a setting whose enabling condition is unmet
  gives a clean, reproducible, meaningless null result that reads just like "this doesn't matter".
- **An idle box is refused.** If the design population leaves nothing scarce, no setting can change
  the outcome; the run says so instead of spending an hour proving it.
- **Blocking findings come first.** A clamped `rmem_max` invalidates every capacity number, so it
  is reported above the tuning result rather than as one row among twenty.

## A note on the corpus

The compression benchmark is a measurement of its input, and this input is easy to get
catastrophically wrong: the redundancy the codecs live on is *structural* — a room of near-idle
players emitting nearly the same payload — and is entirely absent from anything built out of random
bytes. A corpus built that way elsewhere in this tree measured a ratio of 1.005 against production's
~0.87. Prefer a captured one:

```
BASIS_BUNDLE_CAPTURE=<path> ./BasisNetworkClientConsole      # against a populated server
BasisServerBenchmark profile --corpus <path>
```

Without one a modelled crowd is generated and labelled as such — its throughput figures are real,
its ratios only indicative.

## Not mirrored

Unlike the server sources, this project is **not** mirrored into
`Basis/Packages/com.basis.server`. It is a standalone tool, nothing in the Unity client compiles
against it, and copying it there would only drag another console app into the Unity scan path.
