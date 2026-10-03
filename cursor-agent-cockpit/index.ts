import { LoggerLevel, PluginSDK } from '@logitech/plugin-sdk';
import {
  AcceptAction,
  AtDiffAction,
  AtFileAction,
  AtSelectionAction,
  OpenChatAction,
  OpenInlineEditAction,
  RejectAction,
} from './src/actions/context/context-actions.js';
import {
  DiagnosticsWalkerAction,
  DiffHunkScrubberAction,
  FireSelectedPromptAction,
  ModelCycleDialAction,
  UndoTimelineAction,
} from './src/actions/dial/dial-actions.js';
import { buildKeyboardShortcutActions } from './src/actions/productivity/keyboard-shortcut-action.js';
import {
  CycleModelAction,
  ModelModeSwitchAction,
} from './src/actions/productivity/model-mode-action.js';
import { buildPromptShortcutActions } from './src/actions/productivity/prompt-shortcut-action.js';
import { UsageMetricAction } from './src/actions/productivity/usage-metric-action.js';
import { ensureSettings } from './src/settings/store.js';
import { usageMetrics } from './src/state/usage-metrics.js';

const settings = await ensureSettings();
await usageMetrics.load();

// Agent Cockpit (hooks + A1–A6 LCD + Approve/Deny/Kill) lives in the C# plugin
// `agent-cockpit-plugin`, which owns http://127.0.0.1:47821.

const pluginSDK = new PluginSDK({ logLevel: LoggerLevel.INFO });

// —— Productivity Suite ——
pluginSDK.registerAction(new UsageMetricAction());
pluginSDK.registerAction(new ModelModeSwitchAction());
pluginSDK.registerAction(new CycleModelAction());
for (const action of buildKeyboardShortcutActions(settings.keyboardShortcuts)) {
  pluginSDK.registerAction(action);
}
for (const action of buildPromptShortcutActions(settings.promptShortcuts)) {
  pluginSDK.registerAction(action);
}

// —— Context deck ——
pluginSDK.registerAction(new AtFileAction());
pluginSDK.registerAction(new AtSelectionAction());
pluginSDK.registerAction(new AtDiffAction());
pluginSDK.registerAction(new OpenChatAction());
pluginSDK.registerAction(new OpenInlineEditAction());
pluginSDK.registerAction(new AcceptAction());
pluginSDK.registerAction(new RejectAction());

// —— Continuous dial / roller ——
pluginSDK.registerAction(new DiffHunkScrubberAction());
pluginSDK.registerAction(new UndoTimelineAction());
pluginSDK.registerAction(new ModelCycleDialAction());
pluginSDK.registerAction(new FireSelectedPromptAction());
pluginSDK.registerAction(new DiagnosticsWalkerAction());

await pluginSDK.connect();
console.log('[cursor-agent-cockpit] connected (Productivity Suite / Context / Dial)');
console.log('[cursor-agent-cockpit] Agent Cockpit → C# plugin on :47821');
