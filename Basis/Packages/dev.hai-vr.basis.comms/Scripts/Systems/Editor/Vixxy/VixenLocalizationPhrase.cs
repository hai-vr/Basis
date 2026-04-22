namespace HVR.Vixxy.Editor
{
public static class VixenLocalizationPhrase
    {
        private static string TEMP(string _, string defaultValue)
        {
            return defaultValue;
        }

        public static string AddBlendshapeLabel => TEMP(nameof(AddBlendshapeLabel), "Add blendshape");
        public static string AddLabel => TEMP(nameof(AddLabel), "Add");
        public static string AdditionalConditionsLabel => TEMP(nameof(AdditionalConditionsLabel), "Additional conditions");
        public static string AdvancedSettingsLabel => TEMP(nameof(AdvancedSettingsLabel), "Advanced Settings");
        public static string AnimationLabel => TEMP(nameof(AnimationLabel), "Animation");
        public static string AvatarLabel => TEMP(nameof(AvatarLabel), "Avatar");
        public static string BakeLabel => TEMP(nameof(BakeLabel), "Bake");
        public static string BaseSettingsLabel => TEMP(nameof(BaseSettingsLabel), "Base Settings");
        public static string BlendshapesLabel => TEMP(nameof(BlendshapesLabel), "Blendshapes");
        public static string ChangePropertiesLabel => TEMP(nameof(ChangePropertiesLabel), "Change Properties");
        public static string ChangeTheseObjectsAndTheirChildrenLabel => TEMP(nameof(ChangeTheseObjectsAndTheirChildrenLabel), "Change these objects and their children");
        public static string ChangeTheseObjectsLabel => TEMP(nameof(ChangeTheseObjectsLabel), "Change these objects");
        public static string ComplexConditionsLabel => TEMP(nameof(ComplexConditionsLabel), "Complex conditions");
        public static string ConditionGroupLabel => TEMP(nameof(ConditionGroupLabel), "Condition group");
        public static string ConditionalLabel => TEMP(nameof(ConditionalLabel), "Conditional");
        public static string ConditionsLabel => TEMP(nameof(ConditionsLabel), "Conditions");
        public static string ContactLabel => TEMP(nameof(ContactLabel), "Contact Sensor");
        public static string CreateNewClipLabel => TEMP(nameof(CreateNewClipLabel), "Create new clip");
        public static string CurrentLabel => TEMP(nameof(CurrentLabel), "Current");
        public static string DisableTheseWhenActiveLabel => TEMP(nameof(DisableTheseWhenActiveLabel), "Disable these when active");
        public static string DoNotChangeTheseObjectsLabel => TEMP(nameof(DoNotChangeTheseObjectsLabel), "Do not change these objects");
        public static string EditorLabel => TEMP(nameof(EditorLabel), "Editor");
        public static string EnableTheseWhenActiveLabel => TEMP(nameof(EnableTheseWhenActiveLabel), "Enable these when active");
        public static string ErrorsLabel => TEMP(nameof(ErrorsLabel), "Errors");
        public static string EverythingLabel => TEMP(nameof(EverythingLabel), "Everything");
        public static string ExportAnimationsLabel => TEMP(nameof(ExportAnimationsLabel), "Export Animations");
        public static string FolderNameLabel => TEMP(nameof(FolderNameLabel), "Folder Name");
        public static string HelpLabel => TEMP(nameof(HelpLabel), "Help (?)");
        public static string InterpolationDurationSecondsLabel => TEMP(nameof(InterpolationDurationSecondsLabel), "Interpolation Duration (s)");
        public static string InvalidReferenceLabel => TEMP(nameof(InvalidReferenceLabel), "Invalid reference");
        public static string InvalidSavePathLabel => TEMP(nameof(InvalidSavePathLabel), "Invalid save path");
        public static string JustPropertiesLabel => TEMP(nameof(JustPropertiesLabel), "Properties");
        public static string LabelActive => TEMP(nameof(LabelActive), "Active?");
        public static string MaterialLabel => TEMP(nameof(MaterialLabel), "Material");
        public static string MenuAndParameterLabel => TEMP(nameof(MenuAndParameterLabel), "Menu & Parameter");
        public static string MenuNameLabel => TEMP(nameof(MenuNameLabel), "Menu Name");
        public static string MenuSimpleLabel => TEMP(nameof(MenuSimpleLabel), "Menu (Simple)");
        public static string MsgAnimationFileAlreadyExists => TEMP(nameof(MsgAnimationFileAlreadyExists), "Asset already exists, you must not write over an existing animation.");
        public static string MsgConditionalBecauseNonRoot => TEMP(nameof(MsgConditionalBecauseNonRoot), "This is a Conditional because one of its parents is a Control.\nIf you didn't intend to, move this object outside of its parent.");
        public static string MsgConditionalsCannotBeBaked => TEMP(nameof(MsgConditionalsCannotBeBaked), "Conditionals cannot be baked.");
        public static string MsgEverythingInAvatar => TEMP(nameof(MsgEverythingInAvatar), "All valid objects in the avatar that contains these properties will be affected.");
        public static string MsgImplicitCondition => TEMP(nameof(MsgImplicitCondition), "In Simplified and Advanced modes, the condition is created for you. You can specify additional conditions here. All condition groups will also require the condition defined in the menu to pass.");
        public static string MsgInvalidReferencesOrFix => TEMP(nameof(MsgInvalidReferencesOrFix), "Some of the referenced objects aren't part of the avatar. You can press the button to Fix All, or check them beforehand.");
        public static string MsgOutsideOfAssets => TEMP(nameof(MsgOutsideOfAssets), "Save path must be in the project's /Assets path.");
        public static string MsgPlayModeWillNotSave => TEMP(nameof(MsgPlayModeWillNotSave), "You are in Play Mode. Modifications made to this component will NOT be saved when you exit Play Mode!");
        public static string MsgTooManyResults => TEMP(nameof(MsgTooManyResults), "Too many results to show Add buttons. Please enter a search (3 characters minimum), or type a space to show anyways.");
        public static string NonFXLayerTypeLabel => TEMP(nameof(NonFXLayerTypeLabel), "Non-FX Layer type");
        public static string ObjectGroupLabel => TEMP(nameof(ObjectGroupLabel), "Object group");
        public static string ObjectGroupsLabel => TEMP(nameof(ObjectGroupsLabel), "Object groups");
        public static string OscParameterLabel => TEMP(nameof(OscParameterLabel), "OSC Parameter");
        public static string OtherOptionsLabel => TEMP(nameof(OtherOptionsLabel), "Other options");
        public static string OtherPropertiesLabel => TEMP(nameof(OtherPropertiesLabel), "Other");
        public static string PhysBoneLabel => TEMP(nameof(PhysBoneLabel), "PhysBone Sensor");
        public static string PotentialEquivalenceLabel => TEMP(nameof(PotentialEquivalenceLabel), "Equivalence");
        public static string PotentialPathLabel => TEMP(nameof(PotentialPathLabel), "Potential path");
        public static string PreviewWhenActiveLabel => TEMP(nameof(PreviewWhenActiveLabel), "Preview when active");
        public static string PreviewWhenInactiveLabel => TEMP(nameof(PreviewWhenInactiveLabel), "Preview when inactive");
        public static string PropertiesLabel => TEMP(nameof(PropertiesLabel), "Properties");
        public static string PropertyLabel => TEMP(nameof(PropertyLabel), "Property");
        public static string RecursiveSearchLabel => TEMP(nameof(RecursiveSearchLabel), "Recursive search");
        public static string ResultsAreFilteredBySearchLabel => TEMP(nameof(ResultsAreFilteredBySearchLabel), "(results are filtered by search)");
        public static string SampleFromLabel => TEMP(nameof(SampleFromLabel), "Sample from");
        public static string SearchLabel => TEMP(nameof(SearchLabel), "Search");
        public static string ShowArraysLabel => TEMP(nameof(ShowArraysLabel), "Show arrays");
        public static string ShowValuesLabel => TEMP(nameof(ShowValuesLabel), "Show values");
        public static string ToggleObjectsLabel => TEMP(nameof(ToggleObjectsLabel), "Toggle Objects");
        public static string ValueLabel => TEMP(nameof(ValueLabel), "Value");
        //
        public static string AssignedFlagsLabel => TEMP(nameof(AssignedFlagsLabel), "Assigned Flags");
        public static string AssignedItemSlotsLabel => TEMP(nameof(AssignedItemSlotsLabel), "Assigned Item slots");
        public static string CrossControlEffects => TEMP(nameof(CrossControlEffects), "Cross-Control Effects");
        public static string DefaultLabel => TEMP(nameof(DefaultLabel), "Default");
        public static string EjectAndSetControlsLabel => TEMP(nameof(EjectAndSetControlsLabel), "Eject and Set Controls");
        public static string FixAllControlsInCurrentAvatarLabel => TEMP(nameof(FixAllControlsInCurrentAvatarLabel), "Fix All controls in current avatar");
        public static string FixLabel => TEMP(nameof(FixLabel), "Fix");
        public static string FlagLabel => TEMP(nameof(FlagLabel), "Flag");
        public static string InvalidLabel => TEMP(nameof(InvalidLabel), "Invalid");
        public static string ItemSlotsLabel => TEMP(nameof(ItemSlotsLabel), "Item slots");
        public static string ListenToFlagLabel => TEMP(nameof(ListenToFlagLabel), "Listen To Flag");
        public static string MsgImplicitConditionIncluded => TEMP(nameof(MsgImplicitConditionIncluded), "You can specify additional conditions here. All condition groups require the implicit condition from control itself to pass.");
        public static string NewFlagLabel => TEMP(nameof(NewFlagLabel), "+ New flag");
        public static string NewItemSlotLabel => TEMP(nameof(NewItemSlotLabel), "+ New item slot");
        public static string RaiseFlagsLabel => TEMP(nameof(RaiseFlagsLabel), "Raise Flags");
        public static string RegularLabel => TEMP(nameof(RegularLabel), "Regular");
        public static string SetToLabel => TEMP(nameof(SetToLabel), "Set to");
        public static string SwitchOffLabel => TEMP(nameof(SwitchOffLabel), "Switch OFF");
        public static string SwitchOnLabel => TEMP(nameof(SwitchOnLabel), "Switch ON");
        public static string WhenActiveLabel => TEMP(nameof(WhenActiveLabel), "When active");
        public static string WhenInactiveLabel => TEMP(nameof(WhenInactiveLabel), "When inactive");
        public static string WhenThisBecomesActiveLabel => TEMP(nameof(WhenThisBecomesActiveLabel), "When this becomes active");
        public static string WhenThisBecomesInactiveLabel => TEMP(nameof(WhenThisBecomesInactiveLabel), "When this becomes inactive");
        //
        public static string WrittenUnboundLabel => TEMP(nameof(WrittenUnboundLabel), "C");
        public static string SceneUnboundLabel => TEMP(nameof(SceneUnboundLabel), "S");
        //
        public static string IsOnByDefaultLabel => TEMP(nameof(IsOnByDefaultLabel), "Is ON By Default");
        public static string DefaultValueLabel => TEMP(nameof(IsOnByDefaultLabel), "Default Value");

        // # To localize later
    }
}
