namespace Basis.MediaPipe
{
    /// <summary>Runtime configuration for the webcam tracker. Bound to Basis settings in M4.</summary>
    public struct BasisMediaPipeConfig
    {
        public bool EnableFace;
        public bool EnableHands;
        public bool EnablePose;
        public bool EnableHead;
        public bool EnableHandTracking;
        public bool SwapHands;
        public bool MirrorHorizontally;
        public int TargetFps;
        public bool UseGpu;

        public static BasisMediaPipeConfig Default => new BasisMediaPipeConfig
        {
            EnableFace = true,
            EnableHands = true,
            EnablePose = false,
            EnableHead = false,
            EnableHandTracking = false,
            SwapHands = false,
            MirrorHorizontally = true,
            TargetFps = 30,
            UseGpu = false,
        };
    }
}
