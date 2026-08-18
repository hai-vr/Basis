using System.Globalization;
using System.Text;

namespace Basis.Benchmark.Machine;

/// <summary>
/// The kernel socket-buffer ceilings, and whether the server's request for them is being silently
/// cut down.
///
/// <para>This is here because it is the one misconfiguration that is invisible from inside the
/// process. <c>BindSocket</c> asks for a 32 MB SO_RCVBUF; Linux clamps that to
/// <c>net.core.rmem_max</c>, which defaults to roughly 208 KB, and the call reports success either
/// way. Nothing logs it. The symptom surfaces much later and in the wrong place: kernel receive
/// drops that look like a CPU problem, except the CPU is idle.</para>
///
/// <para>A benchmark that does not read these will confidently conclude "this box tops out at N
/// players" when what it actually measured was a 208 KB buffer. So it is checked before any load
/// runs and a clamp is reported as a blocking finding rather than folded into the tuning result —
/// no setting this tool can write will compensate for it.</para>
/// </summary>
public sealed class KernelTuning
{
    /// <summary>What NetConstants.SocketBufferSize asks the kernel for.</summary>
    public const long RequestedSocketBufferBytes = 32L * 1024 * 1024;

    public long RmemMax { get; init; }
    public long WmemMax { get; init; }
    public long NetdevMaxBacklog { get; init; }

    public bool RmemClamped => RmemMax > 0 && RmemMax < RequestedSocketBufferBytes;
    public bool WmemClamped => WmemMax > 0 && WmemMax < RequestedSocketBufferBytes;
    public bool AnyClamped => RmemClamped || WmemClamped;

    public static KernelTuning Read() => new()
    {
        RmemMax = ReadLong("/proc/sys/net/core/rmem_max"),
        WmemMax = ReadLong("/proc/sys/net/core/wmem_max"),
        NetdevMaxBacklog = ReadLong("/proc/sys/net/core/netdev_max_backlog"),
    };

    private static long ReadLong(string path)
    {
        try
        {
            string text = File.ReadAllText(path).Trim();
            return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : -1;
        }
        catch { return -1; }
    }

    /// <summary>
    /// The kernel's cumulative count of UDP datagrams discarded because nothing drained the socket
    /// in time. Returns -1 where it cannot be read.
    ///
    /// This is the receive side's only honest signal. A saturated receive thread is pinned to one
    /// core whether it is keeping up or not, so CPU cannot distinguish the two; the difference
    /// only exists in this counter.
    /// </summary>
    public static long ReadUdpReceiveBufferErrors()
    {
        try
        {
            string[] lines = File.ReadAllLines("/proc/net/snmp");
            for (int i = 0; i + 1 < lines.Length; i++)
            {
                if (!lines[i].StartsWith("Udp:", StringComparison.Ordinal)) continue;
                string[] keys = lines[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string[] values = lines[i + 1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                for (int k = 1; k < keys.Length && k < values.Length; k++)
                {
                    if (keys[k] == "RcvbufErrors" &&
                        long.TryParse(values[k], NumberStyles.Integer, CultureInfo.InvariantCulture, out long v))
                        return v;
                }
            }
        }
        catch { /* not Linux, or /proc not mounted */ }
        return -1;
    }

    public string Describe()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"  net.core.rmem_max            {Fmt(RmemMax)}{(RmemClamped ? "   <-- CLAMPS the 32 MB the server asks for" : "")}");
        sb.AppendLine($"  net.core.wmem_max            {Fmt(WmemMax)}{(WmemClamped ? "   <-- CLAMPS the 32 MB the server asks for" : "")}");
        sb.AppendLine($"  net.core.netdev_max_backlog  {(NetdevMaxBacklog > 0 ? NetdevMaxBacklog.ToString(CultureInfo.InvariantCulture) : "unreadable")}");
        return sb.ToString();
    }

    private static string Fmt(long bytes) =>
        bytes < 0 ? "unreadable" : bytes >= 1048576 ? $"{bytes / 1048576.0:F0} MB" : $"{bytes / 1024.0:F0} KB";

    /// <summary>The sysctl lines an operator should add, or null when nothing is clamped.</summary>
    public string? RemediationSnippet() => !AnyClamped ? null :
        "# /etc/sysctl.d/99-basis.conf   (apply with: sudo sysctl --system)\n" +
        "net.core.rmem_max = 33554432\n" +
        "net.core.wmem_max = 33554432\n";
}
