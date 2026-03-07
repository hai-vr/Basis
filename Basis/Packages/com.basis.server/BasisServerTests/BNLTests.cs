using System;
using System.Threading;
using Xunit;

namespace BasisServerTests
{
    public class BNLTests : IDisposable
    {
        private readonly Action<string> _origLog;
        private readonly Action<string> _origWarn;
        private readonly Action<string> _origError;

        public BNLTests()
        {
            _origLog = BNL.LogOutput;
            _origWarn = BNL.LogWarningOutput;
            _origError = BNL.LogErrorOutput;
        }

        public void Dispose()
        {
            BNL.LogOutput = _origLog;
            BNL.LogWarningOutput = _origWarn;
            BNL.LogErrorOutput = _origError;
        }

        [Fact]
        public void Log_InvokesCustomHandler()
        {
            string captured = null;
            BNL.LogOutput = msg => Interlocked.CompareExchange(ref captured, msg, null);

            BNL.Log("test message");

            Assert.Equal("test message", captured);
        }

        [Fact]
        public void LogWarning_InvokesCustomHandler()
        {
            string captured = null;
            BNL.LogWarningOutput = msg => Interlocked.CompareExchange(ref captured, msg, null);

            BNL.LogWarning("warning message");

            Assert.Equal("warning message", captured);
        }

        [Fact]
        public void LogError_InvokesCustomHandler()
        {
            string captured = null;
            BNL.LogErrorOutput = msg => Interlocked.CompareExchange(ref captured, msg, null);

            BNL.LogError("error message");

            Assert.Equal("error message", captured);
        }

        [Fact]
        public void Log_WithoutHandler_DoesNotThrow()
        {
            BNL.LogOutput = null;
            BNL.Log("should not throw"); // falls through to Console.WriteLine
        }

        [Fact]
        public void LogWarning_WithoutHandler_DoesNotThrow()
        {
            BNL.LogWarningOutput = null;
            BNL.LogWarning("should not throw");
        }

        [Fact]
        public void LogError_WithoutHandler_DoesNotThrow()
        {
            BNL.LogErrorOutput = null;
            BNL.LogError("should not throw");
        }
    }
}
