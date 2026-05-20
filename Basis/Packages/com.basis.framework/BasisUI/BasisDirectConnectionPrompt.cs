using System;
using System.Collections.Generic;
using Basis.BTween;
using UnityEngine;

namespace Basis.BasisUI
{
    /// <summary>
    /// Incoming direct-connection prompt for the recipient: title + body + the per-person
    /// policy dropdown (the same control as the individual player panel) + Accept/Decline.
    ///
    /// Built in code from layout-managed panel elements rather than the fixed Dialogue
    /// Panel prefab — that prefab has no content layout area, so a dropdown can't be placed
    /// in it. Routes to the notification list under do-not-disturb / while the admin or
    /// moderator panel is open, and is recoverable from the bell if closed unanswered.
    /// </summary>
    public static class BasisDirectConnectionPrompt
    {
        public static void Show(string displayName, string uuid, Action<bool> respond)
        {
            string title = BasisLocalization.Get("menu.individualPlayer.directConnection.incomingDialog.title");
            string uuidLabel = string.IsNullOrEmpty(uuid) ? "(unknown)" : uuid;
            string body = BasisLocalization.Get("menu.individualPlayer.directConnection.incomingDialog.body", displayName, uuidLabel);

            // Do-not-disturb / admin panel open → straight to the notification list, no menu.
            if (BasisNotificationCenter.RouteToNotifications)
            {
                BasisNotificationCenter.AddPending(title, body, AddressableAssets.Sprites.Network,
                    reopen: () => Show(displayName, uuid, respond),
                    onDismiss: () => respond(false));
                return;
            }

            // Only open the menu if it isn't already — calling Open() while it's open
            // tears down and rebuilds the whole menu (a one-frame flash).
            if (!BasisMainMenu.Instance) BasisMainMenu.Open();
            if (!BasisMainMenu.Instance)
            {
                respond(false);
                return;
            }

            BasisMenuPanel.PanelData data = new BasisMenuPanel.PanelData
            {
                Title = title,
                PanelSize = new Vector2(950, 640),
                PanelPosition = new Vector3(0, -40, -5),
            };
            BasisMenuPanel panel = BasisMenuPanel.CreateNew(
                data, BasisMainMenu.Instance.MenuObjectInstance.PanelRoot, BasisMenuPanel.PanelStyles.Page);

            // Vertical scrollable content — same pattern as the player panels.
            PanelTabPage tab = PanelTabPage.CreateVertical(panel.Descriptor.ContentParent);
            tab.Descriptor.SetDescription(body);
            RectTransform root = tab.Descriptor.ContentParent;

            // Per-person policy dropdown — identical control to the individual player panel,
            // so the recipient can change it right here.
            if (!string.IsNullOrEmpty(uuid))
            {
                PanelDropdown policy = PanelDropdown.CreateNewEntry(root);
                policy.Descriptor.SetTitle(BasisLocalization.Get("menu.individualPlayer.directConnection.policy"));
                policy.Descriptor.SetDescription(BasisLocalization.Get("menu.individualPlayer.directConnection.policy.description"));

                // Order must match BasisDirectConnectionPolicy (Ask, AlwaysAccept, AlwaysDecline).
                List<string> options = new List<string>
                {
                    BasisLocalization.Get("menu.individualPlayer.directConnection.policy.ask"),
                    BasisLocalization.Get("menu.individualPlayer.directConnection.policy.accept"),
                    BasisLocalization.Get("menu.individualPlayer.directConnection.policy.decline"),
                };
                policy.AssignEntries(options);
                policy.SetValueWithoutNotify(options[(int)BasisTrustedConnections.GetPolicy(uuid)]);
                policy.OnValueChanged = selected =>
                {
                    int idx = options.IndexOf(selected);
                    if (idx < 0) idx = 0;
                    BasisTrustedConnections.SetPolicy(uuid, (BasisDirectConnectionPolicy)idx);
                };
            }

            bool answered = false;
            void Answer(bool value)
            {
                if (answered) return;
                answered = true;
                respond(value);
                panel.ReleaseInstance();
            }

            PanelTabGroup actionGroup = PanelTabGroup.CreateNew(root, LayoutDirection.HorizontalNoBackground);
            actionGroup.Descriptor.SetHeight(80);

            PanelButton accept = PanelButton.CreateNew(PanelButton.ButtonStyles.AcceptButton, actionGroup.TabButtonParent);
            accept.Descriptor.SetTitle(BasisLocalization.Get("menu.individualPlayer.directConnection.accept"));
            accept.OnClicked += () => Answer(true);

            PanelButton decline = PanelButton.CreateNew(PanelButton.ButtonStyles.CancelButton, actionGroup.TabButtonParent);
            decline.Descriptor.SetTitle(BasisLocalization.Get("menu.individualPlayer.directConnection.decline"));
            decline.OnClicked += () => Answer(false);

            // Closed before answering (menu closed, etc.) → recover from the bell.
            panel.OnInstanceReleased += () =>
            {
                if (answered) return;
                answered = true;
                BasisNotificationCenter.AddPending(title, body, AddressableAssets.Sprites.Network,
                    reopen: () => Show(displayName, uuid, respond),
                    onDismiss: () => respond(false));
            };

            panel.Descriptor.ForceRebuild();
            UIAnimations.PopIn(panel);
        }
    }
}
