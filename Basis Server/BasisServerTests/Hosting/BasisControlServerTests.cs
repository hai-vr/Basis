using BasisNetworkConsole;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using Xunit;

namespace BasisServerTests.Hosting
{
    public sealed class UnixFactAttribute : FactAttribute
    {
        public UnixFactAttribute()
        {
            if (OperatingSystem.IsWindows()) Skip = "Unix file modes only";
        }
    }

    public sealed class LinuxFactAttribute : FactAttribute
    {
        public LinuxFactAttribute()
        {
            if (!OperatingSystem.IsLinux()) Skip = "Linux only";
        }
    }

    public sealed class BasisControlServerTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "bctl-" + Guid.NewGuid().ToString("N").Substring(0, 8));

        public BasisControlServerTests()
        {
            Directory.CreateDirectory(directory);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(directory, true);
            }
            catch
            {
            }
        }

        private string SocketPath => Path.Combine(directory, BasisControlProtocol.SocketFileName);

        [Fact]
        public void Run_ReturnsTheCommandOutputThenItsCode()
        {
            BasisControlServer? server = null;
            server = BasisControlServer.Start(SocketPath, line =>
            {
                server!.Publish(BasisLogLevel.Info, "ran " + line);
                server.Publish(BasisLogLevel.Error, "and complained");
                return BasisControlProtocol.CommandRan;
            });
            Assert.NotNull(server);

            using (server)
            using (Client client = Client.Connect(SocketPath))
            {
                client.Send("run /status");
                Assert.Equal(new[] { "I ran /status", "E and complained" }, client.ReadUntilDone(out int code));
                Assert.Equal(BasisControlProtocol.CommandRan, code);
            }
        }

        [Fact]
        public void Run_ReportsUnknownAndFailedCommands()
        {
            using BasisControlServer? server = BasisControlServer.Start(SocketPath, line =>
            {
                if (line == "/boom") throw new InvalidOperationException("boom");
                return BasisControlProtocol.CommandUnknown;
            });
            Assert.NotNull(server);

            using Client client = Client.Connect(SocketPath);
            client.Send("run /nope");
            client.ReadUntilDone(out int unknown);
            Assert.Equal(BasisControlProtocol.CommandUnknown, unknown);

            client.Send("run /boom");
            client.ReadUntilDone(out int failed);
            Assert.Equal(BasisControlProtocol.CommandFailed, failed);
        }

        [Fact]
        public void Run_DoesNotCaptureLinesLoggedByOtherThreads()
        {
            BasisControlServer? server = null;
            server = BasisControlServer.Start(SocketPath, line =>
            {
                Thread other = new Thread(() => server!.Publish(BasisLogLevel.Info, "from another thread"));
                other.Start();
                other.Join();
                server!.Publish(BasisLogLevel.Info, "from the command");
                return BasisControlProtocol.CommandRan;
            });
            Assert.NotNull(server);

            using (server)
            using (Client client = Client.Connect(SocketPath))
            {
                client.Send("run /players");
                Assert.Equal(new[] { "I from the command" }, client.ReadUntilDone(out _));
            }
        }

        [Fact]
        public void Publish_SplitsMultiLineMessagesAndReplacesControlCharacters()
        {
            BasisControlServer? server = null;
            server = BasisControlServer.Start(SocketPath, line =>
            {
                server!.Publish(BasisLogLevel.Warning, "first\r\nsecond [31mred");
                return BasisControlProtocol.CommandRan;
            });
            Assert.NotNull(server);

            using (server)
            using (Client client = Client.Connect(SocketPath))
            {
                client.Send("run /x");
                Assert.Equal(new[] { "W first", "W second ?[31mred" }, client.ReadUntilDone(out _));
            }
        }

        [Fact]
        public void Attach_ReplaysRecentLinesThenStreamsNewOnes()
        {
            using BasisControlServer? server = BasisControlServer.Start(SocketPath, line => BasisControlProtocol.CommandRan);
            Assert.NotNull(server);
            server.Publish(BasisLogLevel.Info, "before attach");

            using Client client = Client.Connect(SocketPath);
            client.Send("attach");
            Assert.Equal("I before attach", client.ReadLine());

            server.Publish(BasisLogLevel.Error, "after attach");
            Assert.Equal("E after attach", client.ReadLine());
        }

        [Fact]
        public void Attach_AStalledClientNeverBlocksPublishing()
        {
            using BasisControlServer? server = BasisControlServer.Start(SocketPath, line => BasisControlProtocol.CommandRan);
            Assert.NotNull(server);

            using Client client = Client.Connect(SocketPath);
            client.Send("attach");
            Thread.Sleep(200);

            string payload = new string('x', 256);
            Stopwatch watch = Stopwatch.StartNew();
            for (int i = 0; i < 100000; i++) server.Publish(BasisLogLevel.Info, payload);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"publishing took {watch.Elapsed}");
        }

        [Fact]
        public void Start_ReplacesAStaleSocketFile()
        {
            using (Socket stale = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified))
            {
                stale.Bind(new UnixDomainSocketEndPoint(SocketPath));
            }
            if (!File.Exists(SocketPath)) File.WriteAllText(SocketPath, "left behind by a crashed server");
            Assert.True(File.Exists(SocketPath));

            using BasisControlServer? server = BasisControlServer.Start(SocketPath, line => BasisControlProtocol.CommandRan);
            Assert.NotNull(server);

            using Client client = Client.Connect(SocketPath);
            client.Send("run /status");
            client.ReadUntilDone(out int code);
            Assert.Equal(BasisControlProtocol.CommandRan, code);
        }

        [Fact]
        public void Start_LeavesASocketThatAnotherServerIsAnswering()
        {
            using BasisControlServer? first = BasisControlServer.Start(SocketPath, line => BasisControlProtocol.CommandRan);
            Assert.NotNull(first);

            using BasisControlServer? second = BasisControlServer.Start(SocketPath, line => BasisControlProtocol.CommandUnknown);
            Assert.Null(second);

            using Client client = Client.Connect(SocketPath);
            client.Send("run /status");
            client.ReadUntilDone(out int code);
            Assert.Equal(BasisControlProtocol.CommandRan, code);
        }

        [Fact]
        public void Dispose_RemovesTheSocketFileAndClosesSessions()
        {
            BasisControlServer? server = BasisControlServer.Start(SocketPath, line => BasisControlProtocol.CommandRan);
            Assert.NotNull(server);

            using Client client = Client.Connect(SocketPath);
            server.Dispose();

            Assert.False(File.Exists(SocketPath));
            Assert.Null(client.ReadLine());
        }

        [UnixFact]
        public void Start_LimitsTheSocketToItsOwnerAndGroup()
        {
            using BasisControlServer? server = BasisControlServer.Start(SocketPath, line => BasisControlProtocol.CommandRan);
            Assert.NotNull(server);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.GroupWrite, File.GetUnixFileMode(SocketPath));
        }

        [Fact]
        public void Fit_KeepsShortPathsAndMapsLongOnesToAStableTempPath()
        {
            Assert.Equal(SocketPath, BasisControlProtocol.Fit(SocketPath));

            string longPath = Path.Combine(directory, new string('d', 120), BasisControlProtocol.SocketFileName);
            string fitted = BasisControlProtocol.Fit(longPath);
            Assert.StartsWith(Path.GetTempPath(), fitted);
            Assert.EndsWith(".sock", fitted);
            Assert.True(Encoding.UTF8.GetByteCount(fitted) < 100);
            Assert.Equal(fitted, BasisControlProtocol.Fit(longPath));
        }

        private sealed class Client : IDisposable
        {
            private readonly Socket socket;
            private readonly StreamReader reader;
            private readonly StreamWriter writer;

            private Client(Socket socket)
            {
                this.socket = socket;
                NetworkStream stream = new NetworkStream(socket, true);
                reader = new StreamReader(stream, new UTF8Encoding(false));
                writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\n", AutoFlush = true };
            }

            public static Client Connect(string path)
            {
                Socket socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified) { ReceiveTimeout = 10000 };
                socket.Connect(new UnixDomainSocketEndPoint(path));
                Client client = new Client(socket);
                Assert.Equal(BasisControlProtocol.Greeting + " " + BasisControlProtocol.Version, client.ReadLine());
                return client;
            }

            public void Send(string line) => writer.WriteLine(line);

            public string? ReadLine()
            {
                try
                {
                    return reader.ReadLine();
                }
                catch (IOException)
                {
                    return null;
                }
            }

            public List<string> ReadUntilDone(out int code)
            {
                List<string> lines = new List<string>();
                while (true)
                {
                    string? line = ReadLine();
                    Assert.NotNull(line);
                    if (line.StartsWith(BasisControlProtocol.DoneVerb + " ", StringComparison.Ordinal))
                    {
                        code = int.Parse(line.Substring(BasisControlProtocol.DoneVerb.Length + 1));
                        return lines;
                    }
                    lines.Add(line);
                }
            }

            public void Dispose()
            {
                reader.Dispose();
                writer.Dispose();
                socket.Dispose();
            }
        }
    }
}
