namespace Basis.Scripts.Drivers
{
    public static class BasisFrameClock
    {
        public static float SmoothedFramesPerSecond { get; private set; }

        public static event System.Action OnTick;

        private static float _smoothedUnscaledDelta;

        public static void Tick(float unscaledDeltaTime)
        {
            if (unscaledDeltaTime > 0f)
            {
                if (_smoothedUnscaledDelta <= 0f)
                {
                    _smoothedUnscaledDelta = unscaledDeltaTime;
                }
                else
                {
                    _smoothedUnscaledDelta += (unscaledDeltaTime - _smoothedUnscaledDelta) * 0.1f;
                }
                SmoothedFramesPerSecond = _smoothedUnscaledDelta > 0f ? 1f / _smoothedUnscaledDelta : 0f;
            }

            OnTick?.Invoke();
        }
    }
}
