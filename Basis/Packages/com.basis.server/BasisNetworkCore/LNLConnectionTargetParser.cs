using System.Globalization;

namespace Basis.Network.Core
{
    public sealed class LNLConnectionTargetParser : IConnectionTargetParser
    {
        public const ushort DefaultPort = 4296;

        public void Parse(ConnectionTarget target)
        {
            if (target == null) return;
            if (TryParseConnectionString(target.Raw, out string address, out ushort port, out _, out string password))
            {
                target.Set(ConnectionTarget.Keys.Address, address);
                target.Set(ConnectionTarget.Keys.Port, port.ToString(CultureInfo.InvariantCulture));
                target.Set(ConnectionTarget.Keys.Password, password ?? string.Empty);
            }
        }

        public string Format(ConnectionTarget target)
        {
            if (target == null) return string.Empty;
            string address = target.Get(ConnectionTarget.Keys.Address, string.Empty);
            string portString = target.Get(ConnectionTarget.Keys.Port, DefaultPort.ToString(CultureInfo.InvariantCulture));
            string password = target.Get(ConnectionTarget.Keys.Password, string.Empty);

            string s = $"{address}:{portString}";
            if (!string.IsNullOrEmpty(password)) s += "#" + password;
            return s;
        }

        public static bool TryParseConnectionString(
            string raw, out string address, out ushort port, out bool portProvided, out string password)
        {
            address = string.Empty;
            port = DefaultPort;
            portProvided = false;
            password = string.Empty;
            if (string.IsNullOrEmpty(raw)) return false;

            string left = raw;
            int hashIdx = raw.IndexOf('#');
            if (hashIdx >= 0)
            {
                password = raw.Substring(hashIdx + 1);
                left = raw.Substring(0, hashIdx);
            }

            int colonIdx = left.LastIndexOf(':');
            if (colonIdx > 0
                && colonIdx < left.Length - 1
                && ushort.TryParse(left.Substring(colonIdx + 1), out ushort parsedPort)
                && parsedPort > 0)
            {
                address = left.Substring(0, colonIdx).Trim();
                port = parsedPort;
                portProvided = true;
            }
            else
            {
                address = left.Trim();
            }

            if (address.StartsWith("[") && address.EndsWith("]") && address.Length >= 2)
                address = address.Substring(1, address.Length - 2);

            return !string.IsNullOrEmpty(address);
        }
    }
}
