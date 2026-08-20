internal static class BasisOpenVRMarkers
{
    const string Group = "BasisDriver.DeviceManagement";
    public static readonly BasisMarker JoinInput = new BasisMarker(Group, "JoinInput"),
        HMDPresence = new BasisMarker(Group, "HMDPresence");
}
