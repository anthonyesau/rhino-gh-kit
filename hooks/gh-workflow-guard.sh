#!/bin/bash
# UserPromptSubmit hook: inject the Grasshopper/Rhino MCP workflow guidance
# (docs/write-scripts/workflow.md) only when the prompt looks Grasshopper-flavored,
# and only once per session.
set -euo pipefail

# jq is a hard dependency below, but a missing tool should skip context
# injection, never break every prompt in the session.
command -v jq >/dev/null || exit 0

INPUT="$(cat)"
PROMPT="$(jq -r '.prompt // ""' <<<"$INPUT")"
SESSION_ID="$(jq -r '.session_id // "unknown"' <<<"$INPUT")"

MARKER="/tmp/rhino-gh-kit-workflow-guard-${SESSION_ID}"
if [[ -f "$MARKER" ]]; then
  exit 0
fi

PATTERN='rhino|grasshopper|mcp__rhino|script forge|script component|scriptforge|rhino-gh-kit|gh_meta|run_csharp|runscript|@component|\.gh\b|\bforge\b|\bcanvas\b'
if ! grep -qiE "$PATTERN" <<<"$PROMPT"; then
  exit 0
fi

DOC="${CLAUDE_PLUGIN_ROOT}/docs/write-scripts/workflow.md"
if [[ ! -f "$DOC" ]]; then
  exit 0
fi

touch "$MARKER"

jq -n --rawfile ctx "$DOC" \
  '{hookSpecificOutput: {hookEventName: "UserPromptSubmit", additionalContext: $ctx}}'
