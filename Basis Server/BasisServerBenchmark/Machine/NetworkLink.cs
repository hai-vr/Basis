using System.Globalization;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace Basis.Benchmark.Machine;

/// <summary>
/// The network path out of this box: how fast the link claims to be, and what size datagram
/// survives it.
///
/// <para>Read rather than measured, because a throughput test would need a cooperating host on the
/// other end and the number it produced would be about that host as much as this one. What matters
/// for the capability question is simpler: a server whose egress at full population would exceed
/// its link has a ceiling that no setting can move, and an operator should be told that in advance
/// rather than discovering it as packet loss.</para>
///
/// <para><b>MTU is the part that bites unexpectedly.</b> The transport sizes its datagrams against
/// a normal 1500-byte path. On a cloud overlay network, a VPN, or a tunnelled interface the real
/// path MTU is commonly 1450 or lower, and every full-size datagram is then fragmented — which
/// multiplies the packet rate, and makes the loss of any one fragment destroy the whole datagram.
/// It is invisible from inside the process and shows up as unexplained loss under load.</para>
/// </summary>
public sealed class NetworkLink
{
    public string Name { get; init; } = "";

    /// <summary>Link speed in megabits per second, or -1 when the interface will not say.</summary>
    public long SpeedMbps { get; init; } = -1;

    public int Mtu { get; init; } = -1;

    /// <summary>True when this is a loopback interface, i.e. not a path to anywhere.</summary>
    public bool IsLoopback { get; init; }

    /// <summary>Standard Ethernet payload MTU. Anything below this fragments full-size datagrams.</summary>
    public const int StandardMtu = 1500;

    public bool MtuIsReduced => Mtu > 0 && Mtu < StandardMtu;

    /// <summary>
    /// The interface most likely to carry player traffic: the first up, non-loopback one with a
    /// real address. Good enough for a capability statement, and wrong only on a multi-homed box
    /// where the operator already knows better than this heuristic.
    /// </summary>
    public static NetworkLink? Primary()
    {
        try
        {
            NetworkInterface[] all = NetworkInterface.GetAllNetworkInterfaces();
            NetworkInterface? best = all
                .Where(n => n.OperationalStatus == OperationalStatus.Up)
                .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .Where(n => n.GetIPProperties().UnicastAddresses
                    .Any(a => a.Address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6))
                .OrderByDescending(n => n.Speed)
                .FirstOrDefault();

            if (best == null) return null;

            int mtu = -1;
            try { mtu = best.GetIPProperties().GetIPv4Properties()?.Mtu ?? -1; }
            catch { /* no IPv4 on this interface */ }

            // Speed is reported in bits/s, and is -1 or 0 on interfaces that will not say — which
            // on Linux is the common case rather than the exception. virtio, veth and most cloud
            // NICs report nothing through the managed API, so without the sysfs fallback the
            // bandwidth ceiling would silently vanish on exactly the platform that hosts the large
            // instances it matters for.
            long speedMbps = best.Speed > 0 ? best.Speed / 1_000_000 : ReadLinuxSpeedMbps(best.Name);

            return new NetworkLink
            {
                Name = best.Name,
                SpeedMbps = speedMbps,
                Mtu = mtu > 0 ? mtu : ReadLinuxMtu(best.Name),
                IsLoopback = false,
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Link speed from sysfs, in Mbit/s, or -1.
    ///
    /// Returns -1 for interfaces with no fixed speed rather than guessing — reading it fails with
    /// EINVAL on virtual devices, and a fabricated number here would produce a bandwidth ceiling
    /// stated with total confidence about a link nobody measured.
    /// </summary>
    private static long ReadLinuxSpeedMbps(string name)
    {
        try
        {
            string text = File.ReadAllText($"/sys/class/net/{name}/speed").Trim();
            return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long mbps) && mbps > 0
                ? mbps
                : -1;
        }
        catch { return -1; }
    }

    /// <summary>Falls back to sysfs, which answers on containers where the managed API does not.</summary>
    private static int ReadLinuxMtu(string name)
    {
        try
        {
            string text = File.ReadAllText($"/sys/class/net/{name}/mtu").Trim();
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int mtu) ? mtu : -1;
        }
        catch { return -1; }
    }

    public string Describe()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"  Link           {Name}, " +
                      (SpeedMbps > 0 ? $"{FormatSpeed(SpeedMbps)}" : "speed not reported") +
                      (Mtu > 0 ? $", MTU {Mtu}" : ""));
        if (MtuIsReduced)
            sb.AppendLine($"                 MTU is below {StandardMtu} - full-size datagrams will be fragmented on this path, " +
                          "which multiplies packet rate and makes one lost fragment destroy a whole datagram.");
        return sb.ToString();
    }

    public static string FormatSpeed(long mbps) =>
        mbps >= 1000 ? $"{mbps / 1000.0:0.#} Gbit/s" : $"{mbps} Mbit/s";
}
