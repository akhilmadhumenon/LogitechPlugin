import { CommandAction } from '@logitech/plugin-sdk';
import { GROUP } from '../../config.js';
import { openComposerTarget, typeText } from '../../input/keystrokes.js';
import type { PromptShortcut } from '../../settings/types.js';

export class PromptShortcutAction extends CommandAction {
  readonly name: string;
  displayName: string;
  description: string;
  override readonly groupName = GROUP.productivity;

  private readonly shortcut: PromptShortcut;

  constructor(shortcut: PromptShortcut) {
    super();
    this.shortcut = shortcut;
    this.name = `prompt_${shortcut.id}`;
    this.displayName = shortcut.label;
    this.description = `Prompt shortcut: ${shortcut.label}`;
  }

  async onKeyDown(): Promise<void> {
    await firePrompt(this.shortcut);
  }
}

export async function firePrompt(shortcut: PromptShortcut): Promise<void> {
  await openComposerTarget(shortcut.target);
  await typeText(shortcut.prompt, {
    focus: false,
    submit: shortcut.submit ?? true,
  });
}

export function buildPromptShortcutActions(
  shortcuts: PromptShortcut[],
): PromptShortcutAction[] {
  return shortcuts.map((s) => new PromptShortcutAction(s));
}
