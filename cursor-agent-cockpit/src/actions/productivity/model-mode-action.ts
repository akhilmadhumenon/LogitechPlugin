import { CommandAction } from '@logitech/plugin-sdk';
import { GROUP } from '../../config.js';
import { sendKeystrokes } from '../../input/keystrokes.js';
import { setActionLabel } from '../../lcd/display.js';
import { getSettingsSync } from '../../settings/store.js';
import { agentState } from '../../state/agent-state.js';

function formatLabel(modeLabel: string, modelLabel: string): string {
  return `${modeLabel}\n${modelLabel}`;
}

export class ModelModeSwitchAction extends CommandAction {
  readonly name = 'model_mode_switch';
  displayName = 'Mode\nModel';
  description = 'Cycle Cursor composer mode. Long-term: pair with dial to cycle models.';
  override readonly groupName = GROUP.productivity;

  constructor() {
    super();
    agentState.subscribe(() => this.refresh());
    this.refresh();
  }

  private refresh(): void {
    const settings = getSettingsSync();
    const mode =
      settings.modes[agentState.getSelectedModeIndex()] ?? settings.modes[0];
    const model =
      settings.models[agentState.getSelectedModelIndex()] ?? settings.models[0];
    const live = agentState.snapshot();
    const modeLabel = live.mode !== 'unknown' ? titleCase(live.mode) : (mode?.label ?? 'Mode');
    const modelLabel =
      short(live.lastModel) || model?.label || 'Model';
    setActionLabel(this, formatLabel(modeLabel, modelLabel));
  }

  async onKeyDown(): Promise<void> {
    const settings = getSettingsSync();
    if (settings.modes.length === 0) return;

    const next = (agentState.getSelectedModeIndex() + 1) % settings.modes.length;
    agentState.setSelectedModeIndex(next);
    const mode = settings.modes[next]!;

    if (settings.openModePicker?.length) {
      await sendKeystrokes(settings.openModePicker);
    }
    if (mode.selectSequence.length) {
      await sendKeystrokes(mode.selectSequence, { focus: !settings.openModePicker?.length });
    }

    this.refresh();
  }
}

export class CycleModelAction extends CommandAction {
  readonly name = 'cycle_model';
  displayName = 'Cycle\nModel';
  description = 'Advance to the next configured model and apply its select sequence';
  override readonly groupName = GROUP.productivity;

  constructor() {
    super();
    agentState.subscribe(() => this.refresh());
    this.refresh();
  }

  private refresh(): void {
    const settings = getSettingsSync();
    const model =
      settings.models[agentState.getSelectedModelIndex()] ?? settings.models[0];
    setActionLabel(this, `Model\n${model?.label ?? '—'}`);
  }

  async onKeyDown(): Promise<void> {
    await applyModelDelta(1);
    this.refresh();
  }
}

export async function applyModelDelta(delta: number): Promise<string> {
  const settings = getSettingsSync();
  if (settings.models.length === 0) return '—';

  const len = settings.models.length;
  const next = (agentState.getSelectedModelIndex() + delta + len * 10) % len;
  agentState.setSelectedModelIndex(next);
  const model = settings.models[next]!;

  if (settings.openModelPicker?.length) {
    await sendKeystrokes(settings.openModelPicker);
  }
  if (model.selectSequence.length) {
    await sendKeystrokes(model.selectSequence, {
      focus: !settings.openModelPicker?.length,
    });
  }

  return model.label;
}

function titleCase(value: string): string {
  return value.charAt(0).toUpperCase() + value.slice(1);
}

function short(model: string | null): string {
  if (!model) return '';
  return model.replace(/^claude-/, '').replace(/-thinking.*$/, '').slice(0, 12);
}
