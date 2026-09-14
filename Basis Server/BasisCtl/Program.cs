using BasisNetworkConsole;
using System.Globalization;
using System.Net.Sockets;
using System.Text;

namespace Basis.Ctl
{
    public static class Program
    {
        private const int ExitCannotConnect = 3;
        private const int ExitAccessDenied = 4;
        private const int ExitUsage = 64;
        private static readonly object PrintGate = new object();

        public static int Main(string[] args)
        {
            string? socketPath = null;
            int index = 0;
            for (; index < args.Length; index++)
            {
                string argument = args[index];
                if (argument == "--")
                {
                    index++;
                    break;
                }
                if (!argument.StartsWith('-')) break;

                if (argument == "-h" || argument == "--help")
                {
                    PrintUsage();
                    return 0;
                }
                if (argument == "-s" || argument == "--socket")
                {
                    if (++index >= args.Length) return UsageError("--socket needs a path.");
                    socketPath = args[index];
                }
                else if (argument.StartsWith("--socket=", StringComparison.Ordinal))
                {
                    socketPath = argument.Substring("--socket=".Length);
                }
                else
                {
                    return UsageError($"Unknown option {argument}.");
                }
            }

            string command = string.Join(' ', args.Skip(index)).Trim();
            bool attach = command.Length == 0 || command == "attach";

            Socket? socket = Connect(socketPath, out int failure);
            if (socket == null) return failure;

            using NetworkStream stream = new NetworkStream(socket, true);
            using StreamReader reader = new StreamReader(stream, new UTF8Encoding(false));
            using StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\n", AutoFlush = true };

            string? greeting;
            try
            {
                greeting = reader.ReadLine();
            }
            catch (IOException)
            {
                greeting = null;
            }
            if (greeting == null || !greeting.StartsWith(BasisControlProtocol.Greeting + " ", StringComparison.Ordinal))
            {
                WriteError(greeting == null ? "The server closed the connection." : StripTag(greeting));
                return ExitCannotConnect;
            }

            return attach ? Attach(reader, writer) : Run(reader, writer, command);
        }

        private static Socket? Connect(string? explicitPath, out int failure)
        {
            List<string> candidates = explicitPath != null ? new List<string> { explicitPath } : BasisControlProtocol.ClientSocketPaths(AppContext.BaseDirectory).Distinct().ToList();
            bool denied = false;
            foreach (string path in candidates)
            {
                Socket socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                try
                {
                    socket.Connect(new UnixDomainSocketEndPoint(path));
                    failure = 0;
                    return socket;
                }
                catch (SocketException e)
                {
                    if (e.SocketErrorCode == SocketError.AccessDenied) denied = true;
                }
                catch (UnauthorizedAccessException)
                {
                    denied = true;
                }
                catch (Exception)
                {
                }
                socket.Dispose();
            }

            if (denied)
            {
                WriteError("Permission denied on the server's control socket. Run basisctl as the server's user, or add yourself to the server's group.");
                failure = ExitAccessDenied;
                return null;
            }

            WriteError($"No Basis server is answering on {string.Join(" or ", candidates)}. Is it running? Use --socket or {BasisControlProtocol.SocketVariable} if it listens somewhere else.");
            failure = ExitCannotConnect;
            return null;
        }

        private static int Run(StreamReader reader, StreamWriter writer, string command)
        {
            try
            {
                writer.WriteLine(BasisControlProtocol.RunVerb + " " + Normalize(command));
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (TryParseDone(line, out int code)) return code;
                    Print(line, false);
                }
            }
            catch (IOException)
            {
            }

            WriteError("The connection closed before the command finished.");
            return ExitCannotConnect;
        }

        private static int Attach(StreamReader reader, StreamWriter writer)
        {
            bool interactive = !Console.IsInputRedirected && !Console.IsOutputRedirected;
            ManualResetEventSlim closed = new ManualResetEventSlim(false);
            SemaphoreSlim finished = new SemaphoreSlim(0);

            Thread pump = new Thread(() =>
            {
                try
                {
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (TryParseDone(line, out _)) finished.Release();
                        else Print(line, true);
                    }
                }
                catch
                {
                }

                closed.Set();
                if (interactive)
                {
                    Print(BasisControlProtocol.Line(BasisLogLevel.Warning, "The server closed the connection."), true);
                    Environment.Exit(ExitCannotConnect);
                }
            })
            { IsBackground = true, Name = "basisctl pump" };

            try
            {
                writer.WriteLine(BasisControlProtocol.AttachVerb);
            }
            catch (IOException)
            {
                WriteError("The server closed the connection.");
                return ExitCannotConnect;
            }

            if (interactive)
            {
                BasisConsoleDriver.Initialize();
                Console.WriteLine("Attached to the Basis server console. Type /help for commands, exit or Ctrl+C to detach.");
            }
            pump.Start();

            while (!closed.IsSet)
            {
                string? input = interactive ? BasisConsoleDriver.ReadLine() : Console.ReadLine();
                if (input == null) break;

                input = input.Trim();
                if (input.Length == 0) continue;
                if (input == "exit" || input == "quit" || input == "detach") break;
                if (input == "clear" || input == "/clear")
                {
                    if (interactive) BasisConsoleDriver.Clear();
                    continue;
                }

                try
                {
                    writer.WriteLine(BasisControlProtocol.RunVerb + " " + Normalize(input));
                }
                catch (IOException)
                {
                    break;
                }

                if (interactive) continue;
                while (!finished.Wait(100))
                {
                    if (closed.IsSet) break;
                }
            }

            if (closed.IsSet)
            {
                WriteError("The server closed the connection.");
                return ExitCannotConnect;
            }
            return 0;
        }

        private static void Print(string line, bool attached)
        {
            BasisLogLevel level = BasisLogLevel.Info;
            string text = line;
            if (line.Length >= 2 && line[1] == ' ' && BasisControlProtocol.TryParseTag(line[0], out BasisLogLevel parsed))
            {
                level = parsed;
                text = line.Substring(2);
            }

            bool toError = level == BasisLogLevel.Error && !attached;
            TextWriter target = toError ? Console.Error : Console.Out;
            bool colour = level != BasisLogLevel.Info && !(toError ? Console.IsErrorRedirected : Console.IsOutputRedirected);
            lock (PrintGate)
            {
                if (!colour)
                {
                    target.WriteLine(text);
                    return;
                }

                ConsoleColor original = Console.ForegroundColor;
                Console.ForegroundColor = level == BasisLogLevel.Error ? ConsoleColor.Red : ConsoleColor.Yellow;
                target.WriteLine(text);
                Console.ForegroundColor = original;
            }
        }

        private static bool TryParseDone(string line, out int code)
        {
            code = 0;
            return line.StartsWith(BasisControlProtocol.DoneVerb + " ", StringComparison.Ordinal) && int.TryParse(line.AsSpan(BasisControlProtocol.DoneVerb.Length + 1), NumberStyles.None, CultureInfo.InvariantCulture, out code);
        }

        private static string Normalize(string command) => command.StartsWith('/') ? command : "/" + command;

        private static string StripTag(string line) => line.Length >= 2 && line[1] == ' ' && BasisControlProtocol.TryParseTag(line[0], out _) ? line.Substring(2) : line;

        private static void WriteError(string message)
        {
            Print(BasisControlProtocol.Line(BasisLogLevel.Error, "basisctl: " + message), false);
        }

        private static int UsageError(string message)
        {
            WriteError(message + " Run basisctl --help for usage.");
            return ExitUsage;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("basisctl controls a running Basis server through its local control socket.");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  basisctl [--socket <path>]                attach to the server console");
            Console.WriteLine("  basisctl [--socket <path>] <command>      run one console command and exit");
            Console.WriteLine();
            Console.WriteLine("Attached, you see the live server log and can type any console command.");
            Console.WriteLine("Type exit or press Ctrl+C to detach. The server keeps running.");
            Console.WriteLine();
            Console.WriteLine("Commands are the server's console commands. The leading / is optional:");
            Console.WriteLine("  basisctl status");
            Console.WriteLine("  basisctl players");
            Console.WriteLine("  basisctl perm user group add did:key:z6Mk... admin");
            Console.WriteLine("  basisctl config PeerLimit 500");
            Console.WriteLine("  basisctl restart");
            Console.WriteLine("  basisctl shutdown");
            Console.WriteLine("  basisctl help                             lists every server command");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  -s, --socket <path>   control socket to use (or set " + BasisControlProtocol.SocketVariable + ")");
            Console.WriteLine("                        default: basis.sock beside basisctl, then " + BasisControlProtocol.SystemdSocketPath);
            Console.WriteLine("  -h, --help            show this help");
            Console.WriteLine();
            Console.WriteLine("Exit codes: 0 ran, 1 command failed, 2 unknown command, 3 server not reachable,");
            Console.WriteLine("4 permission denied, 64 bad usage.");
        }
    }
}
