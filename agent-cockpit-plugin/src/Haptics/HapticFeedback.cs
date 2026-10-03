namespace Loupedeck.AgentCockpitPlugin
{
    using System;

    /// <summary>
    /// Raises PluginEvents that Options+ maps to MX Master 4 haptic waveforms.
    /// </summary>
    internal static class HapticFeedback
    {
        public const String ShellWaitEvent = "shellWait";
        public const String AgentDoneEvent = "agentDone";

        private static Plugin? _plugin;

        public static void Init(Plugin plugin)
        {
            _plugin = plugin;
            try
            {
                plugin.PluginEvents.AddEvent(
                    ShellWaitEvent,
                    "Shell permission needed",
                    "Agent needs Approve/Deny for a shell command");
                plugin.PluginEvents.AddEvent(
                    AgentDoneEvent,
                    "Agent completed",
                    "Agent finished successfully");
            }
            catch (Exception ex)
            {
                PluginLog.Warning($"[haptics] AddEvent failed: {ex.Message}");
            }
        }

        public static void PulseShellWait() => Raise(ShellWaitEvent);

        public static void PulseAgentDone() => Raise(AgentDoneEvent);

        private static void Raise(String eventName)
        {
            try
            {
                _plugin?.PluginEvents.RaiseEvent(eventName);
            }
            catch (Exception ex)
            {
                PluginLog.Warning($"[haptics] RaiseEvent({eventName}) failed: {ex.Message}");
            }
        }
    }
}
