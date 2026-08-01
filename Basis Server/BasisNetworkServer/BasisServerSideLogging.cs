using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Basis.Network
{
    public static class BasisServerSideLogging
    {
        private static string LogDirectory;
        private static string CurrentLogFileName => Path.Combine(LogDirectory, $"{DateTime.UtcNow:yyyy-MM-dd}.log");

        private static CancellationTokenSource _cancellationTokenSource;
        private static Task _loggingTask;
        private static readonly BlockingCollection<string> LogQueue = new(new ConcurrentQueue<string>(), 200);
        private static readonly SemaphoreSlim FileWriteSemaphore = new(1, 1);
        private static readonly object ScreenLock = new();

        static BasisServerSideLogging()
        {
        }
        public static bool UseLogging;
        public static bool WriteToScreen = true;
        /// <summary>
        /// Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs")
        /// </summary>
        /// <param name="config"></param>
        /// <param name="PathOutput"></param>
        public static void Initialize(Configuration config, string logDirectory)
        {
            UseLogging = config.HasFileSupport;
            LogDirectory = logDirectory;
            BNL.LogOutput += Log;
            BNL.LogWarningOutput += LogWarning;
            BNL.LogErrorOutput += LogError;

            if (UseLogging)
            {
                // Ensure the logs directory exists
                if (!Directory.Exists(LogDirectory))
                {
                    Directory.CreateDirectory(LogDirectory);
                }
                Log("Logs are saved to " + CurrentLogFileName);
                StartLoggingTask();
            }
            else
            {
                Log("no logs will be saved");
            }
        }
        private static void StartLoggingTask()
        {
            _cancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = _cancellationTokenSource.Token;

            _loggingTask = Task.Run(async () =>
            {
                try
                {
                    while (!cancellationToken.IsCancellationRequested || !LogQueue.IsCompleted)
                    {
                        if (LogQueue.TryTake(out var logEntry, 50))
                        {
                            await WriteToFileAsync(logEntry, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Task canceled, exit gracefully
                }
            }, cancellationToken);
        }

        private static async Task WriteToFileAsync(string logEntry, CancellationToken cancellationToken)
        {
            try
            {
                await FileWriteSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

                using (var stream = new FileStream(CurrentLogFileName, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, true))
                {
                    var logData = Encoding.UTF8.GetBytes(logEntry + Environment.NewLine);
                    await stream.WriteAsync(logData, 0, logData.Length, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                FileWriteSemaphore.Release();
            }
        }

        public static async Task ShutdownAsync()
        {
            _cancellationTokenSource?.Cancel();
            LogQueue?.CompleteAdding();

            try
            {
                await _loggingTask.ConfigureAwait(false);
            }
            catch (AggregateException)
            {
                // Suppress exceptions caused by cancellation
            }
            finally
            {
                _cancellationTokenSource?.Dispose();
            }
        }
        private static string FormatMessage(string level, string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm");
            return $"[{timestamp}] [{level}] {message}";
        }

        private static string Sanitize(string message)
        {
            if (string.IsNullOrEmpty(message)) return message;
            StringBuilder sb = new StringBuilder(message.Length);
            foreach (char c in message)
            {
                if (c == '\n' || c == '\r') sb.Append(' ');
                else if (c < 0x20 && c != '\t') sb.Append('?');
                else sb.Append(c);
            }
            return sb.ToString();
        }

        private static void Log(string message)
        {
            if (WriteToScreen || UseLogging)
            {
                message = Sanitize(message);
                string formattedMessage = FormatMessage("INFO", message);
                WriteScreenLine("[INFO] ", ConsoleColor.DarkMagenta, message);

                if (UseLogging)
                {
                    if (!LogQueue.TryAdd(formattedMessage))
                    {
                        LogQueue.TryTake(out _); // Drop oldest log if the queue is full
                        LogQueue.TryAdd(formattedMessage); // Retry adding the new message
                    }
                }
            }
        }
        public static void LogWarning(string message)
        {
            if (WriteToScreen || UseLogging)
            {
                message = Sanitize(message);
                string formattedMessage = FormatMessage("WARNING", message);
                WriteScreenLine("[WARNING] ", ConsoleColor.DarkYellow, message);

                if (UseLogging)
                {
                    if (!LogQueue.TryAdd(formattedMessage))
                    {
                        LogQueue.TryTake(out _); // Drop oldest log if the queue is full
                        LogQueue.TryAdd(formattedMessage); // Retry adding the new message
                    }
                }
            }
        }

        public static void LogError(string message)
        {
            if (WriteToScreen || UseLogging)
            {
                message = Sanitize(message);
                string formattedMessage = FormatMessage("ERROR", message);
                WriteScreenLine("[ERROR] ", ConsoleColor.DarkRed, message);


                if (UseLogging)
                {
                    if (!LogQueue.TryAdd(formattedMessage))
                    {
                        LogQueue.TryTake(out _); // Drop oldest log if the queue is full
                        LogQueue.TryAdd(formattedMessage); // Retry adding the new message
                    }
                }
            }
        }

        /// <summary>
        /// Writes one whole log line. The parts have to land together: they share the console's
        /// colour state, so two threads interleaving here mix up both the colours and the text.
        /// </summary>
        private static void WriteScreenLine(string level, ConsoleColor levelColor, string message)
        {
            lock (ScreenLock)
            {
                WriteColoredMessage($"[{DateTime.Now:HH:mm}] ", ConsoleColor.DarkCyan);
                WriteColoredMessage(level, levelColor);
                WriteColoredMessage($"{message}\n", ConsoleColor.Gray);
            }
        }

        private static void WriteColoredMessage(string message, ConsoleColor color)
        {
            var originalColor = Console.ForegroundColor; // Save the original color
            Console.ForegroundColor = color; // Set the desired color
            Console.Write(message); // Write the message (without a new line)
            Console.ForegroundColor = originalColor; // Restore the original color
        }
    }
}
