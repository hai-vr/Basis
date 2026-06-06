using Basis.Scripts.Drivers;
using System;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Basis.Scripts.BasisSdk.Players
{
    /// <summary>
    /// Base component for the local player within the Basis SDK. The remote player no
    /// longer derives from this type — it is a plain managed object implementing
    /// <see cref="IBasisPlayer"/>. Shared state both players need is declared on the
    /// interface; anything below that is not on the interface (the poll hooks, mode
    /// constants) is local-only.
    /// </summary>
    public abstract class BasisPlayer : MonoBehaviour, IBasisPlayer
    {
        /// <summary>
        /// Indicates whether this player represents the local user.
        /// </summary>
        public bool IsLocal { get; set; }

        /// <summary>
        /// Platform this player is associated with.
        /// </summary>
        public string PlayerPlatform { get; set; }

        /// <summary>
        /// Raw (untrusted) display name as provided by the source (user or network).
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// Unique identifier for the player.
        /// </summary>
        public string UUID { get; set; }

        /// <summary>
        /// Display-safe version of <see cref="DisplayName"/> with formatting tags removed.
        /// </summary>
        public string SafeDisplayName { get; set; }

        /// <summary>
        /// Active avatar instance for this player, if any.
        /// </summary>
        public BasisAvatar BasisAvatar { get; set; }

        /// <summary>
        /// Root transform for the avatar representation (if separate from the player object).
        /// </summary>
        public Transform AvatarTransform { get; set; }

        /// <summary>
        /// Transform of the avatar's animator component
        /// </summary>
        public Transform AvatarAnimatorTransform { get; set; }

        /// <summary>
        /// Cached self transform for quick access.
        /// </summary>
        public Transform PlayerSelf { get; set; } // yes caching myself is faster.

        /// <summary>The GameObject this player lives on.</summary>
        public GameObject GameObject => gameObject;

        /// <summary>The player's root transform.</summary>
        public Transform Transform => transform;

        /// <summary>Local avatars are parented under the local player.</summary>
        public Transform AvatarParent => transform;

        /// <summary>
        /// Local players are MonoBehaviours, so Unity's overloaded == reports destruction.
        /// </summary>
        public bool IsDestroyed => this == null;

        /// <summary>
        /// Raised when the player's avatar switches to a new one (non-fallback).
        /// </summary>
        public event Action OnAvatarSwitched;

        /// <summary>
        /// Progress reporter for the current avatar load operation (high-level).
        /// </summary>
        public BasisProgressReport ProgressReportAvatarLoad { get; } = new BasisProgressReport();

        /// <summary>
        /// Network-downloadable avatar load mode constant (value <c>0</c>).
        /// </summary>
        public const byte LoadModeNetworkDownloadable = 0;

        /// <summary>
        /// Local avatar load mode constant (value <c>1</c>).
        /// </summary>
        public const byte LoadModeLocal = 1;

        /// <summary>
        /// Error avatar load mode constant (value <c>2</c>).
        /// </summary>
        public const byte LoadModeError = 2;

        /// <summary>
        /// Whether the face portion of the avatar is currently visible.
        /// </summary>
        public bool FaceIsVisible { get; set; }

        /// <summary>
        /// Helper used to determine whether a face renderer is currently visible to the camera.
        /// </summary>
        public BasisMeshRendererCheck FaceRenderer { get; set; }

        /// <summary>
        /// Fine-grained progress reporter for avatar operations.
        /// </summary>
        public BasisProgressReport AvatarProgress { get; } = new BasisProgressReport();

        /// <summary>
        /// Callback invoked when audio data is received for this player.
        /// </summary>
        public Action AudioReceived { get; set; }

        /// <summary>
        /// Delegate signature for simulation hooks (e.g., pre-bone simulation).
        /// </summary>
        public delegate void SimulationHandler();

        /// <summary>
        /// Called before bone simulation updates for this player, if subscribed.
        /// </summary>
        public SimulationHandler OnLatePollData;

        /// <summary>
        /// Called before bone simulation updates for this player, if subscribed.
        /// </summary>
        public SimulationHandler OnRenderPollData;

        /// <summary>
        /// Called before bone simulation updates for this player, if subscribed.
        /// </summary>
        public SimulationHandler OnVirtualData;

        /// <summary>
        /// Whether the currently active avatar is considered a fallback (placeholder) asset.
        /// </summary>
        public bool IsConsideredFallBackAvatar { get; set; } = true;

        /// <summary>
        /// The current avatar load mode for this player (0 = downloading, 1 = local).
        /// </summary>
        public byte AvatarLoadMode { get; set; } // 0 downloading 1 local

        /// <summary>
        /// Metadata describing the avatar bundle used to create the current avatar.
        /// </summary>
        public BasisLoadableBundle AvatarMetaData { get; set; }

        /// <summary>
        /// Computes and stores a display-safe version of <see cref="DisplayName"/> by stripping any &lt;...&gt; tags.
        /// </summary>
        public void SetSafeDisplayname()
        {
            // Regex pattern to match any <...> tags
            SafeDisplayName = Regex.Replace(DisplayName, "<.*?>", string.Empty);
        }

        /// <summary>
        /// Updates whether the face is considered visible.
        /// </summary>
        /// <param name="State">True if the face is visible; otherwise false.</param>
        public void UpdateFaceVisibility(bool State)
        {
            FaceIsVisible = State;
        }

        /// <summary>
        /// Triggers the <see cref="OnAvatarSwitched"/> event.
        /// </summary>
        public void AvatarSwitched()
        {
            OnAvatarSwitched?.Invoke();
        }
    }
}
