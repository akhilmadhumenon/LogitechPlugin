namespace Loupedeck.AgentCockpitPlugin
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Reads ~/.cursor-agent-cockpit/settings.json.
    /// </summary>
    internal sealed class SettingsStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        };

        public static SettingsStore Instance { get; } = new SettingsStore();

        public static String SettingsPath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cursor-agent-cockpit",
            "settings.json");

        private readonly Object _gate = new();
        private ProductivitySettings _settings = CreateDefaults();
        private DateTime _loadedAt = DateTime.MinValue;

        public ProductivitySettings Current
        {
            get
            {
                this.EnsureFresh();
                lock (this._gate)
                {
                    return this._settings;
                }
            }
        }

        public void Ensure()
        {
            this.EnsureFresh(force: true);
        }

        private void EnsureFresh(Boolean force = false)
        {
            lock (this._gate)
            {
                try
                {
                    var dir = Path.GetDirectoryName(SettingsPath);
                    if (!String.IsNullOrEmpty(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    if (!File.Exists(SettingsPath))
                    {
                        this._settings = CreateDefaults();
                        File.WriteAllText(
                            SettingsPath,
                            JsonSerializer.Serialize(this._settings, new JsonSerializerOptions
                            {
                                WriteIndented = true,
                                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                                Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
                            }));
                        this._loadedAt = DateTime.UtcNow;
                        return;
                    }

                    var write = File.GetLastWriteTimeUtc(SettingsPath);
                    if (!force && write <= this._loadedAt)
                    {
                        return;
                    }

                    var raw = File.ReadAllText(SettingsPath);
                    var parsed = JsonSerializer.Deserialize<ProductivitySettings>(raw, JsonOptions);
                    this._settings = Merge(parsed);
                    this._loadedAt = write;
                }
                catch (Exception ex)
                {
                    PluginLog.Warning($"[settings] load failed: {ex.Message}");
                    if (this._settings.KeyboardShortcuts.Count == 0)
                    {
                        this._settings = CreateDefaults();
                    }
                }
            }
        }

        private static ProductivitySettings Merge(ProductivitySettings? raw)
        {
            var defaults = CreateDefaults();
            if (raw == null)
            {
                return defaults;
            }

            if (raw.KeyboardShortcuts.Count == 0)
            {
                raw.KeyboardShortcuts = defaults.KeyboardShortcuts;
            }

            if (raw.PromptShortcuts.Count == 0)
            {
                raw.PromptShortcuts = defaults.PromptShortcuts;
            }
            else
            {
                foreach (var def in defaults.PromptShortcuts)
                {
                    if (!raw.PromptShortcuts.Exists(p =>
                            String.Equals(p.Id, def.Id, StringComparison.OrdinalIgnoreCase)))
                    {
                        raw.PromptShortcuts.Add(def);
                    }
                }
            }

            if (raw.Models.Count == 0)
            {
                raw.Models = defaults.Models;
            }

            if (raw.Modes.Count == 0)
            {
                raw.Modes = defaults.Modes;
            }

            if (raw.TokenUsage == null || raw.TokenUsage.Warn <= 0 || raw.TokenUsage.High <= 0)
            {
                raw.TokenUsage = defaults.TokenUsage;
            }

            return raw;
        }

        internal static ProductivitySettings CreateDefaults() => new()
        {
            TokenUsage = new TokenUsageSettings(),
            KeyboardShortcuts = new List<KeyboardShortcut>
            {
                Chord("command-palette", "Cmd Palette", "p", "command", "shift"),
                Chord("quick-open", "Quick Open", "p", "command"),
                Chord("toggle-sidebar", "Sidebar", "b", "command"),
                Chord("toggle-terminal", "Terminal", "`", "control"),
            },
            PromptShortcuts = new List<PromptShortcut>
            {
                Prompt("write-tests", "Tests", ComposerTarget.Agent,
                    "Write thorough unit tests for the current file. Cover edge cases."),
                Prompt("explain", "Explain", ComposerTarget.Agent,
                    "Explain the selected code and its responsibilities."),
                Prompt("add-types", "Types", ComposerTarget.Agent,
                    "Add precise TypeScript types to the current file without changing behavior."),
                Prompt("refactor", "Refactor", ComposerTarget.Agent,
                    "Refactor the selection for clarity and maintainability. Keep behavior identical."),
                Prompt("review", "Review", ComposerTarget.Agent,
                    "Review the current file for bugs, edge cases, and risky assumptions. Be specific."),
                Prompt("fix", "Fix", ComposerTarget.Agent,
                    "Fix the selected problem or the most recent error. Keep the change minimal."),
            },
            Models = new List<NamedSequence>
            {
                new() { Id = "auto", Label = "Auto" },
                new() { Id = "sonnet", Label = "Sonnet" },
                new() { Id = "opus", Label = "Opus" },
            },
            Modes = new List<NamedSequence>
            {
                new()
                {
                    Id = "agent",
                    Label = "Agent",
                    SelectSequence = new List<Keystroke> { Stroke("i", "command") },
                },
                new()
                {
                    Id = "ask",
                    Label = "Ask",
                    SelectSequence = new List<Keystroke> { Stroke("l", "command") },
                },
                new()
                {
                    Id = "edit",
                    Label = "Edit",
                    SelectSequence = new List<Keystroke> { Stroke("k", "command") },
                },
            },
        };

        private static KeyboardShortcut Chord(String id, String label, String key, params String[] modifiers) =>
            new()
            {
                Id = id,
                Label = label,
                Sequence = new List<Keystroke> { Stroke(key, modifiers) },
            };

        private static PromptShortcut Prompt(String id, String label, ComposerTarget target, String text) =>
            new()
            {
                Id = id,
                Label = label,
                Target = target,
                Prompt = text,
                Submit = true,
            };

        private static Keystroke Stroke(String key, params String[] modifiers) =>
            new() { Key = key, Modifiers = modifiers };
    }
}
