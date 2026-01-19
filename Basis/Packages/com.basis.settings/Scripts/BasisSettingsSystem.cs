using UnityEngine;
using UnityEngine.SceneManagement;
using System.IO;
using System.Collections.Generic;
using System;
using System.Globalization;
using System.Linq;

[Serializable]
public class KeyValue
{
    public string key;
    public string value;
}

[Serializable]
public class SettingsData
{
    public string version;
    public List<KeyValue> settingsList = new List<KeyValue>();

    [NonSerialized]
    public Dictionary<string, string> settings = new Dictionary<string, string>();

    private static string NormalizeKey(string k)
    {
        return string.IsNullOrEmpty(k) ? "" : k.Trim().ToLowerInvariant();
    }

    private static string NormalizeValue(string v)
    {
        return v == null ? "" : v.Trim().ToLowerInvariant();
    }

    public void RebuildDictionary()
    {
        settings.Clear();

        if (settingsList == null)
            settingsList = new List<KeyValue>();

        foreach (var kv in settingsList)
        {
            if (kv == null) continue;

            string k = NormalizeKey(kv.key);
            if (string.IsNullOrEmpty(k)) continue;

            string v = NormalizeValue(kv.value);

            // "latest wins" for duplicates after normalization
            settings[k] = v;
        }
    }

    public void RebuildList()
    {
        if (settingsList == null)
            settingsList = new List<KeyValue>();

        settingsList.Clear();

        if (settings == null)
            settings = new Dictionary<string, string>();

        foreach (var pair in settings)
        {
            settingsList.Add(new KeyValue
            {
                key = NormalizeKey(pair.Key),
                value = NormalizeValue(pair.Value)
            });
        }
    }
}

public static class BasisSettingsSystem
{
    public const string SettingsJson = "settingsConfig.json";
    private static readonly string filePath = Path.Combine(Application.persistentDataPath, SettingsJson);
    private static readonly string currentVersion = "2.0.5";
    private static SettingsData settingsData = new SettingsData();

    /// <summary>
    /// UniqueName, OptionValue
    /// </summary>
    public static event Action<string, string> OnSettingChanged;
    public static event Action OnSettingsFinishedChanges;

    // --- normalization helpers (ALWAYS lower for both key and value) ---
    private static string NormalizeKey(string key)
    {
        return string.IsNullOrEmpty(key) ? "" : key.Trim().ToLowerInvariant();
    }

    private static string NormalizeValue(string value)
    {
        return value == null ? "" : value.Trim().ToLowerInvariant();
    }

    static BasisSettingsSystem()
    {
        LoadAllSettings();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (settingsData.settings == null || settingsData.settings.Count == 0)
        {
            BasisDebug.LogError("Loading Scene Before Settings Exist!");
        }

        if (settingsData.settings != null)
        {
            foreach (var kv in settingsData.settings)
            {
                // already normalized
                OnSettingChanged?.Invoke(kv.Key, kv.Value);
            }
        }

        OnSettingsFinishedChanges?.Invoke();
        ForceQualityRefresh();
    }
    public static void ForceQualityRefresh()
    {
        QualitySettings.SetQualityLevel(QualitySettings.GetQualityLevel(), true);
    }
    public static void SaveString(string uniqueSettingsName, string value)
    {
        string key = NormalizeKey(uniqueSettingsName);
        string val = NormalizeValue(value);

        if (string.IsNullOrEmpty(key))
            return;

        if (settingsData == null)
            settingsData = new SettingsData { version = currentVersion };

        if (settingsData.settings == null)
            settingsData.settings = new Dictionary<string, string>();

        bool changed = false;

        if (settingsData.settings.TryGetValue(key, out var existing))
        {
            // existing is already normalized
            if (existing != val)
            {
                settingsData.settings[key] = val;
                changed = true;
            }
        }
        else
        {
            settingsData.settings[key] = val;
            changed = true;
        }

        if (changed)
        {
            SaveAllSettings();
            OnSettingChanged?.Invoke(key, val);
            OnSettingsFinishedChanges?.Invoke();
            ForceQualityRefresh();
        }
    }

    public static string LoadString(string uniqueSettingsName, string defaultValue = "")
    {
        string key = NormalizeKey(uniqueSettingsName);
        string def = NormalizeValue(defaultValue);

        if (string.IsNullOrEmpty(key))
            return def;

        if (settingsData == null)
            settingsData = new SettingsData { version = currentVersion };

        if (settingsData.settings == null)
            settingsData.settings = new Dictionary<string, string>();

        if (settingsData.settings.TryGetValue(key, out string value))
        {
            // value should already be normalized, but normalize anyway for safety
            return NormalizeValue(value);
        }

        // Store default so future loads see the key (normalized)
        settingsData.settings[key] = def;
        SaveAllSettings();
        return def;
    }

    public static void LoadAllSettings()
    {
        // Default blank (will fill from file or remain empty)
        settingsData = new SettingsData { version = currentVersion };
        settingsData.RebuildDictionary();

        if (!File.Exists(filePath))
        {
            // First run: no file yet. Just create an empty file at current version.
            BasisDebug.LogError("Settings file not found, creating new settings file.");
            SaveAllSettings();

            // Fire notifications (none yet unless defaults were created through LoadString later)
            OnSettingsFinishedChanges?.Invoke();
            ForceQualityRefresh();
            return;
        }

        string json = null;
        SettingsData loaded = null;

        try
        {
            json = File.ReadAllText(filePath);
            loaded = JsonUtility.FromJson<SettingsData>(json);
        }
        catch (Exception e)
        {
            BasisDebug.LogError($"Failed to read/parse settings file. Creating a fresh one. Exception: {e}");
            // If parsing failed, we fall through to writing a fresh file.
        }

        if (loaded == null)
        {
            // Corrupt or unreadable file. OPTIONAL: backup the bad file for debugging.
            try
            {
                string backupPath = filePath + ".corrupt_backup";
                File.Copy(filePath, backupPath, true);
            }
            catch { /* ignore backup failures */ }

            BasisDebug.LogError("Settings file corrupt/unreadable. Rebuilding empty settings.");
            settingsData = new SettingsData { version = currentVersion };
            settingsData.RebuildDictionary();

            SaveAllSettings();
            OnSettingsFinishedChanges?.Invoke();
            ForceQualityRefresh();
            return;
        }

        // Rebuild dictionary WITH normalization
        loaded.RebuildDictionary();

        // Ensure we never carry non-normalized data
        if (loaded.settings == null)
            loaded.settings = new Dictionary<string, string>();

        // Assign and bump version
        settingsData = loaded;
        settingsData.version = currentVersion;

        // Notify listeners of everything we have (already normalized)
        var settings = settingsData.settings;
        KeyValuePair<string, string>[] array = settings.ToArray();
        foreach (KeyValuePair<string, string> kv in array)
        {
            OnSettingChanged?.Invoke(kv.Key, kv.Value);
        }

        OnSettingsFinishedChanges?.Invoke();
        // Persist rewritten version + normalized list/dict
        SaveAllSettings();
        ForceQualityRefresh();
    }

    public static void SaveAllSettings()
    {
        if (settingsData == null)
            settingsData = new SettingsData();

        if (settingsData.settings == null)
            settingsData.settings = new Dictionary<string, string>();

        // Hard-normalize entire dictionary before writing (belt + suspenders)
        var normalized = new Dictionary<string, string>();
        foreach (var pair in settingsData.settings)
        {
            string k = NormalizeKey(pair.Key);
            if (string.IsNullOrEmpty(k)) continue;

            string v = NormalizeValue(pair.Value);
            normalized[k] = v; // latest wins
        }
        settingsData.settings = normalized;

        settingsData.version = currentVersion;
        settingsData.RebuildList();

        string json = JsonUtility.ToJson(settingsData, true);

        string dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(filePath, json);
    }

    public static int LoadInt(string key, int defaultValue = 0)
    {
        // default is numeric, ToLowerInvariant doesn't change it
        string val = LoadString(key, defaultValue.ToString(CultureInfo.InvariantCulture));
        return int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
            ? result
            : defaultValue;
    }

    public static float LoadFloat(string key, float defaultValue = 0f)
    {
        string val = LoadString(key, defaultValue.ToString(CultureInfo.InvariantCulture));
        return float.TryParse(val, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out float result)
            ? result
            : defaultValue;
    }

    public static bool LoadBool(string key, bool defaultValue = false)
    {
        // stored as "true"/"false" (lowercase) always
        string val = LoadString(key, defaultValue ? "true" : "false");
        return val == "true";
    }

    public static void SaveInt(string key, int value)
        => SaveString(key, value.ToString(CultureInfo.InvariantCulture));

    public static void SaveFloat(string key, float value)
        => SaveString(key, value.ToString(CultureInfo.InvariantCulture));

    public static void SaveBool(string key, bool value)
        => SaveString(key, value ? "true" : "false");
}
