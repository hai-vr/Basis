using System;
using System.Diagnostics;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Timing evidence for the swap-path work: how much main-thread cost the worker-thread
/// parse removes, and what remains in CreateMesh once the decode is pre-cached. Numbers are
/// logged for the report; assertions are correctness plus only pathologically generous
/// bounds (editor/nographics timing is too noisy for tight gates).
/// </summary>
public class BasisFarLodParsePerfTests
{
    private const int PerfVertexCount = 16384;
    private const int PerfBoneCount = 20;

    private static double TicksToMs(long ticks)
    {
        return ticks * 1000.0 / Stopwatch.Frequency;
    }

    [Test]
    public void ParseAndDecode_TimingReport_MainVsWorker()
    {
        string base64 = BasisFarLodTestPayloads.CreateBase64(PerfVertexCount, PerfBoneCount, seed: 99);
        int payloadKilobytes = base64.Length / 1024;

        // Cold parse+decode on the main thread — the exact cost the old install paid on the
        // frame a first wearer appeared.
        Stopwatch stopwatch = Stopwatch.StartNew();
        BasisFarLodPayload mainParsed = BasisFarLodPayload.TryParseBase64(base64);
        BasisFarLodPayload.DecodedMeshData mainDecoded = mainParsed?.PrepareDecodedMeshData();
        stopwatch.Stop();
        double mainThreadMs = TicksToMs(stopwatch.ElapsedTicks);
        Assert.NotNull(mainParsed);
        Assert.NotNull(mainDecoded);

        // Same work on a worker thread — what the new install does instead; wall time here
        // is thread-pool latency plus the same work, none of it on the main thread.
        stopwatch.Restart();
        BasisFarLodPayload workerParsed = null;
        Task worker = Task.Run(() =>
        {
            workerParsed = BasisFarLodPayload.TryParseBase64(base64);
            workerParsed?.PrepareDecodedMeshData();
        });
        Assert.IsTrue(worker.Wait(TimeSpan.FromSeconds(60)));
        stopwatch.Stop();
        double workerWallMs = TicksToMs(stopwatch.ElapsedTicks);
        Assert.NotNull(workerParsed);

        // The cache turns every later decode request into a field read.
        stopwatch.Restart();
        BasisFarLodPayload.DecodedMeshData cached = mainParsed.PrepareDecodedMeshData();
        stopwatch.Stop();
        double cachedMs = TicksToMs(stopwatch.ElapsedTicks);
        Assert.AreSame(mainDecoded, cached);

        Debug.Log($"[FarAvatarPerf] payload {payloadKilobytes} KB base64, {PerfVertexCount} verts / {PerfBoneCount} bones: " +
                  $"parse+decode main-thread {mainThreadMs:F2} ms | worker wall {workerWallMs:F2} ms (main-thread cost ~0) | cached re-decode {cachedMs:F4} ms");

        Assert.Less(mainThreadMs, 30000.0, "parse+decode runaway");
        Assert.Less(cachedMs, mainThreadMs, "cached decode must be cheaper than the full decode");
    }

    [Test]
    public void CreateMesh_TimingReport_PredecodedVsInline()
    {
        string base64 = BasisFarLodTestPayloads.CreateBase64(PerfVertexCount, PerfBoneCount, seed: 123);

        // Inline: CreateMesh pays parse-side decode itself (the tester path, and the old
        // runtime behavior).
        BasisFarLodPayload inlinePayload = BasisFarLodPayload.TryParseBase64(base64);
        Assert.NotNull(inlinePayload);
        Stopwatch stopwatch = Stopwatch.StartNew();
        Mesh inlineMesh = inlinePayload.CreateMesh();
        stopwatch.Stop();
        double inlineMs = TicksToMs(stopwatch.ElapsedTicks);
        Assert.NotNull(inlineMesh);

        // Pre-decoded: the worker already ran PrepareDecodedMeshData, CreateMesh only does
        // the engine-side copies and upload (the new runtime behavior).
        BasisFarLodPayload predecodedPayload = BasisFarLodPayload.TryParseBase64(base64);
        Assert.NotNull(predecodedPayload);
        Task decode = Task.Run(() => predecodedPayload.PrepareDecodedMeshData());
        Assert.IsTrue(decode.Wait(TimeSpan.FromSeconds(60)));
        stopwatch.Restart();
        Mesh predecodedMesh = predecodedPayload.CreateMesh();
        stopwatch.Stop();
        double predecodedMs = TicksToMs(stopwatch.ElapsedTicks);
        Assert.NotNull(predecodedMesh);

        Assert.AreEqual(inlineMesh.vertexCount, predecodedMesh.vertexCount);
        Debug.Log($"[FarAvatarPerf] CreateMesh {PerfVertexCount} verts: inline decode {inlineMs:F2} ms | pre-decoded {predecodedMs:F2} ms");

        UnityEngine.Object.DestroyImmediate(inlineMesh);
        UnityEngine.Object.DestroyImmediate(predecodedMesh);
    }
}
