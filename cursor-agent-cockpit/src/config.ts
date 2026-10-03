import { homedir } from 'os';
import { join } from 'path';

export const HOOK_RELAY_PORT = Number(process.env.CURSOR_COCKPIT_PORT ?? 47821);
export const HOOK_RELAY_HOST = process.env.CURSOR_COCKPIT_HOST ?? '127.0.0.1';

export const SETTINGS_DIR = join(homedir(), '.cursor-agent-cockpit');
export const SETTINGS_PATH = join(SETTINGS_DIR, 'settings.json');
export const METRICS_PATH = join(SETTINGS_DIR, 'usage-metrics.json');

export const GROUP = {
  cockpit: 'Cursor · Agent Cockpit',
  productivity: 'Cursor · Productivity Suite',
  context: 'Cursor · Context',
  dial: 'Cursor · Dial',
} as const;

/** Focus Cursor before sending keystrokes (macOS bundle id). */
export const CURSOR_APP_NAME = 'Cursor';
