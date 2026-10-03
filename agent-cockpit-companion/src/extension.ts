import * as crypto from 'crypto';
import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import * as vscode from 'vscode';
import {
  startServer,
  type CompanionPending,
  type DecisionAction,
  type ServerHandle,
} from './server';

const DISCOVERY_DIR = path.join(os.homedir(), '.agent-cockpit');
const DISCOVERY_FILE = path.join(DISCOVERY_DIR, 'companion.json');

type CommandResult = {
  ok: boolean;
  used?: string;
};

type NewChatResult = CommandResult & {
  composerId?: string;
};

type ChatListResult = {
  ok: boolean;
  ids: string[];
  unusedIds: string[];
};

type FocusTarget = 'agent' | 'chat' | 'inline';

type DiscoveryPayload = {
  port: number;
  token: string;
  pid: number;
  version: string;
  ide: string;
  state: string;
  startedAt: number;
};

type ComposerCommandAttempt = {
  command: string;
  args: unknown[];
};

type CreateNewTabArgs = {
  openInNewTab: boolean;
  unifiedMode?: string;
  view?: string;
  source?: string;
};

type ComposerHandleData = {
  fullConversationHeadersOnly?: unknown;
  text?: unknown;
  name?: unknown;
  title?: unknown;
  status?: unknown;
  context?: {
    selectedImages?: unknown;
  };
  activeCanvas?: unknown;
  composerId?: unknown;
  data?: unknown;
};

let handle: ServerHandle | undefined;
let statusBar: vscode.StatusBarItem | undefined;
let token = '';

export async function activate(context: vscode.ExtensionContext): Promise<void> {
  statusBar = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Right, 90);
  statusBar.text = '$(radio-tower) Cockpit…';
  statusBar.tooltip = 'Agent Cockpit Companion';
  statusBar.command = 'agentCockpit.status';
  statusBar.show();
  context.subscriptions.push(statusBar);

  context.subscriptions.push(
    vscode.commands.registerCommand('agentCockpit.restart', () => restart(context)),
    vscode.commands.registerCommand('agentCockpit.status', () => showStatus()),
  );

  await start(context);
}

export async function deactivate(): Promise<void> {
  await stop();
}

async function start(context: vscode.ExtensionContext): Promise<void> {
  const config = vscode.workspace.getConfiguration('agentCockpit');
  const port = config.get<number>('port', 47822) ?? 47822;
  token = crypto.randomBytes(24).toString('hex');

  try {
    handle = await startServer(port, token, {
      openComposer: openComposerById,
      cancelComposer: cancelComposerById,
      decide: decideForComposer,
      getPending: getPendingForComposer,
      executeCommand: executeCursorCommand,
      focusTarget: focusComposerTarget,
      insertPrompt: insertComposerPrompt,
      createNewChat: createNewAgentChat,
      closeComposer: closeComposerById,
      listChats: listOpenChats,
    });
    await writeDiscovery({
      port: handle.port,
      token,
      pid: process.pid,
      version: context.extension.packageJSON.version ?? '0.0.0',
      ide: vscode.env.appName || 'unknown',
      state: 'ready',
      startedAt: Date.now(),
    });
    if (statusBar) {
      statusBar.text = `$(radio-tower) Cockpit:${handle.port}`;
    }
  } catch (err) {
    const message = err instanceof Error ? err.message : String(err);
    if (statusBar) {
      statusBar.text = '$(error) Cockpit';
      statusBar.tooltip = message;
    }
    void vscode.window.showErrorMessage(`Agent Cockpit Companion failed: ${message}`);
  }
}

async function stop(): Promise<void> {
  if (handle) {
    await handle.close();
    handle = undefined;
  }
  try {
    if (fs.existsSync(DISCOVERY_FILE)) {
      fs.unlinkSync(DISCOVERY_FILE);
    }
  } catch {
    /* ignore */
  }
}

async function restart(context: vscode.ExtensionContext): Promise<void> {
  await stop();
  await start(context);
  void vscode.window.showInformationMessage(
    `Agent Cockpit Companion listening on 127.0.0.1:${handle?.port ?? '?'}`,
  );
}

function showStatus(): void {
  void vscode.window.showInformationMessage(
    handle
      ? `Agent Cockpit Companion ready on 127.0.0.1:${handle.port}\n${DISCOVERY_FILE}`
      : 'Agent Cockpit Companion is not running.',
  );
}

async function writeDiscovery(payload: DiscoveryPayload): Promise<void> {
  fs.mkdirSync(DISCOVERY_DIR, { recursive: true, mode: 0o700 });
  fs.writeFileSync(DISCOVERY_FILE, `${JSON.stringify(payload, null, 2)}\n`, { mode: 0o600 });
}

async function createNewAgentChat(): Promise<NewChatResult> {
  const existing = await listOpenChats();
  const unusedId = existing.unusedIds?.[0];
  if (unusedId) {
    await openComposerById(unusedId);
    return { ok: true, composerId: unusedId, used: 'reuse-unused' };
  }

  await tryCommand('composerMode.agent');
  const tabArgs: CreateNewTabArgs[] = [
    { openInNewTab: true, unifiedMode: 'agent', view: 'pane', source: 'composer_header' },
    { openInNewTab: true, unifiedMode: 'agent' },
    { openInNewTab: true, view: 'pane' },
  ];

  for (const args of tabArgs) {
    try {
      const raw = await vscode.commands.executeCommand('composer.createNew', args);
      const composerId = extractComposerId(raw);
      return { ok: true, composerId, used: 'composer.createNew+tab' };
    } catch {
      /* try next arity */
    }
  }

  try {
    const raw = await vscode.commands.executeCommand('composer.createNewComposerTab', {
      view: 'pane',
      openInNewTab: true,
      source: 'composer_header',
    });
    return { ok: true, composerId: extractComposerId(raw), used: 'composer.createNewComposerTab+pane' };
  } catch {
    return { ok: false };
  }
}

async function listOpenChats(): Promise<ChatListResult> {
  const ids: string[] = [];
  try {
    const raw = await vscode.commands.executeCommand('composer.getOrderedSelectedComposerIds');
    ids.push(...extractIdList(raw));
  } catch {
    /* ignore */
  }

  const unique = [...new Set(ids.filter((id) => id.length > 8))];
  const unusedIds: string[] = [];
  for (const id of unique) {
    if (await isUnusedComposer(id)) {
      unusedIds.push(id);
    }
  }

  const extras = unusedIds.slice(1);
  for (const extra of extras) {
    await closeComposerById(extra);
  }
  await closeExtraNewAgentTabs();

  const keptUnused = unusedIds.slice(0, 1);
  const remaining = unique.filter((id) => !extras.includes(id));
  const active = vscode.window.tabGroups.activeTabGroup.activeTab;
  if (
    keptUnused.length === 0 &&
    remaining.length > 0 &&
    active &&
    isDefaultNewAgentName(active.label)
  ) {
    keptUnused.push(remaining[0]);
  }

  return { ok: true, ids: remaining, unusedIds: keptUnused };
}

/** Cursor's empty-composer check: no bubbles, no draft text, default "New Agent" name. */
async function isUnusedComposer(composerId: string): Promise<boolean> {
  try {
    const raw = await vscode.commands.executeCommand<unknown>(
      'composer.getComposerHandleById',
      composerId,
    );
    const data = unwrapComposerData(raw);
    if (!data) {
      return false;
    }

    const headers = data.fullConversationHeadersOnly;
    const headerCount = Array.isArray(headers) ? headers.length : -1;
    const text = String(data.text ?? '').trim();
    const name = String(data.name ?? data.title ?? '');
    const status = String(data.status ?? '');
    const images = data.context?.selectedImages;
    const imageCount = Array.isArray(images) ? images.length : 0;
    const hasCanvas = data.activeCanvas != null;

    if (status === 'generating' || headerCount > 0 || text.length > 0 || imageCount > 0 || hasCanvas) {
      return false;
    }

    if (headerCount === 0 && text.length === 0) {
      return true;
    }

    return isDefaultNewAgentName(name);
  } catch {
    return false;
  }
}

function unwrapComposerData(raw: unknown): ComposerHandleData | undefined {
  if (!raw || typeof raw !== 'object') {
    return undefined;
  }
  const root = raw as ComposerHandleData;
  if (root.data && typeof root.data === 'object') {
    return root.data as ComposerHandleData;
  }
  if (
    'fullConversationHeadersOnly' in root ||
    'composerId' in root ||
    'name' in root ||
    'text' in root
  ) {
    return root;
  }
  return undefined;
}

function isDefaultNewAgentName(name: string): boolean {
  const n = name.trim();
  return /^new agent$/i.test(n) || /^new chat$/i.test(n);
}

function collectNewAgentTabs(): vscode.Tab[] {
  const tabs: vscode.Tab[] = [];
  for (const group of vscode.window.tabGroups.all) {
    for (const tab of group.tabs) {
      if (isDefaultNewAgentName(tab.label)) {
        tabs.push(tab);
      }
    }
  }
  const active = vscode.window.tabGroups.activeTabGroup.activeTab;
  if (active && tabs.includes(active)) {
    return [active, ...tabs.filter((t) => t !== active)];
  }
  return tabs;
}

async function closeExtraNewAgentTabs(): Promise<void> {
  const tabs = collectNewAgentTabs();
  if (tabs.length <= 1) {
    return;
  }

  const extras = tabs.slice(1);
  try {
    await vscode.window.tabGroups.close(extras, true);
  } catch {
    /* closeComposerById already tried */
  }
}

function extractIdList(raw: unknown): string[] {
  if (Array.isArray(raw)) {
    return raw.flatMap((item) => {
      if (typeof item === 'string') {
        return [item];
      }
      const id = extractComposerId(item);
      return id ? [id] : [];
    });
  }
  const single = extractComposerId(raw);
  return single ? [single] : [];
}

function extractComposerId(raw: unknown): string | undefined {
  if (typeof raw === 'string' && raw.length > 8) {
    return raw;
  }
  if (!raw || typeof raw !== 'object') {
    return undefined;
  }
  const o = raw as Record<string, unknown>;
  for (const key of ['composerId', 'conversationId', 'id']) {
    const value = o[key];
    if (typeof value === 'string' && value.length > 8) {
      return value;
    }
  }
  return undefined;
}

/** Best-effort: Cursor exposes several composer-open commands depending on UI mode. */
async function openComposerById(composerId: string): Promise<CommandResult> {
  const attempts: ComposerCommandAttempt[] = [
    { command: 'composer.openComposerFromNotification', args: [{ composerId }] },
    { command: 'composer.openComposer', args: [composerId] },
    { command: 'glass.openAgentById', args: [composerId] },
  ];

  for (const attempt of attempts) {
    try {
      await vscode.commands.executeCommand(attempt.command, ...attempt.args);
      return { ok: true, used: attempt.command };
    } catch {
      /* try next */
    }
  }

  try {
    await vscode.commands.executeCommand('composer.focusComposer');
  } catch {
    /* ignore */
  }

  return { ok: false };
}

/**
 * Cancel a running agent by composer / conversation id.
 * Primary: composer.cancelChat(composerId) — same path Cursor uses for Stop.
 * Fallback: open the composer, then cancelComposerStep / keystroke-equivalent commands.
 */
async function cancelComposerById(composerId: string): Promise<CommandResult> {
  try {
    await vscode.commands.executeCommand('composer.cancelChat', composerId);
    return { ok: true, used: 'composer.cancelChat' };
  } catch {
    /* try fallbacks */
  }

  await openComposerById(composerId);

  const fallbacks = ['composer.cancelComposerStep', 'composer.cancel'];
  for (const command of fallbacks) {
    try {
      await vscode.commands.executeCommand(command, composerId);
      return { ok: true, used: command };
    } catch {
      try {
        await vscode.commands.executeCommand(command);
        return { ok: true, used: `${command}(no-arg)` };
      } catch {
        /* try next */
      }
    }
  }

  return { ok: false };
}

async function closeComposerById(composerId: string): Promise<CommandResult> {
  const attempts: ComposerCommandAttempt[] = [
    { command: 'composer.closeComposer', args: [composerId] },
    { command: 'composer.closeComposer', args: [{ composerId }] },
    { command: 'composer.closeComposerTab', args: [composerId] },
    { command: 'composer.closeComposerTab', args: [{ composerId }] },
    { command: 'composer.subComposerTab.deleteChat', args: [composerId] },
  ];

  for (const attempt of attempts) {
    try {
      await vscode.commands.executeCommand(attempt.command, ...attempt.args);
      return { ok: true, used: attempt.command };
    } catch {
      /* try next */
    }
  }

  await openComposerById(composerId);
  for (const command of ['composer.closeComposerTab', 'composer.closeComposer']) {
    try {
      await vscode.commands.executeCommand(command);
      return { ok: true, used: `${command}(focused)` };
    } catch {
      /* try next */
    }
  }

  return { ok: false };
}

/**
 * Drive Cursor's pending UI: shell Run/Always/Skip, or Switch/Skip / Fetch accept/reject.
 * Commands typically act on the focused composer — open first.
 */
async function decideForComposer(
  composerId: string,
  action: DecisionAction,
): Promise<CommandResult> {
  await openComposerById(composerId);

  const commandSets: Record<DecisionAction, string[]> = {
    run: [
      'composer.approvePendingShellToolDecision',
      'composer.acceptPendingFromNotification',
      'composer.acceptComposerStep',
    ],
    always: ['composer.approvePendingShellToolDecisionAllowlist'],
    skip: [
      'composer.skipPendingShellToolDecision',
      'composer.rejectPendingFromNotification',
      'composer.cancelComposerStep',
    ],
    switch: [
      'composer.acceptPendingFromNotification',
      'composer.acceptComposerStep',
      'composer.plan_mode.switch_to_plan.accept',
    ],
  };

  for (const command of commandSets[action]) {
    for (const args of [[composerId], [{ composerId }], []] as unknown[][]) {
      try {
        await vscode.commands.executeCommand(command, ...args);
        return { ok: true, used: args.length ? `${command}(+args)` : command };
      } catch {
        /* try next arity */
      }
    }
  }

  return { ok: false };
}

const ALLOWED_COMMANDS = new Set([
  'composer.cycleMode',
  'composer.cycleModel',
  'composer.cycleModelParameter',
  'composer.openModeMenu',
  'composer.openModelToggle',
  'glass.openModelPicker',
  'glass.cycleModelParameter',
  'composer.focusComposer',
  'composer.openComposer',
  'composer.open_chat_sidebar',
  'composer.openAsPane',
  'composerMode.agent',
  'composerMode.chat',
  'composer.submit',
  'composer.submitChat',
  'composer.sendToAgent',
  'aipopup.action.modal.generate',
  'aipopup.action.focusEdit',
  'list.select',
  'workbench.action.acceptSelectedQuickOpenItem',
  'editor.action.marker.next',
  'editor.action.marker.nextInFiles',
  'workbench.action.redo',
  'redo',
  'workbench.actions.view.problems',
  'workbench.panel.markers.view.focus',
  'workbench.action.showCommands',
  'workbench.action.quickOpen',
  'workbench.action.closeQuickOpen',
]);

async function executeCursorCommand(command: string): Promise<CommandResult> {
  if (!ALLOWED_COMMANDS.has(command)) {
    return { ok: false };
  }

  try {
    await vscode.commands.executeCommand(command);
    return { ok: true, used: command };
  } catch {
    /* try no-arg already failed */
  }

  return { ok: false };
}

/** Open Agent/Chat/inline. Avoid toggle commands (Cmd+L/I and startComposerPrompt). */
async function focusComposerTarget(
  target: FocusTarget,
): Promise<CommandResult> {
  if (target === 'inline') {
    for (const command of ['aipopup.action.modal.generate', 'aipopup.action.focusEdit']) {
      if (await tryCommand(command)) {
        return { ok: true, used: command };
      }
    }
    return { ok: false };
  }

  const mode = target === 'chat' ? 'composerMode.chat' : 'composerMode.agent';
  await tryCommand(mode);
  await sleep(80);

  for (const command of [
    'composer.open_chat_sidebar',
    'composer.openAsPane',
    'composer.openComposer',
    'composer.focusComposer',
  ]) {
    if (await tryCommand(command)) {
      return { ok: true, used: `${mode}+${command}` };
    }
  }

  return { ok: false };
}

async function tryCommand(command: string): Promise<boolean> {
  try {
    await vscode.commands.executeCommand(command);
    return true;
  } catch {
    return false;
  }
}

async function submitComposer(): Promise<boolean> {
  for (const command of ['composer.submitChat', 'composer.submit', 'composer.sendToAgent']) {
    if (await tryCommand(command)) {
      return true;
    }
  }
  return false;
}

async function insertComposerPrompt(
  target: FocusTarget,
  text: string,
  submit: boolean,
): Promise<CommandResult> {
  const focused = await focusComposerTarget('agent');
  if (!focused.ok) {
    return { ok: false };
  }

  await sleep(200);
  const previous = await vscode.env.clipboard.readText();
  try {
    await tryCommand('editor.action.selectAll');
    await sleep(40);
    await vscode.env.clipboard.writeText(text);
    await vscode.commands.executeCommand('editor.action.clipboardPasteAction');
    if (submit) {
      await sleep(150);
      await submitComposer();
    }
    return { ok: true, used: focused.used };
  } catch {
    return { ok: false };
  } finally {
    await sleep(120);
    try {
      await vscode.env.clipboard.writeText(previous);
    } catch {
      /* ignore restore */
    }
  }
}

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

/**
 * Best-effort pending probe. Cursor's pending group is mostly internal;
 * we try getComposerHandleById and inspect any serializable fields.
 */
async function getPendingForComposer(composerId: string): Promise<CompanionPending> {
  try {
    const raw = await vscode.commands.executeCommand<unknown>(
      'composer.getComposerHandleById',
      composerId,
    );
    const pending = extractPending(raw);
    if (pending) {
      return pending;
    }
  } catch {
    /* ignore */
  }

  return { kind: 'none' };
}

function extractPending(raw: unknown): CompanionPending | null {
  if (!raw || typeof raw !== 'object') {
    return null;
  }

  const root = raw as Record<string, unknown>;
  const candidates: unknown[] = [
    root.pending,
    root.pendingDecision,
    root.pendingUserDecision,
    dig(root, ['data', 'pending']),
    dig(root, ['data', 'pendingDecision']),
  ];

  for (const c of candidates) {
    const parsed = parsePendingObject(c);
    if (parsed) {
      return parsed;
    }
  }

  // Some handles expose arrays of decisions under various keys.
  for (const key of ['pendingUserDecisionGroup', 'pendingDecisions', 'blockingDecisions']) {
    const arr = root[key];
    if (Array.isArray(arr) && arr.length > 0) {
      const parsed = parsePendingObject(arr[0]);
      if (parsed) {
        return parsed;
      }
    }
  }

  const tool = String(
    root.blockingTool ?? root.clientSideTool ?? dig(root, ['data', 'blockingTool']) ?? '',
  );
  if (tool) {
    return kindFromToolName(tool);
  }

  return null;
}

function dig(obj: Record<string, unknown>, pathParts: string[]): unknown {
  let cur: unknown = obj;
  for (const p of pathParts) {
    if (!cur || typeof cur !== 'object') {
      return undefined;
    }
    cur = (cur as Record<string, unknown>)[p];
  }
  return cur;
}

function parsePendingObject(value: unknown): CompanionPending | null {
  if (!value || typeof value !== 'object') {
    return null;
  }
  const o = value as Record<string, unknown>;
  const tool = String(o.clientSideTool ?? o.toolName ?? o.tool ?? o.kind ?? '');
  const fromTool = kindFromToolName(tool);
  if (fromTool.kind !== 'none') {
    const prompt = String(o.prompt ?? o.command ?? o.title ?? o.explanation ?? '');
    const targetMode = String(o.toModeId ?? o.targetMode ?? o.mode ?? '');
    return {
      kind: fromTool.kind,
      prompt: prompt || undefined,
      targetMode: targetMode || undefined,
    };
  }
  return null;
}

function kindFromToolName(tool: string): CompanionPending {
  const t = tool.toLowerCase();
  if (!t) {
    return { kind: 'none' };
  }
  if (t.includes('switch_mode') || t.includes('switchmode') || t.includes('switch mode')) {
    return { kind: 'switchMode' };
  }
  if (t.includes('web_fetch') || t.includes('webfetch') || t.includes('fetch')) {
    return { kind: 'webFetch' };
  }
  if (t.includes('mcp')) {
    return { kind: 'mcp' };
  }
  if (
    t.includes('terminal') ||
    t.includes('shell') ||
    t.includes('run_terminal') ||
    t.includes('runterminal')
  ) {
    return { kind: 'shell' };
  }
  return { kind: 'none' };
}
