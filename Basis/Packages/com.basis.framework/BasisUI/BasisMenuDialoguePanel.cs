using System;
using Basis.BTween;
using UnityEngine;
using UnityEngine.UI;

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

        /// <summary>
        /// When false, closing the dialogue dismisses it silently instead of parking
        /// it in the notification center.
        /// </summary>
        public bool CaptureOnClose = true;

        public PanelButton AcceptButton;
        public PanelButton DeclineButton;
        public PanelButton AlternateButton;
        public Action<bool> Callback;

        public string Alternate;
        public Action AlternateCallback;


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

        /// <summary>
        /// Adds a third button to the dialogue between Accept and Decline. Choosing it
        /// resolves the dialogue and invokes <paramref name="onChosen"/> instead of the
        /// binary accept/decline callback. The button is created at runtime so existing
        /// two-button callers are unaffected.
        /// </summary>
        public void EnableAlternate(string label, Action onChosen)
        {
            if (string.IsNullOrEmpty(label) || onChosen == null) return;
            if (AcceptButton == null || DeclineButton == null) return;

            Alternate = label;
            AlternateCallback = onChosen;

            AlternateButton = PanelButton.CreateNew(AcceptButton.rectTransform.parent);
            AlternateButton.Descriptor.SetTitle(label);
            // The accent-blue standard style, matching the Accept/Decline button family
            // instead of the neutral grey the base button prefab ships with.
            AlternateButton.ButtonStyling.SetStyle("Button Standard");
            AlternateButton.rectTransform.SetSiblingIndex(DeclineButton.rectTransform.GetSiblingIndex());
            MatchButtonMetrics(AcceptButton, AlternateButton);
            AlternateButton.OnClicked += ResolveAlternate;
        }

        private void ResolveAlternate()
        {
            _resolved = true;
            BasisNotificationCenter.LogResolved(
                Title,
                Description,
                AddressableAssets.Sprites.Information,
                BasisNotificationStatus.Accepted);
            AlternateCallback?.Invoke();
            ReleaseInstance();
        }

        private static void MatchButtonMetrics(PanelButton template, PanelButton target)
        {
            target.rectTransform.sizeDelta = new Vector2(
                target.rectTransform.sizeDelta.x, template.rectTransform.sizeDelta.y);

            LayoutElement from = template.GetComponent<LayoutElement>();
            LayoutElement to = target.Layout;
            if (to == null) return;

            if (from != null)
            {
                to.minWidth = from.minWidth;
                to.preferredWidth = from.preferredWidth;
                to.flexibleWidth = from.flexibleWidth;
                to.minHeight = from.minHeight;
                to.preferredHeight = from.preferredHeight;
                to.flexibleHeight = from.flexibleHeight;
            }
            else
            {
                to.minWidth = 0f;
                to.flexibleWidth = 1f;
                to.preferredHeight = template.rectTransform.sizeDelta.y;
            }
        }

        private void CaptureIfUnresolved()
        {
            if (_resolved) return;
            _resolved = true;

            if (!CaptureOnClose) return;

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
