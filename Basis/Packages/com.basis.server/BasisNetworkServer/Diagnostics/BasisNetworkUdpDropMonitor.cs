using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace BasisNetworkServer
{
    /// <summary>
    /// Linux-only background sampler that polls /proc/net/snmp and warns when the kernel
    /// drops inbound UDP datagrams. Two failure modes show up here:
    ///   1) RcvbufErrors increasing  =>  receive thread can't drain the socket buffer fast
    ///      enough. Fix: raise MultiSocketCount (more recv threads sharing the port via
    ///      SO_REUSEPORT) or grow the kernel buffer (sysctl net.core.rmem_max).
    ///   2) InErrors > RcvbufErrors  =>  checksum/decode-level corruption, unrelated to
    ///      saturation. Indicates link/NIC issues.
    /// On non-Linux platforms Start() is a no-op.
    /// </summary>
    public static class BasisNetworkUdpDropMonitor
    {
        private const string SnmpPath = "/proc/net/snmp";
        private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(10);

        private static Thread _samplerThread;
        private static volatile bool _running;
        private static long _lastRcvbufErrors = -1;
        private static long _lastInErrors = -1;
        private static long _lastInCsumErrors = -1;

        private static long _recvBufferDropsTotal;

        /// <summary>
        /// Running total of receive-buffer drops since start.
        ///
        /// A saturated receive thread is invisible in CPU terms — the thread is pinned either way —
        /// so this is the signal that says the receive side, rather than the send side or the
        /// machine, is what limits throughput. The kernel is discarding datagrams because nothing
        /// drained them in time, which more receive threads (more SO_REUSEPORT sockets) can fix.
        ///
        /// Cumulative rather than take-and-reset so callers can compare rates across windows —
        /// "are drops falling now that I added a socket" is a different question from "are there
        /// drops", and only the first one can tell whether sockets are the right fix at all.
        /// </summary>
        public static long TotalReceiveBufferDrops => Interlocked.Read(ref _recvBufferDropsTotal);

        public static void Start()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return;
            if (_samplerThread != null) return;
            if (!File.Exists(SnmpPath))
            {
                BNL.LogWarning($"[UdpDropMonitor] {SnmpPath} not readable; monitoring disabled");
                return;
            }
            _running = true;
            _lastRcvbufErrors = -1;
            _lastInErrors = -1;
            _lastInCsumErrors = -1;
            _samplerThread = new Thread(Run)
            {
                Name = "UdpDropMonitor",
                IsBackground = true
            };
            _samplerThread.Start();
            BNL.Log("[UdpDropMonitor] Started; sampling /proc/net/snmp every 10s");
        }

        public static void Stop()
        {
            _running = false;
            _samplerThread = null;
        }

        private static void Run()
        {
            while (_running)
            {
                try { Sample(); }
                catch (Exception ex) { BNL.LogError($"[UdpDropMonitor] {ex.Message}"); }
                Thread.Sleep(SampleInterval);
            }
        }

        private static void Sample()
        {
            if (!TryReadSnmpUdp(out long rcvbufErrors, out long inErrors, out long inCsumErrors)) return;

            // First sample establishes the baseline; only deltas after that mean anything.
            if (_lastRcvbufErrors >= 0)
            {
                long droppedBuf = rcvbufErrors - _lastRcvbufErrors;
                long deltaIn = inErrors - _lastInErrors;
                long deltaCsum = inCsumErrors - _lastInCsumErrors;

                if (droppedBuf > 0)
                {
                    // Publish it, not just log it. This is the only direct evidence that the
                    // receive side is the bottleneck, and it is what the socket-growth controller
                    // steers on — a saturated receive thread cannot be seen in CPU numbers,
                    // because the thread is busy either way. It shows up as the kernel throwing
                    // packets away behind it.
                    Interlocked.Add(ref _recvBufferDropsTotal, droppedBuf);

                    BNL.LogWarning(
                        $"[UdpDropMonitor] Kernel dropped {droppedBuf} UDP packets in last {SampleInterval.TotalSeconds:F0}s " +
                        $"(RcvbufErrors). Receive thread is saturated -- raise MultiSocketCount in litenetlib.xml " +
                        $"or grow sysctl net.core.rmem_max.");
                }

                // InErrors - RcvbufErrors isolates non-buffer drops (checksum, length, etc.) so a
                // bad NIC/cable shows up distinctly from a saturated app.
                long otherDrops = deltaIn - droppedBuf;
                if (otherDrops > 0)
                {
                    string detail = deltaCsum > 0 ? $" (InCsumErrors +{deltaCsum})" : "";
                    BNL.LogWarning(
                        $"[UdpDropMonitor] {otherDrops} additional UDP InErrors in last {SampleInterval.TotalSeconds:F0}s{detail}" +
                        $" -- not recv-buffer related; check NIC/link health.");
                }
            }

            _lastRcvbufErrors = rcvbufErrors;
            _lastInErrors = inErrors;
            _lastInCsumErrors = inCsumErrors;
        }

        // /proc/net/snmp emits two consecutive lines starting with "Udp:" — the first names the
        // columns, the second has the values. Column set is kernel-version-dependent so we look
        // up by name rather than by fixed index.
        private static bool TryReadSnmpUdp(out long rcvbufErrors, out long inErrors, out long inCsumErrors)
        {
            rcvbufErrors = 0;
            inErrors = 0;
            inCsumErrors = 0;
            string[] lines;
            try { lines = File.ReadAllLines(SnmpPath); }
            catch { return false; }

            string headerLine = null;
            string dataLine = null;
            for (int i = 0; i < lines.Length - 1; i++)
            {
                if (lines[i].StartsWith("Udp:", StringComparison.Ordinal) &&
                    lines[i + 1].StartsWith("Udp:", StringComparison.Ordinal))
                {
                    headerLine = lines[i];
                    dataLine = lines[i + 1];
                    break;
                }
            }
            if (headerLine == null || dataLine == null) return false;

            string[] headers = headerLine.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            string[] values = dataLine.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (headers.Length != values.Length) return false;

            for (int i = 1; i < headers.Length; i++)
            {
                switch (headers[i])
                {
                    case "RcvbufErrors":
                        long.TryParse(values[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out rcvbufErrors);
                        break;
                    case "InErrors":
                        long.TryParse(values[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out inErrors);
                        break;
                    case "InCsumErrors":
                        long.TryParse(values[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out inCsumErrors);
                        break;
                }
            }
            return true;
        }
    }
}
