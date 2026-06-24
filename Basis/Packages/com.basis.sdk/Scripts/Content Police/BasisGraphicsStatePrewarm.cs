using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

/// <summary>
/// Record-then-replay PSO cache layered on top of <see cref="BasisShaderPrewarm"/>.
/// ShaderVariantCollection.WarmUp only compiles the shader variant; on the explicit-pipeline
/// backends (D3D12 / Vulkan / Metal) the first-draw hitch is the full Pipeline State Object —
/// variant + vertex layout + blend/depth/stencil + render-target formats — which the legacy
/// path can't know ahead of time. GraphicsStateCollection traces the real PSOs that render and
/// persists them, so a later session can pre-create them.
///
/// This is complementary, not a replacement: BasisShaderPrewarm stays the proactive first-load
/// warm for never-before-seen content; this drains a chunk of last session's traced PSOs at each
/// content-load point, where the matching shaders are now resident. The reliable win is the base
/// app plus any content the player re-loads across sessions.
/// </summary>
public static class BasisGraphicsStatePrewarm
{
    // PSO caches are graphics-API + engine-version specific; a file traced on one is meaningless
    // on another. Encoding both in the filename means a driver/API/Unity change simply finds no
    // matching file and starts clean, instead of replaying stale or invalid state.
    private static bool _initialized;
    private static bool _supported;
    private static bool _tracing;
    private static string _filePath;
    private static GraphicsStateCollection _collection;

    // Warming creates GPU pipeline objects; cap per call so a large collection can't turn one
    // content load into a multi-second stall. Each load drains another chunk.
    private const int MaxWarmupPerCall = 64;

    // Only the explicit-PSO backends benefit. On GL / D3D11 the driver builds pipeline state
    // lazily and a precompiled cache buys nothing, so stay off and don't touch disk there.
    private static bool BackendBenefits()
    {
        switch (SystemInfo.graphicsDeviceType)
        {
            case GraphicsDeviceType.Direct3D12:
            case GraphicsDeviceType.Vulkan:
            case GraphicsDeviceType.Metal:
                return true;
            default:
                return false;
        }
    }

    public static void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }
        _initialized = true;
        _supported = BackendBenefits();
        if (!_supported)
        {
            return;
        }

        try
        {
            string dir = System.IO.Path.Combine(Application.persistentDataPath, "GraphicsState");
            System.IO.Directory.CreateDirectory(dir);
            _filePath = System.IO.Path.Combine(dir, $"basis_pso.{SystemInfo.graphicsDeviceType}.{Application.unityVersion}.gpsc");

            _collection = new GraphicsStateCollection();
            if (System.IO.File.Exists(_filePath))
            {
                // A corrupt or version-mismatched file throws or loads nothing; either way fall
                // back to an empty collection so this session still traces fresh.
                try
                {
                    _collection.LoadFromFile(_filePath);
                }
                catch (System.Exception load)
                {
                    BasisDebug.LogWarning($"BasisGraphicsStatePrewarm: load failed, starting fresh ({load.Message})", BasisDebug.LogTag.Event);
                    _collection = new GraphicsStateCollection();
                }
            }

            // Append this session's real PSOs to whatever loaded, so repeat content warms next launch.
            _collection.BeginTrace();
            _tracing = _collection.isTracing;
        }
        catch (System.Exception e)
        {
            BasisDebug.LogWarning($"BasisGraphicsStatePrewarm: init failed, disabling ({e.Message})", BasisDebug.LogTag.Event);
            _supported = false;
            _collection = null;
        }
    }

    /// <summary>
    /// Warms a bounded chunk of the persisted PSOs. Synchronous on purpose: callers invoke it from
    /// the same loading-screen point as <see cref="BasisShaderPrewarm.Warm"/>, so the stall is masked
    /// and the shaders for the content just loaded are resident. No-ops once the collection is drained.
    /// </summary>
    public static void WarmResident(string label)
    {
        EnsureInitialized();
        if (!_supported || _collection == null)
        {
            return;
        }
        try
        {
            if (_collection.variantCount == 0 || _collection.isWarmedUp)
            {
                return;
            }
            _collection.WarmUpProgressively(MaxWarmupPerCall);
        }
        catch (System.Exception e)
        {
            BasisDebug.LogWarning($"BasisGraphicsStatePrewarm: warm '{label}' failed ({e.Message})", BasisDebug.LogTag.Event);
        }
    }

    /// <summary>
    /// Stops tracing and persists the collection. Call on app quit / play-mode exit so the PSOs
    /// traced this session survive to warm the next one.
    /// </summary>
    public static void Flush()
    {
        if (!_initialized || !_supported || _collection == null)
        {
            return;
        }
        try
        {
            if (_tracing)
            {
                _collection.EndTrace();
                _tracing = false;
            }
            if (_filePath != null && _collection.variantCount > 0)
            {
                _collection.SaveToFile(_filePath);
            }
        }
        catch (System.Exception e)
        {
            BasisDebug.LogWarning($"BasisGraphicsStatePrewarm: flush failed ({e.Message})", BasisDebug.LogTag.Event);
        }
    }
}
