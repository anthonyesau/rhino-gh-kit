#!/usr/bin/env bash
#
# Installs the pre-commit hook that auto-bumps .claude-plugin/plugin.json's
# patch version on a commit touching a cache-served path (skills/, commands/,
# docs/, tooling/, hooks/, examples/, script-forge/docs/, script-forge/tooling/).
# Git hooks aren't tracked, so this needs running once per clone — otherwise a
# commit ships to nobody: the version doesn't move and `claude plugin update`
# is a no-op for every consumer.

set -euo pipefail

KIT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

cp "$KIT/tooling/hooks/pre-commit" "$KIT/.git/hooks/pre-commit"
chmod +x "$KIT/.git/hooks/pre-commit"
echo "installed -> $KIT/.git/hooks/pre-commit"
