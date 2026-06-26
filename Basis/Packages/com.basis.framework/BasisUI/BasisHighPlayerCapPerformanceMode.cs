using System;
using Basis.BTween;
using Basis.Scripts.Networking.NetworkedAvatar;
using UnityEngine;

namespace Basis.BasisUI
{
    /// <summary>
    /// Watches the instance population and, once per app run for each population tier, offers to
    /// switch on a "High Player Cap Performance Mode" preset that trades avatar fidelity for frame
    /// rate in large instances. Pumped from the central tick; no MonoBehaviour.
    ///
    /// Each tier asks at most once per app lifetime — the static "asked" flags reset on restart,
    /// so the offer comes back the next time the app is launched into a crowded instance. Crossing
    /// a higher tier supersedes the lower ones (their flags are marked asked at the same time), so
    /// joining an already-huge instance shows a single prompt instead of stacking several.
    ///
    /// Only the pose-update LOD bias tightens between tiers; every other value in the preset is the
    /// same at every tier. The dialogue lists the exact values it will apply, sourced from the same
    /// constants used to apply them, so the text can never drift from the effect.
    /// </summary>
    public static class BasisHighPlayerCapPerformanceMode
    {
        // Population tiers in ascending order. A tier arms when the occupant count exceeds its
        // threshold. The count includes the local player, so "over 250 people" means 251 occupants.
        private static readonly int[] Thresholds = { 250, 500, 1000 };

        // Per-tier pose-update LOD bias (0..5; higher = distant avatars skip more pose updates).
        // This is the only preset value that changes between tiers.
        private static readonly float[] PoseLodByTier = { 2f, 3f, 5f };

        // Whether each tier's prompt has already been offered this app run.
        private static readonly bool[] _tierAsked = new bool[Thresholds.Length];

        // Shared preset values (everything except pose LOD, which is per-tier).
        private const float AvatarVisibilityRangeMeters = 50f;
        private const float MaxVisibleAvatarCount = 100f;
        private const float AvatarMeshLodFraction = 0.03f; // slider is 0..1, surfaced as a percentage (3%)
        private const float ViewConeDegrees = 180f;

        // Cheap throttle so the player-count read isn't taken every single frame in a known-heavy
        // instance. ~ every 32 frames is plenty responsive for a one-off suggestion.
        private static int _frameGate;

        public static void Simulate()
        {
            // Highest tier already handled → nothing left to offer this app run. One bool check/frame.
            if (_tierAsked[_tierAsked.Length - 1])
            {
                return;
            }

            if ((++_frameGate & 31) != 0)
            {
                return;
            }

            int count = BasisNetworkPlayer.GetPlayerCount();

            // Act on the highest tier whose threshold is crossed; a tighter prompt supersedes the
            // lighter ones below it.
            for (int i = Thresholds.Length - 1; i >= 0; i--)
            {
                if (count <= Thresholds[i])
                {
                    continue;
                }

                if (_tierAsked[i])
                {
                    // The highest crossed tier was already asked; lower tiers were marked asked at
                    // the same time, so there is nothing left to offer.
                    return;
                }

                // Mark this tier and every lower tier asked up front: ask once per app run, and
                // never fire a lighter prompt after a tighter one has been shown.
                for (int j = 0; j <= i; j++)
                {
                    _tierAsked[j] = true;
                }

                ShowPrompt(Thresholds[i], PoseLodByTier[i]);
                return;
            }
        }

        private static void ShowPrompt(int threshold, float poseLodBias, bool forceShow = false)
        {
            string title = BasisLocalization.Get("settings.highPlayerCap.prompt.title");
            string body = BasisLocalization.Get(
                "settings.highPlayerCap.prompt.body",
                threshold,
                AvatarVisibilityRangeMeters,
                MaxVisibleAvatarCount,
                poseLodBias,
                AvatarMeshLodFraction * 100f,
                ViewConeDegrees);

            // Do-not-disturb / admin panel open → park it in the notification list, recoverable from
            // the bell. forceShow bypasses this when re-opened from that list so it actually shows.
            if (!forceShow && BasisNotificationCenter.RouteToNotifications)
            {
                BasisNotificationCenter.AddPending(title, body, AddressableAssets.Sprites.Information,
                    reopen: () => ShowPrompt(threshold, poseLodBias, true),
                    onDismiss: () => { });
                return;
            }

            // Force the menu open so an unsolicited prompt is actually seen (same as the eye-tracking
            // and direct-connection prompts).
            BasisMainMenu.Open();
            if (!BasisMainMenu.Instance)
            {
                return;
            }

            BasisMenuPanel.PanelData data = new BasisMenuPanel.PanelData
            {
                Title = title,
                PanelSize = new Vector2(950, 620),
                PanelPosition = new Vector3(0, -40, -5),
            };
            BasisMenuPanel panel = BasisMenuPanel.CreateNew(
                data, BasisMainMenu.Instance.MenuObjectInstance.PanelRoot, BasisMenuPanel.PanelStyles.Page);

            PanelTabPage tab = PanelTabPage.CreateVertical(panel.Descriptor.ContentParent);
            RectTransform root = tab.Descriptor.ContentParent;

            PanelElementDescriptor info = PanelElementDescriptor.CreateNew(
                PanelElementDescriptor.ElementStyles.Group, root);
            info.SetDescription(body);

            bool answered = false;
            void Decide(bool enable)
            {
                if (answered) return;
                answered = true;
                if (enable)
                {
                    ApplyPreset(poseLodBias);
                }
                panel.ReleaseInstance();
            }

            PanelTabGroup actionGroup = PanelTabGroup.CreateNew(root, LayoutDirection.HorizontalNoBackground);
            actionGroup.Descriptor.SetHeight(80);

            PanelButton enableButton = PanelButton.CreateNew(PanelButton.ButtonStyles.AcceptButton, actionGroup.TabButtonParent);
            enableButton.Descriptor.SetTitle(BasisLocalization.Get("settings.highPlayerCap.prompt.enable"));
            enableButton.OnClicked += () => Decide(true);

            PanelButton noButton = PanelButton.CreateNew(PanelButton.ButtonStyles.CancelButton, actionGroup.TabButtonParent);
            noButton.Descriptor.SetTitle(BasisLocalization.Get("settings.highPlayerCap.prompt.no"));
            noButton.OnClicked += () => Decide(false);

            // Closed without choosing (menu closed, tab switched) → recover from the bell. The tier is
            // already marked asked, so the tick won't bring it back on its own.
            panel.OnInstanceReleased += () =>
            {
                if (answered) return;
                answered = true;
                BasisNotificationCenter.AddPending(title, body, AddressableAssets.Sprites.Information,
                    reopen: () => ShowPrompt(threshold, poseLodBias, true),
                    onDismiss: () => { });
            };

            panel.Descriptor.ForceRebuild();
            UIAnimations.PopIn(panel);
        }

        private static void ApplyPreset(float poseLodBias)
        {
            // SetValue persists to disk and raises OnSettingChanged, which the live reduction module
            // (SMModuleDistanceBasedReductions) consumes — so each of these applies immediately as
            // well as surviving a restart.
            BasisSettingsDefaults.AvatarRange.SetValue(AvatarVisibilityRangeMeters);
            BasisSettingsDefaults.UseMaxVisibleAvatars.SetValue(true);
            BasisSettingsDefaults.MaxVisibleAvatars.SetValue(MaxVisibleAvatarCount);
            BasisSettingsDefaults.PoseLOD.SetValue(poseLodBias);
            BasisSettingsDefaults.AvatarMeshLOD.SetValue(AvatarMeshLodFraction);
            BasisSettingsDefaults.UseViewConeAvatars.SetValue(true);
            BasisSettingsDefaults.ViewConeAngle.SetValue(ViewConeDegrees);
        }
    }
}
