using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Shared defaults for the "set a default name / description" validator fixes on avatars, props and
/// scenes, so all three produce the same quality of starting text.
/// </summary>
public static class BasisContentDefaults
{
    private static readonly Regex DuplicateSuffix = new Regex(@"\s*\((?:Clone|\d+)\)\s*$", RegexOptions.IgnoreCase);
    private static readonly Regex Separators = new Regex(@"[_\-\.]+");
    private static readonly Regex CamelBoundary = new Regex(@"(?<=[a-z0-9])(?=[A-Z])");
    private static readonly Regex AcronymBoundary = new Regex(@"(?<=[A-Z])(?=[A-Z][a-z])");
    private static readonly Regex Whitespace = new Regex(@"\s+");

    /// <summary>
    /// Produces a readable display name for a piece of content. Prefers the prefab asset's name over
    /// the scene instance's, since the instance is the one that picks up duplicate suffixes, then
    /// turns separator- or camel-cased identifiers into spaced words.
    /// </summary>
    /// <param name="source">The content's GameObject.</param>
    /// <param name="fallback">Returned when nothing usable survives cleaning.</param>
    public static string ResolveName(GameObject source, string fallback)
    {
        if (source == null) return fallback;

        string name = source.name;

        string prefabPath = string.Empty;
        if (PrefabUtility.IsPartOfPrefabInstance(source))
        {
            prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(source);
        }
        else if (PrefabUtility.IsPartOfPrefabAsset(source))
        {
            prefabPath = AssetDatabase.GetAssetPath(source);
        }
        if (!string.IsNullOrEmpty(prefabPath))
        {
            name = Path.GetFileNameWithoutExtension(prefabPath);
        }

        name = Clean(name);
        return string.IsNullOrEmpty(name) ? fallback : name;
    }

    private static string Clean(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;

        name = DuplicateSuffix.Replace(name, string.Empty);

        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, ' ');
        }

        name = Separators.Replace(name, " ");
        name = CamelBoundary.Replace(name, " ");
        name = AcronymBoundary.Replace(name, " ");
        name = Whitespace.Replace(name, " ").Trim();

        if (name.Length == 0) return string.Empty;

        // Capitalise words the author left lowercase, but leave anything they cased themselves
        // (acronyms, brand names) exactly as written.
        string[] words = name.Split(' ');
        for (int Index = 0; Index < words.Length; Index++)
        {
            if (words[Index].Length > 0 && char.IsLower(words[Index][0]))
            {
                words[Index] = char.ToUpperInvariant(words[Index][0]) + words[Index].Substring(1);
            }
        }
        return string.Join(" ", words);
    }

    /// <summary>
    /// Pushes a value the validator just wrote onto the model into the matching inspector field, so
    /// the fix is visible instead of only taking effect behind the UI.
    /// </summary>
    public static void SyncField(VisualElement root, string fieldName, string value)
    {
        root?.Q<TextField>(fieldName)?.SetValueWithoutNotify(value);
    }
}
