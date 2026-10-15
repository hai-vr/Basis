using BasisNetworkServer.Storage;
using System.Text;

namespace BasisNetworkConsole
{
    public static class BasisStorageCommands
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
        public static void Register()
        {
            BasisConsoleCommands.RegisterCommand("/storage", "Shows player storage status. /storage show <uuid> lists one player's data, /storage wipe <uuid> deletes it.", HandleStatus);
            BasisConsoleCommands.RegisterCommand("/storage show", "Lists the content names, keys and sizes one player keeps in storage, never the values. Usage: /storage show <uuid>", HandleShow);
            BasisConsoleCommands.RegisterCommand("/storage wipe", "Deletes everything one player keeps in storage. Usage: /storage wipe <uuid>", HandleWipe);
        }
        private static void HandleStatus(string[] args)
        {
            BasisStorageService service = BasisServerStorage.Service;
            if (service == null)
            {
                BNL.Log($"Player storage is off. Backends available: {string.Join(", ", BasisStorageBackends.Names)}.");
                return;
            }
            BNL.Log($"Player storage ({service.BackendName}): {service.TotalRecords} players, {service.TotalBytes} of {service.MaxTotalBytes} bytes, {service.BytesPerPlayer} bytes per player. Served {service.Served}, refused {service.Refused}, failed {service.Failures}.");
        }
        private static void HandleShow(string[] args)
        {
            if (!TryService(args, "/storage show <uuid>", out BasisStorageService service, out string uuid)) return;
            if (!service.TryInspect(uuid, Timeout, out BasisStorageRecord record))
            {
                BNL.Log($"Could not read the storage of {uuid} in time.");
                return;
            }
            if (record.IsEmpty)
            {
                BNL.Log($"{uuid} keeps nothing in storage.");
                return;
            }
            StringBuilder text = new StringBuilder();
            text.Append($"{uuid}: {record.Used} of {service.BytesPerPlayer} bytes, {record.Entries} keys in {record.Scopes} contents, revision {record.Revision}.");
            foreach (string scope in record.ScopeNames())
            {
                text.Append($"\n  {scope}");
                foreach (string key in record.KeysOf(scope)) text.Append($"\n    {key} ({record.SizeOf(scope, key)} bytes)");
            }
            BNL.Log(text.ToString());
        }
        private static void HandleWipe(string[] args)
        {
            if (!TryService(args, "/storage wipe <uuid>", out BasisStorageService service, out string uuid)) return;
            BNL.Log(service.TryWipe(uuid, Timeout) ? $"Deleted the storage of {uuid}." : $"Could not delete the storage of {uuid}; see the errors above.");
        }
        private static bool TryService(string[] args, string usage, out BasisStorageService service, out string uuid)
        {
            service = BasisServerStorage.Service;
            uuid = args.Length == 1 ? args[0].Trim() : string.Empty;
            if (uuid.Length == 0 || !BasisStorageService.IsValidOwner(uuid))
            {
                BNL.Log($"Usage: {usage}");
                return false;
            }
            if (service != null) return true;
            BNL.Log("Player storage is off.");
            return false;
        }
    }
}
