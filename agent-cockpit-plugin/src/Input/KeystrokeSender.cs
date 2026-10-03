namespace Loupedeck.AgentCockpitPlugin
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    internal static class KeystrokeSender
    {
        public static void FocusCursorAndSendEscapeCancel()
        {
            if (!OperatingSystem.IsMacOS())
            {
                PluginLog.Warning("[keys] Kill chord only implemented for macOS");
                return;
            }

            RunOsascript("""
                tell application "Cursor" to activate
                delay 0.12
                tell application "System Events"
                  key code 53
                  keystroke "." using command down
                end tell
                """);
        }

        public static void SendSequence(IReadOnlyList<Keystroke> sequence, Boolean focus = true)
        {
            if (sequence == null || sequence.Count == 0)
            {
                return;
            }

            if (!OperatingSystem.IsMacOS())
            {
                PluginLog.Warning("[keys] sequences only implemented for macOS");
                return;
            }

            if (focus)
            {
                FocusCursor();
            }

            var lines = sequence.Select(ToAppleScript).Where(s => s.Length > 0);
            RunOsascript($"""
                tell application "System Events"
                  {String.Join("\n  ", lines)}
                end tell
                """);
        }

        public static void TypeText(String text, Boolean submit = false, Boolean focus = false)
        {
            if (!OperatingSystem.IsMacOS() || String.IsNullOrEmpty(text))
            {
                return;
            }

            if (focus)
            {
                FocusCursor();
            }

            RunOsascript($"""
                set oldClip to the clipboard
                set the clipboard to "{EscapeAppleScript(text)}"
                tell application "System Events"
                  keystroke "a" using command down
                  delay 0.05
                  keystroke "v" using command down
                end tell
                delay 0.12
                set the clipboard to oldClip
                """);

            if (submit)
            {
                Thread.Sleep(80);
                SendSubmit();
            }
        }

        public static void OpenComposerTarget(ComposerTarget target)
        {
            if (CompanionClient.FocusTargetAsync(target).GetAwaiter().GetResult())
            {
                Thread.Sleep(160);
                return;
            }

            // Do not send Cmd+L / Cmd+I — those toggle and close an already-open pane.
            if (target == ComposerTarget.Inline)
            {
                if (!CompanionClient.ExecuteCommandAsync("aipopup.action.modal.generate").GetAwaiter().GetResult()
                    && !CompanionClient.ExecuteCommandAsync("aipopup.action.focusEdit").GetAwaiter().GetResult())
                {
                    FocusCursor();
                    SendSequence(new[] { new Keystroke { Key = "k", Modifiers = new[] { "command" } } }, focus: false);
                }

                Thread.Sleep(180);
                return;
            }

            var mode = target == ComposerTarget.Chat ? "composerMode.chat" : "composerMode.agent";
            CompanionClient.ExecuteCommandAsync(mode).GetAwaiter().GetResult();
            if (!CompanionClient.ExecuteCommandAsync("composer.open_chat_sidebar").GetAwaiter().GetResult()
                && !CompanionClient.ExecuteCommandAsync("composer.openAsPane").GetAwaiter().GetResult()
                && !CompanionClient.ExecuteCommandAsync("composer.openComposer").GetAwaiter().GetResult())
            {
                FocusCursor();
            }

            Thread.Sleep(160);
        }

        private static Int32 _promptTicket;
        private static readonly Object PromptGate = new();

        public static void FirePrompt(PromptShortcut shortcut)
        {
            var ticket = Interlocked.Increment(ref _promptTicket);
            Task.Run(() =>
            {
                lock (PromptGate)
                {
                    try
                    {
                        if (ticket != _promptTicket)
                        {
                            return;
                        }

                        shortcut.Target = ComposerTarget.Agent;
                        var insertOnly = new PromptShortcut
                        {
                            Id = shortcut.Id,
                            Label = shortcut.Label,
                            Prompt = shortcut.Prompt,
                            Target = ComposerTarget.Agent,
                            Submit = false,
                        };
                        var inserted = CompanionClient.InsertPromptAsync(insertOnly).GetAwaiter().GetResult();
                        if (!inserted)
                        {
                            OpenComposerTarget(ComposerTarget.Agent);
                            TypeText(shortcut.Prompt, submit: false, focus: false);
                        }

                        if (ticket != _promptTicket)
                        {
                            return;
                        }

                        if (shortcut.Submit)
                        {
                            Thread.Sleep(180);
                            SendSubmit();
                        }
                    }
                    catch (Exception ex)
                    {
                        PluginLog.Warning($"[keys] prompt failed: {ex.Message}");
                    }
                }
            });
        }

        private static void SendSubmit() =>
            SendSequence(
                new[] { new Keystroke { Key = "enter", Modifiers = new[] { "command" } } },
                focus: false);

        public static void FocusTarget(ComposerTarget target) =>
            Task.Run(() =>
            {
                try
                {
                    OpenComposerTarget(target);
                }
                catch (Exception ex)
                {
                    PluginLog.Warning($"[keys] focus failed: {ex.Message}");
                }
            });

        public static void SendSequenceAsync(IReadOnlyList<Keystroke> sequence, Boolean focus = true) =>
            Task.Run(() =>
            {
                try
                {
                    SendSequence(sequence, focus);
                }
                catch (Exception ex)
                {
                    PluginLog.Warning($"[keys] sequence failed: {ex.Message}");
                }
            });

        private static void FocusCursor()
        {
            if (!OperatingSystem.IsMacOS())
            {
                return;
            }

            RunOsascript("""
                tell application "Cursor" to activate
                """);
            Thread.Sleep(120);
        }

        private static String ToAppleScript(Keystroke stroke)
        {
            var key = (stroke.Key ?? "").Trim();
            if (key.Length == 0)
            {
                return String.Empty;
            }

            var mods = (stroke.Modifiers ?? Array.Empty<String>())
                .Select(NormalizeModifier)
                .Where(m => m.Length > 0)
                .ToArray();
            var usingClause = mods.Length == 0 ? "" : $" using {{{String.Join(", ", mods.Select(m => $"{m} down"))}}}";

            var special = key.ToLowerInvariant() switch
            {
                "enter" or "return" => "keystroke return",
                "escape" or "esc" => "key code 53",
                "tab" => "keystroke tab",
                "space" => "keystroke space",
                "up" => "key code 126",
                "down" => "key code 125",
                "left" => "key code 123",
                "right" => "key code 124",
                "delete" => "key code 51",
                "f7" => "key code 98",
                "f8" => "key code 100",
                "`" => "key code 50",
                _ => null,
            };

            if (special != null)
            {
                return special + usingClause;
            }

            return $"keystroke \"{EscapeAppleScript(key)}\"{usingClause}";
        }

        private static String NormalizeModifier(String raw) =>
            raw.Trim().ToLowerInvariant() switch
            {
                "command" or "cmd" or "meta" => "command",
                "control" or "ctrl" => "control",
                "option" or "alt" => "option",
                "shift" => "shift",
                _ => "",
            };

        private static String EscapeAppleScript(String value) =>
            value.Replace("\\", "\\\\").Replace("\"", "\\\"");

        private static void RunOsascript(String source)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "/usr/bin/osascript",
                    ArgumentList = { "-e", source },
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var process = Process.Start(psi);
                process?.WaitForExit(3000);
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "[keys] osascript failed");
            }
        }
    }
}
