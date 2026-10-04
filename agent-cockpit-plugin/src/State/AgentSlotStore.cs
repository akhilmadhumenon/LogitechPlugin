namespace Loupedeck.AgentCockpitPlugin
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Tracks up to 6 Cursor agents.
    /// Pulse yellow = tool running; solid yellow = real decision; pin targets controls.
    /// </summary>
    public sealed class AgentSlotStore
    {
        public const Int32 SlotCount = 6;
        private readonly Object _gate = new();
        private readonly AgentSlot[] _slots;
        private readonly Timer?[] _stopTimers = new Timer?[SlotCount];
        private readonly HashSet<String> _sessionAlwaysAllow = new(StringComparer.Ordinal);
        private String? _lastEventConversationId;
        private Int32? _pinnedSlotIndex;
        private Int32 _pulsePhase;
        private Timer? _pulseTimer;

        public event Action? Changed;

        public static AgentSlotStore Instance { get; } = new AgentSlotStore();

        private AgentSlotStore()
        {
            this._slots = Enumerable.Range(0, SlotCount)
                .Select(i => new AgentSlot { Index = i })
                .ToArray();
        }

        public Int32 PulsePhase
        {
            get { lock (this._gate) { return this._pulsePhase; } }
        }

        public void StartPulse()
        {
            this._pulseTimer ??= new Timer(_ =>
            {
                lock (this._gate)
                {
                    if (!this._slots.Any(s => s.Status == AgentStatus.ShellRunning))
                    {
                        return;
                    }

                    this._pulsePhase = 1 - this._pulsePhase;
                }

                this.RaiseChanged();
            }, null, TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(400));
        }

        public void StopPulse()
        {
            this._pulseTimer?.Dispose();
            this._pulseTimer = null;
        }

        public AgentSlot[] SnapshotSlots()
        {
            lock (this._gate)
            {
                return this._slots.Select(CloneSlot).ToArray();
            }
        }

        public Int32? PinnedSlotIndex
        {
            get { lock (this._gate) { return this._pinnedSlotIndex; } }
        }

        public Int32 FocusSlotIndex
        {
            get
            {
                lock (this._gate)
                {
                    return this.ResolveFocusSlotIndexUnlocked();
                }
            }
        }

        public AgentSlot? ControlTarget
        {
            get
            {
                lock (this._gate)
                {
                    var idx = this.ResolvePinnedSlotIndexUnlocked();
                    return idx < 0 ? null : CloneSlot(this._slots[idx]);
                }
            }
        }

        public Boolean HasControlTarget
        {
            get
            {
                lock (this._gate)
                {
                    return this.ResolvePinnedSlotIndexUnlocked() >= 0;
                }
            }
        }

        public Boolean Pin(Int32 slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= SlotCount)
            {
                return false;
            }

            lock (this._gate)
            {
                var slot = this._slots[slotIndex];
                if (!slot.IsOccupied)
                {
                    return false;
                }

                this._pinnedSlotIndex = slotIndex;
            }

            this.RaiseChanged();
            return true;
        }

        public void SyncOpenChats(OpenChatsSnapshot snap)
        {
            var unused = (snap.Unused ?? Array.Empty<String>())
                .Where(id => !String.IsNullOrEmpty(id) && !id.StartsWith("pending:", StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .Take(1)
                .ToArray();
            var used = (snap.Used ?? Array.Empty<String>())
                .Where(id => !String.IsNullOrEmpty(id) && !id.StartsWith("pending:", StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .Where(id => !unused.Contains(id, StringComparer.Ordinal))
                .ToArray();
            var keepUnused = unused.FirstOrDefault();

            var changed = false;
            lock (this._gate)
            {
                foreach (var id in unused)
                {
                    var existing = this._slots.FirstOrDefault(s => s.ConversationId == id);
                    if (existing != null)
                    {
                        if (existing.HasUserPrompt)
                        {
                            existing.HasUserPrompt = false;
                            existing.StartedAt = null;
                            changed = true;
                        }

                        continue;
                    }

                    changed |= this.BindOpenChatUnlocked(id, used: false);
                }

                foreach (var id in used)
                {
                    var existing = this._slots.FirstOrDefault(s => s.ConversationId == id);
                    if (existing != null)
                    {
                        if (!existing.HasUserPrompt && existing.ToolCount == 0
                            && existing.Status is AgentStatus.Idle or AgentStatus.Empty)
                        {
                            existing.HasUserPrompt = true;
                            existing.StartedAt ??= DateTimeOffset.UtcNow;
                            changed = true;
                        }

                        continue;
                    }

                    changed |= this.BindOpenChatUnlocked(id, used: true);
                }

                foreach (var extra in this._slots.Where(s => s.IsBlankChat))
                {
                    var id = extra.ConversationId;
                    if (String.IsNullOrEmpty(id) || id.StartsWith("pending:", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (id == keepUnused)
                    {
                        continue;
                    }

                    this.VacateSlotUnlocked(extra);
                    changed = true;
                }
            }

            if (changed)
            {
                this.RaiseChanged();
            }
        }

        private Boolean BindOpenChatUnlocked(String id, Boolean used)
        {
            var reserved = this._slots.FirstOrDefault(s => s.IsReserved);
            if (reserved != null)
            {
                reserved.ConversationId = id;
                reserved.IsReserved = false;
                reserved.Status = AgentStatus.Idle;
                reserved.HasUserPrompt = used;
                reserved.StartedAt = used ? DateTimeOffset.UtcNow : null;
                reserved.LastEventAt = DateTimeOffset.UtcNow;
                return true;
            }

            var empty = this._slots.FirstOrDefault(s => !s.IsOccupied);
            if (empty == null)
            {
                return false;
            }

            empty.ConversationId = id;
            empty.Status = AgentStatus.Idle;
            empty.IsReserved = false;
            empty.HasUserPrompt = used;
            empty.StartedAt = used ? DateTimeOffset.UtcNow : null;
            empty.LastEventAt = DateTimeOffset.UtcNow;
            return true;
        }

        /// <summary>Blank key: open a new Agent chat and bind the next conversation to this slot.</summary>
        public Boolean BeginNewChat(Int32 slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= SlotCount)
            {
                return false;
            }

            try
            {
                var open = CompanionClient.ListOpenChatsAsync().GetAwaiter().GetResult();
                if (open.Used.Length > 0 || open.Unused.Length > 0)
                {
                    this.SyncOpenChats(open);
                }
            }
            catch (Exception ex)
            {
                PluginLog.Warning($"[slot] sync open chats failed: {ex.Message}");
            }

            String? reopenId = null;
            lock (this._gate)
            {
                var existing = this._slots.FirstOrDefault(s => s.IsBlankChat);
                if (existing != null)
                {
                    foreach (var extra in this._slots.Where(s => s.Index != existing.Index && s.IsBlankChat))
                    {
                        this.VacateSlotUnlocked(extra);
                    }

                    this._pinnedSlotIndex = existing.Index;
                    reopenId = existing.ConversationId;
                }
                else
                {
                    var slot = this._slots[slotIndex];
                    if (slot.IsOccupied)
                    {
                        return false;
                    }

                    slot.ConversationId = "pending:" + Guid.NewGuid().ToString("N");
                    slot.Status = AgentStatus.Idle;
                    slot.IsReserved = true;
                    slot.HasUserPrompt = false;
                    slot.StartedAt = null;
                    slot.LastEventAt = DateTimeOffset.UtcNow;
                    this._pinnedSlotIndex = slotIndex;
                }
            }

            this.RaiseChanged();

            if (reopenId != null)
            {
                if (!reopenId.StartsWith("pending:", StringComparison.Ordinal))
                {
                    CompanionClient.OpenComposerFireAndForget(reopenId);
                }

                return false;
            }

            _ = Task.Run(async () =>
            {
                var composerId = await CompanionClient.CreateNewChatAsync().ConfigureAwait(false);
                if (!String.IsNullOrEmpty(composerId))
                {
                    this.BindReservedSlot(slotIndex, composerId);
                }
            });
            return true;
        }

        private void BindReservedSlot(Int32 slotIndex, String conversationId)
        {
            lock (this._gate)
            {
                if (slotIndex < 0 || slotIndex >= SlotCount)
                {
                    return;
                }

                var slot = this._slots[slotIndex];
                if (!slot.IsReserved)
                {
                    return;
                }

                var taken = this._slots.FirstOrDefault(s =>
                    s.Index != slotIndex && s.ConversationId == conversationId);
                if (taken != null)
                {
                    return;
                }

                slot.ConversationId = conversationId;
                slot.IsReserved = false;
                slot.Status = AgentStatus.Idle;
                slot.HasUserPrompt = false;
                slot.StartedAt = null;
                slot.LastEventAt = DateTimeOffset.UtcNow;
            }

            this.RaiseChanged();
        }

        public Boolean Unpin(Int32 slotIndex)
        {
            lock (this._gate)
            {
                if (this._pinnedSlotIndex != slotIndex)
                {
                    return false;
                }

                this._pinnedSlotIndex = null;
            }

            this.RaiseChanged();
            return true;
        }

        public Object IngestHook(JsonElement payload)
        {
            var eventName = GetString(payload, "hook_event_name") ?? "unknown";
            var conversationId = GetString(payload, "conversation_id");
            var model = GetString(payload, "model_id") ?? GetString(payload, "model");
            var mode = GetString(payload, "composer_mode");
            var status = GetString(payload, "status");
            var command = GetString(payload, "command");
            var cwd = GetString(payload, "cwd");
            var toolName = GetString(payload, "tool_name");
            var sandbox = GetBool(payload, "sandbox");
            var targetMode = GetNestedString(payload, "tool_input", "toModeId")
                ?? GetNestedString(payload, "tool_input", "to_mode_id")
                ?? GetString(payload, "toModeId");
            var generationId = GetString(payload, "generation_id");

            UsageStore.Instance.Observe(
                eventName,
                model,
                mode,
                status,
                generationId,
                GetInt64(payload, "input_tokens"),
                GetInt64(payload, "output_tokens"),
                GetInt64(payload, "cache_read_tokens"),
                GetInt64(payload, "cache_write_tokens"));

            if (eventName == "beforeShellExecution")
            {
                return this.HandleBeforeShell(conversationId, command ?? "", cwd, model, mode, sandbox);
            }

            if (eventName == "beforeMCPExecution")
            {
                return this.WaitForDecision(
                    conversationId,
                    PendingDecisionKind.Mcp,
                    toolName ?? command ?? "MCP tool",
                    cwd,
                    model,
                    mode);
            }

            lock (this._gate)
            {
                var slot = this.EnsureSlotUnlocked(conversationId, eventName);
                if (slot == null)
                {
                    return new Dictionary<String, Object>();
                }

                this.ApplyEventUnlocked(slot, eventName, model, mode, status, toolName, command, targetMode);
                this._lastEventConversationId = slot.ConversationId;
            }

            this.RaiseChanged();
            return new Dictionary<String, Object>();
        }

        /// <summary>Run / Switch — allow pending decision.</summary>
        public Boolean ResolvePendingAllow(Boolean alwaysAllow)
        {
            Action<String>? resolveHook = null;
            PendingDecisionKind kind = PendingDecisionKind.None;
            String? conversationId = null;
            lock (this._gate)
            {
                var slot = this.PinnedSlotWithPendingUnlocked();
                if (slot?.Pending == null)
                {
                    return false;
                }

                var pending = slot.Pending;
                kind = pending.Kind;
                conversationId = slot.ConversationId;
                resolveHook = pending.ResolveHook;
                if (alwaysAllow && !String.IsNullOrEmpty(pending.Prompt))
                {
                    this._sessionAlwaysAllow.Add(NormalizeAllowKey(pending.Prompt));
                }

                slot.Pending = null;
                slot.Status = kind is PendingDecisionKind.Shell or PendingDecisionKind.Mcp
                    ? AgentStatus.ShellRunning
                    : AgentStatus.Thinking;
                if (kind is PendingDecisionKind.Shell or PendingDecisionKind.Mcp)
                {
                    slot.RunningCommand = pending.Prompt;
                }

                slot.LastEventAt = DateTimeOffset.UtcNow;
            }

            if (resolveHook != null)
            {
                resolveHook("allow");
            }
            else if (!String.IsNullOrEmpty(conversationId))
            {
                var action = kind == PendingDecisionKind.SwitchMode
                    ? "switch"
                    : alwaysAllow ? "always" : "run";
                CompanionClient.DecisionFireAndForget(conversationId, action);
            }

            this.RaiseChanged();
            return true;
        }

        /// <summary>Skip — deny / reject pending decision.</summary>
        public Boolean ResolvePendingSkip()
        {
            Action<String>? resolveHook = null;
            String? conversationId = null;
            lock (this._gate)
            {
                var slot = this.PinnedSlotWithPendingUnlocked();
                if (slot?.Pending == null)
                {
                    return false;
                }

                resolveHook = slot.Pending.ResolveHook;
                conversationId = slot.ConversationId;
                slot.Pending = null;
                slot.Status = AgentStatus.Thinking;
                slot.RunningCommand = null;
                slot.LastEventAt = DateTimeOffset.UtcNow;
            }

            if (resolveHook != null)
            {
                resolveHook("deny");
            }
            else if (!String.IsNullOrEmpty(conversationId))
            {
                CompanionClient.DecisionFireAndForget(conversationId, "skip");
            }

            this.RaiseChanged();
            return true;
        }

        public Boolean BeginKillPinned()
        {
            String? conversationId;
            Int32 idx;
            lock (this._gate)
            {
                idx = this.ResolvePinnedSlotIndexUnlocked();
                if (idx < 0)
                {
                    PluginLog.Info("Kill ignored — pin an agent first");
                    return false;
                }

                var slot = this._slots[idx];
                conversationId = slot.ConversationId;
                if (slot.Pending?.ResolveHook != null)
                {
                    slot.Pending.ResolveHook("deny");
                }

                slot.Pending = null;
                slot.RunningCommand = null;
                slot.Status = AgentStatus.Stopping;
                slot.LastEventAt = DateTimeOffset.UtcNow;
                this.CancelStopTimerUnlocked(idx);

                this._stopTimers[idx] = new Timer(_ =>
                {
                    lock (this._gate)
                    {
                        var current = this._slots[idx];
                        if (current.Status == AgentStatus.Stopping)
                        {
                            this.VacateSlotUnlocked(current);
                        }

                        this.CancelStopTimerUnlocked(idx);
                    }

                    this.RaiseChanged();
                }, null, TimeSpan.FromSeconds(8), Timeout.InfiniteTimeSpan);
            }

            this.RaiseChanged();
            _ = Task.Run(async () =>
            {
                if (!String.IsNullOrEmpty(conversationId)
                    && !conversationId.StartsWith("pending:", StringComparison.Ordinal))
                {
                    await CompanionClient.CancelComposerAsync(conversationId).ConfigureAwait(false);
                    await CompanionClient.CloseComposerAsync(conversationId).ConfigureAwait(false);
                }

                lock (this._gate)
                {
                    var current = this._slots[idx];
                    if (current.ConversationId == conversationId
                        || current.Status == AgentStatus.Stopping)
                    {
                        this.VacateSlotUnlocked(current);
                    }

                    this.CancelStopTimerUnlocked(idx);
                }

                this.RaiseChanged();
            });
            return true;
        }

        /// <summary>Apply companion-detected native pending (SwitchMode / WebFetch / shell UI).</summary>
        public void ApplyCompanionPending(String conversationId, PendingDecisionKind kind, String prompt, String? targetMode)
        {
            lock (this._gate)
            {
                var slot = this._slots.FirstOrDefault(s => s.ConversationId == conversationId);
                if (slot == null || kind == PendingDecisionKind.None)
                {
                    return;
                }

                // Don't override our own hook-owned gate.
                if (slot.Pending?.ResolveHook != null)
                {
                    return;
                }

                this._pinnedSlotIndex = slot.Index;
                this._lastEventConversationId = slot.ConversationId;
                slot.Pending = new PendingDecision
                {
                    Kind = kind,
                    Prompt = prompt,
                    TargetMode = targetMode,
                    ReceivedAt = DateTimeOffset.UtcNow,
                };
                slot.Status = AgentStatus.AwaitingApproval;
                slot.LastEventAt = DateTimeOffset.UtcNow;
            }

            this.RaiseChanged();
            HapticFeedback.PulseShellWait();
        }

        public void ClearCompanionPendingIfNative(String conversationId)
        {
            lock (this._gate)
            {
                var slot = this._slots.FirstOrDefault(s => s.ConversationId == conversationId);
                if (slot?.Pending == null || slot.Pending.ResolveHook != null)
                {
                    return;
                }

                if (slot.Status == AgentStatus.AwaitingApproval)
                {
                    slot.Pending = null;
                    slot.Status = AgentStatus.Thinking;
                }
            }

            this.RaiseChanged();
        }

        public Object BuildHealthPayload()
        {
            lock (this._gate)
            {
                var focusIdx = this.ResolveFocusSlotIndexUnlocked();
                var controlIdx = this.ResolvePinnedSlotIndexUnlocked();
                return new
                {
                    ok = true,
                    selection = new
                    {
                        mode = controlIdx >= 0 ? "manual" : "none",
                        pinnedSlot = controlIdx >= 0 ? controlIdx + 1 : (Int32?)null,
                        focusSlot = focusIdx >= 0 ? focusIdx + 1 : (Int32?)null,
                        controlRequiresPin = true,
                        lastEventConversationId = this._lastEventConversationId,
                    },
                    slots = this._slots.Select(s => new
                    {
                        id = s.Label,
                        occupied = s.IsOccupied,
                        conversationId = s.ConversationId,
                        status = s.Status.ToString(),
                        toolCount = s.ToolCount,
                        model = s.Model,
                        mode = s.Mode,
                        pending = s.Pending == null
                            ? null
                            : new
                            {
                                kind = s.Pending.Kind.ToString(),
                                prompt = s.Pending.Prompt,
                                targetMode = s.Pending.TargetMode,
                                hookOwned = s.Pending.ResolveHook != null,
                            },
                        runningCommand = s.RunningCommand,
                        isFocus = s.Index == focusIdx,
                        isPinned = this._pinnedSlotIndex == s.Index,
                    }).ToArray(),
                    usage = UsageStore.Instance.SnapshotHealth(),
                };
            }
        }

        private Object HandleBeforeShell(
            String? conversationId,
            String command,
            String? cwd,
            String? model,
            String? mode,
            Boolean? sandbox)
        {
            var allowKey = NormalizeAllowKey(command);
            lock (this._gate)
            {
                if (this._sessionAlwaysAllow.Contains(allowKey))
                {
                    this.MarkShellRunningUnlocked(conversationId, command, model, mode);
                    this.RaiseChanged();
                    return new Dictionary<String, Object> { ["permission"] = "allow" };
                }
            }

            // Only arm Run/Always/Skip when Cursor reports non-sandbox.
            // Missing/true sandbox → pulse yellow only (auto path).
            if (sandbox != false)
            {
                lock (this._gate)
                {
                    this.MarkShellRunningUnlocked(conversationId, command, model, mode);
                }

                this.RaiseChanged();
                return new Dictionary<String, Object> { ["permission"] = "allow" };
            }

            // Explicit non-sandbox: real decision gate.
            return this.WaitForDecision(
                conversationId,
                PendingDecisionKind.Shell,
                command,
                cwd,
                model,
                mode);
        }

        private void MarkShellRunningUnlocked(
            String? conversationId,
            String command,
            String? model,
            String? mode)
        {
            var slot = this.EnsureSlotUnlocked(conversationId, "beforeShellExecution");
            if (slot == null)
            {
                return;
            }

            this._lastEventConversationId = slot.ConversationId;
            slot.Status = AgentStatus.ShellRunning;
            slot.RunningCommand = command;
            slot.LastEventAt = DateTimeOffset.UtcNow;
            if (!String.IsNullOrEmpty(model))
            {
                slot.Model = model;
            }

            if (!String.IsNullOrEmpty(mode))
            {
                slot.Mode = mode!;
            }
        }

        private Object WaitForDecision(
            String? conversationId,
            PendingDecisionKind kind,
            String prompt,
            String? cwd,
            String? model,
            String? mode)
        {
            var tcs = new TaskCompletionSource<Dictionary<String, Object>>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            AgentSlot? slot;
            lock (this._gate)
            {
                slot = this.EnsureSlotUnlocked(
                    conversationId,
                    kind == PendingDecisionKind.Mcp ? "beforeMCPExecution" : "beforeShellExecution");
                if (slot == null)
                {
                    return new Dictionary<String, Object> { ["permission"] = "allow" };
                }

                this._pinnedSlotIndex = slot.Index;
                this._lastEventConversationId = slot.ConversationId;

                var timer = new Timer(_ =>
                {
                    lock (this._gate)
                    {
                        if (slot.Pending?.ResolveHook != null)
                        {
                            slot.Pending = null;
                            slot.Status = kind == PendingDecisionKind.Shell
                                ? AgentStatus.ShellRunning
                                : AgentStatus.Thinking;
                            if (kind == PendingDecisionKind.Shell)
                            {
                                slot.RunningCommand = prompt;
                            }
                        }
                    }

                    tcs.TrySetResult(new Dictionary<String, Object> { ["permission"] = "allow" });
                    this.RaiseChanged();
                }, null, TimeSpan.FromSeconds(25), Timeout.InfiniteTimeSpan);

                slot.Pending = new PendingDecision
                {
                    Kind = kind,
                    Prompt = prompt,
                    Cwd = cwd,
                    ReceivedAt = DateTimeOffset.UtcNow,
                    ResolveHook = decision =>
                    {
                        timer.Dispose();
                        var body = new Dictionary<String, Object> { ["permission"] = decision };
                        if (decision == "deny")
                        {
                            body["user_message"] = "Denied from MX Creative Console";
                        }

                        tcs.TrySetResult(body);
                    },
                };
                slot.Status = AgentStatus.AwaitingApproval;
                slot.LastEventAt = DateTimeOffset.UtcNow;
                if (!String.IsNullOrEmpty(model))
                {
                    slot.Model = model;
                }

                if (!String.IsNullOrEmpty(mode))
                {
                    slot.Mode = mode!;
                }
            }

            this.RaiseChanged();
            HapticFeedback.PulseShellWait();
            return tcs.Task.GetAwaiter().GetResult();
        }

        private void ApplyEventUnlocked(
            AgentSlot slot,
            String eventName,
            String? model,
            String? mode,
            String? status,
            String? toolName,
            String? command,
            String? targetMode = null)
        {
            if (!String.IsNullOrEmpty(model))
            {
                slot.Model = model;
            }

            if (!String.IsNullOrEmpty(mode))
            {
                slot.Mode = mode!;
            }

            slot.LastEventAt = DateTimeOffset.UtcNow;

            switch (eventName)
            {
                case "sessionStart":
                    this.CancelStopTimerUnlocked(slot.Index);
                    slot.Status = AgentStatus.Thinking;
                    slot.ToolCount = 0;
                    slot.StartedAt = DateTimeOffset.UtcNow;
                    slot.RunningCommand = null;
                    break;
                case "beforeSubmitPrompt":
                    this.CancelStopTimerUnlocked(slot.Index);
                    slot.Status = AgentStatus.Thinking;
                    slot.HasUserPrompt = true;
                    slot.StartedAt ??= DateTimeOffset.UtcNow;
                    slot.RunningCommand = null;
                    break;
                case "preToolUse":
                    this.CancelStopTimerUnlocked(slot.Index);
                    slot.HasUserPrompt = true;
                    slot.ToolCount += 1;
                    if (IsWebFetchTool(toolName))
                    {
                        this._pinnedSlotIndex = slot.Index;
                        slot.Pending = new PendingDecision
                        {
                            Kind = PendingDecisionKind.WebFetch,
                            Prompt = command ?? toolName ?? "Fetch page",
                            ReceivedAt = DateTimeOffset.UtcNow,
                        };
                        slot.Status = AgentStatus.AwaitingApproval;
                        _ = Task.Run(HapticFeedback.PulseShellWait);
                    }
                    else if (IsSwitchModeTool(toolName))
                    {
                        this._pinnedSlotIndex = slot.Index;
                        slot.Pending = new PendingDecision
                        {
                            Kind = PendingDecisionKind.SwitchMode,
                            Prompt = "Switch mode",
                            TargetMode = targetMode ?? mode,
                            ReceivedAt = DateTimeOffset.UtcNow,
                        };
                        slot.Status = AgentStatus.AwaitingApproval;
                        _ = Task.Run(HapticFeedback.PulseShellWait);
                    }
                    else if (slot.Status is not AgentStatus.AwaitingApproval)
                    {
                        slot.Status = AgentStatus.Thinking;
                    }

                    break;
                case "postToolUse":
                    if (slot.Status == AgentStatus.AwaitingApproval
                        && slot.Pending?.ResolveHook == null
                        && slot.Pending?.Kind is PendingDecisionKind.WebFetch or PendingDecisionKind.SwitchMode)
                    {
                        slot.Pending = null;
                        slot.Status = AgentStatus.Thinking;
                    }
                    else if (slot.Status == AgentStatus.Empty)
                    {
                        slot.Status = AgentStatus.Thinking;
                    }

                    break;
                case "afterShellExecution":
                    if (slot.Status == AgentStatus.ShellRunning)
                    {
                        slot.Status = AgentStatus.Thinking;
                        slot.RunningCommand = null;
                    }

                    break;
                case "afterMCPExecution":
                    if (slot.Status == AgentStatus.ShellRunning)
                    {
                        slot.Status = AgentStatus.Thinking;
                    }

                    break;
                case "postToolUseFailure":
                    this.MarkSlotErrorUnlocked(slot);
                    break;
                case "stop":
                    this.CancelStopTimerUnlocked(slot.Index);
                    if (slot.Status == AgentStatus.Stopping)
                    {
                        this.VacateSlotUnlocked(slot);
                    }
                    else if (IsErrorStatus(status))
                    {
                        this.MarkSlotErrorUnlocked(slot);
                    }
                    else
                    {
                        this.MarkSlotCompletedUnlocked(slot);
                    }

                    break;
                case "sessionEnd":
                    if (slot.Pending?.ResolveHook != null)
                    {
                        slot.Pending.ResolveHook("deny");
                    }

                    this.VacateSlotUnlocked(slot);
                    break;
                case "afterAgentThought":
                    if (slot.Status is AgentStatus.Empty or AgentStatus.Idle or AgentStatus.Done)
                    {
                        slot.Status = AgentStatus.Thinking;
                        slot.StartedAt ??= DateTimeOffset.UtcNow;
                    }

                    break;
                case "afterAgentResponse":
                    if (slot.Status is AgentStatus.Thinking or AgentStatus.ShellRunning or AgentStatus.AwaitingApproval)
                    {
                        if (slot.Pending?.ResolveHook == null)
                        {
                            this.MarkSlotCompletedUnlocked(slot);
                        }
                    }

                    break;
                default:
                    if (slot.Status == AgentStatus.Empty)
                    {
                        slot.Status = AgentStatus.Thinking;
                    }

                    break;
            }
        }

        private void MarkSlotCompletedUnlocked(AgentSlot slot)
        {
            if (slot.Pending?.ResolveHook != null)
            {
                slot.Pending.ResolveHook("deny");
            }

            var wasDone = slot.Status == AgentStatus.Done;
            slot.Pending = null;
            slot.RunningCommand = null;
            slot.Status = AgentStatus.Done;
            slot.StartedAt = null;
            if (!wasDone)
            {
                _ = Task.Run(HapticFeedback.PulseAgentDone);
            }
        }

        private void MarkSlotErrorUnlocked(AgentSlot slot)
        {
            if (slot.Pending?.ResolveHook != null)
            {
                slot.Pending.ResolveHook("deny");
            }

            slot.Pending = null;
            slot.RunningCommand = null;
            slot.Status = AgentStatus.Error;
            slot.StartedAt = null;
        }

        private void VacateSlotUnlocked(AgentSlot slot)
        {
            this.CancelStopTimerUnlocked(slot.Index);

            if (slot.Pending?.ResolveHook != null)
            {
                slot.Pending.ResolveHook("deny");
            }

            if (this._pinnedSlotIndex == slot.Index)
            {
                this._pinnedSlotIndex = null;
            }

            if (this._lastEventConversationId == slot.ConversationId)
            {
                this._lastEventConversationId = null;
            }

            slot.ConversationId = null;
            slot.Status = AgentStatus.Empty;
            slot.ToolCount = 0;
            slot.Model = null;
            slot.Mode = "unknown";
            slot.StartedAt = null;
            slot.LastEventAt = null;
            slot.Pending = null;
            slot.RunningCommand = null;
            slot.IsReserved = false;
            slot.HasUserPrompt = false;
        }

        private void CancelStopTimerUnlocked(Int32 index)
        {
            if (index < 0 || index >= SlotCount)
            {
                return;
            }

            this._stopTimers[index]?.Dispose();
            this._stopTimers[index] = null;
        }

        private AgentSlot? EnsureSlotUnlocked(String? conversationId, String eventName)
        {
            if (String.IsNullOrEmpty(conversationId))
            {
                if (eventName is "stop" or "afterAgentResponse" or "sessionEnd" or "afterShellExecution")
                {
                    var running = this._slots.FirstOrDefault(s =>
                        s.IsOccupied
                        && s.Status is AgentStatus.Thinking
                            or AgentStatus.AwaitingApproval
                            or AgentStatus.ShellRunning
                            or AgentStatus.Stopping);
                    if (running != null)
                    {
                        return running;
                    }
                }

                if (!String.IsNullOrEmpty(this._lastEventConversationId))
                {
                    var existing = this._slots.FirstOrDefault(s => s.ConversationId == this._lastEventConversationId);
                    if (existing != null)
                    {
                        return existing;
                    }
                }

                return this._slots.FirstOrDefault(s => s.IsOccupied) ?? this._slots[0];
            }

            var match = this._slots.FirstOrDefault(s => s.ConversationId == conversationId);
            if (match != null)
            {
                match.IsReserved = false;
                return match;
            }

            var reserved = this._slots.FirstOrDefault(s => s.IsReserved);
            if (reserved != null)
            {
                reserved.ConversationId = conversationId;
                reserved.IsReserved = false;
                reserved.Status = AgentStatus.Thinking;
                reserved.LastEventAt = DateTimeOffset.UtcNow;
                return reserved;
            }

            var empty = this._slots.FirstOrDefault(s => !s.IsOccupied);
            if (empty != null)
            {
                empty.ConversationId = conversationId;
                empty.Status = AgentStatus.Thinking;
                return empty;
            }

            var victim = this._slots
                .OrderBy(s => s.Status switch
                {
                    AgentStatus.Done or AgentStatus.Idle => 0,
                    AgentStatus.Thinking or AgentStatus.ShellRunning => 1,
                    AgentStatus.AwaitingApproval or AgentStatus.Stopping => 2,
                    AgentStatus.Error => 3,
                    _ => 4,
                })
                .ThenBy(s => s.LastEventAt ?? DateTimeOffset.MinValue)
                .First();

            this.VacateSlotUnlocked(victim);
            victim.ConversationId = conversationId;
            victim.Status = AgentStatus.Thinking;
            victim.LastEventAt = DateTimeOffset.UtcNow;
            return victim;
        }

        private AgentSlot? PinnedSlotWithPendingUnlocked()
        {
            var idx = this.ResolvePinnedSlotIndexUnlocked();
            if (idx < 0)
            {
                return null;
            }

            var slot = this._slots[idx];
            return slot.HasPendingDecision ? slot : null;
        }

        private Int32 ResolvePinnedSlotIndexUnlocked()
        {
            if (this._pinnedSlotIndex is Int32 pinned
                && pinned >= 0
                && pinned < SlotCount
                && this._slots[pinned].IsOccupied)
            {
                return pinned;
            }

            return -1;
        }

        private Int32 ResolveFocusSlotIndexUnlocked()
        {
            if (!String.IsNullOrEmpty(this._lastEventConversationId))
            {
                var idx = Array.FindIndex(this._slots, s => s.ConversationId == this._lastEventConversationId);
                if (idx >= 0)
                {
                    return idx;
                }
            }

            return Array.FindIndex(this._slots, s => s.IsOccupied);
        }

        private void RaiseChanged() => this.Changed?.Invoke();

        private static AgentSlot CloneSlot(AgentSlot s) => new AgentSlot
        {
            Index = s.Index,
            ConversationId = s.ConversationId,
            Status = s.Status,
            ToolCount = s.ToolCount,
            Model = s.Model,
            Mode = s.Mode,
            StartedAt = s.StartedAt,
            LastEventAt = s.LastEventAt,
            Pending = s.Pending,
            RunningCommand = s.RunningCommand,
            IsReserved = s.IsReserved,
            HasUserPrompt = s.HasUserPrompt,
        };

        private static Boolean IsErrorStatus(String? status)
        {
            if (String.IsNullOrEmpty(status))
            {
                return false;
            }

            return status.Equals("error", StringComparison.OrdinalIgnoreCase)
                || status.Equals("aborted", StringComparison.OrdinalIgnoreCase)
                || status.Equals("failed", StringComparison.OrdinalIgnoreCase);
        }

        private static Boolean IsWebFetchTool(String? toolName)
        {
            if (String.IsNullOrEmpty(toolName))
            {
                return false;
            }

            return toolName.Equals("WebFetch", StringComparison.OrdinalIgnoreCase)
                || toolName.Contains("WebFetch", StringComparison.OrdinalIgnoreCase)
                || toolName.Contains("fetch_page", StringComparison.OrdinalIgnoreCase);
        }

        private static Boolean IsSwitchModeTool(String? toolName)
        {
            if (String.IsNullOrEmpty(toolName))
            {
                return false;
            }

            return toolName.Equals("SwitchMode", StringComparison.OrdinalIgnoreCase)
                || toolName.Equals("switch_mode", StringComparison.OrdinalIgnoreCase)
                || toolName.Contains("SWITCH_MODE", StringComparison.OrdinalIgnoreCase);
        }

        private static String NormalizeAllowKey(String command) =>
            command.Trim().ToLowerInvariant();

        private static String? GetString(JsonElement payload, String name)
        {
            if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty(name, out var prop))
            {
                return null;
            }

            return prop.ValueKind == JsonValueKind.String ? prop.GetString() : prop.ToString();
        }

        private static Int64? GetInt64(JsonElement payload, String name)
        {
            if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty(name, out var prop))
            {
                return null;
            }

            return prop.ValueKind switch
            {
                JsonValueKind.Number when prop.TryGetInt64(out var n) => n,
                JsonValueKind.String when Int64.TryParse(prop.GetString(), out var n) => n,
                _ => null,
            };
        }

        private static Boolean? GetBool(JsonElement payload, String name)
        {
            if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty(name, out var prop))
            {
                return null;
            }

            return prop.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String when Boolean.TryParse(prop.GetString(), out var b) => b,
                _ => null,
            };
        }

        private static String? GetNestedString(JsonElement payload, String parent, String child)
        {
            if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty(parent, out var obj))
            {
                return null;
            }

            if (obj.ValueKind == JsonValueKind.String)
            {
                try
                {
                    using var doc = JsonDocument.Parse(obj.GetString() ?? "{}");
                    return GetString(doc.RootElement, child);
                }
                catch
                {
                    return null;
                }
            }

            return obj.ValueKind == JsonValueKind.Object ? GetString(obj, child) : null;
        }
    }
}
