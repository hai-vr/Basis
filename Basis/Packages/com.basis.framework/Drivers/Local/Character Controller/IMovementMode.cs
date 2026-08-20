namespace Basis.Scripts.BasisCharacterController
{

    public interface IMovementMode
    {
        string Name { get; }
        CollisionHandling Collision { get; }

        // Called when mode becomes active
        void Enter(BasisLocalCharacterDriver ctx);

        // Called when mode deactivates
        void Exit(BasisLocalCharacterDriver ctx);

        // Per-frame simulation of displacement and vertical speed
        // Should call CharacterController.Move when Collision==Solid, or set transform directly when Ghost
        void Tick(BasisLocalCharacterDriver ctx, float deltaTime);
    }
}
