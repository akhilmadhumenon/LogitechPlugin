# Cursor Agent Cockpit (C#)

C# Logi Actions plugin for **MX Creative Console** with live LCD agent slots.

## Keypad layout

```
[ Run / Switch / Approve ] [ Always / Deny ] [ Skip / Kill ]
[  Orb ] [ Diamond ] [ Triangle ]
[ Target ] [ Spark ] [ Hex ]
```

Top row is **dynamic** based on the pinned agent’s pending decision.

## Agent colors

| Color | Meaning |
|---|---|
| **Blue** | Running (thinking / tools) |
| **Pulse yellow** | Shell/MCP **executing** (processing) — Kill only |
| **Solid yellow** | Real human decision — Run/Always/Skip or Switch/Skip |
| **Green** | Completed — stays until `sessionEnd` / Kill / new prompt |
| **Red** | Error — sticky until Kill, or the agent resumes |
| **Brown** | Stopping — Kill requested; waiting for Cursor stop |

Dark tile = vacant.

## Top-row controls

| Pending | Key 1 | Key 2 | Key 3 |
|---|---|---|---|
| None (blue / pulse yellow) | Approve (dim) | Deny (dim) | **Kill** (when pinned) |
| Shell / MCP / WebFetch | **Run** | **Always Run** | **Skip** |
| Switch mode | **Switch** | (dim) | **Skip** |

Sandboxed / auto-allowed shells no longer arm Run/Skip — they only pulse yellow while running, then return to blue when `afterShellExecution` fires.

## Behavior

| Control | Behavior |
|---|---|
| **Short-press glyph** | Pin + open that chat in Cursor (companion) |
| **Long-press glyph** | Unpin only (clears control target; chat stays open) |
| **Run / Switch / Always / Skip** | Resolve hook gate and/or companion Cursor decision commands |
| **Kill** | Await companion `cancelChat` + Escape/Cmd+. fallback; slot stays **Stopping** until `stop` or 8s timeout |

Hooks: `http://127.0.0.1:47821/hook`  
Companion: `~/.agent-cockpit/companion.json` → `http://127.0.0.1:47822`

Haptics (MX Master 4, Options+ haptic mapping): yellow → `knock`, green → `happy_alert`.

## Companion

```bash
cd ../agent-cockpit-companion
npm install && npm run build
npx vsce package --no-dependencies --out dist/agent-cockpit-companion.vsix
cursor --install-extension dist/agent-cockpit-companion.vsix
```

Reload Cursor — status bar should show `Cockpit:47822`.

Endpoints: `GET /v1/ping`, `POST /v1/open-composer`, `POST /v1/cancel-composer`, `POST /v1/decision`, `GET /v1/pending`.

## Pages 2–4 (productivity actions)

Assign the **9 actions** from each group onto keypad pages 2–4 (same as Orb–Hex on page 1). Page 1 is unchanged.

| Options+ group | Page | Actions (9) |
|---|---|---|
| **Cursor · Prompts** | 2 | Tests, Explain, Types, Refactor, Review, Fix, Agent, Chat, Edit |
| **Cursor · Shortcuts** | 3 | Cmd Palette, Quick Open, Sidebar, Terminal, Accept, Reject, @file, @sel, @diff |
| **Cursor · Studio** | 4 | Usage, Mode, Model, Hunk −, Hunk +, Accept hunk, Undo, Redo, Problems |

Edit prompts and chords in `~/.cursor-agent-cockpit/settings.json`. Usage persists at `~/.cursor-agent-cockpit/usage-metrics.json`.

Prompt / Agent / Chat keys **focus** the composer via the companion (they no longer send `Cmd+L` / `Cmd+I`, which toggle the pane closed). Clipboard is used only to insert text and is restored afterward. Reload Cursor after updating the companion.

**Studio Usage** press cycles prompts → tools → model → errors → sessions (from the same hook relay as page 1). **Mode** / **Model** apply the next configured item (companion `cycleMode` / `cycleModel` if no keystroke is set).

Continuous loops still belong on the **Dialpad** (Node `Cursor · Dial` group): Diff Hunks, Undo Timeline, Cycle Models, Fire Prompt, Diagnostics. Page 4 keys are the tap stand-ins.

## Build & load

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet build src/AgentCockpitPlugin.csproj -c Debug
```
