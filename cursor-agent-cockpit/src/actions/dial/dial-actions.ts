import { AdjustmentAction, type AdjustmentActionExecuteEvent } from '@logitech/plugin-sdk';
import { GROUP } from '../../config.js';
import { applyModelDelta } from '../productivity/model-mode-action.js';
import { firePrompt } from '../productivity/prompt-shortcut-action.js';
import { sendKeystrokes } from '../../input/keystrokes.js';
import { getSettingsSync } from '../../settings/store.js';

/**
 * Continuous: rotate through diff hunks (next/prev).
 * Pair with Accept key for press-to-accept.
 */
export class DiffHunkScrubberAction extends AdjustmentAction {
  readonly name = 'dial_diff_hunks';
  displayName = 'Diff Hunks';
  description = 'Rotate to move between AI diff hunks';
  override readonly groupName = GROUP.dial;
  readonly hasReset = false;

  async execute(event: AdjustmentActionExecuteEvent): Promise<void> {
    const ticks = Math.max(1, Math.abs(event.tick));
    const key = event.tick >= 0 ? 'down' : 'up';
    for (let i = 0; i < ticks; i += 1) {
      // F7 / Shift+F7 are common next/prev change bindings; also try Alt+↓/↑.
      if (key === 'down') {
        await sendKeystrokes([{ key: 'f7' }]);
      } else {
        await sendKeystrokes([{ key: 'f7', modifiers: ['shift'] }]);
      }
    }
  }
}

/**
 * Continuous: undo / redo timeline scrub after a bad generation.
 */
export class UndoTimelineAction extends AdjustmentAction {
  readonly name = 'dial_undo_timeline';
  displayName = 'Undo Timeline';
  description = 'Rotate backward/forward through edits (undo/redo)';
  override readonly groupName = GROUP.dial;
  readonly hasReset = true;

  async execute(event: AdjustmentActionExecuteEvent): Promise<void> {
    const ticks = Math.max(1, Math.abs(event.tick));
    for (let i = 0; i < ticks; i += 1) {
      if (event.tick < 0) {
        await sendKeystrokes([{ key: 'z', modifiers: ['command'] }]);
      } else {
        await sendKeystrokes([{ key: 'z', modifiers: ['command', 'shift'] }]);
      }
    }
  }
}

/**
 * Continuous: cycle configured models with the dial.
 */
export class ModelCycleDialAction extends AdjustmentAction {
  readonly name = 'dial_model_cycle';
  displayName = 'Cycle Models';
  description = 'Rotate through configured models; apply select sequence each step';
  override readonly groupName = GROUP.dial;
  readonly hasReset = false;

  async execute(event: AdjustmentActionExecuteEvent): Promise<void> {
    const label = await applyModelDelta(event.tick >= 0 ? 1 : -1);
    console.log(`[dial] model → ${label}`);
  }
}

export class FireSelectedPromptAction extends AdjustmentAction {
  // Using Adjustment with hasReset so dial press (reset) can fire the prompt
  // on hosts that map dial press → reset command.
  readonly name = 'dial_prompt_fire';
  displayName = 'Fire Prompt (dial)';
  description = 'Rotate to choose a prompt; reset/press fires it when supported';
  override readonly groupName = GROUP.dial;
  readonly hasReset = true;

  private index = 0;

  async execute(event: AdjustmentActionExecuteEvent): Promise<void> {
    const prompts = getSettingsSync().promptShortcuts;
    if (prompts.length === 0) return;

    // tick === 0 is used by some hosts for dial press / reset.
    if (event.tick === 0) {
      const prompt = prompts[this.index];
      if (prompt) await firePrompt(prompt);
      return;
    }

    const len = prompts.length;
    this.index = (this.index + (event.tick >= 0 ? 1 : -1) + len * 10) % len;
    console.log(`[dial] prompt focus → ${prompts[this.index]?.label}`);
  }
}

/**
 * Continuous: walk diagnostics (errors/warnings).
 */
export class DiagnosticsWalkerAction extends AdjustmentAction {
  readonly name = 'dial_diagnostics';
  displayName = 'Diagnostics';
  description = 'Rotate through editor problems';
  override readonly groupName = GROUP.dial;
  readonly hasReset = false;

  async execute(event: AdjustmentActionExecuteEvent): Promise<void> {
    const ticks = Math.max(1, Math.abs(event.tick));
    for (let i = 0; i < ticks; i += 1) {
      if (event.tick >= 0) {
        await sendKeystrokes([{ key: 'f8' }]);
      } else {
        await sendKeystrokes([{ key: 'f8', modifiers: ['shift'] }]);
      }
    }
  }
}
