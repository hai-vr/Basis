#if UNITY_EDITOR
using System.IO;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

/// <summary>
/// Editor tool that bakes the world's APV into the volumetric fog's world-space 3D volume and saves the
/// result as a shippable Texture3D asset. It reuses the exact runtime bake: it requests a rebake, lets
/// the Scene/Game view render a few frames (which runs the renderer feature's bake pass while APV is
/// bound), then reads the produced render texture back and writes a Texture3D.
///
/// Assign the saved asset to the Volumetric Fog renderer feature's "Baked APV Volume Asset" slot to ship
/// it with a world and skip runtime baking.
/// </summary>
public sealed class VolumetricFogAPVBakerWindow : EditorWindow
{
    private string _savePath = "Assets/BakedAPVFogVolume.asset";
    private double _waitUntil;
    private bool _waiting;

    [MenuItem("Basis/Tools/Bake Volumetric Fog APV Volume", false, 503)]
    private static void Open()
    {
        GetWindow<VolumetricFogAPVBakerWindow>("Bake APV Fog Volume").minSize = new Vector2(380, 180);
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Bakes the world's APV into the volumetric fog 3D volume and saves it as a Texture3D asset.\n\n" +
            "Requires: APV baked + loaded in this scene, and an active Volumetric Fog renderer feature. " +
            "The bake bounds/voxel size come from that feature. Keep the Scene view visible (or enter Play " +
            "mode) so a render can run the bake.",
            MessageType.Info);

        _savePath = EditorGUILayout.TextField("Save Path", _savePath);

        if (!ProbeReferenceVolume.instance.isInitialized)
        {
            EditorGUILayout.HelpBox(
                "APV is not initialized in this scene. Bake APV first (Window > Rendering > Lighting > Generate Lighting).",
                MessageType.Warning);
        }

        using (new EditorGUI.DisabledScope(_waiting || string.IsNullOrEmpty(_savePath)))
        {
            if (GUILayout.Button("Bake & Save Texture3D"))
                StartBake();
        }

        if (_waiting)
            EditorGUILayout.LabelField("Baking... keep the Scene/Game view rendering.");
    }

    private void StartBake()
    {
        VolumetricFogAPVBaker.RequestRebake();
        SceneView.RepaintAll();

        _waiting = true;
        // Safety cap: the runtime bake runs a multi-frame settle window (APV streams its cells in over
        // several frames); normally we read back as soon as that window drains, but never wait past this.
        _waitUntil = EditorApplication.timeSinceStartup + 5.0;
        EditorApplication.update += PollBake;
    }

    private void PollBake()
    {
        // Keep nudging a render so the feature's bake pass executes and APV keeps streaming cells in.
        SceneView.RepaintAll();

        // Read back only once the settle window has drained (APV fully streamed and the final bake ran), or
        // the safety cap elapses - reading earlier would capture a partial, half-streamed volume.
        bool settled = !VolumetricFogAPVBaker.BakeRequested;
        bool timedOut = EditorApplication.timeSinceStartup >= _waitUntil;
        if (!settled && !timedOut)
            return;

        EditorApplication.update -= PollBake;
        _waiting = false;

        if (!VolumetricFogAPVBaker.IsReady || !(VolumetricFogAPVBaker.BakedVolume is RenderTexture rt) || rt.dimension != TextureDimension.Tex3D)
        {
            Debug.LogError("[Volumetric Fog] No baked APV volume was produced. Ensure APV is loaded and a Volumetric Fog feature is active, then try again.");
            Repaint();
            return;
        }

        SaveVolume(rt);
        Repaint();
    }

    private void SaveVolume(RenderTexture rt)
    {
        int w = rt.width;
        int h = rt.height;
        int d = rt.volumeDepth;

        // Explicit volume range so all depth slices are read (not just slice 0).
        AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(rt, 0, 0, w, 0, h, 0, d, GraphicsFormat.R16G16B16A16_SFloat);
        request.WaitForCompletion();

        if (request.hasError)
        {
            Debug.LogError("[Volumetric Fog] GPU readback of the baked APV volume failed.");
            return;
        }

        // RGBA16F = 4 halves per texel.
        NativeArray<ushort> data = request.GetData<ushort>();

        Texture3D tex = new Texture3D(w, h, d, GraphicsFormat.R16G16B16A16_SFloat, TextureCreationFlags.None)
        {
            name = Path.GetFileNameWithoutExtension(_savePath),
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        tex.SetPixelData(data, 0);
        tex.Apply(false, false);

        string directory = Path.GetDirectoryName(_savePath);
        if (!string.IsNullOrEmpty(directory) && !AssetDatabase.IsValidFolder(directory))
            Directory.CreateDirectory(directory);

        AssetDatabase.CreateAsset(tex, _savePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[Volumetric Fog] Saved baked APV volume ({w}x{h}x{d}, RGBA16F) to {_savePath}. Assign it to the Volumetric Fog feature's Baked APV Volume Asset slot to ship it.");
        EditorGUIUtility.PingObject(tex);
    }
}
#endif
