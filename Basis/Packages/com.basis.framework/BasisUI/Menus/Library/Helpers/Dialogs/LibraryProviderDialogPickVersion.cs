using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.UI.UI_Panels;
using UnityEngine;

namespace Basis.BasisUI
{
    public class LibraryProviderDialogPickVersion
    {
        #region PromptUserToPickVersion

        /// <summary>
        /// Shows the stacked versions of one piece of content (newest first) and resolves with the
        /// version the user picked, or null when dismissed. The caller then opens the normal item
        /// overlay for the picked version.
        /// </summary>
        public static async Task<BasisDataStoreItemKeys.ItemKey> PromptUserToPickVersion(BasisMenuPanel panel, List<BasisDataStoreItemKeys.ItemKey> stack)
        {
            BasisDataStoreItemKeys.ItemKey face = stack[0];
            CachedMetaData.TryGetMeta(face.Url ?? string.Empty, out CachedMetaData.CachedContent faceMeta);
            string displayName = LibraryProviderStrUtil.TitleToCase(!string.IsNullOrEmpty(faceMeta?.Name) ? faceMeta.Name : face.Url);

            DialogBox<BasisDataStoreItemKeys.ItemKey> dialog = DialogBox<BasisDataStoreItemKeys.ItemKey>.Create(panel, new Vector2(1000, 800),
                displayName,
                BasisLocalization.Get("library.dialog.pickVersion.body"),
                AddressableAssets.Sprites.List);

            PanelButton exitButton = PanelButton.CreateNew(PanelButton.ButtonStyles.ExitButton, dialog.Descriptor.Header);
            exitButton.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 125);
            exitButton.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 50);
            exitButton.OnClicked += () => dialog.Cancel(null);

            PanelTabPage scrollablePage = PanelTabPage.CreateNew(dialog.Descriptor.ContentParent);
            PanelElementDescriptor scrollDescriptor = PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.ScrollViewVerticalLibraryParentContentSize, scrollablePage.Descriptor.ContentParent);
            scrollablePage.Descriptor.ContentParent = scrollDescriptor.ContentParent;

            for (int Index = 0; Index < stack.Count; Index++)
            {
                BasisDataStoreItemKeys.ItemKey version = stack[Index];
                CachedMetaData.TryGetMeta(version.Url ?? string.Empty, out CachedMetaData.CachedContent meta);

                PanelButton row = PanelButton.CreateNew(PanelButton.ButtonStyles.Default, scrollablePage.Descriptor.ContentParent);
                var rowDescriptor = row.Descriptor;

                string dateText = meta != null && meta.Created.HasValue
                    ? meta.Created.Value.ToString(CultureInfo.InvariantCulture) + " UTC"
                    : null;

                // Content built before the connector carried a creation date has none — and that is
                // exactly the older content grouped by NAME, where the version lives in the name and
                // nowhere else. Labelling those rows by date alone made every one of them read "not
                // available", leaving the raw url as the only way to tell the versions apart.
                string versionName = string.IsNullOrWhiteSpace(meta?.Name)
                    ? null
                    : LibraryProviderStrUtil.TitleToCase(meta.Name);

                string label;
                if (versionName != null && dateText != null)
                {
                    label = $"{versionName} — {dateText}";
                }
                else
                {
                    label = versionName ?? dateText ?? BasisLocalization.Get("library.notAvailable");
                }

                rowDescriptor.SetTitle(Index == 0
                    ? string.Format(BasisLocalization.Get("library.stack.latestEntry"), label)
                    : label);
                rowDescriptor.SetDescription(version.Url ?? string.Empty);
                rowDescriptor.SetHeight(100);
                rowDescriptor.SetWidth(880);

                Sprite thumbnail = meta != null ? CachedMetaData.CreateSpriteFromMetaData(meta) : null;
                if (thumbnail != null)
                {
                    row.SetIcon(thumbnail, false);
                }

                bool isActive;
                switch (version.Mode)
                {
                    case BundledContentHolder.Mode.Avatar:
                        isActive = version.Url == BasisLocalPlayer.Instance.AvatarMetaData.BasisRemoteBundleEncrypted.RemoteBeeFileLocation;
                        break;
                    default:
                        isActive = BasisRuntimeSpawnRegistry.CountIgnoreCase(version.Url) > 0;
                        break;
                }
                row.ButtonStyling.ShowIndicator(isActive);

                row.OnClicked += () =>
                {
                    if (dialog.IsBusy) return;
                    dialog.IsBusy = true;

                    dialog.CloseWithResult(version);
                };
            }

            dialog.Descriptor.ForceRebuild();

            return await dialog.WaitAsync();
        }

        #endregion
    }
}
