using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Public mirror of the native engine state (basis_media_state_t), usable from the
// editor assembly. Values match BasisNativeMedia.State.
public enum BasisMediaEngineState
{
    Idle = 0,
    Connecting = 1,
    Buffering = 2,
    Playing = 3,
    Paused = 4,
    Ended = 5,
    Error = 6,
}

// How the engine sizes its jitter buffer (how far behind live video is presented).
// Values match the native ABI: Fixed=0, Dynamic=1.
public enum BasisVideoBufferMode
{
    Fixed = 0,    // hold BufferMilliseconds exactly
    Dynamic = 1,  // auto-tune: grow on underrun risk, shrink when over-buffered
}

// Zero-copy live media source backed by the OS-codec engine in basis_media_native.
//
// Unlike the CPU IBasisFrameSource path (which decodes into managed byte[] frames
// and uploads them through a renderer), this source never sees pixels on the CPU:
// the native engine decodes with the OS hardware decoder straight into a GPU
// texture, and we wrap that texture's native handle with CreateExternalTexture.
// Each frame the player calls Pump() on the main thread, which issues a render-
// thread plugin event so the engine can publish its newest decoded frame into the
// Unity-visible texture, then polls the frame counter / size / state.
//
// Audio is decoded natively too and exposed as an IBasisPcmSource the audio sink
// pulls on the audio thread.
public sealed class BasisNativeVideoSource : IBasisPcmSource, IDisposable
{
    public event Action OnReady;
    public event Action<int, int> OnVideoSizeChanged;
    public event Action OnEndOfStream;
    public event Action<Exception> OnError;
    public event Action<BasisBitrateTrack> OnBitrateTrackChanged;
    public event Action<BasisAudioTrack> OnAudioTrackChanged;

    public string Url { get; }
    public Texture OutputTexture => externalTexture;
    public Vector2Int VideoSize => new Vector2Int(texW, texH);
    public bool IsRunning => handle != IntPtr.Zero && started && !disposed;
    public BasisMediaEngineState State => (BasisMediaEngineState)(int)BasisNativeMedia.GetState(handle);

    // PTS (microseconds) of the most recently published frame; -1 until known.
    public long PositionUs => BasisNativeMedia.GetPositionUs(handle);

    // One-line native counter string (blit/copy/lag/buf/nodue/acq/audio). For the
    // debug window / diagnostics.
    public string DebugInfo => handle != IntPtr.Zero ? BasisNativeMedia.GetDebug(handle) : null;

    // Monotonic count of frames decoded + written to the ring (for fps readouts).
    public ulong DecodedFrameCount => BasisNativeMedia.GetFrameCounter(handle);

    private static IntPtr renderEventFunc;
    private static bool renderEventResolved;

    private IntPtr handle;
    private CommandBuffer renderCmd;
    private Texture2D externalTexture;
    private IntPtr lastTexturePtr;
    private ulong lastFrameCounter;
    private int texW, texH;
    private bool started;
    private bool disposed;
    private bool readyFired;
    private bool eosRaised;
    private bool errorRaised;

    // Desired jitter buffer; applied to the native engine on Start and on change.
    private int bufferMode = (int)BasisVideoBufferMode.Dynamic;
    private int bufferMs = 120;

    private readonly List<BasisBitrateTrack> bitrateTracks = new List<BasisBitrateTrack>();
    private readonly List<BasisAudioTrack> audioTracks = new List<BasisAudioTrack>();
    private int lastReportedBitrateIndex = -2;
    private int lastReportedAudioTrackIndex = -2;

    public IReadOnlyList<BasisBitrateTrack> BitrateTracks => bitrateTracks;
    public IReadOnlyList<BasisAudioTrack> AudioTracks => audioTracks;
    public int SelectedBitrateIndex => BasisNativeMedia.GetSelectedBitrate(handle);
    public int SelectedAudioTrackIndex => BasisNativeMedia.GetSelectedAudioTrack(handle);

    public bool SelectBitrate(int index)
    {
        bool ok = BasisNativeMedia.SelectBitrate(handle, index);
        if (ok) PollTrackStateOnce();
        return ok;
    }

    public bool SelectAudioTrack(int index)
    {
        bool ok = BasisNativeMedia.SelectAudioTrack(handle, index);
        if (ok) PollTrackStateOnce();
        return ok;
    }

    public bool SeekBackUs(long backUs) => BasisNativeMedia.SeekBackUs(handle, backUs);

    // Diagnostics: heartbeat so we can see whether frames keep flowing.
    private int pumpCount;
    private BasisMediaEngineState lastLoggedState = (BasisMediaEngineState)(-1);

    public BasisNativeVideoSource(string url)
    {
        if (string.IsNullOrEmpty(url)) throw new ArgumentNullException(nameof(url));
        Url = url;
    }

    public void Start()
    {
        if (disposed) throw new ObjectDisposedException(nameof(BasisNativeVideoSource));
        if (started) return;
        ResolveRenderEventFunc();
        handle = BasisNativeMedia.Open(Url); // throws with build instructions if the lib is missing
        if (handle == IntPtr.Zero)
            throw new InvalidOperationException($"basis_media_open returned null for '{Url}' (unsupported scheme or out of memory).");
        BasisNativeMedia.Play(handle);
        BasisNativeMedia.SetBuffer(handle, bufferMode, bufferMs);
        started = true;
        readyFired = eosRaised = errorRaised = false;
        lastFrameCounter = 0;
        lastTexturePtr = IntPtr.Zero;
    }

    // Selects the jitter buffer length and mode. Applied live if running, and
    // remembered for (re)Start. ms <= 0 keeps the current length.
    public void SetBuffer(BasisVideoBufferMode mode, int ms)
    {
        bufferMode = (int)mode;
        if (ms > 0) bufferMs = ms;
        if (handle != IntPtr.Zero) BasisNativeMedia.SetBuffer(handle, bufferMode, bufferMs);
    }

    public void Play() { if (handle != IntPtr.Zero) BasisNativeMedia.Play(handle); }
    public void Pause() { if (handle != IntPtr.Zero) BasisNativeMedia.Pause(handle); }

    public void Stop()
    {
        if (!started) return;
        started = false;
        if (handle != IntPtr.Zero) BasisNativeMedia.Stop(handle);
    }

    private void PollTrackStateOnce()
    {
        if (handle == IntPtr.Zero) return;

        int brCount = BasisNativeMedia.GetBitrateTrackCount(handle);
        if (brCount != bitrateTracks.Count)
        {
            bitrateTracks.Clear();
            for (int i = 0; i < brCount; i++)
            {
                if (BasisNativeMedia.TryGetBitrateTrack(handle, i, out int bps, out int w, out int h, out string codec, out string label))
                {
                    bitrateTracks.Add(new BasisBitrateTrack { Index = i, BitsPerSecond = bps, Width = w, Height = h, Codec = codec, Label = label });
                }
            }
        }
        int brSel = BasisNativeMedia.GetSelectedBitrate(handle);
        if (brSel != lastReportedBitrateIndex)
        {
            lastReportedBitrateIndex = brSel;
            if (brSel >= 0 && brSel < bitrateTracks.Count) OnBitrateTrackChanged?.Invoke(bitrateTracks[brSel]);
        }

        int atCount = BasisNativeMedia.GetAudioTrackCount(handle);
        if (atCount != audioTracks.Count)
        {
            audioTracks.Clear();
            for (int i = 0; i < atCount; i++)
            {
                if (BasisNativeMedia.TryGetAudioTrack(handle, i, out int ch, out int sr, out int bps, out bool dm, out string lang, out string codec, out string label))
                {
                    audioTracks.Add(new BasisAudioTrack { Index = i, ChannelCount = ch, SampleRate = sr, BitsPerSecond = bps, IsDualMono = dm, Language = lang, Codec = codec, Label = label });
                }
            }
        }
        int atSel = BasisNativeMedia.GetSelectedAudioTrack(handle);
        if (atSel != lastReportedAudioTrackIndex)
        {
            lastReportedAudioTrackIndex = atSel;
            if (atSel >= 0 && atSel < audioTracks.Count) OnAudioTrackChanged?.Invoke(audioTracks[atSel]);
        }
    }

    // Main-thread tick: drive the render-thread texture update and surface events.
    public void Pump()
    {
        if (handle == IntPtr.Zero || disposed) return;

        PollTrackStateOnce();

        // Ask the engine to publish its newest decoded frame into the Unity
        // texture on the render thread. IssuePluginEventAndData lives on
        // CommandBuffer (not GL); run it via a reused buffer. Cheap no-op
        // natively when no new frame is ready.
        if (renderEventFunc != IntPtr.Zero)
        {
            renderCmd ??= new CommandBuffer { name = "BasisNativeMedia.Update" };
            renderCmd.Clear();
            renderCmd.IssuePluginEventAndData(renderEventFunc, BasisNativeMedia.RenderUpdate, handle);
            Graphics.ExecuteCommandBuffer(renderCmd);
        }

        ulong fc = BasisNativeMedia.GetFrameCounter(handle);
        if (fc != lastFrameCounter)
        {
            lastFrameCounter = fc;
            IntPtr tex = BasisNativeMedia.GetTexture(handle, out int w, out int h);
            if (tex != IntPtr.Zero && (tex != lastTexturePtr || w != texW || h != texH || externalTexture == null))
            {
                RebindTexture(tex, w, h);
                OnVideoSizeChanged?.Invoke(texW, texH);
            }
            if (!readyFired && externalTexture != null)
            {
                readyFired = true;
                OnReady?.Invoke();
            }
        }

        switch (BasisNativeMedia.GetState(handle))
        {
            case BasisNativeMedia.State.Error when !errorRaised:
                errorRaised = true;
                OnError?.Invoke(new Exception(BasisNativeMedia.GetLastError(handle) ?? "basis_media_native reported an error."));
                break;
            case BasisNativeMedia.State.Ended when !eosRaised:
                eosRaised = true;
                OnEndOfStream?.Invoke();
                break;
        }

        // Heartbeat: log on every state change and ~every 120 pumps so the Editor
        // log shows whether frames keep flowing (frames climbing), the stream
        // ended (state=Ended), or the texture froze (frames stuck but no error).
        pumpCount++;
        BasisMediaEngineState hb = State;
        if (hb != lastLoggedState || (pumpCount % 120) == 0)
        {
            lastLoggedState = hb;
            BasisDebug.Log(
                $"[NativeMedia] state={hb} frames={fc} posUs={PositionUs} tex={(externalTexture != null ? $"{texW}x{texH}" : "none")} [{BasisNativeMedia.GetDebug(handle)}]",
                BasisDebug.LogTag.Video);
        }
    }

    private void RebindTexture(IntPtr nativeTex, int w, int h)
    {
        if (externalTexture != null)
        {
            // Destroys only the managed wrapper; the native texture is owned by
            // the engine and freed on the render thread at Dispose.
            UnityEngine.Object.Destroy(externalTexture);
            externalTexture = null;
        }
        texW = w;
        texH = h;
        lastTexturePtr = nativeTex;
        TextureFormat fmt = NativeTextureFormat();
        externalTexture = Texture2D.CreateExternalTexture(w, h, fmt, false, false, nativeTex);
        externalTexture.wrapMode = TextureWrapMode.Clamp;
        externalTexture.filterMode = FilterMode.Bilinear;
    }

    private static TextureFormat NativeTextureFormat()
    {
        switch (SystemInfo.graphicsDeviceType)
        {
            case GraphicsDeviceType.Direct3D11:
            case GraphicsDeviceType.Direct3D12:
                return TextureFormat.BGRA32; // MF VideoProcessor output is BGRA
            default:
                return TextureFormat.RGBA32; // GLES external blit target
        }
    }

    // ---- IBasisPcmSource (audio thread) ----

    public bool TryGetPcmFormat(out int sampleRate, out int channels)
        => BasisNativeMedia.TryGetAudioFormat(handle, out sampleRate, out channels);

    public int ReadPcm(float[] buffer)
    {
        if (handle == IntPtr.Zero || buffer == null) return 0;
        return BasisNativeMedia.ReadAudio(handle, buffer, buffer.Length);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Stop();

        // Drop the managed wrapper first so Unity stops sampling the native
        // texture, then close. We deliberately do NOT issue a deferred
        // RenderRelease plugin event: it would run at end-of-frame, after
        // basis_media_close has already freed the engine (use-after-free).
        // basis_media_close joins the decode threads and frees GPU resources
        // inside basis_decoder_destroy — D3D11/D3D12 COM Release is thread-safe,
        // and the joined threads guarantee nothing is mid-decode.
        if (externalTexture != null)
        {
            UnityEngine.Object.Destroy(externalTexture);
            externalTexture = null;
        }

        if (handle != IntPtr.Zero)
        {
            BasisNativeMedia.Close(handle);
            handle = IntPtr.Zero;
        }

        if (renderCmd != null)
        {
            renderCmd.Dispose();
            renderCmd = null;
        }
    }

    private static void ResolveRenderEventFunc()
    {
        if (renderEventResolved) return;
        renderEventResolved = true;
        renderEventFunc = BasisNativeMedia.GetRenderEventFunc();
        if (renderEventFunc == IntPtr.Zero)
        {
            BasisDebug.LogWarning(
                "basis_media_native render-event function is null; the native library may be missing. " +
                "Video texture updates will not run until it is built and present in Plugins/.",
                BasisDebug.LogTag.Video);
        }
    }
}
