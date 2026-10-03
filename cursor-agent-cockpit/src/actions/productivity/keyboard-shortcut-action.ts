import { CommandAction } from '@logitech/plugin-sdk';
import { GROUP } from '../../config.js';
import { sendKeystrokes } from '../../input/keystrokes.js';
import type { KeyboardShortcut } from '../../settings/types.js';

export class KeyboardShortcutAction extends CommandAction {
  readonly name: string;
  displayName: string;
  description: string;
  override readonly groupName = GROUP.productivity;

  private readonly shortcut: KeyboardShortcut;

  constructor(shortcut: KeyboardShortcut) {
    super();
    this.shortcut = shortcut;
    this.name = `kbd_${shortcut.id}`;
    this.displayName = shortcut.label;
    this.description =
      shortcut.description ?? `Keyboard shortcut: ${shortcut.label}`;
  }

  async onKeyDown(): Promise<void> {
    await sendKeystrokes(this.shortcut.sequence);
  }
}

export function buildKeyboardShortcutActions(
  shortcuts: KeyboardShortcut[],
): KeyboardShortcutAction[] {
  return shortcuts.map((s) => new KeyboardShortcutAction(s));
}
