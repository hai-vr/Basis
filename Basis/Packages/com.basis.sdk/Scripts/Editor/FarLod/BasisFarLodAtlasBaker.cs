using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Bakes the avatar's rendered appearance into the far LOD atlas. The avatar is rendered with
/// its real materials from a ring of orthographic body views plus tightly-framed close-up
/// passes for hands, feet and head (small parts are only a handful of pixels in a whole-body
/// frame — the close-ups are what keep them legible). Every view is captured twice (black and
/// white background) so coverage comes from difference matting — robust against arbitrary
/// shaders that write no meaningful alpha — and colors are un-premultiplied by that coverage
/// so silhouette edges don't keep background darkening.
///
/// Scene lighting is overridden with flat white ambient during capture so the atlas stores an
/// unlit/albedo-like response; the runtime far LOD shader re-lights it from the world's
/// ambient probe and main light.
///
/// Each atlas texel is projected into the best-facing valid views (close-ups outrank body
/// views), occlusion-tested with a raycast against a temporary collider of the decimated mesh,
/// sampled bilinearly with coverage weighting, dilated, mipped, and finally compressed to BC1
/// (desktop) and ASTC 6x6 (mobile) payloads.
/// </summary>
public static class BasisFarLodAtlasBaker
{
    private const int DilatePasses = 16;
    private const float MinCoverage = 0.4f;
    private const float FallbackCoverage = 0.1f;
    private const int OcclusionLayer = 2; // Ignore Raycast: invisible to default queries, targetable by mask
    private const int RegionCaptureSize = 512;
    private const float RegionScoreBias = 0.75f;

    /// <summary>A body part that deserves its own close-up capture set (bounds in root space).</summary>
    public struct RegionOfInterest
    {
        public string Name;
        public Bounds RootBounds;
    }

    private struct CaptureView
    {
        public Vector3 DirectionWorld;
        public Matrix4x4 WorldToPixel;
        public Color32[] Pixels; // rgb = un-premultiplied color, a = coverage
        public int Size;
        public bool IsRegion;
        public Bounds ValidBoundsRoot; // region views only serve texels inside this
    }

    public static BasisFarLodPayload.FarLodTexture[] Bake(Transform root, Mesh decimatedMesh,
        Vector3[] positions, Vector3[] normals, Vector2[] uv, int[] indices, int atlasSize, int captureSize,
        RegionOfInterest[] regions = null)
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

        Quaternion rootRotation = root.rotation;
        LightingScope lighting = LightingScope.Push();
        GameObject cameraObject = null;
        GameObject colliderObject = null;
        RenderTexture bodyTexture = null;
        RenderTexture regionTexture = null;
        Texture2D bodyReadback = null;
        Texture2D regionReadback = null;
        try
        {
            cameraObject = new GameObject("FarLodBakeCamera") { hideFlags = HideFlags.HideAndDontSave };
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.cullingMask = ~0;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.useOcclusionCulling = false;

            bodyTexture = RenderTexture.GetTemporary(captureSize, captureSize, 24, RenderTextureFormat.ARGB32);
            regionTexture = RenderTexture.GetTemporary(RegionCaptureSize, RegionCaptureSize, 24, RenderTextureFormat.ARGB32);
            bodyReadback = NewReadback(captureSize);
            regionReadback = NewReadback(RegionCaptureSize);

            List<CaptureView> views = new List<CaptureView>(64);

            // Whole-body ring: equator, upper ring, lower ring (palms face down in T-pose —
            // without under-views they only ever see the single bottom capture), poles.
            Vector3[] bodyDirections = BuildBodyViewDirections();
            for (int v = 0; v < bodyDirections.Length; v++)
            {
                Vector3 directionWorld = (rootRotation * bodyDirections[v]).normalized;
                views.Add(CaptureOne(camera, bodyTexture, bodyReadback, captureSize,
                    centerWorld, directionWorld, rootRotation, radius, isRegion: false, default));
            }

            // Close-up passes.
            if (regions != null)
            {
                Vector3[] regionDirections =
                {
                    Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back,
                };
                for (int r = 0; r < regions.Length; r++)
                {
                    Bounds region = regions[r].RootBounds;
                    Vector3 regionCenterWorld = rootToWorld.MultiplyPoint3x4(region.center);
                    float regionRadius = Mathf.Max(rootToWorld.MultiplyVector(region.extents).magnitude, 0.01f);
                    Bounds valid = region;
                    valid.Expand(region.size.magnitude * 0.1f + 0.005f);
                    for (int d = 0; d < regionDirections.Length; d++)
                    {
                        Vector3 directionWorld = (rootRotation * regionDirections[d]).normalized;
                        views.Add(CaptureOne(camera, regionTexture, regionReadback, RegionCaptureSize,
                            regionCenterWorld, directionWorld, rootRotation, regionRadius, isRegion: true, valid));
                    }
                }
            }
            camera.targetTexture = null;

            // Sanity: if every capture came back empty, the camera rendered nothing (edit-mode
            // SRP issue or the avatar's renderers are off) — the atlas would be flat gray.
            long coveredPixels = 0;
            for (int v = 0; v < views.Count; v++)
            {
                Color32[] pixels = views[v].Pixels;
                for (int p = 0; p < pixels.Length; p += 7)
                {
                    if (pixels[p].a > 128)
                    {
                        coveredPixels++;
                    }
                }
            }
            if (coveredPixels == 0)
            {
                Debug.LogWarning("[FarLod] Every view capture was empty — Camera.Render produced no avatar pixels. The atlas will be flat. Check that the avatar's renderers are enabled and visible.");
            }

            colliderObject = new GameObject("FarLodBakeCollider") { hideFlags = HideFlags.HideAndDontSave, layer = OcclusionLayer };
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
            if (bodyTexture != null)
            {
                RenderTexture.ReleaseTemporary(bodyTexture);
            }
            if (regionTexture != null)
            {
                RenderTexture.ReleaseTemporary(regionTexture);
            }
            if (bodyReadback != null)
            {
                Object.DestroyImmediate(bodyReadback);
            }
            if (regionReadback != null)
            {
                Object.DestroyImmediate(regionReadback);
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

    private static Texture2D NewReadback(int size)
    {
        return new Texture2D(size, size, TextureFormat.RGBA32, false, false)
        {
            hideFlags = HideFlags.HideAndDontSave,
        };
    }

    private static Vector3[] BuildBodyViewDirections()
    {
        List<Vector3> directions = new List<Vector3>(18);
        for (int yaw = 0; yaw < 360; yaw += 45)
        {
            float radians = yaw * Mathf.Deg2Rad;
            directions.Add(new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians)));
        }
        for (int yaw = 0; yaw < 360; yaw += 90)
        {
            float radians = yaw * Mathf.Deg2Rad;
            directions.Add(new Vector3(Mathf.Sin(radians), 0.84f, Mathf.Cos(radians)).normalized);
        }
        for (int yaw = 45; yaw < 360; yaw += 90)
        {
            float radians = yaw * Mathf.Deg2Rad;
            directions.Add(new Vector3(Mathf.Sin(radians), -0.84f, Mathf.Cos(radians)).normalized);
        }
        directions.Add(Vector3.up);
        directions.Add(Vector3.down);
        return directions.ToArray();
    }

    private static CaptureView CaptureOne(Camera camera, RenderTexture target, Texture2D readback, int size,
        Vector3 centerWorld, Vector3 directionWorld, Quaternion rootRotation, float frameRadius, bool isRegion, Bounds validBoundsRoot)
    {
        Vector3 up = Mathf.Abs(Vector3.Dot(directionWorld, Vector3.up)) > 0.95f ? rootRotation * Vector3.forward : Vector3.up;
        camera.targetTexture = target;
        camera.orthographicSize = frameRadius * (isRegion ? 1.1f : 1f);
        camera.nearClipPlane = 0.01f;
        camera.farClipPlane = frameRadius * 4f;
        camera.transform.SetPositionAndRotation(centerWorld - directionWorld * (frameRadius * 2f), Quaternion.LookRotation(directionWorld, up));

        Color32[] onBlack = RenderAndRead(camera, readback, size, new Color(0f, 0f, 0f, 0f));
        Color32[] onWhite = RenderAndRead(camera, readback, size, new Color(1f, 1f, 1f, 0f));
        for (int p = 0; p < onBlack.Length; p++)
        {
            int difference = (Mathf.Abs(onWhite[p].r - onBlack[p].r) + Mathf.Abs(onWhite[p].g - onBlack[p].g) + Mathf.Abs(onWhite[p].b - onBlack[p].b)) / 3;
            int coverage = 255 - difference;
            // Un-premultiply: the color rendered on black is truth × coverage — divide the
            // background attenuation back out so silhouette edges don't bake in dark fringes.
            if (coverage > 6 && coverage < 255)
            {
                float scale = 255f / coverage;
                onBlack[p].r = (byte)Mathf.Min(255f, onBlack[p].r * scale);
                onBlack[p].g = (byte)Mathf.Min(255f, onBlack[p].g * scale);
                onBlack[p].b = (byte)Mathf.Min(255f, onBlack[p].b * scale);
            }
            onBlack[p].a = (byte)coverage;
        }

        Matrix4x4 clip = camera.projectionMatrix * camera.worldToCameraMatrix;
        Matrix4x4 ndcToPixel = Matrix4x4.TRS(new Vector3(size * 0.5f, size * 0.5f, 0f), Quaternion.identity, new Vector3(size * 0.5f, size * 0.5f, 1f));
        return new CaptureView
        {
            DirectionWorld = directionWorld,
            WorldToPixel = ndcToPixel * clip,
            Pixels = onBlack,
            Size = size,
            IsRegion = isRegion,
            ValidBoundsRoot = validBoundsRoot,
        };
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

    private static Color32[] ProjectAtlas(List<CaptureView> views, Matrix4x4 rootToWorld, Quaternion rootRotation,
        Vector3[] positions, Vector3[] normals, Vector2[] uv, int[] indices, int atlasSize, float radius)
    {
        int texelCount = atlasSize * atlasSize;
        Color32[] atlas = new Color32[texelCount];
        bool[] written = new bool[texelCount];
        float rayBias = Mathf.Max(0.004f, radius * 0.01f);
        int layerMask = 1 << OcclusionLayer;
        int viewCount = views.Count;
        CaptureView[] viewArray = views.ToArray();
        int[] candidateOrder = new int[viewCount];
        float[] candidateScore = new float[viewCount];

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
                    Vector3 normalRoot = normals[i0] * baryA + normals[i1] * baryB + normals[i2] * baryC;
                    Vector3 positionWorld = rootToWorld.MultiplyPoint3x4(positionRoot);
                    Vector3 normalWorld = (rootRotation * normalRoot).normalized;

                    int candidateCount = 0;
                    for (int v = 0; v < viewCount; v++)
                    {
                        ref CaptureView view = ref viewArray[v];
                        if (view.IsRegion && !view.ValidBoundsRoot.Contains(positionRoot))
                        {
                            continue;
                        }
                        float score = Vector3.Dot(normalWorld, -view.DirectionWorld);
                        if (score > 0.05f)
                        {
                            candidateOrder[candidateCount] = v;
                            candidateScore[candidateCount] = score + (view.IsRegion ? RegionScoreBias : 0f);
                            candidateCount++;
                        }
                    }
                    // insertion sort by score, best first (candidate counts are small)
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

                    int consider = Mathf.Min(candidateCount, 6);
                    for (int c = 0; c < consider && !sampled; c++)
                    {
                        ref CaptureView view = ref viewArray[candidateOrder[c]];
                        if (!TrySampleView(in view, positionWorld, out Color32 color, out float coverage))
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

    /// <summary>Bilinear, coverage-weighted sample — background texels are weighted out.</summary>
    private static bool TrySampleView(in CaptureView view, Vector3 positionWorld, out Color32 color, out float coverage)
    {
        Vector3 pixel = view.WorldToPixel.MultiplyPoint(positionWorld);
        float fx = pixel.x - 0.5f;
        float fy = pixel.y - 0.5f;
        int x0 = Mathf.FloorToInt(fx);
        int y0 = Mathf.FloorToInt(fy);
        if (x0 < -1 || y0 < -1 || x0 >= view.Size || y0 >= view.Size)
        {
            color = default;
            coverage = 0f;
            return false;
        }
        float tx = fx - x0;
        float ty = fy - y0;

        float r = 0f, g = 0f, b = 0f, weightedCoverage = 0f, totalWeight = 0f;
        for (int dy = 0; dy <= 1; dy++)
        {
            int sy = y0 + dy;
            if (sy < 0 || sy >= view.Size)
            {
                continue;
            }
            float wy = dy == 0 ? 1f - ty : ty;
            for (int dx = 0; dx <= 1; dx++)
            {
                int sx = x0 + dx;
                if (sx < 0 || sx >= view.Size)
                {
                    continue;
                }
                float weight = wy * (dx == 0 ? 1f - tx : tx);
                if (weight <= 0f)
                {
                    continue;
                }
                Color32 sample = view.Pixels[sy * view.Size + sx];
                float sampleCoverage = sample.a * (1f / 255f);
                float colorWeight = weight * sampleCoverage;
                r += sample.r * colorWeight;
                g += sample.g * colorWeight;
                b += sample.b * colorWeight;
                weightedCoverage += sampleCoverage * weight;
                totalWeight += weight;
            }
        }

        if (totalWeight <= 0f || weightedCoverage <= 1e-4f)
        {
            color = default;
            coverage = 0f;
            return false;
        }
        float inverseColorWeight = 1f / Mathf.Max(weightedCoverage, 1e-4f);
        color = new Color32(
            (byte)Mathf.Clamp(Mathf.RoundToInt(r * inverseColorWeight), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(g * inverseColorWeight), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(b * inverseColorWeight), 0, 255),
            255);
        coverage = weightedCoverage / totalWeight;
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

    private static BasisFarLodPayload.FarLodTexture[] CompressAtlas(Color32[] atlas, int atlasSize)
    {
        Texture2D source = new Texture2D(atlasSize, atlasSize, TextureFormat.RGBA32, true, false)
        {
            hideFlags = HideFlags.HideAndDontSave,
        };
        try
        {
            source.SetPixels32(atlas);
            source.Apply(true, false);

            List<BasisFarLodPayload.FarLodTexture> textures = new List<BasisFarLodPayload.FarLodTexture>(2);
            AppendCompressed(textures, source, TextureFormat.DXT1, BasisFarLodPayload.FarLodTextureFormat.BC1);
            AppendCompressed(textures, source, TextureFormat.ASTC_6x6, BasisFarLodPayload.FarLodTextureFormat.ASTC6x6);
            return textures.ToArray();
        }
        finally
        {
            Object.DestroyImmediate(source);
        }
    }

    private static void AppendCompressed(List<BasisFarLodPayload.FarLodTexture> textures, Texture2D source,
        TextureFormat format, BasisFarLodPayload.FarLodTextureFormat payloadFormat)
    {
        Texture2D copy = Object.Instantiate(source);
        copy.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            EditorUtility.CompressTexture(copy, format, TextureCompressionQuality.Normal);
            if (copy.format != format)
            {
                Debug.LogWarning($"Far LOD atlas compression to {format} was not applied on this platform; skipping that payload.");
                return;
            }
            textures.Add(new BasisFarLodPayload.FarLodTexture
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
