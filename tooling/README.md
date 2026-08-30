# Tooling

Dev infrastructure for the metadata-header workflow — parsing the canonical
`.cs` sources and turning them into a shipped plugin. Most of it is build-time
only. The exception is `set-param-value.cs`, which the skills invoke at run
time.

## Files

| file                | what it does                                                                 |
|---------------------|------------------------------------------------------------------------------|
| `gh_meta.py`        | This kit's parser for the `@component` metadata header — a Python port of Script Forge's own parser, kept in sync by hand. The grammar itself is specified once, in [`docs/write-scripts/header-reference.md`](../docs/write-scripts/header-reference.md), whose appendix covers this tooling's own bits (`gh-meta: ignore`, `--check` semantics). |
| `publish.sh`        | The release pipeline for a **compiled** plugin, shared by every project that builds a `.gha`: validate → generate → build → package → install → push, cumulative. Projects keep a 3-line wrapper and a `tooling/publish.conf`; see below. |
| `set-param-value.cs` | Payload (run via `mcp__rhino__run_csharp`, **not** a GH script component) that writes a value into any param or input object — panels, value lists, toggles, component inputs, Rhino geometry references. The Platform can set a slider and nothing else; this covers the rest, and is how `forge-push` feeds Script Forge its inputs. |
| `build-forge-rig.cs` | Payload (run via `mcp__rhino__run_csharp`, **not** a GH script component) that builds a forge rig on the active canvas: Source panel, Target value list, the compiled Script Forge, a Run button, and a group, wired and laid out. Scratch canvases are not tracked, so this file *is* the rig — run it rather than hunting for a saved `.gh`. Driving one is [`forge-push`](../skills/forge-push/SKILL.md); testing a change to Script Forge itself is [`../script-forge/docs/forge-under-test.md`](../script-forge/docs/forge-under-test.md). |
| `release.sh`        | Cuts a GitHub Release with the `.yak` attached, for any project `publish.sh` builds. Reads the version from the manifest and derives the tag from it, so the tag, the commit and the asset cannot name different builds; refuses a dirty tree, an unpushed HEAD, a tag already pointing elsewhere, an existing release, or a built asset whose filename does not carry the version. `--dry-run` runs every check and the build, and writes nothing. Every git operation targets the repository containing `--repo`, not the kit, so a project that consumes the kit from elsewhere tags itself. Deliberately separate from `publish.sh`, whose stages are a straight line — a release step inside it would be inherited by `install` or by `push`. |
| `check_filenames.py` | The filename gate `publish.sh` runs before the header check. Two passes: the POSIX-portable character set over every `git ls-files` path, then `kebab(header "name") == stem` over `gh_meta.all_sources()` — the same scope `--all --check` walks, reused so the two cannot drift. Deliberately **not** folded into `gh_meta.check_meta`: that module is a hand-synced port of Script Forge's parser, and the forge has no opinion about filenames. See [../docs/ship-a-plugin/file-naming.md](../docs/ship-a-plugin/file-naming.md). |
| `dev.sh`            | Launches Claude Code with this clone loaded live as the plugin (`--plugin-dir`), so a skill or hook edit is in effect with no install, version bump, cache or restart. The development loop; `claude plugin install`/`update` is for consumers. |
| `install-hooks.sh`  | Installs `tooling/hooks/pre-commit` into `.git/hooks/pre-commit` for this clone. Git hooks aren't tracked, so a fresh clone gets no version gating at all until this runs once. |
| `kit_scopes.py`     | Kit maintenance (not Grasshopper): derives each install entry's `--scope` from `claude plugin list --json`'s `projectPath`, so a re-serve reaches every consuming project instead of a guessed one. Reports stale entries. |

Everything in this table is shared kit infrastructure. Most of it is reused by any
project that builds a `.gha` (this kit's own `script-forge/` included, plus other
projects built on this pipeline); `set-param-value.cs` and
`build-forge-rig.cs` are reused instead by any project that *drives* the forge,
which is every project the kit is installed in. Script Forge's own
project-specific tooling — not generic, and not usable by another
project — lives one level down, in
[`../script-forge/tooling/`](../script-forge/tooling/): `publish.conf` (this
project's build knobs), `test_fixtures.py` + `fixture-runner/` (pins `gh_meta.py`
against `script-forge.cs`'s own header parser over `audit-fixtures/`, see
[../docs/ship-a-plugin/dotnet-build.md](../docs/ship-a-plugin/dotnet-build.md), "Testing the header
parsers"), and `clean-forge-state.cs` (a one-off canvas cleanup script).

See [../docs/ship-a-plugin/publishing.md](../docs/ship-a-plugin/publishing.md) for the full release procedure.

## Release *this* kit

**None of this is needed to develop the kit** — `tooling/dev.sh` (= `claude --plugin-dir .`) serves it live, and `/reload-plugins` applies an edit without a restart.
What follows is how a change reaches *consumers*.

`hooks/pre-commit` auto-bumps `.claude-plugin/plugin.json`'s patch version
whenever a commit touches `skills/`, `commands/`, `docs/`, `tooling/`, `hooks/`,
`examples/`, `script-forge/docs/` or `script-forge/tooling/` — the paths
README.md's "The model" section says only reach a session through the install
cache, so an unbumped version leaves `claude plugin update` a no-op and every
consumer frozen on stale content. It no-ops if the version was already bumped
by hand in the same commit (a deliberate minor/major). Git hooks aren't
tracked, so install it once per clone:

```bash
tooling/install-hooks.sh
```

Tagging (`git tag vX.Y.Z`) stays manual — it marks a release milestone, not
every patch bump.

The bump only matters because the served copy is refreshed **per install
entry**, and `claude plugin update` needs a `--scope` that plain
`claude plugin list` won't show you. `kit_scopes.py` derives it:

```bash
python3 tooling/kit_scopes.py --exec   # re-serve every live entry, then verify
```

See [One install, or one per project?](../docs/maintain-the-kit/kit-releases.md#one-install-or-one-per-project)
— a single `user`-scope install removes the per-project bookkeeping entirely.

## Release a compiled plugin

```bash
tooling/publish.sh --repo script-forge              # validate + generate + build
tooling/publish.sh --repo script-forge package      # ... + build a .yak into the private folder repo
tooling/publish.sh --repo script-forge install      # ... + yak install it from that repo
tooling/publish.sh --repo script-forge push         # ... + PUBLIC, permanent upload; prompts first
```

`--repo` names the project directory and defaults to `.`, so a consuming project
whose conf sits at `./tooling/publish.conf` drops the flag. This kit's own
compiled plugin lives one level down, in `script-forge/`, so its recipes carry it.

The only per-project state is `<repo>/tooling/publish.conf`:

```bash
SLN="src/MyPlugin.sln"
CSPROJ="src/MyPlugin/MyPlugin.csproj"
GHA_NAME="MyPlugin.gha"
CODEGEN_ARGS=(--resource-prefix MyPlugin.Icons)   # optional
PACKAGE_ICON_SVG="icons/my-plugin.svg"            # optional; needs `icon: icon.png` in the manifest
```

**Installing means a yak package out of a private folder repository** — the default
is `~/.rhino-gh-kit/yak-local-repo` (no space in the path — `yak install --source`
fails on one), overridable with `YAK_LOCAL_REPO`, and one
folder serves every project. A yak "source" can be any directory, so this gives
versioned, upgradeable installs and a `yak list` version check without publishing
anything. `push` is the only stage that reaches yak.rhino3d.com, and whether a
project is allowed to take it belongs in that project's `CLAUDE.md`. Full key list
and rationale: the header of [publish.sh](publish.sh); the surrounding build
mechanism: [../docs/ship-a-plugin/dotnet-build.md](../docs/ship-a-plugin/dotnet-build.md).

## Validate the headers

```bash
python3 tooling/gh_meta.py --all --check --root script-forge   # OK/FAIL per file, exit 1 on any error
python3 tooling/gh_meta.py --all --check --root examples
python3 tooling/gh_meta.py "My Component.cs"                   # dump one file's parsed metadata as JSON
```

`--all` scans one directory's root-level `*.cs` and `*.py`, not a tree — `--root`
picks which. It defaults to the current working directory, which holds no
components in this kit, so both of the kit's own component sets are named above.
A consuming project that keeps its components at its root can drop the flag.
`--help` prints usage.
