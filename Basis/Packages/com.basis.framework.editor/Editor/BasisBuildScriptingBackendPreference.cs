#if UNITY_EDITOR
using UnityEditor;

public static class BasisBuildScriptingBackendPreference
{
    public enum Mode
    {
        Ask = 0,
        IL2CPP = 1,
        Mono = 2,
    }

    private const string PrefKey = "Basis_Build_ScriptingBackend_Mode";

    public static Mode Current
    {
        get => (Mode)EditorPrefs.GetInt(PrefKey, (int)Mode.Ask);
        set => EditorPrefs.SetInt(PrefKey, (int)value);
    }
}
#endif
