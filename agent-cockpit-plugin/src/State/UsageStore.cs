namespace Loupedeck.AgentCockpitPlugin
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Daily AI usage, persisted next to Node metrics at ~/.cursor-agent-cockpit/usage-metrics.json.
    /// </summary>
    internal sealed class UsageStore
    {
        public static UsageStore Instance { get; } = new UsageStore();

        public static String MetricsPath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cursor-agent-cockpit",
            "usage-metrics.json");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        };

        private readonly Object _gate = new();
        private DailyUsage _day = EmptyDay();
        private UsageMetricView _view = UsageMetricView.Prompts;
        private readonly List<String> _recentModels = new();
        private String? _lastMode;
        private Int32 _modeIndex;
        private Int32 _modelIndex;

        public event Action? Changed;

        public void Load()
        {
            lock (this._gate)
            {
                try
                {
                    if (!File.Exists(MetricsPath))
                    {
                        return;
                    }

                    using var doc = JsonDocument.Parse(File.ReadAllText(MetricsPath));
                    var root = doc.RootElement;
                    if (root.TryGetProperty("day", out var dayEl))
                    {
                        var parsed = JsonSerializer.Deserialize<DailyUsage>(dayEl.GetRawText(), JsonOptions);
                        if (parsed != null && parsed.Date == TodayKey())
                        {
                            this._day = parsed;
                        }
                    }

                    if (root.TryGetProperty("recentModels", out var models) && models.ValueKind == JsonValueKind.Array)
                    {
                        this._recentModels.Clear();
                        foreach (var m in models.EnumerateArray())
                        {
                            var s = m.GetString();
                            if (!String.IsNullOrEmpty(s))
                            {
                                this._recentModels.Add(s);
                            }
                        }
                    }

                    if (root.TryGetProperty("view", out var viewEl))
                    {
                        var view = viewEl.GetString();
                        this._view = view?.ToLowerInvariant() switch
                        {
                            "tools" => UsageMetricView.Tools,
                            "model" => UsageMetricView.Model,
                            "errors" => UsageMetricView.Errors,
                            "sessions" => UsageMetricView.Sessions,
                            _ => UsageMetricView.Prompts,
                        };
                    }
                }
                catch (Exception ex)
                {
                    PluginLog.Warning($"[usage] load failed: {ex.Message}");
                }
            }
        }

        public void Observe(String eventName, String? model, String? mode, String? status)
        {
            var changed = false;
            lock (this._gate)
            {
                this.RollDayUnlocked();
                if (!String.IsNullOrEmpty(model))
                {
                    this.TrackModelUnlocked(model);
                    changed = true;
                }

                if (!String.IsNullOrEmpty(mode) && mode != "unknown")
                {
                    this._lastMode = mode;
                    changed = true;
                }

                switch (eventName)
                {
                    case "beforeSubmitPrompt":
                        this._day.Prompts += 1;
                        changed = true;
                        break;
                    case "preToolUse":
                        this._day.ToolCalls += 1;
                        changed = true;
                        break;
                    case "sessionStart":
                        this._day.Sessions += 1;
                        changed = true;
                        break;
                    case "postToolUseFailure":
                        this._day.Errors += 1;
                        changed = true;
                        break;
                    case "stop":
                        this._day.CompletedRuns += 1;
                        if (!String.IsNullOrEmpty(status)
                            && (status.Equals("error", StringComparison.OrdinalIgnoreCase)
                                || status.Equals("failed", StringComparison.OrdinalIgnoreCase)
                                || status.Equals("aborted", StringComparison.OrdinalIgnoreCase)))
                        {
                            this._day.Errors += 1;
                        }

                        changed = true;
                        break;
                }

                if (changed)
                {
                    this.PersistUnlocked();
                }
            }

            if (changed)
            {
                this.Changed?.Invoke();
            }
        }

        public UsageMetricView CycleView()
        {
            UsageMetricView view;
            lock (this._gate)
            {
                this._view = this._view switch
                {
                    UsageMetricView.Prompts => UsageMetricView.Tools,
                    UsageMetricView.Tools => UsageMetricView.Model,
                    UsageMetricView.Model => UsageMetricView.Errors,
                    UsageMetricView.Errors => UsageMetricView.Sessions,
                    _ => UsageMetricView.Prompts,
                };
                view = this._view;
                this.PersistUnlocked();
            }

            this.Changed?.Invoke();
            return view;
        }

        public Object SnapshotHealth()
        {
            lock (this._gate)
            {
                this.RollDayUnlocked();
                return new
                {
                    view = this._view.ToString(),
                    prompts = this._day.Prompts,
                    tools = this._day.ToolCalls,
                    errors = this._day.Errors,
                    sessions = this._day.Sessions,
                    model = this.LastModelUnlocked(),
                };
            }
        }

        public (String title, String value) FormatLcd()
        {
            lock (this._gate)
            {
                this.RollDayUnlocked();
                return this._view switch
                {
                    UsageMetricView.Tools => ("Tools", this._day.ToolCalls.ToString()),
                    UsageMetricView.Model => ("Model", ShortModel(this.LastModelUnlocked())),
                    UsageMetricView.Errors => ("Errors", this._day.Errors.ToString()),
                    UsageMetricView.Sessions => ("Sessions", this._day.Sessions.ToString()),
                    _ => ("Prompts", this._day.Prompts.ToString()),
                };
            }
        }

        public String? LastModel
        {
            get { lock (this._gate) { return this.LastModelUnlocked(); } }
        }

        public String? LastMode
        {
            get { lock (this._gate) { return this._lastMode; } }
        }

        public Int32 NextModeIndex(Int32 count)
        {
            if (count <= 0)
            {
                return 0;
            }

            lock (this._gate)
            {
                this._modeIndex = (this._modeIndex + 1) % count;
                return this._modeIndex;
            }
        }

        public Int32 NextModelIndex(Int32 count)
        {
            if (count <= 0)
            {
                return 0;
            }

            lock (this._gate)
            {
                this._modelIndex = (this._modelIndex + 1) % count;
                return this._modelIndex;
            }
        }

        public Int32 ModeIndex
        {
            get { lock (this._gate) { return this._modeIndex; } }
        }

        public Int32 ModelIndex
        {
            get { lock (this._gate) { return this._modelIndex; } }
        }

        private String? LastModelUnlocked() =>
            this._recentModels.Count > 0 ? this._recentModels[0] : null;

        private void TrackModelUnlocked(String model)
        {
            this._recentModels.Remove(model);
            this._recentModels.Insert(0, model);
            if (this._recentModels.Count > 8)
            {
                this._recentModels.RemoveRange(8, this._recentModels.Count - 8);
            }
        }

        private void RollDayUnlocked()
        {
            var key = TodayKey();
            if (this._day.Date != key)
            {
                this._day = EmptyDay(key);
            }
        }

        private void PersistUnlocked()
        {
            try
            {
                var dir = Path.GetDirectoryName(MetricsPath);
                if (!String.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var payload = new
                {
                    day = this._day,
                    recentModels = this._recentModels,
                    view = this._view.ToString().ToLowerInvariant(),
                };
                File.WriteAllText(MetricsPath, JsonSerializer.Serialize(payload, JsonOptions));
            }
            catch (Exception ex)
            {
                PluginLog.Warning($"[usage] persist failed: {ex.Message}");
            }
        }

        private static DailyUsage EmptyDay(String? date = null) =>
            new() { Date = date ?? TodayKey() };

        private static String TodayKey() => DateTime.UtcNow.ToString("yyyy-MM-dd");

        internal static String ShortModel(String? model)
        {
            if (String.IsNullOrEmpty(model))
            {
                return "—";
            }

            var cleaned = model
                .Replace("claude-", "", StringComparison.OrdinalIgnoreCase)
                .Replace("gpt-", "", StringComparison.OrdinalIgnoreCase);
            var cut = cleaned.IndexOf("-thinking", StringComparison.OrdinalIgnoreCase);
            if (cut >= 0)
            {
                cleaned = cleaned.Substring(0, cut);
            }

            return cleaned.Length > 12 ? cleaned.Substring(0, 11) + "…" : cleaned;
        }
    }
}
