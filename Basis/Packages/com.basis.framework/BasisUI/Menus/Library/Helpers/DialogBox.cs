using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Basis.BasisUI
{
    public class DialogBox<T>
    {
        private TaskCompletionSource<T> _tcs;
        private PanelElementDescriptor _background;
        private PanelElementDescriptor _descriptor;

        public PanelElementDescriptor Descriptor => _descriptor;
        public PanelElementDescriptor Background => _background;

        public bool IsBusy = false;

        // Notification-center metadata, captured at Create time so a dialog that is
        // torn down before it resolves can still be noted (and optionally re-opened).
        private bool _completed;
        private string _title;
        private string _description;
        private string _icon;
        private Action _reopen;

        private DialogBox() { }

        public static DialogBox<T> Create(
            BasisMenuPanel panel,
            Vector2 size,
            string title = null,
            string description = null,
            string icon = null,
            bool strongerOverlay = false,
            Action reopen = null
        )
        {
            DialogBox<T> box = new DialogBox<T>
            {
                _background = PanelElementDescriptor.CreateNew(
                    strongerOverlay ? PanelElementDescriptor.ElementStyles.OverlayLessOpacity : PanelElementDescriptor.ElementStyles.Overlay,
                    panel),
                _title = title,
                _description = description,
                _icon = icon,
                _reopen = reopen,
            };

            box._descriptor = PanelElementDescriptor.CreateNew(
                PanelElementDescriptor.ElementStyles.LibraryEntryOverlay,
                box._background);

            box._descriptor.rectTransform.localPosition = Vector3.zero;
            box._descriptor.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            box._descriptor.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            box._descriptor.rectTransform.anchoredPosition = Vector2.zero;
            box._descriptor.SetSize(size);

            if (!string.IsNullOrEmpty(title))
                box._descriptor.SetTitle(title);

            if (!string.IsNullOrEmpty(description))
                box._descriptor.SetDescription(description);

            if (icon != null)
                box._descriptor.SetIcon(icon);

            box._tcs = new TaskCompletionSource<T>();

            // If the overlay is destroyed without CloseWithResult (e.g. the parent
            // panel is released by a tab switch), complete the awaiting task and route
            // the dialog to the notification center instead of leaking the Task forever.
            DialogBoxReleaseWatcher watcher = box._background.gameObject.AddComponent<DialogBoxReleaseWatcher>();
            watcher.OnDestroyed = box.HandlePopped;

            return box;
        }

        public Task<T> WaitAsync()
        {
            return _tcs.Task;
        }

        public void CloseWithResult(T result)
        {
            _completed = true;

            if (_descriptor != null)
            {
                UnityEngine.Object.Destroy(_descriptor.gameObject);
                _descriptor = null;
            }

            if (_background != null)
            {
                UnityEngine.Object.Destroy(_background.gameObject);
                _background = null;
            }

            _tcs?.TrySetResult(result);
        }

        public void Cancel(T fallbackValue = default)
        {
            CloseWithResult(fallbackValue);
        }

        /// <summary>
        /// Called by the overlay's release watcher when it is destroyed without an
        /// explicit result. Notes the dialog in the notification center — re-openable
        /// when a reopen factory was supplied, otherwise logged as dismissed history —
        /// and completes the awaiting task so callers never hang.
        /// </summary>
        private void HandlePopped()
        {
            if (_completed) return;
            _completed = true;

            string title = string.IsNullOrEmpty(_title)
                ? BasisLocalization.Get("notifications.dialog.generic")
                : _title;
            string icon = _icon ?? AddressableAssets.Sprites.Information;

            if (_reopen != null)
            {
                BasisNotificationCenter.AddPending(title, _description, icon, _reopen);
            }
            else
            {
                BasisNotificationCenter.LogResolved(title, _description, icon, BasisNotificationStatus.Dismissed);
            }

            _tcs?.TrySetResult(default);
        }
    }
}
