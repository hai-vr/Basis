using System.Globalization;
using System.Net.Sockets;
using System.Text;

namespace BasisNetworkConsole
{
    public static class BasisSystemd
    {
        private static string? notifyAddress;
        private static long watchdogMicroseconds;
        private static Timer? heartbeat;
        private static Func<string>? statusSource;
        private static string? lastStatus;
        private static bool warned;

        public static bool IsManaged { get; private set; }

        public static bool CanNotify => notifyAddress != null;

        public static bool WatchdogEnabled => watchdogMicroseconds > 0;

        public static void Initialize()
        {
            notifyAddress = NotifyAddress(Environment.GetEnvironmentVariable("NOTIFY_SOCKET"));
            watchdogMicroseconds = WatchdogInterval(Environment.GetEnvironmentVariable("WATCHDOG_USEC"), Environment.GetEnvironmentVariable("WATCHDOG_PID"), Environment.ProcessId);
            Environment.SetEnvironmentVariable("NOTIFY_SOCKET", null);
            Environment.SetEnvironmentVariable("WATCHDOG_USEC", null);
            Environment.SetEnvironmentVariable("WATCHDOG_PID", null);
            IsManaged = OperatingSystem.IsLinux() && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("INVOCATION_ID")) && ParentIsSystemd();
        }

        public static string? NotifyAddress(string? value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            if (value[0] == '@') return "\0" + value.Substring(1);
            return value[0] == '/' ? value : null;
        }

        public static long WatchdogInterval(string? microseconds, string? owner, int processId)
        {
            if (!long.TryParse(microseconds, NumberStyles.None, CultureInfo.InvariantCulture, out long value) || value <= 0) return 0;
            if (string.IsNullOrEmpty(owner)) return value;
            return int.TryParse(owner, NumberStyles.None, CultureInfo.InvariantCulture, out int pid) && pid == processId ? value : 0;
        }

        public static int ParentProcessId(string stat)
        {
            int close = stat.LastIndexOf(')');
            if (close < 0) return -1;

            string[] fields = stat.Substring(close + 1).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return fields.Length > 1 && int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out int parent) ? parent : -1;
        }

        public static bool Notify(string state)
        {
            string? address = notifyAddress;
            if (address == null) return false;

            try
            {
                using Socket socket = new Socket(AddressFamily.Unix, SocketType.Dgram, ProtocolType.Unspecified);
                socket.Connect(new UnixDomainSocketEndPoint(address));
                socket.Send(Encoding.UTF8.GetBytes(state));
                return true;
            }
            catch (Exception e)
            {
                if (!warned)
                {
                    warned = true;
                    BNL.LogWarning($"[systemd] Could not reach the service manager's notify socket: {e.Message}");
                }
                return false;
            }
        }

        public static void Ready(string status)
        {
            lastStatus = status;
            Notify("READY=1\nSTATUS=" + OneLine(status));
        }

        public static void Stopping(string status)
        {
            lastStatus = status;
            Notify("STOPPING=1\nSTATUS=" + OneLine(status));
        }

        public static void Status(string status)
        {
            lastStatus = status;
            Notify("STATUS=" + OneLine(status));
        }

        public static void ExtendStartup(TimeSpan by)
        {
            Notify("EXTEND_TIMEOUT_USEC=" + ((long)by.TotalMilliseconds * 1000).ToString(CultureInfo.InvariantCulture));
        }

        public static void StartHeartbeat(Func<string> status)
        {
            if (notifyAddress == null || heartbeat != null) return;

            statusSource = status;
            TimeSpan period = TimeSpan.FromSeconds(10);
            if (watchdogMicroseconds > 0)
            {
                TimeSpan half = TimeSpan.FromTicks(watchdogMicroseconds * 5);
                if (half < period) period = half;
            }
            heartbeat = new Timer(_ => Beat(), null, TimeSpan.Zero, period);
        }

        public static void StopHeartbeat()
        {
            heartbeat?.Dispose();
            heartbeat = null;
        }

        private static void Beat()
        {
            string? status = null;
            try
            {
                status = statusSource?.Invoke();
            }
            catch
            {
            }

            StringBuilder message = new StringBuilder();
            if (watchdogMicroseconds > 0) message.Append("WATCHDOG=1\n");
            if (status != null && status != lastStatus)
            {
                lastStatus = status;
                message.Append("STATUS=").Append(OneLine(status)).Append('\n');
            }
            if (message.Length != 0) Notify(message.ToString());
        }

        private static bool ParentIsSystemd()
        {
            try
            {
                int parent = ParentProcessId(File.ReadAllText("/proc/self/stat"));
                return parent > 0 && File.ReadAllText($"/proc/{parent}/comm").Trim() == "systemd";
            }
            catch
            {
                return false;
            }
        }

        private static string OneLine(string text) => text.Replace('\r', ' ').Replace('\n', ' ');
    }
}
