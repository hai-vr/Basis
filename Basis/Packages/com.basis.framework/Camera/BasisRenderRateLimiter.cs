namespace Basis
{
    public struct BasisRenderRateLimiter
    {
        private float accumulator;

        public bool AllowThisFrame(float deltaTime, float targetHz, bool limitEnabled)
        {
            if (!limitEnabled || targetHz <= 0f)
            {
                accumulator = 0f;
                return true;
            }

            float interval = 1f / targetHz;
            accumulator += deltaTime;
            if (accumulator >= interval)
            {
                accumulator -= interval;
                if (accumulator > interval) accumulator = interval;
                return true;
            }
            return false;
        }
    }
}
