using Basis;
using Basis.Scripts.BasisSdk.Interactions;
using Basis.Scripts.Device_Management.Devices;
using Basis.Scripts.Networking;
using Basis.Scripts.Networking.NetworkedAvatar;
using Basis.Scripts.Networking.Sync;
using UnityEngine;

public class BasisPickupSyncNetworking : BasisSyncedTransform, IBasisStaticLockable
{
    public BasisPickupInteractable BasisPickupInteractable;
    public bool CanNetworkSteal = true;
    /// <summary>
    /// Server-authoritative "static" flag. When true the prop can't be hovered or grabbed by anyone
    /// and is frozen (kinematic) in place. Set via the library Static toggle; applied through SetStatic.
    /// </summary>
    public bool IsStatic = false;
    public BasisInput pendingStealRequest = null;

    /// <summary>
    /// Opt-in: also stream the Rigidbody's linear/angular velocity, and let a free-flying remote copy simulate
    /// from it (prediction) with the synced pose pulling it back (correction), instead of being kinematic and
    /// pose-driven. Default off keeps the existing pose-only behaviour.
    /// </summary>
    public bool RemoteDeadReckon = false;

    private BasisSyncHandle _velX = BasisSyncHandle.Invalid;
    private BasisSyncHandle _velY = BasisSyncHandle.Invalid;
    private BasisSyncHandle _velZ = BasisSyncHandle.Invalid;
    private BasisSyncHandle _angX = BasisSyncHandle.Invalid;
    private BasisSyncHandle _angY = BasisSyncHandle.Invalid;
    private BasisSyncHandle _angZ = BasisSyncHandle.Invalid;

    private void Reset()
    {
        Target = transform;
        SyncScale = true;
    }

    protected override void Awake()
    {
        base.Awake();
        if (BasisPickupInteractable == null)
        {
            BasisPickupInteractable = this.transform.GetComponentInChildren<BasisPickupInteractable>();
        }
        if (BasisPickupInteractable != null)
        {
            BasisPickupInteractable.CanHoverInjected.Add(CanHover);
            BasisPickupInteractable.CanInteractInjected.Add(CanInteract);
            BasisPickupInteractable.OnInteractStartEvent.AddListener(OnInteractStartEvent);
            if (BasisPickupInteractable.RigidRef != null)
            {
                BasisPickupInteractable.RigidRef.isKinematic = false;
            }
        }

        if (RemoteDeadReckon && BasisPickupInteractable != null && BasisPickupInteractable.RigidRef != null)
        {
            _velX = RegisterFloat(BasisQuantSpec.Half);
            _velY = RegisterFloat(BasisQuantSpec.Half);
            _velZ = RegisterFloat(BasisQuantSpec.Half);
            _angX = RegisterFloat(BasisQuantSpec.Half);
            _angY = RegisterFloat(BasisQuantSpec.Half);
            _angZ = RegisterFloat(BasisQuantSpec.Half);
        }
    }

    public void OnDisable()
    {
        if (BasisPickupInteractable != null)
        {
            BasisPickupInteractable.CanHoverInjected.Remove(CanHover);
            BasisPickupInteractable.CanInteractInjected.Remove(CanInteract);
            BasisPickupInteractable.OnInteractStartEvent.RemoveListener(OnInteractStartEvent);
        }
    }

    public override void OnNetworkReady()
    {
        base.OnNetworkReady();
        ControlState();
    }

    protected override void OnBeforeTransmit()
    {
        base.OnBeforeTransmit();
        if (!_velX.IsValid || BasisPickupInteractable == null) return;
        Rigidbody rb = BasisPickupInteractable.RigidRef;
        if (rb == null) return;
        Vector3 v = rb.linearVelocity;
        Vector3 w = rb.angularVelocity;
        LocalSet(_velX, v.x); LocalSet(_velY, v.y); LocalSet(_velZ, v.z);
        LocalSet(_angX, w.x); LocalSet(_angY, w.y); LocalSet(_angZ, w.z);
    }

    protected override void ApplyInterpolated()
    {
        if (!RemoteDeadReckon || !_velX.IsValid || BasisPickupInteractable == null)
        {
            base.ApplyInterpolated();
            return;
        }
        Rigidbody rb = BasisPickupInteractable.RigidRef;
        if (rb == null || rb.isKinematic)
        {
            base.ApplyInterpolated();
            return;
        }

        rb.linearVelocity = new Vector3(GetFloat(_velX), GetFloat(_velY), GetFloat(_velZ));
        rb.angularVelocity = new Vector3(GetFloat(_angX), GetFloat(_angY), GetFloat(_angZ));

        if (ComposeSyncedPose(out Vector3 lp, out Quaternion lr, out Vector3 ls))
        {
            Vector3 worldPos;
            Quaternion worldRot;
            if (WorldSpace)
            {
                worldPos = lp;
                worldRot = lr;
            }
            else
            {
                Transform parent = Target != null ? Target.parent : null;
                worldPos = parent != null ? parent.TransformPoint(lp) : lp;
                worldRot = parent != null ? parent.rotation * lr : lr;
            }

            const float correction = 0.15f;
            rb.position = Vector3.Lerp(rb.position, worldPos, correction);
            rb.rotation = Quaternion.Slerp(rb.rotation, worldRot, correction);
            if (Target != null) Target.localScale = ls;
        }
    }

    private bool CanHover(BasisInput input)
    {
        if (IsStatic)
        {
            return false;
        }
        if (!BasisNetworkConnection.LocalPlayerIsConnected)
        {
            return true;
        }
        return IsOwnedLocallyOnClient || CanNetworkSteal;
    }

    private bool CanInteract(BasisInput input)
    {
        if (IsStatic)
        {
            return false;
        }
        if (IsOwnedLocallyOnClient)
        {
            return true;
        }
        return CanNetworkSteal && (pendingStealRequest == null || pendingStealRequest == input);
    }

    private void OnInteractStartEvent(BasisInput input)
    {
        if (!IsOwnedLocallyOnClient)
        {
            pendingStealRequest = input;
        }
        CanInteractAsync();
    }

    private async void CanInteractAsync()
    {
        var result = await TakeOwnershipAsync(5000);
        if (result.Success == false)
        {
            pendingStealRequest = null;
        }
    }

    public void SetIsKinematicOnPickup(bool state)
    {
        if (BasisPickupInteractable != null && BasisPickupInteractable.RigidRef != null)
        {
            BasisPickupInteractable.RigidRef.isKinematic = state;
        }
    }

    /// <summary>
    /// Apply or release the server-authoritative static / locked state (<see cref="IBasisStaticLockable"/>).
    /// </summary>
    public void SetStatic(bool isStatic)
    {
        if (IsStatic == isStatic)
        {
            return;
        }
        IsStatic = isStatic;
        ControlState();
    }

    public override void OnOwnershipTransfer(BasisNetworkPlayer newOwner)
    {
        base.OnOwnershipTransfer(newOwner);
        ControlState();
    }

    public override void OnServerOwnershipDestroyed()
    {
        base.OnServerOwnershipDestroyed();
        ControlState();
    }

    public void ControlState()
    {
        if (Target == null) Target = transform;

        if (IsStatic)
        {
            if (BasisPickupInteractable != null)
            {
                BasisPickupInteractable.Drop();
            }
            SetIsKinematicOnPickup(true);
            return;
        }

        if (IsOwnedLocallyOnClient)
        {
            if (pendingStealRequest != null)
            {
                SetIsKinematicOnPickup(false);
                BasisPlayerInteract.Instance.ForceSetInteracting(BasisPickupInteractable, pendingStealRequest);
                pendingStealRequest = null;
            }
            else if (BasisPickupInteractable != null
                && BasisPickupInteractable.KinematicWhileInteracting
                && BasisPickupInteractable.RequiresUpdateLoop)
            {
                // Held with KinematicWhileInteracting - preserve kinematic state
            }
            else
            {
                SetIsKinematicOnPickup(false);
            }
        }
        else
        {
            if (BasisPickupInteractable != null)
            {
                BasisPickupInteractable.Drop();
            }
            SetIsKinematicOnPickup(!RemoteDeadReckon);
        }
    }

    protected override void MigrateSerialized(int fromVersion)
    {
        base.MigrateSerialized(fromVersion);
        if (fromVersion < 1)
        {
            // Legacy pickups synced full position + rotation + scale.
            SyncPosition = true;
            PositionX = true; PositionY = true; PositionZ = true;
            SyncRotation = true;
            RotationX = true; RotationY = true; RotationZ = true;
            SyncScale = true;
            ScaleX = true; ScaleY = true; ScaleZ = true;
        }
    }
}
