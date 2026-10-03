import { execFile } from 'child_process';
import { promisify } from 'util';
import { CURSOR_APP_NAME } from '../config.js';
import type { Keystroke } from '../settings/types.js';

const execFileAsync = promisify(execFile);

function escapeAppleScript(value: string): string {
  return value.replace(/\\/g, '\\\\').replace(/"/g, '\\"');
}

function keystrokeToAppleScript(stroke: Keystroke): string {
  const mods = stroke.modifiers ?? [];
  const using =
    mods.length === 0
      ? ''
      : ` using {${mods.map((m) => `${m} down`).join(', ')}}`;

  // Special keys that System Events expects as `key code` / named keys.
  const special: Record<string, string> = {
    enter: 'keystroke return',
    return: 'keystroke return',
    escape: 'key code 53',
    esc: 'key code 53',
    tab: 'keystroke tab',
    space: 'keystroke space',
    up: 'key code 126',
    down: 'key code 125',
    left: 'key code 123',
    right: 'key code 124',
    delete: 'key code 51',
  };

  const lower = stroke.key.toLowerCase();
  if (special[lower]) {
    const base = special[lower]!;
    if (base.startsWith('keystroke') && using) {
      return `${base}${using}`;
    }
    if (base.startsWith('key code') && using) {
      return `${base}${using}`;
    }
    return base;
  }

  return `keystroke "${escapeAppleScript(stroke.key)}"${using}`;
}

async function runOsascript(source: string): Promise<void> {
  await execFileAsync('osascript', ['-e', source]);
}

export async function focusCursor(): Promise<void> {
  if (process.platform !== 'darwin') return;
  await runOsascript(`tell application "${CURSOR_APP_NAME}" to activate`);
  // Give the app a beat to take focus before keystrokes.
  await sleep(120);
}

export async function sendKeystrokes(
  sequence: Keystroke[],
  options: { focus?: boolean } = {},
): Promise<void> {
  if (sequence.length === 0) return;

  if (process.platform !== 'darwin') {
    console.warn('[keystrokes] Only macOS is supported in this MVP');
    return;
  }

  if (options.focus !== false) {
    await focusCursor();
  }

  const lines = sequence.map((stroke) => keystrokeToAppleScript(stroke));
  const script = `
tell application "System Events"
  ${lines.join('\n  ')}
end tell
`;
  await runOsascript(script);
}

export async function typeText(
  text: string,
  options: { focus?: boolean; submit?: boolean } = {},
): Promise<void> {
  if (process.platform !== 'darwin') {
    console.warn('[keystrokes] Only macOS is supported in this MVP');
    return;
  }

  if (options.focus !== false) {
    await focusCursor();
  }

  // Clipboard paste is more reliable than per-character keystroke for long prompts.
  const escaped = escapeAppleScript(text);
  const script = `
set the clipboard to "${escaped}"
tell application "System Events"
  keystroke "v" using command down
end tell
`;
  await runOsascript(script);

  if (options.submit) {
    await sleep(80);
    await sendKeystrokes([{ key: 'enter' }], { focus: false });
  }
}

export async function openComposerTarget(
  target: 'agent' | 'chat' | 'inline',
): Promise<void> {
  const map: Record<typeof target, Keystroke> = {
    agent: { key: 'i', modifiers: ['command'] },
    chat: { key: 'l', modifiers: ['command'] },
    inline: { key: 'k', modifiers: ['command'] },
  };
  await sendKeystrokes([map[target]]);
  await sleep(180);
}

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}
