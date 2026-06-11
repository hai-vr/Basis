using Basis.Network.Core;
using K4os.Compression.LZ4;
using System;
using System.IO;
using System.Threading.Tasks;
using static BasisNetworkCore.Serializable.SerializableBasis;

namespace BasisNetworkServer.Security
{
    /// <summary>
    /// Builds a single compressed bundle of the server's logs/ and CrashReports/ folders on
    /// demand and streams it back to the requesting admin over
    /// <see cref="BasisNetworkCommons.AdminChannel"/>.
    ///
    /// The files are packed into one length-prefixed container, LZ4-compressed (the same
    /// codec already used for avatar bundles, so no new dependency), and split into ordered
    /// chunks (<see cref="ChunkSize"/>) so a large transfer never depends on one oversized
    /// datagram. The admin channel is ReliableOrdered, so the client reassembles them in
    /// send order. The whole build + send runs off the network thread, and each chunk uses a
    /// fresh <see cref="NetDataWriter"/> (no shared pool) to stay thread-safe.
    ///
    /// Container (before compression):
    ///   [int fileCount] then per file: [string relativePath][int byteLength][bytes]
    ///
    /// Wire (server→client), all on AdminChannel, ReliableOrdered:
    ///   LogBundleBegin : [string serverNameSafe][string fileName][bool isCompressed][int payloadBytes][int rawBytes][int totalChunks]
    ///   LogBundleChunk : [int chunkIndex][lenPrefixed bytes]   (repeated totalChunks times, payload stream)
    ///   LogBundleEnd   : [bool ok][string message]
    ///
    /// Gated upstream by PermNodes.AdminLogs in BasisPlayerModeration.
    /// </summary>
    public static class BasisServerLogBundleService
    {
        /// <summary>Bytes per streamed chunk. Comfortably below any single-message limit while keeping the chunk count low.</summary>
        private const int ChunkSize = 32 * 1024;

        /// <summary>Hard ceiling on the assembled (raw) container. Logs that big almost certainly mean something is wrong; refuse rather than flood the link.</summary>
        private const long MaxRawBytes = 256L * 1024 * 1024;

        public static void SendAllLogsToPeer(NetPeer peer)
        {
            if (peer == null) return;

            if (!NetworkServer.Configuration.HasFileSupport)
            {
                BasisPlayerModeration.SendBackMessage(peer, "File support is disabled on this server; there are no logs to pull.");
                return;
            }

            // Build and stream off the network thread — packing can touch many files.
            _ = Task.Run(() => BuildAndSend(peer));
        }

        public static void DeleteAllLogsForPeer(NetPeer peer)
        {
            if (peer == null) return;

            if (!NetworkServer.Configuration.HasFileSupport)
            {
                BasisPlayerModeration.SendBackMessage(peer, "File support is disabled on this server; there are no logs to delete.");
                return;
            }

            // Deletion touches many files; keep it off the network thread.
            _ = Task.Run(() => DeleteAll(peer));
        }

        private static void DeleteAll(NetPeer peer)
        {
            try
            {
                string baseDir = AppContext.BaseDirectory;
                string logsDir = Path.Combine(baseDir, Configuration.LogsFolderName);
                string crashDir = Path.Combine(baseDir, "CrashReports");

                int deleted = DeleteDirectoryFiles(logsDir) + DeleteDirectoryFiles(crashDir);

                // The error-report writer dedupes identical reports per user for this server
                // session; forget that history so fresh occurrences are recorded again.
                BasisNetworkHandleErrorReport.ClearAllSeen();

                BasisPlayerModeration.SendBackMessage(peer, $"Deleted {deleted} log/crash file(s) from logs/ and CrashReports/.");
                BNL.Log($"Admin (peer {peer.Id}) deleted {deleted} server log/crash file(s).");
            }
            catch (Exception e)
            {
                BNL.LogError($"Failed to delete logs: {e.Message}");
                try { BasisPlayerModeration.SendBackMessage(peer, "Server failed to delete the logs. See server log."); }
                catch { /* peer may be gone */ }
            }
        }

        private static int DeleteDirectoryFiles(string sourceDir)
        {
            if (string.IsNullOrEmpty(sourceDir) || !Directory.Exists(sourceDir)) return 0;

            int deleted = 0;
            foreach (string file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                try
                {
                    File.Delete(file);
                    deleted++;
                }
                catch (Exception e)
                {
                    // The current day's log file is still open for append and can't be removed while running.
                    BNL.LogWarning($"Could not delete log file '{file}' (in use?): {e.Message}");
                }
            }
            return deleted;
        }

        private static void BuildAndSend(NetPeer peer)
        {
            try
            {
                string baseDir = AppContext.BaseDirectory;
                string logsDir = Path.Combine(baseDir, Configuration.LogsFolderName);
                string crashDir = Path.Combine(baseDir, "CrashReports");

                byte[] raw = BuildContainer(logsDir, crashDir, out int fileCount);
                if (raw == null || raw.Length == 0 || fileCount == 0)
                {
                    BasisPlayerModeration.SendBackMessage(peer, "No log files were found to send.");
                    return;
                }

                if (raw.Length > MaxRawBytes)
                {
                    BasisPlayerModeration.SendBackMessage(peer,
                        $"Log bundle is too large to send ({raw.Length / (1024 * 1024)} MB, limit {MaxRawBytes / (1024 * 1024)} MB).");
                    return;
                }

                byte[] payload = Compress(raw, out bool isCompressed);

                string serverNameSafe = SanitizeName(NetworkServer.Configuration.ServerName);
                int totalChunks = (payload.Length + ChunkSize - 1) / ChunkSize;

                SendBegin(peer, serverNameSafe, "logs", isCompressed, payload.Length, raw.Length, totalChunks);

                for (int index = 0; index < totalChunks; index++)
                {
                    int offset = index * ChunkSize;
                    int length = Math.Min(ChunkSize, payload.Length - offset);
                    byte[] slice = new byte[length];
                    Buffer.BlockCopy(payload, offset, slice, 0, length);
                    SendChunk(peer, index, slice);
                }

                SendEnd(peer, true, $"Sent {fileCount} log file(s), {payload.Length / 1024} KB compressed.");
                BNL.Log($"Streamed log bundle to peer {peer.Id}: {fileCount} files, {raw.Length / 1024} KB raw / {payload.Length / 1024} KB sent.");
            }
            catch (Exception e)
            {
                BNL.LogError($"Failed to build/send log bundle: {e.Message}");
                try { SendEnd(peer, false, "Server failed to build the log bundle. See server log."); }
                catch { /* peer may be gone */ }
            }
        }

        private static byte[] BuildContainer(string logsDir, string crashDir, out int fileCount)
        {
            using MemoryStream memory = new MemoryStream();
            using (BinaryWriter writer = new BinaryWriter(memory, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                // Reserve the count slot; backfill once we know how many files were readable.
                long countPos = memory.Position;
                writer.Write(0);

                int count = 0;
                count += AddDirectory(writer, logsDir, "logs");
                count += AddDirectory(writer, crashDir, "CrashReports");

                writer.Flush();
                long endPos = memory.Position;
                memory.Position = countPos;
                writer.Write(count);
                writer.Flush();
                memory.Position = endPos;

                fileCount = count;
                if (count == 0) return null;
            }
            return memory.ToArray();
        }

        private static int AddDirectory(BinaryWriter writer, string sourceDir, string entryPrefix)
        {
            if (string.IsNullOrEmpty(sourceDir) || !Directory.Exists(sourceDir)) return 0;

            int added = 0;
            foreach (string file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                string relative = GetRelativePath(sourceDir, file).Replace('\\', '/');
                string entryName = entryPrefix + "/" + relative;
                try
                {
                    byte[] bytes = ReadAllBytesShared(file);
                    writer.Write(entryName);
                    writer.Write(bytes.Length);
                    writer.Write(bytes);
                    added++;
                }
                catch (Exception e)
                {
                    BNL.LogWarning($"Skipped log file '{entryName}' while bundling: {e.Message}");
                }
            }
            return added;
        }

        // FileShare.ReadWrite so the current day's log file (still open for append) can be read.
        private static byte[] ReadAllBytesShared(string path)
        {
            using FileStream input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using MemoryStream buffer = new MemoryStream();
            input.CopyTo(buffer);
            return buffer.ToArray();
        }

        private static byte[] Compress(byte[] raw, out bool isCompressed)
        {
            try
            {
                byte[] target = new byte[LZ4Codec.MaximumOutputSize(raw.Length)];
                int encoded = LZ4Codec.Encode(raw, 0, raw.Length, target, 0, target.Length, LZ4Level.L00_FAST);
                if (encoded > 0 && encoded < raw.Length)
                {
                    byte[] result = new byte[encoded];
                    Buffer.BlockCopy(target, 0, result, 0, encoded);
                    isCompressed = true;
                    return result;
                }
            }
            catch (Exception e)
            {
                BNL.LogWarning($"Log bundle compression failed, sending raw: {e.Message}");
            }

            isCompressed = false;
            return raw;
        }

        // Compute the path relative to baseDir without relying on Path.GetRelativePath (older TFMs).
        private static string GetRelativePath(string baseDir, string fullPath)
        {
            string normalizedBase = Path.GetFullPath(baseDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string normalizedFull = Path.GetFullPath(fullPath);
            if (normalizedFull.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
            {
                return normalizedFull.Substring(normalizedBase.Length);
            }
            return Path.GetFileName(fullPath);
        }

        private static void SendBegin(NetPeer peer, string serverNameSafe, string fileName, bool isCompressed, int payloadBytes, int rawBytes, int totalChunks)
        {
            NetDataWriter writer = new NetDataWriter();
            new AdminRequest().Serialize(writer, AdminRequestMode.LogBundleBegin);
            writer.Put(serverNameSafe);
            writer.Put(fileName);
            writer.Put(isCompressed);
            writer.Put(payloadBytes);
            writer.Put(rawBytes);
            writer.Put(totalChunks);
            NetworkServer.TrySend(peer, writer, BasisNetworkCommons.AdminChannel, DeliveryMethod.ReliableOrdered);
        }

        private static void SendChunk(NetPeer peer, int chunkIndex, byte[] data)
        {
            NetDataWriter writer = new NetDataWriter();
            new AdminRequest().Serialize(writer, AdminRequestMode.LogBundleChunk);
            writer.Put(chunkIndex);
            writer.PutBytesWithLength(data);
            NetworkServer.TrySend(peer, writer, BasisNetworkCommons.AdminChannel, DeliveryMethod.ReliableOrdered);
        }

        private static void SendEnd(NetPeer peer, bool ok, string message)
        {
            NetDataWriter writer = new NetDataWriter();
            new AdminRequest().Serialize(writer, AdminRequestMode.LogBundleEnd);
            writer.Put(ok);
            writer.Put(message ?? string.Empty);
            NetworkServer.TrySend(peer, writer, BasisNetworkCommons.AdminChannel, DeliveryMethod.ReliableOrdered);
        }

        private static string SanitizeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "server";
            foreach (char c in Path.GetInvalidFileNameChars())
                value = value.Replace(c, '_');
            value = value.Trim().Replace(' ', '_');
            return string.IsNullOrEmpty(value) ? "server" : value;
        }
    }
}
