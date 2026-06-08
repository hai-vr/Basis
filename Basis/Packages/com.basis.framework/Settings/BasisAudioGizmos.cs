using System.Collections.Generic;
using Basis.BasisUI;
using Basis.Scripts.Drivers;
using Basis.Scripts.Networking;
using Basis.Scripts.Networking.Receivers;
using UnityEngine;

/// <summary>
/// Runtime visualisation of the remote-voice spatial-audio pipeline. Driven each
/// frame from <see cref="SMModuleDebugOptions"/> (under the master ShowGizmos gate)
/// so it appears live in VR/desktop, not just the editor scene view.
///
/// Three independent sub-toggles:
///  • Direction  — per speaker: an arrow along the Steam Audio source-forward axis
///                 (the directivity dipole axis), coloured by the net loudness that
///                 reaches the local listener (dipole × occlusion × listener-cone
///                 dampening). Red = the speaker is being attenuated for you. This
///                 is the direct visual test for the "facing me but quiet" dipole
///                 mis-alignment.
///  • Ranges     — the local listener's hearing-distance sphere (wireframe), plus a
///                 per-speaker full-volume ring at the AudioSource minDistance.
///  • ListenerCone — the directional-dampening cone (RAListenerConeAngle) projected
///                 from the listener: inside the wedge a source plays at full volume,
///                 outside it is damped toward RAListenerDampenAmount.
/// </summary>
public static class BasisAudioGizmos
{
    // Mirrored from settings by SMModuleDebugOptions.
    public static bool ShowDirection;
    public static bool ShowRanges;
    public static bool ShowListenerCone;
    public static bool ShowLabels;

    private const int RingSegments = 28;
    private const float ArrowBaseLength = 0.45f;   // metres, before avatar-scale
    private const float TipBaseSize = 0.05f;
    private const float LineBaseWidth = 0.006f;
    private const float ConeLength = 2.5f;          // how far to project the cone wedge
    // The cone apex would otherwise sit exactly on the listener camera, so every
    // spoke converges on the near plane and clutters/clips the view. Push it forward.
    private const float ConeApexOffset = 0.6f;
    private const float LabelBaseScale = 0.025f;
    private const float LabelBaseHeight = 0.12f;    // lift above the anchor point

    private static readonly Color HearingColor = new Color(0.25f, 0.7f, 1f, 1f);
    private static readonly Color MinDistanceColor = new Color(0.3f, 0.95f, 0.4f, 1f);
    private static readonly Color ConeColor = new Color(1f, 0.6f, 0.15f, 1f);
    private static readonly Color GainLow = new Color(0.95f, 0.2f, 0.15f, 1f);
    private static readonly Color GainMid = new Color(0.95f, 0.85f, 0.2f, 1f);
    private static readonly Color GainHigh = new Color(0.3f, 0.9f, 0.35f, 1f);

    private struct SpeakerGizmos
    {
        public int ArrowLine;
        public int TipSphere;
        public int MinRing;
        public int Label;
        public int LastPct;     // cached so the label string only rebuilds on change
        public string LabelText;
    }

    private static readonly Dictionary<ushort, SpeakerGizmos> _speakers = new Dictionary<ushort, SpeakerGizmos>();
    private static readonly List<ushort> _seen = new List<ushort>();

    // Listener-centric gizmos (created once, repositioned each frame).
    private static int _hearingRingX = -1, _hearingRingY = -1, _hearingRingZ = -1;
    private static int _coneStar = -1, _coneRing = -1;
    private static int _hearingLabel = -1, _coneLabel = -1;
    private static int _lastHearingMeters = int.MinValue;
    private static int _lastConeDegrees = int.MinValue;
    private static string _hearingLabelText = "";
    private static string _coneLabelText = "";

    // Listener camera position this frame, used to billboard labels toward the viewer.
    private static Vector3 _camPos;

    // Reused so per-frame ring/star rebuilds don't allocate.
    private static readonly Vector3[] _ringScratch = new Vector3[RingSegments];
    private static readonly Vector3[] _coneScratch = new Vector3[9];

    private static bool _hooked;

    /// <summary>Per-frame entry point. <paramref name="scale"/> is the local avatar scale.</summary>
    public static void Tick(float scale)
    {
        EnsureMasterHook();

        if (!ShowDirection && !ShowRanges && !ShowListenerCone)
        {
            Shutdown();
            return;
        }
        if (scale <= 0f)
        {
            scale = 1f;
        }

        _camPos = BasisLocalCameraDriver.Position;
        UpdateListenerGizmos(scale);
        UpdateSpeakerGizmos(scale);
    }

    public static void Shutdown()
    {
        if (_speakers.Count > 0)
        {
            foreach (KeyValuePair<ushort, SpeakerGizmos> kvp in _speakers)
            {
                DestroySpeaker(kvp.Value);
            }
            _speakers.Clear();
        }
        DestroyId(ref _hearingRingX);
        DestroyId(ref _hearingRingY);
        DestroyId(ref _hearingRingZ);
        DestroyId(ref _coneStar);
        DestroyId(ref _coneRing);
        DestroyId(ref _hearingLabel);
        DestroyId(ref _coneLabel);
        _lastHearingMeters = int.MinValue;
        _lastConeDegrees = int.MinValue;
    }

    // ── Listener-centric: hearing sphere + dampening cone ───────────────────

    private static void UpdateListenerGizmos(float scale)
    {
        Vector3 listenerPos = BasisLocalCameraDriver.Position;
        float width = LineBaseWidth * scale;

        if (ShowRanges)
        {
            float radius = Mathf.Sqrt(Mathf.Max(0f, SMModuleDistanceBasedReductions.HearingRange));
            BuildCircle(listenerPos, Vector3.right, radius);
            EnsureRing(ref _hearingRingX, "AudioHearing_X", width, HearingColor);
            BasisGizmoManager.UpdateLineGizmo(_hearingRingX, _ringScratch);

            BuildCircle(listenerPos, Vector3.up, radius);
            EnsureRing(ref _hearingRingY, "AudioHearing_Y", width, HearingColor);
            BasisGizmoManager.UpdateLineGizmo(_hearingRingY, _ringScratch);

            BuildCircle(listenerPos, Vector3.forward, radius);
            EnsureRing(ref _hearingRingZ, "AudioHearing_Z", width, HearingColor);
            BasisGizmoManager.UpdateLineGizmo(_hearingRingZ, _ringScratch);

            if (ShowLabels)
            {
                int meters = Mathf.RoundToInt(radius);
                if (meters != _lastHearingMeters)
                {
                    _lastHearingMeters = meters;
                    _hearingLabelText = $"Hearing {meters} m";
                }
                // Anchor on the sphere's top edge, in front of the listener so it isn't
                // lost behind them.
                Vector3 anchor = listenerPos + Vector3.up * radius;
                UpdateLabel(ref _hearingLabel, "AudioHearingLabel", anchor, _hearingLabelText, HearingColor, scale);
            }
            else
            {
                DestroyId(ref _hearingLabel);
            }
        }
        else
        {
            DestroyId(ref _hearingRingX);
            DestroyId(ref _hearingRingY);
            DestroyId(ref _hearingRingZ);
            DestroyId(ref _hearingLabel);
        }

        if (ShowListenerCone)
        {
            float coneAngle = Mathf.Clamp(BasisSettingsDefaults.RAListenerConeAngle.RawValue, 0f, 360f);
            Vector3 fwd = BasisLocalCameraDriver.Forward();
            if (fwd.sqrMagnitude < 1e-6f)
            {
                fwd = Vector3.forward;
            }
            fwd.Normalize();
            // A 360° cone means "no dampening anywhere" — nothing meaningful to draw.
            if (coneAngle >= 360f)
            {
                DestroyId(ref _coneStar);
                DestroyId(ref _coneRing);
                DestroyId(ref _coneLabel);
            }
            else
            {
                // Apex pushed forward off the camera so the spokes don't converge on
                // the near plane (the reported clipping/clutter).
                Vector3 apex = listenerPos + fwd * (ConeApexOffset * scale);
                BuildCone(apex, fwd, coneAngle * 0.5f, out Vector3 ringCenter);
                EnsureStar(ref _coneStar, "AudioListenerCone", width, ConeColor);
                BasisGizmoManager.UpdateLineGizmo(_coneStar, _coneScratch);

                float ringRadius = ConeLength * Mathf.Tan(Mathf.Min(89f, coneAngle * 0.5f) * Mathf.Deg2Rad);
                BuildCircle(ringCenter, fwd, ringRadius);
                EnsureRing(ref _coneRing, "AudioListenerConeRing", width, ConeColor);
                BasisGizmoManager.UpdateLineGizmo(_coneRing, _ringScratch);

                if (ShowLabels)
                {
                    int deg = Mathf.RoundToInt(coneAngle);
                    if (deg != _lastConeDegrees)
                    {
                        _lastConeDegrees = deg;
                        _coneLabelText = $"Voice cone {deg}°";
                    }
                    UpdateLabel(ref _coneLabel, "AudioConeLabel", ringCenter, _coneLabelText, ConeColor, scale);
                }
                else
                {
                    DestroyId(ref _coneLabel);
                }
            }
        }
        else
        {
            DestroyId(ref _coneStar);
            DestroyId(ref _coneRing);
            DestroyId(ref _coneLabel);
        }
    }

    // ── Per-speaker: facing arrow + full-volume ring ────────────────────────

    private static void UpdateSpeakerGizmos(float scale)
    {
        _seen.Clear();

        BasisNetworkReceiver[] snapshot = BasisNetworkPlayers.ReceiversSnapshot;
        int count = BasisNetworkPlayers.ReceiverCount;
        float arrowLen = ArrowBaseLength * scale;
        float tipSize = TipBaseSize * scale;
        float width = LineBaseWidth * scale;

        for (int i = 0; i < count && i < snapshot.Length; i++)
        {
            BasisNetworkReceiver receiver = snapshot[i];
            if (receiver == null)
            {
                continue;
            }
            BasisAudioReceiver audio = receiver.AudioReceiverModule;
            if (audio == null || !audio.HasAudioSource || audio.AudioSourceTransform == null)
            {
                continue;
            }

            ushort id = receiver.playerId;
            _seen.Add(id);

            Transform sourceT = audio.AudioSourceTransform;
            Vector3 pos = sourceT.position;
            Vector3 fwd = sourceT.forward;
            Vector3 tip = pos + fwd * arrowLen;

            _speakers.TryGetValue(id, out SpeakerGizmos g);

            // Only the direction arrow (and its label) need the gain, and computing it
            // probes the SteamAudioSource — skip it when the arrow is off.
            float net = 0f;
            Color gain = default;

            if (ShowDirection)
            {
                net = NetLoudness(audio);
                gain = GainColor(net);

                if (g.ArrowLine <= 0)
                {
                    BasisGizmoManager.CreateLineGizmo($"AudioDir_{id}", out g.ArrowLine, pos, tip, width, gain);
                    BasisGizmoManager.CreateSphereGizmo($"AudioDirTip_{id}", out g.TipSphere, tip, tipSize, gain);
                }
                else
                {
                    BasisGizmoManager.UpdateLineGizmo(g.ArrowLine, pos, tip);
                    BasisGizmoManager.UpdateGizmoColor(g.ArrowLine, gain);
                    BasisGizmoManager.UpdateSphereGizmo(g.TipSphere, tip, Vector3.one * tipSize);
                    BasisGizmoManager.UpdateGizmoColor(g.TipSphere, gain);
                }
            }
            else if (g.ArrowLine > 0)
            {
                DestroyId(ref g.ArrowLine);
                DestroyId(ref g.TipSphere);
            }

            if (ShowRanges)
            {
                float minDist = audio.audioSource != null ? audio.audioSource.minDistance : 0.5f;
                BuildCircle(pos, Vector3.up, minDist);
                if (g.MinRing <= 0)
                {
                    BasisGizmoManager.CreateLineGizmo($"AudioMinDist_{id}", out g.MinRing, _ringScratch, width, MinDistanceColor, true);
                }
                else
                {
                    BasisGizmoManager.UpdateLineGizmo(g.MinRing, _ringScratch);
                }
            }
            else if (g.MinRing > 0)
            {
                DestroyId(ref g.MinRing);
            }

            // Label rides the direction arrow: speaker name + net loudness %. The
            // string only rebuilds when the rounded % changes, so a steady speaker
            // costs nothing but a billboard transform write.
            if (ShowLabels && ShowDirection)
            {
                int pct = Mathf.RoundToInt(net * 100f);
                if (g.Label <= 0 || pct != g.LastPct || g.LabelText == null)
                {
                    g.LastPct = pct;
                    string name = receiver.displayName;
                    g.LabelText = string.IsNullOrEmpty(name) ? $"{pct}%" : $"{name}  {pct}%";
                }
                Vector3 anchor = tip + Vector3.up * (LabelBaseHeight * scale);
                UpdateLabel(ref g.Label, $"AudioLabel_{id}", anchor, g.LabelText, gain, scale);
            }
            else if (g.Label > 0)
            {
                DestroyId(ref g.Label);
            }

            _speakers[id] = g;
        }

        PruneStaleSpeakers();
    }

    /// <summary>
    /// Lazily creates / updates a billboarded world-space label. Faces the listener
    /// camera each frame; text + colour are diffed inside the gizmo so an unchanged
    /// label only pays a transform write.
    /// </summary>
    private static void UpdateLabel(ref int id, string name, Vector3 position, string text, Color color, float scale)
    {
        Quaternion rot = Billboard(position);
        float labelScale = LabelBaseScale * scale;
        if (id <= 0)
        {
            BasisGizmoManager.CreateTextGizmo(name, out id, position, text, color);
        }
        BasisGizmoManager.UpdateTextGizmo(id, position, rot, labelScale, text, color);
    }

    private static Quaternion Billboard(Vector3 worldPos)
    {
        return BasisGizmoManager.BillboardRotation(worldPos, _camPos);
    }

    /// <summary>
    /// Net Basis/Steam-Audio gain reaching the listener: the dipole directivity term
    /// (the suspect for "facing me but quiet") × occlusion × the listener-cone
    /// dampening. AudioSource.volume itself is pinned to 1 — attenuation lives in the
    /// per-sample gain and the spatializer — so this is the meaningful loudness proxy.
    /// </summary>
    private static float NetLoudness(BasisAudioReceiver audio)
    {
        float net = Mathf.Clamp01(audio.DirectionalDampeningMultiplier);
#if STEAMAUDIO_ENABLED
        if (audio.audioSource != null && audio.audioSource.TryGetComponent<SteamAudio.SteamAudioSource>(out var sa))
        {
            net *= Mathf.Clamp01(sa.directivityValue) * Mathf.Clamp01(sa.occlusionValue);
        }
#endif
        return net;
    }

    // ── Geometry helpers ────────────────────────────────────────────────────

    private static void BuildCircle(Vector3 center, Vector3 normal, float radius)
    {
        normal = normal.sqrMagnitude < 1e-6f ? Vector3.up : normal.normalized;
        Vector3 tangent = Vector3.Cross(normal, Vector3.up);
        if (tangent.sqrMagnitude < 1e-4f)
        {
            tangent = Vector3.Cross(normal, Vector3.right);
        }
        tangent.Normalize();
        Vector3 bitangent = Vector3.Cross(normal, tangent);

        float step = (Mathf.PI * 2f) / RingSegments;
        for (int i = 0; i < RingSegments; i++)
        {
            float a = i * step;
            _ringScratch[i] = center + (tangent * Mathf.Cos(a) + bitangent * Mathf.Sin(a)) * radius;
        }
    }

    /// <summary>
    /// Star polyline radiating from the apex out to the 4 cone edges and the centre
    /// axis, drawn with one LineRenderer by returning to the apex between each spoke.
    /// </summary>
    private static void BuildCone(Vector3 apex, Vector3 fwd, float halfAngle, out Vector3 ringCenter)
    {
        Vector3 up = Vector3.Cross(fwd, Vector3.right);
        if (up.sqrMagnitude < 1e-4f)
        {
            up = Vector3.Cross(fwd, Vector3.up);
        }
        up.Normalize();
        Vector3 right = Vector3.Cross(up, fwd).normalized;

        Vector3 eUp = apex + (Quaternion.AngleAxis(-halfAngle, right) * fwd) * ConeLength;
        Vector3 eDown = apex + (Quaternion.AngleAxis(halfAngle, right) * fwd) * ConeLength;
        Vector3 eLeft = apex + (Quaternion.AngleAxis(-halfAngle, up) * fwd) * ConeLength;
        Vector3 eRight = apex + (Quaternion.AngleAxis(halfAngle, up) * fwd) * ConeLength;
        Vector3 center = apex + fwd * ConeLength;

        _coneScratch[0] = apex;
        _coneScratch[1] = eUp;
        _coneScratch[2] = apex;
        _coneScratch[3] = eDown;
        _coneScratch[4] = apex;
        _coneScratch[5] = eLeft;
        _coneScratch[6] = apex;
        _coneScratch[7] = eRight;
        _coneScratch[8] = center;

        ringCenter = center;
    }

    private static Color GainColor(float gain)
    {
        gain = Mathf.Clamp01(gain);
        return gain < 0.5f
            ? Color.Lerp(GainLow, GainMid, gain * 2f)
            : Color.Lerp(GainMid, GainHigh, (gain - 0.5f) * 2f);
    }

    // ── Lifecycle plumbing ──────────────────────────────────────────────────

    private static void EnsureRing(ref int id, string name, float width, Color color)
    {
        if (id <= 0)
        {
            BasisGizmoManager.CreateLineGizmo(name, out id, _ringScratch, width, color, true);
        }
    }

    private static void EnsureStar(ref int id, string name, float width, Color color)
    {
        if (id <= 0)
        {
            BasisGizmoManager.CreateLineGizmo(name, out id, _coneScratch, width, color, false);
        }
    }

    private static void DestroyId(ref int id)
    {
        if (id > 0)
        {
            BasisGizmoManager.DestroyGizmo(id);
            id = -1;
        }
    }

    private static void DestroySpeaker(SpeakerGizmos g)
    {
        if (g.ArrowLine > 0) BasisGizmoManager.DestroyGizmo(g.ArrowLine);
        if (g.TipSphere > 0) BasisGizmoManager.DestroyGizmo(g.TipSphere);
        if (g.MinRing > 0) BasisGizmoManager.DestroyGizmo(g.MinRing);
        if (g.Label > 0) BasisGizmoManager.DestroyGizmo(g.Label);
    }

    private static void PruneStaleSpeakers()
    {
        if (_speakers.Count == _seen.Count)
        {
            return;
        }
        List<ushort> stale = null;
        foreach (KeyValuePair<ushort, SpeakerGizmos> kvp in _speakers)
        {
            if (!_seen.Contains(kvp.Key))
            {
                (stale ??= new List<ushort>()).Add(kvp.Key);
            }
        }
        if (stale == null)
        {
            return;
        }
        for (int i = 0; i < stale.Count; i++)
        {
            if (_speakers.TryGetValue(stale[i], out SpeakerGizmos g))
            {
                DestroySpeaker(g);
                _speakers.Remove(stale[i]);
            }
        }
    }

    private static void EnsureMasterHook()
    {
        if (_hooked)
        {
            return;
        }
        BasisGizmoManager.OnUseGizmosChanged += OnMasterToggleChanged;
        _hooked = true;
    }

    /// <summary>
    /// Master ShowGizmos going off destroys BasisGizmoManager's gizmo dictionaries,
    /// so our cached IDs are stale — forget them. Next Tick re-creates cleanly.
    /// </summary>
    private static void OnMasterToggleChanged(bool state)
    {
        if (state)
        {
            return;
        }
        _speakers.Clear();
        _hearingRingX = _hearingRingY = _hearingRingZ = -1;
        _coneStar = _coneRing = -1;
        _hearingLabel = _coneLabel = -1;
        _lastHearingMeters = int.MinValue;
        _lastConeDegrees = int.MinValue;
    }
}
