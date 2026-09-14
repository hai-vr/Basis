using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;

namespace BasisNetworkConsole
{
    public sealed class BasisControlServer : IDisposable
    {
        private const int BacklogLines = 200;
        private const int MaxSessions = 16;
        private const int SessionQueueLines = 10000;

        [ThreadStatic] private static Session? capturing;

        private readonly Func<string, int> execute;
        private readonly Socket listener;
        private readonly object gate = new object();
        private readonly List<Session> sessions = new List<Session>();
        private readonly (BasisLogLevel Level, string Text)[] backlog = new (BasisLogLevel, string)[BacklogLines];
        private readonly DateTime boundWriteTime;
        private int backlogStart;
        private int backlogCount;
        private Session[] attached = Array.Empty<Session>();
        private bool disposed;

        public string SocketPath { get; }

        private BasisControlServer(string path, Socket listener, Func<string, int> execute)
        {
            SocketPath = path;
            this.listener = listener;
            this.execute = execute;
            boundWriteTime = WriteTime(path);
            new Thread(AcceptLoop) { IsBackground = true, Name = "basisctl accept" }.Start();
        }

        public static BasisControlServer? Start(string path, Func<string, int> execute)
        {
            try
            {
                string? directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                if (File.Exists(path))
                {
                    if (IsAnswering(path))
                    {
                        BNL.LogWarning($"[basisctl] Another server is already answering on {path}, so this one is not taking the control socket.");
                        return null;
                    }
                    File.Delete(path);
                }

                Socket socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                try
                {
                    socket.Bind(new UnixDomainSocketEndPoint(path));
                    if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.GroupWrite);
                    socket.Listen(MaxSessions);
                }
                catch
                {
                    socket.Dispose();
                    try
                    {
                        File.Delete(path);
                    }
                    catch
                    {
                    }
                    throw;
                }

                BNL.Log($"[basisctl] Control socket listening at {path}");
                return new BasisControlServer(path, socket, execute);
            }
            catch (Exception e)
            {
                BNL.LogWarning($"[basisctl] Control socket unavailable at {path}: {e.Message}");
                return null;
            }
        }

        public void Publish(BasisLogLevel level, string message)
        {
            message ??= string.Empty;
            if (!BasisControlProtocol.NeedsCleaning(message))
            {
                Deliver(level, message);
                return;
            }
            foreach (string line in BasisControlProtocol.CleanLines(message)) Deliver(level, line);
        }

        public void Dispose()
        {
            Session[] open;
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                open = sessions.ToArray();
            }

            try
            {
                listener.Dispose();
            }
            catch
            {
            }

            foreach (Session session in open) session.Close();

            try
            {
                if (File.Exists(SocketPath) && WriteTime(SocketPath) == boundWriteTime) File.Delete(SocketPath);
            }
            catch
            {
            }
        }

        private void Deliver(BasisLogLevel level, string line)
        {
            Session? capture = capturing;
            if (capture != null) capture.Send(BasisControlProtocol.Line(level, line), true);

            Session[] live;
            lock (gate)
            {
                backlog[(backlogStart + backlogCount) % BacklogLines] = (level, line);
                if (backlogCount < BacklogLines) backlogCount++;
                else backlogStart = (backlogStart + 1) % BacklogLines;
                live = attached;
            }
            if (live.Length == 0) return;

            string wire = BasisControlProtocol.Line(level, line);
            foreach (Session session in live)
            {
                if (session != capture) session.Send(wire, false);
            }
        }

        private void AcceptLoop()
        {
            while (true)
            {
                Socket client;
                try
                {
                    client = listener.Accept();
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (SocketException e)
                {
                    if (disposed) return;
                    BNL.LogWarning($"[basisctl] Accepting a control connection failed: {e.Message}");
                    Thread.Sleep(250);
                    continue;
                }

                Session session = new Session(this, client);
                string? refusal = null;
                lock (gate)
                {
                    if (disposed) refusal = "The server is shutting down.";
                    else if (sessions.Count >= MaxSessions) refusal = $"Too many basisctl sessions are open (limit {MaxSessions}).";
                    else sessions.Add(session);
                }

                if (refusal != null) session.Refuse(refusal);
                else session.Start();
            }
        }

        private int Run(Session session, string commandLine)
        {
            int space = commandLine.IndexOf(' ');
            string root = space < 0 ? commandLine : commandLine.Substring(0, space);
            BNL.Log($"[basisctl] {session.Caller} ran {root}");

            capturing = session;
            try
            {
                return execute(commandLine);
            }
            catch (Exception e)
            {
                BNL.LogError($"[basisctl] {root} failed: {e.Message}");
                return BasisControlProtocol.CommandFailed;
            }
            finally
            {
                capturing = null;
            }
        }

        private void Attach(Session session)
        {
            lock (gate)
            {
                for (int i = 0; i < backlogCount; i++)
                {
                    (BasisLogLevel level, string text) = backlog[(backlogStart + i) % BacklogLines];
                    session.Send(BasisControlProtocol.Line(level, text), false);
                }

                Session[] next = new Session[attached.Length + 1];
                attached.CopyTo(next, 0);
                next[attached.Length] = session;
                attached = next;
            }
        }

        private void Remove(Session session)
        {
            lock (gate)
            {
                sessions.Remove(session);
                int index = Array.IndexOf(attached, session);
                if (index < 0) return;

                Session[] next = new Session[attached.Length - 1];
                Array.Copy(attached, 0, next, 0, index);
                Array.Copy(attached, index + 1, next, index, attached.Length - index - 1);
                attached = next;
            }
        }

        private static bool IsAnswering(string path)
        {
            try
            {
                using Socket probe = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                probe.Connect(new UnixDomainSocketEndPoint(path));
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
        }

        private static DateTime WriteTime(string path)
        {
            try
            {
                return File.GetLastWriteTimeUtc(path);
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        private static string DescribePeer(Socket socket)
        {
            if (!OperatingSystem.IsLinux()) return "a local client";

            try
            {
                Span<byte> credentials = stackalloc byte[12];
                if (socket.GetRawSocketOption(1, 17, credentials) >= 12)
                {
                    int pid = BitConverter.ToInt32(credentials.Slice(0, 4));
                    int uid = BitConverter.ToInt32(credentials.Slice(4, 4));
                    return $"uid {uid} (pid {pid})";
                }
            }
            catch
            {
            }
            return "a local client";
        }

        private sealed class Session
        {
            private readonly BasisControlServer owner;
            private readonly Socket socket;
            private readonly BlockingCollection<string> outgoing = new BlockingCollection<string>(SessionQueueLines);
            private int dropped;
            private bool isAttached;

            public string Caller { get; }

            public Session(BasisControlServer owner, Socket socket)
            {
                this.owner = owner;
                this.socket = socket;
                Caller = DescribePeer(socket);
            }

            public void Start()
            {
                new Thread(WriteLoop) { IsBackground = true, Name = "basisctl write" }.Start();
                new Thread(ReadLoop) { IsBackground = true, Name = "basisctl read" }.Start();
            }

            public void Send(string line, bool required)
            {
                try
                {
                    if (required) outgoing.Add(line);
                    else if (!outgoing.TryAdd(line)) Interlocked.Increment(ref dropped);
                }
                catch (InvalidOperationException)
                {
                }
            }

            public void Close()
            {
                try
                {
                    outgoing.CompleteAdding();
                }
                catch (ObjectDisposedException)
                {
                }
            }

            public void Refuse(string reason)
            {
                try
                {
                    socket.Send(Encoding.UTF8.GetBytes(BasisControlProtocol.Line(BasisLogLevel.Error, reason) + "\n"));
                }
                catch
                {
                }
                socket.Dispose();
            }

            private void ReadLoop()
            {
                try
                {
                    Send(BasisControlProtocol.Greeting + " " + BasisControlProtocol.Version, true);

                    using NetworkStream stream = new NetworkStream(socket, false);
                    using StreamReader reader = new StreamReader(stream, new UTF8Encoding(false));
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.StartsWith(BasisControlProtocol.RunVerb + " ", StringComparison.Ordinal))
                        {
                            int code = owner.Run(this, line.Substring(BasisControlProtocol.RunVerb.Length + 1).Trim());
                            Send(BasisControlProtocol.Done(code), true);
                        }
                        else if (line == BasisControlProtocol.AttachVerb)
                        {
                            if (isAttached) continue;
                            isAttached = true;
                            owner.Attach(this);
                        }
                        else
                        {
                            Send(BasisControlProtocol.Line(BasisLogLevel.Error, "Unrecognised request. Update basisctl to match this server."), true);
                            Send(BasisControlProtocol.Done(BasisControlProtocol.CommandUnknown), true);
                        }
                    }
                }
                catch
                {
                }
                finally
                {
                    owner.Remove(this);
                    Close();
                }
            }

            private void WriteLoop()
            {
                try
                {
                    using NetworkStream stream = new NetworkStream(socket, false);
                    using StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\n" };
                    foreach (string line in outgoing.GetConsumingEnumerable())
                    {
                        int lost = Interlocked.Exchange(ref dropped, 0);
                        if (lost > 0) writer.WriteLine(BasisControlProtocol.Line(BasisLogLevel.Warning, $"[basisctl] {lost} log lines were skipped because this client fell behind."));
                        writer.WriteLine(line);
                        if (outgoing.Count == 0) writer.Flush();
                    }
                    writer.Flush();
                }
                catch
                {
                }
                finally
                {
                    Close();
                    try
                    {
                        socket.Shutdown(SocketShutdown.Both);
                    }
                    catch
                    {
                    }
                    socket.Dispose();
                }
            }
        }
    }
}
