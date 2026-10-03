import { CommandAction } from '@logitech/plugin-sdk';
import { GROUP } from '../../config.js';
import { setActionLabel } from '../../lcd/display.js';
import { agentState } from '../../state/agent-state.js';

export class ApproveShellAction extends CommandAction {
  readonly name = 'agent_approve_shell';
  displayName = 'Approve\nShell';
  description = 'Allow a shell command blocked by beforeShellExecution';
  override readonly groupName = GROUP.cockpit;

  constructor() {
    super();
    agentState.subscribe((snapshot) => {
      if (snapshot.pendingShell) {
        const cmd = snapshot.pendingShell.command.slice(0, 18);
        setActionLabel(this, `Allow?\n${cmd}`);
      } else {
        setActionLabel(this, 'Approve\nShell');
      }
    });
  }

  async onKeyDown(): Promise<void> {
    const ok = agentState.approveShell();
    setActionLabel(this, ok ? 'Approved' : 'Approve\nShell');
  }
}

export class DenyShellAction extends CommandAction {
  readonly name = 'agent_deny_shell';
  displayName = 'Deny\nShell';
  description = 'Deny a shell command blocked by beforeShellExecution';
  override readonly groupName = GROUP.cockpit;

  async onKeyDown(): Promise<void> {
    agentState.denyShell();
  }
}
