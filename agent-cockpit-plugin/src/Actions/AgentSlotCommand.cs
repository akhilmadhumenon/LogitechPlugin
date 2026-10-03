namespace Loupedeck.AgentCockpitPlugin.Actions
{
    using System;
    using System.Collections.Concurrent;
    using System.Globalization;
    using Loupedeck.AgentCockpitPlugin;

    /// <summary>
    /// Six agent slots with unique glyphs.
    /// Short-press: pin + open chat. Long-press: unpin only.
    /// </summary>
    public class AgentSlotCommand : PluginDynamicCommand
    {
        private static readonly TimeSpan LongPressThreshold = TimeSpan.FromMilliseconds(550);
        private readonly ConcurrentDictionary<String, DateTimeOffset> _pressDownAt = new();
        private readonly ConcurrentDictionary<String, Byte> _longPressHandled = new();

        public AgentSlotCommand()
            : base()
        {
            this.DisplayName = "Agent Slots";
            this.Description = "Blank: new Agent chat on this key. Occupied: open. Long-press: unpin.";
            this.GroupName = "Cursor · Agent Cockpit";

            var names = new[] { "Orb", "Diamond", "Triangle", "Target", "Spark", "Hex" };
            for (var i = 1; i <= AgentSlotStore.SlotCount; i++)
            {
                this.AddParameter(
                    i.ToString(CultureInfo.InvariantCulture),
                    names[i - 1],
                    "Cursor · Agent Cockpit");
            }

            AgentSlotStore.Instance.Changed += this.OnStoreChanged;
        }

        protected override Boolean OnUnload()
        {
            AgentSlotStore.Instance.Changed -= this.OnStoreChanged;
            return base.OnUnload();
        }

        private void OnStoreChanged() => this.ActionImageChanged();

        /// <summary>
        /// Own press/release so we can distinguish short vs long without toggling on every tap.
        /// </summary>
        protected override Boolean ProcessButtonEvent2(String actionParameter, DeviceButtonEvent2 buttonEvent)
        {
            if (buttonEvent.IsPress())
            {
                this._pressDownAt[actionParameter] = DateTimeOffset.UtcNow;
                this._longPressHandled.TryRemove(actionParameter, out _);
                return true;
            }

            if (buttonEvent.IsLongPress())
            {
                this.HandleSlotPress(actionParameter, longPress: true);
                this._longPressHandled[actionParameter] = 1;
                this._pressDownAt.TryRemove(actionParameter, out _);
                return true;
            }

            if (buttonEvent.IsRelease())
            {
                if (this._longPressHandled.TryRemove(actionParameter, out _))
                {
                    return true; // already handled as long-press
                }

                var longPress = false;
                if (this._pressDownAt.TryRemove(actionParameter, out var started))
                {
                    longPress = DateTimeOffset.UtcNow - started >= LongPressThreshold;
                }

                this.HandleSlotPress(actionParameter, longPress);
                return true;
            }

            return false;
        }

        // Fallback if a host only delivers RunCommand (no button events).
        protected override void RunCommand(String actionParameter) =>
            this.HandleSlotPress(actionParameter, longPress: false);

        private void HandleSlotPress(String actionParameter, Boolean longPress)
        {
            if (!Int32.TryParse(actionParameter, out var oneBased) || oneBased < 1 || oneBased > AgentSlotStore.SlotCount)
            {
                return;
            }

            var idx = oneBased - 1;
            var slot = AgentSlotStore.Instance.SnapshotSlots()[idx];
            if (!slot.IsOccupied)
            {
                if (!longPress)
                {
                    if (AgentSlotStore.Instance.BeginNewChat(idx))
                    {
                        PluginLog.Info($"[slot] new chat on {slot.Label}");
                    }
                    else
                    {
                        PluginLog.Info("[slot] ignored extra blank key; one unused chat already open");
                    }

                    this.ActionImageChanged();
                }

                return;
            }

            if (longPress)
            {
                // Clear Approve/Deny/Kill target only — does not close the Cursor chat.
                AgentSlotStore.Instance.Unpin(idx);
                PluginLog.Info($"[slot] long-press unpin {slot.Label}");
            }
            else
            {
                AgentSlotStore.Instance.Pin(idx);
                CompanionClient.OpenComposerFireAndForget(slot.ConversationId);
            }

            this.ActionImageChanged();
        }

        protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize) =>
            String.Empty;

        protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        {
            if (!Int32.TryParse(actionParameter, out var oneBased))
            {
                return CockpitTiles.RenderControl(
                    imageSize,
                    CockpitTiles.ControlKind.Allow,
                    null,
                    CockpitTiles.Empty,
                    armed: false);
            }

            var slot = AgentSlotStore.Instance.SnapshotSlots()[oneBased - 1];
            var focusIdx = AgentSlotStore.Instance.FocusSlotIndex;
            var pinned = AgentSlotStore.Instance.PinnedSlotIndex == slot.Index;
            var isFocus = focusIdx == slot.Index;

            return CockpitTiles.RenderAgentSlot(
                imageSize,
                slot,
                isFocus,
                pinned,
                AgentSlotStore.Instance.PulsePhase);
        }
    }
}

