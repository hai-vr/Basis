using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;

namespace BasisNetworkServer.Security
{
    public class BasisBanList
    {
        private readonly ConcurrentDictionary<string, byte> bannedPlayers = new();
        private readonly string filePath;

        public BasisBanList(string path = "BasisBanList.txt")
        {
            filePath = path;
            LoadBanList();
        }

        private void LoadBanList()
        {
            bannedPlayers.Clear();
            if (File.Exists(filePath))
            {
                string[] lines = File.ReadAllLines(filePath);
                foreach (string line in lines)
                {
                    string trimmedLine = line.Trim();
                    if (!string.IsNullOrEmpty(trimmedLine))
                    {
                        bannedPlayers.TryAdd(trimmedLine, 0);
                    }
                }
            }
        }

        private async Task LoadBanListAsync()
        {
            bannedPlayers.Clear();
            if (File.Exists(filePath))
            {
                string[] lines = await File.ReadAllLinesAsync(filePath);
                foreach (string line in lines)
                {
                    string trimmedLine = line.Trim();
                    if (!string.IsNullOrEmpty(trimmedLine))
                    {
                        bannedPlayers.TryAdd(trimmedLine, 0);
                    }
                }
            }
        }

        public bool IsBanned(string playerId) => bannedPlayers.ContainsKey(playerId);

        public async Task ReloadBanListAsync()
        {
            await LoadBanListAsync();
            Console.WriteLine("Ban list reloaded.");
        }

        public async Task AddToBanListAsync(string playerId)
        {
            if (!bannedPlayers.ContainsKey(playerId))
            {
                bannedPlayers.TryAdd(playerId, 0);
                await File.AppendAllTextAsync(filePath, playerId + Environment.NewLine);
                Console.WriteLine($"{playerId} added to ban list.");
            }
        }

        public async Task RemoveFromBanListAsync(string playerId)
        {
            if (bannedPlayers.TryRemove(playerId, out _))
            {
                await SaveBanListAsync();
                Console.WriteLine($"{playerId} removed from ban list.");
            }
        }

        private async Task SaveBanListAsync()
        {
            await File.WriteAllLinesAsync(filePath, bannedPlayers.Keys);
        }
    }
}
