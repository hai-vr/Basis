using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Xml.Serialization;
using Basis.Network.Core;

/// <summary>One setting the benchmark fitted, and why.</summary>
[Serializable]
public class BasisTunedSetting
{
    /// <summary>Field name on <see cref="Configuration"/> or on the transport config.</summary>
    [XmlAttribute] public string Name = string.Empty;

    /// <summary>Value to write, in invariant culture.</summary>
    [XmlAttribute] public string Value = string.Empty;

    /// <summary>
    /// Empty for a server setting; otherwise the network stack whose sidecar declares it
    /// (<c>litenetlib</c>). Settings live in two different files and the profile has to say which.
    /// </summary>
    [XmlAttribute] public string Stack = string.Empty;

    /// <summary>How the value was arrived at, carried through so the boot log can say.</summary>
    [XmlAttribute] public string Evidence = string.Empty;

    /// <summary>The reasoning, kept in the file because a bare number in a config explains nothing.</summary>
    public string Rationale = string.Empty;
}

/// <summary>
/// Settings fitted to this machine by the benchmark, applied once on the next boot.
///
/// <para>The point of the file is to close the loop. The benchmark can measure a host precisely and
/// still be useless if acting on the result means an operator hand-copying a dozen numbers into two
/// XML files without typos — so it writes this instead, drops it next to the config, and the server
/// picks it up.</para>
///
/// <para><b>Applied once, then folded into config.xml.</b> The obvious alternative is to keep the
/// profile authoritative and re-read it every boot, and it is a trap: the operator later edits
/// config.xml, the profile silently overrides them on the next restart, and the setting appears not
/// to work with nothing anywhere explaining why. Instead the values are written into config.xml and
/// the transport sidecar, the profile is stamped as applied, and from that moment config.xml is the
/// single source of truth again — edits behave normally and the boot log records exactly what
/// changed and why.</para>
///
/// <para><b>Refuses to apply on different hardware.</b> Every setting in here is a function of the
/// core count and the kernel of the box it was measured on; a 64-core profile landing on a 4-vCPU
/// container is worse than no profile at all. The fingerprint is deliberately coarse — OS family,
/// architecture, core count — because those are what the settings actually depend on, and anything
/// finer would reject a machine that had a stick of RAM added.</para>
/// </summary>
[Serializable]
[XmlRoot("BasisTuningProfile")]
public class BasisTuningProfile
{
    /// <summary>Bump when the shape changes. A newer file is refused rather than half-read.</summary>
    public const int CurrentVersion = 1;

    /// <summary>Looked for in the config folder, beside config.xml.</summary>
    public const string FileName = "tuning-profile.xml";

    public int ProfileVersion = CurrentVersion;

    /// <summary>When the benchmark produced this, ISO-8601 UTC.</summary>
    public string GeneratedUtc = string.Empty;

    /// <summary>Tool and version that produced it.</summary>
    public string GeneratedBy = string.Empty;

    /// <summary>The machine it was measured on. Compared against the booting host.</summary>
    public string Machine = string.Empty;

    /// <summary>Human-readable detail about that machine, for the log and for a person reading the file.</summary>
    public string MachineDetail = string.Empty;

    /// <summary>Population the settings were fitted at, so a reader knows what they are tuned for.</summary>
    public int DesignPlayers;

    /// <summary>
    /// Apply even when the fingerprint does not match the booting machine. Off by default; set it
    /// deliberately when moving a profile between identical hosts that fingerprint differently.
    /// </summary>
    public bool ApplyToAnyMachine;

    /// <summary>Empty until applied; stamped afterwards so a restart does not re-apply it.</summary>
    public string AppliedUtc = string.Empty;

    [XmlArray("Settings")]
    [XmlArrayItem("Setting")]
    public List<BasisTunedSetting> Settings = new List<BasisTunedSetting>();

    /// <summary>OS family, architecture and core count — the properties the settings depend on.</summary>
    public static string Fingerprint()
    {
        string os = RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux"
            : RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macos"
            : "other";
        return $"{os}-{RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()}-{Environment.ProcessorCount}c";
    }

    public static string ResolvePath(string configDir) => Path.Combine(configDir, FileName);

    public static BasisTuningProfile TryLoad(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var serializer = new XmlSerializer(typeof(BasisTuningProfile));
            using (var reader = new StreamReader(path))
            {
                return (BasisTuningProfile)serializer.Deserialize(reader);
            }
        }
        catch (Exception ex)
        {
            BNL.LogWarning($"[Tuning] Could not read '{path}': {ex.Message}");
            return null;
        }
    }

    public void Save(string path)
    {
        var serializer = new XmlSerializer(typeof(BasisTuningProfile));
        string dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        string temp = path + ".tmp";
        using (var writer = new StreamWriter(temp))
        {
            serializer.Serialize(writer, this);
        }
        if (File.Exists(path)) File.Replace(temp, path, null);
        else File.Move(temp, path);
    }

    /// <summary>
    /// The boot hook: finds a profile beside the config, applies it, and folds it into the config
    /// files. Silent and harmless when there is no profile, which is the normal case.
    /// </summary>
    /// <param name="configDir">The server's config folder.</param>
    /// <param name="config">The live configuration, already loaded.</param>
    /// <returns>True when settings were applied and the config files were rewritten.</returns>
    public static bool ApplyIfPresent(string configDir, Configuration config)
    {
        string path = ResolvePath(configDir);
        BasisTuningProfile profile = TryLoad(path);
        if (profile == null) return false;

        if (profile.ProfileVersion > CurrentVersion)
        {
            BNL.LogWarning(
                $"[Tuning] '{FileName}' is version {profile.ProfileVersion}; this build understands {CurrentVersion}. " +
                "Ignoring it rather than applying part of a file it does not fully understand.");
            return false;
        }

        if (!string.IsNullOrEmpty(profile.AppliedUtc))
        {
            BNL.Log($"[Tuning] '{FileName}' was already applied on {profile.AppliedUtc}; its settings are in config.xml. " +
                    "Delete the file, or clear its AppliedUtc, to apply it again.");
            return false;
        }

        string here = Fingerprint();
        if (!profile.ApplyToAnyMachine && !string.Equals(profile.Machine, here, StringComparison.OrdinalIgnoreCase))
        {
            BNL.LogWarning(
                $"[Tuning] '{FileName}' was measured on '{profile.Machine}' and this host is '{here}'. " +
                "Every setting in it is derived from the core count and kernel of the machine it was fitted on, so " +
                "it has NOT been applied. Re-run the benchmark here, or set <ApplyToAnyMachine>true</ApplyToAnyMachine> " +
                "in the file if the two hosts really are equivalent.");
            return false;
        }

        if (profile.Settings == null || profile.Settings.Count == 0)
        {
            BNL.Log($"[Tuning] '{FileName}' contains no settings - the benchmark found nothing worth changing.");
            profile.Stamp();
            profile.Save(path);
            return false;
        }

        BNL.Log($"[Tuning] Applying '{FileName}' (measured {profile.GeneratedUtc} on {profile.Machine}" +
                (profile.DesignPlayers > 0 ? $", fitted at {profile.DesignPlayers} players" : "") + ")");

        int applied = 0;
        foreach (BasisTunedSetting setting in profile.Settings)
        {
            if (setting == null || string.IsNullOrEmpty(setting.Name)) continue;

            object target = string.IsNullOrEmpty(setting.Stack)
                ? config
                : BasisTransportConfigStore.Get(setting.Stack);

            if (target == null)
            {
                BNL.LogWarning($"[Tuning]   {setting.Name}: no '{setting.Stack}' transport is registered; skipped.");
                continue;
            }

            if (TrySet(target, setting.Name, setting.Value, out string previous, out string failure))
            {
                string where = string.IsNullOrEmpty(setting.Stack) ? "config.xml" : setting.Stack + ".xml";
                BNL.Log($"[Tuning]   {setting.Name}: {previous} -> {setting.Value}  [{where}" +
                        (string.IsNullOrEmpty(setting.Evidence) ? "" : ", " + setting.Evidence) + "]");
                applied++;
            }
            else
            {
                BNL.LogWarning($"[Tuning]   {setting.Name}: {failure}");
            }
        }

        if (applied == 0)
        {
            BNL.LogWarning("[Tuning] Nothing could be applied; the config files are unchanged.");
            return false;
        }

        // Written into the config files, so from here on config.xml is authoritative again and an
        // operator editing it is not silently overridden on the next restart.
        try
        {
            config.SaveToXml(Path.Combine(configDir, "config.xml"));
        }
        catch (Exception ex)
        {
            BNL.LogError($"[Tuning] Applied {applied} setting(s) in memory but could not persist them: {ex.Message}. " +
                         "They are live for this run and the profile has NOT been stamped, so the next boot retries.");
            return true;
        }

        profile.Stamp();
        try { profile.Save(path); }
        catch (Exception ex) { BNL.LogWarning($"[Tuning] Could not stamp '{path}': {ex.Message}"); }

        BNL.Log($"[Tuning] {applied} setting(s) written into the config. config.xml is authoritative from here.");
        return true;
    }

    private void Stamp() => AppliedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

    /// <summary>
    /// Sets one public field by name, converting from the profile's string form.
    ///
    /// Deliberately the same conversion set the environment overrides accept, so a value that can
    /// be expressed one way can be expressed the other and neither route can reach a field the
    /// other cannot.
    /// </summary>
    private static bool TrySet(object target, string fieldName, string value, out string previous, out string failure)
    {
        previous = string.Empty;
        failure = string.Empty;

        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
        if (field == null)
        {
            failure = "no such setting in this build; skipped.";
            return false;
        }

        previous = field.GetValue(target)?.ToString() ?? string.Empty;

        try
        {
            if (field.FieldType == typeof(int))
            {
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)) { failure = $"'{value}' is not an integer."; return false; }
                field.SetValue(target, i);
            }
            else if (field.FieldType == typeof(ushort))
            {
                if (!ushort.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort u)) { failure = $"'{value}' is not a ushort."; return false; }
                field.SetValue(target, u);
            }
            else if (field.FieldType == typeof(byte))
            {
                if (!byte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte b)) { failure = $"'{value}' is not a byte."; return false; }
                field.SetValue(target, b);
            }
            else if (field.FieldType == typeof(long))
            {
                if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l)) { failure = $"'{value}' is not a long."; return false; }
                field.SetValue(target, l);
            }
            else if (field.FieldType == typeof(float))
            {
                if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) { failure = $"'{value}' is not a number."; return false; }
                field.SetValue(target, f);
            }
            else if (field.FieldType == typeof(double))
            {
                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) { failure = $"'{value}' is not a number."; return false; }
                field.SetValue(target, d);
            }
            else if (field.FieldType == typeof(bool))
            {
                if (!bool.TryParse(value, out bool bo)) { failure = $"'{value}' is not true or false."; return false; }
                field.SetValue(target, bo);
            }
            else if (field.FieldType == typeof(string))
            {
                field.SetValue(target, value);
            }
            else
            {
                failure = $"type {field.FieldType.Name} cannot be set from a profile.";
                return false;
            }
        }
        catch (Exception ex)
        {
            failure = ex.Message;
            return false;
        }

        return true;
    }
}
