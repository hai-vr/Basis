using System.Collections.Generic;
using Basis.Cinematics;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Drivers;
using UnityEngine;

/// <summary>
/// Drives the cinematic shot rig. Auto Follow stays the simple one-offset follow it always was;
/// turning Cinematic on hands the same resolved subject to <see cref="BasisCameraDirector"/>, which
/// runs whichever shot is live and blends when it changes.
/// </summary>
public abstract partial class BasisHandHeldCameraInteractable
{
    [Header("Cinematic Rig")]
    [Tooltip("Drives the camera from the shot list instead of the single follow offset.")]
    public bool cinematicEnabled = false;

    [Tooltip("Frames every listed player at once instead of a single target.")]
    public bool useTargetGroup = false;

    [Tooltip("Bounding radius assumed for one player when framing, in metres at default scale.")]
    public float subjectFramingRadius = 0.45f;

    [Tooltip("Radius of the cast that looks for geometry between a shot and its subject.")]
    public float occlusionProbeRadius = 0.12f;

    public BasisCameraDirector Director { get; private set; }
    public BasisCameraDollyTrack DollyTrack { get; private set; }

    /// <summary>Net ids framed by a group shot. 0 is the local player.</summary>
    public readonly List<ushort> TargetGroup = new List<ushort>();

    private const float SubjectVelocitySmoothing = 6f;

    private Vector3 smoothedSubjectVelocity;
    private Vector3 lastCinematicAnchor;
    private bool hasLastCinematicAnchor;

    private int occlusionMask = ~0;
    private bool occlusionMaskBuilt;

    private readonly List<BasisCameraSubject> groupSubjects = new List<BasisCameraSubject>();
    private BasisCameraOcclusionProbe occlusionProbe;

    /// <summary>Layers that never block a shot: the players themselves, UI, and the camera's own props.</summary>
    private static readonly string[] NonBlockingLayers =
    {
        "OverlayUI", "UI", "Ignore Raycast", "IgnoredByInteractable",
        "Player", "LocalPlayerAvatar", "RemotePlayerAvatar", "Interactable",
    };

    internal void InitializeCinematics()
    {
        Director ??= new BasisCameraDirector();
        DollyTrack ??= new BasisCameraDollyTrack();
        occlusionProbe ??= ProbeOcclusion;

        if (Director.Count == 0)
        {
            CreateDefaultShots();
        }
    }

    /// <summary>
    /// Rebuilds the dolly path from the live marker transforms and redraws it. Runs every frame
    /// whatever mode the camera is in — a track is normally laid out with the rig switched off, and
    /// gating this on the rig left placed points unnumbered, undrawn, and stale in the solve.
    /// </summary>
    internal void TickDollyTrack()
    {
        if (DollyTrack == null)
        {
            return;
        }
        // Runs even with no waypoints left: the point cache is rebuilt here, so skipping the empty
        // case would leave a solved shot riding the positions of points that have been deleted.
        DollyTrack.Refresh(BasisHeightDriver.AvatarToDefaultRatioScaledWithAvatarScale, GetLabelFacing());
    }

    internal void DisposeCinematics()
    {
        DollyTrack?.Dispose();
        DollyTrack = null;
        Director?.Clear();
    }

    /// <summary>
    /// Seeds the rig with the three setups that cover most of what the camera is used for, so the
    /// feature is usable before anything is authored.
    /// </summary>
    private void CreateDefaultShots()
    {
        BasisCameraShot follow = Director.AddShot();
        follow.name = "Follow";
        follow.priority = 10;
        follow.bodyMode = BasisCameraBodyMode.Transposer;
        follow.aimMode = BasisCameraAimMode.Composer;
        follow.positionOffset = autoFollowPositionOffset;
        follow.noise = BasisCameraNoiseSettings.ForProfile(BasisCameraNoiseProfile.Handheld);

        BasisCameraShot orbit = Director.AddShot();
        orbit.name = "Orbit";
        orbit.priority = 5;
        orbit.bodyMode = BasisCameraBodyMode.Orbital;
        orbit.aimMode = BasisCameraAimMode.Composer;
        orbit.blendTime = 1.5f;

        BasisCameraShot wide = Director.AddShot();
        wide.name = "Wide";
        wide.priority = 1;
        wide.bodyMode = BasisCameraBodyMode.Framing;
        wide.aimMode = BasisCameraAimMode.Composer;
        wide.positionOffset = new Vector3(1.2f, 0.4f, 3f);
        wide.framingScreenFraction = 0.28f;
        wide.blendTime = 2f;
    }

    /// <summary>
    /// One cinematic step. Replaces <see cref="MoveCameraAutoFollow"/> while the rig is on, and
    /// writes the same <c>smoothedPosition</c>/<c>smoothedRotation</c> the pin constraint consumes,
    /// so nothing downstream needs to know which mode produced the pose.
    /// </summary>
    private void MoveCameraCinematic(float deltaTime)
    {
        if (Director == null)
        {
            InitializeCinematics();
        }

        BasisCameraSubject subject = ResolveCinematicSubject(deltaTime);

        float scale = subject.Valid && subject.Scale > 1e-4f ? subject.Scale : 1f;
        if (subject.Valid)
        {
            if (hasLastCinematicAnchor &&
                Vector3.Distance(subject.AnchorPos, lastCinematicAnchor) > autoFollowTeleportDistance * scale)
            {
                Director.ReseedShots();
                // The jump registers as a velocity in the hundreds of metres per second. Left in the
                // filter it keeps look-ahead throwing the aim point for a second after the landing.
                smoothedSubjectVelocity = Vector3.zero;
                subject.Velocity = Vector3.zero;
            }
            lastCinematicAnchor = subject.AnchorPos;
            hasLastCinematicAnchor = true;
        }
        else
        {
            hasLastCinematicAnchor = false;
        }

        BasisCameraSolveContext context = new BasisCameraSolveContext
        {
            Subject = subject,
            Fov = GetCaptureFov(),
            Aspect = GetCaptureAspect(),
            DeltaTime = deltaTime,
            Time = Time.time,
            DollyPoints = DollyTrack.Points,
            DollyLooped = DollyTrack.Looped,
            ManualRotation = Quaternion.Euler(currentPitch, currentYaw, 0f),
            OcclusionProbe = occlusionProbe,
        };

        BasisCameraPose pose = Director.Solve(context);

        smoothedPosition = pose.Position;
        smoothedRotation = pose.Rotation;

        BasisCameraShot live = Director.GetShot(Director.LiveShotId);
        if (live != null && live.DrivesLens && HHC != null && HHC.captureCamera != null)
        {
            HHC.captureCamera.fieldOfView = Mathf.Clamp(pose.Fov, 1f, 179f);
        }
    }

    private float GetCaptureFov()
    {
        if (HHC != null && HHC.captureCamera != null)
        {
            return HHC.captureCamera.fieldOfView;
        }
        return 40f;
    }

    private float GetCaptureAspect()
    {
        if (HHC != null && HHC.captureCamera != null && HHC.captureCamera.aspect > 0f)
        {
            return HHC.captureCamera.aspect;
        }
        return 16f / 9f;
    }

    /// <summary>Billboards the waypoint numbers at whichever camera the operator is actually looking through.</summary>
    private Quaternion GetLabelFacing()
    {
        if (BasisLocalCameraDriver.HasInstance && BasisLocalCameraDriver.CameraInstance != null)
        {
            return BasisLocalCameraDriver.CameraInstance.transform.rotation;
        }
        return Quaternion.identity;
    }

    /// <summary>
    /// Resolves whoever is being filmed into the solver's own subject type, collapsing a group into
    /// a single bounded subject when group framing is on.
    /// </summary>
    private BasisCameraSubject ResolveCinematicSubject(float deltaTime)
    {
        BasisCameraSubject subject;

        if (useTargetGroup && TargetGroup.Count > 0)
        {
            groupSubjects.Clear();
            for (int Index = 0; Index < TargetGroup.Count; Index++)
            {
                if (TryResolveSubjectForNetId(TargetGroup[Index], out BasisCameraSubject member))
                {
                    groupSubjects.Add(member);
                }
            }

            if (!BasisCameraTargetGroup.TryCombine(groupSubjects, null, out subject))
            {
                subject = ToCinematicSubject(ResolveFollowSubject());
            }
        }
        else
        {
            subject = ToCinematicSubject(ResolveFollowSubject());
        }

        if (!subject.Valid)
        {
            smoothedSubjectVelocity = Vector3.zero;
            return subject;
        }

        if (hasLastCinematicAnchor && deltaTime > 1e-5f)
        {
            Vector3 instantaneous = (subject.AnchorPos - lastCinematicAnchor) / deltaTime;
            smoothedSubjectVelocity = Vector3.Lerp(smoothedSubjectVelocity, instantaneous,
                1f - Mathf.Exp(-SubjectVelocitySmoothing * deltaTime));
        }

        subject.Velocity = smoothedSubjectVelocity;
        return subject;
    }

    private bool TryResolveSubjectForNetId(ushort netId, out BasisCameraSubject subject)
    {
        if (netId == 0)
        {
            subject = ToCinematicSubject(ResolveLocalSubject());
            return subject.Valid;
        }

        if (Basis.Scripts.Networking.BasisNetworkPlayers.RemotePlayers.TryGetValue(netId, out var remote) &&
            remote != null && !remote.IsDestroyed &&
            TryResolveRemoteSubject(netId, remote, out FollowSubject resolved))
        {
            subject = ToCinematicSubject(resolved);
            return subject.Valid;
        }

        subject = default;
        return false;
    }

    private BasisCameraSubject ToCinematicSubject(in FollowSubject source)
        => new BasisCameraSubject
        {
            Valid = source.Valid,
            AnchorPos = source.AnchorPos,
            LookPoint = source.LookPoint,
            GroundPos = source.GroundPos,
            Yaw = source.Yaw,
            Scale = source.Scale > 1e-4f ? source.Scale : 1f,
            Radius = Mathf.Max(0.05f, subjectFramingRadius),
        };

    /// <summary>
    /// Casts from the subject out toward the shot looking for anything solid in between. Player and
    /// UI layers are excluded: the cast starts inside the subject's own collider, and a sphere cast
    /// that begins overlapping reports a zero-distance hit, which would slam the camera into their face.
    /// </summary>
    private bool ProbeOcclusion(Vector3 target, Vector3 desiredCameraPos, out float freeDistanceFromTarget)
    {
        freeDistanceFromTarget = 0f;

        Vector3 delta = desiredCameraPos - target;
        float distance = delta.magnitude;
        if (distance <= 1e-4f)
        {
            return false;
        }

        if (!occlusionMaskBuilt)
        {
            BuildOcclusionMask();
        }

        float radius = Mathf.Max(0.01f, occlusionProbeRadius);
        if (Physics.SphereCast(target, radius, delta / distance, out RaycastHit hit, distance, occlusionMask, QueryTriggerInteraction.Ignore))
        {
            freeDistanceFromTarget = hit.distance;
            return true;
        }
        return false;
    }

    private void BuildOcclusionMask()
    {
        int mask = ~0;
        for (int Index = 0; Index < NonBlockingLayers.Length; Index++)
        {
            int layer = LayerMask.NameToLayer(NonBlockingLayers[Index]);
            if (layer >= 0)
            {
                mask &= ~(1 << layer);
            }
        }
        occlusionMask = mask & ~BasisLayerMapper.HandHeldCameraUIMask;
        occlusionMaskBuilt = true;
    }

    public void SetCinematicEnabled(bool enabled)
    {
        if (cinematicEnabled == enabled)
        {
            return;
        }

        cinematicEnabled = enabled;
        InitializeCinematics();

        if (enabled)
        {
            if (HHC != null && HHC.captureCamera != null)
            {
                HHC.captureCamera.transform.GetPositionAndRotation(out smoothedPosition, out smoothedRotation);
            }
            Director.SnapTo(smoothedPosition, smoothedRotation, GetCaptureFov());
            PinSpace = CameraPinSpace.WorldSpace;
            hasLastCinematicAnchor = false;
        }
        else if (PinSpace == CameraPinSpace.WorldSpace && !autoFollowEnabled)
        {
            PinSpace = CameraPinSpace.HandHeld;
        }
    }

    // ---- Shot list API used by the panel -------------------------------------------------

    public BasisCameraShot AddShot(string name = null)
    {
        InitializeCinematics();
        BasisCameraShot shot = Director.AddShot();
        if (!string.IsNullOrEmpty(name))
        {
            shot.name = name;
        }
        return shot;
    }

    /// <summary>Copies the live shot, so a variant is authored by tweaking rather than rebuilding.</summary>
    public BasisCameraShot DuplicateShot(int id)
    {
        InitializeCinematics();
        BasisCameraShot source = Director.GetShot(id);
        if (source == null)
        {
            return null;
        }

        BasisCameraShot copy = Director.AddShot(source);
        copy.name = source.name + " Copy";
        return copy;
    }

    public bool RemoveShot(int id) => Director != null && Director.RemoveShot(id);

    public bool MoveShot(int id, int newIndex) => Director != null && Director.MoveShot(id, newIndex);

    public void SelectShot(int id)
    {
        InitializeCinematics();
        Director.SelectedShotId = id;
    }

    public BasisCameraShot GetLiveShot() => Director?.GetShot(Director.LiveShotId);

    // ---- Dolly queue API used by the panel ------------------------------------------------

    /// <summary>
    /// Drops a waypoint where the camera is now and appends it to the queue, which is how a track
    /// gets built: fly the shot to a spot, place a point, repeat.
    /// </summary>
    public BasisCameraDollyWaypoint SpawnWaypointAtCamera()
    {
        InitializeCinematics();

        Vector3 position;
        Quaternion rotation;
        if (HHC != null && HHC.captureCamera != null)
        {
            HHC.captureCamera.transform.GetPositionAndRotation(out position, out rotation);
        }
        else
        {
            transform.GetPositionAndRotation(out position, out rotation);
        }

        return DollyTrack.AddWaypoint(position, rotation, BasisHeightDriver.AvatarToDefaultRatioScaledWithAvatarScale);
    }

    /// <summary>Drops a waypoint an arm's length in front of the player, for building a track on foot.</summary>
    public BasisCameraDollyWaypoint SpawnWaypointInFrontOfPlayer()
    {
        InitializeCinematics();

        float scale = BasisHeightDriver.AvatarToDefaultRatioScaledWithAvatarScale;
        Vector3 position;
        Quaternion rotation = Quaternion.identity;

        if (BasisLocalPlayer.Instance != null)
        {
            BasisLocalPlayer.Instance.transform.GetPositionAndRotation(out Vector3 root, out Quaternion rot);
            Quaternion yaw = Quaternion.Euler(0f, rot.eulerAngles.y, 0f);
            position = root + yaw * new Vector3(0f, 1.2f * scale, 1f * scale);
            rotation = yaw;
        }
        else
        {
            transform.GetPositionAndRotation(out position, out _);
        }

        return DollyTrack.AddWaypoint(position, rotation, scale);
    }

    public bool RemoveWaypoint(int index) => DollyTrack != null && DollyTrack.RemoveWaypointAt(index);

    /// <summary>
    /// Moves a waypoint to a different slot in the queue. The marker does not move — only where the
    /// path visits it, which is what the panel's position field edits.
    /// </summary>
    public bool SetWaypointQueuePosition(int currentIndex, int newIndex)
        => DollyTrack != null && DollyTrack.MoveWaypoint(currentIndex, newIndex);

    public void ClearDollyTrack() => DollyTrack?.Clear();

    public int DollyWaypointCount => DollyTrack?.Count ?? 0;

    // ---- Persistence ----------------------------------------------------------------------

    /// <summary>Snapshot of the authored shots for the settings file.</summary>
    public List<BasisCameraShot> SaveShots()
    {
        List<BasisCameraShot> saved = new List<BasisCameraShot>();
        if (Director == null)
        {
            return saved;
        }

        IReadOnlyList<BasisCameraShot> shots = Director.Shots;
        for (int Index = 0; Index < shots.Count; Index++)
        {
            saved.Add(shots[Index].Clone());
        }
        return saved;
    }

    /// <summary>
    /// Replaces the rig with a saved shot list. An empty or missing list rebuilds the starter
    /// shots rather than leaving the rig with nothing to run, which is what a pre-rig settings
    /// file and a first launch both look like.
    /// </summary>
    public void LoadShots(List<BasisCameraShot> shots)
    {
        InitializeCinematics();

        if (shots == null || shots.Count == 0)
        {
            if (Director.Count == 0)
            {
                CreateDefaultShots();
            }
            return;
        }

        Director.Clear();
        for (int Index = 0; Index < shots.Count; Index++)
        {
            if (shots[Index] != null)
            {
                Director.AddShot(shots[Index]);
            }
        }

        if (Director.Count == 0)
        {
            CreateDefaultShots();
        }
    }
}
