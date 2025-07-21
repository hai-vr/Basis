
#if !UNITY_SERVER
using System.IO;
using System.Net.Http;
using System;
using UnityEngine;
using FFmpeg.Unity;
using System.Threading.Tasks;
using Basis.Scripts.BasisSdk.Players;
using TMPro;
namespace Basis.VideoPlayer
{
    public class BasisVideoPlayer : MonoBehaviour
    {
        public FFPlayUnity ffmpeg; // FFmpeg integration script or component
        public RenderTexture RuntimeTexture;
        public Material RuntimeMaterial;
        public string MaterialTextureId = "_EmissionMap";
        public string GlobalTextureId = null;
        public string DefaultUrl = "";
        public bool SetBothEmissionAndMainTexture = true;
        [SerializeField] private TMP_InputField Field;

        public string Url { get; set; }

        private string lastUrl = string.Empty;
        private Texture2D lastTexture = null;

        private void Start()
        {

            Url = DefaultUrl;
            ffmpeg.texturePlayer.OnDisplay += Display;
            ResetUrlInput();
            DynamicallyLoadedBindings.LibrariesPath = Path.Combine(Application.streamingAssetsPath, "ffmpeg", "");
            DynamicallyLoadedBindings.Initialize();

            if (BasisLocalPlayer.Instance != null)
            {
                OnLocalPlayerReady();
            }
            else
            {
                BasisLocalPlayer.OnLocalPlayerCreated += OnLocalPlayerReady;
            }
        }

        private void OnLocalPlayerReady()
        {
            BasisLocalPlayer.OnLocalPlayerCreated -= OnLocalPlayerReady;
            Play(Url);
        }

        public void ResetUrlInput()
        {
            if (Field != null) Field.SetTextWithoutNotify(Url);
        }

        private void OnDestroy()
        {
            ffmpeg.texturePlayer.OnDisplay -= Display;
        }

        private void Display(Texture2D tex)
        {
            // update given render texture content
            if (RuntimeTexture != null) Graphics.Blit(tex, RuntimeTexture);

            if (tex == lastTexture) return; // if texture refs match, skip remaining texture ref assignments
            lastTexture = tex;

            // assign to given material
            if (RuntimeMaterial != null)
            {
                // use given texture property
                if (string.IsNullOrEmpty(MaterialTextureId))
                {
                    RuntimeMaterial.mainTexture = tex;
                }
                // use default texture properties
                else
                {
                    RuntimeMaterial.SetTexture(MaterialTextureId, tex);
                }
            }

            // assign to given global texture property
            if (GlobalTextureId != null) Shader.SetGlobalTexture(GlobalTextureId, tex);
        }

        public void Play()
        {
            BasisLocalPlayer.OnSpawnedEvent -= Play;
            Play(Url);
        }

        public void Play(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                BasisDebug.LogError("Url was empty");
                return;
            }
            if (url == lastUrl)
            {
                if (IsPaused) Resume();
                return;
            }

            PlayAsync(url);
            Url = lastUrl = url;
            if (Field != null) Field.SetTextWithoutNotify(url);
        }

        private readonly string[] liveProtocols = { "rtmp://", "rtsp://", "rtspt://", "rtspu://", "rtsps://" };

        // Play a video from a URL or local file path
        public void PlayAsync(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                Error("Error: URL or file path is empty.");
                return;
            }

            ffmpeg.Play(url);
        }

        public async void PlayFile(string filePath)
        {
            await PlayFileAsync(filePath);
        }

        // Play a local video file using FFmpeg
        public async Task PlayFileAsync(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    Log("Playing local file: " + filePath);
                    await using FileStream videoStream = File.OpenRead(filePath);
                    ffmpeg.Play(videoStream, videoStream);
                }
                else
                {
                    Error("File not found: " + filePath);
                }
            }
            catch (Exception e)
            {
                Error("Error playing file: " + e.Message);
            }
        }

        // Play a video stream from a URL
        public void PlayStreamAsync(string url)
        {
            Log("Playing stream from URL: " + url);
            ffmpeg.Play(url, url);
        }

        public async void PlayFromWeb(string url)
        {
            await PlayAsyncFromWeb(url);
        }

        // Optionally, allow for downloading and playing video from the web using HTTP
        public async Task PlayAsyncFromWeb(string url)
        {
            HttpClient client = new HttpClient();
            try
            {
                HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                Stream videoStream = await response.Content.ReadAsStreamAsync();
                ffmpeg.Play(videoStream, videoStream);
            }
            catch (Exception e)
            {
                Error("Error fetching video stream: " + e.Message);
            }
        }

        public void TogglePlay()
        {
            if (ffmpeg.IsPaused) Resume();
            else Pause();
        }

        // API method to pause video playback
        public void Pause()
        {
            if (!ffmpeg.IsPaused) ffmpeg.Pause();
        }

        // API method to resume video playback
        public void Resume()
        {
            if (ffmpeg.IsPaused) ffmpeg.Resume();
        }


        /// <summary>
        /// Seek update API method that is compatible with UI slider components
        /// </summary>
        /// <param name="timeInSeconds">Where do you want to seek to?</param>
        public void Seek(double timeInSeconds)
        {
            ffmpeg.Seek(timeInSeconds);
        }

        public void SeekPercent(float timeNormalized)
        {
            Seek(ffmpeg.videoTimings.GetLength() * timeNormalized);
        }

        /// <summary>
        /// Volume update API method that is compatible with UI slider components
        /// </summary>
        /// <param name="volume">THE LOUDNESS VALUE</param>
        public void SetVolume(float volume)
        {
            ffmpeg.audioPlayer.SetVolume(Mathf.Clamp(volume, 0f, 1f));
        }

        public double PlaybackTime => ffmpeg.PlaybackTime;
        public bool IsPaused => ffmpeg.IsPaused;

        private static void Log(string message)
        {
            Debug.Log($"[<color=#00ffff>{nameof(BasisVideoPlayer)}</color>] {message}");
        }

        private static void Error(string message)
        {
            Debug.LogError($"[<color=#00ffff>{nameof(BasisVideoPlayer)}</color>] {message}");
        }
    }
}
#endif
