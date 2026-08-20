internal static class BasisImagePickupMarkers
{
    const string Group = "Basis.ImagePickup.AnimatedImage";
    public static readonly BasisMarker DepthPrepare = new BasisMarker(Group, "DepthPrepare"),
        DepthReadback = new BasisMarker(Group, "DepthReadback"),
        Schedule = new BasisMarker(Group, "Schedule"),
        GpuCommands = new BasisMarker(Group, "GpuCommands"),
        JobFlush = new BasisMarker(Group, "JobFlush"),
        CpuFrontFacing = new BasisMarker(Group, "CpuFrontFacing");
}
