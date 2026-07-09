using System.Collections.Generic;
using UnityEditor;
using UnityEngine.InputSystem;

namespace Basis.Setup.Modules
{
    public sealed class BasisInputActionsSetup : BasisSetupModuleBase
    {
        private const string Folder = "Assets/Basis/Settings/InputActions";
        private const string ActionsPath = Folder + "/InputAction.inputactions";
        private const string SettingsPath = Folder + "/InputSystem.inputsettings.asset";
        private const string SettingsConfigKey = "com.unity.input.settings";
        private const string ActionsConfigKey = "com.unity.input.settings.actions";

        public override string Key => "input.actions";
        public override string DisplayName => "Input Actions & Settings";
        public override string Category => "Input";
        public override int Version => 1;

        public override IEnumerable<string> OwnedPaths => new[] { Folder };

        protected override void Activate()
        {
            InputSettings settings = AssetDatabase.LoadAssetAtPath<InputSettings>(SettingsPath);
            if (settings != null)
            {
                EditorBuildSettings.AddConfigObject(SettingsConfigKey, settings, true);
            }

            InputActionAsset actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ActionsPath);
            if (actions != null)
            {
                EditorBuildSettings.AddConfigObject(ActionsConfigKey, actions, true);
            }
        }
    }
}
