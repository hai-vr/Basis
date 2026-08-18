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
| `/burst [players]` | Everyone connects at once — what a restart looks like |
| `/auto [quick\|medium\|long] [full]` | Measure and fit — see the table below |
| `/status` `/stop` | Watch or end the running job |
| `/expect` | Plain-English capability sheet; also written to `what-to-expect.txt` |
| `/findings` `/report` | What it has concluded, short or in full |
| `/write [path]` | Write the tuning profile by hand — `/auto` already does this |
| `/show` `/set` | Run parameters — windows, window length, warmup, ladder ceiling |

A job runs on its own thread so the prompt stays live while it prints; `/stop` ends it cleanly
between windows rather than killing a server and a few thousand load clients mid-flight. Ctrl-C does
the same rather than exiting, because the process is holding the operator's configs.

Piped stdin runs commands **sequentially** instead of backgrounding them, so a script does what it
reads like:

```sh
printf '/auto\n/report\n/write\n/quit\n' | ./BasisServerBenchmark
```

`--auto [mode]` is the same thing with no prompt at all, for systemd or `nohup`; it defaults to
`medium`. `--server` and `--client` override the discovered paths.

Before the first run, start the server and the load client once each by hand so both write their
default configs — a server's first boot runs an *interactive* wizard this cannot answer.

### Three depths

| Mode | Time | What it can conclude |
|---|---|---|
| `quick` | ~5 min | Codec settings, parallel pass width, auth window. One load point is a *point*, not a curve — so no memory or bandwidth ceiling, and the player cap is only "this much worked" |
| `medium` (default) | ~15 min | Adds a 250/1k/2k ladder plus one bisection step. Three points are the fewest a curve can be fitted through, so this is where the player cap, the binding constraint and the capability sheet become real |
| `long` | ~2 h | Full ladder to 4k, two bisection steps, and the A/B setting sweep |

The sweep is roughly three quarters of a long run's wall time — one full server restart per arm —
and on a box with headroom it usually concludes that nothing measurably changed, because nothing was
scarce enough for a setting to relieve. It earns its cost on a machine actually working at the
population it serves. Add the word `full` to any mode to also *measure* the loopback-untrusted
settings; they are still never written.

Warmup never drops below 45s in any mode. That is not padding — the slicing controller oscillates
over several windows, and under about 45s a run records wherever that oscillation happened to be
rather than the steady state. Windows are what the modes trade.

Anything you `/set` by hand survives a mode: the profile fills in only what you have no opinion
about, and says which of its values it skipped. `/set knobs <name>` re-measures one setting after a
change; `/set max-players` bounds the climb.

## First boot

The benchmark ships inside the server build, under `benchmark/` beside the server binary, with the
load client it drives under `benchmark/loadclient/`. On a server's **first** boot — after the setup
wizard, before anything is served — it offers to tune the machine:

```
  This machine has not been tuned yet.

    1  quick    ~5 minutes    codec settings, parallel pass width, auth window
    2  medium   ~15 minutes   adds the player cap and what limits this box  (recommended)
    3  long     ~2 hours      adds the A/B setting sweep
    s  skip                   start now on the shipped defaults

  Which? [2]
```

Three choices rather than yes/no, because offering only the two-hour one gets it declined. Whatever
it runs, it writes the profile, applies it, and the server carries on with fitted settings.

`BASIS_AUTOTUNE` accepts `quick`, `medium` or `long`; `0`/`false` skips. `1`/`true` predate the modes
and map to `medium` — a config that used to mean "yes please" should not silently become a two-hour
outage. **With no terminal and no variable set, it skips** — a server started by systemd that
silently vanished on its first boot would look like a failed deploy.

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

**A run applies its own findings.** `/auto` writes `config/tuning-profile.xml` when it finishes —
measuring and then requiring a separate command to act on it is a good way to have nobody act on it.
`/set autowrite off` turns that back into report-only, and `/write` still works by hand.

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

## What to expect from this machine

`/expect` writes `what-to-expect.txt` — a plain-English capability sheet for whoever has to decide
how many players to advertise and whether the box needs more hardware. It answers four things the
tuning report does not:

**How many players**, three ways: the population served at full quality (measured), the population
that stays up while shedding, and the hard limit where a resource actually runs out.

**What binds first** — quality, CPU, memory or link bandwidth. Each is fitted separately against
population and solved for where it runs out, so the answer names the thing to fix. Quality binding
first is the *healthy* result: the server degrades by design, so on a machine with headroom it stops
delivering at full rate long before it exhausts anything physical, and no hardware will move that
number.

**What it costs at the operating point** — cores, memory, egress, per player and in total, plus the
share of voice frames a receiver actually heard.

**How it scales**, with the superlinear step stated outright. Every player is tracked against every
other, so cost grows with the square of the population: one run measured 2× the players costing 3.9×
the CPU. Capacity cannot be estimated by multiplying up from a small test, and this is the most
common way people get it wrong.

### The honesty rules it keeps

Fits are quadratic because the workload genuinely is — a linear fit through two rungs understates
the top badly. Coefficients are clamped non-negative, since none of these costs can fall as
population rises and three noisy points otherwise produce a downward curve that solves to nonsense.

Anything above the highest rung actually run is marked **extrapolated**, and anything more than 10×
past it is reported as "over N — not a limit within anything measured" rather than as a number. A
curve fitted to three populations and solved 200× beyond the largest is set by measurement error,
not by the machine; an early version printed a memory ceiling of 201,845 players, which is precise,
confident and meaningless.

A CPU sample that could not be read is carried as **unknown**, never as zero. That distinction is
load-bearing: reading a child process's CPU fails transiently, and the natural handling — reuse the
last value, so the delta is zero — produces "this server did 20 MB/s on no CPU at all", which then
fits a curve concluding the machine never runs out. It was observed intermittently before the fix.

## What it sets that you would otherwise have to guess

`PeerLimit` is the one worth calling out. It ships at 65535 — no cap at all — so a server admits
everyone and then discovers it cannot serve them. That failure is quiet and collective: past capacity
the reduction system sheds across the whole roster, so an overfull room does not fail for the last
arrivals, it degrades for everyone at once. The benchmark writes the measured full-quality ceiling
instead, lowered to a physical ceiling if one binds sooner, and leaves an operator's own tighter cap
alone.

## How the ladder finds the knee

Coarse rungs at **250 / 1,000 / 2,000 / 4,000**, then **bisection** between the last success and the
first failure — two refinement steps, cutting the bracket to a quarter of its width. If 2,000 fails
and 1,000 held, it tries 1,500 next.

Coarse-then-bisect rather than doubling, for two reasons. A doubling ladder leaves the answer known
only to within a factor of two, and "somewhere between 1,000 and 2,000" is not a number anyone can
set a player cap from. It also gets *slower the better the hardware is* — a strong box passes every
rung and pays for all of them — which is a perverse way to spend a test budget.

**A ladder that runs out of rungs has not found a limit.** `CapacityResult.KneeFound` records
whether any rung actually failed; when none did, the top rung is reported as `250+` — a floor, not a
ceiling — and the summary says so outright. Without that distinction a box sitting at 2% of its
cores gets described as "comfortably 250".

### Two limits, and why they can disagree

The capability sheet reports the software/CPU limit (measured) and the tightest physical limit
(fitted) side by side, and recommends the lower. These can look contradictory:

```
software/CPU     500   measured - this held and the next rung did not
bandwidth        386   fitted - 700 Mbit/s of the link's 1 Gbit/s
```

That is not a contradiction, and the sheet now explains it inline rather than printing the two as a
descending list. The server really did serve 500 players — but on a single-box run the load clients
shared the machine, so that traffic never crossed the NIC the fitted limit is measured against. The
bytes are real; the path they took is not. Over a real deployment the lower figure governs, which is
why it becomes the cap.

## Admission is measured separately from throughput

`/burst` starts every client at once (`ClientConnectIntervalMs=0`) and samples the population every
100 ms as it fills. `/auto` runs one at the design population automatically.

This needs its own test because admission and steady state are different subsystems under different
pressure: a handshake with several round trips and a signature verification per client, all racing a
per-client timeout, versus a send loop. **A box comfortable at 2,000 players can still be unable to
get 2,000 players in** — which has happened here, with 596 of 4,000 clients missing the auth window
after a restart, the only trace being a log line saying they were not in the authenticated set.

The measurement is the *ramp*, not the endpoint. "Everyone got in eventually" hides the race: the
last client in the queue waits for the whole burst to drain while its handshake is being timed. That
worst-case wait is what `AuthValidationTimeOutMiliseconds` is fitted from — doubled for headroom
(the burst is the good case: loopback, no loss, no retransmits) and with the server's own per-peer
widening subtracted so it is not double-counted.

## Running the crowd on another machine

On a single box the load clients share the server's cores, cache and memory bandwidth — measured at
**3.26 cores for the client alone at 1,000 players** — and the traffic never crosses a NIC. Both
problems have the same fix.

`BasisBenchAgent` runs on the load-generating machine:

```sh
# on the load machine
./BasisBenchAgent --client ./loadclient

# on the server machine
./BasisServerBenchmark --agent 10.0.0.7 --server-host 10.0.0.5
```

`--server-host` is required and cannot be inferred: the address this box calls itself is not the one
another machine uses to find it. A run that silently fell back to local load would produce
packet-rate findings that look measured and are not, so an unreachable agent aborts rather than
degrading.

Line-delimited JSON over TCP on port **4297** — deliberately not 4296, which is the server's UDP game
port that the clients on that machine are already talking to; sharing the number would stop the agent
ever running on the server's own box and make a packet capture ambiguous. The control connection is
held open for the whole run, because the agent stops its clients when it closes: a benchmark that
dies mid-run must not leave a thousand clients hammering the server with nothing owning them.

**What changes with an agent attached:** `MultiSocketCount`, `MergeHoldMs` and `MaxSendSockets` stop
being `Untrusted` — they are swept by default and their results are written, because the topology can
finally judge them. The report drops its loopback caveat.

The control channel is unauthenticated and starts processes on request, so keep it on a trusted
network.

### What sharing a box actually does to the CPU figure

The client's CPU is excluded from the server's score — both are sampled per process — but sharing a
machine still moves the number, and **not reliably in one direction**:

| | |
|---|---|
| inflates it | contention for cores, shared cache and memory bandwidth; lower boost clocks with more cores busy; loopback performs receive-side work inside the sender, so the server pays for delivery a NIC would have handled elsewhere |
| deflates it | no driver, no checksums, no interrupts, no wire — per-packet costs are understated, which is why packet-rate settings cannot be judged locally at all |

Which dominates is not known, so single-box CPU figures are indicative rather than a bound in either
direction. Byte counts, delivery and egress are real. A ladder rung where the server and client
together exceed 70% of the machine is flagged outright — past that it becomes possible that the
*client* ran out first, which would make the server look comfortable for a reason that has nothing to
do with the server.

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
