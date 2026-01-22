using System;
using System.Collections.Generic;
using Basis;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Vehicles.Main;
using Basis.Scripts.Vehicles.Parts;
using UnityEngine;
namespace Basis.Network.Vehicles
{
    public class BasisNetworkedVehicle : BasisNetworkBehaviour
    {
        public BasisVehicleBody BasisVehicleBody;
        public BasisVehiclePilotSeat Seat;
        public BasisSeatSync SeatSync;
        public Rigidbody Rigidbody;
        public WheelCollider[] Colliders;
        public BasisVehicleWheel[] Wheels;
        public BasisVehicleEngineAudio EngineAudio;
        public BasisVehicleSteeringWheel SteeringWheel;

        [Header("Wheel Visual Transforms (REQUIRED for remote visuals)")]
        [Tooltip("One per wheel. This transform will be rotated by spin angle (degrees) around SpinAxisLocal.")]
        public Transform[] SpinVisuals;

        [Tooltip("Steer visuals (usually 2: front-left, front-right). These are rotated by steer angle (degrees) around SteerAxisLocal.")]
        public Transform[] SteerVisuals;

        [Tooltip("Local axis for wheel spin (in the SpinVisual's local space). Commonly Vector3.right or Vector3.forward depending on your rig.")]
        public Vector3 SpinAxisLocal = Vector3.right;

        [Tooltip("Local axis for steering (in the SteerVisual's local space). Commonly Vector3.up.")]
        public Vector3 SteerAxisLocal = Vector3.up;

        [Header("Owner Send")]
        [Tooltip("How often the owner sends updates (seconds).")]
        public float SendInterval = 0.05f; // 50ms

        [Header("Remote Playback (Jitter Buffer)")]
        [Tooltip("How far in the past we render remote vehicles. Usually 2-3x SendInterval.")]
        public float InterpDelay = 0.10f; // 100ms default (2 ticks)

        [Tooltip("Max snapshots kept in buffer.")]
        public int MaxBufferSize = 32;

        [Tooltip("Allow small extrapolation when we run out of snapshots (seconds).")]
        public float MaxExtrapolation = 0.10f;

        [Tooltip("Clamp crazy velocity spikes when extrapolating (m/s).")]
        public float MaxExtrapolationSpeed = 200f;

        [Header("Wheel Quantization")]
        [Tooltip("Bits used per wheel spin angle (absolute 0..360). 12 bits = 0.088° steps.")]
        [Range(8, 12)] public int SpinBits = 12;

        [Tooltip("Bits used per steer angle (absolute -SteerRange..+SteerRange). 10 bits = 0.117° steps over 120° range.")]
        [Range(7, 11)] public int SteerBits = 10;

        [Tooltip("Steer angle range in degrees. Angles outside clamp.")]
        public float SteerRangeDeg = 60f;

        [Header("Engine / SteeringWheel Sync")]
        [Tooltip("Bits used for engine revs value (0..1).")]
        [Range(6, 10)] public int EngineBits = 8;

        [Tooltip("Bits used for steering wheel ratio (-1..1).")]
        [Range(7, 11)] public int SteerRatioBits = 9;

        private float _sendTimer;
        private Transform _tr;

        public BasisPlayer Player;

        private struct Snapshot
        {
            public double t;      // local receive timestamp (Time.timeAsDouble)
            public Vector3 pos;
            public Quaternion rot;
            public Vector3 scale;

            // Wheel angles (ABSOLUTE):
            public float[] spinDeg;   // length = wheelCount
            public float[] steerDeg;  // length = steerCount

            // Extras:
            public float engineRevs01; // 0..1
            public float steerRatio;   // -1..1
        }

        // Snapshot buffer (ordered oldest -> newest)
        private readonly List<Snapshot> _snapshots = new List<Snapshot>(64);

        // For extrapolation
        private Vector3 _vel;          // m/s
        private Vector3 _angVel;       // axis * rad/s (approx)
        private bool _haveVel;

        // OWNER: absolute spin angle tracking
        private float[] _ownerSpinAbsDeg;
        private int _wheelCount;
        private int _steerCount;

        // Last received extras (for single-snapshot snap/extrap frames)
        private float _remoteEngineRevs01;
        private float _remoteSteerRatio;

        public override void Start()
        {
            Player = null;
            base.Start();

            _tr = transform;

            if (Seat != null)
            {
                Seat.OnPlayerEnterSeat += OnPlayerEnterSeat;
                Seat.OnPlayerExitSeat += OnPlayerExitSeat;
            }


            // Cache counts
            _wheelCount = Wheels != null ? Wheels.Length : 0;
            _steerCount = SteerVisuals != null ? SteerVisuals.Length : 0;

            // Allocate owner spin accumulator (absolute)
            _ownerSpinAbsDeg = new float[_wheelCount];

            // If we start as remote, make sure physics is off (but keep audio/steer visuals driven by network)
            if (Player == null || !Player.IsLocal)
            {
                ToggleItems(false);
                ApplyRemoteExtrasToParts(0f, 0f);
            }

            BasisLocalPlayer.AfterSimulateOnRender.AddAction(203, SimulateLocal);
            BasisLocalPlayer.JustBeforeNetworkApply.AddAction(10, SimulateRemote);
        }

        public override void OnDestroy()
        {
            BasisLocalPlayer.AfterSimulateOnRender.RemoveAction(203, SimulateLocal);
            BasisLocalPlayer.JustBeforeNetworkApply.RemoveAction(10, SimulateRemote);
            if (Seat != null)
            {
                Seat.OnPlayerEnterSeat -= OnPlayerEnterSeat;
            }

            base.OnDestroy();

        }
        private void OnPlayerExitSeat(BasisPlayer player)
        {
            Player = null;
            ToggleItems(false);//when player exits the vehicle we set all the items to false


        }

        private void OnPlayerEnterSeat(BasisPlayer player)
        {
            Player = player;

            bool isLocal = player != null && player.IsLocal;
            ToggleItems(isLocal);

            if (isLocal)
            {
                _snapshots.Clear();
                _haveVel = false;

                for (int i = 0; i < _ownerSpinAbsDeg.Length; i++)
                    _ownerSpinAbsDeg[i] = 0f;

                // Local: let components compute from real inputs/physics
                if (EngineAudio != null) EngineAudio.UseNetworkRevs = false;
                if (SteeringWheel != null) SteeringWheel.UseNetworkSteerRatio = false;
            }
            else
            {
                // Remote: force network drive
                ApplyRemoteExtrasToParts(_remoteEngineRevs01, _remoteSteerRatio);
            }
        }

        public override void OnNetworkReady()
        {
            if (Player == null || !Player.IsLocal)
            {
                ToggleItems(false);
                ApplyRemoteExtrasToParts(_remoteEngineRevs01, _remoteSteerRatio);
            }
        }

        public override void OnNetworkMessage(ushort PlayerID, byte[] buffer, Basis.Network.Core.DeliveryMethod DeliveryMethod)
        {
            if (buffer == null)
                return;

            // If we're local driver, ignore remote state
            if (Player != null && Player.IsLocal)
                return;

            int expectedMin = BasisVehicleNetCodec.MinPacketSize
                            + BasisVehicleWheelNetCodec.ExtraBytes(_wheelCount, _steerCount, SpinBits, SteerBits, EngineBits, SteerRatioBits);

            if (buffer.Length < expectedMin)
                return;

            BasisVehicleWheelNetCodec.ReadPacketWithWheels(
                buffer,
                _wheelCount, _steerCount,
                SpinBits, SteerBits,
                EngineBits, SteerRatioBits,
                -SteerRangeDeg, SteerRangeDeg,
                out Vector3 pos, out Quaternion rot, out Vector3 scale,
                out float[] spinDeg, out float[] steerDeg,
                out float engineRevs01, out float steerRatio
            );

            double now = Time.timeAsDouble;

            _remoteEngineRevs01 = engineRevs01;
            _remoteSteerRatio = steerRatio;

            var s = new Snapshot
            {
                t = now,
                pos = pos,
                rot = rot,
                scale = scale,
                spinDeg = spinDeg,
                steerDeg = steerDeg,
                engineRevs01 = engineRevs01,
                steerRatio = steerRatio
            };

            _snapshots.Add(s);

            if (_snapshots.Count > MaxBufferSize)
                _snapshots.RemoveRange(0, _snapshots.Count - MaxBufferSize);

            UpdateVelocityEstimates();
        }

        private void UpdateVelocityEstimates()
        {
            int n = _snapshots.Count;
            if (n < 2)
            {
                _haveVel = false;
                return;
            }

            Snapshot a = _snapshots[n - 2];
            Snapshot b = _snapshots[n - 1];

            double dt = b.t - a.t;
            if (dt <= 1e-6)
            {
                _haveVel = false;
                return;
            }

            Vector3 v = (b.pos - a.pos) / (float)dt;

            float speed = v.magnitude;
            if (speed > MaxExtrapolationSpeed)
                v = v.normalized * MaxExtrapolationSpeed;

            _vel = v;

            Quaternion dq = b.rot * Quaternion.Inverse(a.rot);
            dq.ToAngleAxis(out float angleDeg, out Vector3 axis);
            if (float.IsNaN(axis.x) || axis == Vector3.zero) axis = Vector3.up;

            if (angleDeg > 180f) angleDeg -= 360f;

            float angleRad = angleDeg * Mathf.Deg2Rad;
            _angVel = axis.normalized * (angleRad / (float)dt);

            _haveVel = true;
        }

        public void SimulateLocal()
        {
            // OWNER SEND
            if (Player != null && Player.IsLocal)
            {
                UpdateOwnerAbsoluteWheelSpin();

                _sendTimer += Time.deltaTime;
                if (_sendTimer >= SendInterval)
                {
                    _sendTimer -= SendInterval;

                    _tr.GetPositionAndRotation(out var pos, out var rot);
                    var scale = _tr.localScale;

                    float[] spinDeg = GetOwnerWheelSpinAbs();
                    float[] steerDeg = GetOwnerSteerAbs();

                    float engineRevs01 = ComputeEngineRevs01ForNetwork();
                    float steerRatio = ComputeSteerRatioForNetwork();

                    byte[] data = BasisVehicleWheelNetCodec.WritePacketWithWheels(
                        pos, rot, scale,
                        spinDeg, steerDeg,
                        engineRevs01, steerRatio,
                        SpinBits, SteerBits,
                        EngineBits, SteerRatioBits,
                        -SteerRangeDeg, SteerRangeDeg
                    );

                    SendCustomNetworkEvent(data, Basis.Network.Core.DeliveryMethod.Sequenced);
                }

                return;
            }
        }
        public void SimulateRemote()
        {
            // REMOTE PLAYBACK
            if (_snapshots.Count == 0)
                return;

            double now = Time.timeAsDouble;
            double renderTime = now - InterpDelay;

            while (_snapshots.Count >= 2 && _snapshots[1].t <= renderTime)
                _snapshots.RemoveAt(0);

            if (_snapshots.Count >= 2)
            {
                Snapshot s0 = _snapshots[0];
                Snapshot s1 = _snapshots[1];

                double dt = s1.t - s0.t;
                float alpha = dt > 1e-6 ? (float)((renderTime - s0.t) / dt) : 0f;
                alpha = Mathf.Clamp01(alpha);

                Vector3 pos = Vector3.LerpUnclamped(s0.pos, s1.pos, alpha);
                Quaternion rot = Quaternion.SlerpUnclamped(s0.rot, s1.rot, alpha);
                Vector3 scale = Vector3.LerpUnclamped(s0.scale, s1.scale, alpha);

                _tr.SetPositionAndRotation(pos, rot);
                _tr.localScale = scale;

                ApplyRemoteWheelsInterpolated(s0, s1, alpha);

                float engine = Mathf.Lerp(s0.engineRevs01, s1.engineRevs01, alpha);
                float steerRatio = Mathf.Lerp(s0.steerRatio, s1.steerRatio, alpha);
                ApplyRemoteExtrasToParts(engine, steerRatio);
            }
            else
            {
                Snapshot s = _snapshots[0];

                double ahead = renderTime - s.t;
                if (ahead > 0 && _haveVel && ahead <= MaxExtrapolation)
                {
                    float dt = (float)ahead;

                    Vector3 pos = s.pos + _vel * dt;

                    Vector3 axis = _angVel;
                    float ang = axis.magnitude * dt;
                    Quaternion delta = (axis.sqrMagnitude > 1e-8f)
                        ? Quaternion.AngleAxis(ang * Mathf.Rad2Deg, axis.normalized)
                        : Quaternion.identity;

                    Quaternion rot = delta * s.rot;

                    _tr.SetPositionAndRotation(pos, rot);
                    _tr.localScale = s.scale;
                }
                else
                {
                    _tr.SetPositionAndRotation(s.pos, s.rot);
                    _tr.localScale = s.scale;
                }

                ApplyRemoteWheelsSnap(s);
                ApplyRemoteExtrasToParts(s.engineRevs01, s.steerRatio);
            }
        }

        // ---------------- OWNER: ABSOLUTE ANGLES ----------------

        private void UpdateOwnerAbsoluteWheelSpin()
        {
            if (Colliders == null || _ownerSpinAbsDeg == null) return;

            int n = Mathf.Min(Colliders.Length, _ownerSpinAbsDeg.Length);
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            for (int i = 0; i < n; i++)
            {
                var wc = Colliders[i];
                if (wc == null) continue;

                float degPerSec = wc.rpm * 6f;
                _ownerSpinAbsDeg[i] = Wrap360(_ownerSpinAbsDeg[i] + degPerSec * dt);
            }
        }

        private float[] GetOwnerWheelSpinAbs()
        {
            float[] a = new float[_wheelCount];
            for (int i = 0; i < _wheelCount; i++)
                a[i] = (i < _ownerSpinAbsDeg.Length) ? _ownerSpinAbsDeg[i] : 0f;
            return a;
        }

        private float[] GetOwnerSteerAbs()
        {
            float[] s = new float[_steerCount];

            if (_steerCount == 0)
                return s;

            for (int i = 0; i < _steerCount; i++)
            {
                float steer = 0f;
                if (Colliders != null && i < Colliders.Length && Colliders[i] != null)
                    steer = Colliders[i].steerAngle;

                s[i] = Mathf.Clamp(steer, -SteerRangeDeg, SteerRangeDeg);
            }

            return s;
        }

        private float ComputeEngineRevs01ForNetwork()
        {
            // Match your BasisVehicleEngineAudio vibe: speed ratio + throttle influence + idle floor.
            float speed = 0f;
            if (Rigidbody != null)
                speed = Rigidbody.linearVelocity.magnitude;

            float throttle = 0f;
            if (BasisVehicleBody != null)
            {
                throttle = Mathf.Clamp(BasisVehicleBody.LinearActivation.z, -1f, 1f);
                throttle = Mathf.Abs(throttle);
            }

            const float idle = 0.12f;
            const float throttleInfluence = 1.0f;

            float maxSpeed = 30f;
            if (BasisVehicleBody != null && BasisVehicleBody.MaxSpeed > 0.001f)
                maxSpeed = BasisVehicleBody.MaxSpeed;

            float speedRatio = (maxSpeed > 0.001f) ? Mathf.Clamp01(speed / maxSpeed) : 0f;

            float revs =
                Mathf.Max(idle,
                          Mathf.Clamp01(speedRatio + throttle * throttleInfluence * (1f - speedRatio)));

            return revs;
        }

        private float ComputeSteerRatioForNetwork()
        {
            // Steering wheel visual ratio (-1..1). Use average steerAngle / SteerRangeDeg.
            if (Colliders == null || Colliders.Length == 0)
                return 0f;

            float denom = Mathf.Max(1f, SteerRangeDeg);

            float sum = 0f;
            int count = 0;

            for (int i = 0; i < Colliders.Length; i++)
            {
                var wc = Colliders[i];
                if (wc == null) continue;

                float r = Mathf.Clamp(wc.steerAngle / denom, -1f, 1f);
                sum += r;
                count++;
            }

            return (count == 0) ? 0f : (sum / count);
        }

        private static float Wrap360(float deg)
        {
            deg %= 360f;
            if (deg < 0f) deg += 360f;
            return deg;
        }

        // ---------------- REMOTE: APPLY VISUALS ----------------

        private void ApplyRemoteWheelsInterpolated(in Snapshot a, in Snapshot b, float alpha)
        {
            if (SpinVisuals != null && a.spinDeg != null && b.spinDeg != null)
            {
                int n = Mathf.Min(SpinVisuals.Length, Mathf.Min(a.spinDeg.Length, b.spinDeg.Length));
                for (int i = 0; i < n; i++)
                {
                    var t = SpinVisuals[i];
                    if (t == null) continue;

                    float deg = Mathf.LerpAngle(a.spinDeg[i], b.spinDeg[i], alpha);
                    SetAxisLocalRotation(t, SpinAxisLocal, deg);
                }
            }

            if (SteerVisuals != null && a.steerDeg != null && b.steerDeg != null)
            {
                int n = Mathf.Min(SteerVisuals.Length, Mathf.Min(a.steerDeg.Length, b.steerDeg.Length));
                for (int i = 0; i < n; i++)
                {
                    var t = SteerVisuals[i];
                    if (t == null) continue;

                    float deg = Mathf.LerpAngle(a.steerDeg[i], b.steerDeg[i], alpha);
                    SetAxisLocalRotation(t, SteerAxisLocal, deg);
                }
            }
        }

        private void ApplyRemoteWheelsSnap(in Snapshot s)
        {
            if (SpinVisuals != null && s.spinDeg != null)
            {
                int n = Mathf.Min(SpinVisuals.Length, s.spinDeg.Length);
                for (int i = 0; i < n; i++)
                {
                    var t = SpinVisuals[i];
                    if (t == null) continue;
                    SetAxisLocalRotation(t, SpinAxisLocal, s.spinDeg[i]);
                }
            }

            if (SteerVisuals != null && s.steerDeg != null)
            {
                int n = Mathf.Min(SteerVisuals.Length, s.steerDeg.Length);
                for (int i = 0; i < n; i++)
                {
                    var t = SteerVisuals[i];
                    if (t == null) continue;
                    SetAxisLocalRotation(t, SteerAxisLocal, s.steerDeg[i]);
                }
            }
        }

        private void ApplyRemoteExtrasToParts(float engineRevs01, float steerRatio)
        {
            // Remotes: physics is off, so these must be network-driven.
            if (EngineAudio != null)
            {
                EngineAudio.UseNetworkRevs = true;
                EngineAudio.NetworkRevs01 = Mathf.Clamp01(engineRevs01);
            }

            if (SteeringWheel != null)
            {
                SteeringWheel.UseNetworkSteerRatio = true;
                SteeringWheel.NetworkSteerRatio = Mathf.Clamp(steerRatio, -1f, 1f);
            }
        }

        private static void SetAxisLocalRotation(Transform t, Vector3 axisLocal, float degrees)
        {
            axisLocal = axisLocal.sqrMagnitude > 1e-8f ? axisLocal.normalized : Vector3.right;
            t.localRotation = Quaternion.AngleAxis(degrees, axisLocal);
        }

        public void ToggleItems(bool state)
        {
            BasisDebug.Log($"Toggle Vehicle To {state}");

            if (Rigidbody != null)
                Rigidbody.isKinematic = !state;

            if (Colliders != null)
            {
                foreach (var item in Colliders)
                    if (item != null) item.enabled = state;
            }

            if (Wheels != null)
            {
                foreach (var wheel in Wheels)
                    if (wheel != null) wheel.enabled = state;
            }
            // EngineAudio.enabled = state || isRemote;
            //  SteeringWheel.enabled = state || isRemote;
        }
    }
}
