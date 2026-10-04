# Agent Cockpit Companion

Cursor / VS Code extension used by the C# Logi plugin to open chats, cancel agents, and resolve decisions.

Install from the [root README](../README.md). After reload, the status bar shows `Cockpit:47822`.

## API (loopback only)

Discovery: `~/.agent-cockpit/companion.json` (port + bearer token). Every request needs `Authorization: Bearer <token>`.

| Method | Path | Body / query |
|---|---|---|
| GET | `/v1/ping` | — |
| POST | `/v1/open-composer` | `{ "composerId": "<conversation_id>" }` |
| POST | `/v1/cancel-composer` | `{ "composerId": "<conversation_id>" }` |
| POST | `/v1/decision` | `{ "composerId": "…", "action": "run\|always\|skip\|switch" }` |
| POST | `/v1/command` | `{ "command": "composer.cycleMode\|composer.cycleModel" }` |
| GET | `/v1/pending?composerId=` | Best-effort pending kind |

Commands in the Command Palette: **Agent Cockpit: Restart Companion Server**, **Show Companion Status**.
