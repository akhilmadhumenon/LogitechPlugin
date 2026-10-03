import { execFile } from 'child_process';
import type { AgentSnapshot, AgentStatus } from '../state/types.js';
import { agentState } from '../state/agent-state.js';

const TITLES: Record<AgentStatus, string> = {
  idle: 'Agent Idle',
  thinking: 'Agent Thinking',
  'awaiting-approval': 'Shell Approval Needed',
  done: 'Agent Done',
  error: 'Agent Error',
};

function escapeAppleScript(value: string): string {
  return value.replace(/\\/g, '\\\\').replace(/"/g, '\\"');
}

function notify(title: string, subtitle: string): void {
  if (process.platform !== 'darwin') return;
  const script = `display notification "${escapeAppleScript(subtitle)}" with title "${escapeAppleScript(title)}" sound name "Tink"`;
  execFile('osascript', ['-e', script], () => {
    /* best-effort */
  });
}

function subtitleFor(snapshot: AgentSnapshot): string {
  if (snapshot.status === 'awaiting-approval' && snapshot.pendingShell) {
    return snapshot.pendingShell.command.slice(0, 80);
  }
  if (snapshot.status === 'thinking') {
    const model = snapshot.lastModel ?? 'model?';
    return `${snapshot.toolCount} tools · ${model}`;
  }
  if (snapshot.lastModel) return snapshot.lastModel;
  return snapshot.status;
}

/**
 * Node SDK cannot push live LCD text yet. Desktop notifications make Agent
 * Cockpit status changes observable while testing on MX Keypad.
 */
export function startStatusNotifications(): void {
  let lastStatus: AgentStatus | null = null;

  agentState.subscribe((snapshot) => {
    if (snapshot.status === lastStatus) return;
    lastStatus = snapshot.status;
    notify(TITLES[snapshot.status], subtitleFor(snapshot));
  });
}
