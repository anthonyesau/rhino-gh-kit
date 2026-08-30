#!/usr/bin/env bash
# Launch Claude Code with this clone loaded LIVE as the plugin.
#
#   tooling/dev.sh            # interactive session on the kit
#   tooling/dev.sh -p "…"     # one-shot; extra args pass straight through
#
# Why this exists: plain `claude` serves the kit through its *install entry*, so
# a skill you just added is invisible until the version is bumped and re-served.
# `--plugin-dir` bypasses all of that — no install, no version, no cache, and
# `/reload-plugins` applies an edit without restarting the session.
#
# Confirm it took: the session's plugin source reads `rhino-gh-kit@inline`
# rather than `rhino-gh-kit@rhino-gh-kit`.

set -euo pipefail
KIT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
exec claude --plugin-dir "$KIT" "$@"
