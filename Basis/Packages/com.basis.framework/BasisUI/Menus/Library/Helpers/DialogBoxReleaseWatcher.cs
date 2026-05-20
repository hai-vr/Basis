using System;
using UnityEngine;

namespace Basis.BasisUI
{
    /// <summary>
    /// Fires once when its GameObject is destroyed. Attached to a <see cref="DialogBox{T}"/>
    /// overlay so the box can tell an explicit close (CloseWithResult) apart from being
    /// torn down with its parent panel — the latter is treated as "popped before answered".
    /// </summary>
    public sealed class DialogBoxReleaseWatcher : MonoBehaviour
    {
        public Action OnDestroyed;
        private bool _fired;

        private void OnDestroy()
        {
            if (_fired) return;
            _fired = true;

            Action handler = OnDestroyed;
            OnDestroyed = null;
            handler?.Invoke();
        }
    }
}
