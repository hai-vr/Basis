internal static class BasisEerieMarkers
{
    const string Group = "BasisEerie";
    const string SpineGroup = Group + ".Spine";
    public static readonly BasisMarker Spine = new BasisMarker(Group + ".Spine"),
        Shoulders = new BasisMarker(Group + ".Shoulders"),
        Legs = new BasisMarker(Group + ".Legs"),
        Arms = new BasisMarker(Group + ".Arms"),
        Toes = new BasisMarker(Group + ".Toes"),
        TrackerOverrides = new BasisMarker(Group + ".TrackerOverrides");
    public static readonly BasisMarker SpineHipsPlacement = new BasisMarker(SpineGroup + ".HipsPlacement"),
        SpineChainPrep = new BasisMarker(SpineGroup + ".ChainPrep"),
        SpineSequentialIK = new BasisMarker(SpineGroup + ".SequentialIK"),
        SpineLordosis = new BasisMarker(SpineGroup + ".Lordosis");
}
