# Cursor Agent Cockpit

Live agent status on a [Logitech MX Creative Console](https://www.logitech.com/products/productivity/mx-creative-console.html) for [Cursor](https://cursor.com).

Two pieces, both required:

| Folder | What it is |
|---|---|
| [agent-cockpit-plugin](agent-cockpit-plugin/) | C# Logi Actions plugin — LCD slots, Approve / Run / Kill, usage tile, hook server on **127.0.0.1:47821** |
| [agent-cockpit-companion](agent-cockpit-companion/) | Cursor extension — opens chats, cancels agents, resolves decisions on **127.0.0.1:47822** |

```
Cursor hooks  →  C# plugin :47821  →  MX Keypad LCD
Keypad press  →  Companion :47822  →  Cursor IDE
```

## Prerequisites

- macOS (keystrokes use `osascript`; grant **Accessibility** to Logi Plugin Service)
- [Logi Options+](https://www.logitech.com/software/logi-options-plus.html) with Plugin Service
- MX Creative Console
- [Cursor](https://cursor.com) with [Hooks](https://cursor.com/docs/agent/hooks) enabled
- [.NET SDK](https://dotnet.microsoft.com/download) (plugin targets `net10.0`)
- Node.js **≥ 18** (companion + hook relay script)

## Install

### 1. Companion

```bash
cd agent-cockpit-companion
npm install
npm run build
npx vsce package --no-dependencies --out dist/agent-cockpit-companion.vsix
cursor --install-extension dist/agent-cockpit-companion.vsix
```

Reload Cursor. The status bar should show `Cockpit:47822`.

### 2. Logi plugin

```bash
export PATH="$HOME/.dotnet:$PATH"
cd agent-cockpit-plugin
dotnet build src/AgentCockpitPlugin.csproj -c Debug
```

A successful build writes `AgentCockpitPlugin.link` into Logi Plugin Service and asks it to reload. In **Logi Options+**, open your MX Creative Console profile and assign:

- **Cursor · Agent Cockpit** — page 1 (A1–A6 + Run / Always / Kill)
- **Cursor · Prompts**, **Shortcuts**, **Studio** — optional pages 2–4

### 3. Cursor hooks

```bash
./agent-cockpit-plugin/scripts/install-hooks.sh
```

Reload Cursor. The script copies a relay into `~/.cursor/hooks/` and merges entries into `~/.cursor/hooks.json`.

## Check it

```bash
curl -s http://127.0.0.1:47821/health
```

You should see `ok: true` and six slots. After a Cursor agent turn, a slot goes blue, then green.

## Layout and colors

```
[ Run / Switch / Approve ] [ Always / Deny ] [ Skip / Kill ]
[  Orb ] [ Diamond ] [ Triangle ]
[ Target ] [ Spark ] [ Hex ]
```

| Color | Meaning |
|---|---|
| Dark | Empty slot |
| Blue | Running |
| Pulse yellow | Shell / MCP executing |
| Solid yellow | Waiting on you (Run / Always / Skip or Switch) |
| Green | Completed |
| Red | Error |
| Brown | Stopping |

Short-press a glyph to pin and open that chat. Long-press unpins. Details: [agent-cockpit-plugin/README.md](agent-cockpit-plugin/README.md).

## Settings

Created on first plugin load:

| File | Purpose |
|---|---|
| `~/.cursor-agent-cockpit/settings.json` | Prompts, shortcuts, models, modes, token color thresholds |
| `~/.cursor-agent-cockpit/usage-metrics.json` | Daily usage (including tokens) |
| `~/.agent-cockpit/companion.json` | Companion port + bearer token |

Token tile (Studio **Usage**): dark at 0, green below 1M, yellow at 1M, red at 10M. Change `tokenUsage.warn` / `tokenUsage.high` in settings.

Reset today’s token total (running plugin):

```bash
curl -s -X POST -H 'Content-Length: 0' http://127.0.0.1:47821/usage/reset-tokens
```

## Limits

- LCD agent state is C# only. This repo no longer ships the older Node Logi plugin.
- MX Master 4 haptics: yellow → `knock`, green → `happy_alert`.
- Shell gates and prompt insert need Accessibility for Logi Plugin Service.

## License

[MIT](LICENSE)
