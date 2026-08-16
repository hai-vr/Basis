/// <summary>
/// Releases captured microphone frames on a steady frame-period cadence instead of in
/// whatever bursts the main-thread capture pump delivers them in. Timestamps are raw
/// <see cref="System.Diagnostics.Stopwatch"/> ticks; the caller supplies them so the
/// pacer can be driven off a virtual clock in tests.
/// </summary>
public sealed class BasisMicrophonePacer
{
    /// <summary>
    /// Queued frames at or above which pacing is abandoned and the backlog is released
    /// as fast as it can be processed. Holding is only better than bursting while the
    /// hold stays inside the listener's jitter buffer; three held frames is 60 ms
    /// against a 100 ms <see cref="RemoteOpusSettings.JitterBufferSize"/> floor.
    /// </summary>
    public const int MaxBacklogFrames = 4;

    private long _nextRelease;

    public long NextRelease => _nextRelease;

    public void Resync(long now)
    {
        _nextRelease = now;
    }

    /// <summary>
    /// True when a frame may be released now. On false, <paramref name="waitTicks"/> is
    /// the remaining hold, or 0 when nothing is queued at all.
    /// </summary>
    public bool TryRelease(long now, int pendingFrames, long framePeriodTicks, out long waitTicks)
    {
        waitTicks = 0;

        if (pendingFrames <= 0)
        {
            _nextRelease = now;
            return false;
        }

        if (pendingFrames >= MaxBacklogFrames || framePeriodTicks <= 0)
        {
            _nextRelease = now;
            return true;
        }

        if (now - _nextRelease > framePeriodTicks)
        {
            _nextRelease = now;
        }

        long remaining = _nextRelease - now;
        if (remaining > 0)
        {
            waitTicks = remaining;
            return false;
        }

        _nextRelease += framePeriodTicks;
        return true;
    }
}
