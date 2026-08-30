# rhino-gh-kit

## What this is

**rhino-gh-kit** lets you author Rhino 8 Grasshopper "C# Script" and "Python 3
Script" components as ordinary files on disk instead of editing them by hand
on the canvas. Write a component as a `.cs` or `.py` file with a header
declaring its name, params and types, then drive **Script Forge** — a
compiled Grasshopper component this repo builds — to push it onto a live
canvas, creating or updating the component in place with type hints,
per-param tooltips and the icon all synced from that one header. The same
header format is also read by a compiler, so a finished set of components can
be built into a standalone plugin when you want to ship one.

**Two things live in this repo**, built from one source tree and installed
independently of each other:

| | What it is | You install it as | Start at |
|---|---|---|---|
| **Script Forge** | A compiled Grasshopper component that creates and updates *other* C# / Python 3 script components from source text. | a Grasshopper plugin (`ScriptForge.gha`, via Yak) | [docs/use-the-forge/](docs/use-the-forge/) |
| **The script kit** | Skills, the `@component` header format, and tooling for writing your own Rhino 8 **inline** script components — the kind you paste into Grasshopper's built-in "C# Script" / "Python 3 Script" component rather than compile — plus the pipeline for compiling them into a plugin of your own when you do want one. | a Claude Code plugin (this repo is also its own marketplace) | [docs/write-scripts/](docs/write-scripts/) |

Licensed under the [MIT License](LICENSE).

## The skills

| Skill | Purpose | Coupling |
|-------|---------|----------|
| `forge-push` | **The authoring path.** Drive an on-canvas Script Forge to create or update script components from `.cs`/`.py` — params, type hints, access, defaults, identity, per-param tooltips and icon, all synced from the `@component` header, N components per pass. | needs Script Forge installed |
| `set-param-value` | Write a value into any param or input object — panels, sliders, value lists, toggles, component inputs by name, Rhino geometry references. One payload, `tooling/set-param-value.cs`. | fully general |
| `ship-plugin` | Build, version, install and release a **compiled** plugin (`.gha`/`.yak`) — `publish.sh`'s stages, `release.sh`'s guards, which of the three version numbers a change moves, and how to tell which build Rhino actually loaded. | needs a `tooling/publish.conf` |
| `write-csharp-script` | C# Script component authoring rules — the class wrapper, `out`-param quirk, `RunScript`-rewrite, the durable identity slots, the `default`-vs-presence-sensed decision, and more. Invoked by intent (creating/editing a `.cs` script body), not path-scoped, so it applies to greenfield authoring too. | fully general |
| `write-python-script` | Python 3 Script component authoring rules — script mode vs. SDK mode, outputs as module-level assignments, the input/output shared-label allowance Python has and C# doesn't, unwired inputs and `NameError`, `MarshOutputs` vs. output hints, params as plain attributes. Same trigger-by-intent pattern as its C# sibling. | fully general |

> **`forge-push` is the only path that pushes source**, and it stops if Script
> Forge is absent from the canvas — there is no reflection fallback. One forge
> pass does create-or-update, native param sync with type hints,
> identity/tooltip/icon stamping, Python 3 as well as C#, and tree fan-out to N
> components at a time. It is also re-runnable from the canvas by the user, with
> no agent in the loop.
>
> Script Forge is a compiled Grasshopper plugin built from this repo's
> `script-forge/` folder but **installed separately**: building the `.gha` is
> `tooling/publish.sh --repo script-forge install`, orthogonal to installing this
> Claude Code plugin.
>
> Where a component's Name / NickName / Description / icon actually live, and
> which of the paired slots survives a save, is
> [docs/write-scripts/identity-properties.md](docs/write-scripts/identity-properties.md)
> — reference material describing what the forge implements, not instructions for
> stamping by hand.

### Quickstart

[`examples/list-stats.cs`](examples/list-stats.cs) is a real component this repo
ships — three numbers in (a list, a rounding digit, an ignore-zeros flag), three
statistics out. With Rhino running, `MCPStart` already run, and Script Forge on
the canvas (see [Prerequisites](#prerequisites) and [Install](#install-once-per-machine)
below):

1. Check the header before pushing anything — this is the one command a
   session runs by hand; everything after it is the agent driving the canvas:

   ```bash
   python3 "${CLAUDE_PLUGIN_ROOT}/tooling/gh_meta.py" --all --check --root examples
   ```

2. Ask Claude Code to forge `examples/list-stats.cs` onto the canvas. That
   invokes `forge-push`, which finds or places a Script Forge, sets its
   `Source` to the file path, and pulses `Run`.
3. A "List Stats" component appears with three inputs, three outputs, a
   per-param tooltip on each, and its icon — every bit of that read from the
   `@component` header at the top of the file, not configured by hand.

## Prerequisites

- **Rhino 8**, with a Grasshopper document open.
- **Claude Code**, either with this repo installed as a plugin (see
  [Install](#install-once-per-machine)) or served live from a clone with
  `tooling/dev.sh` if you're developing the kit itself.
- **McNeel's Rhino MCP Platform**, installed per machine from Rhino's Package
  Manager and registered under your own Claude Code config as `rhino` — the
  kit bundles no `.mcp.json` of its own. See
  [docs/write-scripts/rhino-mcp-platform.md](docs/write-scripts/rhino-mcp-platform.md).
- **`MCPStart`**, run once per Rhino session. Until then every MCP tool call
  fails with *"Could not connect to Rhino"* — that's the normal first failure
  of a session, not a bug.
- **`jq`**, on the `PATH` of whatever runs Claude Code — the `gh-workflow-guard.sh`
  hook needs it to inspect a prompt. A missing `jq` just skips that hook's context
  injection; nothing else in the kit depends on it.
- **Script Forge**, if you want source to reach the canvas at all — see
  [Install](#install-once-per-machine) below. There is no reflection fallback.
- **macOS**, only if you also want to compile a finished set of components into
  a standalone plugin — the build tooling (`tooling/publish.sh`, `gh_codegen.py`)
  assumes a macOS toolchain. Authoring and pushing script components needs none
  of this.

## Install (once per machine)

### The Claude Code plugin

**Dev machine** (where you make kit changes) — **do not install it.** Point Claude
Code straight at the clone:

```bash
git clone https://github.com/anthonyesau/rhino-gh-kit.git ~/rhino-gh-kit
cd ~/rhino-gh-kit
tooling/dev.sh                                                # = claude --plugin-dir .
```

> `--plugin-dir` loads the plugin **live from disk** — skills, hooks and tooling
> are the files you are editing. No install, no version bump, no cache, no
> restart: after an edit, `/reload-plugins` picks it up in the running session.
> A `--plugin-dir` plugin also takes precedence over an installed one of the
> same name, so this works without uninstalling anything.
>
> Installing is how *consumers* get the kit, and it is the only place versions,
> the cache and `--scope` matter — see
> [docs/maintain-the-kit/kit-releases.md](docs/maintain-the-kit/kit-releases.md).
> Never iterate through the install loop.

**Other machines / read-only consumers** — install from the GitHub URL instead:

```bash
claude plugin marketplace add anthonyesau/rhino-gh-kit        # github
/plugin install rhino-gh-kit
```

This clones a **managed, read-only** copy under `~/.claude/plugins/`; don't hand-edit it
(updates overwrite). Pull new versions with `/plugin update`.

### Script Forge

Download the latest `.yak` from this repo's [Releases](../../releases) page,
then install it from that folder:

```bash
yak install --source ~/Downloads ScriptForge
```

`yak install` needs a `--source` naming the folder holding the `.yak` — it is
not on the public Yak server (`yak search ScriptForge` finds nothing there), by
design; see [docs/ship-a-plugin/publishing.md](docs/ship-a-plugin/publishing.md#cutting-a-release).
Restart Rhino afterward — there is no hot reload on macOS.

**Building it yourself** is the contributor path, not the only one:

```bash
tooling/publish.sh --repo script-forge install
```

run from a clone of this repo, then restart Rhino. See
[docs/use-the-forge/script-forge.md](docs/use-the-forge/script-forge.md).

## Adopt the kit in a project

From the project root:

```
/rhino-gh-kit-init
```

This creates `icons/` and (optionally) drops a starter component. There is no rule to
symlink any more: the `write-csharp-script` / `write-python-script` skills and the
`gh-workflow-guard.sh` hook all come with the plugin install — the skills apply even when
Claude *creates* a new component from scratch (unlike a path-scoped rule, which only loads
on file read), and the hook
injects the MCP/workflow guidance on the first Grasshopper-flavored prompt of a session.
Validate headers from the project root with:

```bash
python3 "${CLAUDE_PLUGIN_ROOT}/tooling/gh_meta.py" --all --check
```

---

Everything below is reference material — repo layout, the delivery model, and
the details a project needs once it's set up, not the onboarding path above.

They meet at one place: the **`@component` header**, the block at the top of a
`.cs`/`.py` source that declares its name, params, type hints, tooltips and
icon. Script Forge reads it to build a live component; `gh_meta.py` validates
it; `gh_codegen.py` compiles it. That grammar is
[docs/write-scripts/header-reference.md](docs/write-scripts/header-reference.md).

**Where things live** follows one rule: **`script-forge/` is for *building* the
component; `docs/` is for *using* either product.** Inside `docs/`, the folder
name is the job you are doing — see [docs/README.md](docs/README.md).

**Script Forge itself is cross-platform** — `script-forge.cs` runs on Windows
as well as macOS, including its icon handling (a `.png` path or an embedded
`base64:`/`data:` payload works on both; only SVG rasterizing falls back, with
a warning, where macOS `sips` isn't available). **The build tooling that
compiles it is macOS-oriented and out of scope for portability** —
`tooling/publish.sh`, `gh_codegen.py`, and the Claude Code skills assume a
macOS toolchain (`/Applications/Rhino 8.app`, `~/Library/Application
Support/…`) and are not written to run on Windows.

## Layout

| Path | What it is |
|------|-----------|
| `.claude-plugin/plugin.json` | Plugin manifest (name, version, description). |
| `.claude-plugin/marketplace.json` | Makes this repo its own marketplace, hosting the one plugin. |
| `skills/` | Five Claude Code skills (above), auto-discovered when the plugin is installed. |
| `commands/` | `rhino-gh-kit-init` — adopt the kit in a project. |
| *(no `.mcp.json`)* | Deliberately absent. The kit talks to McNeel's own **Rhino MCP Platform**, registered per machine under the user's own config; a bundled project-scope entry would shadow it. See [MCP server](#mcp-server). |
| `hooks/` | `gh-workflow-guard.sh`, a `UserPromptSubmit` hook that injects `docs/write-scripts/workflow.md` as context — but only on a Grasshopper-flavored prompt, once per session — plus `hooks.json` declaring it. Auto-discovered with the plugin install, no per-project step. |
| `.claude/rules/` | One kit-internal rule: `tooling-python.md`, path-scoped to `tooling/*.py`, never symlinked into consumers. Consumer-facing guidance belongs in the `write-csharp-script` / `write-python-script` skills or the `hooks/` guard above, not here. |
| `tooling/` | `gh_meta.py` (header parser), `gh_codegen.py` (script sources → a buildable `.gha`), `publish.sh` (the build pipeline), `release.sh` (tag + GitHub Release with the `.yak`), `check_filenames.py` (the filename gate), `set-param-value.cs`, `build-forge-rig.cs`, `kit_scopes.py` (kit maintenance), and `templates/` — the three shim types `gh_codegen.py` copies into a generated build. Single-sourced here; run via `${CLAUDE_PLUGIN_ROOT}`. See [tooling/README.md](tooling/README.md). |
| `docs/` | Four folders named for the job you're doing: [`use-the-forge/`](docs/use-the-forge/), [`write-scripts/`](docs/write-scripts/), [`ship-a-plugin/`](docs/ship-a-plugin/), [`maintain-the-kit/`](docs/maintain-the-kit/). Only the last is internal to this repo. Index: [docs/README.md](docs/README.md). |
| `CLAUDE.md` | Thin overview pointing at `skills/`, `hooks/` and `.claude/rules/`, plus what's specific to Script Forge as a shipped component. |
| `script-forge/` | Everything needed to **build** Script Forge — see the next table. Nothing about *using* it: that's `docs/use-the-forge/`. |
| [`examples/`](examples/) | ~30 demo components (plus `list-stats.cs`, the canonical skeleton cited by the `write-csharp-script` skill) that Script Forge builds — forged only, never compiled, with icons in `examples/icons/` and the worked-examples canvas (`examples/script-forge-examples.gh`) saved beside them so the headers' bare `icons/<name>.svg` resolves. Kit-wide, shared with the authoring skills, so it stays out of `script-forge/`. A fuller build is walked through in [`examples/native-ceiling/`](examples/native-ceiling/). |

### `script-forge/` — building the component

| Path | What it is |
|------|-----------|
| `script-forge/script-forge.cs` | The published component — the only `.cs` directly inside `script-forge/`, and the single canonical source `gh_codegen.py` compiles into `ScriptForge.gha`. |
| `script-forge/script-forge.svg` (+`-dark`) | Its canvas/package icon, sitting beside the source rather than in an `icons/` subfolder. |
| `script-forge/src/`, `script-forge/yak/` | Script Forge's `.gha` project and Yak package manifest — built by the kit-wide pipeline, see [docs/ship-a-plugin/dotnet-build.md](docs/ship-a-plugin/dotnet-build.md) and [docs/ship-a-plugin/publishing.md](docs/ship-a-plugin/publishing.md). |
| `script-forge/docs/` | The component's own internals: `forge-under-test.md`, `known-limitations.md`. Everything about *driving* the forge is in [docs/use-the-forge/](docs/use-the-forge/) instead. |
| `script-forge/audit-fixtures/` | Deliberately malformed / edge-case sources, pinned against both `gh_meta.py` and Script Forge's own header parser by `python3 script-forge/tooling/test_fixtures.py` — see [docs/ship-a-plugin/dotnet-build.md](docs/ship-a-plugin/dotnet-build.md), "Testing the header parsers". |
| `script-forge/tooling/` | Script Forge's own build knobs and dev-only tooling — `publish.conf`, `clean-forge-state.cs`, `test_fixtures.py`, `fixture-runner/`. The shared engine they call (`publish.sh`, `gh_codegen.py`, `gh_meta.py`) stays kit-wide in the top-level `tooling/`. |

## The model

Everything reusable — skills, tooling, docs, rules, hooks — is **single-sourced in
this repo** and delivered to every project by the plugin. Nothing is copied into consuming
projects, and nothing is symlinked either: a consuming project holds only its own component
sources (`*.cs`) and its `icons/`. Two consequences:

- Adopting the kit in a new project is one command (`/rhino-gh-kit-init`): it scaffolds `icons/`
  and checks prerequisites. There are no rule symlinks to create or gitignore.
- "Improve a skill/tool/rule/hook mid-project and sync it back" is one uniform workflow —
  you edit the clone, push, and re-serve it. There is no per-artifact copy-back step. Skills,
  commands, docs and hooks all reach sessions through the install cache, so they need the
  update-and-restart loop in
  [docs/maintain-the-kit/kit-releases.md](docs/maintain-the-kit/kit-releases.md)
  (verified 2026-08-25: editing `hooks/` in the clone did **not** reach a fresh session
  until a re-serve); only clone-path tooling invocations (`$KIT/tooling/...`) are live
  immediately.

## Per-project setup the tooling expects

- `.cs` component sources live at the **project repo root**, each carrying an `@component`
  header. The canonical spec is
  [`docs/write-scripts/header-reference.md`](docs/write-scripts/header-reference.md)
  — it covers both the forged and the compiled path, and its appendix covers
  running `gh_meta.py` over it.
- `icons/` holds one SVG per component (kebab-case of the name) plus a `<stem>-dark.svg`
  dark-theme variant — conventions and the `sips` rasterizing recipe are in
  [docs/write-scripts/icons.md](docs/write-scripts/icons.md).
- **Source stem == icon stem == kebab of the header `name`**, and every tracked path
  stays POSIX-portable (no spaces, no non-ASCII). `publish.sh` gates on this via
  `tooling/check_filenames.py`; the convention and its rationale are in
  [docs/ship-a-plugin/file-naming.md](docs/ship-a-plugin/file-naming.md).
- Publishing builds a `.gha` with `dotnet` straight from the root `.cs`
  ([docs/ship-a-plugin/publishing.md](docs/ship-a-plugin/publishing.md)), so a project that ships needs `src/` +
  `yak/manifest.yml` and a `tooling/publish.conf` naming them. Don't copy the kit's
  Python into the consuming project — the tooling is single-sourced here and invoked
  through `${CLAUDE_PLUGIN_ROOT}`.

## Improve the kit

Edit the kit files in your dev clone — that clone *is* canonical — and run
`tooling/dev.sh` from it so the session serves those files directly.
`/reload-plugins` applies an edit without a restart. Nothing needs installing,
versioning or re-serving to try a change.

Run `tooling/install-hooks.sh` once per clone. Git hooks aren't tracked, so a
fresh clone otherwise gets no version gating at all: `tooling/hooks/pre-commit`
is what bumps `.claude-plugin/plugin.json`'s patch version on a commit touching
a cache-served path, and without it installed a commit ships to nobody.

Shipping that change to consumers is a separate act: bump, commit, push. That
loop, and the `--scope` trap that makes "I updated the kit and only one repo got
it" a recurring complaint, is
[docs/maintain-the-kit/kit-releases.md](docs/maintain-the-kit/kit-releases.md).

**Observations from inside another project go to the repo's GitHub issues**, not into a
local note. An issue survives the session, is visible to someone else, and is where a
stranger would file the same thing.

## Two languages, two skills

Per-language rules live only in the language skill — `write-csharp-script` and
`write-python-script`. Everything shared is elsewhere and language-agnostic: the
`gh-workflow-guard.sh` hook (MCP guard, metadata-header workflow), the header grammar
(`gh_meta.py` reads `/* @component */`, `"""@component"""` and `# @component` alike), and
`forge-push`, which pushes `.cs` and `.py` in the same pass. Both skills are invoked by
intent rather than symlinked, so neither needs a `/rhino-gh-kit-init` step. Only the compiled
path is C#-only: `gh_codegen.py` never processes a `.py` source
(`docs/ship-a-plugin/dotnet-build.md`, "Scope: C# only").

## MCP server

One server: McNeel's first-party **Rhino MCP Platform**, installed per machine from
Rhino's `PackageManager` and registered under the user's own config as `rhino`. The tools
are plainly `mcp__rhino__*`. Everything the kit does that isn't placing a component goes
through `run_csharp`.

**The kit bundles no `.mcp.json`, deliberately.** Project scope shadows user scope, so a
project-level `rhino` entry would silently hide the machine's Platform registration.

Two things must be true before a live canvas can be driven, and neither is implied by the
tools appearing in the session:

- **`MCPStart` has been run in Rhino.** Until then every call fails with *"Could not
  connect to Rhino"*. That is the normal first failure of a session.
- **Script Forge is installed** — a separately built Grasshopper plugin, required by
  `forge-push`. There is no reflection fallback for pushing source.

If either is missing, **stop and ask the user** — don't work around it. Install, connect,
the five tools the kit uses, and the six `run_csharp` payload constraints are in
[docs/write-scripts/rhino-mcp-platform.md](docs/write-scripts/rhino-mcp-platform.md);
[docs/use-the-forge/script-forge.md](docs/use-the-forge/script-forge.md) covers the forge.
