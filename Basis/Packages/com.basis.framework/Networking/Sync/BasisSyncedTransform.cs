using UnityEngine;

namespace Basis.Scripts.Networking.Sync
{
    /// <summary>
    /// Drop-in transform sync built on <see cref="BasisSyncedObject"/>. The owner streams the enabled
    /// axes; every other client's copy is interpolated and composed each frame. Per-axis toggles let
    /// you sync only what moves (e.g. a door = rotation Y only), half precision halves the float cost,
    /// and extrapolation / teleport-threshold (inherited) tune late-packet behaviour.
    ///
    /// For custom values use the code API on <see cref="BasisSyncedObject"/> directly
    /// (RegisterFloat / RegisterColor / RegisterUShort / ... then LocalSet / RemoteGet).
    /// </summary>
    public class BasisSyncedTransform : BasisSyncedObject
    {
        public Transform Target;

        public bool SyncPosition = true;
        public bool PositionX = true;
        public bool PositionY = true;
        public bool PositionZ = true;

        public bool SyncRotation = true;
        public bool RotationX = true;
        public bool RotationY = true;
        public bool RotationZ = true;

        public bool SyncScale = false;
        public bool ScaleX = true;
        public bool ScaleY = true;
        public bool ScaleZ = true;

        public bool WorldSpace = false;
        public bool HalfPrecision = false;

        private BasisSyncHandle _posX = BasisSyncHandle.Invalid;
        private BasisSyncHandle _posY = BasisSyncHandle.Invalid;
        private BasisSyncHandle _posZ = BasisSyncHandle.Invalid;
        private BasisSyncHandle _rotQuat = BasisSyncHandle.Invalid;
        private BasisSyncHandle _eulerX = BasisSyncHandle.Invalid;
        private BasisSyncHandle _eulerY = BasisSyncHandle.Invalid;
        private BasisSyncHandle _eulerZ = BasisSyncHandle.Invalid;
        private BasisSyncHandle _scaleX = BasisSyncHandle.Invalid;
        private BasisSyncHandle _scaleY = BasisSyncHandle.Invalid;
        private BasisSyncHandle _scaleZ = BasisSyncHandle.Invalid;
        private bool _rotEuler;
        private Vector3 _heldEuler;

        private void Reset()
        {
            Target = transform;
        }

        protected virtual void Awake()
        {
            if (Target == null) Target = transform;
            WantsMainThreadApply = true;

            int posAxes = 0;
            if (SyncPosition)
            {
                if (PositionX) { _posX = RegisterFloat(true, HalfPrecision); posAxes++; }
                if (PositionY) { _posY = RegisterFloat(true, HalfPrecision); posAxes++; }
                if (PositionZ) { _posZ = RegisterFloat(true, HalfPrecision); posAxes++; }
            }

            if (SyncRotation)
            {
                if (RotationX && RotationY && RotationZ)
                {
                    _rotQuat = RegisterRotation();
                }
                else if (RotationX || RotationY || RotationZ)
                {
                    _rotEuler = true;
                    _heldEuler = WorldSpace ? Target.eulerAngles : Target.localEulerAngles;
                    if (RotationX) _eulerX = RegisterAngle(true, HalfPrecision);
                    if (RotationY) _eulerY = RegisterAngle(true, HalfPrecision);
                    if (RotationZ) _eulerZ = RegisterAngle(true, HalfPrecision);
                }
            }

            if (SyncScale)
            {
                if (ScaleX) _scaleX = RegisterFloat(true, HalfPrecision);
                if (ScaleY) _scaleY = RegisterFloat(true, HalfPrecision);
                if (ScaleZ) _scaleZ = RegisterFloat(true, HalfPrecision);
            }

            // Position axes are registered first, so they occupy the start of the continuous range —
            // that's the window the teleport threshold watches.
            TeleportWatchStart = 0;
            TeleportWatchCount = posAxes;
        }

        protected override void OnBeforeTransmit()
        {
            if (Target == null) return;

            Vector3 p;
            Quaternion r;
            if (WorldSpace) Target.GetPositionAndRotation(out p, out r);
            else Target.GetLocalPositionAndRotation(out p, out r);

            if (_posX.IsValid) LocalSet(_posX, p.x);
            if (_posY.IsValid) LocalSet(_posY, p.y);
            if (_posZ.IsValid) LocalSet(_posZ, p.z);

            if (_rotQuat.IsValid)
            {
                LocalSet(_rotQuat, r);
            }
            else if (_rotEuler)
            {
                Vector3 e = WorldSpace ? Target.eulerAngles : Target.localEulerAngles;
                if (_eulerX.IsValid) LocalSet(_eulerX, e.x);
                if (_eulerY.IsValid) LocalSet(_eulerY, e.y);
                if (_eulerZ.IsValid) LocalSet(_eulerZ, e.z);
            }

            if (_scaleX.IsValid || _scaleY.IsValid || _scaleZ.IsValid)
            {
                Vector3 s = Target.localScale;
                if (_scaleX.IsValid) LocalSet(_scaleX, s.x);
                if (_scaleY.IsValid) LocalSet(_scaleY, s.y);
                if (_scaleZ.IsValid) LocalSet(_scaleZ, s.z);
            }
        }

        protected override void ApplyInterpolated()
        {
            if (Target == null) return;

            Vector3 p;
            Quaternion r;
            if (WorldSpace) Target.GetPositionAndRotation(out p, out r);
            else Target.GetLocalPositionAndRotation(out p, out r);
            Vector3 s = Target.localScale;

            if (_posX.IsValid) p.x = GetFloat(_posX);
            if (_posY.IsValid) p.y = GetFloat(_posY);
            if (_posZ.IsValid) p.z = GetFloat(_posZ);

            if (_rotQuat.IsValid)
            {
                r = GetQuaternion(_rotQuat);
            }
            else if (_rotEuler)
            {
                Vector3 e = _heldEuler;
                if (_eulerX.IsValid) e.x = GetAngle(_eulerX);
                if (_eulerY.IsValid) e.y = GetAngle(_eulerY);
                if (_eulerZ.IsValid) e.z = GetAngle(_eulerZ);
                r = Quaternion.Euler(e);
            }

            if (_scaleX.IsValid) s.x = GetFloat(_scaleX);
            if (_scaleY.IsValid) s.y = GetFloat(_scaleY);
            if (_scaleZ.IsValid) s.z = GetFloat(_scaleZ);

            if (WorldSpace) Target.SetPositionAndRotation(p, r);
            else Target.SetLocalPositionAndRotation(p, r);
            Target.localScale = s;
        }

        protected override bool TryGetSyncWorldPosition(out Vector3 position)
        {
            if (Target != null) { position = Target.position; return true; }
            position = default;
            return false;
        }
    }
}
