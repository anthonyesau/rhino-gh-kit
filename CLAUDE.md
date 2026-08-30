# rhino-gh-kit — plugin overview

Reusable Claude Code **plugin** for authoring inline Rhino 8 Grasshopper C# and
Python 3 Script components. This file is a thin pointer — the actual agent
instructions live as **skills** in [skills/](skills/), a **hook** in
[hooks/](hooks/), and one kit-internal **path-scoped rule** in
[.claude/rules/](.claude/rules/), all auto-discovered every session the moment
the plugin is enabled (in this repo they dogfood the kit). What each one does
and why it's shaped that way is
[docs/maintain-the-kit/kit-architecture.md](docs/maintain-the-kit/kit-architecture.md).
No consumer-facing piece needs a per-project symlink — `/rhino-gh-kit-init` scaffolds
`icons/` and checks prerequisites, and that is all it does.

The kit bundles **no** `.mcp.json`: the one server is McNeel's Rhino MCP
Platform, registered per machine under the user's own config as `rhino`, and a
project-scope entry would shadow it.

**Developing the kit needs no install.** Run `tooling/dev.sh` from this clone
and the plugin is served live off disk; `/reload-plugins` applies an edit
without restarting. Never iterate through `claude plugin install`/`update` —
that path copies into a version-gated cache and is how a change silently fails
to take effect.

See [README.md](README.md) for how to install the plugin, adopt it in another
project (`/rhino-gh-kit-init`), and the dev loop.

## Where things go

This repo ships **two** things — **Script Forge** (a Grasshopper component,
installed as a Yak package) and **the script kit** (this Claude Code plugin).
One rule keeps them apart:

- **`script-forge/` holds what changes `ScriptForge.gha`** — its source, icon,
  `.NET` project, Yak manifest, fixtures, and its own internals docs.
  Nothing about *driving* the forge belongs there.
- **`docs/` holds what a user of either product reads**, in four folders named
  for the job: `use-the-forge/`, `write-scripts/`, `ship-a-plugin/`, and
  `maintain-the-kit/` (the only internal one). Index: [docs/README.md](docs/README.md).

`tooling/` is shared machinery for both, and `examples/` is kit-wide.

## File naming

Every tracked path is POSIX-portable: **`A–Z a–z 0–9 . _ -` only**, no leading
`-` in a segment, no trailing dot or space, no Windows-reserved basename
(`CON`, `NUL`, `COM1`…). No spaces, and no non-ASCII — an em dash in a filename
is two problems, not one.

`python3 tooling/check_filenames.py` enforces this and runs first in
`publish.sh`; it has no exemption list. Case convention and the traps
(case-only renames, NFD/NFC) are in
[docs/ship-a-plugin/file-naming.md](docs/ship-a-plugin/file-naming.md).

## Docs and comments record current state

Prose describes **what is true now**. Git holds the timeline, so a doc or comment
never carries one: no "used to", no "previously", no "before 0.4.0", no dated
account of a conversion, split, rename or fixed bug. Retiring something means
deleting its prose, not annotating it as retired, and a doc that turns out to have
been wrong gets the sentence fixed, not a paragraph refuting itself.

Three cases — and only these — earn a reference to code that no longer exists:

| Keep | Because | Keep only |
|---|---|---|
| a **migration the reader must run** — old artifacts are still in the wild (a saved `.gh`, an installed package) | they cannot act without knowing the old shape | the symptom and the fix |
| an **error message a stranded user will hit**, naming a retired construct | the string *is* the doc | the string — not the comment above it |
| a **constraint the past still imposes** — a pinned guid, a reused library id, a name that cannot change | regenerating it breaks real users | the standing rule, not the archaeology |

The per-sentence test: *could a reader who has never seen the old version act
correctly with the history deleted?* If yes, delete it. A dated **measurement of
external behaviour** (a Rhino or Platform API that moves under us) is evidence, not
history — that stays.

## Script Forge

Everything specific to the compiled component lives under **`script-forge/`**,
kept separate from the kit's shared skills/hooks/tooling. General authoring
guidance is in `skills/` and `hooks/`, and
[docs/use-the-forge/script-forge.md](docs/use-the-forge/script-forge.md)
+ the `forge-push` skill are the agent-facing description of driving one.

Two pieces the component depends on stay at the kit's top level, *not* under
`script-forge/`, because they are shared kit-wide infrastructure rather than
Script Forge's own: `tooling/publish.sh` + `tooling/gh_codegen.py` (the
compiled-plugin pipeline other projects also build against at this exact path,
see [docs/ship-a-plugin/dotnet-build.md](docs/ship-a-plugin/dotnet-build.md)),
and [docs/write-scripts/header-reference.md](docs/write-scripts/header-reference.md)
(the canonical `@component` grammar — Script Forge, `gh_meta.py` and
`gh_codegen.py` all read it, so it is the kit's format, not the forge's).

### Layout (`script-forge/`)

| Path | What |
|---|---|
| `script-forge/script-forge.cs` | The published component — the only `.cs` directly inside `script-forge/`; everything else lives under `examples/` (kit-wide, unmoved). |
| `script-forge/script-forge.svg` (+`-dark`) | Its icon, sitting alongside the source rather than in an `icons/` subfolder — doubles as both the 24px canvas icon and, rasterized to 128px, the Yak package icon. |
| `examples/` | ~30 demo components the forge builds, with icons in `examples/icons/` and the worked-examples canvas saved alongside — kit-wide (unmoved), forged only, never on the compiled ship list. |
| `script-forge/docs/` | The component's **internals** only — `forge-under-test.md` (how to test an edit to `script-forge.cs`) and `known-limitations.md`. Driving the forge (`script-forge.md`, `component-reference.md`) is in `docs/use-the-forge/`; the header grammar, the build and the filename gate are kit-wide under `docs/`. |
| `script-forge/audit-fixtures/` | Deliberately malformed / edge-case `@component` sources — pinned against both parsers by `python3 script-forge/tooling/test_fixtures.py` (see `docs/ship-a-plugin/dotnet-build.md`, "Testing the header parsers"). |
| `script-forge/src/ScriptForge/` | The .NET project — **two** hand-written files: the csproj and `ScriptForgeInfo.cs`. Neither mentions the component. |
| `script-forge/yak/manifest.yml` | Yak package metadata (name `ScriptForge`, independent of everything else). |
| `script-forge/tooling/publish.conf` | This project's knobs for the kit's `tooling/publish.sh` (sln, csproj, `.gha` name, codegen resource prefix, package icon) — read when `publish.sh` is run with `--repo script-forge`, since a project's conf always lives at `<repo>/tooling/publish.conf`. |
| `script-forge/tooling/clean-forge-state.cs` | One-off: purges stale pre-`{path}#t` Script Forge state from a `.gh`'s value table. Run via `run_csharp` against the open document; see the file's own header. |
| `script-forge/tooling/test_fixtures.py` + `script-forge/tooling/fixture-runner/` | The `audit-fixtures/` test suite and the dev-only C# console harness it uses to reach `script-forge.cs`'s own private header parser by reflection — not a shipped artifact (never compiles into `ScriptForge.gha`), but permanent tracked source. Project-specific, not shared kit infrastructure — imports the kit's `gh_meta.py` and shells out to the kit's `publish.sh` from one level up. |

`tooling/check_filenames.py` (kit-wide, unmoved) still gates `script-forge/` —
it takes `--root` and is run with `--root script-forge` as part of that
project's `publish.sh` pass.

No canvas is ever a build input — the build reads `script-forge/script-forge.cs`
off disk. Two are tracked because a reader is told to open them:
`examples/script-forge-examples.gh` (the worked-examples canvas) and
`examples/native-ceiling/native-ceiling.gh`. Scratch canvases are ignored **by
name** in `.gitignore`, one line each — see its own comment for why a
`*.gh` wildcard isn't usable here. A forge rig is built by
`tooling/build-forge-rig.cs`, not saved.

### Environment

- **The forge cannot update itself** — it refuses its own `InstanceGuid`. Push
  `script-forge/script-forge.cs` to a live forge with the `forge-push` skill, or
  drive a *second* forge. When targeting many components at once, always
  exclude the driving forge's GUID.
- Icon paths in a header resolve against the **`.gh` document's** directory on
  canvas, but against `gh_codegen.py`'s `--root` (`script-forge/`, via
  `--repo script-forge`) in a compiled build. Those two roots differ, so a single
  header value cannot satisfy both. The
  compiled build wins: `script-forge.cs`'s header says
  `"icon": "script-forge.svg"`, bare, correct for the build (resolved against
  `script-forge/`). Forge a Forge-under-test from a canvas saved at the repo root
  and that same value resolves against the *canvas's* folder, coming up one
  segment short — a `missing icon` warning, harmless (icon stamping is always
  best-effort) but expected; save the canvas inside `script-forge/` if a session
  needs to verify icon stamping. Example components have the same one-anchor
  problem and solve it the other way: their headers say the bare
  `"icon": "icons/<name>.svg"`, correct for the worked-examples canvas that lives
  in `examples/` beside them. Forge an example from a canvas saved anywhere else
  and its icon warns instead — same harmless, expected miss.
- **A forge rig is built, not stored.** `tooling/build-forge-rig.cs` places and
  wires the five objects on the active canvas; testing a change to Script Forge
  itself is [script-forge/docs/forge-under-test.md](script-forge/docs/forge-under-test.md).
- **Take inputs as trees and loop the branches by hand**, emitting every output on
  the *input* branch path ({i} in → {i} out). A **list-access** script component
  under GH's implicit iteration appends the iteration index instead ({i} in →
  {i;0} out), which silently breaks path pairing with the Source wire.

### Building the compiled `.gha`

Generated from `script-forge/script-forge.cs` by `tooling/gh_codegen.py`; the
full mechanism — shim types, hook methods, upgraders, param mapping — is
documented once in
[docs/ship-a-plugin/dotnet-build.md](docs/ship-a-plugin/dotnet-build.md);
**read that, not this section, for how it works.**

**The one rule:** `script-forge.cs` is the single canonical source. It stays
byte-for-byte Forge-pushable; the generator's only textual change to it is
rewriting `public class Script_Instance : GH_ScriptInstance`. If a
per-component hand-written file ever appears in `script-forge/src/ScriptForge/`,
the rule has been broken — extend the header grammar and the generator instead.

`script-forge/` is its own `--repo` target for the kit's shared pipeline —
every path in `script-forge/tooling/publish.conf` is relative to that folder:

```bash
tooling/publish.sh --repo script-forge              # validate + generate + build   (no network, no install)
tooling/publish.sh --repo script-forge package      # ... + build a .yak into the local repo
tooling/publish.sh --repo script-forge install      # ... + yak install it from that repo
tooling/publish.sh --repo script-forge push         # ... + PUBLIC, permanent upload; prompts first
```

Iterating is unchanged and still the fast path: edit `script-forge/script-forge.cs`
→ push to a live forge → test on canvas. Compiling is the *release* step. There is no
hot reload on macOS, so every compiled-behaviour test costs a Rhino restart —
verify which binary is live by reflecting on something the build changed,
never by the file's timestamp.

### Ribbon placement

Four independent surfaces — ribbon tab, ribbon prominence, assembly name,
package name — are explained in
[script-forge/yak/manifest.yml](script-forge/yak/manifest.yml). Do **not** call
`AddCategoryIcon` for `Params`: Grasshopper owns that tab, and stamping over
it collides with Grasshopper's own entry.

### Two identities are pinned and must not be regenerated

- `"componentGuid": "41822538-1827-4da2-bf84-58074c49b3ad"` in the header →
  `ComponentGuid`. Every `.gh` holding a Script Forge references it, so changing
  it orphans all of them. The header carries the same value as `instanceGuid`,
  which is a different claim — the canvas instance the forge would update — and
  is what lets a live forge be pushed to from disk.
- `2bc1a899-…` in `ScriptForgeInfo.Id` → the Grasshopper library id. An install
  upgrades in place only while this holds; change it and the next one appears
  alongside its predecessor as a duplicate.

Any second build of Script Forge loaded at the same time collides on that
ComponentGuid — rename the loser's `.gha` to `.gha.disabled` to park it
reversibly before smoke-testing.

### The header is load-bearing, not documentation

`gh_codegen.py` derives param registration *and* the `RunScript` call
signature from the `@component` header, and treats any header↔signature
disagreement as fatal. The canvas cannot catch that drift — it rewrites the
signature on every solve, for inputs as well as outputs — so `publish.sh`'s
`gh_meta.py --all --check` gate and the C# compiler are the only things that
ever will. This is why `RunScript`'s outputs are declared
`out DataTree<Guid>` / `<object>` / `<bool>` / `<string>` rather than the
`out object` the canvas rewrites them to.

```bash
python3 tooling/gh_meta.py --all --check --root script-forge   # the published component
python3 tooling/gh_meta.py --all --check --root examples
python3 tooling/gh_codegen.py --list --root script-forge       # ship list, no writes
```

### Publishing

Do not push the compiled plugin or yak to the public package manager
(yak.rhino3d.com). This is a restriction on *distribution*, not on packaging:
`tooling/publish.sh --repo script-forge install` deliberately builds and
installs a real yak package out of a local folder repository, and that is the
standard install path. Only the `push` stage is off-limits.

Handing someone a build is a **GitHub Release**, with the `.yak` attached — a
two-command procedure, deliberately not a `publish.sh` stage. That, the
`kit-v*` / `forge-v*` tag convention, and why the plugin, package and assembly
versions are three independent numbers are all in
[docs/ship-a-plugin/publishing.md](docs/ship-a-plugin/publishing.md).
