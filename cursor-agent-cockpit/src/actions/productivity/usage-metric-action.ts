import { CommandAction } from '@logitech/plugin-sdk';
import { GROUP } from '../../config.js';
import { setActionLabel } from '../../lcd/display.js';
import { agentState } from '../../state/agent-state.js';
import { usageMetrics } from '../../state/usage-metrics.js';

export class UsageMetricAction extends CommandAction {
  readonly name = 'usage_metric';
  displayName = 'Usage\n—';
  description = 'AI usage metrics for today. Press to cycle prompts / tools / model / errors / sessions.';
  override readonly groupName = GROUP.productivity;

  constructor() {
    super();
    void usageMetrics.load().then(() => this.refresh());
    agentState.subscribe(() => this.refresh());
  }

  private refresh(): void {
    const model = agentState.snapshot().lastModel;
    setActionLabel(this, usageMetrics.formatLcd(model));
  }

  async onKeyDown(): Promise<void> {
    usageMetrics.cycleView();
    this.refresh();
  }
}
