using System;
using System.Collections.Generic;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Device_Management.Devices;
using Basis.Scripts.TransformBinders.BoneControl;
using Basis.Scripts.UI;
using Basis.Scripts.UI.UI_Panels;
using UnityEngine;

/// <summary>
/// Input action router for Basis. 
/// - Each behavior is its own function.
/// - Bind behaviors to devices (BasisBoneTrackedRole) at runtime via the public API.
/// - Call UpdatePlayerControl(...) per device as usual; the router invokes the bound behaviors for that device.
/// </summary>
public static class BasisActionDriver
{
    /// <summary>
    /// The individual behaviors this driver can execute.
    /// Add new entries here when you add new behavior functions.
    /// </summary>
    public enum ActionId
    {
        // Movement
        SetMovementSpeedMultiplierFromPrimary2DAxis,
        SetMovementVectorFromPrimary2DAxis,
        TickMovementSpeed,

        // UI / System
        ToggleHamburgerOnSecondaryRelease,
        ToggleMicOnPrimaryReleaseIfNoHover,

        // Camera/Character orientation & locomotion
        RotateFromPrimary2DAxis,
        JumpOnPrimaryButton
    }

    /// <summary>
    /// Bind a behavior to the device (tracked role) that should drive it.
    /// If the action was previously bound, the old binding is replaced.
    /// </summary>
    public static void Bind(ActionId action, BasisBoneTrackedRole role)
    {
        // Remove existing binding (if any)
        if (s_ActionToRole.TryGetValue(action, out var oldRole))
        {
            if (s_RoleToActions.TryGetValue(oldRole, out var list))
            {
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
        if (!actionsForRole.Contains(action))
        {
            actionsForRole.Add(action);
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
        }
        s_ActionToRole.Remove(action);
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
    public static void ResetDefaultBindings()
    {
        s_ActionToRole.Clear();
        s_RoleToActions.Clear();

        // Left hand — everything that was in PrimaryDevice()
        Bind(ActionId.SetMovementSpeedMultiplierFromPrimary2DAxis, BasisBoneTrackedRole.LeftHand);
        Bind(ActionId.SetMovementVectorFromPrimary2DAxis, BasisBoneTrackedRole.LeftHand);
        Bind(ActionId.TickMovementSpeed, BasisBoneTrackedRole.LeftHand);
        Bind(ActionId.ToggleHamburgerOnSecondaryRelease, BasisBoneTrackedRole.LeftHand);

        // Right hand — everything that was in SecondaryDevice()
        Bind(ActionId.RotateFromPrimary2DAxis, BasisBoneTrackedRole.RightHand);
        Bind(ActionId.JumpOnPrimaryButton, BasisBoneTrackedRole.RightHand);

        // Center eye — everything that was in HeadsDevice()
        Bind(ActionId.ToggleMicOnPrimaryReleaseIfNoHover, BasisBoneTrackedRole.CenterEye);
    }
    /// <summary>
    /// Call this once per device input update (same signature as before).
    /// The router will execute only the actions currently bound to <paramref name="trackedRole"/>.
    /// </summary>
    public static void UpdatePlayerControl(BasisBoneTrackedRole trackedRole, ref BasisInputState CurrentInputState, ref BasisInputState LastInputState)
    {
        if (!s_RoleToActions.TryGetValue(trackedRole, out var actions)) return;

        for (int i = 0; i < actions.Count; i++)
        {
            var actionId = actions[i];
            if (!s_ActionImpl.TryGetValue(actionId, out var actionImpl)) continue;

            try
            {
                actionImpl(ref CurrentInputState, ref LastInputState);
            }
            catch (Exception ex)
            {
                BasisDebug.LogError(ex, BasisDebug.LogTag.Input);
            }
        }
    }

    // Delegate type so we can pass ref states.
    public delegate void InputAction(ref BasisInputState current, ref BasisInputState last);

    // Map ActionId -> implementation
    private static readonly Dictionary<ActionId, InputAction> s_ActionImpl = new Dictionary<ActionId, InputAction>
    {
        { ActionId.SetMovementSpeedMultiplierFromPrimary2DAxis, SetMovementSpeedMultiplierFromPrimary2DAxis },
        { ActionId.SetMovementVectorFromPrimary2DAxis,          SetMovementVectorFromPrimary2DAxis },
        { ActionId.TickMovementSpeed,                           TickMovementSpeed },

        { ActionId.ToggleHamburgerOnSecondaryRelease,           ToggleHamburgerOnSecondaryRelease },
        { ActionId.ToggleMicOnPrimaryReleaseIfNoHover,          ToggleMicOnPrimaryReleaseIfNoHover },

        { ActionId.RotateFromPrimary2DAxis,                     RotateFromPrimary2DAxis },
        { ActionId.JumpOnPrimaryButton,                         JumpOnPrimaryButton },
    };

    /// <summary>
    /// Compute the largest absolute component of the primary 2D axis and apply it as a movement speed multiplier.
    /// </summary>
    public static void SetMovementSpeedMultiplierFromPrimary2DAxis(ref BasisInputState current, ref BasisInputState last)
    {
        var axis = current.Primary2DAxis;
        float largestValue = Mathf.Abs(axis.x) > Mathf.Abs(axis.y) ? axis.x : axis.y;
        var controller = BasisLocalPlayer.Instance?.LocalCharacterDriver;
        if (controller == null) return;

        controller.SetMovementSpeedMultiplier(largestValue);
    }

    /// <summary>
    /// Feed the raw primary 2D axis into the character movement vector.
    /// </summary>
    public static void SetMovementVectorFromPrimary2DAxis(ref BasisInputState current, ref BasisInputState last)
    {
        var controller = BasisLocalPlayer.Instance?.LocalCharacterDriver;
        if (controller == null) return;

        controller.SetMovementVector(current.Primary2DAxis);
    }

    /// <summary>
    /// Update movement speed (e.g., apply sprint/walk curves).
    /// </summary>
    public static void TickMovementSpeed(ref BasisInputState current, ref BasisInputState last)
    {
        var controller = BasisLocalPlayer.Instance?.LocalCharacterDriver;
        if (controller == null) return;

        // In the original code this was always 'true'.
        controller.UpdateMovementSpeed(true);
    }

    /// <summary>
    /// On Secondary Button RELEASE: toggle hamburger menu (open if closed, close if open).
    /// </summary>
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
    public static void RotateFromPrimary2DAxis(ref BasisInputState current, ref BasisInputState last)
    {
        var driver = BasisLocalPlayer.Instance?.LocalCharacterDriver;
        if (driver == null) return;

        driver.Rotation = current.Primary2DAxis;
    }

    /// <summary>
    /// While Primary Button is held, perform jump handling.
    /// </summary>
    public static void JumpOnPrimaryButton(ref BasisInputState current, ref BasisInputState last)
    {
        if (current.PrimaryButtonGetState)
        {
            BasisLocalPlayer.Instance?.LocalCharacterDriver?.HandleJump();
        }
    }


    private static readonly Dictionary<ActionId, BasisBoneTrackedRole> s_ActionToRole = new Dictionary<ActionId, BasisBoneTrackedRole>();
    private static readonly Dictionary<BasisBoneTrackedRole, List<ActionId>> s_RoleToActions = new Dictionary<BasisBoneTrackedRole, List<ActionId>>();
    private static readonly List<ActionId> s_EmptyActions = new List<ActionId>(0);

    // Initialize with the original layout so nothing changes unless you rebind.
    static BasisActionDriver()
    {
        ResetDefaultBindings();
    }
}
