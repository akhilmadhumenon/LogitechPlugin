namespace Loupedeck.AgentCockpitPlugin
{
    using System;
    using System.Collections.Generic;

    public enum ComposerTarget
    {
        Agent,
        Chat,
        Inline,
    }

    public enum UsageMetricView
    {
        Prompts,
        Tools,
        Model,
        Errors,
        Sessions,
    }

    public sealed class Keystroke
    {
        public String Key { get; set; } = String.Empty;
        public String[] Modifiers { get; set; } = Array.Empty<String>();
    }

    public sealed class KeyboardShortcut
    {
        public String Id { get; set; } = String.Empty;
        public String Label { get; set; } = String.Empty;
        public String? Description { get; set; }
        public List<Keystroke> Sequence { get; set; } = new();
    }

    public sealed class PromptShortcut
    {
        public String Id { get; set; } = String.Empty;
        public String Label { get; set; } = String.Empty;
        public String Prompt { get; set; } = String.Empty;
        public ComposerTarget Target { get; set; } = ComposerTarget.Agent;
        public Boolean Submit { get; set; } = true;
    }

    public sealed class NamedSequence
    {
        public String Id { get; set; } = String.Empty;
        public String Label { get; set; } = String.Empty;
        public List<Keystroke> SelectSequence { get; set; } = new();
    }

    public sealed class ProductivitySettings
    {
        public Int32 Version { get; set; } = 1;
        public List<KeyboardShortcut> KeyboardShortcuts { get; set; } = new();
        public List<PromptShortcut> PromptShortcuts { get; set; } = new();
        public List<NamedSequence> Models { get; set; } = new();
        public List<NamedSequence> Modes { get; set; } = new();
        public List<Keystroke> OpenModelPicker { get; set; } = new();
        public List<Keystroke> OpenModePicker { get; set; } = new();
    }

    public sealed class DailyUsage
    {
        public String Date { get; set; } = String.Empty;
        public Int32 Prompts { get; set; }
        public Int32 ToolCalls { get; set; }
        public Int32 Sessions { get; set; }
        public Int32 Errors { get; set; }
        public Int32 CompletedRuns { get; set; }
    }
}
