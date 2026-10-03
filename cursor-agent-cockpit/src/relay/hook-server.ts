import { createServer, type IncomingMessage, type Server, type ServerResponse } from 'http';
import { HOOK_RELAY_HOST, HOOK_RELAY_PORT } from '../config.js';
import { agentState } from '../state/agent-state.js';
import type { HookEventPayload } from '../state/types.js';

async function readBody(req: IncomingMessage): Promise<string> {
  const chunks: Buffer[] = [];
  for await (const chunk of req) {
    chunks.push(Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk));
  }
  return Buffer.concat(chunks).toString('utf8');
}

function sendJson(res: ServerResponse, status: number, body: unknown): void {
  const payload = JSON.stringify(body ?? {});
  res.writeHead(status, {
    'Content-Type': 'application/json',
    'Content-Length': Buffer.byteLength(payload),
  });
  res.end(payload);
}

export function startHookRelay(): Server {
  const server = createServer(async (req, res) => {
    try {
      if (req.method === 'GET' && req.url === '/health') {
        sendJson(res, 200, { ok: true, agent: agentState.snapshot() });
        return;
      }

      if (req.method === 'POST' && (req.url === '/hook' || req.url === '/event')) {
        const raw = await readBody(req);
        const payload = (raw ? JSON.parse(raw) : {}) as HookEventPayload;
        const response = await agentState.ingest(payload);
        sendJson(res, 200, response);
        return;
      }

      sendJson(res, 404, { error: 'not_found' });
    } catch (error) {
      console.error('[hook-relay]', error);
      sendJson(res, 500, { error: 'internal_error' });
    }
  });

  server.listen(HOOK_RELAY_PORT, HOOK_RELAY_HOST, () => {
    console.log(`[hook-relay] listening on http://${HOOK_RELAY_HOST}:${HOOK_RELAY_PORT}`);
  });

  return server;
}
