using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Adds an "SSGIGBuffer" pass to Poiyomi URP shaders so surfaces using them receive screen space global illumination
/// with their real albedo and normals, instead of the constant-albedo fallback.
///
/// Poiyomi URP (Toon and Pro, 10.x) ships UniversalForward / DepthOnly / DepthNormals / MotionVectors passes but no
/// "UniversalGBuffer" pass. This tool copies a shader's DepthNormals pass, which already computes the base colour, alpha
/// clipping and the normal, and turns the copy into a pass that writes the first three targets of the URP GBuffer
/// layout (albedo, metallic/occlusion, normal + smoothness). The pass uses its own LightMode so the Deferred rendering
/// path never picks it up; only the SSGI forward GBuffer pass draws it. Skinned meshes work like any other renderer.
///
/// Shaders locked by the Poiyomi shader optimizer are generated from the master shader, so locking a material after
/// running this keeps the pass. Run it again after updating Poiyomi or for shaders locked before it ran.
/// This file has no dependency on the rest of the package and can be copied into any project that contains Poiyomi.
/// </summary>
public static class ScreenSpaceGlobalIlluminationPoiyomiGBufferPass
{
    public const string LightMode = "SSGIGBuffer";

    private const string DepthNormalsTag = "\"LightMode\" = \"DepthNormals\"";
    private const string GBufferTag = "\"LightMode\" = \"" + LightMode + "\"";
    private const string PoiyomiUrpMarker = "POI_PIPE == POI_URP";
    private const string DepthNormalsDefine = "#define POI_PASS_DEPTH_NORMALS";

    private static readonly Regex PassLine = new Regex(@"^[ \t]*Pass[ \t]*\r?$", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex PassCloseLine = new Regex(@"^[ \t]*\}[ \t]*\r?$", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex StencilBlock = new Regex(@"^[ \t]*Stencil[ \t]*\r?\n[ \t]*\{[^}]*\}[ \t]*\r?\n(?:[ \t]*\r?\n)?", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex BlendOpLine = new Regex(@"BlendOp \[_BlendOp\], \[_BlendOpAlpha\]", RegexOptions.Compiled);
    private static readonly Regex BlendLine = new Regex(@"Blend \[_SrcBlend\] \[_DstBlend\], \[_SrcBlendAlpha\] \[_DstBlendAlpha\]", RegexOptions.Compiled);
    private static readonly Regex DepthNormalsDefineLine = new Regex(@"^([ \t]*)#define POI_PASS_DEPTH_NORMALS[ \t]*(?=\r?$)", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex RenderingLayersInclude = new Regex(@"[ \t]*#if POI_PIPE == POI_URP\s+#include_with_pragmas ""Packages/com\.unity\.render-pipelines\.universal/ShaderLibrary/RenderingLayers\.hlsl""\s+#endif[ \t]*\r?\n", RegexOptions.Compiled);
    private static readonly Regex FragmentSignature = new Regex(
        @"#if POI_PIPE == POI_BIRP\s+float4\s+#else\s+void\s+#endif\s+frag\(\s*VertexOut i, bool facing : SV_IsFrontFace\s+#if POI_PIPE == POI_URP\s+,out half4 outNormalWS : SV_Target0\s+(?:#ifdef _WRITE_RENDERING_LAYERS\s+,out uint outRenderingLayers : SV_Target1\s+#endif\s+)?#endif\s+\)",
        RegexOptions.Compiled);
    private static readonly Regex FragmentTail = new Regex(
        @"#if POI_PIPE == POI_URP\s+float3 normalWS = NormalizeNormalPerPixel\(poiMesh\.normals\[0\]\);\s+outNormalWS = half4\(normalWS, 0\.0\) \+ POI_SAFE_RGB0;\s+(?:#ifdef _WRITE_RENDERING_LAYERS\s+outRenderingLayers = EncodeMeshRenderingLayer\(\);\s+#endif\s+)?#else\s+return float4\(0, 1, 0, 1\);\s+#endif",
        RegexOptions.Compiled);

    [MenuItem("Basis/Rendering/SSGI/Add GBuffer Pass To Poiyomi URP Shaders")]
    private static void InjectIntoProject()
    {
        List<string> candidates = FindShaderPaths(true);
        if (candidates.Count == 0)
        {
            EditorUtility.DisplayDialog("SSGI Poiyomi GBuffer Pass", "No Poiyomi URP shader without an SSGI GBuffer pass was found under Assets.", "OK");
            return;
        }
        if (!EditorUtility.DisplayDialog("SSGI Poiyomi GBuffer Pass", candidates.Count + " Poiyomi URP shader file(s) will get an \"" + LightMode + "\" pass appended after their DepthNormals pass.", "Add", "Cancel"))
            return;

        int done = 0;
        foreach (string path in candidates)
        {
            if (InjectIntoFile(path, out string error))
                done++;
            else
                Debug.LogWarning("SSGI Poiyomi GBuffer Pass: skipped " + path + " - " + error);
        }
        AssetDatabase.Refresh();
        Debug.Log("SSGI Poiyomi GBuffer Pass: added the pass to " + done + " of " + candidates.Count + " shader(s).");
    }

    [MenuItem("Basis/Rendering/SSGI/Remove GBuffer Pass From Poiyomi URP Shaders")]
    private static void RemoveFromProject()
    {
        List<string> candidates = FindShaderPaths(false);
        if (candidates.Count == 0)
        {
            EditorUtility.DisplayDialog("SSGI Poiyomi GBuffer Pass", "No shader with an SSGI GBuffer pass was found under Assets.", "OK");
            return;
        }
        if (!EditorUtility.DisplayDialog("SSGI Poiyomi GBuffer Pass", "The \"" + LightMode + "\" pass will be removed from " + candidates.Count + " shader file(s).", "Remove", "Cancel"))
            return;

        int done = 0;
        foreach (string path in candidates)
        {
            if (RemoveFromFile(path))
                done++;
        }
        AssetDatabase.Refresh();
        Debug.Log("SSGI Poiyomi GBuffer Pass: removed the pass from " + done + " shader(s).");
    }

    private static List<string> FindShaderPaths(bool withoutPass)
    {
        List<string> result = new List<string>();
        foreach (string guid in AssetDatabase.FindAssets("t:Shader"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.StartsWith("Assets/") || !path.EndsWith(".shader"))
                continue;
            string source = File.ReadAllText(path);
            if (withoutPass ? IsCandidate(source) : HasPass(source))
                result.Add(path);
        }
        return result;
    }

    /// <summary>A Poiyomi URP shader with a DepthNormals pass and no SSGI GBuffer pass yet.</summary>
    public static bool IsCandidate(string source)
    {
        return source.Contains(DepthNormalsTag) && source.Contains(PoiyomiUrpMarker) && !source.Contains(GBufferTag);
    }

    public static bool HasPass(string source)
    {
        return source.Contains(GBufferTag);
    }

    public static bool InjectIntoFile(string path, out string error)
    {
        string source = ReadText(path, out bool hadBom);
        if (!TryInject(source, out string result, out error))
            return false;
        WriteText(path, result, hadBom);
        AssetDatabase.ImportAsset(path);
        return true;
    }

    public static bool RemoveFromFile(string path)
    {
        string source = ReadText(path, out bool hadBom);
        if (!TryRemove(source, out string result))
            return false;
        WriteText(path, result, hadBom);
        AssetDatabase.ImportAsset(path);
        return true;
    }

    /// <summary>Builds the SSGI GBuffer pass from the DepthNormals pass and inserts it right after that pass.</summary>
    public static bool TryInject(string source, out string result, out string error)
    {
        result = source;
        error = null;

        if (!IsCandidate(source))
        {
            error = HasPass(source) ? "the shader already has an " + LightMode + " pass" : "not a Poiyomi URP shader with a DepthNormals pass";
            return false;
        }

        int tagIndex = source.IndexOf(DepthNormalsTag, System.StringComparison.Ordinal);
        if (!TryGetPassBounds(source, tagIndex, out int passStart, out int passEnd))
        {
            error = "could not find the bounds of the DepthNormals pass";
            return false;
        }

        string newline = source.Contains("\r\n") ? "\r\n" : "\n";
        string pass = source.Substring(passStart, passEnd - passStart);

        if (FragmentSignature.Matches(pass).Count != 1)
        {
            error = "the DepthNormals fragment signature does not match the known Poiyomi layout";
            return false;
        }
        if (FragmentTail.Matches(pass).Count != 1)
        {
            error = "the DepthNormals fragment output does not match the known Poiyomi layout";
            return false;
        }
        Match defineMatch = DepthNormalsDefineLine.Match(pass);
        if (!defineMatch.Success)
        {
            error = "the DepthNormals pass has no " + DepthNormalsDefine;
            return false;
        }

        string indent = defineMatch.Groups[1].Value;
        string gbufferPass = pass;
        gbufferPass = gbufferPass.Replace("Name \"DepthNormals\"", "Name \"" + LightMode + "\"");
        gbufferPass = gbufferPass.Replace(DepthNormalsTag, GBufferTag);
        gbufferPass = StencilBlock.Replace(gbufferPass, "");
        gbufferPass = BlendOpLine.Replace(gbufferPass, "BlendOp Add");
        gbufferPass = BlendLine.Replace(gbufferPass, "Blend One Zero");
        gbufferPass = RenderingLayersInclude.Replace(gbufferPass, "");
        gbufferPass = DepthNormalsDefineLine.Replace(gbufferPass,
            indent + DepthNormalsDefine + newline +
            indent + "#define POI_PASS_SSGI_GBUFFER" + newline +
            indent + "#pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT");
        gbufferPass = FragmentSignature.Replace(gbufferPass,
            "void frag( VertexOut i, bool facing : SV_IsFrontFace" + newline +
            indent + ", out half4 outGBuffer0 : SV_Target0" + newline +
            indent + ", out half4 outGBuffer1 : SV_Target1" + newline +
            indent + ", out half4 outGBuffer2 : SV_Target2" + newline +
            indent + ")");
        gbufferPass = FragmentTail.Replace(gbufferPass,
            "// Screen space global illumination GBuffer: URP layout, first three targets." + newline +
            indent + "\tfloat3 normalWS = NormalizeNormalPerPixel(poiMesh.normals[1]);" + newline +
            indent + "\t#if defined(_GBUFFER_NORMALS_OCT)" + newline +
            indent + "\tfloat2 octNormalWS = PackNormalOctQuadEncode(normalWS);" + newline +
            indent + "\thalf3 packedNormalWS = half3(PackFloat2To888(saturate(octNormalWS * 0.5 + 0.5)));" + newline +
            indent + "\t#else" + newline +
            indent + "\thalf3 packedNormalWS = half3(normalWS);" + newline +
            indent + "\t#endif" + newline +
            indent + "\toutGBuffer0 = half4(poiFragData.baseColor, 0.0);" + newline +
            indent + "\toutGBuffer1 = half4(0.0, 0.0, 0.0, 1.0);" + newline +
            indent + "\toutGBuffer2 = half4(packedNormalWS, 0.5);");

        result = source.Substring(0, passEnd) + newline + newline + gbufferPass + source.Substring(passEnd);
        return true;
    }

    /// <summary>Removes the SSGI GBuffer pass added by <see cref="TryInject"/>.</summary>
    public static bool TryRemove(string source, out string result)
    {
        result = source;
        int tagIndex = source.IndexOf(GBufferTag, System.StringComparison.Ordinal);
        if (tagIndex < 0 || !TryGetPassBounds(source, tagIndex, out int passStart, out int passEnd))
            return false;

        // Also drop the blank lines the injection put in front of the pass.
        int cut = passStart;
        while (cut > 0 && (source[cut - 1] == '\n' || source[cut - 1] == '\r'))
            cut--;
        result = source.Substring(0, cut) + source.Substring(passEnd);
        return true;
    }

    // From the line "Pass" preceding the tag to the end of the line closing the pass block (after ENDHLSL).
    private static bool TryGetPassBounds(string source, int tagIndex, out int passStart, out int passEnd)
    {
        passStart = -1;
        passEnd = -1;

        foreach (Match match in PassLine.Matches(source))
        {
            if (match.Index > tagIndex)
                break;
            passStart = match.Index;
        }
        if (passStart < 0)
            return false;

        int end = source.IndexOf("ENDHLSL", tagIndex, System.StringComparison.Ordinal);
        if (end < 0)
            return false;
        Match close = PassCloseLine.Match(source, end);
        if (!close.Success)
            return false;

        passEnd = close.Index + close.Length;
        return passEnd > passStart;
    }

    private static string ReadText(string path, out bool hadBom)
    {
        byte[] bytes = File.ReadAllBytes(path);
        hadBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        return new UTF8Encoding(false).GetString(bytes, hadBom ? 3 : 0, bytes.Length - (hadBom ? 3 : 0));
    }

    private static void WriteText(string path, string text, bool withBom)
    {
        File.WriteAllText(path, text, new UTF8Encoding(withBom));
    }
}
