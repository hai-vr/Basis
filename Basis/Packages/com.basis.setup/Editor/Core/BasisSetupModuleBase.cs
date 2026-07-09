using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Basis.Setup
{
    public abstract class BasisSetupModuleBase : IBasisSetupModule
    {
        public abstract string Key { get; }
        public abstract string DisplayName { get; }
        public virtual string Category => "General";
        public abstract int Version { get; }
        public virtual int Order => 0;
        public virtual bool IsAvailable => true;
        public virtual bool AutoApplyOnImport => false;

        /// <summary>
        /// Project-relative files/folders this module replicates verbatim from the package's template store.
        /// A module that sets this needs no <see cref="Build"/> override — the default copies the templates in,
        /// then runs <see cref="Activate"/>. Legacy modules still override <see cref="Build"/> directly.
        /// </summary>
        public virtual IEnumerable<string> OwnedPaths => Array.Empty<string>();

        protected virtual bool Exists()
        {
            bool any = false;
            foreach (string path in OwnedPaths)
            {
                any = true;
                if (!BasisSetupIO.ProjectPathExists(path))
                {
                    return false;
                }
            }

            return any;
        }

        protected virtual bool Build(BasisSetupMode mode)
        {
            bool changed = false;
            foreach (string path in OwnedPaths)
            {
                changed |= BasisSetupTemplates.CopyFromTemplates(path, mode);
            }

            if (changed)
            {
                AssetDatabase.Refresh();
            }

            Activate();
            return changed;
        }

        /// <summary>Editor wiring an asset can't carry (config-object registration, graphics defaults). Runs after copy.</summary>
        protected virtual void Activate()
        {
        }

        public BasisSetupStatus GetStatus()
        {
            if (!IsAvailable)
            {
                return BasisSetupStatus.NotApplicable;
            }

            if (!Exists())
            {
                return BasisSetupStatus.Missing;
            }

            return BasisSetupState.GetVersion(Key) >= Version
                ? BasisSetupStatus.UpToDate
                : BasisSetupStatus.Outdated;
        }

        public BasisSetupReport Apply(BasisSetupMode mode)
        {
            if (!IsAvailable)
            {
                return BasisSetupReport.Result(Key, BasisSetupStatus.NotApplicable, false, "Dependency not present.");
            }

            bool existed = Exists();
            if (mode == BasisSetupMode.EnsureExists && existed)
            {
                return BasisSetupReport.Result(Key, GetStatus(), false, "Already present.");
            }

            try
            {
                bool changed = Build(mode);
                BasisSetupState.SetVersion(Key, Version);
                string message = changed ? (existed ? "Updated." : "Created.") : "No changes needed.";
                return BasisSetupReport.Result(Key, BasisSetupStatus.UpToDate, changed, message);
            }
            catch (Exception e)
            {
                Debug.LogError($"[BasisSetup] {DisplayName} failed: {e}");
                return BasisSetupReport.Result(Key, BasisSetupStatus.Error, false, e.Message);
            }
        }
    }
}
