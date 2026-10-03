import { CommandAction } from '@logitech/plugin-sdk';
import { GROUP } from '../../config.js';
import { openComposerTarget, sendKeystrokes, typeText } from '../../input/keystrokes.js';

async function insertMention(token: string): Promise<void> {
  await openComposerTarget('agent');
  await typeText(token, { focus: false, submit: false });
}

export class AtFileAction extends CommandAction {
  readonly name = 'ctx_at_file';
  displayName = '@file';
  description = 'Insert @file mention into Agent composer';
  override readonly groupName = GROUP.context;

  async onKeyDown(): Promise<void> {
    await insertMention('@');
    // After '@', Cursor opens the mention picker — user picks the file.
  }
}

export class AtSelectionAction extends CommandAction {
  readonly name = 'ctx_at_selection';
  displayName = '@sel';
  description = 'Focus Agent and reference the current selection';
  override readonly groupName = GROUP.context;

  async onKeyDown(): Promise<void> {
    await insertMention('@selection ');
  }
}

export class AtDiffAction extends CommandAction {
  readonly name = 'ctx_at_diff';
  displayName = '@diff';
  description = 'Focus Agent and reference git diff context';
  override readonly groupName = GROUP.context;

  async onKeyDown(): Promise<void> {
    await insertMention('@git ');
  }
}

export class OpenChatAction extends CommandAction {
  readonly name = 'ctx_open_chat';
  displayName = 'Chat\n⌘L';
  description = 'Open Cursor Chat';
  override readonly groupName = GROUP.context;

  async onKeyDown(): Promise<void> {
    await openComposerTarget('chat');
  }
}

export class OpenInlineEditAction extends CommandAction {
  readonly name = 'ctx_inline_edit';
  displayName = 'Edit\n⌘K';
  description = 'Open Cursor inline edit';
  override readonly groupName = GROUP.context;

  async onKeyDown(): Promise<void> {
    await openComposerTarget('inline');
  }
}

export class AcceptAction extends CommandAction {
  readonly name = 'ctx_accept';
  displayName = 'Accept';
  description = 'Accept the current AI suggestion / diff hunk';
  override readonly groupName = GROUP.context;

  async onKeyDown(): Promise<void> {
    // Common accept chords in Cursor / VS Code AI flows.
    await sendKeystrokes([
      { key: 'enter', modifiers: ['command'] },
    ]);
  }
}

export class RejectAction extends CommandAction {
  readonly name = 'ctx_reject';
  displayName = 'Reject';
  description = 'Reject / dismiss the current AI suggestion';
  override readonly groupName = GROUP.context;

  async onKeyDown(): Promise<void> {
    await sendKeystrokes([{ key: 'escape' }]);
  }
}
