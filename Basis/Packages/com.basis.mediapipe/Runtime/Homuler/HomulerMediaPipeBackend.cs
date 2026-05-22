#if BASIS_MEDIAPIPE
using System;
using System.IO;
using System.Threading;
using Mediapipe;
using Mediapipe.Tasks.Core;
using Mediapipe.Tasks.Vision.Core;
using Mediapipe.Tasks.Vision.FaceLandmarker;
using Mediapipe.Tasks.Vision.HandLandmarker;
using Mediapipe.Tasks.Vision.PoseLandmarker;
using Unity.Collections;
using UnityEngine;

namespace Basis.MediaPipe.Homuler
{
    /// <summary>
    /// MediaPipe Unity Plugin (homuler) backend. Compiled only when the plugin is present
    /// (BASIS_MEDIAPIPE). Runs FaceLandmarker + HandLandmarker + PoseLandmarker in VIDEO mode
    /// on a background worker thread; the main thread only does GetPixels32 (Unity API) and
    /// signals the worker. A busy flag drops frames instead of racing the shared buffer.
    /// </summary>
    public sealed class HomulerMediaPipeBackend : IBasisMediaPipeBackend
    {
        private const string ModelSubdir = "MediaPipe";
        private const string FaceModel = "face_landmarker.task";
        private const string HandModel = "hand_landmarker.task";
        private const string PoseModel = "pose_landmarker_lite.task";

        public bool IsAvailable { get; private set; }
        public string BackendName => "homuler MediaPipe Unity Plugin";
        private bool _swapHands;

        private FaceLandmarker _face;
        private HandLandmarker _hand;
        private PoseLandmarker _pose;

        private readonly object _resultLock = new object();
        private BasisMediaPipeResult _latest;
        private bool _hasLatest;
        private long _lastTimestamp;

        private Thread _worker;
        private AutoResetEvent _signal;
        private volatile bool _running;
        private volatile bool _busy;

        private Color32[] _pixels;
        private byte[] _rgba;
        private NativeArray<byte> _native;
        private int _w;
        private int _h;
        private long _ts;
        private bool _mirror;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register() =>
            BasisMediaPipeBackendRegistry.Register(() => new HomulerMediaPipeBackend());

        public void Initialize(BasisMediaPipeConfig config)
        {
            _mirror = config.MirrorHorizontally;
            _swapHands = config.SwapHands;
            try
            {
                if (config.EnableFace) _face = CreateFaceLandmarker(ResolveModelPath(FaceModel));
                if (config.EnableHands) _hand = CreateHandLandmarker(ResolveModelPath(HandModel));
                if (config.EnablePose) _pose = CreatePoseLandmarker(ResolveModelPath(PoseModel));
                IsAvailable = _face != null || _hand != null || _pose != null;
            }
            catch (Exception e)
            {
                BasisDebug.LogError($"BasisMediaPipe(homuler): init failed: {e}");
                IsAvailable = false;
            }

            if (IsAvailable)
            {
                _signal = new AutoResetEvent(false);
                _running = true;
                _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "BasisMediaPipe" };
                _worker.Start();
            }
        }

        private static FaceLandmarker CreateFaceLandmarker(string modelPath)
        {
            BaseOptions baseOptions = new BaseOptions(modelAssetBuffer: File.ReadAllBytes(modelPath));
            FaceLandmarkerOptions options = new FaceLandmarkerOptions(
                baseOptions,
                runningMode: RunningMode.VIDEO,
                numFaces: 1,
                minFaceDetectionConfidence: 0.5f,
                minFacePresenceConfidence: 0.5f,
                minTrackingConfidence: 0.5f,
                outputFaceBlendshapes: true,
                outputFaceTransformationMatrixes: true);
            return FaceLandmarker.CreateFromOptions(options);
        }

        private static HandLandmarker CreateHandLandmarker(string modelPath)
        {
            BaseOptions baseOptions = new BaseOptions(modelAssetBuffer: File.ReadAllBytes(modelPath));
            HandLandmarkerOptions options = new HandLandmarkerOptions(
                baseOptions,
                runningMode: RunningMode.VIDEO,
                numHands: 2,
                minHandDetectionConfidence: 0.5f,
                minHandPresenceConfidence: 0.5f,
                minTrackingConfidence: 0.5f);
            return HandLandmarker.CreateFromOptions(options);
        }

        private static PoseLandmarker CreatePoseLandmarker(string modelPath)
        {
            BaseOptions baseOptions = new BaseOptions(modelAssetBuffer: File.ReadAllBytes(modelPath));
            PoseLandmarkerOptions options = new PoseLandmarkerOptions(
                baseOptions,
                runningMode: RunningMode.VIDEO,
                numPoses: 1,
                minPoseDetectionConfidence: 0.5f,
                minPosePresenceConfidence: 0.5f,
                minTrackingConfidence: 0.5f,
                outputSegmentationMasks: false);
            return PoseLandmarker.CreateFromOptions(options);
        }

        // Main thread: copy pixels (Unity API) and hand the frame to the worker.
        public void SubmitFrame(WebCamTexture frame, double timestampMs)
        {
            if (!IsAvailable || _busy || frame == null || frame.width <= 16) return;

            int w = frame.width;
            int h = frame.height;
            int len = w * h;

            if (_pixels == null || _pixels.Length != len)
            {
                _pixels = new Color32[len];
                _rgba = new byte[len * 4];
                if (_native.IsCreated) _native.Dispose();
                _native = new NativeArray<byte>(len * 4, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            }

            frame.GetPixels32(_pixels);

            long ts = (long)timestampMs;
            if (ts <= _lastTimestamp) ts = _lastTimestamp + 1;
            _lastTimestamp = ts;

            _w = w;
            _h = h;
            _ts = ts;
            _busy = true;
            _signal.Set();
        }

        private void WorkerLoop()
        {
            while (_running)
            {
                _signal.WaitOne();
                if (!_running) break;

                try
                {
                    ProcessFrame();
                }
                catch (Exception e)
                {
                    BasisDebug.LogError($"BasisMediaPipe(homuler): inference failed: {e}");
                }
                finally
                {
                    _busy = false;
                }
            }
        }

        private void ProcessFrame()
        {
            int w = _w;
            int h = _h;

            // WebCamTexture origin is bottom-left; MediaPipe expects top-left. Flip rows,
            // and mirror columns for a selfie-style camera.
            for (int y = 0; y < h; y++)
            {
                int srcRow = (h - 1 - y) * w;
                int dstRow = y * w;
                for (int x = 0; x < w; x++)
                {
                    int src = srcRow + (_mirror ? (w - 1 - x) : x);
                    int dst = (dstRow + x) * 4;
                    Color32 c = _pixels[src];
                    _rgba[dst + 0] = c.r;
                    _rgba[dst + 1] = c.g;
                    _rgba[dst + 2] = c.b;
                    _rgba[dst + 3] = c.a;
                }
            }

            _native.CopyFrom(_rgba);

            BasisMediaPipeResult result = new BasisMediaPipeResult { TimestampMs = _ts };
            // DetectForVideo consumes (disposes) the Image it is given, so build a fresh one per call.
            if (_face != null) ParseFace(_face.DetectForVideo(NewImage(w, h), _ts), ref result);
            if (_hand != null) ParseHands(_hand.DetectForVideo(NewImage(w, h), _ts), ref result);
            if (_pose != null) ParsePose(_pose.DetectForVideo(NewImage(w, h), _ts), ref result);
            if (result.HasFace) result.TongueOut = ComputeTongueOut(result.FaceLandmarks, w, h);

            lock (_resultLock)
            {
                _latest = result;
                _hasLatest = true;
            }
        }

        private static void ParseFace(FaceLandmarkerResult result, ref BasisMediaPipeResult output)
        {
            if (result.faceLandmarks != null && result.faceLandmarks.Count > 0)
            {
                output.HasFace = true;
                var landmarks = result.faceLandmarks[0].landmarks;
                if (landmarks != null)
                {
                    output.FaceLandmarks = new Vector3[landmarks.Count];
                    for (int i = 0; i < landmarks.Count; i++)
                    {
                        output.FaceLandmarks[i] = new Vector3(landmarks[i].x, landmarks[i].y, landmarks[i].z);
                    }
                }
            }

            if (result.faceBlendshapes != null && result.faceBlendshapes.Count > 0)
            {
                var categories = result.faceBlendshapes[0].categories;
                if (categories != null)
                {
                    output.FaceBlendshapes = new float[(int)MediaPipeArkitBlendshape.Count];
                    for (int i = 0; i < categories.Count; i++)
                    {
                        int index = categories[i].index;
                        if (index >= 0 && index < output.FaceBlendshapes.Length)
                        {
                            output.FaceBlendshapes[index] = categories[i].score;
                        }
                    }
                }
            }

            if (result.facialTransformationMatrixes != null && result.facialTransformationMatrixes.Count > 0)
            {
                output.FaceTransform = result.facialTransformationMatrixes[0];
            }
        }

        private void ParseHands(HandLandmarkerResult result, ref BasisMediaPipeResult output)
        {
            if (result.handLandmarks == null) return;

            for (int h = 0; h < result.handLandmarks.Count; h++)
            {
                var landmarks = result.handLandmarks[h].landmarks;
                if (landmarks == null) continue;

                Vector3[] arr = new Vector3[landmarks.Count];
                for (int i = 0; i < landmarks.Count; i++)
                {
                    arr[i] = new Vector3(landmarks[i].x, landmarks[i].y, landmarks[i].z);
                }

                bool isLeft = IsLeftHand(result, h);
                if (_swapHands) isLeft = !isLeft;
                if (isLeft) { output.LeftHandLandmarks = arr; output.HasLeftHand = true; }
                else { output.RightHandLandmarks = arr; output.HasRightHand = true; }
            }
        }

        private static bool IsLeftHand(HandLandmarkerResult result, int index)
        {
            if (result.handedness != null && index < result.handedness.Count)
            {
                var categories = result.handedness[index].categories;
                if (categories != null && categories.Count > 0)
                {
                    return categories[0].categoryName == "Left";
                }
            }
            return index == 0;
        }

        private static void ParsePose(PoseLandmarkerResult result, ref BasisMediaPipeResult output)
        {
            if (result.poseWorldLandmarks == null || result.poseWorldLandmarks.Count == 0) return;

            var landmarks = result.poseWorldLandmarks[0].landmarks;
            if (landmarks == null) return;

            output.HasPose = true;
            output.PoseWorldLandmarks = new Vector3[landmarks.Count];
            for (int i = 0; i < landmarks.Count; i++)
            {
                output.PoseWorldLandmarks[i] = new Vector3(landmarks[i].x, landmarks[i].y, landmarks[i].z);
            }
        }

        // Tongue isn't a landmark; estimate it from pink/red pixels filling the lower mouth
        // interior (a tongue protruding past the lower lip), gated on the mouth being open.
        private float ComputeTongueOut(Vector3[] lm, int w, int h)
        {
            if (lm == null || lm.Length < 468 || _rgba == null) return 0f;

            float openAmount = Mathf.Abs(lm[14].y - lm[13].y);
            if (openAmount < 0.02f) return 0f;

            float x0 = Mathf.Min(lm[78].x, lm[308].x);
            float x1 = Mathf.Max(lm[78].x, lm[308].x);
            float y0 = (lm[13].y + lm[14].y) * 0.5f;
            float y1 = lm[14].y + openAmount * 0.5f;

            int px0 = Mathf.Clamp((int)(x0 * w), 0, w - 1);
            int px1 = Mathf.Clamp((int)(x1 * w), 0, w - 1);
            int py0 = Mathf.Clamp((int)(y0 * h), 0, h - 1);
            int py1 = Mathf.Clamp((int)(y1 * h), 0, h - 1);
            if (px1 <= px0 || py1 <= py0) return 0f;

            int total = 0;
            int tongue = 0;
            int stepX = Mathf.Max(1, (px1 - px0) / 16);
            int stepY = Mathf.Max(1, (py1 - py0) / 16);
            for (int y = py0; y <= py1; y += stepY)
            {
                int row = y * w;
                for (int x = px0; x <= px1; x += stepX)
                {
                    int idx = (row + x) * 4;
                    byte r = _rgba[idx];
                    byte g = _rgba[idx + 1];
                    byte b = _rgba[idx + 2];
                    total++;
                    // pink/red, not too dark, not white (teeth)
                    if (r > 60 && r > g + 12 && r > b + 12 && r + g + b < 600)
                    {
                        tongue++;
                    }
                }
            }
            float fraction = total == 0 ? 0f : (float)tongue / total;
            // Subtract a baseline so lip/gum edges inside the ROI don't read as tongue.
            return Mathf.Clamp01((fraction - 0.25f) / 0.75f);
        }

        private Image NewImage(int w, int h) =>
            new Image(ImageFormat.Types.Format.Srgba, w, h, w * 4, _native);

        public bool TryGetLatestResult(out BasisMediaPipeResult result)
        {
            lock (_resultLock)
            {
                if (_hasLatest)
                {
                    result = _latest;
                    _hasLatest = false;
                    return true;
                }
            }
            result = default;
            return false;
        }

        public void Shutdown()
        {
            _running = false;
            _signal?.Set();
            if (_worker != null && _worker.IsAlive)
            {
                _worker.Join(500);
            }
            _worker = null;
            _signal?.Dispose();
            _signal = null;

            _face?.Close();
            _hand?.Close();
            _pose?.Close();
            _face = null;
            _hand = null;
            _pose = null;
            if (_native.IsCreated) _native.Dispose();
            IsAvailable = false;
            _busy = false;
            lock (_resultLock) { _hasLatest = false; }
        }

        private static string ResolveModelPath(string fileName) =>
            Path.Combine(Application.streamingAssetsPath, ModelSubdir, fileName);
    }
}
#endif
