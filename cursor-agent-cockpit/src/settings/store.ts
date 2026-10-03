import { mkdir, readFile, writeFile } from 'fs/promises';
import { dirname } from 'path';
import { SETTINGS_PATH } from '../config.js';
import { DEFAULT_SETTINGS, type PluginSettings } from './types.js';

let cached: PluginSettings | null = null;

function mergeSettings(raw: Partial<PluginSettings>): PluginSettings {
  return {
    ...DEFAULT_SETTINGS,
    ...raw,
    version: 1,
    keyboardShortcuts: raw.keyboardShortcuts ?? DEFAULT_SETTINGS.keyboardShortcuts,
    promptShortcuts: raw.promptShortcuts ?? DEFAULT_SETTINGS.promptShortcuts,
    models: raw.models ?? DEFAULT_SETTINGS.models,
    modes: raw.modes ?? DEFAULT_SETTINGS.modes,
  };
}

export async function loadSettings(): Promise<PluginSettings> {
  if (cached) return cached;

  try {
    const text = await readFile(SETTINGS_PATH, 'utf8');
    cached = mergeSettings(JSON.parse(text) as Partial<PluginSettings>);
  } catch {
    cached = { ...DEFAULT_SETTINGS };
    await saveSettings(cached);
  }

  return cached;
}

export async function saveSettings(settings: PluginSettings): Promise<void> {
  cached = settings;
  await mkdir(dirname(SETTINGS_PATH), { recursive: true });
  await writeFile(SETTINGS_PATH, JSON.stringify(settings, null, 2), 'utf8');
}

export function getSettingsSync(): PluginSettings {
  return cached ?? DEFAULT_SETTINGS;
}

export async function ensureSettings(): Promise<PluginSettings> {
  return loadSettings();
}
