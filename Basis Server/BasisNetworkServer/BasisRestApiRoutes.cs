#if !UNITY_2017_1_OR_NEWER
using Basis.Network.Core;
using BasisNetworkServer.Security;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using static BasisNetworkCore.Serializable.SerializableBasis;
using static SerializableBasis;

namespace Basis.Network.Server
{
public static class BasisRestApiRoutes
{
    private const int MaxBodyBytes    = 1 << 20; // 1 MiB
    private const int MaxMessageLength = 512;
    private static readonly byte[] Empty = Array.Empty<byte>();
    private static readonly JsonElement EmptyObject;

    static BasisRestApiRoutes()
    {
        using var d = JsonDocument.Parse("{}");
        EmptyObject = d.RootElement.Clone();
    }

    public static void Dispatch(HttpListenerRequest req, HttpListenerResponse res, string[] segments)
    {
        // segments[0] = "api", segments[1] = resource, segments[2] = id (optional)
        string resource = segments.Length > 1 ? segments[1] : "";
        string id       = segments.Length > 2 ? segments[2] : "";
        string method   = req.HttpMethod.ToUpperInvariant();

        try
        {
            switch (resource)
            {
                case "announce":
                    if (method != "POST") { MethodNotAllowed(res, "POST"); return; }
                    if (string.IsNullOrEmpty(id)) AnnounceAll(req, res);
                    else                          AnnouncePlayer(req, res, id);
                    break;

                case "worlds":
                    if (id == "switch")
                    {
                        if (method == "POST") SwitchWorld(req, res);
                        else MethodNotAllowed(res, "POST");
                        break;
                    }
                    switch (method)
                    {
                        case "GET":    ListWorlds(res);                              break;
                        case "POST":   LoadWorld(req, res);                          break;
                        case "DELETE":
                            if (string.IsNullOrEmpty(id)) { NotFound(res); return; }
                            UnloadWorld(res, id);
                            break;
                        default: MethodNotAllowed(res, "GET, POST, DELETE");         break;
                    }
                    break;

                default:
                    NotFound(res);
                    break;
            }
        }
        catch (Exception e)
        {
            BNL.LogError("REST API handler error: " + e);
            try { WriteJson(res, """{"error":"internal server error"}""", 500); } catch { }
        }
    }

    // POST /api/announce
    private static void AnnounceAll(HttpListenerRequest req, HttpListenerResponse res)
    {
        if (ReadBody(req, res) is not { } body) return;
        if (!body.TryGetProperty("message", out var msgProp)) { BadRequest(res, "missing message"); return; }
        if (msgProp.ValueKind != JsonValueKind.String) { BadRequest(res, "message must be a string"); return; }
        string msg = msgProp.GetString()!;
        if (string.IsNullOrEmpty(msg)) { BadRequest(res, "message is empty"); return; }
        if (msg.Length > MaxMessageLength) { BadRequest(res, $"message exceeds {MaxMessageLength} characters"); return; }

        var writer = NetworkServer.RentWriter();
        try
        {
            new AdminRequest().Serialize(writer, AdminRequestMode.MessageAll);
            writer.Put(msg);
            NetworkServer.BroadcastMessageToClients(writer, BasisNetworkCommons.AdminChannel, NetworkServer.PeerSnapshot, DeliveryMethod.ReliableOrdered);
        }
        finally { NetworkServer.ReturnWriter(writer); }

        BNL.Log($"[REST] Announced to all: {msg}");
        WriteJson(res, """{"ok":true}""");
    }

    // POST /api/announce/{uuid}
    private static void AnnouncePlayer(HttpListenerRequest req, HttpListenerResponse res, string uuid)
    {
        if (ReadBody(req, res) is not { } body) return;
        if (!body.TryGetProperty("message", out var msgProp)) { BadRequest(res, "missing message"); return; }
        if (msgProp.ValueKind != JsonValueKind.String) { BadRequest(res, "message must be a string"); return; }
        string msg = msgProp.GetString()!;
        if (string.IsNullOrEmpty(msg)) { BadRequest(res, "message is empty"); return; }
        if (msg.Length > MaxMessageLength) { BadRequest(res, $"message exceeds {MaxMessageLength} characters"); return; }

        if (NetworkServer.AuthIdentity == null ||
            !NetworkServer.AuthIdentity.UUIDToNetID(uuid, out int id) ||
            !NetworkServer.AuthenticatedPeers.TryGetValue(id, out var peer))
        {
            NotFound(res, "player not found");
            return;
        }

        BasisPlayerModeration.SendBackMessage(peer, msg);
        BNL.Log($"[REST] Announced to {uuid}: {msg}");
        WriteJson(res, """{"ok":true}""");
    }

    // GET /api/worlds
    private static void ListWorlds(HttpListenerResponse res)
    {
        var worlds = BasisNetworkResourceManagement.UshortNetworkDatabase.Values
            .Where(r => r.Mode == 1)
            .Select(r => $$"""{"netId":{{JsonSerializer.Serialize(r.LoadedNetID)}},"url":{{JsonSerializer.Serialize(r.CombinedURL)}},"persistent":{{(r.Persist ? "true" : "false")}},"adminLocked":{{(r.IsAdminLocked ? "true" : "false")}},"strategy":{{r.LoadStrategy}}}""");
        WriteJson(res, $$"""{"worlds":[{{string.Join(",", worlds)}}]}""");
    }

    // POST /api/worlds
    private static void LoadWorld(HttpListenerRequest req, HttpListenerResponse res)
    {
        if (ReadBody(req, res) is not { } body) return;
        if (!body.TryGetProperty("url", out var urlProp) || !body.TryGetProperty("password", out var passProp))
        {
            BadRequest(res, "missing url or password");
            return;
        }
        if (urlProp.ValueKind != JsonValueKind.String || passProp.ValueKind != JsonValueKind.String)
        {
            BadRequest(res, "url and password must be strings");
            return;
        }

        string url      = urlProp.GetString()!;
        string password = passProp.GetString()!;
        bool persistent = body.TryGetProperty("persistent", out var pp) && pp.ValueKind == JsonValueKind.True;
        byte strategy = 0;
        if (body.TryGetProperty("strategy", out var sp))
        {
            if (sp.ValueKind == JsonValueKind.String)
            {
                if (sp.GetString() == "synchronized") strategy = 2;
                else { BadRequest(res, "unknown strategy"); return; }
            }
            else if (sp.ValueKind == JsonValueKind.Number && sp.TryGetByte(out byte n)) strategy = n;
        }

        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(password))
        {
            BadRequest(res, "url and password must not be empty");
            return;
        }

        var resource = BuildLoadResource(url, password, persistent, strategy);
        if (strategy == 2)
            BasisNetworkPreloadResourceManagement.StartSynchronizedLoad(resource);
        else
            BasisNetworkResourceManagement.LoadResource(resource);

        BNL.Log($"[REST] Load world: {url} strategy={strategy} netId={resource.LoadedNetID}");
        WriteJson(res, $$"""{"ok":true,"netId":{{JsonSerializer.Serialize(resource.LoadedNetID)}}}""");
    }

    // DELETE /api/worlds/{netId}
    private static void UnloadWorld(HttpListenerResponse res, string netId)
    {
        if (!BasisNetworkResourceManagement.UnloadResource(new UnLoadResource { LoadedNetID = netId, Mode = 1 }))
        {
            NotFound(res, "world not found");
            return;
        }

        BNL.Log($"[REST] Unloaded world: {netId}");
        WriteJson(res, """{"ok":true}""");
    }

    // POST /api/worlds/switch  — announce then synchronized load
    private static void SwitchWorld(HttpListenerRequest req, HttpListenerResponse res)
    {
        if (ReadBody(req, res) is not { } body) return;
        if (!body.TryGetProperty("url", out var urlProp) || !body.TryGetProperty("password", out var passProp))
        {
            BadRequest(res, "missing url or password");
            return;
        }
        if (urlProp.ValueKind != JsonValueKind.String || passProp.ValueKind != JsonValueKind.String)
        {
            BadRequest(res, "url and password must be strings");
            return;
        }

        string url      = urlProp.GetString()!;
        string password = passProp.GetString()!;
        bool persistent = body.TryGetProperty("persistent", out var pp) && pp.ValueKind == JsonValueKind.True;
        string announce = "";
        if (body.TryGetProperty("announceMessage", out var ap))
        {
            if (ap.ValueKind != JsonValueKind.String) { BadRequest(res, "announceMessage must be a string"); return; }
            announce = ap.GetString()!;
        }

        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(password))
        {
            BadRequest(res, "url and password must not be empty");
            return;
        }
        if (announce.Length > MaxMessageLength) { BadRequest(res, $"announceMessage exceeds {MaxMessageLength} characters"); return; }

        var resource = BuildLoadResource(url, password, persistent, strategy: 2);

        if (!string.IsNullOrEmpty(announce))
        {
            var writer = NetworkServer.RentWriter();
            try
            {
                new AdminRequest().Serialize(writer, AdminRequestMode.MessageAll);
                writer.Put(announce);
                NetworkServer.BroadcastMessageToClients(writer, BasisNetworkCommons.AdminChannel, NetworkServer.PeerSnapshot, DeliveryMethod.ReliableOrdered);
            }
            finally { NetworkServer.ReturnWriter(writer); }
        }
        BasisNetworkPreloadResourceManagement.StartSynchronizedLoad(resource);

        BNL.Log($"[REST] Switch world: {url} netId={resource.LoadedNetID} announce={!string.IsNullOrEmpty(announce)}");
        WriteJson(res, $$"""{"ok":true,"netId":{{JsonSerializer.Serialize(resource.LoadedNetID)}}}""");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static LocalLoadResource BuildLoadResource(string url, string password, bool persistent, byte strategy)
    {
        return new LocalLoadResource
        {
            LoadedNetID    = GenerateNetId(),
            Mode           = 1,
            CombinedURL    = url,
            UnlockPassword = password,
            UUIDOfCreator  = "server",
            IsAdminLocked  = true,
            Persist        = persistent,
            LoadStrategy   = strategy,
        };
    }

    private static string GenerateNetId() => Guid.NewGuid().ToString("N");

    // Returns null and writes the error response for oversized, empty, or malformed bodies.
    // Empty body is treated as {} so callers' field-missing checks produce the right 400.
    private static JsonElement? ReadBody(HttpListenerRequest req, HttpListenerResponse res)
    {
        // ContentLength64 is -1 when absent; the streaming check below is the authoritative gate.
        if (req.ContentLength64 > MaxBodyBytes)
        {
            WriteJson(res, """{"error":"payload too large"}""", 413);
            return null;
        }

        using var ms = new MemoryStream();
        var buf = new byte[4096];
        int totalBytes = 0;
        int read;
        while ((read = req.InputStream.Read(buf, 0, buf.Length)) > 0)
        {
            totalBytes += read;
            if (totalBytes > MaxBodyBytes)
            {
                WriteJson(res, """{"error":"payload too large"}""", 413);
                return null;
            }
            ms.Write(buf, 0, read);
        }

        string json = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
        if (string.IsNullOrWhiteSpace(json)) return EmptyObject;

        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            BadRequest(res, "invalid JSON body");
            return null;
        }
    }

    private static void WriteJson(HttpListenerResponse res, string json, int status = 200)
    {
        byte[] payload = Encoding.UTF8.GetBytes(json);
        res.StatusCode = status;
        res.ContentType = "application/json; charset=utf-8";
        res.ContentLength64 = payload.Length;
        try { res.OutputStream.Write(payload, 0, payload.Length); }
        finally { res.OutputStream.Close(); }
    }

    private static void BadRequest(HttpListenerResponse res, string msg) =>
        WriteJson(res, $$"""{"error":{{JsonSerializer.Serialize(msg)}}}""", 400);

    private static void NotFound(HttpListenerResponse res, string msg = "not found") =>
        WriteJson(res, $$"""{"error":{{JsonSerializer.Serialize(msg)}}}""", 404);

    private static void MethodNotAllowed(HttpListenerResponse res, string allow)
    {
        res.StatusCode = 405;
        res.Headers["Allow"] = allow;
        res.Close(Empty, false);
    }
}
}
#endif
