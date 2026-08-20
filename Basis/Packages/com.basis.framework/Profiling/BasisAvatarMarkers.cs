internal static class BasisAvatarMarkers
{
    const string Group = "BasisDriver.Avatar";
    const string InstallGroup = Group + ".Install";
    const string CalibrateGroup = Group + ".Calibrate";
    const string RegisterGroup = CalibrateGroup + ".BoneJobRegister";
    public static readonly BasisMarker Install = new BasisMarker(Group, "Install"),
        Calibrate = new BasisMarker(Group, "Calibrate");
    public static readonly BasisMarker InstallUnregisterOld = new BasisMarker(InstallGroup, "UnregisterOld"),
        InstallDeleteLast = new BasisMarker(InstallGroup, "DeleteLast"),
        InstallHarvest = new BasisMarker(InstallGroup, "Harvest"),
        InstallPerfTrim = new BasisMarker(InstallGroup, "PerfTrim");
    public static readonly BasisMarker CalibrateTpose = new BasisMarker(CalibrateGroup, "Tpose"),
        CalibrateDetectReferences = new BasisMarker(CalibrateGroup, "DetectReferences"),
        CalibrateBoneData = new BasisMarker(CalibrateGroup, "BoneData"),
        CalibrateBodyFit = new BasisMarker(CalibrateGroup, "BodyFit"),
        CalibrateFace = new BasisMarker(CalibrateGroup, "Face"),
        CalibrateRenderers = new BasisMarker(CalibrateGroup, "Renderers"),
        CalibrateJiggle = new BasisMarker(CalibrateGroup, "Jiggle");
    public static readonly BasisMarker CalibrateBoneJobRegister = new BasisMarker(CalibrateGroup, "BoneJobRegister"),
        CalibrateBoneJobRegisterSlotSeed = new BasisMarker(RegisterGroup, "SlotSeed"),
        CalibrateBoneJobRegisterAdd = new BasisMarker(RegisterGroup, "Add");
}
