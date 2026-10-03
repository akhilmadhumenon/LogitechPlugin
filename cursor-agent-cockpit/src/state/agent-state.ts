import { EventEmitter } from 'events';
import type {
  AgentSnapshot,
  AgentStatus,
  ComposerMode,
  HookEventPayload,
  PendingShell,
} from './types.js';
import { usageMetrics } from './usage-metrics.js';

type Listener = (snapshot: AgentSnapshot) => void;

class AgentStateStore extends EventEmitter {
  private status: AgentStatus = 'idle';
  private toolCount = 0;
  private startedAt: number | null = null;
  private lastEventAt: number | null = null;
  private lastModel: string | null = null;
  private mode: ComposerMode = 'unknown';
  private conversationId: string | null = null;
  private pendingShell: PendingShell | null = null;
  private selectedModelIndex = 0;
  private selectedModeIndex = 0;

  subscribe(listener: Listener): () => void {
    this.on('change', listener);
    return () => this.off('change', listener);
  }

  snapshot(): AgentSnapshot {
    return {
      status: this.status,
      toolCount: this.toolCount,
      startedAt: this.startedAt,
      lastEventAt: this.lastEventAt,
      lastModel: this.lastModel,
      mode: this.mode,
      conversationId: this.conversationId,
      pendingShell: this.pendingShell
        ? {
            command: this.pendingShell.command,
            cwd: this.pendingShell.cwd,
            receivedAt: this.pendingShell.receivedAt,
          }
        : null,
    };
  }

  private touch(model?: string): void {
    this.lastEventAt = Date.now();
    if (model) this.lastModel = model;
    this.emit('change', this.snapshot());
  }

  setSelectedModelIndex(index: number): void {
    this.selectedModelIndex = index;
    this.emit('change', this.snapshot());
  }

  getSelectedModelIndex(): number {
    return this.selectedModelIndex;
  }

  setSelectedModeIndex(index: number): void {
    this.selectedModeIndex = index;
    this.emit('change', this.snapshot());
  }

  getSelectedModeIndex(): number {
    return this.selectedModeIndex;
  }

  formatStatusLcd(): string {
    const elapsed = this.startedAt
      ? formatElapsed(Date.now() - this.startedAt)
      : '';
    switch (this.status) {
      case 'idle':
        return 'Agent\nIdle';
      case 'thinking':
        return `Agent\n● ${elapsed || '…'}\n${this.toolCount} tools`;
      case 'awaiting-approval':
        return 'Agent\n⚠ Shell';
      case 'done':
        return `Agent\nDone\n${this.toolCount} tools`;
      case 'error':
        return 'Agent\nError';
    }
  }

  async ingest(payload: HookEventPayload): Promise<Record<string, unknown>> {
    const event = payload.hook_event_name ?? 'unknown';
    const model = payload.model_id ?? payload.model;

    switch (event) {
      case 'sessionStart': {
        this.status = 'thinking';
        this.toolCount = 0;
        this.startedAt = Date.now();
        this.conversationId = payload.conversation_id ?? null;
        if (payload.composer_mode) this.mode = payload.composer_mode;
        await usageMetrics.recordSessionStart(model);
        this.touch(model);
        return {};
      }
      case 'beforeSubmitPrompt': {
        this.status = 'thinking';
        if (!this.startedAt) this.startedAt = Date.now();
        await usageMetrics.recordPrompt(model);
        this.touch(model);
        return {};
      }
      case 'preToolUse': {
        this.status = 'thinking';
        this.toolCount += 1;
        await usageMetrics.recordToolCall();
        this.touch(model);
        return {};
      }
      case 'postToolUseFailure': {
        await usageMetrics.recordError();
        this.touch(model);
        return {};
      }
      case 'beforeShellExecution': {
        return this.waitForShellDecision(payload);
      }
      case 'stop': {
        this.status = payload.status === 'error' ? 'error' : 'done';
        await usageMetrics.recordCompletion(payload.status, model);
        this.touch(model);
        // Return to idle after a short dwell so the LCD shows Done.
        setTimeout(() => {
          if (this.status === 'done' || this.status === 'error') {
            this.status = 'idle';
            this.startedAt = null;
            this.emit('change', this.snapshot());
          }
        }, 8000);
        return {};
      }
      case 'sessionEnd': {
        this.status = 'idle';
        this.startedAt = null;
        this.touch(model);
        return {};
      }
      default: {
        this.touch(model);
        return {};
      }
    }
  }

  private waitForShellDecision(payload: HookEventPayload): Promise<Record<string, unknown>> {
    // Fail-open if nobody answers within 25s so the agent is not stuck.
    return new Promise((resolve) => {
      const timer = setTimeout(() => {
        if (this.pendingShell) {
          this.pendingShell = null;
          this.status = 'thinking';
          this.emit('change', this.snapshot());
        }
        resolve({ permission: 'allow' });
      }, 25_000);

      this.pendingShell = {
        command: String(payload.command ?? ''),
        cwd: payload.cwd ? String(payload.cwd) : undefined,
        receivedAt: Date.now(),
        resolve: (decision) => {
          clearTimeout(timer);
          this.pendingShell = null;
          this.status = decision === 'allow' ? 'thinking' : 'idle';
          this.emit('change', this.snapshot());
          resolve({
            permission: decision,
            user_message:
              decision === 'deny' ? 'Denied from MX Creative Console' : undefined,
          });
        },
      };
      this.status = 'awaiting-approval';
      this.emit('change', this.snapshot());
    });
  }

  approveShell(): boolean {
    if (!this.pendingShell) return false;
    this.pendingShell.resolve('allow');
    return true;
  }

  denyShell(): boolean {
    if (!this.pendingShell) return false;
    this.pendingShell.resolve('deny');
    return true;
  }

  /** Soft kill: best-effort Escape / cancel chord; state resets locally. */
  markKilled(): void {
    this.status = 'idle';
    this.startedAt = null;
    this.toolCount = 0;
    if (this.pendingShell) {
      this.pendingShell.resolve('deny');
    }
    this.emit('change', this.snapshot());
  }
}

function formatElapsed(ms: number): string {
  const totalSec = Math.floor(ms / 1000);
  const min = Math.floor(totalSec / 60);
  const sec = totalSec % 60;
  return min > 0 ? `${min}m${sec.toString().padStart(2, '0')}s` : `${sec}s`;
}

export const agentState = new AgentStateStore();
