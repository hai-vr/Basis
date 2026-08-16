using System;
using UnityEngine;

namespace Basis.Cinematics
{
    /// <summary>Which frame a positional offset is authored in.</summary>
    public enum BasisCameraBindingMode
    {
        /// <summary>Offset turns with the subject, so it stays in front as they turn.</summary>
        SubjectYaw = 0,
        /// <summary>Offset is fixed to world axes; the subject can turn without moving the camera.</summary>
        WorldSpace = 1,
        /// <summary>Hold the distance but keep the current heading, so the camera never swings around behind.</summary>
        SimpleFollow = 2,
    }

    [Serializable]
    public struct BasisCameraFollowSettings
    {
        [Tooltip("Offset from the subject in the binding frame, in metres at default avatar scale: X right, Y up, Z forward.")]
        public Vector3 positionOffset;

        public BasisCameraBindingMode bindingMode;

        [Tooltip("Seconds to catch up per axis of the binding frame: X sideways, Y vertical, Z on approach.")]
        public Vector3 damping;

        [Tooltip("How strongly sideways subject movement pulls the camera in to keep the shot tight. 0 holds the fixed side offset; 1 closes the side gap fully while strafing.")]
        [Range(0f, 1f)] public float lateralTracking;

        [Tooltip("Snap instead of easing when the target is further away than this, in metres at default avatar scale.")]
        public float teleportDistance;

        public static BasisCameraFollowSettings Default => new BasisCameraFollowSettings
        {
            positionOffset = new Vector3(0.5f, 0f, 1.4f),
            bindingMode = BasisCameraBindingMode.SubjectYaw,
            damping = new Vector3(0.35f, 0.5f, 0.5f),
            lateralTracking = 0.5f,
            teleportDistance = 10f,
        };
    }

    /// <summary>
    /// Who the camera films, as opposed to how it moves — the third slot on the stack. Every
    /// position and rotation modifier resolves its subject through this one, which is why it is a
    /// slot rather than a setting repeated on each of them.
    /// </summary>
    [Serializable]
    public struct BasisCameraSubjectSettings
    {
        public BasisCameraSubjectModifier modifier;

        [Tooltip("Anchor to the subject's centre of mass so room-scale movement keeps them in frame. Off anchors to the playspace origin, which is steadier but ignores physical walking.")]
        public bool anchorToBody;

        [Tooltip("Shifts the aim point up or down from head height, in metres at default avatar scale. Negative aims lower down the body.")]
        public float aimHeightOffset;

        [Tooltip("Bounding radius assumed for one subject when framing, in metres at default scale.")]
        public float framingRadius;

        [Tooltip("Frame yourself as part of the group, alongside everyone else in it.")]
        public bool groupIncludesLocal;

        [Tooltip("The world point the camera films while Fixed Point is fitted.")]
        public Vector3 fixedPoint;

        public static BasisCameraSubjectSettings Default => new BasisCameraSubjectSettings
        {
            modifier = BasisCameraSubjectModifier.FollowPlayer,
            anchorToBody = true,
            aimHeightOffset = 0f,
            framingRadius = 0.45f,
            groupIncludesLocal = true,
            fixedPoint = Vector3.zero,
        };
    }

    [Serializable]
    public struct BasisCameraFramingSettings
    {
        [Tooltip("Direction the camera sits in from the subject. Its length is only a starting distance — the framing solve sets the real one.")]
        public Vector3 directionOffset;

        public BasisCameraBindingMode bindingMode;

        [Tooltip("Seconds to catch up per axis of the binding frame: X sideways, Y vertical, Z on approach.")]
        public Vector3 damping;

        [Tooltip("Share of the frame the subject should fill.")]
        [Range(0.05f, 1f)] public float screenFraction;

        public float minDistance;
        public float maxDistance;

        [Tooltip("Hold subject size by changing focal length instead of dollying.")]
        public bool usesZoom;

        [Tooltip("Snap instead of easing when the target is further away than this, in metres at default avatar scale.")]
        public float teleportDistance;

        public static BasisCameraFramingSettings Default => new BasisCameraFramingSettings
        {
            directionOffset = new Vector3(1.2f, 0.4f, 3f),
            bindingMode = BasisCameraBindingMode.SubjectYaw,
            damping = new Vector3(0.35f, 0.5f, 0.5f),
            screenFraction = 0.28f,
            minDistance = 0.6f,
            maxDistance = 12f,
            usesZoom = false,
            teleportDistance = 10f,
        };
    }

    [Serializable]
    public struct BasisCameraDollySettings
    {
        [Tooltip("Where on the track the camera sits, in waypoints. This is the playhead.")]
        public float position;

        public BasisCameraDollyMode mode;

        [Tooltip("Whether the move is running. Pausing holds the camera exactly where it had got to.")]
        public bool playing;

        [Tooltip("Seconds for the camera to catch up to its target point on the track.")]
        public float damping;

        [Tooltip("Metres per second the move travels along the track.")]
        public float speed;

        [Tooltip("Offset from the track, in the track's own frame.")]
        public Vector3 offset;

        public static BasisCameraDollySettings Default => new BasisCameraDollySettings
        {
            position = 0f,
            mode = BasisCameraDollyMode.Manual,
            playing = false,
            damping = 0.5f,
            speed = 1.5f,
            offset = Vector3.zero,
        };
    }

    [Serializable]
    public struct BasisCameraLookAtSettings
    {
        [Tooltip("Extra rotation applied after aiming, in degrees.")]
        public Vector3 rotationOffset;

        [Tooltip("Seconds to catch up per rotation axis: X pitch, Y yaw, Z roll.")]
        public Vector3 damping;

        public static BasisCameraLookAtSettings Default => new BasisCameraLookAtSettings
        {
            rotationOffset = Vector3.zero,
            damping = new Vector3(0.4f, 0.35f, 0.8f),
        };
    }

    [Serializable]
    public struct BasisCameraComposeSettings
    {
        public BasisComposerSettings composer;

        [Tooltip("Extra rotation applied after aiming, in degrees.")]
        public Vector3 rotationOffset;

        public static BasisCameraComposeSettings Default => new BasisCameraComposeSettings
        {
            composer = BasisComposerSettings.Default,
            rotationOffset = Vector3.zero,
        };
    }

    [Serializable]
    public struct BasisCameraMatchSubjectSettings
    {
        [Tooltip("Extra rotation applied after matching, in degrees.")]
        public Vector3 rotationOffset;

        [Tooltip("Seconds to catch up per rotation axis: X pitch, Y yaw, Z roll.")]
        public Vector3 damping;

        public static BasisCameraMatchSubjectSettings Default => new BasisCameraMatchSubjectSettings
        {
            rotationOffset = Vector3.zero,
            damping = new Vector3(0.4f, 0.35f, 0.8f),
        };
    }

    [Serializable]
    public struct BasisCameraLookAheadSettings
    {
        [Tooltip("Seconds of subject motion to lead by.")]
        public float time;

        [Tooltip("Furthest the lead is allowed to reach, in metres.")]
        public float limit;

        public static BasisCameraLookAheadSettings Default => new BasisCameraLookAheadSettings
        {
            time = 0.25f,
            limit = 2f,
        };
    }

    [Serializable]
    public struct BasisCameraOcclusionSettings
    {
        [Tooltip("Extra distance kept clear of whatever is in the way, in metres.")]
        public float padding;

        [Tooltip("Closest the camera may be pulled toward the subject, in metres.")]
        public float minDistance;

        [Tooltip("Seconds to ease back out once the shot is clear again. Pulling in is always instant.")]
        public float returnDamping;

        [Tooltip("Radius of the cast that looks for geometry between the camera and its subject.")]
        public float probeRadius;

        public static BasisCameraOcclusionSettings Default => new BasisCameraOcclusionSettings
        {
            padding = 0.25f,
            minDistance = 0.4f,
            returnDamping = 0.6f,
            probeRadius = 0.12f,
        };
    }

    [Serializable]
    public struct BasisCameraLensSettings
    {
        [Range(5f, 120f)] public float fov;

        [Tooltip("Seconds for the lens to reach a changed field of view.")]
        public float damping;

        public static BasisCameraLensSettings Default => new BasisCameraLensSettings
        {
            fov = 40f,
            damping = 0.5f,
        };
    }
}
