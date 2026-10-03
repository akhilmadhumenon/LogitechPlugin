# Agent Cockpit Companion

Tiny Cursor/VS Code extension that lets the MX Creative Console **Agent Cockpit** plugin open chats, cancel agents, and resolve pending decisions.

## Install

```bash
cd agent-cockpit-companion
npm install
npm run build
npx vsce package --no-dependencies --out dist/agent-cockpit-companion.vsix
cursor --install-extension dist/agent-cockpit-companion.vsix
```

Reload Cursor. Status bar should show `Cockpit:47822`.

## API (loopback only)

Discovery file: `~/.agent-cockpit/companion.json` (port + bearer token).

| Method | Path | Body / query |
|---|---|---|
| GET | `/v1/ping` | — |
| POST | `/v1/open-composer` | `{ "composerId": "<conversation_id>" }` |
| POST | `/v1/cancel-composer` | `{ "composerId": "<conversation_id>" }` |
| POST | `/v1/decision` | `{ "composerId": "…", "action": "run\|always\|skip\|switch" }` |
| POST | `/v1/command` | `{ "command": "composer.cycleMode\|composer.cycleModel" }` |
| GET | `/v1/pending?composerId=` | Best-effort pending kind probe |

All requests require `Authorization: Bearer <token>`.

- Short-press glyph → open that composer (`composer.openComposer…`)
- Kill → `composer.cancelChat(composerId)` (Cursor’s Stop path), with keystroke fallback from the C# plugin
- Decision row → `approvePendingShellToolDecision` / allowlist / skip, or accept/reject pending notification for Fetch / Switch mode
