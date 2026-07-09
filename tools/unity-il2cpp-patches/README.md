# Unity IL2CPP editor patches

Patches applied to the *installed Unity editor's* IL2CPP sources on Basis build
machines. IL2CPP compiles these sources into `GameAssembly.dll` during every
player build, so a patched editor produces fixed builds automatically (Bee
content-hashes inputs; no cache clearing needed). Re-apply after installing or
upgrading an editor.

## bdwgc-dead-thread-suspend

**Fixes:** hard crash (fatal breakpoint, exception `0x80000003`) on the main
thread inside `GameAssembly.dll` with this call chain (crash-handler stack in
`Player.log` / `error.log`):

```
GC_stop_world           (il2cpp/external/bdwgc/win32_threads.c)
GC_stopped_mark         (bdwgc/alloc.c)
GC_collect_a_little_inner
GC_collect_a_little
UnityPlayer.dll ... (incremental GC step in the player loop)
```

with `GC ABORT: SuspendThread loop failed` written to stderr (visible in the
Proton log, not in `Player.log`).

**Cause:** Boehm GC stop-the-world must suspend every registered managed
thread. A thread that *died without unregistering* leaves a stale registration
whose handle can never be suspended; Unity's bundled bdwgc retries
`SuspendThread`/`GetThreadContext` 1,000,000 times and then aborts the process.
Threads unregister via `DllMain(DLL_THREAD_DETACH)` →
`il2cpp::os::ThreadImpl::OnCurrentThreadExiting`; under Wine/Proton that
notification is occasionally lost on thread exit, so any managed-thread churn
(ThreadPool worker retirement, LiteNetLib `NetManager` stop, P2P connection
bursts) rolls the dice. Diagnosed from a Proton crash dump on 2026-07-08: the
GC hung on hash bucket 156 whose only plausible occupant (tid 1176) was a
just-died ThreadPool worker absent from the dump's live-thread list, with
`rdi = 1,000,000 = MAX_SUSPEND_THREAD_RETRIES` at the abort site.

**Fix:** backport of the upstream bdwgc `GC_suspend` guard
(<https://github.com/bdwgc/bdwgc/blob/master/win32_threads.c>): if
`GetExitCodeThread` reports the thread already exited, drop the stale
registration (`GC_delete_gc_thread_no_free` — unlinks and closes the handle
without freeing, so the `GC_stop_world` iterator stays valid) instead of
aborting. A dead thread has no stack to scan, so skipping it is safe; the
check is a definitive kernel query, so live threads can never be dropped.

**Apply:** `.\Apply-BdwgcPatch.ps1` (self-elevates; editor version read from
`Basis/ProjectSettings/ProjectVersion.txt`). `bdwgc-dead-thread-suspend.patch`
is the same change as a reviewable diff. The script is idempotent and keeps a
`win32_threads.c.orig` backup beside the target.

**Upstream:** report to Unity (their bdwgc fork predates the upstream fix) so
this patch can eventually be dropped. Suggested report: "IL2CPP player aborts
in GC_stop_world (SuspendThread loop failed) when a managed thread exits
without unregistering; upstream bdwgc handles this via GetExitCodeThread in
GC_suspend — please update the bundled bdwgc." Attach this README's analysis.
