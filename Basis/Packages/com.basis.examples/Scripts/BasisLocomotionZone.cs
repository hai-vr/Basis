using Basis.Scripts.BasisCharacterController;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Drivers;
using UnityEngine;

/// <summary>
/// Overrides the local player's jump height, movement speeds and character controller mode while
/// they stand inside this trigger. Each zone registers under its own key, so overlapping zones stack
/// and leaving one restores whatever the zone underneath asked for rather than a guessed default.
/// </summary>
public class BasisLocomotionZone : MonoBehaviour
{
    public bool OverrideJumpHeight = false;
    public float JumpHeight = 1.0f;

    public bool OverrideWalkSpeed = false;
    public float WalkSpeed = 2.5f;

    public bool OverrideRunSpeed = false;
    public float RunSpeed = 4.0f;

    public bool OverrideGravity = false;
    public float Gravity = -9.81f;

    public bool OverrideMovementMode = false;
    public BasisLocalCharacterDriver.Mode MovementMode = BasisLocalCharacterDriver.Mode.Fly;

    /// <summary>Leave empty to key the override off this object's instance id.</summary>
    public string OverrideKey = string.Empty;

    /// <summary>Release the override when the player leaves the trigger.</summary>
    public bool ReleaseOnExit = true;

    private string Key => string.IsNullOrWhiteSpace(OverrideKey)
        ? $"{nameof(BasisLocomotionZone)}:{GetEntityId()}"
        : OverrideKey;

    private void OnTriggerEnter(Collider other)
    {
        if (other == null || other.gameObject == null)
        {
            return;
        }

        if (other.gameObject.TryGetComponent(out BasisLocalPlayer _) == false)
        {
            return;
        }

        Apply();
    }

    private void OnTriggerExit(Collider other)
    {
        if (ReleaseOnExit == false || other == null || other.gameObject == null)
        {
            return;
        }

        if (other.gameObject.TryGetComponent(out BasisLocalPlayer _) == false)
        {
            return;
        }

        Release();
    }

    private void OnDisable() => Release();

    public void Apply()
    {
        BasisLocomotionValues values = default;

        if (OverrideJumpHeight)
        {
            values.Fields |= BasisLocomotionField.JumpHeight;
            values.JumpHeight = JumpHeight;
        }
        if (OverrideWalkSpeed)
        {
            values.Fields |= BasisLocomotionField.WalkSpeed;
            values.WalkSpeed = WalkSpeed;
        }
        if (OverrideRunSpeed)
        {
            values.Fields |= BasisLocomotionField.RunSpeed;
            values.RunSpeed = RunSpeed;
        }
        if (OverrideGravity)
        {
            values.Fields |= BasisLocomotionField.Gravity;
            values.Gravity = Gravity;
        }
        if (OverrideMovementMode)
        {
            values.Fields |= BasisLocomotionField.Mode;
            values.Mode = MovementMode;
        }

        BasisLocomotionOverrides.Set(Key, values);
    }

    public void Release()
    {
        BasisLocomotionOverrides.Remove(Key);
    }
}
