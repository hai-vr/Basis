internal static class BasisNetworkMarkers
{
    const string Group = "BasisDriver.Network";
    const string TransmitGroup = Group + ".Transmit";
    public static readonly BasisMarker CommitAvatarAdds = new BasisMarker(Group, "CommitAvatarAdds");
    public static readonly BasisMarker TransmitFillPositions = new BasisMarker(TransmitGroup, "FillPositions"),
        TransmitCompress = new BasisMarker(TransmitGroup, "Compress"),
        TransmitJobComplete = new BasisMarker(TransmitGroup, "JobComplete"),
        TransmitPostProcess = new BasisMarker(TransmitGroup, "PostProcess"),
        TransmitAudioStartStop = new BasisMarker(TransmitGroup, "AudioStartStop"),
        TransmitReloadAvatar = new BasisMarker(TransmitGroup, "ReloadAvatar"),
        TransmitChangeMeshLOD = new BasisMarker(TransmitGroup, "ChangeMeshLOD"),
        TransmitTalkingPoints = new BasisMarker(TransmitGroup, "TalkingPoints");
    public static readonly BasisMarker TransmitFarLodShared = new BasisMarker(TransmitGroup, "FarLodShared"),
        TransmitFarLodBuild = new BasisMarker(TransmitGroup, "FarLodBuild"),
        TransmitFarLodFactory = new BasisMarker(TransmitGroup, "FarLodFactory"),
        TransmitFarLodInstall = new BasisMarker(TransmitGroup, "FarLodInstall"),
        TransmitFarLodReload = new BasisMarker(TransmitGroup, "FarLodReload");
}
