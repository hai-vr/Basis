using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Everyone's saved camera modes, in one file next to the camera's own settings.
///
/// <para>Deliberately a flat list in a single file rather than a file per mode: the whole point of
/// a mode is that it sits in a dropdown beside four others, so the read that matters is "give me
/// all of them" and it happens the moment the panel opens. A directory of files would turn that
/// into a scan, and buy nothing — these are a handful of small records, not a library.</para>
///
/// <para>Writes are rare (a button press) and small, so they are synchronous and atomic. The panel
/// needs to know the write landed before it rebuilds the dropdown, and a mode list half-written by
/// a crash is worse than a save that took a millisecond.</para>
/// </summary>
public static class BasisCameraUserModes
{
    public const string CameraModesJson = "CameraModes.json";

    /// <summary>
    /// Enough that nobody sensible will meet it, low enough that a stuck save loop cannot grow the
    /// file without bound. Hitting it is reported rather than silently dropping the newest mode.
    /// </summary>
    public const int MaxModes = 64;

    [Serializable]
    private class ModeFile
    {
        public int version = 1;
        public List<BasisCameraUserMode> modes = new List<BasisCameraUserMode>();
    }

    private static List<BasisCameraUserMode> _modes;
    private static int _count;
    private static int _revision;

    /// <summary>Raised after the list changes, so an open panel can rebuild its dropdown.</summary>
    public static event Action OnChanged;

    /// <summary>Every saved mode, in the order they were first saved. Never null.</summary>
    public static IReadOnlyList<BasisCameraUserMode> Modes
    {
        get
        {
            EnsureLoaded();
            return _modes;
        }
    }

    /// <summary>
    /// How many modes there are, without touching the list. Read on the panel tick purely to
    /// decide whether there is anything to do, so it stays a field read even before the file has
    /// ever been opened.
    /// </summary>
    public static int Count
    {
        get
        {
            EnsureLoaded();
            return _count;
        }
    }

    /// <summary>
    /// Bumped by every change. A rebuild of the mode dropdown throws away the entries an open
    /// dropdown is showing, so the panel only wants to do it when something actually moved — and
    /// the count alone cannot tell it, since a rename or a colour change leaves the count still.
    /// </summary>
    public static int Revision => _revision;

    public static BasisCameraUserMode Find(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;

        EnsureLoaded();
        for (int Index = 0; Index < _modes.Count; Index++)
        {
            if (BasisCameraUserMode.NamesMatch(_modes[Index].name, name)) return _modes[Index];
        }

        return null;
    }

    public static bool Exists(string name) => Find(name) != null;

    /// <summary>
    /// Saves a mode, replacing any existing one of the same name. Overwriting rather than
    /// refusing is the point: "save" on a name you already have is how a mode gets updated after
    /// you have tweaked it, and demanding a delete first would make that the long way round.
    ///
    /// <para>Replacement keeps the mode's position in the list, so updating the one you use does
    /// not move it to the bottom of the dropdown you just picked it from.</para>
    /// </summary>
    /// <param name="error">Localization key describing why nothing was saved, or null on success.</param>
    public static bool Store(BasisCameraUserMode mode, out string error)
    {
        error = null;
        if (mode == null)
        {
            error = "camera.userMode.error.empty";
            return false;
        }

        string cleaned = BasisCameraUserMode.SanitizeName(mode.name);
        if (cleaned == null)
        {
            error = "camera.userMode.error.empty";
            return false;
        }

        mode.name = cleaned;
        if (mode.settings == null)
        {
            error = "camera.userMode.error.empty";
            return false;
        }

        EnsureLoaded();

        for (int Index = 0; Index < _modes.Count; Index++)
        {
            if (!BasisCameraUserMode.NamesMatch(_modes[Index].name, cleaned)) continue;

            _modes[Index] = mode;
            Save();
            return true;
        }

        if (_modes.Count >= MaxModes)
        {
            error = "camera.userMode.error.full";
            return false;
        }

        _modes.Add(mode);
        Save();
        return true;
    }

    public static bool Remove(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;

        EnsureLoaded();
        for (int Index = 0; Index < _modes.Count; Index++)
        {
            if (!BasisCameraUserMode.NamesMatch(_modes[Index].name, name)) continue;

            _modes.RemoveAt(Index);
            Save();
            return true;
        }

        return false;
    }

#if UNITY_INCLUDE_TESTS
    /// <summary>
    /// Where the modes file lives, when it is not where it lives. Tests must never open the real
    /// one: they save, clear and delete modes, and the file they would be doing that to is the
    /// player's own list of saved modes.
    /// </summary>
    public static string DirectoryOverrideForTest;

    private static string StorageDirectory =>
        string.IsNullOrEmpty(DirectoryOverrideForTest) ? Application.persistentDataPath : DirectoryOverrideForTest;
#else
    private static string StorageDirectory => Application.persistentDataPath;
#endif

    private static string FilePath => Path.Combine(StorageDirectory, CameraModesJson);

    private static void EnsureLoaded()
    {
        if (_modes != null) return;

        _modes = new List<BasisCameraUserMode>();
        LoadFromDisk();

        // Set here rather than inside the load, which returns from several places — and bumped
        // even for an empty file, since "not read yet" and "read, and there are none" are
        // different states to anything caching off the revision.
        _count = _modes.Count;
        _revision++;
    }

    private static void LoadFromDisk()
    {
        try
        {
            string path = FilePath;
            if (!File.Exists(path)) return;

            ModeFile file = JsonUtility.FromJson<ModeFile>(File.ReadAllText(path));
            if (file?.modes == null) return;

            for (int Index = 0; Index < file.modes.Count; Index++)
            {
                BasisCameraUserMode mode = file.modes[Index];

                // A record that lost its name or its settings cannot be selected, applied or
                // deleted from the panel — it would sit in the dropdown as a blank row that does
                // nothing. Dropping it on load is the only way it can ever leave the file.
                if (mode == null || mode.settings == null) continue;

                mode.name = BasisCameraUserMode.SanitizeName(mode.name);
                if (mode.name == null) continue;

                // The file is text on disk and can be hand-edited. A zero colour is transparent
                // black, which the section tint blends toward — an unusable mode would quietly
                // darken the whole page rather than colour it.
                if (mode.tint.a <= 0f) mode.tint = BasisCameraUserMode.DefaultTint;

                if (Exists(mode.name)) continue;

                _modes.Add(mode);
            }
        }
        catch (Exception ex)
        {
            // Keep whatever loaded. A mode list is a convenience, and losing it must not stop the
            // camera opening — but it is worth saying so, since the file will be overwritten by
            // the next save and this is the only warning that it is about to be.
            BasisDebug.LogError($"[BasisCameraUserModes] Failed to load saved modes: {ex.Message}");
        }
    }

    private static void Save()
    {
        _count = _modes.Count;
        _revision++;

        try
        {
            ModeFile file = new ModeFile();
            file.modes.AddRange(_modes);

            string path = FilePath;
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonUtility.ToJson(file, true));
            if (File.Exists(path))
            {
                File.Replace(temp, path, null);
            }
            else
            {
                File.Move(temp, path);
            }
        }
        catch (Exception ex)
        {
            BasisDebug.LogError($"[BasisCameraUserModes] Failed to save modes: {ex.Message}");
        }

        // Raised even where the write failed: the in-memory list has already changed, and an open
        // panel showing the old one would be lying about what picking a mode will now do.
        OnChanged?.Invoke();
    }

#if UNITY_INCLUDE_TESTS
    /// <summary>
    /// Drops the in-memory list so the next read comes off disk again. Tests own the file they
    /// point at; this is what lets one assert that a save actually reached it.
    /// </summary>
    public static void ResetCacheForTest() => _modes = null;

    /// <summary>Empties the list and the file, for a test that needs a known starting point.</summary>
    public static void ClearForTest()
    {
        EnsureLoaded();
        _modes.Clear();
        Save();
    }
#endif
}
