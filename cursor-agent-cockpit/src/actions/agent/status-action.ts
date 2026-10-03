import { CommandAction } from '@logitech/plugin-sdk';
import { GROUP } from '../../config.js';
import { setActionLabel } from '../../lcd/display.js';
import { agentState } from '../../state/agent-state.js';

export class AgentStatusAction extends CommandAction {
  readonly name = 'agent_status';
  displayName = 'Agent\nIdle';
  description = 'Live Cursor agent status (idle / thinking / awaiting shell / done)';
  override readonly groupName = GROUP.cockpit;

  constructor() {
    super();
    agentState.subscribe(() => {
      setActionLabel(this, agentState.formatStatusLcd());
    });
  }

  async onKeyDown(): Promise<void> {
    // Press refreshes the label; if a shell is pending, treat as "show pending".
    setActionLabel(this, agentState.formatStatusLcd());
  }
}
