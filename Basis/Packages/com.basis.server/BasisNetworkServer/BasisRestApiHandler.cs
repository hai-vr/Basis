#if !UNITY_2017_1_OR_NEWER
using Basis.Network.Core;
using System;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Basis.Network.Server
{
    public sealed class BasisRestApiHandler : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly string _apiKey;
        private readonly byte[] _keyHash;
        private int _disposed;
        private static readonly byte[] Empty = Array.Empty<byte>();

        public BasisRestApiHandler(Configuration config)
        {
            _apiKey  = config.ApiKey;
            _keyHash = string.IsNullOrEmpty(_apiKey)
                ? Array.Empty<byte>()
                : SHA256.HashData(Encoding.UTF8.GetBytes(_apiKey));
            _listener.Prefixes.Add($"http://{config.ApiHost}:{config.ApiPort}/");
            _listener.Start();
            _ = ListenLoopAsync(_cts.Token);
            BNL.Log($"REST API started at http://{config.ApiHost}:{config.ApiPort}/api/");
        }

        private async Task ListenLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync().ConfigureAwait(false); }
                catch (ObjectDisposedException) { return; }
                catch (HttpListenerException e)
                {
                    if (!token.IsCancellationRequested) BNL.LogWarning("REST API listener stopped unexpectedly: " + e.Message);
                    return;
                }
                catch (Exception e) { BNL.LogWarning("REST API loop error: " + e); continue; }
                _ = Task.Run(() => HandleRequest(ctx), token);
            }
        }

        private void HandleRequest(HttpListenerContext ctx)
        {
            try
            {
                var req = ctx.Request;
                var res = ctx.Response;
                res.Headers["Cache-Control"] = "no-store, max-age=0";
                res.Headers["X-Content-Type-Options"] = "nosniff";

                if (!Authenticate(req))
                {
                    res.StatusCode = 401;
                    res.Close(Empty, false);
                    return;
                }

                var path = req.Url?.AbsolutePath?.TrimEnd('/') ?? "";
                var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

                // Must start with /api
                if (segments.Length < 2 || segments[0] != "api")
                {
                    res.StatusCode = 404;
                    res.Close(Empty, false);
                    return;
                }

                BasisRestApiRoutes.Dispatch(req, res, segments);
            }
            catch (Exception e)
            {
                BNL.LogError("REST API unhandled: " + e);
                try { ctx.Response?.Abort(); } catch { }
            }
        }

        private bool Authenticate(HttpListenerRequest req)
        {
            if (string.IsNullOrEmpty(_apiKey)) return false;
            var auth = req.Headers["Authorization"];
            if (auth == null || !auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return false;
            var tokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(auth["Bearer ".Length..]));
            return CryptographicOperations.FixedTimeEquals(_keyHash, tokenHash);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _cts.Cancel();
            try { _listener.Stop(); } catch { }
            try { _listener.Close(); } catch { }
            _cts.Dispose();
        }
    }
}
#endif
