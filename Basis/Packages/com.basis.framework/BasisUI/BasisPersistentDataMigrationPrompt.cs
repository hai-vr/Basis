using Basis.Scripts.Common;
using Basis.Scripts.Networking;
using Basis.Scripts.Settings;
using UnityEngine;

namespace Basis.BasisUI
{
    /// <summary>
    /// Asks the user before moving persistent data from the previous Basis storage location.
    /// The check only runs after Basis has initialized settings and only when that initialization
    /// had to create a fresh settings file. No files are moved until the user accepts this prompt.
    /// </summary>
    public static class BasisPersistentDataMigrationPrompt
    {
        public static void ShowIfPending()
        {
            if (!BasisSettingsSystem.SettingsLoaded || !BasisSettingsSystem.FreshSettingsFile)
            {
                return;
            }

            if (!BasisPersistentDataMigration.TryPrepareMigration(out BasisPersistentDataMigration.MigrationRequest request))
            {
                return;
            }

            BasisMainMenu.Open();
            if (!BasisMainMenu.Instance)
            {
                BasisDebug.LogWarning(
                    $"Persistent data migration prompt could not be displayed. Old directory: \"{request.SourcePath}\".");
                return;
            }

            string applicationName = Application.productName;
            string restartNote = BasisLocalization.Get(
                BasisAppRelaunch.IsSupported
                    ? "migration.persistent.restartAutomatic"
                    : "migration.persistent.restartManual",
                applicationName);
            string description = BasisLocalization.Get(
                "migration.persistent.description",
                applicationName,
                request.SourcePath,
                request.DestinationPath,
                restartNote);

            if (BasisMainMenu.Instance.Dialogue != null)
            {
                BasisMainMenu.Instance.Dialogue.ReleaseInstance();
            }

            BasisMainMenu.Instance.OpenDialogue(
                BasisLocalization.Get("migration.persistent.title", applicationName),
                description,
                BasisLocalization.Get("ui.yes"),
                BasisLocalization.Get("ui.no"),
                migrate =>
                {
                    if (migrate)
                    {
                        if (!BasisPersistentDataMigration.MigratePending(out BasisPersistentDataMigration.MigrationNotice notice))
                        {
                            BasisDebug.LogWarning(
                                $"Persistent data migration did not complete. Old directory: \"{request.SourcePath}\".");
                            return;
                        }

                        BasisDebug.Log(
                            $"Migrated {notice.MovedFiles} user file(s). Old directory remains at \"{notice.SourcePath}\".");

                        if (BasisAppRelaunch.IsSupported)
                        {
                            BasisAppRelaunch.RebootAndReconnect();
                        }
                        else
                        {
                            BasisDebug.LogWarning("Restart Basis to load the migrated settings.");
                        }
                    }
                    else
                    {
                        // Ask again on a future launch, but do not repeatedly prompt during this one.
                        BasisPersistentDataMigration.DismissPendingMigration();
                    }
                },
                category: BasisNotificationCategory.System);

            if (BasisMainMenu.Instance.Dialogue)
            {
                BasisMainMenu.Instance.Dialogue.BlocksOtherActions = true;
            }
        }
    }
}
