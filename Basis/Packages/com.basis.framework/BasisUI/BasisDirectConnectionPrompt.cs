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
        public static void Show(string displayName, string uuid, Action<bool> respond, bool forceShow = false)
        {
            string title = BasisLocalization.Get("menu.individualPlayer.directConnection.incomingDialog.title");
            string uuidLabel = string.IsNullOrEmpty(uuid) ? "(unknown)" : uuid;
            string body = BasisLocalization.Get("menu.individualPlayer.directConnection.incomingDialog.body", displayName, uuidLabel);

            // Do-not-disturb / admin panel open → straight to the notification list, no menu.
            // forceShow is set when re-opened from the notification panel so it bypasses the
            // divert and actually shows, instead of being captured again and closing.
            if (!forceShow && BasisNotificationCenter.RouteToNotifications)
            {
                BasisNotificationCenter.AddPending(title, body, AddressableAssets.Sprites.Network,
                    reopen: () => Show(displayName, uuid, respond, true),
                    onDismiss: () => respond(false));
                return;
            }

            // Open the menu fresh. Done unconditionally on purpose: when re-opened from
            // the notification page this clears that panel, so the prompt stands alone
            // instead of being created alongside it (which closed it instantly).
            BasisMainMenu.Open();
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
            panel.SetLayer(BasisMenuPanel.PanelLayer.Overlay);
            BasisPanelMoveHandle.Attach(panel, nameof(BasisDirectConnectionPrompt));

            // Vertical scrollable content — same pattern as the player panels.
            PanelTabPage tab = PanelTabPage.CreateVertical(panel.Descriptor.ContentParent);
            RectTransform root = tab.Descriptor.ContentParent;

            // The warning body shown in a Group so it actually renders — the tab descriptor's
            // own description isn't surfaced, so it would only show the panel title. The card
            // reads hot rather than colouring the "Accepting reveals your IP address" sentence
            // itself, so the severity is stated the same way the settings cards state theirs.
            PanelElementDescriptor info = PanelElementDescriptor.CreateNew(
                PanelElementDescriptor.ElementStyles.Group, root);
            info.SetDescription(body);
            BasisPanelTint.Apply(BasisPanelTint.Capture(info), BasisPanelSeverity.Hot, false);

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
                if (!answered)
                {
                    answered = true;
                    BasisNotificationCenter.AddPending(title, body, AddressableAssets.Sprites.Network,
                        reopen: () => Show(displayName, uuid, respond, true),
                        onDismiss: () => respond(false));
                }

                // Opening the menu above tore down the page and virtual keyboard the user had
                // up; put them back now the prompt is gone. Runs after the pending entry is
                // filed so a restored notification page already lists it.
                BasisMenuPromptRestore.RestoreAfterPrompt();
            };

            panel.Descriptor.ForceRebuild();
            UIAnimations.PopIn(panel);
        }
    }
}
