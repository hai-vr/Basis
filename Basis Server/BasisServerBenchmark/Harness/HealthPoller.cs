namespace Basis.Benchmark.Harness;

/// <summary>
/// Reads the server's health endpoint.
///
/// <para>One shared client with a short timeout and no keep-alive games. A poll that blocks is
/// worse than a poll that fails: the sampler runs on the measurement's own clock, so a stalled
/// request stretches the window it is bounding and silently changes every rate derived from
/// it.</para>
/// </summary>
public static class HealthPoller
{
    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(3),
    };

    /// <summary>Reads one sample, or null if the server is not answering.</summary>
    public static HealthSample? TryRead(string url)
    {
        try
        {
            DateTime sampledUtc = DateTime.UtcNow;
            string json = Client.GetStringAsync(url).GetAwaiter().GetResult();
            return HealthSample.Parse(json, sampledUtc);
        }
        catch
        {
            // Starting up, shutting down, or mid-restart. The callers all treat null as "not yet".
            return null;
        }
    }
}
