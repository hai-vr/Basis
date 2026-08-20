using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Drivers;
using UnityEngine;

namespace Basis.BasisUI
{
    public partial class IndividualPlayerProvider
    {
        /// <summary>
        /// One resolved answer to "why am I not seeing this person?" — a short state label, the
        /// explanation that goes under it, and how much attention it deserves.
        /// </summary>
        public readonly struct AvatarStatusReport
        {
            public readonly string Title;
            public readonly string Body;
            public readonly BasisPanelSeverity Severity;

            public AvatarStatusReport(string title, string body, BasisPanelSeverity severity)
            {
                Title = title;
                Body = body;
                Severity = severity;
            }
        }

        /// <summary>
        /// Metres between the local head and this player, plus the avatar range slider in the same
        /// units. <see cref="SMModuleDistanceBasedReductions.AvatarRange"/> holds the SQUARED
        /// distance because that is the form the Burst distance job consumes.
        /// </summary>
        private static void MeasureAvatarRange(BasisRemotePlayer player, out float distance, out float rangeMeters)
        {
            Vector3 localPosition = BasisLocalCameraDriver.Position;
            Vector3 remotePosition = localPosition;
            if (player.MouthTransform != null)
            {
                remotePosition = player.MouthTransform.position;
            }
            else if (player.Transform != null)
            {
                remotePosition = player.Transform.position;
            }

            distance = Vector3.Distance(localPosition, remotePosition);
            rangeMeters = Mathf.Sqrt(Mathf.Max(SMModuleDistanceBasedReductions.AvatarRange, 0f));
        }

        /// <summary>
        /// What is on screen in place of the real avatar, or null while the real avatar is worn.
        /// </summary>
        private static string DescribeStandIn(BasisRemotePlayer player)
        {
            if (player.BasisAvatar == null)
            {
                return BasisLocalization.Get("menu.individualPlayer.avatarStatus.showing.nothing");
            }
            if (player.IsFarLodActive)
            {
                return BasisLocalization.Get("menu.individualPlayer.avatarStatus.showing.far");
            }
            if (player.IsConsideredFallBackAvatar)
            {
                return BasisLocalization.Get("menu.individualPlayer.avatarStatus.showing.placeholder");
            }
            return null;
        }

        private static string WithStandIn(string body, string standIn)
        {
            return string.IsNullOrEmpty(standIn) ? body : body + "\n" + standIn;
        }

        private static string WithReason(string body, string reason)
        {
            return string.IsNullOrEmpty(reason)
                ? body
                : body + "\n" + BasisLocalization.Get("menu.individualPlayer.avatarStatus.reason", reason);
        }

        /// <summary>
        /// Resolves why the local client is showing something other than this player's real avatar.
        /// The ladder covers every clause of the gate in <see cref="BasisRemotePlayer.CreateAvatar"/>
        /// that routes a player down the stand-in branch, ordered by which answer is the most useful
        /// one rather than by the order the gate happens to evaluate them: a player who is both
        /// blocked and out of range is blocked, and moving closer would change nothing.
        /// </summary>
        public static AvatarStatusReport DescribeAvatarStatus(BasisRemotePlayer player)
        {
            // IsDestroyed, not a null check: BasisRemotePlayer is a plain class, so a player who
            // left the instance stays reachable through the panel that was open on them.
            if (player == null || player.IsDestroyed)
            {
                return new AvatarStatusReport(
                    BasisLocalization.Get("menu.individualPlayer.avatarStatus.gone"),
                    BasisLocalization.Get("menu.individualPlayer.avatarStatus.gone.body"),
                    BasisPanelSeverity.None);
            }

            string standIn = DescribeStandIn(player);

            // Blocking wins outright — it is the only state that also takes their voice and chat,
            // and nothing further down the ladder can put an avatar back while it holds.
            if (player.IsBlocked)
            {
                return new AvatarStatusReport(
                    BasisLocalization.Get("menu.individualPlayer.avatarStatus.blockedByYou"),
                    BasisLocalization.Get("menu.individualPlayer.avatarStatus.blockedByYou.body"),
                    BasisPanelSeverity.Caution);
            }

            if (player.TempBlocked)
            {
                return new AvatarStatusReport(
                    BasisLocalization.Get("menu.individualPlayer.avatarStatus.blockedByThem"),
                    BasisLocalization.Get("menu.individualPlayer.avatarStatus.blockedByThem.body"),
                    BasisPanelSeverity.Caution);
            }

            // Read the hide flag from the settings cache rather than the copy the panel opened
            // with, so pressing Hide/Show Avatar is reflected on the next poll. The cache is warm
            // for anyone who has an avatar at all — CreateAvatar reads it on every load.
            if (BasisPlayerSettingsManager.TryGetCached(player.UUID, out BasisPlayerSettingsData settings)
                && !settings.AvatarVisible)
            {
                return new AvatarStatusReport(
                    BasisLocalization.Get("menu.individualPlayer.avatarStatus.hidden"),
                    BasisLocalization.Get("menu.individualPlayer.avatarStatus.hidden.body"),
                    BasisPanelSeverity.Caution);
            }

            // Terminal: BasisAvatarFactory stops retrying once this latches, so it never clears on
            // its own and the recorded error is the only clue to what went wrong.
            if (player.HasFailedAvatarLoadGlobally)
            {
                return new AvatarStatusReport(
                    BasisLocalization.Get("menu.individualPlayer.avatarStatus.failed"),
                    WithStandIn(
                        WithReason(BasisLocalization.Get("menu.individualPlayer.avatarStatus.failed.body"),
                            player.AvatarLoadErrorMessage),
                        standIn),
                    BasisPanelSeverity.Hot);
            }

            if (player.IsBlockedByPerformance)
            {
                return new AvatarStatusReport(
                    BasisLocalization.Get("menu.individualPlayer.avatarStatus.performance"),
                    WithStandIn(
                        WithReason(BasisLocalization.Get("menu.individualPlayer.avatarStatus.performance.body"),
                            player.PerformanceBlockReason),
                        standIn),
                    BasisPanelSeverity.Caution);
            }

            if (!player.InAvatarRange && !player.AvatarAlwaysLoaded)
            {
                MeasureAvatarRange(player, out float distance, out float rangeMeters);

                // BasisAvatarCapJob only ever clears a range bit the distance test had already set,
                // so a player sitting comfortably inside the slider yet still flagged out of range
                // lost their slot to somebody closer.
                bool insideSlider = distance <= rangeMeters;
                if (insideSlider && SMModuleDistanceBasedReductions.UseMaxVisibleAvatars)
                {
                    int cap = SMModuleDistanceBasedReductions.MaxVisibleAvatars;
                    return new AvatarStatusReport(
                        BasisLocalization.Get("menu.individualPlayer.avatarStatus.overCap"),
                        WithStandIn(
                            cap <= 0
                                ? BasisLocalization.Get("menu.individualPlayer.avatarStatus.overCap.body.zero")
                                : BasisLocalization.Get("menu.individualPlayer.avatarStatus.overCap.body", cap),
                            standIn),
                        BasisPanelSeverity.None);
                }

                if (insideSlider)
                {
                    // Inside the slider with the cap off — the range flag is mid-debounce and the
                    // real avatar is already on its way back.
                    return new AvatarStatusReport(
                        BasisLocalization.Get("menu.individualPlayer.avatarStatus.rangePending"),
                        WithStandIn(BasisLocalization.Get("menu.individualPlayer.avatarStatus.rangePending.body"), standIn),
                        BasisPanelSeverity.None);
                }

                return new AvatarStatusReport(
                    BasisLocalization.Get("menu.individualPlayer.avatarStatus.outOfRange"),
                    WithStandIn(
                        BasisLocalization.Get("menu.individualPlayer.avatarStatus.outOfRange.body", distance, rangeMeters),
                        standIn),
                    BasisPanelSeverity.None);
            }

            // Past the gate, so the real avatar is wanted. Anything still standing in is a download
            // in flight or the tail end of one.
            if (player.IsLoadingAnAvatar)
            {
                return new AvatarStatusReport(
                    BasisLocalization.Get("menu.individualPlayer.avatarStatus.loading"),
                    WithStandIn(BasisLocalization.Get("menu.individualPlayer.avatarStatus.loading.body"), standIn),
                    BasisPanelSeverity.None);
            }

            if (standIn != null)
            {
                return new AvatarStatusReport(
                    BasisLocalization.Get("menu.individualPlayer.avatarStatus.placeholder"),
                    WithStandIn(BasisLocalization.Get("menu.individualPlayer.avatarStatus.placeholder.body"), standIn),
                    BasisPanelSeverity.None);
            }

            return new AvatarStatusReport(
                BasisLocalization.Get("menu.individualPlayer.avatarStatus.shown"),
                BasisLocalization.Get("menu.individualPlayer.avatarStatus.shown.body"),
                BasisPanelSeverity.Calm);
        }

        /// <summary>
        /// Writes a fresh <see cref="DescribeAvatarStatus"/> onto a card. Both setters skip
        /// identical text, so this is cheap to call from a poll — the tint is the only part that
        /// needs its own change check, and <see cref="BasisPanelTint.Apply"/> already has one.
        /// </summary>
        internal static void PaintAvatarStatus(BasisRemotePlayer player, PanelElementDescriptor field,
            BasisPanelTint.Handle tint, bool animate)
        {
            if (field == null)
            {
                return;
            }

            AvatarStatusReport report = DescribeAvatarStatus(player);
            field.SetTitle(report.Title);
            field.SetDescription(report.Body);

            // A tween started on a disabled object never ticks, which would strand the colour
            // part-way and leave the handle believing it finished — so only animate while the
            // Actions page is the tab actually on screen.
            BasisPanelTint.Apply(tint, report.Severity, animate && field.gameObject.activeInHierarchy);
        }
    }
}
