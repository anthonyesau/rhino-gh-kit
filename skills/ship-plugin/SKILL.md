---
name: ship-plugin
description: TRIGGER — load before building, installing, versioning or releasing a **compiled** Grasshopper plugin (`.gha` / `.yak`) in a project that uses this kit's pipeline, whenever: cutting a GitHub Release or attaching a `.yak` to one; running `publish.sh` at any stage or reaching for `dotnet build`, `yak build`, `yak install` or `gh release create` by hand; bumping or reconciling a package / assembly / plugin version; a pre-commit hook refuses a commit over a version mismatch or an un-bumped package version; working out which build Rhino actually has loaded; or standing a new compiled-plugin project up. SKIP for authoring a script component's body (`write-csharp-script` / `write-python-script`) and for pushing source onto a live canvas (`forge-push`) — nothing here compiles a script component or forges anything.
allowed-tools: Bash, Read, Edit, Grep, Glob, mcp__rhino__run_csharp
---

# Ship a compiled Grasshopper plugin

Two shared scripts do all of it, and **a project never copies either one**. It
keeps a `tooling/publish.conf` holding the handful of things that differ, and
calls the kit's copy:

| script | does | run it |
|---|---|---|
| `tooling/publish.sh` | validate → generate → build → package → install → push, cumulative | every build |
| `tooling/release.sh` | tag + GitHub Release with the `.yak` attached | releases only |

```bash
"${CLAUDE_PLUGIN_ROOT}/tooling/publish.sh" --repo <path> install
"${CLAUDE_PLUGIN_ROOT}/tooling/release.sh" --repo <path> --dry-run
```

Inside the kit's own clone, drop the prefix and call the clone's copy
(`tooling/publish.sh --repo …`), so you run what you just edited. Everywhere else
use the `${CLAUDE_PLUGIN_ROOT}` form above.

`--repo` is the folder holding `tooling/publish.conf`, and every path in that conf
is relative to it. It is **not** necessarily the git repo root: `release.sh`
derives that itself, so a project consuming the kit from elsewhere tags and
releases *itself*, never the kit.

The pipeline, the shim types, the generator and the conf keys are
`${CLAUDE_PLUGIN_ROOT}/docs/ship-a-plugin/dotnet-build.md`; the versioning and
release rationale is
`${CLAUDE_PLUGIN_ROOT}/docs/ship-a-plugin/publishing.md`. This file is the
procedure only.

## Stop signs

**`publish.sh push` is a public, permanent upload to yak.rhino3d.com.** It is the
one stage that makes anything public and it cannot be undone. Never run it because
a task said "publish" or "ship" — whether a project may take that step is written
in that project's own `CLAUDE.md`, and in this kit's it is forbidden. Everything
below stays local or lands on a GitHub Release.

**`install` and `release` write outside the repo** — into the Rhino packages
folder and onto GitHub. Both are the intended everyday commands, but confirm
before the first one in a session if the user only asked you to build.

## Cut a release

```bash
tooling/release.sh --repo script-forge --dry-run   # every check + the build; no tag, no release
tooling/release.sh --repo script-forge             # tag, push, release
```

**You never type a version.** `release.sh` reads it from the manifest, derives the
tag as `TAG_PREFIX + version`, and checks the built `.yak` filename carries it —
which is the whole point: the tag, the commit and the asset cannot end up naming
different builds. Do not pass a version, do not `git tag` by hand, and do not
reach for `gh release create` — it will invent a missing tag at whatever HEAD
happens to be.

Run `--dry-run` first. It performs every check *and* the full build, so a clean dry
run means the real run has nothing left to fail on. It touches nothing in git and
nothing on GitHub — but it is not inert: it writes `<repo>/build/`, and `package`
drops the `.yak` into `$YAK_LOCAL_REPO`, so that version becomes installable
locally whether or not you go on to release it.

Two conf keys beyond `publish.sh`'s:

```bash
TAG_PREFIX="forge-v"          # tag = this + the manifest version
PRODUCT_NAME="Script Forge"   # release title; the manifest `name:` cannot hold a space
```

### When it refuses

Each guard is protecting something that has actually gone wrong for someone. Fix
the cause; do not work around it.

| refusal | do this |
|---|---|
| working tree is dirty | commit or stash — the asset must match a commit |
| HEAD is not on origin | push first; a release must point at a fetchable commit |
| tag exists, points elsewhere | release from that commit, or bump the version. **Never move a published tag.** |
| a release already exists | bump and release that. Re-uploading silently changes what people already have. |
| asset filename lacks the version | stale output — remove `<repo>/build/yak/` and retry |
| version mismatch: manifest vs csproj | they are one number in two files; make them agree |

## Versions

Three numbers, deliberately independent. Know which one a change moves.

| number | lives in | moves when |
|---|---|---|
| plugin | `.claude-plugin/plugin.json` | any commit changing what a session reads — the pre-commit hook **bumps it for you** |
| package | `<repo>/yak/manifest.yml` `version:` | the compiled component's own behaviour or build changes — **you bump it** |
| assembly | `<repo>/<csproj>` `<Version>` | never independently; it must equal the package version |

**Bump the manifest and the csproj in the same edit, always.** A commit that splits
them is one nobody can build, and both the hook and `publish.sh` refuse it.

**If the pre-commit hook refuses your commit** over an un-bumped package version,
that is a judgement call it is handing to a person: the commit touched the
component's source, so either bump both files, or — for an edit that genuinely
cannot reach a user, like a reworded comment — take the opt-out deliberately:

```bash
FORGE_NO_BUMP=1 git commit …
```

Do not reach for the opt-out to get past the hook. Ask the user which it is.

### Tags

`kit-v*` marks the Claude Code plugin, `forge-v*` the compiled component; an
unprefixed `vX.Y.Z` cannot say which. Only `forge-v*` is cut by `release.sh` and
only it carries an asset. Plugin versions move on nearly every commit, so **most
are untagged on purpose** — tag one by hand only when the user wants a marker.

## After installing

**Only a Rhino restart loads a new build.** The file on disk changing does not
swap what a running instance already mapped, and there is no hot reload on macOS.

**Verify by reflecting on something the new build changed, never by the `.gha`'s
timestamp** — a stale binary reporting a newer file on disk is the classic
confusing result. `mcp__rhino__run_csharp` against the live session, checking for a
member or behaviour that only exists in the new build, is the only honest check.

`yak list` reports the installed package version, which confirms what the
*installer* did — not what Rhino has loaded.

## Stand up a new project

`docs/ship-a-plugin/dotnet-build.md` § "Starting a new project: boilerplate
checklist" is the list. The shape:

```
<repo>/*.cs          canonical sources, Forge-pushable, compiled by the generator
icons/*.svg          + <stem>-dark.svg variants
src/<Project>/       exactly two hand-written files: the csproj and AssemblyInfo.cs
yak/manifest.yml
tooling/publish.conf the project-specific knobs
```

Two rules that are easy to break and expensive to unpick:

- **No per-component hand-written file in `src/`.** If one appears, the one-file
  rule is broken — extend the header grammar and the generator instead.
- **Never regenerate an identity that has shipped.** A published `componentGuid`
  is referenced by every `.gh` holding that component, and the `GH_AssemblyInfo.Id`
  is what makes an installed package upgrade in place instead of appearing twice.

## Related

- `${CLAUDE_PLUGIN_ROOT}/docs/ship-a-plugin/publishing.md` — versioning, tags, why the release step is its own script
- `${CLAUDE_PLUGIN_ROOT}/docs/ship-a-plugin/dotnet-build.md` — the pipeline in full, and the new-project checklist
- `${CLAUDE_PLUGIN_ROOT}/docs/ship-a-plugin/file-naming.md` — the gate `publish.sh` runs first
