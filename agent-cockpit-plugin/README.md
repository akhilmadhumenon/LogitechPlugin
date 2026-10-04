# Agent Cockpit plugin

C# Logi Actions plugin for the MX Creative Console. Start from the [root README](../README.md) for install order.

## Groups (Logi Options+)

| Group | Typical page | Actions |
|---|---|---|
| **Cursor · Agent Cockpit** | 1 | Run / Always / Kill + six agent glyphs |
| **Cursor · Prompts** | 2 | Tests, Explain, Types, Refactor, Review, Fix, Agent, Chat, Edit |
| **Cursor · Shortcuts** | 3 | Command palette, Quick Open, Sidebar, Terminal, Accept, Reject, @file, @sel, @diff |
| **Cursor · Studio** | 4 | Usage, Mode, Model, hunks, Accept hunk, Undo, Redo, Problems |

## Agent page

```
[ Run / Switch / Approve ] [ Always / Deny ] [ Skip / Kill ]
[  Orb ] [ Diamond ] [ Triangle ]
[ Target ] [ Spark ] [ Hex ]
```

Top row follows the **pinned** agent’s pending decision.

| Pending | Key 1 | Key 2 | Key 3 |
|---|---|---|---|
| None | Approve (dim) | Deny (dim) | **Kill** (when pinned) |
| Shell / MCP / WebFetch | **Run** | **Always Run** | **Skip** |
| Switch mode | **Switch** | (dim) | **Skip** |

Sandboxed shells only pulse yellow while running; they do not arm Run / Skip.

| Control | Behavior |
|---|---|
| Short-press glyph | Pin + open that chat (companion) |
| Long-press glyph | Unpin (chat stays open) |
| Run / Always / Skip / Switch | Resolve the hook gate and/or companion decision |
| Kill | Companion `cancelChat`, then Escape / Cmd+. ; slot stays brown until `stop` or 8s |

## Usage tile

Studio **Usage** cycles **tokens → prompts → tools → model → errors → sessions**.

Tokens are `input_tokens + output_tokens` from Cursor `stop` / `afterAgentResponse` (same `generation_id` is not double-counted). Color uses `tokenUsage` in `~/.cursor-agent-cockpit/settings.json`:

```json
"tokenUsage": { "warn": 1000000, "high": 10000000, "excludeCacheReads": false }
```

## Develop

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet build src/AgentCockpitPlugin.csproj -c Debug
```

Build copies package metadata, writes the `.link` file under Logi Plugin Service, and triggers `loupedeck:plugin/AgentCockpit/reload`.

```bash
curl -s http://127.0.0.1:47821/health
./scripts/install-hooks.sh
```

Hook relay: `http://127.0.0.1:47821/hook`  
Companion discovery: `~/.agent-cockpit/companion.json`
