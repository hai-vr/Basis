# Applies the bdwgc dead-thread-suspend fix to an installed Unity editor's
# IL2CPP sources so player builds stop aborting with
# "GC ABORT: SuspendThread loop failed" (breakpoint crash in
# GC_stop_world/GC_suspend) when a managed thread died without unregistering
# from the Boehm GC (observed under Wine/Proton).
#
# Usage (elevated, or it will self-elevate):
#   .\Apply-BdwgcPatch.ps1                      # editor version read from ProjectSettings
#   .\Apply-BdwgcPatch.ps1 -EditorVersion 6000.5.2f1
#   .\Apply-BdwgcPatch.ps1 -EditorRoot "C:\Program Files\Unity\Hub\Editor\6000.5.2f1"
#
# IL2CPP compiles bdwgc from these sources into GameAssembly.dll on every
# player build (Bee tracks file content, so the next build picks it up
# automatically). Re-run after installing or upgrading a Unity editor.

param(
    [string]$EditorVersion,
    [string]$EditorRoot
)

$ErrorActionPreference = 'Stop'

if (-not $EditorRoot) {
    if (-not $EditorVersion) {
        $projVer = Join-Path $PSScriptRoot '..\..\Basis\ProjectSettings\ProjectVersion.txt'
        if (Test-Path $projVer) {
            $line = (Get-Content $projVer | Where-Object { $_ -match 'm_EditorVersion:' } | Select-Object -First 1)
            $EditorVersion = ($line -split ':')[1].Trim()
        }
    }
    if (-not $EditorVersion) { throw "Pass -EditorVersion or -EditorRoot." }
    $EditorRoot = "C:\Program Files\Unity\Hub\Editor\$EditorVersion"
}

$target = Join-Path $EditorRoot 'Editor\Data\il2cpp\external\bdwgc\win32_threads.c'
if (-not (Test-Path $target)) { throw "Not found: $target" }

$content = [IO.File]::ReadAllText($target)
if ($content.Contains('GetExitCodeThread(t->handle, &exitCode)')) {
    Write-Host "Already patched: $target"
    exit 0
}

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "Elevating..."
    $psArgs = @('-NoProfile','-ExecutionPolicy','Bypass','-File',"`"$PSCommandPath`"",'-EditorRoot',"`"$EditorRoot`"")
    $p = Start-Process powershell -Verb RunAs -ArgumentList $psArgs -Wait -PassThru
    exit $p.ExitCode
}

$anchorRetry = @'
# elif defined(RETRY_GET_THREAD_CONTEXT)
    for (;;) {
      if (SuspendThread(t->handle) != (DWORD)-1) {
'@

$replaceRetry = @'
# elif defined(RETRY_GET_THREAD_CONTEXT)
    for (;;) {
      /* Backported from upstream bdwgc GC_suspend: a thread that exited   */
      /* without unregistering (observed under Wine/Proton, where the      */
      /* DLL_THREAD_DETACH-driven detach can be skipped) can never be      */
      /* suspended; SuspendThread/GetThreadContext would fail              */
      /* MAX_SUSPEND_THREAD_RETRIES times and ABORT.  Drop the stale       */
      /* registration instead.                                             */
      {
        DWORD exitCode;

        if (GetExitCodeThread(t->handle, &exitCode)
            && exitCode != STILL_ACTIVE) {
          GC_release_dirty_lock();
          GC_delete_gc_thread_no_free(t);
          return;
        }
      }
      if (SuspendThread(t->handle) != (DWORD)-1) {
'@

$anchorPlain = @'
# else
    if (SuspendThread(t -> handle) == (DWORD)-1)
      ABORT("SuspendThread failed");
# endif
'@

$replacePlain = @'
# else
    {
      DWORD exitCode;

      if (GetExitCodeThread(t->handle, &exitCode)
          && exitCode != STILL_ACTIVE) {
        GC_release_dirty_lock();
        GC_delete_gc_thread_no_free(t);
        return;
      }
    }
    if (SuspendThread(t -> handle) == (DWORD)-1)
      ABORT("SuspendThread failed");
# endif
'@

# Normalize anchors to the file's line endings
$nl = if ($content.Contains("`r`n")) { "`r`n" } else { "`n" }
foreach ($v in 'anchorRetry','replaceRetry','anchorPlain','replacePlain') {
    Set-Variable $v ((Get-Variable $v -ValueOnly) -replace "`r`n", "`n" -replace "`n", $nl)
}

if (([regex]::Matches($content, [regex]::Escape($anchorRetry))).Count -ne 1) { throw "Retry-loop anchor not found exactly once; bdwgc source differs - update this script." }
if (([regex]::Matches($content, [regex]::Escape($anchorPlain))).Count -ne 1) { throw "Plain-suspend anchor not found exactly once; bdwgc source differs - update this script." }

$backup = "$target.orig"
if (-not (Test-Path $backup)) { Copy-Item $target $backup }
$content = $content.Replace($anchorRetry, $replaceRetry).Replace($anchorPlain, $replacePlain)
[IO.File]::WriteAllText($target, $content)
Write-Host "Patched: $target"
Write-Host "Backup:  $backup"
