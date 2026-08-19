using System.Globalization;
using System.Xml.Linq;

namespace Basis.Benchmark.Harness;

/// <summary>
/// Edits the server's XML configs in place, and puts them back afterwards.
///
/// <para><b>Why patch rather than write a fresh file:</b> the configs carry per-setting doc
/// comments the server generates and heals, plus a version stamp that drives migrations. Writing a
/// replacement would either lose all of that or require this tool to reimplement the generator and
/// then drift from it. Setting element values inside the loaded document leaves everything else
/// exactly as the server wrote it.</para>
///
/// <para><b>Why patch files at all,</b> when the server already accepts environment overrides:
/// those only reach <c>config.xml</c>. The override walker recurses through public fields of the
/// <c>Configuration</c> object, and the transport settings do not live there — they are held in a
/// separate store keyed by stack id. Half of what needs sweeping (<c>MultiSocketCount</c>,
/// <c>MergeHoldMs</c>, <c>PeerUpdatePeersPerWorker</c>, the queue bounds) is therefore reachable
/// only through the file. Since <c>MultiSocketCount</c> is read once at socket bind and needs a
/// restart anyway, writing files costs nothing extra and keeps one mechanism instead of two.</para>
///
/// <para>Backups are taken once per session and restored on exit, including on Ctrl-C. An operator
/// who runs this against a live install must get their configuration back exactly as it was.</para>
/// </summary>
public sealed class ConfigPatcher : IDisposable
{
    private readonly Dictionary<string, string> _backups = new(StringComparer.OrdinalIgnoreCase);
    private bool _restored;

    public string ServerDirectory { get; }
    public string ConfigPath { get; }
    public string TransportConfigPath { get; }

    /// <summary>The load client's own config, when one was pointed at. Edited per run, so restored too.</summary>
    public string? LoadClientConfigPath { get; }

    public ConfigPatcher(string serverDirectory, string? loadClientDirectory = null)
    {
        ServerDirectory = serverDirectory;
        string configDir = Path.Combine(serverDirectory, "config");
        ConfigPath = Path.Combine(configDir, "config.xml");
        // The transport sidecar is named after the stack id, and litenetlib is the default stack.
        TransportConfigPath = Path.Combine(configDir, "transports", "litenetlib.xml");
        LoadClientConfigPath = loadClientDirectory == null ? null : Path.Combine(loadClientDirectory, "ClientSimConfig.xml");
    }

    /// <summary>True once both config files exist, which they do only after one server boot.</summary>
    public bool ConfigsExist => File.Exists(ConfigPath) && File.Exists(TransportConfigPath);

    /// <summary>
    /// Snapshots every file this tool edits so <see cref="Restore"/> can put them back byte for
    /// byte — the load client's included, since each run rewrites its player count and leaving it
    /// on whatever the last arm used would silently change the next thing the operator runs by
    /// hand.
    /// </summary>
    public void Backup()
    {
        foreach (string? path in new[] { ConfigPath, TransportConfigPath, LoadClientConfigPath })
        {
            if (path == null || !File.Exists(path) || _backups.ContainsKey(path)) continue;
            _backups[path] = File.ReadAllText(path);
        }
    }

    /// <summary>
    /// Puts both files back to the snapshot, and stays reusable.
    ///
    /// <para>Called before every arm, and that is not tidiness — it is what keeps the arms
    /// independent. <see cref="Apply"/> only writes the settings it is handed, so without a reset
    /// each arm inherits whatever the previous one left in the file. The sweep would then measure
    /// knob C on top of knob B's last candidate rather than on top of the baseline, and read the
    /// incumbent value for the next knob off a file the last arm had already edited.</para>
    /// </summary>
    public void ResetToBackup()
    {
        foreach ((string path, string content) in _backups)
        {
            try { File.WriteAllText(path, content); }
            catch (Exception ex) { Console.Error.WriteLine($"  ! could not reset {path}: {ex.Message}"); }
        }
    }

    /// <summary>Final restore on the way out. Idempotent, so the Ctrl-C path and Dispose can both call it.</summary>
    public void Restore()
    {
        if (_restored) return;
        _restored = true;
        ResetToBackup();
    }

    /// <summary>
    /// Keeps whatever is currently on disc instead of restoring it.
    ///
    /// Called once, after the recommended settings have been written. Without it the dispose-time
    /// restore at the end of the run would put the operator's old config straight back and the
    /// tool would report success having changed nothing.
    /// </summary>
    public void KeepChanges() => _restored = true;

    /// <summary>Applies a set of setting values, routing each to whichever file declares it.</summary>
    public void Apply(IReadOnlyDictionary<string, string> settings)
    {
        var forServer = new Dictionary<string, string>();
        var forTransport = new Dictionary<string, string>();

        foreach ((string name, string value) in settings)
        {
            if (Tuning.KnobCatalog.IsTransportSetting(name)) forTransport[name] = value;
            else forServer[name] = value;
        }

        if (forServer.Count > 0) PatchFile(ConfigPath, forServer);
        if (forTransport.Count > 0) PatchFile(TransportConfigPath, forTransport);
    }

    /// <summary>Reads a setting's current value from whichever file holds it, or null.</summary>
    public string? Read(string name)
    {
        string path = Tuning.KnobCatalog.IsTransportSetting(name) ? TransportConfigPath : ConfigPath;
        try
        {
            if (!File.Exists(path)) return null;
            XDocument doc = XDocument.Load(path, LoadOptions.PreserveWhitespace);
            return doc.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim();
        }
        catch { return null; }
    }

    private static void PatchFile(string path, IReadOnlyDictionary<string, string> settings)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Config not found: {path}. Start the server once so it writes its defaults.", path);

        XDocument doc = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        XElement root = doc.Root ?? throw new InvalidDataException($"{path} has no root element.");

        foreach ((string name, string value) in settings)
        {
            XElement? element = root.Elements().FirstOrDefault(e => e.Name.LocalName == name);
            if (element != null)
            {
                element.Value = value;
            }
            else
            {
                // A setting the running build does not know about. Adding it is harmless - the
                // deserialiser ignores unknown elements - but it means the sweep is not actually
                // changing anything, so say so rather than reporting a silent null result.
                Console.Error.WriteLine($"  ! {Path.GetFileName(path)} has no <{name}>; this build may predate the setting.");
                root.Add(new XElement(name, value));
            }
        }

        // Written through a temp file for the same reason the server does it: an interrupted write
        // leaves an operator with a truncated config and a server that will not boot.
        string temp = path + ".benchtmp";
        doc.Save(temp);
        File.Move(temp, path, overwrite: true);
    }

    public static string Format(object value) => value switch
    {
        bool b => b ? "true" : "false",
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    public void Dispose() => Restore();
}
