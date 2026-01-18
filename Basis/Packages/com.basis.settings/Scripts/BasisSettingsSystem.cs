using UnityEngine;
using UnityEngine.SceneManagement;
using System.IO;
using System.Collections.Generic;
using System;
using System.Globalization;

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

    public void RebuildDictionary()
    {
        settings.Clear();
        foreach (var kv in settingsList)
        {
            if (!string.IsNullOrEmpty(kv?.key) && !settings.ContainsKey(kv.key))
            {
                settings[kv.key] = kv.value;
            }
        }
    }

    public void RebuildList()
    {
        settingsList.Clear();
        foreach (var pair in settings)
        {
            settingsList.Add(new KeyValue { key = pair.Key, value = pair.Value });
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

    static BasisSettingsSystem()
    {
        LoadAllSettings();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (settingsData.settings.Count == 0)
        {
            BasisDebug.LogError("Loading Scene Before Settings Exist!");
        }

        foreach (var kv in settingsData.settings)
        {
            OnSettingChanged?.Invoke(kv.Key, kv.Value);
        }

        OnSettingsFinishedChanges?.Invoke();
    }

    public static void SaveString(string uniqueSettingsName, string value)
    {
        bool changed = false;

        if (settingsData.settings.TryGetValue(uniqueSettingsName, out var existing))
        {
            if (existing != value)
            {
                settingsData.settings[uniqueSettingsName] = value;
                changed = true;
            }
        }
        else
        {
            settingsData.settings[uniqueSettingsName] = value;
            changed = true;
        }

        if (changed)
        {
            SaveAllSettings();
            OnSettingChanged?.Invoke(uniqueSettingsName, value);
            OnSettingsFinishedChanges?.Invoke();
        }
    }

    public static string LoadString(string uniqueSettingsName, string defaultValue = "")
    {
        if (settingsData.settings.TryGetValue(uniqueSettingsName, out string value))
        {
            return value;
        }

        // Store default so future loads see the key.
        settingsData.settings[uniqueSettingsName] = defaultValue;
        SaveAllSettings();
        return defaultValue;
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
            return;
        }

        // IMPORTANT CHANGE:
        // Do NOT nuke user data just because version differs.
        // Assume existing values are valid, keep them, just rewrite the version.
        loaded.RebuildDictionary();

        settingsData = loaded;
        settingsData.version = currentVersion; // bump to current
        // (Dictionary already rebuilt; keep as-is.)

        // Notify listeners of everything we have
        foreach (var kv in settingsData.settings)
        {
            OnSettingChanged?.Invoke(kv.Key, kv.Value);
        }

        OnSettingsFinishedChanges?.Invoke();

        // Persist rewritten version + normalized list/dict
        SaveAllSettings();
    }

    public static void SaveAllSettings()
    {
        if (settingsData == null)
            settingsData = new SettingsData();

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
        string val = LoadString(key, defaultValue ? "true" : "false");
        return val == "true" || string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
    }

    public static void SaveInt(string key, int value) => SaveString(key, value.ToString(CultureInfo.InvariantCulture));
    public static void SaveFloat(string key, float value) => SaveString(key, value.ToString(CultureInfo.InvariantCulture));
    public static void SaveBool(string key, bool value) => SaveString(key, value ? "true" : "false");
}
