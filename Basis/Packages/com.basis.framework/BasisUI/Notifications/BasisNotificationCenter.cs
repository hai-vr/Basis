using System;
using System.Collections.Generic;
using Basis.Scripts.Common;

namespace Basis.BasisUI
{
    /// <summary>
    /// Process-wide collection point for popups. Any popup that is closed before the
    /// user accepts or denies it is captured here as a pending entry that can be
    /// re-opened later; answered popups are kept as history. State is in-memory only
    /// and resets when the application restarts (pending entries hold live callbacks,
    /// which cannot be serialised across sessions).
    /// </summary>
    public static class BasisNotificationCenter
    {
        private static readonly List<BasisNotification> _all = new();

        /// <summary>All notifications in insertion order (oldest first).</summary>
        public static IReadOnlyList<BasisNotification> All => _all;

        /// <summary>
        /// Raised whenever the collection changes so any open notification UI (the
        /// panel and the hotbar badge) can refresh.
        /// </summary>
        public static event Action Changed;

        public static int PendingCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _all.Count; i++)
                {
                    if (_all[i].Status == BasisNotificationStatus.Pending) count++;
                }
                return count;
            }
        }

        // ── Ignore ("do not disturb") mode ───────────────────────────────────
        private const string IgnoreModeStoreKey = "NotificationsIgnoreMode.BAS";
        private static bool _ignoreModeLoaded;
        private static bool _ignoreMode;

        /// <summary>
        /// When enabled, new interruptive popups (accept/deny dialogues and load
        /// prompts) are routed straight into the pending list instead of being shown,
        /// so they never interrupt the user — they can still be brought up from the
        /// notification panel. Persists across sessions.
        /// </summary>
        public static bool IgnoreMode
        {
            get
            {
                if (!_ignoreModeLoaded)
                {
                    _ignoreMode = BasisDataStore.LoadInt(IgnoreModeStoreKey, 0) != 0;
                    _ignoreModeLoaded = true;
                }
                return _ignoreMode;
            }
            set
            {
                _ignoreModeLoaded = true;
                if (_ignoreMode == value) return;
                _ignoreMode = value;
                BasisDataStore.SaveInt(value ? 1 : 0, IgnoreModeStoreKey);
                Changed?.Invoke();
            }
        }

        // ── Panel section visibility (persisted view prefs) ──────────────────
        private const string ShowPendingStoreKey = "NotificationsShowPending.BAS";
        private const string ShowHistoryStoreKey = "NotificationsShowHistory.BAS";
        private static bool _showPendingLoaded;
        private static bool _showPending = true;
        private static bool _showHistoryLoaded;
        private static bool _showHistory; // history hidden by default

        /// <summary>Whether the panel shows the Pending section.</summary>
        public static bool ShowPending
        {
            get
            {
                if (!_showPendingLoaded)
                {
                    _showPending = BasisDataStore.LoadInt(ShowPendingStoreKey, 1) != 0;
                    _showPendingLoaded = true;
                }
                return _showPending;
            }
            set
            {
                _showPendingLoaded = true;
                if (_showPending == value) return;
                _showPending = value;
                BasisDataStore.SaveInt(value ? 1 : 0, ShowPendingStoreKey);
                Changed?.Invoke();
            }
        }

        /// <summary>Whether the panel shows the History section.</summary>
        public static bool ShowHistory
        {
            get
            {
                if (!_showHistoryLoaded)
                {
                    _showHistory = BasisDataStore.LoadInt(ShowHistoryStoreKey, 0) != 0;
                    _showHistoryLoaded = true;
                }
                return _showHistory;
            }
            set
            {
                _showHistoryLoaded = true;
                if (_showHistory == value) return;
                _showHistory = value;
                BasisDataStore.SaveInt(value ? 1 : 0, ShowHistoryStoreKey);
                Changed?.Invoke();
            }
        }

        // ── Forced suppression scopes ────────────────────────────────────────
        // Some contexts (the admin and moderator panels) require every popup to be
        // routed into the list while they are open, regardless of the user's toggle.
        // They push a scope on open and pop it on close; normal handling resumes once
        // all scopes are gone.
        private static int _forcedScopes;

        /// <summary>True while an open context is forcing popups into the list.</summary>
        public static bool SuppressionForced => _forcedScopes > 0;

        /// <summary>
        /// Whether new popups should be routed into the list instead of shown — because
        /// the user enabled ignore mode, or because a forcing context (admin/moderator
        /// panel) is currently open.
        /// </summary>
        public static bool RouteToNotifications => IgnoreMode || SuppressionForced;

        /// <summary>
        /// Begin a context that forces popups into the list (call from a panel's
        /// OnEnable). Must be balanced with <see cref="EndForcedScope"/> on close.
        /// </summary>
        public static void BeginForcedScope() => _forcedScopes++;

        /// <summary>End a forcing context (call from the matching OnDisable).</summary>
        public static void EndForcedScope()
        {
            if (_forcedScopes > 0) _forcedScopes--;
        }

        // ── New-action indicator ─────────────────────────────────────────────
        private static int _unseen;

        /// <summary>Number of notifications added since the panel was last viewed.</summary>
        public static int UnseenCount => _unseen;

        /// <summary>Whether a "new action" visual should be shown in the main menu.</summary>
        public static bool HasUnseen => _unseen > 0;

        /// <summary>Clear the new-action indicator — called when the panel is opened.</summary>
        public static void MarkAllSeen()
        {
            _unseen = 0;
            Changed?.Invoke();
        }

        /// <summary>
        /// Capture a popup that was closed before being answered. The entry stays
        /// pending until <see cref="Reopen"/> brings the popup back (and it is then
        /// answered) or <see cref="Dismiss"/> drops it.
        /// </summary>
        public static BasisNotification AddPending(
            string title,
            string description,
            string iconAddress,
            Action reopen,
            Action onDismiss = null)
        {
            BasisNotification notification = new BasisNotification
            {
                Title = title,
                Description = description,
                IconAddress = iconAddress,
                Status = BasisNotificationStatus.Pending,
                Reopen = reopen,
                OnDismiss = onDismiss,
            };

            _all.Add(notification);
            _unseen++;
            Changed?.Invoke();
            return notification;
        }

        /// <summary>
        /// Record a popup that reached a terminal state (accepted/denied/dismissed)
        /// without ever having been captured as pending.
        /// </summary>
        public static BasisNotification LogResolved(
            string title,
            string description,
            string iconAddress,
            BasisNotificationStatus outcome)
        {
            if (outcome == BasisNotificationStatus.Pending)
            {
                outcome = BasisNotificationStatus.Dismissed;
            }

            BasisNotification notification = new BasisNotification
            {
                Title = title,
                Description = description,
                IconAddress = iconAddress,
                Status = outcome,
                ResolvedUtc = DateTime.UtcNow,
            };

            _all.Add(notification);
            Changed?.Invoke();
            return notification;
        }

        /// <summary>
        /// Bring a pending popup back up. The re-spawned popup logs its own outcome,
        /// so the stale pending entry is removed before the factory runs.
        /// </summary>
        public static void Reopen(BasisNotification notification)
        {
            if (notification == null) return;

            Action reopen = notification.Reopen;
            _all.Remove(notification);
            Changed?.Invoke();
            reopen?.Invoke();
        }

        /// <summary>
        /// Drop a pending entry without re-opening it, letting the source resolve its
        /// callback. The entry is kept as <see cref="BasisNotificationStatus.Dismissed"/>
        /// history.
        /// </summary>
        public static void Dismiss(BasisNotification notification)
        {
            if (notification == null || notification.Status != BasisNotificationStatus.Pending) return;

            Action onDismiss = notification.OnDismiss;
            notification.OnDismiss = null;
            notification.Reopen = null;
            notification.Status = BasisNotificationStatus.Dismissed;
            notification.ResolvedUtc = DateTime.UtcNow;
            Changed?.Invoke();
            onDismiss?.Invoke();
        }

        /// <summary>Remove every terminal (non-pending) entry.</summary>
        public static void ClearHistory()
        {
            _all.RemoveAll(n => n.Status != BasisNotificationStatus.Pending);
            Changed?.Invoke();
        }
    }
}
