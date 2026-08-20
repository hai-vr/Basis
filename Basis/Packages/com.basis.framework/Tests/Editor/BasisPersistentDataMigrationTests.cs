using System;
using System.IO;
using System.Reflection;
using Basis.Scripts.Common;
using NUnit.Framework;

namespace Basis.Tests
{
    public class BasisPersistentDataMigrationTests
    {
        string root;
        string source;
        string destination;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "BasisPersistentDataMigrationTests", Guid.NewGuid().ToString("N"));
            source = Path.Combine(root, "old");
            destination = Path.Combine(root, "new");
            Directory.CreateDirectory(source);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void MigrationMovesUserStateButLeavesCachesAndDiagnostics()
        {
            Write(source, "ItemKeyStore.json", "items");
            Write(source, "KeyStore.json", "avatars");
            Write(source, "settingsConfig.json", "settings");
            Write(source, "SavedServers.BAS", "servers");
            Write(source, "BasisMediaPlayerDiag.csv", "diagnostic");
            Write(source, "unclassified.bin", "other");

            Write(Path.Combine(source, "PlayerSettings"), "player.json", "player");
            Write(Path.Combine(source, "BasisActions"), "Desktop", "bindings.json", "bindings");
            Write(Path.Combine(source, "VoiceRecordings"), "recording.wav", "voice");
            Write(Path.Combine(source, "Backups"), "backup.basisbackup", "backup");
            Write(Path.Combine(source, "Basis"), "saved-image.png", "image");

            Write(Path.Combine(source, "BEEData"), "bundle.basis", "cache");
            Write(Path.Combine(source, "GraphicsState"), "warmup.bin", "cache");
            Write(Path.Combine(source, "CrashReports"), "crash.json", "diagnostic");
            Write(Path.Combine(source, "CalibrationDebug"), "capture.csv", "diagnostic");

            RunMigration();

            Assert.That(File.Exists(Path.Combine(destination, "ItemKeyStore.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(destination, "KeyStore.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(destination, "settingsConfig.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(destination, "SavedServers.BAS")), Is.True);
            Assert.That(File.Exists(Path.Combine(destination, "PlayerSettings", "player.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(destination, "BasisActions", "Desktop", "bindings.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(destination, "VoiceRecordings", "recording.wav")), Is.True);
            Assert.That(File.Exists(Path.Combine(destination, "Backups", "backup.basisbackup")), Is.True);
            Assert.That(File.Exists(Path.Combine(destination, "Basis", "saved-image.png")), Is.True);

            Assert.That(File.Exists(Path.Combine(source, "BasisMediaPlayerDiag.csv")), Is.True);
            Assert.That(File.Exists(Path.Combine(source, "unclassified.bin")), Is.True);
            Assert.That(File.Exists(Path.Combine(source, "BEEData", "bundle.basis")), Is.True);
            Assert.That(File.Exists(Path.Combine(source, "GraphicsState", "warmup.bin")), Is.True);
            Assert.That(File.Exists(Path.Combine(source, "CrashReports", "crash.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(source, "CalibrationDebug", "capture.csv")), Is.True);
        }

        [Test]
        public void MigrationNeverOverwritesExistingNewUserData()
        {
            Write(source, "settingsConfig.json", "old-settings");
            Write(destination, "settingsConfig.json", "new-settings");

            RunMigration();

            Assert.That(File.ReadAllText(Path.Combine(destination, "settingsConfig.json")), Is.EqualTo("new-settings"));
            Assert.That(File.ReadAllText(Path.Combine(source, "settingsConfig.json")), Is.EqualTo("old-settings"));
        }

        [Test]
        public void MigrationReplacesDefaultsCreatedDuringFreshBoot()
        {
            Write(source, "settingsConfig.json", "old-settings");
            Write(Path.Combine(source, "PlayerSettings"), "player.json", "old-player");
            Write(destination, "settingsConfig.json", "fresh-default-settings");
            Write(Path.Combine(destination, "PlayerSettings"), "player.json", "fresh-default-player");

            RunMigration(replaceExisting: true);

            Assert.That(File.ReadAllText(Path.Combine(destination, "settingsConfig.json")), Is.EqualTo("old-settings"));
            Assert.That(File.ReadAllText(Path.Combine(destination, "PlayerSettings", "player.json")), Is.EqualTo("old-player"));
            Assert.That(File.Exists(Path.Combine(source, "settingsConfig.json")), Is.False);
            Assert.That(File.Exists(Path.Combine(source, "PlayerSettings", "player.json")), Is.False);
        }

        [Test]
        public void MigrationLeavesTemporaryFilesInsideUserFolders()
        {
            Write(Path.Combine(source, "VoiceRecordings"), "complete.wav", "complete");
            Write(Path.Combine(source, "VoiceRecordings"), "recording.tmp", "partial");

            RunMigration();

            Assert.That(File.Exists(Path.Combine(destination, "VoiceRecordings", "complete.wav")), Is.True);
            Assert.That(File.Exists(Path.Combine(source, "VoiceRecordings", "recording.tmp")), Is.True);
            Assert.That(File.Exists(Path.Combine(destination, "VoiceRecordings", "recording.tmp")), Is.False);
        }

        void RunMigration(bool replaceExisting = false)
        {
            MethodInfo method = typeof(BasisPersistentDataMigration).GetMethod(
                "MoveSelectedUserData", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(null, new object[] { source, destination, replaceExisting });
        }

        static void Write(string directory, string fileName, string contents)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, fileName), contents);
        }

        static void Write(string directory, string childDirectory, string fileName, string contents)
        {
            Write(Path.Combine(directory, childDirectory), fileName, contents);
        }
    }
}
