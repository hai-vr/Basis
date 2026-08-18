using System.Text.Json.Serialization;

namespace Basis.Bench.Agent;

/// <summary>
/// The wire contract between the benchmark and a remote load-generating machine.
///
/// <para>Compiled into both sides from this one file rather than shared through an assembly
/// reference, so the benchmark does not take a dependency on the agent to talk to it — and so a
/// change to the protocol cannot compile on one side and not the other.</para>
///
/// <para>Line-delimited JSON over TCP: one request per line, one response per line. Deliberately
/// the dullest thing that works. It is inspectable with netcat when a run misbehaves at 2am, it has
/// no framing subtleties to get wrong, and the traffic is a handful of messages per run — the
/// bandwidth this protocol uses must stay far below the noise floor of the thing it is measuring.
/// </para>
/// </summary>
public static class BenchAgentProtocol
{
    /// <summary>
    /// Default TCP control port.
    ///
    /// <para><b>Not 4296.</b> That is the server's UDP game port, and the load clients on this very
    /// machine will be talking to it. Reusing the number would stop the agent ever running on the
    /// server's own box, muddle firewall rules that want to treat control and game traffic
    /// differently, and make a packet capture ambiguous about which thing it was looking at.</para>
    /// </summary>
    public const int DefaultPort = 4297;

    /// <summary>Bumped when the message shapes change; a mismatched pair refuses rather than guesses.</summary>
    public const int Version = 1;
}

public sealed class AgentRequest
{
    [JsonPropertyName("cmd")] public string Command { get; set; } = "";
    [JsonPropertyName("version")] public int Version { get; set; } = BenchAgentProtocol.Version;

    /// <summary>How many simulated clients to run.</summary>
    [JsonPropertyName("clients")] public int Clients { get; set; }

    /// <summary>The server they should connect to, as seen from the agent's machine.</summary>
    [JsonPropertyName("host")] public string Host { get; set; } = "";
    [JsonPropertyName("port")] public int Port { get; set; }

    /// <summary>Delay between starting each client; 0 is the thundering herd a restart produces.</summary>
    [JsonPropertyName("connectIntervalMs")] public int ConnectIntervalMs { get; set; } = 1;
}

public sealed class AgentResponse
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
    [JsonPropertyName("version")] public int Version { get; set; } = BenchAgentProtocol.Version;

    // ── hello ────────────────────────────────────────────────────────────────
    [JsonPropertyName("agent")] public string? Agent { get; set; }
    [JsonPropertyName("cores")] public int Cores { get; set; }
    [JsonPropertyName("os")] public string? Os { get; set; }

    // ── status ───────────────────────────────────────────────────────────────
    [JsonPropertyName("running")] public bool Running { get; set; }

    /// <summary>
    /// Load-client CPU on the AGENT's machine, in cores.
    ///
    /// Reported not because it is part of any score — it explicitly is not — but so the benchmark
    /// can tell whether the load generator itself ran out of capacity. A client that is saturated
    /// stops pushing, and the server then looks comfortable for a reason that has nothing to do
    /// with the server.
    /// </summary>
    [JsonPropertyName("clientCores")] public double ClientCores { get; set; }

    /// <summary>Share of simulated voice frames a receiver actually got, or -1 when unknown.</summary>
    [JsonPropertyName("voiceDelivered")] public double VoiceDelivered { get; set; } = -1;
}
