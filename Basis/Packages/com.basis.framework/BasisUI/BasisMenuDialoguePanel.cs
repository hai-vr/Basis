using System;
using Basis.BTween;
using UnityEngine;

namespace Basis.BasisUI
{
    public class BasisMenuDialoguePanel : BasisMenuPanel
    {

        public static class DialogueStyles
        {
            public static string Default => "Packages/com.basis.sdk/Prefabs/Dialogue Panel.prefab";
        }

        public static PanelData DialoguePanelData => new PanelData
        {
            Title = "Dialogue",
            PanelSize = new Vector2(700, 500),
            PanelPosition = new Vector3(0, -100, -5),
        };

        public static string AcceptDefault = "Accept";
        public static string DeclineDefault = "Decline";

        public string Title;
        public string Description;
        public string Accept;
        public string Decline;

        public bool BlocksOtherActions;

        public PanelButton AcceptButton;
        public PanelButton DeclineButton;
        public Action<bool> Callback;


        private bool _resolved;

        public override void OnCreateEvent()
        {
            base.OnCreateEvent();
            AcceptButton.OnClicked += () => Resolve(true);
            DeclineButton.OnClicked += () => Resolve(false);

            // Closing this dialogue without choosing accept/decline (switching tabs,
            // closing the menu, the panel being destroyed) routes it to the
            // notification center as a pending entry that can be brought back up,
            // instead of silently dropping the request.
            OnInstanceReleased += CaptureIfUnresolved;
        }

        private void Resolve(bool accepted)
        {
            _resolved = true;
            BasisNotificationCenter.LogResolved(
                Title,
                Description,
                AddressableAssets.Sprites.Information,
                accepted ? BasisNotificationStatus.Accepted : BasisNotificationStatus.Denied);
            Callback?.Invoke(accepted);
            ReleaseInstance();
        }

        private void CaptureIfUnresolved()
        {
            if (_resolved) return;
            _resolved = true;

            // Snapshot the dialogue contents so the captured entry can rebuild the
            // exact same prompt with its original callback still attached.
            string title = Title;
            string description = Description;
            string accept = Accept;
            string deny = Decline;
            Action<bool> callback = Callback;

            BasisNotificationCenter.AddPending(
                title,
                description,
                AddressableAssets.Sprites.Information,
                reopen: () =>
                {
                    if (!BasisMainMenu.Instance) BasisMainMenu.Open();
                    if (!BasisMainMenu.Instance) return;
                    if (BasisMainMenu.Instance.Dialogue) BasisMainMenu.Instance.Dialogue.ReleaseInstance();
                    // CreateInternal bypasses ignore mode so re-open always shows.
                    BasisMainMenu.Instance.Dialogue = CreateInternal(title, description, accept, deny, callback);
                },
                onDismiss: () => callback?.Invoke(false));
        }

        /// <summary>
        /// Instantiate a new Panel and load in the corresponding panel data.
        /// When ignore mode is on the prompt is routed to the notification center
        /// instead of being shown, and this returns null.
        /// </summary>
        public static BasisMenuDialoguePanel CreateNew(
            string title,
            string description,
            string accept,
            string deny,
            Action<bool> callback,
            bool divertible = false)
        {
            // Only "divertible" (incoming/unsolicited) popups route to the notification
            // list under do-not-disturb or while the admin/moderator panel is open.
            // User-initiated confirmations (the default) always show.
            if (divertible && BasisNotificationCenter.RouteToNotifications)
            {
                return SuppressToNotifications(title, description, accept, deny, callback);
            }
            return CreateInternal(title, description, accept, deny, callback);
        }

        /// <summary>
        /// Instantiate a new Panel and load in the corresponding panel data.
        /// When ignore mode is on the prompt is routed to the notification center
        /// instead of being shown, and this returns null.
        /// </summary>
        public static BasisMenuDialoguePanel CreateNew(
            string title,
            string description,
            string accept,
            Action<bool> callback,
            bool divertible = false)
        {
            if (divertible && BasisNotificationCenter.RouteToNotifications)
            {
                return SuppressToNotifications(title, description, accept, null, callback);
            }
            return CreateInternal(title, description, accept, null, callback);
        }

        /// <summary>
        /// Actually instantiate and show the dialogue, bypassing ignore mode. Used both
        /// by the normal path and when a suppressed/captured prompt is re-opened.
        /// </summary>
        private static BasisMenuDialoguePanel CreateInternal(
            string title,
            string description,
            string accept,
            string deny,
            Action<bool> callback)
        {
            if (!BasisMainMenu.Instance)
            {
                return null;
            }

            // The dialogue parents under the menu's panel root. That root can be gone
            // while the menu chrome is torn down — and because the global exception
            // notifier opens dialogues, it can reach here mid-teardown. Bail quietly
            // instead of passing a null parent into CreateNew, whose "Parent Missing!"
            // LogError would feed straight back into the notifier.
            var menuInstance = BasisMainMenu.Instance.MenuObjectInstance;
            if (menuInstance == null || menuInstance.PanelRoot == null)
            {
                return null;
            }

            Component parent = menuInstance.PanelRoot;

            BasisMenuDialoguePanel panel = CreateNew<BasisMenuDialoguePanel>(DialogueStyles.Default, parent);
            if (panel == null)
            {
                return null;
            }
            panel.LoadData(DialoguePanelData);
            panel.Callback = callback;
            panel.FillDialogue(title, description, accept, deny);

            // Pop-in animation for dialogues
            UIAnimations.PopIn(panel);

            return panel;
        }

        /// <summary>
        /// Register an unshown prompt as a pending notification that can be brought up
        /// on demand. Returns null since no panel is created.
        /// </summary>
        private static BasisMenuDialoguePanel SuppressToNotifications(
            string title,
            string description,
            string accept,
            string deny,
            Action<bool> callback)
        {
            BasisNotificationCenter.AddPending(
                title,
                description,
                AddressableAssets.Sprites.Information,
                reopen: () =>
                {
                    if (!BasisMainMenu.Instance) BasisMainMenu.Open();
                    if (!BasisMainMenu.Instance) return;
                    if (BasisMainMenu.Instance.Dialogue) BasisMainMenu.Instance.Dialogue.ReleaseInstance();
                    BasisMainMenu.Instance.Dialogue = CreateInternal(title, description, accept, deny, callback);
                },
                onDismiss: () => callback?.Invoke(false));
            return null;
        }

        public void FillDialogue(string title, string description, string accept, string decline = null)
        {
            Title = title;
            Description = description;
            Accept = accept;

            Descriptor.SetTitle(title);
            Descriptor.SetDescription(description);

            AcceptButton.Descriptor.SetTitle(Accept);

            if (!string.IsNullOrEmpty(decline))
            {
                Decline = decline;
                DeclineButton.Descriptor.SetTitle(decline);
                DeclineButton.gameObject.SetActive(true);
            }
            else
            {
                DeclineButton.gameObject.SetActive(false);
            }
        }
    }
}
