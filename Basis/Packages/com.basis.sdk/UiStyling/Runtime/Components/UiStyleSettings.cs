using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using static UnityEngine.Rendering.VirtualTexturing.Debugging;

namespace Basis.BasisUI.Styling
{
    public static class UiStyleSettings
    {
        // Path inside Resources (no .asset extension)
        //Packages/com.basis.sdk/UiStyling/Style Library.asset

        public static UiStyleLibrary Library;
        public static UiStylePalette Palette;
        public static UiStyleLibrary GetActiveStyles()
        {
            if (Library == null)
            {
                var Data = Addressables.LoadAssetAsync<UiStyleLibrary>("StyleLibrary");
                Library = Data.WaitForCompletion();
            }
            if (Library == null)
            {
                BasisDebug.LogError("Misssing Library!");
            }
            return Library;

        }

        public static UiStylePalette GetActivePalette()
        {
            if (Palette == null)
            {
                var Data = Addressables.LoadAssetAsync<UiStylePalette>("StylePalette");
                Palette = Data.WaitForCompletion();
            }
            if (Palette == null)
            {
                BasisDebug.LogError("Misssing Palette!");
            }
            return Palette;
        }

        public static void SetActiveStyles(UiStyleLibrary library)
        {
            Library = library;
            UpdateAllStyleComponents();

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(Library);
#endif
        }

        public static void SetActivePalette(UiStylePalette palette)
        {
            Palette = palette;
            UpdateAllStyleComponents();

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(Palette);
#endif
        }

        public static void UpdateAllStyleComponents()
        {
            // This works at runtime too (2022+). If you're on older Unity, switch to Object.FindObjectsOfType<BaseUiStyleComponent>()
            BaseUiStyleComponent[] components =
                Object.FindObjectsByType<BaseUiStyleComponent>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);

            foreach (BaseUiStyleComponent comp in components)
            {
                if (!comp || !comp.enabled) continue;

                UiStyleUtilities.RecordComponent(comp); // assuming this is runtime-safe
                comp.ApplyActiveStyle();
            }
        }
    }
}
