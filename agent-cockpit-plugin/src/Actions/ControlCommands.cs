namespace Loupedeck.AgentCockpitPlugin.Actions
{
    using System;
    using Loupedeck.AgentCockpitPlugin;

    internal static class ControlTargets
    {
        public static AgentSlot? Pinned() => AgentSlotStore.Instance.ControlTarget;

        public static Int32? PinnedAgentIndex() => Pinned()?.Index;

        public static Boolean HasPin() => AgentSlotStore.Instance.HasControlTarget;

        public static Boolean HasPendingDecision()
        {
            var t = Pinned();
            return t?.HasPendingDecision == true;
        }

        public static PendingDecisionKind PendingKind() =>
            Pinned()?.Pending?.Kind ?? PendingDecisionKind.None;

        public static String? PendingPrompt() => Pinned()?.Pending?.Prompt;
    }

    /// <summary>Left key: Approve (idle) or Run / Switch (pending).</summary>
    public class ApproveShellCommand : PluginDynamicCommand
    {
        public ApproveShellCommand()
            : base("Approve / Run", "Run or Switch when a decision is pending", "Cursor · Agent Cockpit")
        {
            AgentSlotStore.Instance.Changed += () => this.ActionImageChanged();
        }

        protected override void RunCommand(String actionParameter)
        {
            if (!ControlTargets.HasPendingDecision())
            {
                return;
            }

            AgentSlotStore.Instance.ResolvePendingAllow(alwaysAllow: false);
            this.ActionImageChanged();
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) =>
            String.Empty;

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            if (!ControlTargets.HasPendingDecision())
            {
                return CockpitTiles.RenderControl(
                    imageSize,
                    CockpitTiles.ControlKind.Allow,
                    null,
                    CockpitTiles.ControlIdle,
                    armed: false);
            }

            var kind = ControlTargets.PendingKind() == PendingDecisionKind.SwitchMode
                ? CockpitTiles.ControlKind.Switch
                : CockpitTiles.ControlKind.Run;

            return CockpitTiles.RenderControl(
                imageSize,
                kind,
                ControlTargets.PinnedAgentIndex(),
                CockpitTiles.ApproveHot,
                armed: true,
                preview: ControlTargets.PendingPrompt());
        }
    }

    /// <summary>Center key: Deny (idle) or Always Run (shell/mcp/fetch) or dim (switch mode).</summary>
    public class DenyShellCommand : PluginDynamicCommand
    {
        public DenyShellCommand()
            : base("Deny / Always", "Always Run when shell/MCP pending; unused for Switch mode", "Cursor · Agent Cockpit")
        {
            AgentSlotStore.Instance.Changed += () => this.ActionImageChanged();
        }

        protected override void RunCommand(String actionParameter)
        {
            if (!ControlTargets.HasPendingDecision())
            {
                return;
            }

            if (ControlTargets.PendingKind() == PendingDecisionKind.SwitchMode)
            {
                return; // no Always for mode switch
            }

            AgentSlotStore.Instance.ResolvePendingAllow(alwaysAllow: true);
            this.ActionImageChanged();
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) =>
            String.Empty;

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            if (!ControlTargets.HasPendingDecision())
            {
                return CockpitTiles.RenderControl(
                    imageSize,
                    CockpitTiles.ControlKind.Deny,
                    null,
                    CockpitTiles.ControlIdle,
                    armed: false);
            }

            if (ControlTargets.PendingKind() == PendingDecisionKind.SwitchMode)
            {
                return CockpitTiles.RenderControl(
                    imageSize,
                    CockpitTiles.ControlKind.Always,
                    ControlTargets.PinnedAgentIndex(),
                    CockpitTiles.ControlIdle,
                    armed: false);
            }

            return CockpitTiles.RenderControl(
                imageSize,
                CockpitTiles.ControlKind.Always,
                ControlTargets.PinnedAgentIndex(),
                CockpitTiles.DecisionHot,
                armed: true);
        }
    }

    /// <summary>Right key: Kill (idle/running) or Skip (pending decision).</summary>
    public class KillAgentCommand : PluginDynamicCommand
    {
        public KillAgentCommand()
            : base("Kill / Skip", "Skip when pending; otherwise kill pinned agent", "Cursor · Agent Cockpit")
        {
            AgentSlotStore.Instance.Changed += () => this.ActionImageChanged();
        }

        protected override void RunCommand(String actionParameter)
        {
            if (ControlTargets.HasPendingDecision())
            {
                AgentSlotStore.Instance.ResolvePendingSkip();
                this.ActionImageChanged();
                return;
            }

            if (!AgentSlotStore.Instance.HasControlTarget)
            {
                PluginLog.Info("Kill ignored — pin an agent first");
                this.ActionImageChanged();
                return;
            }

            AgentSlotStore.Instance.BeginKillPinned();
            this.ActionImageChanged();
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) =>
            String.Empty;

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            if (ControlTargets.HasPendingDecision())
            {
                return CockpitTiles.RenderControl(
                    imageSize,
                    CockpitTiles.ControlKind.Skip,
                    ControlTargets.PinnedAgentIndex(),
                    CockpitTiles.DenyHot,
                    armed: true);
            }

            var armed = ControlTargets.HasPin();
            return CockpitTiles.RenderControl(
                imageSize,
                CockpitTiles.ControlKind.Kill,
                ControlTargets.PinnedAgentIndex(),
                armed ? CockpitTiles.Kill : CockpitTiles.ControlIdle,
                armed);
        }
    }
}
