using Basis.Scripts.Drivers;
using System;
using UnityEngine;

namespace Basis.Scripts.BasisSdk.Players
{
    /// <summary>
    /// Shared contract for local and remote players. Lets systems hold a player
    /// without caring whether it is a <see cref="MonoBehaviour"/> (local) or a plain
    /// managed object (remote). Exposes the player's backing <see cref="GameObject"/>
    /// and <see cref="Transform"/> explicitly so callers never depend on the
    /// implementation being a Unity component.
    /// </summary>
    public interface IBasisPlayer
    {
        bool IsLocal { get; set; }
        string PlayerPlatform { get; set; }
        string DisplayName { get; set; }
        string UUID { get; set; }
        string SafeDisplayName { get; set; }

        BasisAvatar BasisAvatar { get; set; }
        Transform AvatarTransform { get; set; }
        Transform AvatarAnimatorTransform { get; set; }
        Transform PlayerSelf { get; set; }

        BasisProgressReport ProgressReportAvatarLoad { get; }
        BasisProgressReport AvatarProgress { get; }
        Action AudioReceived { get; set; }

        bool FaceIsVisible { get; set; }
        BasisMeshRendererCheck FaceRenderer { get; set; }

        bool IsConsideredFallBackAvatar { get; set; }
        byte AvatarLoadMode { get; set; }
        BasisLoadableBundle AvatarMetaData { get; set; }

        /// <summary>The GameObject this player lives on (local component host or remote-owned root).</summary>
        GameObject GameObject { get; }
        /// <summary>The player's anchor transform (local component host or remote mouth marker).</summary>
        Transform Transform { get; }
        /// <summary>
        /// Parent for the player's avatar when instantiated. Local avatars live under the
        /// local player; remote avatars are scene roots (null) so the bone jobs spread across
        /// worker threads instead of sharing one Transform.root.
        /// </summary>
        Transform AvatarParent { get; }
        /// <summary>
        /// True once the player has been torn down. Replaces the Unity <c>this == null</c>
        /// check for the remote player, which is no longer a UnityEngine.Object.
        /// </summary>
        bool IsDestroyed { get; }

        event Action OnAvatarSwitched;

        void SetSafeDisplayname();
        void UpdateFaceVisibility(bool State);
        void AvatarSwitched();
    }
}
