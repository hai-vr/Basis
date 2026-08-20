internal static class BasisLocalPlayerMarkers
{
    const string Group = "BasisDriver.LocalPlayer";
    const string MoveGroup = Group + ".Move";
    const string IKDestGroup = Group + ".IKDest";
    public static readonly BasisMarker LocoPoseSchedule = new BasisMarker(Group, "LocoPoseSchedule"),
        Movement = new BasisMarker(Group, "Movement"),
        PlayspaceMover = new BasisMarker(Group, "PlayspaceMover"),
        VirtualData = new BasisMarker(Group, "VirtualData"),
        LateSimulateBones = new BasisMarker(Group, "LateSimulateBones"),
        BoneDriver = new BasisMarker(Group, "BoneDriver"),
        VirtualSpine = new BasisMarker(Group, "VirtualSpine"),
        IKDestinations = new BasisMarker(Group, "IKDestinations"),
        Animator = new BasisMarker(Group, "Animator"),
        HandDriver = new BasisMarker(Group, "HandDriver"),
        AfterSimulateOnLate = new BasisMarker(Group, "AfterSimulateOnLate");
    public static readonly BasisMarker MoveSize = new BasisMarker(MoveGroup, "Size"),
        MoveMode = new BasisMarker(MoveGroup, "Mode"),
        MoveTurn = new BasisMarker(MoveGroup, "Turn"),
        MovePhysics = new BasisMarker(MoveGroup, "Physics");
    public static readonly BasisMarker IKDestPrep = new BasisMarker(IKDestGroup, "Prep"),
        IKDestFootSchedule = new BasisMarker(IKDestGroup, "FootSchedule"),
        IKDestGatherTargets = new BasisMarker(IKDestGroup, "GatherTargets"),
        IKDestFilters = new BasisMarker(IKDestGroup, "Filters"),
        IKDestFootJoin = new BasisMarker(IKDestGroup, "FootJoin"),
        IKDestBuildIKTargets = new BasisMarker(IKDestGroup, "BuildIKTargets"),
        IKDestLocoPoseJoin = new BasisMarker(IKDestGroup, "LocoPoseJoin"),
        IKDestPoseGather = new BasisMarker(IKDestGroup, "PoseGather"),
        IKDestApplyFit = new BasisMarker(IKDestGroup, "ApplyFit"),
        IKDestSolve = new BasisMarker(IKDestGroup, "Solve"),
        IKDestSolveJoin = new BasisMarker(IKDestGroup, "SolveJoin"),
        IKDestSolveGizmos = new BasisMarker(IKDestGroup, "SolveGizmos"),
        IKDestPoseScatter = new BasisMarker(IKDestGroup, "PoseScatter"),
        IKDestPublishWorldData = new BasisMarker(IKDestGroup, "PublishWorldData");
}
