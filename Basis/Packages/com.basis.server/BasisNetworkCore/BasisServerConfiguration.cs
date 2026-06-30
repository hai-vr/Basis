using Basis.Network.Core;
using BasisNetworkCore.Security;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Serialization;

[Serializable]
public class Configuration
{
    public const string ConfigFolderName = "config";
    public const string LogsFolderName = "logs";
    public const string InitialResourcesFolderName = "initialresources";
    public const string DefaultLibraryFolderName = "defaultlibrary";

    /// <summary>
    /// Bump when config changes should force existing files to be rewritten (e.g. to refresh
    /// doc comments). Newly-added settings are healed automatically regardless: on load a
    /// config missing any current field is re-saved with the new settings added.
    /// </summary>
    public const int CurrentConfigVersion = 3;
    /// <summary>Schema version stamped into config.xml; 0 = a pre-versioning file that is upgraded on load.</summary>
    public int ConfigVersion = 0;

    public int PeerLimit = ushort.MaxValue;
    public ushort SetPort = 4296;
    /// <summary>Display name returned by the unconnected server-info query — what shows up as the row title in a client server-list UI.</summary>
    public string ServerName = "Basis Server";
    /// <summary>Short MOTD returned alongside the server name in the info query response. Two short lines render cleanly in the list UI.</summary>
    public string ServerMotd = "";
    public bool EnableStatistics = true;
    public bool HasFileSupport = true;
    public string HealthCheckHost = "localhost";
    public ushort HealthCheckPort = 10666;
    public string HealthPath = "/health";
    public int BSRSMillisecondDefaultInterval = 50;
    public int BSRBaseMultiplier = 1;
    public float BSRSIncreaseRate = 0.005f;
    public float BSRSlowestSendRate = 2.55f;
    public float HighQualityDistance = 10f;
    public float MediumQualityDistance = 20f;
    public float LowQualityDistance = 40f;
    public bool OverrideAutoDiscoveryOfIpv = false;
    public string IPv4Address = "0.0.0.0";
    public string IPv6Address = "::";
    public string Password = "default_password";
    public bool UseAuth = true;
    public bool UseAuthIdentity = true;
    public string NetworkStackId = "";
    public BasisUserRestrictionMode BasisUserRestrictionMode;
    public int HowManyDuplicateAuthCanExist = 2;
    public int AuthValidationTimeOutMiliseconds = 9000;
    public bool EnableConsole = true;
    public bool DisableWriteUnlessAdminPersistentFlag = true;
    public bool DisableReadUnlessAdminPersistentFlag = false;
    /// <summary>
    /// When true, the avatar reduction system bundles per-receiver avatar messages
    /// and emits them deflated on CompressedAvatarBundleChannel. Falls back to
    /// per-message uncompressed sends when a receiver has too few queued messages
    /// for compression to be worthwhile, or when the compressed result would
    /// exceed peer MTU. Clients must implement the matching decoder.
    /// </summary>
    public bool EnableAvatarBundleCompression = true;
    /// <summary>Minimum queued avatar messages to a single receiver before a bundle is even attempted.</summary>
    public int AvatarBundleMinMessages = 4;
    /// <summary>Minimum uncompressed bundle bytes before LZ4 compression is attempted. With LZ4 having near-zero per-call setup, 128 just guards the very smallest cases where LZ4 can't find any redundancy.</summary>
    public int AvatarBundleMinBytes = 128;
    public bool EnableBSRProfiling = false;
    public bool DisallowHeadless = false;

    // Global lockout defaults applied at server boot. Users need the matching
    // basis.resource.lockbypass.{avatar,prop,world} permission to load while locked.
    public bool AvatarsLocked = false;
    public bool PropsLocked = false;
    public bool WorldsLocked = true;
    /// <summary>
    /// When true, peers may not share saved-server entries through the content
    /// share system. Toggled live via the admin panel and persisted to config.xml
    /// alongside the other content lockouts. Default off so existing deployments
    /// behave as before.
    /// </summary>
    public bool ServersLocked = false;
    /// <summary>
    /// When true, the server tells every client to hard-disable the desktop third-person
    /// camera. Toggled live via the admin panel and persisted to config.xml alongside the
    /// other content lockouts. Default off so existing deployments behave as before.
    /// </summary>
    public bool ThirdPersonDisabled = false;
    /// <summary>
    /// When true, the server strips AdditionalAvatarDatas (blendshapes, custom-behaviour
    /// params) from every inbound avatar sync message before propagating to other peers.
    /// Muscle/position/rotation still sync normally; only the additional-data payload is
    /// dropped. Toggled live via the admin panel and persisted alongside the other
    /// content lockouts. Default off.
    /// </summary>
    public bool AdditionalAvatarDataLock = false;
    /// <summary>
    /// Per-category bitmask of camera photo-metadata embedding categories disallowed for all
    /// clients. 0 = everything allowed (default). Seeds BasisGlobalLockManager at boot and is
    /// broadcast to clients in GlobalGetLockState.
    /// </summary>
    public byte CameraMetadataDisallowMask = 0;
    public bool CrashReportingEnabled = true;
    public float MaxMicrophoneRangeMeters = 25f;
    public float MaxHearingRangeMeters = 25f;
    public float MinAvatarEyeHeightMeters = 0.1f;
    public float MaxAvatarEyeHeightMeters = 100f;
    public int MaxDatabaseEntries = 10000;
    public int MaxDatabaseNameLength = 256;
    public int MaxDatabasePayloadEntries = 1000;
    public int MaxContentSpheresPerPlayer = 32;
    public bool PlayspaceMoverLocked = false;
    public bool DirectConnectLocked = false;
    /// <summary>
    /// When true, every client blocks sandboxed Cilbox code on avatars from running; props and
    /// worlds keep their own. Seeds BasisGlobalLockManager at boot and can be toggled live from the
    /// admin panel; the state is broadcast to clients in GlobalGetLockState. Default off.
    /// </summary>
    public bool CilboxLocked = false;
    /// <summary>
    /// When true, non-bypass clients cannot share new image pickups and won't accept inbound ones.
    /// Enforced client-side — image pickups ride the generic scene relay, so the server can't single
    /// them out the way it blocks content shares. Seeds BasisGlobalLockManager at boot, can be toggled
    /// live from the admin panel, and is broadcast to clients in GlobalGetLockState. Default off.
    /// </summary>
    public bool ImagesLocked = false;

    // ── REST API ──────────────────────────────────────────────────────────────
    /// <summary>Set to true to enable the REST management API.</summary>
    public bool ApiEnabled = false;
    public string ApiHost = "localhost";
    public ushort ApiPort = 10667;
    /// <summary>Bearer token required on every API request. Empty string disables the API even if ApiEnabled is true.</summary>
    public string ApiKey = "";
    /// <summary>
    /// Read config from file. If no file is found create a default config file at filePath.
    /// Also loads per-transport config sidecars from <c>{configDir}/transports/{stackId}.xml</c>.
    /// </summary>
    public static Configuration LoadFromXml(string filePath)
    {
        RuntimeHelpers.RunClassConstructor(typeof(BasisNetworkStackRegistry).TypeHandle);

        Configuration result;
        var serializer = new XmlSerializer(typeof(Configuration));
        if (File.Exists(filePath))
        {
            using (var fileReader = new StreamReader(filePath))
            {
                result = (Configuration)serializer.Deserialize(fileReader);
            }

            // Heal an older config: if it predates the current schema version or is missing
            // any setting we now write, re-save it so the new settings (with defaults and
            // doc comments) are added without disturbing the values already present.
            if (BasisConfigXmlDocs.NeedsUpgrade(filePath, typeof(Configuration), result))
            {
                BNL.Log($"{filePath} is from an older version; adding missing settings.");
                result.WriteXml(filePath);
            }
        }
        else
        {
            BNL.Log($"{filePath} not found, creating with default values");
            result = new Configuration();
            result.WriteXml(filePath);
        }

        string configDir = Path.GetDirectoryName(filePath);
        BasisTransportConfigStore.LoadAll(configDir);
        return result;
    }

    /// <summary>
    /// Persist this configuration back to <paramref name="filePath"/>. Used by the
    /// admin panel to make in-game changes (server name, MOTD, allowlist mode)
    /// survive a restart. Writes via a sibling temp file + atomic move so a crash
    /// mid-write doesn't corrupt the live config.
    /// </summary>
    public void SaveToXml(string filePath)
    {
        WriteXml(filePath);
        BasisTransportConfigStore.SaveAll(Path.GetDirectoryName(filePath));
    }

    /// <summary>
    /// Atomically write just this config.xml (temp file + replace), stamping the current
    /// schema version and injecting doc comments. Does not touch the transport sidecars.
    /// </summary>
    private void WriteXml(string filePath)
    {
        ConfigVersion = CurrentConfigVersion;
        var serializer = new XmlSerializer(typeof(Configuration));
        string dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        string tempPath = filePath + ".tmp";
        using (var writer = new StreamWriter(tempPath))
        {
            BasisConfigXmlDocs.Serialize(serializer, typeof(Configuration), this, writer);
        }
        if (File.Exists(filePath)) File.Replace(tempPath, filePath, null);
        else File.Move(tempPath, filePath);
    }

    /// <summary>
    /// Resolve the canonical config.xml path under <c>{BaseDirectory}/{ConfigFolderName}/config.xml</c>
    /// — same path the bootstrappers (BasisServerConsole.Program / Unity host runner) read on startup.
    /// </summary>
    public static string GetDefaultPath()
    {
        return Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, ConfigFolderName, "config.xml");
    }

    /// <summary>
    /// This code will override what is written in the config.xml if it finds
    /// an Environmental Variable with the same name as a public config field.
    ///
    /// On windows you can test this in the console:
    ///    $env:PeerLimit = "256"
    ///   .\BasisNetworkConsole.exe
    /// But it is intended to allow Linux admins to override defaults during launch.
    /// </summary>
    public void ProcessEnvironmentalOverrides()
    {
        ApplyEnvironmentalOverridesTo(this);
    }

    private static void ApplyEnvironmentalOverridesTo(object target)
    {
        if (target == null) return;
        Type type = target.GetType();
        FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
        foreach (var field in fields)
        {
            if (!field.FieldType.IsPrimitive && field.FieldType != typeof(string) && field.FieldType.IsClass)
            {
                object nested = field.GetValue(target);
                if (nested != null) ApplyEnvironmentalOverridesTo(nested);
                continue;
            }

            string value = Environment.GetEnvironmentVariable(field.Name);
            if (value == null) continue;

            BNL.Log($"Applying Environmental Override with Field:{field.Name} Value:{value}");

            if (field.FieldType == typeof(int))
            {
                if (int.TryParse(value, out int number)) field.SetValue(target, number);
                else BNL.LogWarning("Could not cast to int. Failed Override");
            }
            else if (field.FieldType == typeof(ushort))
            {
                if (ushort.TryParse(value, out ushort number)) field.SetValue(target, number);
                else BNL.LogWarning("Could not cast to ushort. Failed Override.");
            }
            else if (field.FieldType == typeof(float))
            {
                if (float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float number)) field.SetValue(target, number);
                else BNL.LogWarning("Could not cast to float. Failed Override.");
            }
            else if (field.FieldType == typeof(string))
            {
                field.SetValue(target, value);
            }
            else if (field.FieldType == typeof(bool))
            {
                if (bool.TryParse(value, out bool boolResult)) field.SetValue(target, boolResult);
                else BNL.LogWarning($"Could not parse '{value}' as bool for field {field.Name}. Failed Override");
            }
            else
            {
                BNL.LogWarning($"Environmental variable type could not be processed for Config Field:{field.Name} Value:{value}");
            }
        }
    }
}
