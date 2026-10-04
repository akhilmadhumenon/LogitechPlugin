#!/usr/bin/env node
/**
 * Cursor hook relay — POSTs stdin JSON to Agent Cockpit on 127.0.0.1:47821.
 */
import { stdin } from 'node:process';

const PORT = Number(process.env.CURSOR_COCKPIT_PORT ?? 47821);
const HOST = process.env.CURSOR_COCKPIT_HOST ?? '127.0.0.1';
const URL = `http://${HOST}:${PORT}/hook`;

async function readStdin() {
  const chunks = [];
  for await (const chunk of stdin) chunks.push(chunk);
  return Buffer.concat(chunks).toString('utf8');
}

async function main() {
  const raw = await readStdin();
  let payload = {};
  try {
    payload = raw ? JSON.parse(raw) : {};
  } catch {
    payload = { raw };
  }

  let responseBody = {};
  try {
    const res = await fetch(URL, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
      signal: AbortSignal.timeout(28_000),
    });
    if (res.ok) {
      responseBody = await res.json().catch(() => ({}));
    }
  } catch {
    responseBody = {};
  }

  process.stdout.write(`${JSON.stringify(responseBody)}\n`);
}

main().catch(() => {
  process.stdout.write('{}\n');
  process.exit(0);
});
