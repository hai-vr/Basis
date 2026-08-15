using System;
using System.IO;

namespace Basis.BasisUI
{
    /// <summary>
    /// Reveals a file or folder in the host OS file browser, defensively.
    ///
    /// This launches an OS process / URL handler, and some callers pass paths that embed
    /// server-influenced text (e.g. a pulled-log folder named after the remote server). To stop
    /// that becoming an argument- or URL-injection vector this:
    ///   • canonicalises the path and requires it to be a real, existing local file/folder, so a
    ///     hostile string (a process-flag or URL-scheme payload) can never reach a launcher;
    ///   • refuses any path containing a double-quote or control character — a legitimate reveal
    ///     target never has one, and a double-quote is the only thing that could break out of the
    ///     quoted argument we pass;
    ///   • never routes through a shell, invokes the absolute explorer.exe on Windows and the
    ///     absolute /usr/bin/open on macOS, and on the fallback emits only a file:// URI (never an
    ///     arbitrary scheme);
    ///   • swallows and logs every failure, so a UI callback can never be broken by it.
    /// No-op for an empty / invalid / non-existent path.
    ///
    /// Returns whether a launcher was actually reached, so a caller with a broader target (a file
    /// and the folder holding it) can fall back rather than leave the user with nothing.
    /// </summary>
    public static class BasisFileBrowserUtility
    {
        public static bool Reveal(string path, bool selectFile = false)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return false;

                // Resolve to an absolute path and require it to exist locally. Only a real on-disk
                // target ever reaches a launcher below.
                string fullPath = Path.GetFullPath(path);

                bool isFile = File.Exists(fullPath);
                bool isDirectory = Directory.Exists(fullPath);
                if (!isFile && !isDirectory) return false;

                // A legitimate reveal target carries no double-quote or control character; if one
                // does (a crafted name), refuse rather than risk breaking out of the quoted argument.
                if (HasUnsafeCharacters(fullPath)) return false;

                // Only a real file can be highlighted; otherwise reveal the directory itself.
                if (selectFile && !isFile) selectFile = false;
                string directory = isDirectory ? fullPath : (Path.GetDirectoryName(fullPath) ?? fullPath);

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                // Pin to the absolute system explorer so a CWD-planted "explorer.exe" cannot run
                // instead. explorer expects exactly "/select,<file>" to highlight one, and the bare
                // path to open a folder; both targets are verified to exist and Windows paths cannot
                // contain a double-quote, so neither can break out of the quoted argument.
                string explorer = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = explorer,
                    Arguments = selectFile
                        ? $"/select,\"{fullPath.Replace('/', '\\')}\""
                        : $"\"{directory.Replace('/', '\\')}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
                // Absolute path (no PATH lookup to hijack) and no shell (UseShellExecute = false); the
                // quoted target has no quote/control chars, so it cannot inject extra `open` flags
                // (e.g. -a to launch an arbitrary application).
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "/usr/bin/open",
                    Arguments = selectFile ? $"-R \"{fullPath}\"" : $"\"{directory}\"",
                    UseShellExecute = false
                });
#else
                // Build a real file:// URI from the verified directory so the URL handler can never be
                // steered to a remote or script scheme. (This platform cannot highlight a file.)
                UnityEngine.Application.OpenURL(new Uri(directory).AbsoluteUri);
#endif
                return true;
            }
            catch (Exception e)
            {
                // Best-effort convenience: a failure here must never propagate into the caller.
                BasisDebug.LogWarning($"Could not reveal path in file browser: {e.Message}");
                return false;
            }
        }

        // Rejects the double-quote (the only character that can break out of the quoted argument we
        // pass to a launcher) plus control characters (which a real path never contains and which can
        // corrupt argument/log parsing). Spaces, single quotes and unicode are intentionally allowed.
        private static bool HasUnsafeCharacters(string value)
        {
            foreach (char c in value)
            {
                if (c == '"' || c < ' ' || c == (char)0x7F) return true;
            }
            return false;
        }
    }
}
