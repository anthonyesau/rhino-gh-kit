# Releasing the kit — developing, and delivering to consumers

Two different things, and confusing them is what makes kit changes feel
unreliable:

| you are | use | version bump? |
|---|---|---|
| **working on the kit** | `tooling/dev.sh` | no |
| **delivering it to consumers** | the install + a version bump | yes |

Only the second involves the cache, `claude plugin update`, or `--scope`. It is
maintainer material; a project that merely *uses* the kit needs none of it, and
[README.md](../../README.md) deliberately keeps only the install commands.

## Working on the kit

**Do not develop the kit through its install.** Launch with the clone loaded
directly:

```bash
tooling/dev.sh          # = claude --plugin-dir <this clone>
```

`--plugin-dir` is Claude Code's supported development path. The plugin is loaded
**live from the directory** — skills, hooks, commands and tooling are the files
on disk, so an edit is in effect immediately, with nothing copied and no version
involved. After an edit, `/reload-plugins` applies it **without restarting the
session**.

A `--plugin-dir` plugin takes precedence over an installed plugin of the same
name for that session, so this needs no uninstall and disturbs nothing.

**Check it took**, deterministically — the session's own init record names the
source:

| launched with | source | means |
|---|---|---|
| `tooling/dev.sh` | `rhino-gh-kit@inline` | live from the clone ✅ |
| plain `claude` | `rhino-gh-kit@rhino-gh-kit` | through the install entry |

Verified 2026-08-28 against Claude Code 2.1.247, reading that record rather than
asking a model: `--plugin-dir` reports `@inline`, and the plugin's
`UserPromptSubmit` hook fires from the clone too. A `pluginDirs` key in
`settings.json` does **not** work — it is silently ignored, so the flag (or this
launcher) is the only way.

That is the whole development loop. Everything below is about *shipping*.

## Delivering to consumers

**An install is version-gated, and that is what bites.** `claude plugin install`
copies the kit into `~/.claude/plugins/cache/<marketplace>/<plugin>/<version>/`
— distinct files, stamped at re-serve time, so a later edit in the clone is not
in them (re-verified 2026-08-28).

Which of the two a session actually reads depends on how the marketplace was
added, and this is the trap: from a **`directory`** marketplace the served path
is the source directory itself, so file *contents* look live — but the skill
**roster** and version still come from the install entry. That is why a
newly-added skill stays invisible until a re-serve even though editing an
existing one appears to work. From a **GitHub** marketplace — how consumers
install — the served path is the version cache and nothing is live.

**`claude plugin update` is version-gated**, so the version bump *is* the
release. With `plugin.json`'s `version` unchanged it short-circuits — "already at
the latest version" — without re-copying, and a bare `install` is a no-op once an
install entry exists, so an unbumped push reaches nobody. `tooling/hooks/pre-commit`
bumps the patch automatically when a commit touches a served path, which is what
keeps this from being something you have to remember.

### The procedure

Three commands, and **the order is load-bearing**:

```bash
git add -A && git commit -m "…"        # 1. pre-commit bumps plugin.json's version
git push                                # 2. reaches consumers on the GitHub URL
python3 tooling/kit_scopes.py --exec    # 3. re-serves your own install entries
```

Then **restart the session**. `/reload-plugins` refreshes a `--plugin-dir`
plugin, not a newly copied install, and a newly added skill never appears in a
running session.

Why the order:

- **Commit before re-serving — the version.** `claude plugin update` short-circuits when the version has not
  moved, so a re-serve with no bump has nothing to update to. Every "I updated
  the kit and nothing changed" is this.
- **Commit before re-serving — the contents.** A re-serve copies the **working tree**, not the
  committed state. Commit first and the served copy matches a commit; run it with
  edits outstanding and you have served something that exists in no commit.
- **Pushing is independent.** It is the only thing that reaches consumers installing
  from the GitHub URL, who have no clone. Step 3 only touches this machine.

`kit_scopes.py` exists because `claude plugin update` needs a `--scope` and
**guessing one is unsafe** — see
[One install, or one per project?](#one-install-or-one-per-project).

### Confirming it worked

**Gate on state, not on a command reporting success.**

```bash
python3 tooling/kit_scopes.py     # every entry: scope, version, project, staleness
```

If the version shown matches `.claude-plugin/plugin.json`, the serve landed. In a
session, the plugin *source* tells you which copy you are on —
`rhino-gh-kit@inline` is `dev.sh` serving the live clone,
`rhino-gh-kit@rhino-gh-kit` is the install entry.

Each version lands in its own cache directory; older ones are left behind and can
be deleted by hand.

## Editing the kit

The dev clone *is* canonical — never edit inside
`~/.claude/plugins/cache/`, which has no `.git`. Note that
`${CLAUDE_PLUGIN_ROOT}` does **not** always point there: for a `directory`-source
marketplace it resolves to the marketplace directory itself (your clone), while
for a GitHub-source marketplace it is the version cache. Write skills so they
work either way rather than depending on which.

**Observations from inside another project go to the repo's GitHub issues**, not
into a local note. An issue survives the session, is visible to someone else, and
is where a stranger would file the same thing.

## One install, or one per project?

`claude plugin install` takes a `--scope`, and the choice decides how much bookkeeping
every future release costs you:

| scope | entry lives in | serves |
| --- | --- | --- |
| `user` *(the CLI's default)* | `~/.claude/settings.json` | **every project on the machine** |
| `project` | `<project>/.claude/settings.json` | that project, and it's checked into git |
| `local` | `<project>/.claude/settings.local.json` | that project, gitignored |

**What `user` scope actually costs.** It makes the kit's commands and skills available in
every session on the machine, including projects that will never touch Grasshopper. That
is less than it sounds, because the expensive part of the kit does **not** travel with the
install:

| surface | delivered by | in an unrelated project |
| --- | --- | --- |
| `.claude/rules/` (kit-internal only, `tooling-python.md`) | never symlinked into any project | **absent** — the install never delivers it |
| skill and command *bodies* (~65 KB across five skills + one command) | loaded only when invoked | absent |
| skill and command *descriptions* | the plugin listing, every session | ~4 KB (≈1,000 tokens) |
| `gh-workflow-guard.sh` (`UserPromptSubmit` hook) | registered for every prompt in every session, wherever the plugin is enabled | one cheap subprocess exec per prompt (jq + a regex match); silent, no tokens spent, unless the prompt happens to match |
| agents, MCP servers | — | the kit ships none |

So the standing cost in a project that never uses the kit is the six frontmatter
`description` lines (five skills, one command) — roughly 1,000 tokens per session —
plus all five entries in the skill list being Grasshopper-flavored, plus the hook's
per-prompt exec. Every skill description names "Grasshopper" or "Script Forge", and
the hook's keyword set is Grasshopper/Rhino-specific, so spurious triggering is
unlikely, but the noise is real.

**Choose on how often you start new Grasshopper projects.** With a small, stable set of
them, per-project installs cost nothing elsewhere and
[`kit_scopes.py --exec`](../../tooling/kit_scopes.py) already updates all of them in one command
— the "only one repo got it" problem is solved without going machine-wide. Go `user` when
you spin up new projects often enough that per-project install bookkeeping is the thing
that keeps biting:

```bash
claude plugin install rhino-gh-kit@rhino-gh-kit --scope user
```

Per-project installs are the source of the recurring *"I updated the kit and only one repo
got it"* complaint. Each project gets its own entry, each entry is updated separately, and
the ones you forget sit on an old version indefinitely. Worse, the scopes are **invisible**
in the obvious place: plain `claude plugin list` prints one identical-looking block per
install, each with a scope and no directory. Guessing which is yours is not merely
unhelpful — a `--scope` with no entry for the current project does **not** fail. It applies
the update to whichever *other* project holds the only entry at that scope and reports
success. (Verified 2026-08-22: `--scope project` from a project with no project-scope entry
updated a different project's install instead.)

`claude plugin list --json` carries the field the human-readable form drops —
`projectPath`. [`tooling/kit_scopes.py`](../../tooling/kit_scopes.py) reads it, so the scope is
derived rather than guessed, and reports stale entries whose project directory is gone:

```bash
python3 tooling/kit_scopes.py              # every entry: scope, version, project, staleness
python3 tooling/kit_scopes.py --scope-for  # the scope serving cwd, or NONE (exit 1)
python3 tooling/kit_scopes.py --plan       # the re-serve commands for every live entry
python3 tooling/kit_scopes.py --exec       # run them, then verify each entry moved
```

**Migrating from per-project to `user`** — install once at `user` scope, then remove the
per-project entries so they stop shadowing it and stop needing updates:

```bash
claude plugin install rhino-gh-kit@rhino-gh-kit --scope user
python3 tooling/kit_scopes.py                          # list what's left
# for each remaining project/local entry:
(cd "<project>" && claude plugin uninstall rhino-gh-kit@rhino-gh-kit --scope <its scope>)
```

Then restart your sessions. The per-project `enabledPlugins` lines that `/plugin install`
wrote into each `.claude/settings.json` / `settings.local.json` are harmless once the
install entry is gone, but can be deleted too.

## Each re-serve banks another copy of your working tree

`claude plugin install` copies the **working tree**, not the tracked content —
gitignored build output included — and every version it has ever served stays
in `~/.claude/plugins/cache/rhino-gh-kit/rhino-gh-kit/<version>/` until you
delete it by hand. Nothing prunes them.

The gap is large, because one dev-only artifact dominates (measured 2026-08-27):

| | size |
|---|---|
| tracked content | **1.6 MB** |
| one served version | **73 MB** |
| …of which `script-forge/tooling/fixture-runner/bin/Release` | **70 MB** |

That directory is the self-contained build of the C# console harness
`test_fixtures.py` shells out to — never a shipped artifact, and never needed by
a session. Six accumulated versions came to 220 MB before they were pruned.

Two ways to keep it from growing, either is enough:

```bash
# 1. clean before a release re-serve — 73 MB becomes well under 2 MB
rm -rf script-forge/build script-forge/src/ScriptForge/{bin,obj} \
       script-forge/tooling/fixture-runner/{bin,obj}

# 2. or delete the previous version directory after each re-serve
ls  ~/.claude/plugins/cache/rhino-gh-kit/rhino-gh-kit/     # what's accumulated
rm -rf ~/.claude/plugins/cache/rhino-gh-kit/rhino-gh-kit/<old-version>
```

**Before deleting a version directory, confirm nothing serves it** —
`claude plugin list --json` carries `installPath`, which names the one directory
in use. Deleting the unreferenced ones is safe with sessions running; the served
one is not.

Rebuilding the harness after a clean costs nothing extra:
`test_fixtures.py` runs `dotnet build` on the runner itself unless you pass
`--no-build`, so the next fixture run restores it.
