# Cursor Agent Cockpit

Developer-productivity plugins for the [Logitech MX Creative Console](https://www.logitech.com/products/productivity/mx-creative-console.html) and [Cursor](https://cursor.com). Live LCD agent slots, hook-driven status, and keypad/dial actions — without turning every Cursor shortcut into a extra button.

This repository is three packages that work together:

| Package | What it does |
|---|---|
| [agent-cockpit-plugin](agent-cockpit-plugin/) | C# Logi Actions plugin: live A1–A6 LCD slots, Approve / Run / Kill, Cursor hook relay on **`:47821`** |
| [cursor-agent-cockpit](cursor-agent-cockpit/) | Node Logi plugin: Productivity Suite, Context, and Dial actions |
| [agent-cockpit-companion](agent-cockpit-companion/) | Cursor / VS Code extension: loopback API on **`:47822`** (open chats, cancel agents, resolve decisions) |

Design notes live in [PLAN.md](PLAN.md).

```
Cursor hooks ──► C# plugin :47821 ──► MX Keypad LCD
                       ▲
Keypad / Dial ─────────┴──► Companion :47822 ──► Cursor IDE
Node plugin ──► macOS keystrokes (productivity / dial)
```

## Prerequisites

- [Logi Options+](https://www.logitech.com/software/logi-options-plus.html) with Plugin Service
- MX Creative Console (Keypad / Dialpad)
- [Cursor](https://cursor.com) with [Hooks](https://cursor.com/docs/hooks.md)
- Node.js **≥ 22**
- .NET SDK (for the C# plugin)
- macOS (keystroke path uses `osascript` + System Events; grant **Accessibility** to Logi Plugin Service)

## Install order

### 1. Companion (Cursor)

```bash
cd agent-cockpit-companion
npm install
npm run build
npx vsce package --no-dependencies --out dist/agent-cockpit-companion.vsix
cursor --install-extension dist/agent-cockpit-companion.vsix
```

Reload Cursor. The status bar should show `Cockpit:47822`. Discovery file: `~/.agent-cockpit/companion.json`.

### 2. Agent Cockpit (C#)

```bash
export PATH="$HOME/.dotnet:$PATH"
cd agent-cockpit-plugin
dotnet build src/AgentCockpitPlugin.csproj -c Debug
```

Load the plugin through Logi Plugin Service / Options+. Hook endpoint: `http://127.0.0.1:47821/hook`.

Keypad page 1 is the live agent grid. Short-press a glyph to pin and open that chat; long-press to unpin. See [agent-cockpit-plugin/README.md](agent-cockpit-plugin/README.md) for colors, top-row decisions, and pages 2–4.

### 3. Productivity / Dial (Node)

```bash
cd cursor-agent-cockpit
npm install
npm run build
npm run link
npm run hooks:install
```

In **Logi Options+**, assign actions from `Cursor · Productivity Suite`, `Cursor · Context`, and `Cursor · Dial`. User settings: `~/.cursor-agent-cockpit/settings.json`.

Details: [cursor-agent-cockpit/README.md](cursor-agent-cockpit/README.md).

## Ports and files

| Service | Default | Notes |
|---|---|---|
| Hook relay (C# plugin) | `127.0.0.1:47821` | Cursor hooks POST here; override via env on the Node plugin if used |
| Companion HTTP | `127.0.0.1:47822` | Bearer token in `~/.agent-cockpit/companion.json` |
| Settings | `~/.cursor-agent-cockpit/settings.json` | Prompts, chords, models / modes |
| Usage | `~/.cursor-agent-cockpit/usage-metrics.json` | Studio Usage cycle |

Companion API: ping, open/cancel composer, decision, command, pending — see [agent-cockpit-companion/README.md](agent-cockpit-companion/README.md). All requests require `Authorization: Bearer <token>`.

## Limits

- Node SDK **0.1.1** does not live-update keypad LCD text; agent LCD state is owned by the **C#** plugin.
- MX Master 4 **haptics** are C#-only (yellow → knock, green → happy_alert).
- Write channel is synthetic keystrokes; Accessibility permission is required on macOS.
- On Logi Plugin Service **6.4+**, Node packages need `pluginRuntime: NodeJs22` in `LoupedeckPackage.yaml`.

## License

The companion extension is **MIT**. The Node plugin is marked **UNLICENSED**. There is no repo-wide license yet.
