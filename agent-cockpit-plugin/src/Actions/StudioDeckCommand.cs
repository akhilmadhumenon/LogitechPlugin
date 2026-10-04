namespace Loupedeck.AgentCockpitPlugin.Actions
{
    using System;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Loupedeck.AgentCockpitPlugin;

    /// <summary>Page 4 — nine assignable usage / mode / review keys.</summary>
    public class StudioDeckCommand : PluginDynamicCommand
    {
        private const Int32 PickerTimeoutMs = 25000;

        private static readonly String[] Names =
        {
            "Usage", "Mode", "Model", "Hunk −", "Hunk +", "Accept hunk", "Undo", "Redo", "Problems",
        };

        /// <summary>0 = closed, 1 = Fast/Effort/Model menu, 2 = Model or Effort submenu.</summary>
        private static volatile Int32 _pickerDepth;
        private Timer? _pickerTimer;

        public StudioDeckCommand()
            : base()
        {
            this.DisplayName = "Studio";
            this.Description = "Page 4: usage, mode, model, and review stand-ins";
            this.GroupName = "Cursor · Studio";

            for (var i = 1; i <= 9; i++)
            {
                this.AddParameter(
                    i.ToString(CultureInfo.InvariantCulture),
                    Names[i - 1],
                    "Cursor · Studio");
            }

            UsageStore.Instance.Changed += this.OnChanged;
            AgentSlotStore.Instance.Changed += this.OnChanged;
        }

        protected override Boolean OnUnload()
        {
            UsageStore.Instance.Changed -= this.OnChanged;
            AgentSlotStore.Instance.Changed -= this.OnChanged;
            this._pickerTimer?.Dispose();
            this._pickerTimer = null;
            _pickerDepth = 0;
            return base.OnUnload();
        }

        protected override void RunCommand(String actionParameter)
        {
            var index = SlotIndex(actionParameter);
            if (_pickerDepth > 0 && this.HandlePickerOverlay(index))
            {
                return;
            }

            switch (index)
            {
                case 0:
                    this.CycleUsage(actionParameter);
                    return;
                case 1:
                    StudioCursor.CycleMode();
                    break;
                case 2:
                    this.OpenModelPicker();
                    return;
                case 3:
                    KeystrokeSender.SendSequenceAsync(new[]
                    {
                        new Keystroke { Key = "f7", Modifiers = new[] { "shift" } },
                    });
                    break;
                case 4:
                    KeystrokeSender.SendSequenceAsync(new[] { new Keystroke { Key = "f7" } });
                    break;
                case 5:
                    KeystrokeSender.SendSequenceAsync(new[]
                    {
                        new Keystroke { Key = "enter", Modifiers = new[] { "command" } },
                    });
                    break;
                case 6:
                    KeystrokeSender.SendSequenceAsync(new[]
                    {
                        new Keystroke { Key = "z", Modifiers = new[] { "command" } },
                    });
                    break;
                case 7:
                    Task.Run(async () =>
                    {
                        if (!await CompanionClient.ExecuteCommandAsync("workbench.action.redo").ConfigureAwait(false)
                            && !await CompanionClient.ExecuteCommandAsync("redo").ConfigureAwait(false))
                        {
                            KeystrokeSender.SendSequence(new[]
                            {
                                new Keystroke { Key = "y", Modifiers = new[] { "command" } },
                            });
                        }
                    });
                    break;
                case 8:
                    StudioCursor.OpenProblems();
                    break;
            }

            this.ActionImageChanged(actionParameter);
        }

        private Boolean HandlePickerOverlay(Int32 index)
        {
            switch (index)
            {
                case 1:
                    this.TouchPickerTimeout();
                    StudioCursor.CycleEffort();
                    return true;
                case 2:
                    this.ExitPickerToChat();
                    return true;
                case 3:
                    this.TouchPickerTimeout();
                    KeystrokeSender.SendSequenceAsync(new[] { new Keystroke { Key = "up" } }, focus: false);
                    return true;
                case 4:
                    this.TouchPickerTimeout();
                    KeystrokeSender.SendSequenceAsync(new[] { new Keystroke { Key = "down" } }, focus: false);
                    return true;
                case 5:
                    this.SelectInPicker();
                    return true;
                default:
                    return false;
            }
        }

        private void OpenModelPicker()
        {
            StudioCursor.OpenModelPicker();
            this.SetPickerDepth(1);
        }

        private void SelectInPicker()
        {
            StudioCursor.ConfirmPickerSelection();
            if (_pickerDepth == 1)
            {
                // Enter on Model/Effort opens the submenu; keep keypad titles.
                this.SetPickerDepth(2);
                return;
            }

            this.SetPickerDepth(0);
        }

        private void ExitPickerToChat()
        {
            StudioCursor.DismissPickerAndFocusChat();
            this.SetPickerDepth(0);
        }

        private void SetPickerDepth(Int32 depth)
        {
            _pickerDepth = Math.Clamp(depth, 0, 2);
            if (_pickerDepth == 0)
            {
                this._pickerTimer?.Dispose();
                this._pickerTimer = null;
            }
            else
            {
                this.TouchPickerTimeout();
            }

            this.RefreshStudioKeys();
        }

        private void TouchPickerTimeout()
        {
            if (_pickerDepth == 0)
            {
                return;
            }

            this._pickerTimer?.Dispose();
            this._pickerTimer = new Timer(
                _ =>
                {
                    _pickerDepth = 0;
                    this.RefreshStudioKeys();
                },
                null,
                PickerTimeoutMs,
                Timeout.Infinite);
        }

        private void RefreshStudioKeys()
        {
            this.ActionImageChanged();
            for (var i = 1; i <= 9; i++)
            {
                this.ActionImageChanged(i.ToString(CultureInfo.InvariantCulture));
            }
        }

        private void CycleUsage(String actionParameter)
        {
            var view = UsageStore.Instance.CycleView();
            var lcd = UsageStore.Instance.FormatLcd();
            PluginLog.Info($"[usage] cycle → {view} {lcd.title}={lcd.value}");
            this.ActionImageChanged("1");
            this.ActionImageChanged(actionParameter);
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize)
        {
            var overlay = OverlayTitle(SlotIndex(actionParameter));
            return overlay ?? String.Empty;
        }

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var index = SlotIndex(actionParameter);
            if (index == 0)
            {
                var lcd = UsageStore.Instance.FormatLcd();
                return CockpitTiles.RenderUsageTile(
                    imageSize,
                    lcd.title,
                    lcd.value,
                    UsageStore.Instance.ColorForCurrentView());
            }

            var overlayIcon = OverlayIcon(index);
            if (overlayIcon != null)
            {
                return CockpitTiles.RenderDeckTile(
                    imageSize,
                    overlayIcon.Value,
                    String.Empty,
                    null,
                    CockpitTiles.DeckLive);
            }

            var (title, sub, live) = Describe(index);
            var icon = index switch
            {
                1 => CockpitTiles.DeckIcon.Mode,
                2 => CockpitTiles.DeckIcon.Model,
                3 => CockpitTiles.DeckIcon.HunkPrev,
                4 => CockpitTiles.DeckIcon.HunkNext,
                5 => CockpitTiles.DeckIcon.Accept,
                6 => CockpitTiles.DeckIcon.Undo,
                7 => CockpitTiles.DeckIcon.Redo,
                _ => CockpitTiles.DeckIcon.Problems,
            };
            return CockpitTiles.RenderDeckTile(
                imageSize,
                icon,
                title,
                sub,
                live ? CockpitTiles.DeckLive : CockpitTiles.Deck);
        }

        private void OnChanged() => this.RefreshStudioKeys();

        private static String? OverlayTitle(Int32 index)
        {
            if (_pickerDepth <= 0)
            {
                return null;
            }

            return index switch
            {
                1 => "Effort",
                2 => "Exit",
                3 => "Up",
                4 => "Down",
                5 => "Select",
                _ => null,
            };
        }

        private static CockpitTiles.DeckIcon? OverlayIcon(Int32 index)
        {
            if (_pickerDepth <= 0)
            {
                return null;
            }

            return index switch
            {
                1 => CockpitTiles.DeckIcon.Effort,
                2 => CockpitTiles.DeckIcon.Exit,
                3 => CockpitTiles.DeckIcon.ArrowUp,
                4 => CockpitTiles.DeckIcon.ArrowDown,
                5 => CockpitTiles.DeckIcon.Accept,
                _ => null,
            };
        }

        private static Int32 SlotIndex(String? actionParameter) =>
            Int32.TryParse(actionParameter, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
                ? Math.Clamp(i - 1, 0, 8)
                : 0;

        private static (String title, String? sub, Boolean live) Describe(Int32 index)
        {
            var settings = SettingsStore.Instance.Current;
            switch (index)
            {
                case 0:
                    var lcd = UsageStore.Instance.FormatLcd();
                    return (lcd.title, lcd.value, true);
                case 1:
                    return ("Mode", CurrentModeLabel(settings), true);
                case 2:
                    return ("Model", CurrentModelLabel(settings), true);
                case 3:
                    return ("Hunk −", "⇧F7", false);
                case 4:
                    return ("Hunk +", "F7", false);
                case 5:
                    return ("Accept", "hunk", false);
                case 6:
                    return ("Undo", "⌘Z", false);
                case 7:
                    return ("Redo", "⇧⌘Z", false);
                case 8:
                    return ("Problems", "F8", false);
                default:
                    return ("—", null, false);
            }
        }

        private static String CurrentModeLabel(ProductivitySettings settings)
        {
            var live = UsageStore.Instance.LastMode;
            if (!String.IsNullOrEmpty(live) && live != "unknown")
            {
                return Title(live);
            }

            if (settings.Modes.Count == 0)
            {
                return "—";
            }

            var idx = UsageStore.Instance.ModeIndex % settings.Modes.Count;
            return settings.Modes[idx].Label;
        }

        private static String CurrentModelLabel(ProductivitySettings settings)
        {
            var live = UsageStore.Instance.LastModel;
            if (!String.IsNullOrEmpty(live))
            {
                return UsageStore.ShortModel(live);
            }

            if (settings.Models.Count == 0)
            {
                return "—";
            }

            var idx = UsageStore.Instance.ModelIndex % settings.Models.Count;
            return settings.Models[idx].Label;
        }

        private static String Title(String value) =>
            value.Length == 0 ? value : Char.ToUpperInvariant(value[0]) + value.Substring(1);
    }

    internal static class StudioCursor
    {
        public static void CycleMode() =>
            Task.Run(async () =>
            {
                await CompanionClient.FocusTargetAsync(ComposerTarget.Agent).ConfigureAwait(false);
                if (!await CompanionClient.ExecuteCommandAsync("composer.cycleMode").ConfigureAwait(false))
                {
                    await CompanionClient.ExecuteCommandAsync("composer.openModeMenu").ConfigureAwait(false);
                }
            });

        public static void OpenModelPicker() =>
            Task.Run(async () =>
            {
                await CompanionClient.FocusTargetAsync(ComposerTarget.Agent).ConfigureAwait(false);
                if (!await CompanionClient.ExecuteCommandAsync("composer.openModelToggle").ConfigureAwait(false)
                    && !await CompanionClient.ExecuteCommandAsync("glass.openModelPicker").ConfigureAwait(false))
                {
                    await CompanionClient.ExecuteCommandAsync("composer.cycleModel").ConfigureAwait(false);
                }
            });

        public static void DismissPickerAndFocusChat() =>
            Task.Run(async () =>
            {
                var escape = new[] { new Keystroke { Key = "escape" } };
                KeystrokeSender.SendSequence(escape);
                Thread.Sleep(100);
                KeystrokeSender.SendSequence(escape, focus: false);
                Thread.Sleep(100);
                KeystrokeSender.SendSequence(escape, focus: false);
                await CompanionClient.FocusTargetAsync(ComposerTarget.Agent).ConfigureAwait(false);
            });

        public static void ConfirmPickerSelection() =>
            Task.Run(async () =>
            {
                if (!await CompanionClient.ExecuteCommandAsync("list.select").ConfigureAwait(false)
                    && !await CompanionClient.ExecuteCommandAsync("workbench.action.acceptSelectedQuickOpenItem").ConfigureAwait(false))
                {
                    KeystrokeSender.SendSequence(new[] { new Keystroke { Key = "enter" } }, focus: true);
                }
            });

        public static void CycleEffort() =>
            Task.Run(async () =>
            {
                if (!await CompanionClient.ExecuteCommandAsync("composer.cycleModelParameter").ConfigureAwait(false)
                    && !await CompanionClient.ExecuteCommandAsync("glass.cycleModelParameter").ConfigureAwait(false))
                {
                    KeystrokeSender.SendSequence(new[]
                    {
                        new Keystroke { Key = "/", Modifiers = new[] { "command", "shift" } },
                    });
                }
            });

        public static void OpenProblems() =>
            Task.Run(async () =>
            {
                if (!await CompanionClient.ExecuteCommandAsync("workbench.actions.view.problems").ConfigureAwait(false)
                    && !await CompanionClient.ExecuteCommandAsync("workbench.panel.markers.view.focus").ConfigureAwait(false))
                {
                    KeystrokeSender.SendSequence(new[]
                    {
                        new Keystroke { Key = "m", Modifiers = new[] { "command", "shift" } },
                    });
                }
            });
    }
}
