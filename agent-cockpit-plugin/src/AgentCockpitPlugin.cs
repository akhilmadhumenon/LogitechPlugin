namespace Loupedeck.AgentCockpitPlugin
{
    using System;

    public class AgentCockpitPlugin : Plugin
    {
        private HookHttpServer? _hookServer;
        private PendingPoller? _pendingPoller;

        public override Boolean UsesApplicationApiOnly => true;

        public override Boolean HasNoApplication => true;

        public AgentCockpitPlugin()
        {
            PluginLog.Init(this.Log);
            PluginResources.Init(this.Assembly);
        }

        public override void Load()
        {
            HapticFeedback.Init(this);
            SettingsStore.Instance.Ensure();
            UsageStore.Instance.Load();
            AgentSlotStore.Instance.StartPulse();
            this._pendingPoller = new PendingPoller();

            try
            {
                this._hookServer = new HookHttpServer(47821);
                this._hookServer.Start();
                PluginLog.Info("Agent Cockpit loaded (hooks + slots + pending poller + haptics)");
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Failed to start hook relay — is port 47821 free?");
            }
        }

        public override void Unload()
        {
            this._pendingPoller?.Dispose();
            this._pendingPoller = null;
            AgentSlotStore.Instance.StopPulse();
            this._hookServer?.Dispose();
            this._hookServer = null;
        }
    }
}
