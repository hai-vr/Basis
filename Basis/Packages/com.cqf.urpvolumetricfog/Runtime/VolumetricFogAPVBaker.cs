using UnityEngine;

/// <summary>
/// Holds the baked APV in-scatter volume consumed by the volumetric fog's baked APV mode, and the
/// world-space bounds needed to map a position into it. Populated by the runtime bake pass or the
/// editor baker; until a bake exists, <see cref="IsReady"/> is false and the fog's baked APV mode
/// contributes nothing (it does not silently fall back to the expensive live path).
///
/// This is intentionally a plain static holder so producers (a render-graph bake pass, an editor
/// window) and the consumer (the fog render pass) stay decoupled, and so world-load code can request
/// a rebake without holding a reference to the renderer feature.
/// </summary>
public static class VolumetricFogAPVBaker
{
	/// <summary>The baked world-space 3D texture of APV in-scatter (RGB irradiance). Null until baked.</summary>
	public static Texture BakedVolume { get; private set; }

	/// <summary>World-space min corner of the baked volume.</summary>
	public static Vector3 BoundsMin { get; private set; }

	/// <summary>World-space size of the baked volume.</summary>
	public static Vector3 BoundsSize { get; private set; }

	/// <summary>1 / <see cref="BoundsSize"/>, precomputed for the shader's world-to-uvw mapping (0 on degenerate axes).</summary>
	public static Vector3 BoundsInvSize { get; private set; }

	/// <summary>True when a baked volume is available to sample.</summary>
	public static bool IsReady => BakedVolume != null;

	/// <summary>
	/// Assigns the baked volume and its world-space bounds. Called by the bake pass / editor baker.
	/// </summary>
	/// <param name="volume">The baked 3D texture (RenderTexture with dimension Tex3D, or a Texture3D asset).</param>
	/// <param name="boundsMin">World-space min corner the volume covers.</param>
	/// <param name="boundsSize">World-space size the volume covers.</param>
	public static void SetBakedVolume(Texture volume, Vector3 boundsMin, Vector3 boundsSize)
	{
		BakedVolume = volume;
		BoundsMin = boundsMin;
		BoundsSize = boundsSize;
		BoundsInvSize = new Vector3(
			boundsSize.x > 1e-6f ? 1.0f / boundsSize.x : 0.0f,
			boundsSize.y > 1e-6f ? 1.0f / boundsSize.y : 0.0f,
			boundsSize.z > 1e-6f ? 1.0f / boundsSize.z : 0.0f);
	}

	/// <summary>
	/// Drops the reference to the baked volume (e.g. on world unload). Does not release the texture;
	/// the owner that created it is responsible for its lifetime.
	/// </summary>
	public static void Clear()
	{
		BakedVolume = null;
	}

	// APV GPU streaming uploads only a few cells per frame (URP default: 1), nearest-camera-first, so a
	// world's full probe data isn't resident until several frames after its APV registers. A bake taken on
	// the first eligible frame therefore captures only the cells near the camera - the rest of the world
	// reads as empty until something happens to request another bake (e.g. toggling the feature off/on).
	// To avoid that, a request keeps the bake alive for a short settle window: the runtime baker re-bakes
	// each frame (while forcing APV to stream everything in) and the window's final frame is the
	// authoritative, fully-streamed full-world bake.
	private const int BakeSettleFrames = 16;

	private static int s_PendingBakeFrames;

	/// <summary>
	/// Requests that the runtime baker (re)bake the APV in-scatter volume, then keep re-baking for a short
	/// settle window so APV GPU streaming can make the whole bake region resident first. Call after a
	/// world's APV registers, or to force a refresh. Cheap and idempotent - calls coalesce / re-arm the window.
	/// </summary>
	public static void RequestRebake()
	{
		s_PendingBakeFrames = BakeSettleFrames;
	}

	/// <summary>True while a bake is pending or its settle window has not yet drained.</summary>
	public static bool BakeRequested => s_PendingBakeFrames > 0;

	/// <summary>
	/// The runtime baker calls this on each frame it actually dispatches a bake, advancing the settle
	/// window. The window keeps the bake re-running while APV streams in; its final frame is authoritative.
	/// </summary>
	public static void NotifyBakeServiced()
	{
		if (s_PendingBakeFrames > 0)
			s_PendingBakeFrames--;
	}

	/// <summary>
	/// Clears any pending bake / settle window without baking. Used by the shipped-asset path (which needs
	/// no runtime bake) and to abandon the window. Returns whether a bake was pending.
	/// </summary>
	public static bool ConsumeBakeRequest()
	{
		bool requested = s_PendingBakeFrames > 0;
		s_PendingBakeFrames = 0;
		return requested;
	}
}
