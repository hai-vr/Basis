using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Basis.Bench.Agent;

/// <summary>
/// Finds the server / load-client executable in a build directory and makes sure it can actually
/// be run.
///
/// <para><b>Why this exists.</b> On Linux the apphost has no extension, and the execute bit is the
/// first thing lost when a build is produced on Windows, copied off a share, pulled out of a CI
/// artifact, or unzipped — none of those carry Unix modes. <see cref="System.Diagnostics.Process"/>
/// then fails with a bare "Permission denied", which reads like the benchmark needs privileges it
/// does not need. The usual response is to re-run the whole thing under sudo, which works for the
/// wrong reason and leaves root-owned config files and logs behind for the next run to trip over.
/// Nothing here requires elevation: the ports are all above 1024, no sysctl is touched, and the
/// sockets ask for no privileged options.</para>
///
/// <para>So the missing bit is repaired in place when the file is ours to repair, and when it is
/// not, the error names the file and the one-line fix instead of leaving the caller to guess.</para>
/// </summary>
public static class LaunchTarget
{
    /// <summary>
    /// Resolves <paramref name="baseName"/> inside <paramref name="directory"/> — the Windows
    /// <c>.exe</c> if present, otherwise the extensionless Unix apphost — and returns a path that
    /// is ready to start.
    /// </summary>
    public static string Resolve(string directory, string baseName)
    {
        string? path = Find(directory, baseName)
            ?? throw new FileNotFoundException(
                $"Could not find {baseName} in {directory}. Build the solution in Release first.");
        EnsureExecutable(path);
        return path;
    }

    /// <summary>
    /// Locates the executable without touching it, for "is a build here?" probes. Discovery walks
    /// candidate directories that may not be the one finally used, and repairing permissions on a
    /// directory nobody is going to run is not this code's business.
    /// </summary>
    public static string? Find(string directory, string baseName)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return null;
        string windows = Path.Combine(directory, baseName + ".exe");
        if (File.Exists(windows)) return windows;
        string unix = Path.Combine(directory, baseName);
        return File.Exists(unix) ? unix : null;
    }

    /// <summary>
    /// Non-throwing form for the agent, which answers a request with an error string rather than
    /// dying. <paramref name="error"/> is null on success.
    /// </summary>
    public static bool TryResolve(string directory, string baseName, out string? path, out string? error)
    {
        path = null;
        error = null;
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            error = $"no directory '{directory}'";
            return false;
        }
        try
        {
            path = Resolve(directory, baseName);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Adds the execute bit on Unix when it is missing. No-op on Windows, and no-op when the file
    /// is already runnable — the common case, which costs one stat.
    /// </summary>
    public static void EnsureExecutable(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        UnixFileMode mode;
        try
        {
            mode = File.GetUnixFileMode(path);
        }
        catch (Exception ex)
        {
            // Not being able to read the mode is not itself fatal — let Process.Start be the one to
            // decide, and say what was tried if it fails.
            throw new InvalidOperationException(
                $"Could not read permissions on '{path}': {ex.Message}", ex);
        }

        const UnixFileMode AnyExecute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
        if ((mode & AnyExecute) != 0) return;

        try
        {
            // Mirror the read bits rather than granting a blanket 0755: a build directory that is
            // deliberately group- or user-private stays that way.
            UnixFileMode wanted = mode;
            if ((mode & UnixFileMode.UserRead) != 0) wanted |= UnixFileMode.UserExecute;
            if ((mode & UnixFileMode.GroupRead) != 0) wanted |= UnixFileMode.GroupExecute;
            if ((mode & UnixFileMode.OtherRead) != 0) wanted |= UnixFileMode.OtherExecute;
            if (wanted == mode) wanted |= UnixFileMode.UserExecute;

            File.SetUnixFileMode(path, wanted);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"'{path}' is not executable and the benchmark could not make it so ({ex.Message}). " +
                $"Run: chmod +x \"{path}\"" + Environment.NewLine +
                "The execute bit does not survive a Windows build, a zip, or most CI artifact " +
                "downloads. The benchmark itself needs no elevated permissions — every port it uses " +
                "is above 1024 and it changes no system settings — so do not reach for sudo here: " +
                "running it as root leaves root-owned config files and logs in the build directory " +
                "that the next unprivileged run cannot rewrite.", ex);
        }
    }
}
