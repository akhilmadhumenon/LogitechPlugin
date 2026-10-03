import * as http from 'http';

export type DecisionAction = 'run' | 'always' | 'skip' | 'switch';

export type CompanionPending = {
  kind: 'shell' | 'mcp' | 'webFetch' | 'switchMode' | 'none';
  prompt?: string;
  targetMode?: string;
};

export type CompanionHandlers = {
  openComposer: (composerId: string) => Promise<{ ok: boolean; used?: string }>;
  cancelComposer: (composerId: string) => Promise<{ ok: boolean; used?: string }>;
  decide: (
    composerId: string,
    action: DecisionAction,
  ) => Promise<{ ok: boolean; used?: string }>;
  getPending: (composerId: string) => Promise<CompanionPending>;
  executeCommand: (command: string) => Promise<{ ok: boolean; used?: string }>;
  focusTarget: (target: 'agent' | 'chat' | 'inline') => Promise<{ ok: boolean; used?: string }>;
  insertPrompt: (
    target: 'agent' | 'chat' | 'inline',
    text: string,
    submit: boolean,
  ) => Promise<{ ok: boolean; used?: string }>;
  createNewChat: () => Promise<{ ok: boolean; composerId?: string; used?: string }>;
  closeComposer: (composerId: string) => Promise<{ ok: boolean; used?: string }>;
  listChats: () => Promise<{ ok: boolean; ids: string[]; unusedIds?: string[] }>;
};

export type ServerHandle = {
  port: number;
  close: () => Promise<void>;
};

export function startServer(
  port: number,
  token: string,
  handlers: CompanionHandlers,
): Promise<ServerHandle> {
  return new Promise((resolve, reject) => {
    const server = http.createServer(async (req, res) => {
      try {
        await route(req, res, token, handlers);
      } catch (err) {
        const message = err instanceof Error ? err.message : String(err);
        sendJson(res, 500, { ok: false, error: message });
      }
    });

    server.once('error', reject);
    server.listen(port, '127.0.0.1', () => {
      const address = server.address();
      const bound = typeof address === 'object' && address ? address.port : port;
      resolve({
        port: bound,
        close: () =>
          new Promise((resClose, rejClose) => {
            server.close((err) => (err ? rejClose(err) : resClose()));
          }),
      });
    });
  });
}

async function route(
  req: http.IncomingMessage,
  res: http.ServerResponse,
  token: string,
  handlers: CompanionHandlers,
): Promise<void> {
  if (!authorize(req, token)) {
    sendJson(res, 401, { ok: false, error: 'unauthorized' });
    return;
  }

  const url = new URL(req.url ?? '/', 'http://127.0.0.1');

  if (req.method === 'GET' && url.pathname === '/v1/ping') {
    sendJson(res, 200, { ok: true });
    return;
  }

  if (req.method === 'GET' && url.pathname === '/v1/chats') {
    const result = await handlers.listChats();
    sendJson(res, 200, {
      ok: result.ok,
      ids: result.ids,
      unusedIds: result.unusedIds ?? [],
    });
    return;
  }

  if (req.method === 'POST' && url.pathname === '/v1/open-composer') {
    const composerId = await readComposerId(req);
    if (!composerId) {
      sendJson(res, 400, { ok: false, error: 'composerId required' });
      return;
    }

    const result = await handlers.openComposer(composerId);
    sendJson(res, result.ok ? 200 : 404, {
      ok: result.ok,
      composerId,
      used: result.used ?? null,
    });
    return;
  }

  if (req.method === 'POST' && url.pathname === '/v1/close-composer') {
    const composerId = await readComposerId(req);
    if (!composerId) {
      sendJson(res, 400, { ok: false, error: 'composerId required' });
      return;
    }

    const result = await handlers.closeComposer(composerId);
    sendJson(res, result.ok ? 200 : 404, {
      ok: result.ok,
      composerId,
      used: result.used ?? null,
    });
    return;
  }

  if (req.method === 'POST' && url.pathname === '/v1/cancel-composer') {
    const composerId = await readComposerId(req);
    if (!composerId) {
      sendJson(res, 400, { ok: false, error: 'composerId required' });
      return;
    }

    const result = await handlers.cancelComposer(composerId);
    sendJson(res, result.ok ? 200 : 404, {
      ok: result.ok,
      composerId,
      used: result.used ?? null,
    });
    return;
  }

  if (req.method === 'POST' && url.pathname === '/v1/decision') {
    const body = await readJson(req);
    const composerId =
      (typeof body.composerId === 'string' && body.composerId) ||
      (typeof body.conversationId === 'string' && body.conversationId) ||
      '';
    const action = typeof body.action === 'string' ? body.action : '';
    if (!composerId) {
      sendJson(res, 400, { ok: false, error: 'composerId required' });
      return;
    }
    if (!isDecisionAction(action)) {
      sendJson(res, 400, { ok: false, error: 'action must be run|always|skip|switch' });
      return;
    }

    const result = await handlers.decide(composerId, action);
    sendJson(res, result.ok ? 200 : 404, {
      ok: result.ok,
      composerId,
      action,
      used: result.used ?? null,
    });
    return;
  }

  if (req.method === 'POST' && url.pathname === '/v1/new-chat') {
    const result = await handlers.createNewChat();
    sendJson(res, result.ok ? 200 : 404, {
      ok: result.ok,
      composerId: result.composerId ?? null,
      used: result.used ?? null,
    });
    return;
  }

  if (req.method === 'POST' && url.pathname === '/v1/focus') {
    const body = await readJson(req);
    const target = parseTarget(body.target);
    if (!target) {
      sendJson(res, 400, { ok: false, error: 'target must be agent|chat|inline' });
      return;
    }
    const result = await handlers.focusTarget(target);
    sendJson(res, result.ok ? 200 : 404, { ok: result.ok, target, used: result.used ?? null });
    return;
  }

  if (req.method === 'POST' && url.pathname === '/v1/prompt') {
    const body = await readJson(req);
    const target = parseTarget(body.target) ?? 'agent';
    const text = typeof body.text === 'string' ? body.text : typeof body.prompt === 'string' ? body.prompt : '';
    const submit = body.submit !== false;
    if (!text) {
      sendJson(res, 400, { ok: false, error: 'text required' });
      return;
    }
    const result = await handlers.insertPrompt(target, text, submit);
    sendJson(res, result.ok ? 200 : 404, { ok: result.ok, used: result.used ?? null });
    return;
  }

  if (req.method === 'POST' && url.pathname === '/v1/command') {
    const body = await readJson(req);
    const command = typeof body.command === 'string' ? body.command : '';
    if (!command) {
      sendJson(res, 400, { ok: false, error: 'command required' });
      return;
    }

    const result = await handlers.executeCommand(command);
    sendJson(res, result.ok ? 200 : 404, {
      ok: result.ok,
      command,
      used: result.used ?? null,
    });
    return;
  }

  if (req.method === 'GET' && url.pathname === '/v1/pending') {
    const composerId = url.searchParams.get('composerId') || url.searchParams.get('conversationId') || '';
    if (!composerId) {
      sendJson(res, 400, { ok: false, error: 'composerId required' });
      return;
    }

    const pending = await handlers.getPending(composerId);
    sendJson(res, 200, {
      ok: true,
      composerId,
      pending: pending.kind === 'none' ? null : pending,
    });
    return;
  }

  sendJson(res, 404, { ok: false, error: 'not found' });
}

function parseTarget(value: unknown): 'agent' | 'chat' | 'inline' | null {
  return value === 'agent' || value === 'chat' || value === 'inline' ? value : null;
}

function isDecisionAction(action: string): action is DecisionAction {
  return action === 'run' || action === 'always' || action === 'skip' || action === 'switch';
}

async function readComposerId(req: http.IncomingMessage): Promise<string> {
  const body = await readJson(req);
  return (
    (typeof body.composerId === 'string' && body.composerId) ||
    (typeof body.conversationId === 'string' && body.conversationId) ||
    ''
  );
}

function authorize(req: http.IncomingMessage, token: string): boolean {
  const header = req.headers.authorization ?? '';
  return header === `Bearer ${token}`;
}

function readJson(req: http.IncomingMessage): Promise<Record<string, unknown>> {
  return new Promise((resolve, reject) => {
    const chunks: Buffer[] = [];
    req.on('data', (c) => chunks.push(Buffer.isBuffer(c) ? c : Buffer.from(c)));
    req.on('end', () => {
      const raw = Buffer.concat(chunks).toString('utf8').trim();
      if (!raw) {
        resolve({});
        return;
      }
      try {
        resolve(JSON.parse(raw) as Record<string, unknown>);
      } catch (err) {
        reject(err);
      }
    });
    req.on('error', reject);
  });
}

function sendJson(res: http.ServerResponse, status: number, body: unknown): void {
  const payload = JSON.stringify(body);
  res.writeHead(status, {
    'Content-Type': 'application/json',
    'Content-Length': Buffer.byteLength(payload),
  });
  res.end(payload);
}
