export type AgentStatus =
  | 'idle'
  | 'thinking'
  | 'awaiting-approval'
  | 'done'
  | 'error';

export type ComposerMode = 'agent' | 'ask' | 'edit' | 'unknown';

export type UsageMetricView = 'prompts' | 'tools' | 'model' | 'errors' | 'sessions';

export type PendingShell = {
  command: string;
  cwd?: string | undefined;
  receivedAt: number;
  resolve: (decision: 'allow' | 'deny') => void;
};

export type HookEventPayload = {
  hook_event_name?: string;
  conversation_id?: string;
  generation_id?: string;
  session_id?: string;
  model?: string;
  model_id?: string;
  composer_mode?: ComposerMode;
  status?: string;
  command?: string;
  cwd?: string;
  tool_name?: string;
  [key: string]: unknown;
};

export type DailyUsage = {
  date: string;
  prompts: number;
  toolCalls: number;
  sessions: number;
  errors: number;
  completedRuns: number;
};

export type AgentSnapshot = {
  status: AgentStatus;
  toolCount: number;
  startedAt: number | null;
  lastEventAt: number | null;
  lastModel: string | null;
  mode: ComposerMode;
  conversationId: string | null;
  pendingShell: Omit<PendingShell, 'resolve'> | null;
};
