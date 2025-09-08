using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Device_Management;
using Basis.Scripts.Device_Management.Devices;
using Basis.Scripts.TransformBinders.BoneControl;
using Basis.Scripts.UI;
using Basis.Scripts.UI.UI_Panels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Input action router for Basis, optimized for hot-path runtime performance.
/// - Compile per-role arrays of delegates once on (re)bind; Update only iterates a cached array (no per-frame dictionary lookups).
/// - Fast early-out in Bind() when rebinding to the same role (avoids list scans/duplication).
/// - Optional try/catch in editor/development builds only (no exception machinery on player builds).
/// - Batch rebuilds during ResetDefaultBindings() to avoid N× recompilations.
/// - fixed array indexed by ActionId (O(1) lookup at build time;
/// </summary>
public static class BasisActionDriver
{
    public const string FileName = "BasisActionBindingsV1.json";
    public const string FolderPath = "BasisActions"; // No leading slash!

    public static string SavePath => Path.Combine(Application.persistentDataPath, FolderPath, BasisDeviceManagement.StaticCurrentMode, FileName
    );

    /// <summary>
    /// True if a bindings file is present on disk.
    /// </summary>
    public static bool HasSavedBindings => File.Exists(SavePath);
    /// <summary>
    /// The individual behaviors this driver can execute.
    /// Add new entries here when you add new behavior functions.
    /// </summary>
    public enum ActionId
    {
        // Movement
        SetMovementSpeedMultiplierFromPrimary2DAxis = 0,
        SetMovementVectorFromPrimary2DAxis = 1,
        TickMovementSpeed = 2,

        // UI / System
        ToggleHamburgerOnSecondaryRelease = 3,
        ToggleMicOnPrimaryReleaseIfNoHover = 4,

        // Camera/Character orientation & locomotion
        RotateFromPrimary2DAxis = 5,
        JumpOnPrimaryButton = 6,

        // Keep this as the last entry for sizing arrays.
        Count = 7
    }
    /// <summary>
    /// Bind a behavior to the device (tracked role) that should drive it.
    /// If the action was previously bound, the old binding is replaced.
    /// </summary>
    public static void Bind(ActionId action, BasisBoneTrackedRole role)
    {
        // Fast path: if already bound to the same role, do nothing.
        if (s_ActionToRole.TryGetValue(action, out var oldRole) && EqualityComparer<BasisBoneTrackedRole>.Default.Equals(oldRole, role))
            return;

        // Remove existing binding (if any)
        if (s_ActionToRole.TryGetValue(action, out oldRole))
        {
            if (s_RoleToActions.TryGetValue(oldRole, out var list))
            {
                // Remove without Contains() branch — List.Remove handles the scan once.
                list.Remove(action);
            }
        }

        // Add new binding
        s_ActionToRole[action] = role;
        if (!s_RoleToActions.TryGetValue(role, out var actionsForRole))
        {
            actionsForRole = new List<ActionId>(8);
            s_RoleToActions[role] = actionsForRole;
        }
        actionsForRole.Add(action);

        // Rebuild compiled delegates for affected roles unless we're batching.
        if (!s_SuppressRebuild)
        {
            if (s_RoleToActions.TryGetValue(role, out _)) RebuildCompiledActionsForRole(role);
            if (s_ActionToRole.TryGetValue(action, out var prevRole)) // in case action moved
                if (!EqualityComparer<BasisBoneTrackedRole>.Default.Equals(prevRole, role))
                    RebuildCompiledActionsForRole(prevRole);
        }
    }

    /// <summary>
    /// Unbind a behavior from any device.
    /// </summary>
    public static void Unbind(ActionId action)
    {
        if (s_ActionToRole.TryGetValue(action, out var role))
        {
            if (s_RoleToActions.TryGetValue(role, out var list))
            {
                list.Remove(action);
            }
            s_ActionToRole.Remove(action);

            if (!s_SuppressRebuild)
                RebuildCompiledActionsForRole(role);
        }
    }

    /// <summary>
    /// Get the device (tracked role) currently bound to an action. Returns null if not bound.
    /// </summary>
    public static BasisBoneTrackedRole? GetBinding(ActionId action)
    {
        return s_ActionToRole.TryGetValue(action, out var role) ? role : (BasisBoneTrackedRole?)null;
    }

    /// <summary>
    /// Get all actions currently bound to a given device.
    /// </summary>
    public static IReadOnlyList<ActionId> GetActionsForRole(BasisBoneTrackedRole role)
    {
        return s_RoleToActions.TryGetValue(role, out var list) ? list : s_EmptyActions;
    }

    /// <summary>
    /// Restore the original ("legacy") layout:
    /// - LeftHand: movement (speed, vector, tick), UI toggle on secondary release
    /// - RightHand: rotate, jump
    /// - CenterEye: mic toggle on primary release (when not hovering UI)
    /// </summary>
    /// <param name="CurrentMode">device mode that we are in we should load based on this</param>
    /// <returns></returns>
    public static async Task LoadBindings()
    {
        s_ActionToRole.Clear();
        s_RoleToActions.Clear();
        s_RoleToCompiled.Clear();

        // Batch up the rebuilds to a single pass at the end.
        s_SuppressRebuild = true;

        // Left hand — everything that was in PrimaryDevice()
        Bind(ActionId.SetMovementSpeedMultiplierFromPrimary2DAxis, BasisBoneTrackedRole.LeftHand);
        Bind(ActionId.SetMovementVectorFromPrimary2DAxis, BasisBoneTrackedRole.LeftHand);
        Bind(ActionId.TickMovementSpeed, BasisBoneTrackedRole.LeftHand);
        Bind(ActionId.ToggleHamburgerOnSecondaryRelease, BasisBoneTrackedRole.LeftHand);

        // Right hand — everything that was in SecondaryDevice()
        Bind(ActionId.RotateFromPrimary2DAxis, BasisBoneTrackedRole.RightHand);
        Bind(ActionId.JumpOnPrimaryButton, BasisBoneTrackedRole.RightHand);
        if (BasisDeviceManagement.IsCurrentModeVR() == false)
        {
            Bind(ActionId.ToggleMicOnPrimaryReleaseIfNoHover, BasisBoneTrackedRole.CenterEye);
        }
        else
        {
            // Center eye — everything that was in HeadsDevice()
            Bind(ActionId.ToggleMicOnPrimaryReleaseIfNoHover, BasisBoneTrackedRole.LeftHand);
        }
        s_SuppressRebuild = false;
        RebuildAllCompiled();

        if (File.Exists(SavePath))
        {
            await LoadApplyToDriverAsync();
        }
        else
        {
            await SaveFromDriver();
        }
    }
    /// <summary>
    /// Call this once per device input update (same signature as before).
    /// The router will execute only the actions currently bound to <paramref name="trackedRole"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void UpdatePlayerControl(BasisBoneTrackedRole trackedRole, ref BasisInputState CurrentInputState, ref BasisInputState LastInputState)
    {
        if (!s_RoleToCompiled.TryGetValue(trackedRole, out var compiled) || compiled.Length == 0)
            return;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Keep diagnostics in editor/dev builds.
        for (int Index = 0; Index < compiled.Length; Index++)
        {
            var actionImpl = compiled[Index];
            try
            {
                actionImpl(ref CurrentInputState, ref LastInputState);
            }
            catch (Exception ex)
            {
                BasisDebug.LogError(ex, BasisDebug.LogTag.Input);
            }
        }
#else
        // Hot-path: no per-action try/catch in player builds.
        for (int Index = 0; Index < compiled.Length; Index++)
        {
            compiled[Index](ref CurrentInputState, ref LastInputState);
        }
#endif
    }
    public delegate void InputAction(ref BasisInputState current, ref BasisInputState last);
    /// <summary>
    /// Compute the largest absolute component of the primary 2D axis and apply it as a movement speed multiplier.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetMovementSpeedMultiplierFromPrimary2DAxis(ref BasisInputState current, ref BasisInputState last)
    {
        var axis = current.Primary2DAxis;
        float largestValue = Mathf.Abs(axis.x) > Mathf.Abs(axis.y) ? axis.x : axis.y;
        var controller = BasisLocalPlayer.Instance.LocalCharacterDriver;
        controller.SetMovementSpeedMultiplier(largestValue);
    }

    /// <summary>
    /// Feed the raw primary 2D axis into the character movement vector.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetMovementVectorFromPrimary2DAxis(ref BasisInputState current, ref BasisInputState last)
    {
        var controller = BasisLocalPlayer.Instance.LocalCharacterDriver;
        controller.SetMovementVector(current.Primary2DAxis);
    }

    /// <summary>
    /// Update movement speed (e.g., apply sprint/walk curves).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void TickMovementSpeed(ref BasisInputState current, ref BasisInputState last)
    {
        var controller = BasisLocalPlayer.Instance.LocalCharacterDriver;
        // In the original code this was always 'true'.
        controller.UpdateMovementSpeed(true);
    }

    /// <summary>
    /// On Secondary Button RELEASE: toggle hamburger menu (open if closed, close if open).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToggleHamburgerOnSecondaryRelease(ref BasisInputState current, ref BasisInputState last)
    {
        // Only act on release edge.
        if (current.SecondaryButtonGetState == false && last.SecondaryButtonGetState)
        {
            if (BasisHamburgerMenu.Instance == null)
            {
                BasisHamburgerMenu.OpenHamburgerMenuNow();
            }
            else
            {
                BasisHamburgerMenu.Instance.CloseThisMenu();
            }
        }
    }

    /// <summary>
    /// On Primary Button RELEASE (and when not hovering UI): toggle microphone paused state.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ToggleMicOnPrimaryReleaseIfNoHover(ref BasisInputState current, ref BasisInputState last)
    {
        if (current.PrimaryButtonGetState == false && last.PrimaryButtonGetState)
        {
            if (BasisInputModuleHandler.Instance.HasHoverONInput == false)
            {
                BasisLocalMicrophoneDriver.ToggleIsPaused();
            }
        }
    }

    /// <summary>
    /// Write the primary 2D axis into the local character driver's rotation vector.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void RotateFromPrimary2DAxis(ref BasisInputState current, ref BasisInputState last)
    {
        var driver = BasisLocalPlayer.Instance.LocalCharacterDriver;
        driver.Rotation = current.Primary2DAxis;
    }

    /// <summary>
    /// While Primary Button is held, perform jump handling.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void JumpOnPrimaryButton(ref BasisInputState current, ref BasisInputState last)
    {
        if (current.PrimaryButtonGetState)
        {
            BasisLocalPlayer.Instance.LocalCharacterDriver.HandleJump();
        }
    }

    // --------- INTERNAL: FAST LOOKUPS / COMPILED TABLES ---------

    // Fixed array (indexed by ActionId) holding the implementation delegates.
    // This is built once and never touched per frame.
    private static readonly InputAction[] s_ActionImplArray = new InputAction[(int)ActionId.Count]
    {
        SetMovementSpeedMultiplierFromPrimary2DAxis,   // 0
        SetMovementVectorFromPrimary2DAxis,            // 1
        TickMovementSpeed,                              // 2
        ToggleHamburgerOnSecondaryRelease,              // 3
        ToggleMicOnPrimaryReleaseIfNoHover,             // 4
        RotateFromPrimary2DAxis,                        // 5
        JumpOnPrimaryButton                             // 6
    };
    private static readonly Dictionary<ActionId, BasisBoneTrackedRole> s_ActionToRole = new Dictionary<ActionId, BasisBoneTrackedRole>(capacity: 16);
    private static readonly Dictionary<BasisBoneTrackedRole, List<ActionId>> s_RoleToActions = new Dictionary<BasisBoneTrackedRole, List<ActionId>>(capacity: 8);
    private static readonly Dictionary<BasisBoneTrackedRole, InputAction[]> s_RoleToCompiled = new Dictionary<BasisBoneTrackedRole, InputAction[]>(capacity: 8);
    private static readonly List<ActionId> s_EmptyActions = new List<ActionId>(0);
    private static readonly InputAction[] s_EmptyImpls = Array.Empty<InputAction>();
    private static bool s_SuppressRebuild;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void RebuildCompiledActionsForRole(BasisBoneTrackedRole role)
    {
        if (!s_RoleToActions.TryGetValue(role, out var list) || list == null || list.Count == 0)
        {
            s_RoleToCompiled[role] = s_EmptyImpls;
            return;
        }

        // Build without allocations beyond exactly what's needed.
        var count = list.Count;
        var compiled = new InputAction[count];
        for (int Index = 0; Index < count; Index++)
        {
            var action = list[Index];
            compiled[Index] = s_ActionImplArray[(int)action];
        }
        s_RoleToCompiled[role] = compiled;
    }

    private static void RebuildAllCompiled()
    {
        foreach (var kvp in s_RoleToActions)
        {
            RebuildCompiledActionsForRole(kvp.Key);
        }
    }

    /// <summary>
    /// Deletes the saved bindings file (if any).
    /// </summary>
    public static void DeleteSaveFile()
    {
        if (!File.Exists(SavePath)) return;

        try
        {
            File.Delete(SavePath);
            BasisDebug.Log($"Bindings Deleted {SavePath}", BasisDebug.LogTag.Input);
        }
        catch (Exception ex)
        {
            BasisDebug.LogError($"Bindings Failed to delete save file: {ex.Message}");
        }
    }

    /// <summary>
    /// Saves all currently bound actions from BasisActionDriver to disk.
    /// </summary>
    public static async Task SaveFromDriver()
    {
        // Emit records for bound actions only (skip unbound to keep JSON compact)
        List<BasisBindingRecord> list = new List<BasisBindingRecord>(16);

        foreach (ActionId action in Enum.GetValues(typeof(ActionId)))
        {
            if (action == ActionId.Count) continue;

            var role = BasisActionDriver.GetBinding(action);
            if (role.HasValue)
            {
                list.Add(new BasisBindingRecord
                {
                    action = action.ToString(),
                    role = role.Value.ToString()
                });
            }
        }

        BindingWrapper wrapper = new BindingWrapper { records = list.ToArray() };
      await  WriteWrapperToDisk(wrapper);
    }

    /// <summary>
    /// Loads bindings (if present) and applies them to BasisActionDriver.
    /// </summary>
    public static async Task LoadApplyToDriverAsync()
    {
        if (!File.Exists(SavePath))
        {
            return;
        }

        BindingWrapper wrapper;
        try
        {
            string json = await File.ReadAllTextAsync(SavePath);
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            wrapper = JsonUtility.FromJson<BindingWrapper>(json);
        }
        catch (Exception ex)
        {
            BasisDebug.LogError($"Bindings Failed to read/parse bindings file: {ex.Message}", BasisDebug.LogTag.Input);
            await SaveFromDriver();
            return;
        }

        if (wrapper.records == null || wrapper.records.Length == 0) return;

        // Apply saved bindings
        for (int Index = 0; Index < wrapper.records.Length; Index++)
        {
            var rec = wrapper.records[Index];
            if (!EnumTryParse(rec.action, out ActionId action)) continue;
            if (!EnumTryParse(rec.role, out BasisBoneTrackedRole role)) continue;

            BasisActionDriver.Bind(action, role);
        }
    }
    private static async Task WriteWrapperToDisk(BindingWrapper wrapper)
    {
        string json = JsonUtility.ToJson(wrapper, prettyPrint: true);

        try
        {
            // Ensure directory exists (persistentDataPath should exist, but just in case)
            var dir = Path.GetDirectoryName(SavePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string tmpPath = SavePath + ".tmp";
            await File.WriteAllTextAsync(tmpPath, json);
            if (File.Exists(SavePath))
            {
                File.Replace(tmpPath, SavePath, null);
            }
            else
            {
                File.Move(tmpPath, SavePath);
            }
#if UNITY_EDITOR
            BasisDebug.Log($"Bindings Saved {wrapper.records?.Length ?? 0} bindings to {SavePath}", BasisDebug.LogTag.Input);
#endif
        }
        catch (Exception ex)
        {
            BasisDebug.LogError($"Bindings Failed to save bindings to disk: {ex.Message}", BasisDebug.LogTag.Input);
        }
    }
    private static bool EnumTryParse<TEnum>(string s, out TEnum value) where TEnum : struct
    {
#if UNITY_2021_2_OR_NEWER
        return Enum.TryParse(s, ignoreCase: true, out value);
#else
            try { value = (TEnum)Enum.Parse(typeof(TEnum), s, true); return true; }
            catch { value = default; return false; }
#endif
    }
    [Serializable]
    public struct BasisBindingRecord
    {
        public string action;
        public string role;
    }

    [Serializable]
    public struct BindingWrapper
    {
        public BasisBindingRecord[] records;
    }
}
