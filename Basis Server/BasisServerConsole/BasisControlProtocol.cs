using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace BasisNetworkConsole
{
    public enum BasisLogLevel : byte
    {
        Info,
        Warning,
        Error,
    }

    public static class BasisControlProtocol
    {
        public const int Version = 1;
        public const string Greeting = "basis";
        public const string RunVerb = "run";
        public const string AttachVerb = "attach";
        public const string DoneVerb = "done";
        public const string SocketFileName = "basis.sock";
        public const string SocketVariable = "BASIS_CONTROL_SOCKET";
        public const string SystemdSocketPath = "/run/basis-server/basis.sock";
        public const int CommandRan = 0;
        public const int CommandFailed = 1;
        public const int CommandUnknown = 2;
        private const int MaxSocketPathBytes = 100;

        public static char Tag(BasisLogLevel level)
        {
            switch (level)
            {
                case BasisLogLevel.Warning: return 'W';
                case BasisLogLevel.Error: return 'E';
                default: return 'I';
            }
        }

        public static bool TryParseTag(char tag, out BasisLogLevel level)
        {
            switch (tag)
            {
                case 'I': level = BasisLogLevel.Info; return true;
                case 'W': level = BasisLogLevel.Warning; return true;
                case 'E': level = BasisLogLevel.Error; return true;
                default: level = BasisLogLevel.Info; return false;
            }
        }

        public static string Line(BasisLogLevel level, string text) => Tag(level) + " " + text;

        public static string Done(int code) => DoneVerb + " " + code.ToString(CultureInfo.InvariantCulture);

        public static bool IsDisabled(string? value)
        {
            switch (value?.Trim().ToLowerInvariant())
            {
                case "off":
                case "0":
                case "false":
                case "none":
                    return true;
                default:
                    return false;
            }
        }

        public static string? ServerSocketPath(string baseDirectory)
        {
            string? configured = Environment.GetEnvironmentVariable(SocketVariable);
            if (!string.IsNullOrWhiteSpace(configured)) return IsDisabled(configured) ? null : configured.Trim();

            string? runtimeDirectory = Environment.GetEnvironmentVariable("RUNTIME_DIRECTORY");
            if (!string.IsNullOrWhiteSpace(runtimeDirectory)) return Fit(Path.Combine(runtimeDirectory.Split(':')[0], SocketFileName));

            return Fit(Path.Combine(baseDirectory, SocketFileName));
        }

        public static List<string> ClientSocketPaths(string baseDirectory)
        {
            List<string> paths = new List<string>();
            string? configured = Environment.GetEnvironmentVariable(SocketVariable);
            if (!string.IsNullOrWhiteSpace(configured) && !IsDisabled(configured)) paths.Add(configured.Trim());
            paths.Add(Fit(Path.Combine(baseDirectory, SocketFileName)));
            if (!OperatingSystem.IsWindows()) paths.Add(SystemdSocketPath);
            return paths;
        }

        public static string Fit(string path)
        {
            if (Encoding.UTF8.GetByteCount(path) < MaxSocketPathBytes) return path;

            string key = OperatingSystem.IsWindows() ? path.ToLowerInvariant() : path;
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)), 0, 6).ToLowerInvariant();
            return Path.Combine(Path.GetTempPath(), "basis-" + hash + ".sock");
        }

        public static bool NeedsCleaning(string text)
        {
            foreach (char c in text)
            {
                if (IsControl(c)) return true;
            }
            return false;
        }

        public static string[] CleanLines(string text)
        {
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (!NeedsCleaning(lines[i])) continue;

                StringBuilder builder = new StringBuilder(lines[i].Length);
                foreach (char c in lines[i]) builder.Append(IsControl(c) ? '?' : c);
                lines[i] = builder.ToString();
            }
            return lines;
        }

        private static bool IsControl(char c) => (c < 0x20 && c != '\t') || (c >= 0x7F && c <= 0x9F);
    }
}
