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
| A trade-off with no in-process feedback signal | `MergeHoldMs`, the bundle codec settings, `BSRSendPhaseBudgetPercent` |
| Outside the process entirely | `net.core.rmem_max` / `wmem_max` |

`MultiSocketCount` is the one worth reading twice. SO_REUSEPORT must be set on the primary socket
before bind, so it is fixed at `NetManager.Start()` and can never be raised later — and at its
default of `1` the *entire* `MaxSendSockets` growth path silently no-ops. A server in that state
can sit at 15% CPU with the reduction system maximally degraded while the kernel discards hundreds
of thousands of datagrams per 10s, and nothing in the logs names the cause.

## Running it

No arguments. It finds the server and load-client directories relative to its own binary and opens
a console:

```
./BasisServerBenchmark
bench> /help
```

| Command | What it does |
|---|---|
| `/machine` | What this box is, and whether the kernel is limiting it |
| `/profile` | Offline benchmarks only — core scaling and codec cost. ~2 min, no server |
| `/measure [players]` | One operating point at that population, printed |
| `/auto [full]` | Climb until it breaks, fit the settings, confirm the combination. Hours |
| `/status` `/stop` | Watch or end the running job |
| `/findings` `/report` | What it has concluded, short or in full |
| `/write [path]` | Write the tuning profile the server reads at boot |
| `/show` `/set` | Run parameters — windows, window length, warmup, ladder ceiling |

A job runs on its own thread so the prompt stays live while it prints; `/stop` ends it cleanly
between windows rather than killing a server and a few thousand load clients mid-flight. Ctrl-C does
the same rather than exiting, because the process is holding the operator's configs.

Piped stdin runs commands **sequentially** instead of backgrounding them, so a script does what it
reads like:

```sh
printf '/auto\n/report\n/write\n/quit\n' | ./BasisServerBenchmark
```

`--auto` is the same thing with no prompt at all, for systemd or `nohup`. `--server` and
`--client` override the discovered paths.

Before the first run, start the server and the load client once each by hand so both write their
default configs — a server's first boot runs an *interactive* wizard this cannot answer.

At defaults an `/auto` is roughly two hours: a ladder to 1000 players, then about twenty sweep arms
at five minutes each. `/set windows 5` and `/set warmup-sec 45` roughly halve it at some cost in
confidence; `/set max-players` bounds the climb.

## First boot

The benchmark ships inside the server build, under `benchmark/` beside the server binary, with the
load client it drives under `benchmark/loadclient/`. On a server's **first** boot — after the setup
wizard, before anything is served — it offers to tune the machine:

```
  This machine has not been tuned yet.
  ...
  Tune this machine now? [y/N]
```

Say yes and it runs, writes the profile, applies it, and the server carries on with fitted settings.
Say no and the server starts on its defaults, which is a supported configuration.

`BASIS_AUTOTUNE=1` tunes without asking; `BASIS_AUTOTUNE=0` skips without asking. **With no terminal
and no variable set, it skips** — a server started by systemd that silently vanished for two hours
on its first boot would look like a failed deploy.

### It is never loaded into the server

The server launches the benchmark as a **child process** and names no type from it. That is not
tidiness, it is the only thing that works: the benchmark measures the server by *restarting* it,
repeatedly, because the values worth fitting include ones read once at socket bind and because the
runtime's adaptive state carries across a reconfiguration. Something that has to restart the server
cannot live inside it.

It also settles the memory question completely. The CLR maps an assembly when a method referencing
its types is first JITted — not when that code runs — so one direct call, however well guarded by an
`if`, would load the benchmark and its two compression dependencies into every server process for
the life of the instance. The project reference exists only for build ordering
(`ReferenceOutputAssembly="false"`), the benchmark appears nowhere in the server's `deps.json`, and a
normal boot costs one `File.Exists`.

Shipping without it is `rm -rf benchmark/`; the server detects the absence and says so.

### Two of them are fitted rather than swept

`PeerUpdatePeersPerWorker` comes from the core-scaling microbenchmark's knee at the design
population. `BSRSendPhaseBudgetPercent` comes from one subtraction on the ladder's own numbers: the
server reports what its send pass costs and what its whole tick costs, and the difference is what
the phases sharing that tick — the drain, message processing, the distance slice, the transport
kick — cost. That remainder is what the send budget has to leave room for.

Fitting beats sweeping here because of *which* number it uses. The obvious reading — how full the
send pass's own budget looks — is unstable in the direction that hides it: widen the budget, the
pool widens, the pass finishes sooner, its duty falls, and the next run reads the new value as
roomy and widens again. The non-send phases do not respond to the send pool's width at all, so a
share derived by subtracting them stays put once it is written. Four sweep arms would spend twenty
minutes arriving at a noisier version of the same subtraction.

## Handing the result to the server

`/write` produces `config/tuning-profile.xml` beside the server's config. On its next boot the
server finds it, applies each setting to whichever file declares it, and logs what changed:

```
[Tuning] Applying 'tuning-profile.xml' (measured 2026-08-18T11:00:00Z on linux-x64-64c, fitted at 1000 players)
[Tuning]   PeerUpdatePeersPerWorker: 0 -> 62  [litenetlib.xml, Microbenchmark]
[Tuning]   MultiSocketCount: 1 -> 8  [litenetlib.xml, Derived]
[Tuning] 2 setting(s) written into the config. config.xml is authoritative from here.
```

**Applied once, then folded into config.xml.** Keeping the profile authoritative and re-reading it
every boot is the obvious alternative and it is a trap: the operator later edits config.xml, the
profile silently overrides them on the next restart, and the setting appears not to work with
nothing anywhere explaining why. Instead the values are written into the config files, the profile
is stamped, and from that moment config.xml is the single source of truth again.

**It refuses to apply on different hardware.** Every setting in it is a function of the core count
and kernel it was measured on, so a 64-core profile landing on a 4-vCPU container is worse than no
profile at all. The fingerprint is coarse on purpose — OS family, architecture, core count — so
adding a stick of RAM does not reject it. `<ApplyToAnyMachine>true</ApplyToAnyMachine>` overrides.

Only findings that earned it get written. Anything measured on a topology that cannot judge it
honestly stays in the text report, where a person can weigh the caveat, and out of the file, which
a machine cannot.

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
