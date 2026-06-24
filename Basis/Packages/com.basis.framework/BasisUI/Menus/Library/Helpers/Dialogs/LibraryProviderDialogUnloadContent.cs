using System.Threading.Tasks;
using Basis.Scripts.UI.UI_Panels;
using UnityEngine;

namespace Basis.BasisUI
{
    public class LibraryProviderDialogUnloadContent
    {
        #region PromptUserToUnloadExistingContent

        public static void BuildDialogButtons(DialogBox<bool> dialog)
        {
            PanelTabGroup actionsPanel = PanelTabGroup.CreateNew(dialog.Descriptor.ContentParent, LayoutDirection.HorizontalNoBackground);

            actionsPanel.Descriptor.SetHeight(60);

            PanelButton noPanelButton = PanelButton.CreateNew(PanelButton.ButtonStyles.CancelButton, actionsPanel.TabButtonParent);
            noPanelButton.Descriptor.SetTitle(BasisLocalization.Get("ui.no"));
            noPanelButton.Descriptor.SetWidth(200);
            noPanelButton.Descriptor.SetHeight(60);
            noPanelButton.OnClicked += () =>
            {
                if (dialog.IsBusy) return;
                dialog.IsBusy = true;

                dialog.CloseWithResult(false);
            };

            PanelButton yesPanelButton = PanelButton.CreateNew(PanelButton.ButtonStyles.AcceptButton, actionsPanel.TabButtonParent);
            yesPanelButton.Descriptor.SetTitle(BasisLocalization.Get("ui.yes"));
            yesPanelButton.Descriptor.SetWidth(200);
            yesPanelButton.Descriptor.SetHeight(60);
            yesPanelButton.OnClicked += () =>
            {
                if (dialog.IsBusy) return;
                dialog.IsBusy = true;

                dialog.CloseWithResult(true);
            };
        }

        /// <summary>
        /// Asks the user whether to unload all existing world and prop content before loading a new world.
        /// Returns true if the user wants existing content cleared first.
        /// </summary>
        public static async Task<bool> PromptUserToUnloadExistingContent(BasisMenuPanel panel)
        {
            DialogBox<bool> dialog = DialogBox<bool>.Create(panel, new Vector2(830, 200),
                BasisLocalization.Get("library.dialog.unloadExisting.title"),
                BasisLocalization.Get("library.dialog.unloadExisting.body"),
                AddressableAssets.Sprites.Information,
                true
            );

            BuildDialogButtons(dialog);

            return await dialog.WaitAsync();
        }

        #endregion
    }

}
