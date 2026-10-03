namespace Loupedeck.AgentCockpitPlugin.Actions
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using Loupedeck.AgentCockpitPlugin;

    /// <summary>Page 3 — nine assignable chord / context keys.</summary>
    public class ShortcutDeckCommand : PluginDynamicCommand
    {
        private static Boolean _paletteOpen;
        private static Boolean _quickOpenOpen;

        private static readonly String[] Names =
        {
            "Cmd Palette", "Quick Open", "Sidebar", "Terminal", "Accept", "Reject", "@file", "@sel", "@diff",
        };

        public ShortcutDeckCommand()
            : base()
        {
            this.DisplayName = "Shortcuts";
            this.Description = "Page 3: purpose-labeled chords and context inserts";
            this.GroupName = "Cursor · Shortcuts";

            for (var i = 1; i <= 9; i++)
            {
                this.AddParameter(
                    i.ToString(CultureInfo.InvariantCulture),
                    Names[i - 1],
                    "Cursor · Shortcuts");
            }
        }

        protected override void RunCommand(String actionParameter)
        {
            var tile = TileAt(SlotIndex(actionParameter));
            if (tile.Empty)
            {
                return;
            }

            if (tile.Mention != null)
            {
                KeystrokeSender.FirePrompt(new PromptShortcut
                {
                    Label = tile.Label,
                    Prompt = tile.Mention,
                    Target = ComposerTarget.Agent,
                    Submit = false,
                });
                return;
            }

            var index = SlotIndex(actionParameter);
            if (index == 0)
            {
                ToggleOverlay(ref _paletteOpen, tile.Sequence);
                return;
            }

            if (index == 1)
            {
                ToggleOverlay(ref _quickOpenOpen, tile.Sequence);
                return;
            }

            if (tile.Sequence.Count > 0)
            {
                KeystrokeSender.SendSequenceAsync(tile.Sequence);
            }
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) =>
            String.Empty;

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var index = SlotIndex(actionParameter);
            var tile = TileAt(index);
            var icon = index switch
            {
                0 => CockpitTiles.DeckIcon.Palette,
                1 => CockpitTiles.DeckIcon.QuickOpen,
                2 => CockpitTiles.DeckIcon.Sidebar,
                3 => CockpitTiles.DeckIcon.Terminal,
                4 => CockpitTiles.DeckIcon.Accept,
                5 => CockpitTiles.DeckIcon.Reject,
                6 => CockpitTiles.DeckIcon.AtFile,
                7 => CockpitTiles.DeckIcon.AtSel,
                _ => CockpitTiles.DeckIcon.AtDiff,
            };
            var bg = index switch
            {
                4 => CockpitTiles.Approve,
                5 => CockpitTiles.Deny,
                >= 6 => new BitmapColor(48, 36, 72),
                _ => CockpitTiles.Deck,
            };
            return CockpitTiles.RenderDeckTile(imageSize, icon, tile.Label, tile.Hint, bg, dim: tile.Empty);
        }

        private static void ToggleOverlay(ref Boolean open, IReadOnlyList<Keystroke> openSequence)
        {
            if (open)
            {
                open = false;
                KeystrokeSender.SendSequenceAsync(new[] { new Keystroke { Key = "escape" } });
                return;
            }

            open = true;
            KeystrokeSender.SendSequenceAsync(openSequence);
        }

        private static Int32 SlotIndex(String? actionParameter) =>
            Int32.TryParse(actionParameter, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
                ? Math.Clamp(i - 1, 0, 8)
                : 0;

        private static ShortcutTile TileAt(Int32 index)
        {
            if (index < 4)
            {
                var list = SettingsStore.Instance.Current.KeyboardShortcuts;
                if (index < list.Count)
                {
                    return new ShortcutTile(list[index].Label, null, list[index].Sequence, null, false);
                }

                return new ShortcutTile(Names[index], null, Array.Empty<Keystroke>(), null, true);
            }

            return index switch
            {
                4 => new ShortcutTile("Accept", "⌘↵", new[]
                {
                    new Keystroke { Key = "enter", Modifiers = new[] { "command" } },
                }, null, false),
                5 => new ShortcutTile("Reject", "Esc", new[]
                {
                    new Keystroke { Key = "escape" },
                }, null, false),
                6 => new ShortcutTile("@file", "A", Array.Empty<Keystroke>(), "@", false),
                7 => new ShortcutTile("@sel", "A", Array.Empty<Keystroke>(), "@selection ", false),
                8 => new ShortcutTile("@diff", "A", Array.Empty<Keystroke>(), "@git ", false),
                _ => new ShortcutTile("—", null, Array.Empty<Keystroke>(), null, true),
            };
        }

        private readonly record struct ShortcutTile(
            String Label,
            String? Hint,
            IReadOnlyList<Keystroke> Sequence,
            String? Mention,
            Boolean Empty);
    }
}
