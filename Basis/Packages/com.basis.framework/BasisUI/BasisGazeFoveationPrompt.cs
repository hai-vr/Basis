using System;
using Basis.BTween;
using UnityEngine;

namespace Basis.BasisUI
{
    /// <summary>
    /// One-time, per-headset prompt offering to enable gaze-foveated Variable Rate Shading
    /// when an eye-tracking headset is first detected. <paramref name="onDecided"/> fires with
    /// the user's explicit choice (remembered by the caller); <paramref name="onDismissed"/>
    /// fires if the prompt is closed without answering (left undecided, re-asked next time).
    /// </summary>
    public static class BasisGazeFoveationPrompt
    {
        public static void Show(Action<bool> onDecided, Action onDismissed, bool forceShow = false)
        {
            string title = BasisLocalization.Get("settings.eyeFoveation.prompt.title");
            string body = BasisLocalization.Get("settings.eyeFoveation.prompt.body");

            if (!forceShow && BasisNotificationCenter.RouteToNotifications)
            {
                BasisNotificationCenter.AddPending(title, body, AddressableAssets.Sprites.Information,
                    reopen: () => Show(onDecided, onDismissed, true),
                    onDismiss: () => onDismissed?.Invoke());
                return;
            }

            BasisMainMenu.Open();
            if (!BasisMainMenu.Instance)
            {
                onDismissed?.Invoke();
                return;
            }

            BasisMenuPanel.PanelData data = new BasisMenuPanel.PanelData
            {
                Title = title,
                PanelSize = new Vector2(950, 520),
                PanelPosition = new Vector3(0, -40, -5),
            };
            BasisMenuPanel panel = BasisMenuPanel.CreateNew(
                data, BasisMainMenu.Instance.MenuObjectInstance.PanelRoot, BasisMenuPanel.PanelStyles.Page);
            panel.SetLayer(BasisMenuPanel.PanelLayer.Overlay);
            BasisPanelMoveHandle.Attach(panel, nameof(BasisGazeFoveationPrompt));

            PanelTabPage tab = PanelTabPage.CreateVertical(panel.Descriptor.ContentParent);
            RectTransform root = tab.Descriptor.ContentParent;

            PanelElementDescriptor info = PanelElementDescriptor.CreateNew(
                PanelElementDescriptor.ElementStyles.Group, root);
            info.SetDescription(body);

            bool answered = false;
            void Decide(bool value)
            {
                if (answered) return;
                answered = true;
                onDecided?.Invoke(value);
                panel.ReleaseInstance();
            }

            PanelTabGroup actionGroup = PanelTabGroup.CreateNew(root, LayoutDirection.HorizontalNoBackground);
            actionGroup.Descriptor.SetHeight(80);

            PanelButton enable = PanelButton.CreateNew(PanelButton.ButtonStyles.AcceptButton, actionGroup.TabButtonParent);
            enable.Descriptor.SetTitle(BasisLocalization.Get("settings.eyeFoveation.prompt.enable"));
            enable.OnClicked += () => Decide(true);

            PanelButton no = PanelButton.CreateNew(PanelButton.ButtonStyles.CancelButton, actionGroup.TabButtonParent);
            no.Descriptor.SetTitle(BasisLocalization.Get("settings.eyeFoveation.prompt.no"));
            no.OnClicked += () => Decide(false);

            panel.OnInstanceReleased += () =>
            {
                if (!answered)
                {
                    answered = true;
                    onDismissed?.Invoke();
                }

                // Opening the menu above tore down the page and virtual keyboard the user had
                // up; put them back now the prompt is gone.
                BasisMenuPromptRestore.RestoreAfterPrompt();
            };

            panel.Descriptor.ForceRebuild();
            UIAnimations.PopIn(panel);
        }
    }
}
