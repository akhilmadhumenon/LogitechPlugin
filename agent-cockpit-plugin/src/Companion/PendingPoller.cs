namespace Loupedeck.AgentCockpitPlugin
{
    using System;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Polls the companion for Cursor-native pending decisions (mode switch, fetch, etc.).
    /// </summary>
    internal sealed class PendingPoller : IDisposable
    {
        private readonly Timer _timer;
        private Int32 _tick;
        private Int32 _syncN;

        public PendingPoller()
        {
            this._timer = new Timer(
                _ => _ = this.TickAsync(),
                null,
                TimeSpan.FromMilliseconds(750),
                TimeSpan.FromMilliseconds(750));
        }

        private async Task TickAsync()
        {
            if (Interlocked.Exchange(ref this._tick, 1) == 1)
            {
                return;
            }

            try
            {
                try
                {
                    if (Interlocked.Increment(ref this._syncN) % 4 == 0)
                    {
                        var open = await CompanionClient.ListOpenChatsAsync().ConfigureAwait(false);
                        if (open.Used.Length > 0 || open.Unused.Length > 0)
                        {
                            AgentSlotStore.Instance.SyncOpenChats(open);
                        }
                    }
                }
                catch
                {
                    /* companion may not be ready */
                }

                var slots = AgentSlotStore.Instance.SnapshotSlots()
                    .Where(s => s.IsOccupied && !String.IsNullOrEmpty(s.ConversationId))
                    .ToArray();

                foreach (var slot in slots)
                {
                    // Hook-owned gates already drive the keypad.
                    if (slot.Pending?.ResolveHook != null)
                    {
                        continue;
                    }

                    try
                    {
                        var pending = await CompanionClient.GetPendingAsync(slot.ConversationId!).ConfigureAwait(false);
                        if (pending == null)
                        {
                            continue;
                        }

                        if (pending.Kind == PendingDecisionKind.None)
                        {
                            if (slot.HasPendingDecision && slot.Pending?.ResolveHook == null)
                            {
                                AgentSlotStore.Instance.ClearCompanionPendingIfNative(slot.ConversationId!);
                            }

                            continue;
                        }

                        AgentSlotStore.Instance.ApplyCompanionPending(
                            slot.ConversationId!,
                            pending.Kind,
                            String.IsNullOrEmpty(pending.Prompt) ? pending.Kind.ToString() : pending.Prompt,
                            pending.TargetMode);
                    }
                    catch
                    {
                        /* ignore per-slot */
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref this._tick, 0);
            }
        }

        public void Dispose() => this._timer.Dispose();
    }
}
