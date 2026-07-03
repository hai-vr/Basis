#if !UNITY_2017_1_OR_NEWER
using Basis.Network.Core;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Basis.Network.Server
{
public sealed class BasisRestApiRoutes
{
    private const int MaxBodyBytes     = 1 << 20;
    private const int MaxMessageLength = 512;
    private static readonly byte[]       Empty = Array.Empty<byte>();
    private static readonly JsonElement  EmptyObject;
    private readonly IServerControl _control;

    static BasisRestApiRoutes()
    {
        using var d = JsonDocument.Parse("{}");
        EmptyObject = d.RootElement.Clone();
    }

    public BasisRestApiRoutes(IServerControl control) { _control = control; }

    public void Dispatch(HttpListenerRequest req, HttpListenerResponse res, string[] segments, CancellationToken cancellationToken = default)
    {
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

                case "players":
                    if (method != "GET") { MethodNotAllowed(res, "GET"); return; }
                    ListPlayers(res);
                    break;

                case "worlds":
                    if (id == "switch")
                    {
                        if (method == "POST") SwitchWorld(req, res, cancellationToken);
                        else MethodNotAllowed(res, "POST");
                        break;
                    }
                    switch (method)
                    {
                        case "GET":    ListWorlds(res);                              break;
                        case "POST":   LoadWorld(req, res);                          break;
                        case "DELETE":
                            if (string.IsNullOrEmpty(id)) ClearAllWorlds(res);
                            else                          UnloadWorld(res, id);
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

    private void AnnounceAll(HttpListenerRequest req, HttpListenerResponse res)
    {
        if (ReadBody(req, res) is not { } body) return;
        if (!TryGetMessage(body, res, out var msg)) return;
        _control.AnnounceAll(msg);
        WriteJson(res, """{"ok":true}""");
    }

    private void AnnouncePlayer(HttpListenerRequest req, HttpListenerResponse res, string uuid)
    {
        if (ReadBody(req, res) is not { } body) return;
        if (!TryGetMessage(body, res, out var msg)) return;
        if (!_control.AnnouncePlayer(uuid, msg)) { NotFound(res, "player not found"); return; }
        WriteJson(res, """{"ok":true}""");
    }

    private void ListPlayers(HttpListenerResponse res)
    {
        var entries = _control.ListPlayers().Select(p =>
            $$"""{"netId":{{p.NetId}},"uuid":{{JsonSerializer.Serialize(p.Uuid)}},"displayName":{{JsonSerializer.Serialize(p.DisplayName)}},"platform":{{JsonSerializer.Serialize(p.Platform)}},"position":{{JsonSerializer.Serialize(p.Position)}}}""");
        WriteJson(res, $$"""{"players":[{{string.Join(",", entries)}}]}""");
    }

    private void ListWorlds(HttpListenerResponse res)
    {
        var entries = _control.ListWorlds().Select(w =>
            $$"""{"netId":{{JsonSerializer.Serialize(w.NetId)}},"url":{{JsonSerializer.Serialize(w.Url)}},"persistent":{{(w.Persistent ? "true" : "false")}},"adminLocked":{{(w.AdminLocked ? "true" : "false")}},"strategy":{{(byte)w.Strategy}}}""");
        WriteJson(res, $$"""{"worlds":[{{string.Join(",", entries)}}]}""");
    }

    private void LoadWorld(HttpListenerRequest req, HttpListenerResponse res)
    {
        if (ReadBody(req, res) is not { } body) return;
        if (!TryGetUrlAndPassword(body, res, out var url, out var password)) return;

        bool persistent = body.TryGetProperty("persistent", out var pp) && pp.ValueKind == JsonValueKind.True;
        var strategy = LoadStrategy.Immediate;
        if (body.TryGetProperty("strategy", out var sp))
        {
            if (sp.ValueKind == JsonValueKind.String)
            {
                switch (sp.GetString())
                {
                    case "synchronized": strategy = LoadStrategy.Synchronized; break;
                    case "predownload":  strategy = LoadStrategy.Predownload;  break;
                    case "immediate":    strategy = LoadStrategy.Immediate;    break;
                    default: BadRequest(res, "unknown strategy"); return;
                }
            }
            else if (sp.ValueKind == JsonValueKind.Number && sp.TryGetByte(out byte n))
            {
                if (!Enum.IsDefined(typeof(LoadStrategy), n))
                {
                    BadRequest(res, "unknown strategy");
                    return;
                }
                strategy = (LoadStrategy)n;
            }
        }

        var netId = _control.LoadWorld(new WorldLoadParams(url, password, persistent, strategy));
        WriteJson(res, $$"""{"ok":true,"netId":{{JsonSerializer.Serialize(netId)}}}""");
    }

    private void UnloadWorld(HttpListenerResponse res, string netId)
    {
        if (!_control.UnloadWorld(netId)) { NotFound(res, "world not found"); return; }
        WriteJson(res, """{"ok":true}""");
    }

    private void ClearAllWorlds(HttpListenerResponse res)
    {
        int count = _control.ClearAllWorlds();
        WriteJson(res, $$"""{"ok":true,"unloaded":{{count}}}""");
    }

    private void SwitchWorld(HttpListenerRequest req, HttpListenerResponse res, CancellationToken cancellationToken)
    {
        if (ReadBody(req, res) is not { } body) return;
        if (!TryGetUrlAndPassword(body, res, out var url, out var password)) return;

        bool persistent = body.TryGetProperty("persistent", out var pp) && pp.ValueKind == JsonValueKind.True;
        string announce = "";
        if (body.TryGetProperty("announceMessage", out var ap))
        {
            if (ap.ValueKind != JsonValueKind.String) { BadRequest(res, "announceMessage must be a string"); return; }
            announce = ap.GetString()!;
            if (announce.Length > MaxMessageLength) { BadRequest(res, $"announceMessage exceeds {MaxMessageLength} characters"); return; }
        }

        int delay = 0;
        if (body.TryGetProperty("delay", out var dp))
        {
            if (dp.ValueKind == JsonValueKind.Number && dp.TryGetInt32(out int n) && n >= 0 && n <= 300)
                delay = n;
            else if (dp.ValueKind != JsonValueKind.Null)
            { BadRequest(res, "delay must be an integer 0–300 (seconds)"); return; }
        }

        var netId = _control.SwitchWorld(new SwitchWorldParams(url, password, persistent, announce, delay), cancellationToken);
        WriteJson(res, $$"""{"ok":true,"netId":{{JsonSerializer.Serialize(netId)}}}""");
    }

    // ── Parse helpers ──────────────────────────────────────────────────────

    private bool TryGetMessage(JsonElement body, HttpListenerResponse res, out string message)
    {
        message = "";
        if (!body.TryGetProperty("message", out var mp)) { BadRequest(res, "missing message"); return false; }
        if (mp.ValueKind != JsonValueKind.String) { BadRequest(res, "message must be a string"); return false; }
        message = mp.GetString()!;
        if (string.IsNullOrEmpty(message)) { BadRequest(res, "message is empty"); return false; }
        if (message.Length > MaxMessageLength) { BadRequest(res, $"message exceeds {MaxMessageLength} characters"); return false; }
        return true;
    }

    private bool TryGetUrlAndPassword(JsonElement body, HttpListenerResponse res, out string url, out string password)
    {
        url = ""; password = "";
        if (!body.TryGetProperty("url", out var urlProp)) { BadRequest(res, "missing url"); return false; }
        if (urlProp.ValueKind != JsonValueKind.String) { BadRequest(res, "url must be a string"); return false; }

        var (rawUrl, embedded) = SplitUrlFragment(urlProp.GetString()!);
        string? pw = null;
        if (body.TryGetProperty("password", out var passProp))
        {
            if (passProp.ValueKind != JsonValueKind.String) { BadRequest(res, "password must be a string"); return false; }
            pw = passProp.GetString();
        }
        pw ??= embedded;

        if (string.IsNullOrEmpty(rawUrl)) { BadRequest(res, "url must not be empty"); return false; }
        if (!RequireHttpsUrl(rawUrl, res)) return false;
        if (string.IsNullOrEmpty(pw)) { BadRequest(res, "password required (provide password field or embed in url as #fragment)"); return false; }

        url = rawUrl; password = pw;
        return true;
    }

    private static (string url, string? fragment) SplitUrlFragment(string raw)
    {
        raw = raw.Trim();
        int idx = raw.IndexOf('#');
        if (idx >= 0)
            return (raw[..idx], DecodeFragmentPassword(raw[(idx + 1)..]));
        idx = raw.IndexOf("%23", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
            return (raw[..idx], DecodeFragmentPassword(raw[(idx + 3)..]));
        return (raw, null);
    }

    private static string? DecodeFragmentPassword(string fragment)
    {
        if (string.IsNullOrEmpty(fragment)) return null;
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(fragment)); }
        catch { return fragment; }
    }

    private static bool RequireHttpsUrl(string url, HttpListenerResponse res)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var parsed) && parsed.Scheme == Uri.UriSchemeHttps)
            return true;
        BadRequest(res, "url must use https://");
        return false;
    }

    // ── Response helpers ───────────────────────────────────────────────────

    private static JsonElement? ReadBody(HttpListenerRequest req, HttpListenerResponse res)
    {
        if (req.ContentLength64 > MaxBodyBytes) { WriteJson(res, """{"error":"payload too large"}""", 413); return null; }

        using var ms = new MemoryStream();
        var buf = new byte[4096];
        int totalBytes = 0, read;
        while ((read = req.InputStream.Read(buf, 0, buf.Length)) > 0)
        {
            totalBytes += read;
            if (totalBytes > MaxBodyBytes) { WriteJson(res, """{"error":"payload too large"}""", 413); return null; }
            ms.Write(buf, 0, read);
        }

        string json = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
        if (string.IsNullOrWhiteSpace(json)) return EmptyObject;

        try { using var doc = JsonDocument.Parse(json); return doc.RootElement.Clone(); }
        catch (JsonException) { BadRequest(res, "invalid JSON body"); return null; }
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
