# Cursor Agent Cockpit — Logitech MX Creative Console Plugin

A developer-productivity plugin for the Logitech MX Creative Console, targeting **Cursor**.
Built on the [Logi Actions SDK (Node.js)](https://logitech.github.io/actions-sdk-docs/nodejs/introduction/).

Status: **in progress**

- **Agent Cockpit (C#)** — `agent-cockpit-plugin/` (live LCD A1–A6 + Approve/Deny/Kill, owns hook relay `:47821`)
- **Productivity / Context / Dial (Node)** — `cursor-agent-cockpit/`

---

## 1. Platform constraints (researched, 2026-08-08)

### SDK primitives

The Actions SDK exposes exactly two action types:

| Type | Trigger | Callback | Notes |
|---|---|---|---|
| `CommandAction` | Button press | `onKeyDown()` | Discrete, one press = one result |
| `AdjustmentAction` | Dial / roller rotation | `execute(event)` | `event.tick` gives rotation magnitude |

Both require `name`, `displayName`, `description`, `groupName`, and registration before connect:

```js
const pluginSDK = new PluginSDK();
pluginSDK.registerAction(new MyAction());
await pluginSDK.connect();
```

### Target hardware

- **MX Creative Console — Keypad**: 9 programmable LCD buttons
- **MX Creative Console — Dialpad**: rotary dial + roller + buttons
- **Actions Ring**: 8-button virtual overlay (secondary target)
- **MX Master 4**: haptics sink — plugins can push tactile feedback

### Runtime

Plugins are Node processes managed by **Logi Plugin Service**, distributed as `.lplug4`.
npm packages are explicitly supported → `child_process`, `fs`, and sockets are available.
Scaffold with `npx @logitech/plugin-toolkit create <plugin-name>`.

### Design consequence

The only things justifying a keypad over a plain keybinding are:
1. **the LCD rendering live state**, and
2. **the dial doing continuous work**.

Weight every feature against that test. "Button sends Cmd+K" is a keybinding with extra steps.

---

## 2. Architecture decision: how the plugin talks to Cursor

| Path | Gets you | Cost |
|---|---|---|
| Synthetic keystrokes (nut.js / robotjs / osascript) | Fires any Cursor command | Zero setup, but **write-only** — no state for the LCDs |
| **Cursor Hooks** ⭐ | Real agent lifecycle events | `hooks.json` + a small relay script |
| Companion VS Code extension (Cursor accepts `.vsix`) | Diagnostics, git, editor, terminal, `executeCommand` | Ship two artifacts |
| CLI / filesystem (`cursor-agent`, git, watchers) | Repo & test state | Easy, coarse |

**Chosen design: Hooks are the read channel, keystrokes are the write channel.**

[Cursor Hooks](https://cursor.com/docs/hooks.md) shipped in Cursor 1.7. Hooks run as standalone
processes, receive JSON on stdin, return JSON on stdout, and exit with a status code that tells
Cursor whether to proceed or block.

Available events (subset we consume):

```
sessionStart / sessionEnd
beforeSubmitPrompt
preToolUse / postToolUse / postToolUseFailure
beforeShellExecution / afterShellExecution
stop
afterAgentThought / afterAgentResponse
```

So: a ~20-line hook script POSTs each event to `localhost:PORT`; the Logitech plugin runs the
listener. No reverse-engineering, no polling, no scraping Cursor's internal state.

Add the companion extension **only** if Pillar 4 (dashboard) makes the cut.

---

## 3. Feature catalog

### Pillar 1 — Agent Cockpit (flagship)

Solves the real pain: fire off an agent, tab away to Slack, no idea if it finished, stalled, or is
waiting on you.

- **Ambient status key** — one LCD as a desk status light: idle / thinking / awaiting-approval /
  done. Driven by `sessionStart` → `stop`.
- **Haptic completion pulse on MX Master 4** — hand is already on the mouse. Highest
  delight-per-line-of-code in the project; almost nobody uses this hook.
  *(Node SDK: not exposed yet — C# only via `PluginEvents.RaiseEvent`. Stubbed.)*
- **Elapsed-time / tool-count badge** on the status key, from `preToolUse` counts.
- **Kill switch** — abort a runaway agent.
- **Approve-shell-command key** — pairs with `beforeShellExecution` blocking.

### Pillar 2 — Dial work (best differentiator)

Reviewing AI diffs *is* a rotate-and-approve loop. Maps to a dial better than any keyboard.

- **Diff hunk scrubber** — rotate = next/prev hunk, press = accept hunk. Most defensible idea here.
- **Checkpoint / undo timeline scrub** — rotate backwards through agent edits after a bad
  generation. Post-agent recovery is currently a fumbling experience.
- **Diagnostics walker** — rotate through errors/warnings; LCD shows count as a badge.
- **Partial-accept Tab** — rotate to accept a completion word-by-word.

### Pillar 3 — Prompt & context deck

- **Canned prompt keys** — "write tests for this file", "explain this", "add types". Press →
  focus Composer, type, submit. Must be **configurable in plugin settings**, not hardcoded.
- **Context builders** — one-press `@file` / `@selection` / `@git diff` / terminal output.
- **Saved context bundles** — pin a named set of files, one key each.
- **Model / mode switcher** — Agent vs Ask, Sonnet vs Opus, current choice rendered on the key.

### Pillar 4 — Ambient dashboard (needs companion extension)

Test pass/fail count, error badge, git branch + dirty count, CI status via `gh`, dev-server
up/down. Cheap to add once the extension exists; each is one more key.

### Pillar 5 — Flow (cut unless time permits)

Deep-work key: Zen mode + DND + timer on LCD. Pleasant, but not Cursor-specific.

### Pillar 6 — Productivity Suite (added)

Desk-level Cursor productivity controls that belong on the keypad / dial, not as more IDE
keybindings. Ships alongside Pillars 1–3.

#### 6.1 Keyboard Shortcuts

Configurable chord keys. Each slot maps a display label → a keystroke sequence (e.g.
`Cmd+Shift+P`, `Cmd+P`, `Cmd+B`). Settings-driven so users bind their own Cursor / OS shortcuts
without forking the plugin.

Why a keypad key beats a keyboard shortcut: the LCD shows the *purpose* ("Toggle Sidebar",
"Command Palette") instead of a chord you have to remember.

#### 6.2 Prompt-based shortcuts

Named prompts fired into Composer / Agent chat in one press:

- Configurable list in `settings.json` (`id`, `label`, `prompt`, `submit`, `target`)
- Optional auto-submit
- Target: Agent (`Cmd+I` / Agent) vs Chat (`Cmd+L`) vs inline edit (`Cmd+K`)

Distinct from raw keyboard shortcuts: the payload is natural-language intent, not a chord.

#### 6.3 AI Usage Metric

Live usage badge driven by the hook relay — the LCD earns its place:

| Metric | Source hooks |
|---|---|
| Prompts today | `beforeSubmitPrompt` |
| Tool calls (session / day) | `preToolUse` / `postToolUse` |
| Active / completed sessions | `sessionStart` / `sessionEnd` / `stop` |
| Current model | hook payload `model` / `model_id` |
| Errors / failures | `postToolUseFailure`, `stop.status` |
| Elapsed thinking time | `sessionStart` → `stop` |

Press the Usage key to cycle the metric shown on the LCD (prompts → tools → model → errors).

#### 6.4 Model / Mode Switch

- **Mode cycle**: Agent ↔ Ask ↔ Edit (from `sessionStart.composer_mode` when known)
- **Model cycle**: walk a user-configured list (Sonnet / Opus / Auto / …)
- Write channel = configured keystrokes / command-palette sequences
- LCD renders the currently selected mode + model slug

#### 6.5 Continuous-usage surfaces (dial / roller)

Identified continuous loops that justify the dial over discrete keys:

| Continuous loop | Control | Behavior |
|---|---|---|
| Diff review | Dial | Next/prev hunk; press = accept hunk |
| Undo / checkpoint timeline | Roller | Scrub agent edits backward/forward |
| Model palette | Dial | Rotate through configured models; press = apply |
| Prompt library | Dial | Rotate through saved prompts; press = fire |
| Usage history | Dial | Scrub recent sessions / metric windows on the Usage key |
| Diagnostics | Dial | Walk errors/warnings (Pillar 2) |
| Partial Tab accept | Dial | Accept completion word-by-word |

Rule of thumb: if the user would otherwise mash the same key N times, it belongs on the dial.

---

## 4. MVP scope

**Pillars 1 + 2 + Productivity Suite (Pillar 6), with Pillar 3 keys as filler.**

Proposed Agent Cockpit keypad layout (C# plugin):

```
┌──────────┬──────────┬──────────┐
│ Approve  │   Deny   │   Kill   │   <- control active agent
├──────────┼──────────┼──────────┤
│    A1    │    A2    │    A3    │   <- up to 6 agents, live LCD
├──────────┼──────────┼──────────┤
│    A4    │    A5    │    A6    │
└──────────┴──────────┴──────────┘
  Default active = last hook event
  Press An = pin that agent (●); press again = unpin → last-event
```

Productivity Suite / Context / Dial stay on the Node plugin for a second profile or Dialpad.

Deliverables:
1. The `.lplug4` plugin (`cursor-agent-cockpit/`)
2. `hooks.json` + relay script (installed into the user's Cursor config)
3. Companion `.vsix` extension — **optional**, only if Pillar 4 is in scope

Demo narrative: fire an agent → walk away → see Usage / Agent update on the keypad → spin the
dial to review the diff → switch model for the next pass.

---

## 5. Open questions — status

1. **Can the Node SDK dynamically update a key's LCD at runtime?**
   **Partial yes (text).** Internals expose `GetActionText` (returns `action.displayName`) and an
   `ActionTextChanged` protocol event. `GetActionImage` currently always returns `null` — no
   dynamic images in Node beta. Strategy: mutate `displayName` for live labels; best-effort push
   `ActionTextChanged` if LPS honors it. C# still wins for rich bitmap LCDs.

2. **Do plugin-sent keystrokes reliably reach Cursor?**
   macOS accessibility permissions apply. MVP uses `osascript` / System Events; verify with
   Cursor focused vs Logi Plugin Service owning foreground context.

3. Does the Actions SDK expose the MX Master 4 haptics API from a Node plugin, or C# only?
   **C# only today** (`PluginEvents.AddEvent` / `RaiseEvent` + `HasHapticMapping`). Node SDK
   has no haptics surface in `0.1.1`. Defer or dual-ship a thin C# haptics companion later.

---

## 6. Reference links

- Actions SDK — Node.js intro: https://logitech.github.io/actions-sdk-docs/nodejs/introduction/
- Creating an action: https://logitech.github.io/actions-sdk-docs/nodejs/creating-action/
- Plugin basics: https://logitech.github.io/actions-sdk-docs/plugin-basics/
- Supported devices: https://logitech.github.io/actions-sdk-docs/supported-devices/
- Working with assets: https://logitech.github.io/actions-sdk-docs/nodejs/working-with-assets/
- Haptics (C#): https://logitech.github.io/actions-sdk-docs/csharp/haptics/haptics-overview/
- Marketplace approval guidelines: https://logitech.github.io/actions-sdk-docs/marketplace-approval-guidelines/
- Example plugin repo: https://github.com/Logitech/actions-sdk
- Logi Developer Discord: https://discord.gg/ptV2BfHCmm
- Cursor Hooks docs: https://cursor.com/docs/hooks.md
