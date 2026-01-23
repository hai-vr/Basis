// BasisNetworkedVehicle.cs
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
        public Transform[] SpinVisuals;
        public Transform[] SteerVisuals;

        public Vector3 SpinAxisLocal = Vector3.right;
        public Vector3 SteerAxisLocal = Vector3.up;

        [Header("Owner Send")]
        public float SendInterval = 0.05f;

        [Header("Remote Playback (Jitter Buffer)")]
        public float InterpDelay = 0.10f;
        public int MaxBufferSize = 32;
        public float MaxExtrapolation = 0.10f;
        public float MaxExtrapolationSpeed = 200f;

        [Header("Adaptive Interp Delay")]
        public float MinInterpDelay = 0.03f;
        public float MaxInterpDelay = 0.12f;
        public float DelayAdjustSpeed = 6.0f;

        [Header("Wheel Quantization")]
        [Range(8, 12)] public int SpinBits = 12;
        [Range(7, 11)] public int SteerBits = 10;
        public float SteerRangeDeg = 60f;

        [Header("Engine / SteeringWheel Sync")]
        [Range(6, 10)] public int EngineBits = 8;
        [Range(7, 11)] public int SteerRatioBits = 9;

        // --- NEW: tick settings ---
        [Header("Tick Timing")]
        [Tooltip("Seconds per tick. Usually same as SendInterval. Keep constant for stable playback.")]
        public float TickInterval = 0.05f;

        private float _sendTimer;
        private Transform _tr;

        public BasisPlayer Player;

        private struct Snapshot
        {
            public double t;      // reconstructed sender time mapped into local time domain
            public Vector3 pos;
            public Quaternion rot;
            public Vector3 scale;

            // Wheel angles:
            public float[] spinDeg;   // UNWRAPPED (can exceed 360)
            public float[] steerDeg;  // -range..+range

            public float engineRevs01;
            public float steerRatio;

            public ushort tick;
        }

        private readonly List<Snapshot> _snapshots = new List<Snapshot>(64);

        private Vector3 _vel;
        private Vector3 _angVel;
        private bool _haveVel;

        private float[] _ownerSpinAbsDeg;
        private int _wheelCount;
        private int _steerCount;

        private float _remoteEngineRevs01;
        private float _remoteSteerRatio;

        // --- NEW: sender tick counter (owner) ---
        private ushort _ownerTick;

        // --- NEW: tick->time mapping (receiver) ---
        private bool _haveTickBase;
        private ushort _tickBase;
        private double _timeBase;

        // --- NEW: adaptive delay ---
        private float _adaptiveDelay;

        public override void Start()
        {
            Player = null;
            base.Start();

            _tr = transform;
            _adaptiveDelay = InterpDelay;

            if (Seat != null)
            {
                Seat.OnPlayerEnterSeat += OnPlayerEnterSeat;
                Seat.OnPlayerExitSeat += OnPlayerExitSeat;
            }

            _wheelCount = Wheels != null ? Wheels.Length : 0;
            _steerCount = SteerVisuals != null ? SteerVisuals.Length : 0;

            _ownerSpinAbsDeg = new float[_wheelCount];

            if (Player == null || !Player.IsLocal)
            {
                ToggleItems(false);
                ApplyRemoteExtrasToParts(0f, 0f);
            }

            BasisLocalPlayer.JustBeforeNetworkApply.AddAction(9, SimulateLocal);
            BasisLocalPlayer.JustBeforeNetworkApply.AddAction(10, SimulateRemote);
        }

        public override void OnDestroy()
        {
            BasisLocalPlayer.JustBeforeNetworkApply.RemoveAction(9, SimulateLocal);
            BasisLocalPlayer.JustBeforeNetworkApply.RemoveAction(10, SimulateRemote);

            if (Seat != null)
            {
                Seat.OnPlayerEnterSeat -= OnPlayerEnterSeat;
                Seat.OnPlayerExitSeat -= OnPlayerExitSeat;
            }

            base.OnDestroy();
        }

        private void OnPlayerExitSeat(BasisPlayer player)
        {
            Player = null;
            ToggleItems(false);
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
                _haveTickBase = false;
                _ownerTick = 0;
                _sendTimer = 0f;

                for (int i = 0; i < _ownerSpinAbsDeg.Length; i++)
                    _ownerSpinAbsDeg[i] = 0f;

                if (EngineAudio != null) EngineAudio.UseNetworkRevs = false;
                if (SteeringWheel != null) SteeringWheel.UseNetworkSteerRatio = false;
            }
            else
            {
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
            if (buffer == null) return;
            if (Player != null && Player.IsLocal) return;

            int expectedMin = BasisVehicleNetCodec.MinPacketSize
                            + BasisVehicleWheelNetCodec.ExtraBytes(_wheelCount, _steerCount, SpinBits, SteerBits, EngineBits, SteerRatioBits);

            if (buffer.Length < expectedMin) return;

            BasisVehicleWheelNetCodec.ReadPacketWithWheels(
                buffer,
                _wheelCount, _steerCount,
                SpinBits, SteerBits,
                EngineBits, SteerRatioBits,
                -SteerRangeDeg, SteerRangeDeg,
                out Vector3 pos, out Quaternion rot, out Vector3 scale,
                out float[] spinDegMod, out float[] steerDeg,
                out float engineRevs01, out float steerRatio,
                out ushort tick
            );

            double now = Time.timeAsDouble;

            // --- NEW: tick->time mapping (turn jittery arrival into stable timeline) ---
            if (!_haveTickBase)
            {
                _haveTickBase = true;
                _tickBase = tick;
                _timeBase = now;
            }

            int dticks = TickDelta(_tickBase, tick);
            double snapTime = _timeBase + dticks * TickInterval;

            // --- NEW: unwrap spin so it doesn't "shortest-arc" reverse ---
            float[] spinUnwrapped = UnwrapSpinAgainstLast(spinDegMod);

            _remoteEngineRevs01 = engineRevs01;
            _remoteSteerRatio = steerRatio;

            var s = new Snapshot
            {
                t = snapTime,
                pos = pos,
                rot = rot,
                scale = scale,
                spinDeg = spinUnwrapped,
                steerDeg = steerDeg,
                engineRevs01 = engineRevs01,
                steerRatio = steerRatio,
                tick = tick
            };

            // keep in order by time (rarely, out-of-order happens)
            InsertSnapshotSorted(s);

            if (_snapshots.Count > MaxBufferSize)
                _snapshots.RemoveRange(0, _snapshots.Count - MaxBufferSize);

            UpdateVelocityEstimates();
        }

        // ---------------- OWNER SEND ----------------
        public void SimulateLocal()
        {
            if (Player != null && Player.IsLocal)
            {
                UpdateOwnerAbsoluteWheelSpin();

                _sendTimer += Time.deltaTime;
                if (_sendTimer >= SendInterval)
                {
                    _sendTimer -= SendInterval;

                    // tick increments on each send
                    _ownerTick++;

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
                        _ownerTick,
                        SpinBits, SteerBits,
                        EngineBits, SteerRatioBits,
                        -SteerRangeDeg, SteerRangeDeg
                    );

                    SendCustomNetworkEvent(data, Basis.Network.Core.DeliveryMethod.Sequenced);
                }

                return;
            }
        }

        // ---------------- REMOTE PLAYBACK ----------------
        public void SimulateRemote()
        {
            if (_snapshots.Count == 0) return;

            // --- NEW: adaptive delay based on buffer health ---
            float targetDelay = 1.3f * SendInterval;
            int buffered = _snapshots.Count;
            if (buffered < 2)
            {
                targetDelay = 3.0f * SendInterval;
            }
            else if (buffered > 6)
            {
                targetDelay = 1.8f * SendInterval;
            }

            _adaptiveDelay = Mathf.MoveTowards(
                _adaptiveDelay,
                Mathf.Clamp(targetDelay, MinInterpDelay, MaxInterpDelay),
                DelayAdjustSpeed * Time.deltaTime
            );

            double now = Time.timeAsDouble;
            double renderTime = now - _adaptiveDelay;

            // drop old snapshots until we straddle renderTime
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

        // ---------------- Snapshot helpers ----------------
        private void InsertSnapshotSorted(in Snapshot s)
        {
            // Usually already in order; fast path
            if (_snapshots.Count == 0 || _snapshots[_snapshots.Count - 1].t <= s.t)
            {
                _snapshots.Add(s);
                return;
            }

            // Otherwise insert by time (rare out-of-order)
            int idx = _snapshots.BinarySearch(s, SnapshotTimeComparer.Instance);
            if (idx < 0) idx = ~idx;
            _snapshots.Insert(idx, s);
        }

        private sealed class SnapshotTimeComparer : IComparer<Snapshot>
        {
            public static readonly SnapshotTimeComparer Instance = new SnapshotTimeComparer();
            public int Compare(Snapshot a, Snapshot b) => a.t.CompareTo(b.t);
        }

        private static int TickDelta(ushort a, ushort b)
        {
            // signed delta with wrap support
            return (short)(b - a);
        }

        private float[] UnwrapSpinAgainstLast(float[] incomingMod360)
        {
            int n = incomingMod360 != null ? incomingMod360.Length : 0;
            float[] outUnwrapped = new float[n];

            if (n == 0) return outUnwrapped;

            // if we have a last snapshot, unwrap against it; else just use incoming
            if (_snapshots.Count > 0 && _snapshots[_snapshots.Count - 1].spinDeg != null)
            {
                var prev = _snapshots[_snapshots.Count - 1].spinDeg;
                int m = Mathf.Min(prev.Length, n);

                for (int i = 0; i < m; i++)
                    outUnwrapped[i] = UnwrapClosest(prev[i], incomingMod360[i]);

                for (int i = m; i < n; i++)
                    outUnwrapped[i] = incomingMod360[i];
            }
            else
            {
                for (int i = 0; i < n; i++)
                    outUnwrapped[i] = incomingMod360[i];
            }

            return outUnwrapped;
        }

        private static float UnwrapClosest(float prevUnwrapped, float incomingMod)
        {
            float baseVal = Mathf.Repeat(incomingMod, 360f);
            float k = Mathf.Round((prevUnwrapped - baseVal) / 360f);
            return baseVal + 360f * k;
        }

        private static float Wrap360(float deg)
        {
            deg %= 360f;
            if (deg < 0f) deg += 360f;
            return deg;
        }

        private void UpdateVelocityEstimates()
        {
            int n = _snapshots.Count;
            if (n < 2)
            {
                _haveVel = false;
                return;
            }

            // Use last two snapshots (you can later upgrade to multi-sample smoothing)
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
            if (_steerCount == 0) return s;

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

        // ---------------- REMOTE: APPLY VISUALS ----------------
        private void ApplyRemoteWheelsInterpolated(in Snapshot a, in Snapshot b, float alpha)
        {
            // Spin: UNWRAPPED linear interpolation (no shortest-arc reversal)
            if (SpinVisuals != null && a.spinDeg != null && b.spinDeg != null)
            {
                int n = Mathf.Min(SpinVisuals.Length, Mathf.Min(a.spinDeg.Length, b.spinDeg.Length));
                for (int i = 0; i < n; i++)
                {
                    var t = SpinVisuals[i];
                    if (t == null) continue;

                    float degUnwrapped = Mathf.Lerp(a.spinDeg[i], b.spinDeg[i], alpha);
                    float deg = Wrap360(degUnwrapped);
                    SetAxisLocalRotation(t, SpinAxisLocal, deg);
                }
            }

            // Steer: LerpAngle is fine for [-range,+range]
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

                    SetAxisLocalRotation(t, SpinAxisLocal, Wrap360(s.spinDeg[i]));
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
        }
    }
}
