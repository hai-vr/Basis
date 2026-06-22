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

	private static bool s_BakeRequested;

	/// <summary>
	/// Requests that the runtime baker re-bake the APV in-scatter volume on the next rendered frame
	/// (once APV is initialized). Call after a world's APV registers, or to force a refresh. Cheap and
	/// idempotent - multiple requests before the next bake coalesce into one.
	/// </summary>
	public static void RequestRebake()
	{
		s_BakeRequested = true;
	}

	/// <summary>True when a rebake has been requested but not yet serviced.</summary>
	public static bool BakeRequested => s_BakeRequested;

	/// <summary>
	/// Atomically reads and clears the rebake request. The runtime baker calls this when it commits to
	/// baking on the current frame.
	/// </summary>
	public static bool ConsumeBakeRequest()
	{
		bool requested = s_BakeRequested;
		s_BakeRequested = false;
		return requested;
	}
}
