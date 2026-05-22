using UnityEngine;

namespace Basis.MediaPipe
{
    /// <summary>Abstraction over the landmark inference engine (homuler MediaPipe, or a no-op).</summary>
    public interface IBasisMediaPipeBackend
    {
        bool IsAvailable { get; }
        string BackendName { get; }

        void Initialize(BasisMediaPipeConfig config);
        void SubmitFrame(WebCamTexture frame, double timestampMs);
        bool TryGetLatestResult(out BasisMediaPipeResult result);
        void Shutdown();
    }
}
