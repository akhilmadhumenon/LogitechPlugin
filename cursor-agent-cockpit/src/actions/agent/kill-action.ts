import { CommandAction } from '@logitech/plugin-sdk';
import { GROUP } from '../../config.js';
import { sendKeystrokes } from '../../input/keystrokes.js';
import { agentState } from '../../state/agent-state.js';

export class KillAgentAction extends CommandAction {
  readonly name = 'agent_kill';
  displayName = 'Kill\nAgent';
  description = 'Best-effort cancel of the running Cursor agent';
  override readonly groupName = GROUP.cockpit;

  async onKeyDown(): Promise<void> {
    // Escape is the common cancel; Cmd+. is a secondary interrupt chord in many editors.
    await sendKeystrokes([{ key: 'escape' }, { key: '.', modifiers: ['command'] }]);
    agentState.markKilled();
  }
}
