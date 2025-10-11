using Basis.Scripts.Device_Management.Devices.Desktop;
using Basis.Scripts.Drivers;
using Unity.Mathematics;
using UnityEngine;

namespace Basis.Scripts.BasisCharacterController
{
    public class BasisNoClipMovementMode : IMovementMode
    {
        public string Name => "NoClip";
        public CollisionHandling Collision => CollisionHandling.Ghost;

        public void Enter(BasisLocalCharacterDriver ctx)
        {
            if (ctx.characterController != null)
            {
                // Fully ghost: no collisions, and disabling avoids stray hits & depenetration.
                ctx.characterController.detectCollisions = false;
                ctx.characterController.enabled = false;
            }
            ctx.currentVerticalSpeed = 0f;
            ctx.groundedPlayer = false;
            ctx.IsFalling = false;
        }

        public void Exit(BasisLocalCharacterDriver ctx)
        {
            if (ctx.characterController != null)
            {
                // Re-enable for other modes
                ctx.characterController.detectCollisions = true;
                ctx.characterController.enabled = true;
            }
        }

        public void Tick(BasisLocalCharacterDriver ctx, float dt)
        {
            // Flatten head yaw so forward is camera yaw on the horizontal plane
            var yaw = BasisLocalBoneDriver.HeadControl.OutgoingWorldData.rotation.eulerAngles;
            yaw.x = 0f; yaw.z = 0f;
            Quaternion facing = Quaternion.Euler(yaw);

            // Compute base speed (same model you use elsewhere)
            ctx.CurrentSpeed =
                math.lerp(ctx.MinimumMovementSpeed, ctx.MaximumMovementSpeed, ctx.MovementSpeedScale)
                + ctx.MinimumMovementSpeed * ctx.MovementSpeedBoost;

            // Planar input
            Vector3 planar = new Vector3(ctx.MovementVector.x, 0f, ctx.MovementVector.y).normalized;
            Vector3 move = facing * planar * ctx.CurrentSpeed * dt;

            // --- Vertical input (held every frame) ---
            float ascendHeld = 0f;
            float descendHeld = 0f;

            if (BasisLocalInputActions.Instance != null)
            {
                // Replace with your exact property names if different
                ascendHeld = BasisLocalInputActions.Instance.IsJumpHeld ? 1f : 0f;
                descendHeld = BasisLocalInputActions.Instance.IsCrouchHeld ? 1f : 0f;
            }

            // Optionally treat a tap as a one-frame nudge up
            if (ctx.HasJumpAction) ascendHeld = 1f;

            float verticalInput = ascendHeld - descendHeld; // -1..+1
            move.y = verticalInput * ctx.CurrentSpeed * dt;

            // Consume tap
            ctx.HasJumpAction = false;

            // Movement lock
            if (ctx.MovementLock) move = Vector3.zero;

            // Ghost move: bypass CharacterController entirely
            ctx.BasisLocalPlayerTransform.position += move;

            // Sync state for the driver/anim/etc.
            ctx.BasisLocalPlayerTransform.GetPositionAndRotation(out ctx.CurrentPosition, out ctx.CurrentRotation);
            ctx.groundedPlayer = false;
            ctx.IsFalling = false;
            ctx.Flags = CollisionFlags.None;
        }
    }
}
