#!/usr/bin/env node
/**
 * Install Cursor hook relay into ~/.cursor so Agent Cockpit receives lifecycle events.
 */
import { copyFile, mkdir, readFile, writeFile, chmod } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { homedir } from 'node:os';

const __dirname = dirname(fileURLToPath(import.meta.url));
const root = join(__dirname, '..');
const cursorDir = join(homedir(), '.cursor');
const hooksDir = join(cursorDir, 'hooks');
const relaySrc = join(root, 'assets/hooks/relay.mjs');
const relayDest = join(hooksDir, 'cursor-agent-cockpit-relay.mjs');
const hooksJsonPath = join(cursorDir, 'hooks.json');
const templatePath = join(root, 'assets/hooks/hooks.json');

const RELAY_EVENTS = [
  'sessionStart',
  'sessionEnd',
  'beforeSubmitPrompt',
  'preToolUse',
  'postToolUse',
  'postToolUseFailure',
  'beforeShellExecution',
  'stop',
  'afterAgentThought',
];

async function main() {
  await mkdir(hooksDir, { recursive: true });
  await copyFile(relaySrc, relayDest);
  await chmod(relayDest, 0o755);

  let existing = { version: 1, hooks: {} };
  try {
    existing = JSON.parse(await readFile(hooksJsonPath, 'utf8'));
    existing.hooks ??= {};
  } catch {
    // fresh install
  }

  const entry = { command: './hooks/cursor-agent-cockpit-relay.mjs' };
  const shellEntry = {
    command: './hooks/cursor-agent-cockpit-relay.mjs',
    timeout: 30,
  };

  for (const event of RELAY_EVENTS) {
    const list = Array.isArray(existing.hooks[event]) ? existing.hooks[event] : [];
    const already = list.some(
      (h) =>
        typeof h?.command === 'string' &&
        h.command.includes('cursor-agent-cockpit-relay'),
    );
    if (!already) {
      list.push(event === 'beforeShellExecution' ? { ...shellEntry } : { ...entry });
      existing.hooks[event] = list;
    }
  }

  existing.version = existing.version ?? 1;
  await writeFile(hooksJsonPath, JSON.stringify(existing, null, 2) + '\n', 'utf8');

  console.log(`Installed relay → ${relayDest}`);
  console.log(`Updated hooks  → ${hooksJsonPath}`);
  console.log('Restart Cursor (or reload hooks) if events do not appear immediately.');
  void templatePath;
}

main().catch((error) => {
  console.error(error);
  process.exit(1);
});
