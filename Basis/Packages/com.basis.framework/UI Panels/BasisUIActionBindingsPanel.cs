using Basis.Scripts.TransformBinders.BoneControl;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
// Short alias so we can refer to the Action enum cleanly.
using ActionId = BasisActionDriver.ActionId;

namespace Basis.Scripts.UI.UI_Panels
{
    public class BasisUIActionBindingsPanel : BasisUIBase
    {
        public static string ActionBindings = "ActionBindings";
        public BasisUIMovementDriver BasisUIMovementDriver;

        public override void DestroyEvent()
        {
            BasisCursorManagement.LockCursor(nameof(BasisUISettings));
            BasisUIMovementDriver.DeInitalize();
        }

        public override void InitalizeEvent()
        {
            BasisCursorManagement.UnlockCursor(nameof(BasisUISettings));
        }

        [Header("UI References")]
        [Tooltip("Where rows will be instantiated (should have a VerticalLayoutGroup).")]
        [SerializeField] private Transform rowsParent;

        [Tooltip("Prefab with a Text + Dropdown (see BindingRowUI).")]
        [SerializeField] private BasisBindingRowUI rowPrefab;

        [Space]
        [SerializeField] private Button saveButton;
        [SerializeField] private Button resetButton;

        [Header("Behavior")]
        [Tooltip("If true, saves immediately to disk whenever a binding changes.")]
        [SerializeField] private bool autoSaveOnChange = true;

        private readonly List<BasisBindingRowUI> _rows = new List<BasisBindingRowUI>(16);

        // Cache the role list we expose in the dropdown.
        private BasisBoneTrackedRole[] _roles;
        private string[] _roleNames; // pretty names in same order as _roles (index +1 in dropdown, +0 is Unbound)

        private void Awake()
        {
            // Build role list from enum (stable order).
            _roles = (BasisBoneTrackedRole[])Enum.GetValues(typeof(BasisBoneTrackedRole));

            // Build pretty names WITHOUT LINQ.
            _roleNames = new string[_roles.Length];
            for (int i = 0; i < _roles.Length; i++)
            {
                _roleNames[i] = PrettyEnumName(_roles[i].ToString());
            }

            WireButtons(true);
        }

        private async void OnEnable()
        {
            RebuildRows();

            // Load from disk (if present) and apply to driver.
            if (BasisActionDriver.HasSavedBindings)
            {
                await BasisActionDriver.LoadApplyToDriverAsync();
            }

            // Reflect the driver state into the UI.
            RefreshSelectionsFromDriver();
        }

        private void OnDisable()
        {
            // (Optional) Persist on close if auto-save is off but you still prefer saving.
            if (!autoSaveOnChange)
            {
                SaveToDisk();
            }
        }

        private void WireButtons(bool on)
        {
            if (on)
            {
                saveButton.onClick.AddListener(SaveToDisk);
                resetButton.onClick.AddListener(ResetToDefault);
            }
            else
            {
                saveButton.onClick.RemoveListener(SaveToDisk);
                resetButton.onClick.RemoveListener(ResetToDefault);
            }
        }

        private void RebuildRows()
        {
            // Clear existing
            for (int i = _rows.Count - 1; i >= 0; i--)
            {
                if (_rows[i]) Destroy(_rows[i].gameObject);
            }
            _rows.Clear();

            // Create one row per action (skip Count sentinel)
            foreach (ActionId action in Enum.GetValues(typeof(ActionId)))
            {
                if (action == ActionId.Count)
                {
                    continue;
                }

                var row = Instantiate(rowPrefab, rowsParent);
                row.gameObject.name = $"Row_{action}";
                row.SetLabel(PrettyEnumName(action.ToString()));
                row.gameObject.SetActive(true);

                // Build options: Unbound + roles
                var options = new List<string>(_roles.Length + 1) { "Unbound" };
                for (int i = 0; i < _roleNames.Length; i++)
                    options.Add(_roleNames[i]);

                row.SetOptions(options);

                // Hook change
                row.SetOnValueChanged(idx => OnRowChanged(action, idx));

                _rows.Add(row);
            }
        }

        private void RefreshSelectionsFromDriver()
        {
            // For each row/action, read binding and set dropdown index
            for (int index = 0; index < _rows.Count; index++)
            {
                var action = (ActionId)index; // relies on enum being contiguous from 0..Count-1
                var binding = BasisActionDriver.GetBinding(action);

                int dropdownIndex = 0; // Unbound by default
                if (binding.HasValue)
                {
                    // Find the role in our _roles list
                    var role = binding.Value;
                    int roleIdx = Array.IndexOf(_roles, role);
                    if (roleIdx >= 0) dropdownIndex = roleIdx + 1; // +1 because 0 is Unbound
                }
                _rows[index].SetValueWithoutNotify(dropdownIndex);
            }
        }

        private void OnRowChanged(ActionId action, int dropdownIndex)
        {
            // 0 => Unbound
            if (dropdownIndex <= 0)
            {
                BasisActionDriver.Unbind(action);
            }
            else
            {
                var role = _roles[dropdownIndex - 1];
                BasisActionDriver.Bind(action, role);
            }

            if (autoSaveOnChange)
            {
                SaveToDisk();
            }
        }

        private async void ResetToDefault()
        {
            await BasisActionDriver.LoadBindings();

            // Remove any persistent custom layout on disk
            BasisActionDriver.DeleteSaveFile();

            RefreshSelectionsFromDriver();
        }
        private async void SaveToDisk()
        {
            await BasisActionDriver.SaveFromDriver();
        }

        private static string PrettyEnumName(string raw)
        {
            // Turn "ToggleMicOnPrimaryReleaseIfNoHover" -> "Toggle Mic On Primary Release If No Hover"
            if (string.IsNullOrEmpty(raw)) return raw;
            var chars = new List<char>(raw.Length + 8);
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (i > 0 && char.IsUpper(c) && (char.IsLower(raw[i - 1]) || (i + 1 < raw.Length && char.IsLower(raw[i + 1]))))
                    chars.Add(' ');
                chars.Add(c);
            }
            return new string(chars.ToArray());
        }
    }
}
