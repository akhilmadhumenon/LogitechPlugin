# Cursor Productivity Suite (Node)

Node.js Logi plugin for **Cursor** — Productivity Suite, Context, and Dial controls.

> **Agent Cockpit** (Approve / Deny / Kill + live A1–A6 LCD slots) moved to the C# plugin:
> [`../agent-cockpit-plugin`](../agent-cockpit-plugin). That plugin owns `http://127.0.0.1:47821`.

## Features

| Group | Actions |
|---|---|
| **Productivity Suite** | Keyboard shortcuts, prompt shortcuts, AI usage metrics, model/mode switch |
| **Context** | `@file` / `@sel` / `@diff`, Chat, inline Edit, Accept / Reject |
| **Dial** | Diff hunk scrub, undo timeline, model cycle, prompt library, diagnostics |

## Prerequisites

- Node.js **≥ 22**
- [Logi Options+](https://www.logitech.com/software/logi-options-plus.html) with Plugin Service
- MX Creative Console (Keypad / Dialpad)
- macOS (keystroke MVP uses `osascript` + System Events; grant Accessibility to Logi Plugin Service)
- Cursor with Hooks support

## Quick start

```bash
npm install
npm run build
npm run link
npm run hooks:install
```

Then in **Logi Options+**, open the MX Creative Console profile and assign actions from:

- `Cursor · Agent Cockpit`
- `Cursor · Productivity Suite`
- `Cursor · Context`
- `Cursor · Dial`

Suggested 9-key layout:

```
● AGENT   |  USAGE    |  MODE/MDL
Shortcut  |  Prompt 1 |  Prompt 2
@file     |  Accept   |  Reject
```

Dial → **Diff Hunks** (or **Cycle Models** / **Fire Prompt**)  
Roller → **Undo Timeline**

## Configuration

User settings live at `~/.cursor-agent-cockpit/settings.json` (created on first launch from defaults).

Edit to customize:

- `keyboardShortcuts` — label + keystroke sequences
- `promptShortcuts` — canned prompts (`target`: `agent` | `chat` | `inline`)
- `models` / `modes` — labels + optional `selectSequence` chords
- `openModelPicker` / `openModePicker` — optional chords before applying a selection

Usage metrics persist at `~/.cursor-agent-cockpit/usage-metrics.json`.

## Cursor hooks

`npm run hooks:install` copies the relay into `~/.cursor/hooks/` and merges entries into `~/.cursor/hooks.json`.

The plugin listens on `http://127.0.0.1:47821/hook` (override with `CURSOR_COCKPIT_PORT` / `CURSOR_COCKPIT_HOST`).

## Develop

```bash
npm run watch   # rebuild + hot-reload into Logi Plugin Service
```

Package a distributable:

```bash
npm run build:pack   # → .lplug4
```

## Notes / limits (Node SDK 0.1.1)

- LCD **text** does **not** live-update on MX Keypad with Node SDK `0.1.1` (`ActionTextChanged` is not wired). Agent Cockpit state is real (see `/health`); macOS notifications fire on status changes as a stand-in. Dynamic **images** are also unavailable in the Node beta.
- MX Master 4 **haptics** are C#-only today; completion pulse is deferred.
- Write channel is synthetic keystrokes — Accessibility permission required on macOS.
- On Logi Plugin Service **6.4+**, set `pluginRuntime: NodeJs22` in `LoupedeckPackage.yaml`. The toolkit template still scaffolds `nodejs`, which fails with `Unknown plugin runtime type 'nodejs'`.

## Verify on this machine

```bash
# Plugin process + hook relay
curl -s http://127.0.0.1:47821/health | jq .

# Live plugin log
tail -f "$HOME/Library/Application Support/Logi/LogiPluginService/Logs/plugin_logs/CursorAgentCockpit.log"
```

In **Logi Options+** → MX Creative Console → pick a profile → add actions from the `Cursor · …` groups.
