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

    // Runtime helper dictionary (not serialized directly)
    [NonSerialized]
    public Dictionary<string, string> settings = new Dictionary<string, string>();

    // Convert List -> Dictionary after loading
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

    // Convert Dictionary -> List before saving
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
    /// UniqueName,Optionvalue
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

        if (settingsData.settings.ContainsKey(uniqueSettingsName))
        {
            if (settingsData.settings[uniqueSettingsName] != value)
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

        // If we're in the middle of a load, defer the actual save.
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

        // record default but don't force a save if we're currently loading
        settingsData.settings[uniqueSettingsName] = defaultValue;
        SaveAllSettings();

        return defaultValue;
    }

    public static void LoadAllSettings()
    {
        if (!File.Exists(filePath))
        {
            BasisDebug.LogError("Settings file not found, creating defaults.");
            ResetToDefault_Internal(); // internal to avoid double locking
        }
        else
        {
            string json = File.ReadAllText(filePath);
            var loaded = JsonUtility.FromJson<SettingsData>(json);

            bool IsNull = loaded == null;
            if(IsNull)
            {
                BasisDebug.LogError("Settings version mismatch or corrupt file. Resetting.");
                ResetToDefault_Internal();
                return;
            }
            bool VersionDifference = loaded.version != currentVersion;
            if (VersionDifference)
            {
                BasisDebug.LogError("Loaded Version != Current Version Resetting");
                ResetToDefault_Internal();
                return;
            }
            settingsData = loaded;
            settingsData.RebuildDictionary();
        }

        // Fire notifications outside the lock (safe: read-only iteration on our dictionary snapshot)
        foreach (var kv in settingsData.settings)
        {
            OnSettingChanged?.Invoke(kv.Key, kv.Value);
        }

        OnSettingsFinishedChanges?.Invoke();
        SaveAllSettings();
    }

    public static void SaveAllSettings()
    {
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
    private static void ResetToDefault_Internal()
    {
        settingsData = new SettingsData { version = currentVersion };
        settingsData.RebuildDictionary();
    }
    public static int LoadInt(string key, int defaultValue = 0)
    {
        string val = LoadString(key, defaultValue.ToString(CultureInfo.InvariantCulture));
        return int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) ? result : defaultValue;
    }

    public static float LoadFloat(string key, float defaultValue = 0f)
    {
        string val = LoadString(key, defaultValue.ToString(CultureInfo.InvariantCulture));
        return float.TryParse(val, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out float result)  ? result : defaultValue;
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
