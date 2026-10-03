namespace Loupedeck.AgentCockpitPlugin
{
    using System;
    using System.IO;
    using System.Net;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    public sealed class HookHttpServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly Int32 _port;
        private CancellationTokenSource? _cts;
        private Task? _loop;

        public HookHttpServer(Int32 port = 47821)
        {
            this._port = port;
        }

        public void Start()
        {
            if (this._loop != null)
            {
                return;
            }

            var prefix = $"http://127.0.0.1:{this._port}/";
            this._listener.Prefixes.Add(prefix);
            this._listener.Start();
            this._cts = new CancellationTokenSource();
            this._loop = Task.Run(() => this.RunAsync(this._cts.Token));
            PluginLog.Info($"[hook-relay] listening on {prefix}");
        }

        public void Dispose()
        {
            try
            {
                this._cts?.Cancel();
                if (this._listener.IsListening)
                {
                    this._listener.Stop();
                }

                this._listener.Close();
            }
            catch (Exception ex)
            {
                PluginLog.Warning($"[hook-relay] dispose: {ex.Message}");
            }
        }

        private async Task RunAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && this._listener.IsListening)
            {
                HttpListenerContext? ctx = null;
                try
                {
                    ctx = await this._listener.GetContextAsync().WaitAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    PluginLog.Warning($"[hook-relay] accept: {ex.Message}");
                    continue;
                }

                _ = Task.Run(() => this.Handle(ctx!), ct);
            }
        }

        private void Handle(HttpListenerContext ctx)
        {
            try
            {
                var path = ctx.Request.Url?.AbsolutePath ?? "/";
                if (ctx.Request.HttpMethod == "GET" && path == "/health")
                {
                    WriteJson(ctx, 200, AgentSlotStore.Instance.BuildHealthPayload());
                    return;
                }

                if (ctx.Request.HttpMethod == "POST" && (path == "/hook" || path == "/event"))
                {
                    using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
                    var raw = reader.ReadToEnd();
                    using var doc = JsonDocument.Parse(String.IsNullOrWhiteSpace(raw) ? "{}" : raw);
                    var response = AgentSlotStore.Instance.IngestHook(doc.RootElement);
                    WriteJson(ctx, 200, response);
                    return;
                }

                WriteJson(ctx, 404, new { error = "not_found" });
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "[hook-relay] handle failed");
                try
                {
                    WriteJson(ctx, 500, new { error = "internal_error" });
                }
                catch
                {
                    // ignore
                }
            }
        }

        private static void WriteJson(HttpListenerContext ctx, Int32 status, Object body)
        {
            var json = JsonSerializer.Serialize(body);
            var bytes = Encoding.UTF8.GetBytes(json);
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json";
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.OutputStream.Close();
        }
    }
}
