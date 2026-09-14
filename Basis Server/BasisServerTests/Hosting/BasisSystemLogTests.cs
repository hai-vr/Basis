using BasisNetworkConsole;
using System.Net.Sockets;
using System.Text;
using Xunit;

namespace BasisServerTests.Hosting
{
    public sealed class BasisSystemLogTests
    {
        [Theory]
        [InlineData(BasisLogLevel.Info, "<6>server booting\n")]
        [InlineData(BasisLogLevel.Warning, "<4>server booting\n")]
        [InlineData(BasisLogLevel.Error, "<3>server booting\n")]
        public void FormatJournal_PrefixesTheSyslogSeverity(BasisLogLevel level, string expected)
        {
            Assert.Equal(expected, BasisSystemLog.FormatJournal(level, "server booting"));
        }

        [Fact]
        public void FormatJournal_GivesEveryLineOfAMultiLineMessageItsSeverity()
        {
            Assert.Equal("<3>Unhandled Exception: boom\n<3>   at Somewhere()\n", BasisSystemLog.FormatJournal(BasisLogLevel.Error, "Unhandled Exception: boom\r\n   at Somewhere()"));
        }

        [Fact]
        public void FormatJournal_ReplacesControlCharacters()
        {
            Assert.Equal("<6>name ?[2Jhere\ttab\n", BasisSystemLog.FormatJournal(BasisLogLevel.Info, "name [2Jhere\ttab"));
        }

        [Fact]
        public void FormatSyslog_UsesTheDaemonFacilityAndTheLocalSocketFormat()
        {
            DateTime time = new DateTime(2026, 9, 4, 7, 8, 9);
            Assert.Equal("<28>Sep  4 07:08:09 basis-server[42]: port in use", BasisSystemLog.FormatSyslog(BasisLogLevel.Warning, "port in use", time, 42));
            Assert.Equal("<27>Dec 25 23:59:59 basis-server[7]: crashed", BasisSystemLog.FormatSyslog(BasisLogLevel.Error, "crashed", new DateTime(2026, 12, 25, 23, 59, 59), 7));
            Assert.Equal("<30>Sep  4 07:08:09 basis-server[42]: ready", BasisSystemLog.FormatSyslog(BasisLogLevel.Info, "ready", time, 42));
        }

        [Theory]
        [InlineData(null, false, BasisLogTarget.Console)]
        [InlineData(null, true, BasisLogTarget.Journal)]
        [InlineData("auto", true, BasisLogTarget.Journal)]
        [InlineData("journal", false, BasisLogTarget.Journal)]
        [InlineData("Syslog", true, BasisLogTarget.Syslog)]
        [InlineData("console", true, BasisLogTarget.Console)]
        [InlineData("nonsense", false, BasisLogTarget.Console)]
        public void ChooseTarget_PrefersTheSettingAndOtherwiseDetectsTheJournal(string? configured, bool stdoutIsJournal, BasisLogTarget expected)
        {
            Assert.Equal(expected, BasisSystemLog.ChooseTarget(configured, stdoutIsJournal));
        }

        [Theory]
        [InlineData("8:41562", "socket:[41562]", true, true)]
        [InlineData("8:41562", "socket:[99]", true, false)]
        [InlineData("8:41562", "/dev/pts/3", false, false)]
        [InlineData("8:41562", null, true, true)]
        [InlineData("8:41562", null, false, false)]
        [InlineData("", "socket:[41562]", true, false)]
        [InlineData("41562", "socket:[41562]", true, false)]
        public void MatchesJournalStream_ComparesTheStdoutInode(string journalStream, string? stdoutLink, bool redirected, bool expected)
        {
            Assert.Equal(expected, BasisSystemLog.MatchesJournalStream(journalStream, stdoutLink, redirected));
        }
    }

    public sealed class BasisSystemdTests
    {
        [Theory]
        [InlineData("/run/systemd/notify", "/run/systemd/notify")]
        [InlineData("@/org/freedesktop/systemd1/notify/42", "\0/org/freedesktop/systemd1/notify/42")]
        [InlineData("", null)]
        [InlineData(null, null)]
        [InlineData("vsock:2:1234", null)]
        public void NotifyAddress_SupportsPathsAndAbstractSockets(string? value, string? expected)
        {
            Assert.Equal(expected, BasisSystemd.NotifyAddress(value));
        }

        [Theory]
        [InlineData("30000000", null, 5, 30000000L)]
        [InlineData("30000000", "5", 5, 30000000L)]
        [InlineData("30000000", "6", 5, 0L)]
        [InlineData("0", null, 5, 0L)]
        [InlineData("soon", null, 5, 0L)]
        [InlineData(null, null, 5, 0L)]
        public void WatchdogInterval_OnlyAppliesToThisProcess(string? microseconds, string? owner, int processId, long expected)
        {
            Assert.Equal(expected, BasisSystemd.WatchdogInterval(microseconds, owner, processId));
        }

        [Theory]
        [InlineData("1234 (dotnet) S 1 1234 1234 0 -1 4194560", 1)]
        [InlineData("77 (odd) name (x)) R 42 77 77 0 -1", 42)]
        [InlineData("no parenthesis here", -1)]
        public void ParentProcessId_ReadsTheFieldAfterTheCommandName(string stat, int expected)
        {
            Assert.Equal(expected, BasisSystemd.ParentProcessId(stat));
        }

        [LinuxFact]
        public void Notify_SendsTheStateToTheServiceManagerSocket()
        {
            string path = Path.Combine(Path.GetTempPath(), "bnotify-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".sock");
            using Socket manager = new Socket(AddressFamily.Unix, SocketType.Dgram, ProtocolType.Unspecified);
            manager.Bind(new UnixDomainSocketEndPoint(path));
            manager.ReceiveTimeout = 5000;
            try
            {
                Environment.SetEnvironmentVariable("NOTIFY_SOCKET", path);
                BasisSystemd.Initialize();
                Assert.Null(Environment.GetEnvironmentVariable("NOTIFY_SOCKET"));
                Assert.True(BasisSystemd.CanNotify);

                BasisSystemd.Ready("Listening on UDP port 4296");

                byte[] buffer = new byte[512];
                int length = manager.Receive(buffer);
                Assert.Equal("READY=1\nSTATUS=Listening on UDP port 4296", Encoding.UTF8.GetString(buffer, 0, length));
            }
            finally
            {
                Environment.SetEnvironmentVariable("NOTIFY_SOCKET", null);
                BasisSystemd.Initialize();
                File.Delete(path);
            }
        }
    }
}
