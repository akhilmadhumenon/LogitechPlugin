namespace Loupedeck.AgentCockpitPlugin.Actions
{
    using System;
    using System.Globalization;
    using System.Linq;
    using Loupedeck.AgentCockpitPlugin;

    /// <summary>Page 2 — nine assignable prompt / composer-target keys.</summary>
    public class PromptDeckCommand : PluginDynamicCommand
    {
        private static readonly String[] Names =
        {
            "Tests", "Explain", "Types", "Refactor", "Review", "Fix", "Agent", "Chat", "Edit",
        };

        private static readonly String[] PromptIds =
        {
            "write-tests", "explain", "add-types", "refactor", "review", "fix",
        };

        private static readonly CockpitTiles.DeckIcon[] Icons =
        {
            CockpitTiles.DeckIcon.Tests,
            CockpitTiles.DeckIcon.Explain,
            CockpitTiles.DeckIcon.Types,
            CockpitTiles.DeckIcon.Refactor,
            CockpitTiles.DeckIcon.Review,
            CockpitTiles.DeckIcon.Fix,
            CockpitTiles.DeckIcon.Agent,
            CockpitTiles.DeckIcon.Chat,
            CockpitTiles.DeckIcon.Edit,
        };

        public PromptDeckCommand()
            : base()
        {
            this.DisplayName = "Prompts";
            this.Description = "Page 2: fire saved prompts, or open Agent / Chat / Edit";
            this.GroupName = "Cursor · Prompts";

            for (var i = 1; i <= 9; i++)
            {
                this.AddParameter(
                    i.ToString(CultureInfo.InvariantCulture),
                    Names[i - 1],
                    "Cursor · Prompts");
            }
        }

        protected override void RunCommand(String actionParameter)
        {
            var index = SlotIndex(actionParameter);
            if (index >= 6)
            {
                KeystrokeSender.FocusTarget(TargetAt(index));
                return;
            }

            var prompt = PromptAt(index);
            if (prompt != null)
            {
                KeystrokeSender.FirePrompt(new PromptShortcut
                {
                    Id = prompt.Id,
                    Label = prompt.Label,
                    Prompt = prompt.Prompt,
                    Target = ComposerTarget.Agent,
                    Submit = true,
                });
            }
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) =>
            String.Empty;

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            var index = SlotIndex(actionParameter);
            if (index >= 6)
            {
                var target = TargetAt(index);
                return CockpitTiles.RenderDeckTile(
                    imageSize,
                    Icons[index],
                    target.ToString(),
                    background: new BitmapColor(48, 36, 72));
            }

            var prompt = PromptAt(index);
            var label = prompt?.Label ?? Names[index];
            return CockpitTiles.RenderDeckTile(
                imageSize,
                Icons[index],
                label,
                prompt == null ? "—" : null,
                new BitmapColor(22, 56, 78),
                dim: prompt == null);
        }

        private static Int32 SlotIndex(String? actionParameter) =>
            Int32.TryParse(actionParameter, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
                ? Math.Clamp(i - 1, 0, 8)
                : 0;

        private static PromptShortcut? PromptAt(Int32 index)
        {
            if (index < 0 || index >= PromptIds.Length)
            {
                return null;
            }

            var id = PromptIds[index];
            var list = SettingsStore.Instance.Current.PromptShortcuts;
            return list.FirstOrDefault(p => String.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))
                ?? list.ElementAtOrDefault(index);
        }

        private static ComposerTarget TargetAt(Int32 index) =>
            index switch
            {
                7 => ComposerTarget.Chat,
                8 => ComposerTarget.Inline,
                _ => ComposerTarget.Agent,
            };
    }
}
