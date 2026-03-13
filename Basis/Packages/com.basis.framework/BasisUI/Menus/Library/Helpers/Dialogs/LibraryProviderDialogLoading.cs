
using System;
using System.Threading.Tasks;
using Basis.Scripts.UI.UI_Panels;
using UnityEngine;
using static Basis.BasisUI.LibraryProvider;

namespace Basis.BasisUI
{
    public class LibraryProviderDialogLoading
    {
        #region Loading Dialog

        public static async Task<bool> PromptUserLoadingInProgress(
            BasisMenuPanel panel,
            BasisDataStoreItemKeys.ItemKey item,
            BundledContentHolder.NetworkType networkType = BundledContentHolder.NetworkType.Local,
            bool persistence = false,
            bool modifyScale = false
        )
        {
            DialogBox<bool> contentLoadingDialogBox = DialogBox<bool>.Create(panel, new Vector2(830, 120),
                "Please wait",
                "Your content is currently loading standby...",
                AddressableAssets.Sprites.HourGlass,
                true
            );

            try
            {
                await LoadSelectedItem(item, networkType, persistence, modifyScale);
            }
            catch (Exception ex)
            {
                BasisDebug.LogError($"Content loading failed: {ex}");
            }

            // Close the loading dialog
            contentLoadingDialogBox.CloseWithResult(true);

            // If there was a reachability warning, show a notice to the user
            string warning = ContentLoader.LastAvatarReachabilityWarning;
            if (!string.IsNullOrEmpty(warning))
            {
                DialogBox<bool> noticeDialog = DialogBox<bool>.Create(panel, new Vector2(830, 200),
                    "Avatar File Unreachable",
                    warning + "\n\nThe avatar was still loaded, but the remote file could not be reached.",
                    AddressableAssets.Sprites.Information
                );

                PanelButton closeButton = PanelButton.CreateNew(PanelButton.ButtonStyles.AcceptButton, noticeDialog.Descriptor.ContentParent);
                closeButton.Descriptor.SetTitle("OK");
                closeButton.Descriptor.SetWidth(200);
                closeButton.Descriptor.SetHeight(60);
                closeButton.OnClicked += () => noticeDialog.CloseWithResult(true);

                await noticeDialog.WaitAsync();
            }

            return await contentLoadingDialogBox.WaitAsync();
        }

        #endregion
    }

}
