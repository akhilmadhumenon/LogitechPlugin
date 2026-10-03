namespace Loupedeck.AgentCockpitPlugin
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Text.Json;
    using System.Threading.Tasks;

    internal static class CompanionClient
    {
        private static readonly HttpClient Http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(3),
        };

        private static readonly String DiscoveryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".agent-cockpit",
            "companion.json");

        public static void OpenComposerFireAndForget(String? conversationId) =>
            PostFireAndForget("/v1/open-composer", conversationId, "open", body: null);

        public static void CancelComposerFireAndForget(String? conversationId) =>
            PostFireAndForget("/v1/cancel-composer", conversationId, "cancel", body: null);

        public static Task<Boolean> CancelComposerAsync(String? conversationId) =>
            PostAsync("/v1/cancel-composer", conversationId, "cancel", body: null);

        public static Task<Boolean> CloseComposerAsync(String? conversationId) =>
            PostAsync("/v1/close-composer", conversationId, "close", body: null);

        public static async Task<OpenChatsSnapshot> ListOpenChatsAsync()
        {
            try
            {
                if (!TryReadDiscovery(out var port, out var token))
                {
                    return OpenChatsSnapshot.Empty;
                }

                using var req = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/v1/chats");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var res = await Http.SendAsync(req).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode)
                {
                    return OpenChatsSnapshot.Empty;
                }

                var json = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var ids = ReadIdArray(root, "ids");
                var unused = ReadIdArray(root, "unusedIds");
                var unusedSet = unused.ToHashSet(StringComparer.Ordinal);
                var used = ids.Where(id => !unusedSet.Contains(id)).ToArray();
                return new OpenChatsSnapshot { Used = used, Unused = unused };
            }
            catch (Exception ex)
            {
                PluginLog.Warning($"[companion] list chats failed: {ex.Message}");
                return OpenChatsSnapshot.Empty;
            }
        }

        private static String[] ReadIdArray(JsonElement root, String name)
        {
            if (!root.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<String>();
            }

            return arr.EnumerateArray()
                .Select(e => e.GetString())
                .Where(s => !String.IsNullOrEmpty(s))
                .Cast<String>()
                .ToArray();
        }

        public static void DecisionFireAndForget(String? conversationId, String action) =>
            PostFireAndForget(
                "/v1/decision",
                conversationId,
                "decision",
                body: new { composerId = conversationId, conversationId, action });

        public static Task<Boolean> FocusTargetAsync(ComposerTarget target) =>
            PostPathAsync(
                "/v1/focus",
                "focus",
                new { target = target.ToString().ToLowerInvariant() });

        public static Task<Boolean> InsertPromptAsync(PromptShortcut shortcut) =>
            PostPathAsync(
                "/v1/prompt",
                "prompt",
                new
                {
                    target = shortcut.Target.ToString().ToLowerInvariant(),
                    text = shortcut.Prompt,
                    submit = shortcut.Submit,
                });

        public static Task<Boolean> ExecuteCommandAsync(String command) =>
            PostPathAsync("/v1/command", "command", new { command });

        public static async Task<String?> CreateNewChatAsync()
        {
            try
            {
                if (!TryReadDiscovery(out var port, out var token))
                {
                    return null;
                }

                using var req = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/v1/new-chat");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                req.Content = new StringContent("{}", Encoding.UTF8, "application/json");
                using var res = await Http.SendAsync(req).ConfigureAwait(false);
                var json = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!res.IsSuccessStatusCode)
                {
                    PluginLog.Warning($"[companion] new-chat HTTP {(Int32)res.StatusCode}: {json}");
                    return null;
                }

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("composerId", out var idEl))
                {
                    var id = idEl.GetString();
                    if (!String.IsNullOrEmpty(id))
                    {
                        return id;
                    }
                }

                return root.TryGetProperty("ok", out var ok) && ok.GetBoolean() ? "" : null;
            }
            catch (Exception ex)
            {
                PluginLog.Warning($"[companion] new-chat failed: {ex.Message}");
                return null;
            }
        }

        public static void ExecuteCommandFireAndForget(String command) =>
            _ = ExecuteCommandAsync(command);

        private static async Task<Boolean> PostPathAsync(String path, String label, Object body)
        {
            try
            {
                if (!TryReadDiscovery(out var port, out var token))
                {
                    return false;
                }

                using var req = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}{path}");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                req.Content = new StringContent(
                    JsonSerializer.Serialize(body),
                    Encoding.UTF8,
                    "application/json");
                using var res = await Http.SendAsync(req).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode)
                {
                    var text = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                    PluginLog.Warning($"[companion] {label} HTTP {(Int32)res.StatusCode}: {text}");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                PluginLog.Warning($"[companion] {label} failed: {ex.Message}");
                return false;
            }
        }

        public static async Task<CompanionPending?> GetPendingAsync(String conversationId)
        {
            if (!TryReadDiscovery(out var port, out var token))
            {
                return null;
            }

            using var req = new HttpRequestMessage(
                HttpMethod.Get,
                $"http://127.0.0.1:{port}/v1/pending?composerId={Uri.EscapeDataString(conversationId)}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var res = await Http.SendAsync(req).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
            {
                return null;
            }

            if (!root.TryGetProperty("pending", out var pending) || pending.ValueKind != JsonValueKind.Object)
            {
                return new CompanionPending { Kind = PendingDecisionKind.None };
            }

            var kindStr = pending.TryGetProperty("kind", out var k) ? k.GetString() ?? "" : "";
            var kind = kindStr.ToLowerInvariant() switch
            {
                "shell" => PendingDecisionKind.Shell,
                "mcp" => PendingDecisionKind.Mcp,
                "webfetch" or "web_fetch" or "fetch" => PendingDecisionKind.WebFetch,
                "switchmode" or "switch_mode" or "switch" => PendingDecisionKind.SwitchMode,
                _ => PendingDecisionKind.None,
            };

            return new CompanionPending
            {
                Kind = kind,
                Prompt = pending.TryGetProperty("prompt", out var p) ? p.GetString() ?? "" : "",
                TargetMode = pending.TryGetProperty("targetMode", out var t) ? t.GetString() : null,
            };
        }

        private static void PostFireAndForget(String path, String? conversationId, String label, Object? body)
        {
            if (String.IsNullOrWhiteSpace(conversationId))
            {
                return;
            }

            _ = Task.Run(async () =>
            {
                await PostAsync(path, conversationId, label, body).ConfigureAwait(false);
            });
        }

        private static async Task<Boolean> PostAsync(String path, String? conversationId, String label, Object? body)
        {
            if (String.IsNullOrWhiteSpace(conversationId))
            {
                return false;
            }

            try
            {
                if (!TryReadDiscovery(out var port, out var token))
                {
                    PluginLog.Warning("[companion] discovery missing");
                    return false;
                }

                using var req = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}{path}");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var payload = body ?? new { composerId = conversationId, conversationId };
                req.Content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json");

                using var res = await Http.SendAsync(req).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode)
                {
                    var text = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                    PluginLog.Warning($"[companion] {label} HTTP {(Int32)res.StatusCode}: {text}");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                PluginLog.Warning($"[companion] {label} failed: {ex.Message}");
                return false;
            }
        }

        private static Boolean TryReadDiscovery(out Int32 port, out String token)
        {
            port = 0;
            token = String.Empty;
            try
            {
                if (!File.Exists(DiscoveryPath))
                {
                    return false;
                }

                using var doc = JsonDocument.Parse(File.ReadAllText(DiscoveryPath));
                var root = doc.RootElement;
                if (!root.TryGetProperty("port", out var portEl) || !root.TryGetProperty("token", out var tokenEl))
                {
                    return false;
                }

                port = portEl.GetInt32();
                token = tokenEl.GetString() ?? String.Empty;
                return port > 0 && !String.IsNullOrEmpty(token);
            }
            catch
            {
                return false;
            }
        }
    }

    public sealed class OpenChatsSnapshot
    {
        public static OpenChatsSnapshot Empty { get; } = new();

        public String[] Used { get; init; } = Array.Empty<String>();
        public String[] Unused { get; init; } = Array.Empty<String>();
    }

    internal sealed class CompanionPending
    {
        public PendingDecisionKind Kind { get; init; }
        public String Prompt { get; init; } = String.Empty;
        public String? TargetMode { get; init; }
    }
}
