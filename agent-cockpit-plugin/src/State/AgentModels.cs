namespace Loupedeck.AgentCockpitPlugin
{
    using System;

    public enum AgentStatus
    {
        Empty,
        Idle,
        Thinking,
        /// <summary>Shell/tool executing — pulse yellow; controls stay Kill-only.</summary>
        ShellRunning,
        /// <summary>Real human decision — solid yellow; dynamic Run/Switch/Skip row.</summary>
        AwaitingApproval,
        /// <summary>Kill requested; waiting for Cursor stop confirmation.</summary>
        Stopping,
        Done,
        Error,
    }

    public enum PendingDecisionKind
    {
        None,
        Shell,
        Mcp,
        WebFetch,
        SwitchMode,
    }

    public sealed class PendingDecision
    {
        public PendingDecisionKind Kind { get; init; }
        public String Prompt { get; init; } = String.Empty;
        public String? TargetMode { get; init; }
        public String? Cwd { get; init; }
        public DateTimeOffset ReceivedAt { get; init; }
        /// <summary>Resolves our hook wait with allow/deny when we own the gate; null for Cursor-native UI.</summary>
        public Action<String>? ResolveHook { get; init; }
    }

    public sealed class AgentSlot
    {
        public Int32 Index { get; init; } // 0..5
        public String? ConversationId { get; set; }
        public AgentStatus Status { get; set; } = AgentStatus.Empty;
        public Int32 ToolCount { get; set; }
        public String? Model { get; set; }
        public String Mode { get; set; } = "unknown";
        public DateTimeOffset? StartedAt { get; set; }
        public DateTimeOffset? LastEventAt { get; set; }
        public PendingDecision? Pending { get; set; }
        /// <summary>Last shell command while ShellRunning (display only).</summary>
        public String? RunningCommand { get; set; }
        /// <summary>Opened from a blank key; next hook conversation binds here.</summary>
        public Boolean IsReserved { get; set; }
        /// <summary>True after the user actually sends a prompt in this chat.</summary>
        public Boolean HasUserPrompt { get; set; }

        public String Label => $"A{this.Index + 1}";

        public Boolean IsOccupied =>
            !String.IsNullOrEmpty(this.ConversationId) && this.Status != AgentStatus.Empty;

        /// <summary>New chat opened from a blank key, not used yet. Only one of these is allowed.</summary>
        public Boolean IsBlankChat =>
            this.IsReserved
            || (this.ConversationId?.StartsWith("pending:", StringComparison.Ordinal) == true)
            || (this.IsOccupied && !this.HasUserPrompt && this.ToolCount == 0
                && this.Status is not AgentStatus.Stopping);

        public Boolean HasPendingDecision =>
            this.Pending != null && this.Status == AgentStatus.AwaitingApproval;
    }
}
