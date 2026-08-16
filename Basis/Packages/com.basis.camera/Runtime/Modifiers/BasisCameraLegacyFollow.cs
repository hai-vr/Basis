using System;
using System.Collections.Generic;
using UnityEngine;

namespace Basis.Cinematics
{
    /// <summary>
    /// The auto-follow block as it was stored up to settings version 8, read straight off the same
    /// JSON so the fields can be carried into the modifier stack without <c>CameraSettings</c>
    /// having to keep them. JsonUtility ignores members the text does not mention and members the
    /// text mentions but the type does not, which is what lets a partial view like this parse a
    /// whole settings file.
    /// </summary>
    [Serializable]
    public class BasisCameraLegacyFollow
    {
        public const int UpgradedAtVersion = 9;

        public Vector3 autoFollowPositionOffset;
        public Vector3 autoFollowRotationOffset;
        public bool autoFollowPlayspace;
        public float autoFollowLookAtHeightOffset;
        public float autoFollowLateralTracking;
        public float subjectFramingRadius;

        public BasisCameraLegacyFollow()
        {
            autoFollowPositionOffset = new Vector3(0.5f, 0f, 1.4f);
            autoFollowRotationOffset = Vector3.zero;
            autoFollowPlayspace = true;
            autoFollowLookAtHeightOffset = 0f;
            autoFollowLateralTracking = 0.5f;
            subjectFramingRadius = 0.45f;
        }

        /// <summary>
        /// Carries the stored configuration onto a stack. The slots are deliberately left alone:
        /// whether follow was armed was never saved — a camera that restored armed would fly out of
        /// your hand the moment it spawned — so an upgraded file keeps its numbers and stays put.
        /// </summary>
        public void ApplyTo(BasisCameraModifierStack stack)
        {
            if (stack == null)
            {
                return;
            }

            stack.follow.positionOffset = autoFollowPositionOffset;
            stack.follow.lateralTracking = Mathf.Clamp01(autoFollowLateralTracking);
            stack.lookAt.rotationOffset = autoFollowRotationOffset;
            stack.compose.rotationOffset = autoFollowRotationOffset;

            stack.subject.anchorToBody = autoFollowPlayspace;
            stack.subject.aimHeightOffset = autoFollowLookAtHeightOffset;
            stack.subject.framingRadius = subjectFramingRadius > 0f ? subjectFramingRadius : 0.45f;

            stack.Sanitize();
        }

        /// <summary>
        /// Reads the legacy block out of a settings file. Returns false when the text is not JSON
        /// this can parse, so a corrupt file upgrades to the defaults rather than throwing.
        /// </summary>
        public static bool TryRead(string json, out BasisCameraLegacyFollow legacy)
        {
            legacy = null;
            if (string.IsNullOrEmpty(json))
            {
                return false;
            }

            try
            {
                legacy = JsonUtility.FromJson<BasisCameraLegacyFollow>(json);
            }
            catch (Exception)
            {
                legacy = null;
            }
            return legacy != null;
        }
    }

    /// <summary>
    /// A saved mode's legacy view: the follow block it carried, plus whether it was saved with
    /// follow or the shot rig armed, which is what decides the slots it upgrades into.
    /// </summary>
    [Serializable]
    public class BasisCameraLegacyMode
    {
        public string name;
        public bool autoFollow;
        public bool cinematic;
        public BasisCameraLegacyFollow settings = new BasisCameraLegacyFollow();
    }

    [Serializable]
    public class BasisCameraLegacyModeFile
    {
        public List<BasisCameraLegacyMode> modes = new List<BasisCameraLegacyMode>();

        public static bool TryRead(string json, out BasisCameraLegacyModeFile file)
        {
            file = null;
            if (string.IsNullOrEmpty(json))
            {
                return false;
            }

            try
            {
                file = JsonUtility.FromJson<BasisCameraLegacyModeFile>(json);
            }
            catch (Exception)
            {
                file = null;
            }
            return file?.modes != null;
        }

        /// <summary>
        /// The slots a mode saved before version 9 should wear. A mode saved with the shot rig on
        /// composes rather than hard-looks, matching what the rig's default shot did.
        /// </summary>
        public static void ResolveSlots(BasisCameraLegacyMode mode, BasisCameraModifierStack stack)
        {
            if (mode == null || stack == null)
            {
                return;
            }

            if (mode.cinematic)
            {
                stack.positionModifier = BasisCameraPositionModifier.FollowSubject;
                stack.rotationModifier = BasisCameraRotationModifier.Compose;
            }
            else if (mode.autoFollow)
            {
                stack.positionModifier = BasisCameraPositionModifier.FollowSubject;
                stack.rotationModifier = BasisCameraRotationModifier.LookAtSubject;
            }
        }
    }
}
