using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Bakes the avatar's rendered appearance into the imposter atlas. The avatar is rendered with
/// its real materials from a ring of orthographic views, twice per view (black and white
/// background) so coverage comes from difference matting — robust against arbitrary shaders
/// that write no meaningful alpha. Scene lighting is overridden with flat white ambient during
/// capture so the atlas stores an unlit/albedo-like response; the runtime imposter shader
/// re-lights it from the world's ambient probe and main light.
///
/// Each atlas texel is projected into the best-facing views, occlusion-tested with a raycast
/// against a temporary collider of the decimated mesh, sampled, dilated, mipped, and finally
/// compressed to BC1 (desktop) and ASTC 6x6 (mobile) payloads.
/// </summary>
public static class BasisImposterAtlasBaker
{
    private const int DilatePasses = 16;
    private const float MinCoverage = 0.4f;
    private const float FallbackCoverage = 0.1f;
    private const int OcclusionLayer = 2; // Ignore Raycast: invisible to default queries, targetable by mask

    private struct CaptureView
    {
        public Vector3 DirectionWorld;
        public Matrix4x4 WorldToPixel;
        public Color32[] Pixels; // rgb = color rendered on black, a = coverage
    }

    public static BasisImposterPayload.ImposterTexture[] Bake(Transform root, Mesh decimatedMesh,
        Vector3[] positions, Vector3[] normals, Vector2[] uv, int[] indices, int atlasSize, int captureSize)
    {
        Bounds rootBounds = new Bounds(positions[0], Vector3.zero);
        for (int i = 1; i < positions.Length; i++)
        {
            rootBounds.Encapsulate(positions[i]);
        }
        Matrix4x4 rootToWorld = root.localToWorldMatrix;
        Vector3 centerWorld = rootToWorld.MultiplyPoint3x4(rootBounds.center);
        float radius = 0.001f;
        Vector3 extents = rootBounds.extents;
        for (int corner = 0; corner < 8; corner++)
        {
            Vector3 cornerLocal = rootBounds.center + Vector3.Scale(extents, new Vector3(
                (corner & 1) == 0 ? -1f : 1f,
                (corner & 2) == 0 ? -1f : 1f,
                (corner & 4) == 0 ? -1f : 1f));
            radius = Mathf.Max(radius, (rootToWorld.MultiplyPoint3x4(cornerLocal) - centerWorld).magnitude);
        }

        Vector3[] localDirections = BuildViewDirections();
        Quaternion rootRotation = root.rotation;

        LightingScope lighting = LightingScope.Push();
        GameObject cameraObject = null;
        GameObject colliderObject = null;
        RenderTexture renderTexture = null;
        Texture2D readback = null;
        try
        {
            cameraObject = new GameObject("ImposterBakeCamera") { hideFlags = HideFlags.HideAndDontSave };
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = radius;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = radius * 4f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.cullingMask = ~0;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.useOcclusionCulling = false;

            renderTexture = RenderTexture.GetTemporary(captureSize, captureSize, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = renderTexture;
            readback = new Texture2D(captureSize, captureSize, TextureFormat.RGBA32, false, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
            };

            CaptureView[] views = new CaptureView[localDirections.Length];
            for (int v = 0; v < localDirections.Length; v++)
            {
                Vector3 directionWorld = (rootRotation * localDirections[v]).normalized;
                Vector3 up = Mathf.Abs(Vector3.Dot(directionWorld, Vector3.up)) > 0.95f ? rootRotation * Vector3.forward : Vector3.up;
                camera.transform.SetPositionAndRotation(centerWorld - directionWorld * (radius * 2f), Quaternion.LookRotation(directionWorld, up));

                Color32[] onBlack = RenderAndRead(camera, readback, captureSize, new Color(0f, 0f, 0f, 0f));
                Color32[] onWhite = RenderAndRead(camera, readback, captureSize, new Color(1f, 1f, 1f, 0f));
                for (int p = 0; p < onBlack.Length; p++)
                {
                    int difference = (Mathf.Abs(onWhite[p].r - onBlack[p].r) + Mathf.Abs(onWhite[p].g - onBlack[p].g) + Mathf.Abs(onWhite[p].b - onBlack[p].b)) / 3;
                    onBlack[p].a = (byte)(255 - difference);
                }

                // world → clip → pixel, matching ReadPixels' lower-left origin.
                Matrix4x4 clip = camera.projectionMatrix * camera.worldToCameraMatrix;
                Matrix4x4 ndcToPixel = Matrix4x4.TRS(new Vector3(captureSize * 0.5f, captureSize * 0.5f, 0f), Quaternion.identity, new Vector3(captureSize * 0.5f, captureSize * 0.5f, 1f));
                views[v] = new CaptureView
                {
                    DirectionWorld = directionWorld,
                    WorldToPixel = ndcToPixel * clip,
                    Pixels = onBlack,
                };
            }
            camera.targetTexture = null;

            colliderObject = new GameObject("ImposterBakeCollider") { hideFlags = HideFlags.HideAndDontSave, layer = OcclusionLayer };
            colliderObject.transform.SetPositionAndRotation(root.position, root.rotation);
            colliderObject.transform.localScale = root.lossyScale;
            MeshCollider collider = colliderObject.AddComponent<MeshCollider>();
            collider.sharedMesh = decimatedMesh;
            Physics.SyncTransforms();

            Color32[] atlas = ProjectAtlas(views, rootToWorld, rootRotation, positions, normals, uv, indices, atlasSize, radius);
            return CompressAtlas(atlas, atlasSize);
        }
        finally
        {
            if (renderTexture != null)
            {
                RenderTexture.ReleaseTemporary(renderTexture);
            }
            if (readback != null)
            {
                Object.DestroyImmediate(readback);
            }
            if (cameraObject != null)
            {
                Object.DestroyImmediate(cameraObject);
            }
            if (colliderObject != null)
            {
                Object.DestroyImmediate(colliderObject);
            }
            lighting.Pop();
        }
    }

    private static Vector3[] BuildViewDirections()
    {
        List<Vector3> directions = new List<Vector3>(14);
        for (int yaw = 0; yaw < 360; yaw += 45)
        {
            float radians = yaw * Mathf.Deg2Rad;
            directions.Add(new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians)));
        }
        for (int yaw = 0; yaw < 360; yaw += 90)
        {
            float radians = yaw * Mathf.Deg2Rad;
            directions.Add((new Vector3(Mathf.Sin(radians), 0.84f, Mathf.Cos(radians))).normalized);
        }
        directions.Add(Vector3.up);
        directions.Add(Vector3.down);
        return directions.ToArray();
    }

    private static Color32[] RenderAndRead(Camera camera, Texture2D readback, int captureSize, Color background)
    {
        camera.backgroundColor = background;
        camera.Render();
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = camera.targetTexture;
        readback.ReadPixels(new Rect(0, 0, captureSize, captureSize), 0, 0, false);
        RenderTexture.active = previous;
        return readback.GetPixels32();
    }

    private static Color32[] ProjectAtlas(CaptureView[] views, Matrix4x4 rootToWorld, Quaternion rootRotation,
        Vector3[] positions, Vector3[] normals, Vector2[] uv, int[] indices, int atlasSize, float radius)
    {
        int texelCount = atlasSize * atlasSize;
        Color32[] atlas = new Color32[texelCount];
        bool[] written = new bool[texelCount];
        int captureSize = (int)Mathf.Sqrt(views[0].Pixels.Length);
        float rayBias = Mathf.Max(0.004f, radius * 0.01f);
        int layerMask = 1 << OcclusionLayer;
        int[] candidateOrder = new int[views.Length];
        float[] candidateScore = new float[views.Length];

        for (int t = 0; t + 2 < indices.Length; t += 3)
        {
            int i0 = indices[t], i1 = indices[t + 1], i2 = indices[t + 2];
            Vector2 uv0 = uv[i0] * atlasSize, uv1 = uv[i1] * atlasSize, uv2 = uv[i2] * atlasSize;

            float minX = Mathf.Min(uv0.x, Mathf.Min(uv1.x, uv2.x)) - 1f;
            float maxX = Mathf.Max(uv0.x, Mathf.Max(uv1.x, uv2.x)) + 1f;
            float minY = Mathf.Min(uv0.y, Mathf.Min(uv1.y, uv2.y)) - 1f;
            float maxY = Mathf.Max(uv0.y, Mathf.Max(uv1.y, uv2.y)) + 1f;
            int startX = Mathf.Clamp(Mathf.FloorToInt(minX), 0, atlasSize - 1);
            int endX = Mathf.Clamp(Mathf.CeilToInt(maxX), 0, atlasSize - 1);
            int startY = Mathf.Clamp(Mathf.FloorToInt(minY), 0, atlasSize - 1);
            int endY = Mathf.Clamp(Mathf.CeilToInt(maxY), 0, atlasSize - 1);

            Vector2 edge0 = uv1 - uv0;
            Vector2 edge1 = uv2 - uv0;
            float denominator = edge0.x * edge1.y - edge0.y * edge1.x;
            if (Mathf.Abs(denominator) < 1e-8f)
            {
                continue;
            }
            float inverseDenominator = 1f / denominator;

            for (int y = startY; y <= endY; y++)
            {
                for (int x = startX; x <= endX; x++)
                {
                    Vector2 point = new Vector2(x + 0.5f, y + 0.5f) - uv0;
                    float baryB = (point.x * edge1.y - point.y * edge1.x) * inverseDenominator;
                    float baryC = (edge0.x * point.y - edge0.y * point.x) * inverseDenominator;
                    float baryA = 1f - baryB - baryC;
                    const float slack = -0.08f;
                    if (baryA < slack || baryB < slack || baryC < slack)
                    {
                        continue;
                    }

                    Vector3 positionRoot = positions[i0] * baryA + positions[i1] * baryB + positions[i2] * baryC;
                    Vector3 normalRoot = (normals[i0] * baryA + normals[i1] * baryB + normals[i2] * baryC);
                    Vector3 positionWorld = rootToWorld.MultiplyPoint3x4(positionRoot);
                    Vector3 normalWorld = (rootRotation * normalRoot).normalized;

                    int candidateCount = 0;
                    for (int v = 0; v < views.Length; v++)
                    {
                        float score = Vector3.Dot(normalWorld, -views[v].DirectionWorld);
                        if (score > 0.05f)
                        {
                            candidateOrder[candidateCount] = v;
                            candidateScore[candidateCount] = score;
                            candidateCount++;
                        }
                    }
                    // insertion sort by score, best first (candidate counts are tiny)
                    for (int a = 1; a < candidateCount; a++)
                    {
                        int order = candidateOrder[a];
                        float score = candidateScore[a];
                        int b = a - 1;
                        while (b >= 0 && candidateScore[b] < score)
                        {
                            candidateOrder[b + 1] = candidateOrder[b];
                            candidateScore[b + 1] = candidateScore[b];
                            b--;
                        }
                        candidateOrder[b + 1] = order;
                        candidateScore[b + 1] = score;
                    }

                    int texelIndex = y * atlasSize + x;
                    bool sampled = false;
                    Color32 fallbackColor = default;
                    bool hasFallback = false;

                    int consider = Mathf.Min(candidateCount, 5);
                    for (int c = 0; c < consider && !sampled; c++)
                    {
                        CaptureView view = views[candidateOrder[c]];
                        if (!TrySampleView(view, positionWorld, captureSize, out Color32 color, out float coverage))
                        {
                            continue;
                        }
                        if (!hasFallback && coverage >= FallbackCoverage)
                        {
                            fallbackColor = color;
                            hasFallback = true;
                        }
                        if (coverage < MinCoverage)
                        {
                            continue;
                        }
                        Vector3 towardCamera = -view.DirectionWorld;
                        Vector3 origin = positionWorld + normalWorld * rayBias + towardCamera * rayBias;
                        if (Physics.Raycast(origin, towardCamera, radius * 3f, layerMask))
                        {
                            continue;
                        }
                        atlas[texelIndex] = new Color32(color.r, color.g, color.b, 255);
                        written[texelIndex] = true;
                        sampled = true;
                    }

                    if (!sampled && hasFallback)
                    {
                        atlas[texelIndex] = new Color32(fallbackColor.r, fallbackColor.g, fallbackColor.b, 255);
                        written[texelIndex] = true;
                    }
                }
            }
        }

        Dilate(atlas, written, atlasSize);
        return atlas;
    }

    private static bool TrySampleView(in CaptureView view, Vector3 positionWorld, int captureSize, out Color32 color, out float coverage)
    {
        Vector3 pixel = view.WorldToPixel.MultiplyPoint(positionWorld);
        int x = (int)pixel.x;
        int y = (int)pixel.y;
        if (x < 0 || y < 0 || x >= captureSize || y >= captureSize)
        {
            color = default;
            coverage = 0f;
            return false;
        }
        Color32 sample = view.Pixels[y * captureSize + x];
        color = sample;
        coverage = sample.a * (1f / 255f);
        return true;
    }

    private static void Dilate(Color32[] atlas, bool[] written, int atlasSize)
    {
        bool[] current = written;
        for (int pass = 0; pass < DilatePasses; pass++)
        {
            bool[] next = (bool[])current.Clone();
            bool any = false;
            for (int y = 0; y < atlasSize; y++)
            {
                for (int x = 0; x < atlasSize; x++)
                {
                    int index = y * atlasSize + x;
                    if (current[index])
                    {
                        continue;
                    }
                    int r = 0, g = 0, b = 0, count = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int ny = y + dy;
                        if (ny < 0 || ny >= atlasSize)
                        {
                            continue;
                        }
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx;
                            if (nx < 0 || nx >= atlasSize)
                            {
                                continue;
                            }
                            int neighbor = ny * atlasSize + nx;
                            if (!current[neighbor])
                            {
                                continue;
                            }
                            r += atlas[neighbor].r;
                            g += atlas[neighbor].g;
                            b += atlas[neighbor].b;
                            count++;
                        }
                    }
                    if (count > 0)
                    {
                        atlas[index] = new Color32((byte)(r / count), (byte)(g / count), (byte)(b / count), 255);
                        next[index] = true;
                        any = true;
                    }
                }
            }
            current = next;
            if (!any)
            {
                break;
            }
        }

        long totalR = 0, totalG = 0, totalB = 0, totalCount = 0;
        for (int i = 0; i < atlas.Length; i++)
        {
            if (current[i])
            {
                totalR += atlas[i].r;
                totalG += atlas[i].g;
                totalB += atlas[i].b;
                totalCount++;
            }
        }
        Color32 average = totalCount > 0
            ? new Color32((byte)(totalR / totalCount), (byte)(totalG / totalCount), (byte)(totalB / totalCount), 255)
            : new Color32(128, 128, 128, 255);
        for (int i = 0; i < atlas.Length; i++)
        {
            if (!current[i])
            {
                atlas[i] = average;
            }
        }
    }

    private static BasisImposterPayload.ImposterTexture[] CompressAtlas(Color32[] atlas, int atlasSize)
    {
        Texture2D source = new Texture2D(atlasSize, atlasSize, TextureFormat.RGBA32, true, false)
        {
            hideFlags = HideFlags.HideAndDontSave,
        };
        try
        {
            source.SetPixels32(atlas);
            source.Apply(true, false);

            List<BasisImposterPayload.ImposterTexture> textures = new List<BasisImposterPayload.ImposterTexture>(2);
            AppendCompressed(textures, source, TextureFormat.DXT1, BasisImposterPayload.ImposterTextureFormat.BC1);
            AppendCompressed(textures, source, TextureFormat.ASTC_6x6, BasisImposterPayload.ImposterTextureFormat.ASTC6x6);
            return textures.ToArray();
        }
        finally
        {
            Object.DestroyImmediate(source);
        }
    }

    private static void AppendCompressed(List<BasisImposterPayload.ImposterTexture> textures, Texture2D source,
        TextureFormat format, BasisImposterPayload.ImposterTextureFormat payloadFormat)
    {
        Texture2D copy = Object.Instantiate(source);
        copy.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            EditorUtility.CompressTexture(copy, format, TextureCompressionQuality.Normal);
            if (copy.format != format)
            {
                Debug.LogWarning($"Imposter atlas compression to {format} was not applied on this platform; skipping that payload.");
                return;
            }
            textures.Add(new BasisImposterPayload.ImposterTexture
            {
                Format = payloadFormat,
                Width = (ushort)copy.width,
                Height = (ushort)copy.height,
                MipCount = (byte)copy.mipmapCount,
                Data = copy.GetRawTextureData(),
            });
        }
        finally
        {
            Object.DestroyImmediate(copy);
        }
    }

    /// <summary>
    /// Overrides scene lighting for a neutral capture (flat white ambient, no lights, no fog,
    /// no reflections) and restores everything afterwards.
    /// </summary>
    private struct LightingScope
    {
        private UnityEngine.Rendering.AmbientMode _ambientMode;
        private Color _ambientLight;
        private float _reflectionIntensity;
        private bool _fog;
        private Light[] _disabledLights;

        public static LightingScope Push()
        {
            LightingScope scope = new LightingScope
            {
                _ambientMode = RenderSettings.ambientMode,
                _ambientLight = RenderSettings.ambientLight,
                _reflectionIntensity = RenderSettings.reflectionIntensity,
                _fog = RenderSettings.fog,
            };

            Light[] lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            List<Light> disabled = new List<Light>(lights.Length);
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i].enabled)
                {
                    lights[i].enabled = false;
                    disabled.Add(lights[i]);
                }
            }
            scope._disabledLights = disabled.ToArray();

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = Color.white;
            RenderSettings.reflectionIntensity = 0f;
            RenderSettings.fog = false;
            return scope;
        }

        public void Pop()
        {
            RenderSettings.ambientMode = _ambientMode;
            RenderSettings.ambientLight = _ambientLight;
            RenderSettings.reflectionIntensity = _reflectionIntensity;
            RenderSettings.fog = _fog;
            if (_disabledLights != null)
            {
                for (int i = 0; i < _disabledLights.Length; i++)
                {
                    if (_disabledLights[i] != null)
                    {
                        _disabledLights[i].enabled = true;
                    }
                }
            }
        }
    }
}
