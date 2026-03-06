using System;
using System.IO;
using Xunit;

namespace BasisServerTests
{
    public class ConfigurationTests
    {
        [Fact]
        public void DefaultConfiguration_HasExpectedDefaults()
        {
            var config = new Configuration();

            Assert.Equal(ushort.MaxValue, config.PeerLimit);
            Assert.Equal(4296, config.SetPort);
            Assert.True(config.UseNativeSockets);
            Assert.False(config.NatPunchEnabled);
            Assert.Equal(1500, config.PingInterval);
            Assert.Equal(6000, config.DisconnectTimeout);
            Assert.Equal("0.0.0.0", config.IPv4Address);
            Assert.Equal("::1", config.IPv6Address);
            Assert.Equal("default_password", config.Password);
            Assert.True(config.UseAuth);
            Assert.True(config.UseAuthIdentity);
        }

        [Fact]
        public void LoadFromXml_CreatesDefaultConfig_WhenFileDoesNotExist()
        {
            string tempPath = Path.Combine(Path.GetTempPath(), $"test_config_{Guid.NewGuid()}.xml");
            try
            {
                var config = Configuration.LoadFromXml(tempPath);

                Assert.NotNull(config);
                Assert.Equal(ushort.MaxValue, config.PeerLimit);
                Assert.True(File.Exists(tempPath));
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        [Fact]
        public void LoadFromXml_ReadsExistingConfig()
        {
            string tempPath = Path.Combine(Path.GetTempPath(), $"test_config_{Guid.NewGuid()}.xml");
            try
            {
                // Create a config with custom values
                var original = Configuration.LoadFromXml(tempPath);

                // Reload it
                var loaded = Configuration.LoadFromXml(tempPath);

                Assert.Equal(original.PeerLimit, loaded.PeerLimit);
                Assert.Equal(original.SetPort, loaded.SetPort);
                Assert.Equal(original.Password, loaded.Password);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        [Fact]
        public void ProcessEnvironmentalOverrides_Int_OverridesField()
        {
            var config = new Configuration();
            string envKey = "PeerLimit";
            string originalValue = Environment.GetEnvironmentVariable(envKey);
            try
            {
                Environment.SetEnvironmentVariable(envKey, "256");
                config.ProcessEnvironmentalOverrides();
                Assert.Equal(256, config.PeerLimit);
            }
            finally
            {
                Environment.SetEnvironmentVariable(envKey, originalValue);
            }
        }

        [Fact]
        public void ProcessEnvironmentalOverrides_UShort_OverridesField()
        {
            var config = new Configuration();
            string envKey = "SetPort";
            string originalValue = Environment.GetEnvironmentVariable(envKey);
            try
            {
                Environment.SetEnvironmentVariable(envKey, "9999");
                config.ProcessEnvironmentalOverrides();
                Assert.Equal((ushort)9999, config.SetPort);
            }
            finally
            {
                Environment.SetEnvironmentVariable(envKey, originalValue);
            }
        }

        [Fact]
        public void ProcessEnvironmentalOverrides_String_OverridesField()
        {
            var config = new Configuration();
            string envKey = "Password";
            string originalValue = Environment.GetEnvironmentVariable(envKey);
            try
            {
                Environment.SetEnvironmentVariable(envKey, "super_secret");
                config.ProcessEnvironmentalOverrides();
                Assert.Equal("super_secret", config.Password);
            }
            finally
            {
                Environment.SetEnvironmentVariable(envKey, originalValue);
            }
        }

        [Fact]
        public void ProcessEnvironmentalOverrides_Bool_True()
        {
            var config = new Configuration();
            string envKey = "NatPunchEnabled";
            string originalValue = Environment.GetEnvironmentVariable(envKey);
            try
            {
                Environment.SetEnvironmentVariable(envKey, "true");
                config.ProcessEnvironmentalOverrides();
                Assert.True(config.NatPunchEnabled);
            }
            finally
            {
                Environment.SetEnvironmentVariable(envKey, originalValue);
            }
        }

        [Fact]
        public void ProcessEnvironmentalOverrides_Bool_False()
        {
            var config = new Configuration();
            string envKey = "UseNativeSockets";
            string originalValue = Environment.GetEnvironmentVariable(envKey);
            try
            {
                Environment.SetEnvironmentVariable(envKey, "false");
                config.ProcessEnvironmentalOverrides();
                Assert.False(config.UseNativeSockets);
            }
            finally
            {
                Environment.SetEnvironmentVariable(envKey, originalValue);
            }
        }

        [Fact]
        public void ProcessEnvironmentalOverrides_Float_OverridesField()
        {
            var config = new Configuration();
            string envKey = "BSRSIncreaseRate";
            string originalValue = Environment.GetEnvironmentVariable(envKey);
            try
            {
                Environment.SetEnvironmentVariable(envKey, "0.1");
                config.ProcessEnvironmentalOverrides();
                Assert.Equal(0.1f, config.BSRSIncreaseRate);
            }
            finally
            {
                Environment.SetEnvironmentVariable(envKey, originalValue);
            }
        }

        [Fact]
        public void ProcessEnvironmentalOverrides_InvalidInt_DoesNotChange()
        {
            var config = new Configuration();
            int original = config.PeerLimit;
            string envKey = "PeerLimit";
            string originalValue = Environment.GetEnvironmentVariable(envKey);
            try
            {
                Environment.SetEnvironmentVariable(envKey, "not_a_number");
                config.ProcessEnvironmentalOverrides();
                Assert.Equal(original, config.PeerLimit);
            }
            finally
            {
                Environment.SetEnvironmentVariable(envKey, originalValue);
            }
        }

        [Fact]
        public void ProcessEnvironmentalOverrides_InvalidBool_DoesNotChange()
        {
            var config = new Configuration();
            bool original = config.UseAuth;
            string envKey = "UseAuth";
            string originalValue = Environment.GetEnvironmentVariable(envKey);
            try
            {
                Environment.SetEnvironmentVariable(envKey, "maybe");
                config.ProcessEnvironmentalOverrides();
                Assert.Equal(original, config.UseAuth);
            }
            finally
            {
                Environment.SetEnvironmentVariable(envKey, originalValue);
            }
        }

        [Fact]
        public void ProcessEnvironmentalOverrides_NoEnvVar_DoesNotChange()
        {
            var config = new Configuration();
            int originalPeerLimit = config.PeerLimit;
            string originalPassword = config.Password;

            // Ensure no env vars are set for these
            string envKeyPeer = "PeerLimit";
            string envKeyPass = "Password";
            string origPeer = Environment.GetEnvironmentVariable(envKeyPeer);
            string origPass = Environment.GetEnvironmentVariable(envKeyPass);
            try
            {
                Environment.SetEnvironmentVariable(envKeyPeer, null);
                Environment.SetEnvironmentVariable(envKeyPass, null);

                config.ProcessEnvironmentalOverrides();

                Assert.Equal(originalPeerLimit, config.PeerLimit);
                Assert.Equal(originalPassword, config.Password);
            }
            finally
            {
                Environment.SetEnvironmentVariable(envKeyPeer, origPeer);
                Environment.SetEnvironmentVariable(envKeyPass, origPass);
            }
        }
    }
}
