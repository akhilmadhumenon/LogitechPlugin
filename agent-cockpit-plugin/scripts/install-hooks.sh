#!/usr/bin/env bash
# Install the Cursor hook relay into ~/.cursor so Agent Cockpit receives events.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
RELAY_SRC="$ROOT/hooks/relay.mjs"
CURSOR_DIR="${HOME}/.cursor"
HOOKS_DIR="${CURSOR_DIR}/hooks"
RELAY_DEST="${HOOKS_DIR}/agent-cockpit-relay.mjs"
HOOKS_JSON="${CURSOR_DIR}/hooks.json"

mkdir -p "$HOOKS_DIR"
cp "$RELAY_SRC" "$RELAY_DEST"
chmod +x "$RELAY_DEST"

python3 - "$HOOKS_JSON" <<'PY'
import json, sys
from pathlib import Path

path = Path(sys.argv[1])
events = [
    "sessionStart",
    "sessionEnd",
    "beforeSubmitPrompt",
    "preToolUse",
    "postToolUse",
    "postToolUseFailure",
    "beforeShellExecution",
    "afterShellExecution",
    "beforeMCPExecution",
    "afterMCPExecution",
    "stop",
    "afterAgentThought",
    "afterAgentResponse",
]
gated = {"beforeShellExecution", "beforeMCPExecution"}
entry = {"command": "./hooks/agent-cockpit-relay.mjs"}
shell = {**entry, "timeout": 30}

data = {"version": 1, "hooks": {}}
if path.exists():
    try:
        data = json.loads(path.read_text())
        data.setdefault("hooks", {})
    except json.JSONDecodeError:
        data = {"version": 1, "hooks": {}}

def is_legacy(h):
    cmd = str(h.get("command", "")) if isinstance(h, dict) else ""
    return "cursor-agent-cockpit-relay" in cmd

for event in events:
    lst = data["hooks"].get(event) or []
    if not isinstance(lst, list):
        lst = [lst]
    lst = [h for h in lst if not is_legacy(h)]
    already = any(
        isinstance(h, dict)
        and "agent-cockpit-relay" in str(h.get("command", ""))
        for h in lst
    )
    if not already:
        lst.append(dict(shell if event in gated else entry))
    data["hooks"][event] = lst

data["version"] = data.get("version") or 1
path.write_text(json.dumps(data, indent=2) + "\n")
print(f"Updated hooks → {path}")
PY

echo "Installed relay → $RELAY_DEST"
echo "Reload Cursor if events do not appear immediately."
