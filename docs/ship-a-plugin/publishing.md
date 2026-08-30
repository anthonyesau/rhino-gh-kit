# Publishing the plugin

A kit of inline GH C# Script components ships as a Grasshopper plugin (Yak /
`.gha`) by **compiling with `dotnet`**. The component `.cs` files at the project
root, with their `@component` headers, are the canonical source; everything —
identity, params, tooltips, icons, the ribbon tab — flows *from* them. The
header grammar itself is
[`docs/write-scripts/header-reference.md`](../write-scripts/header-reference.md),
whose appendix covers running this kit's `gh_meta.py` over it.

There is no palette `.gh`, no `.rhproj`, and no GUI build step. Rhino's own
ScriptEditor **File ▸ Publish** route is not part of this pipeline: it hard-couples
the ribbon tab, the Yak package name and the assembly name to one string, so two
suites that want the same native tab collide. [dotnet-build.md · Why not the
ScriptEditor / rhproj publish
path](dotnet-build.md#why-not-the-scripteditor--rhproj-publish-path) has the
full argument.

An agent working in a project that has the kit installed gets the procedure from
the **`ship-plugin`** skill instead of this file; this is the reference behind it.

## The pipeline

`tooling/publish.sh` runs it, cumulatively — validate → generate → build →
package → install → push:

```bash
tooling/publish.sh --repo script-forge              # gh_meta.py --all --check + gh_codegen.py + dotnet build
tooling/publish.sh --repo script-forge package      # ... + yak build into the private folder repo
tooling/publish.sh --repo script-forge install      # ... + yak install from that repo
tooling/publish.sh --repo script-forge push         # ... + PUBLIC, permanent upload; prompts first
```

`--repo` names the project directory and defaults to `.`; this kit's compiled
plugin sits in `script-forge/`, so its own recipes carry the flag.

Underneath, that is:

```bash
python3 "$KIT/tooling/gh_meta.py" --all --check   # headers valid, no header/signature drift
python3 "$KIT/tooling/gh_codegen.py"              # .cs + headers -> build/gen/*.g.cs + icons
dotnet build src/<Name>.sln -c Release            # -> <Name>.gha
```

A project keeps only a three-line wrapper and a `tooling/publish.conf`; the keys
are documented in [tooling/README.md](../../tooling/README.md) and in the header of
`tooling/publish.sh`.

Two things to know before you trust a result:

- **Installing means a yak package out of a private folder repository**, not a
  hand-copied `.gha`. `yak install --source <dir>` accepts any directory —
  verified against `yak` 8.x, both `search` and `install` — which buys
  versioned, upgradeable installs and a `yak list` version check without
  publishing anything. `install` copies the built package into
  `$YAK_LOCAL_REPO` (default `~/.rhino-gh-kit/yak-local-repo` — no space in
  the path, since `yak install --source` fails on one — outside the repo so
  `rm -rf build/` can't take it) and installs from there. `push` is the only
  stage that reaches yak.rhino3d.com, and whether a project may take it
  belongs in that project's `CLAUDE.md`.
- **Register the local repo as a package source in Rhino, or the Package
  Manager entry shows only a name and an installed version.** Author, Url,
  Description, Date published and the Version dropdown all come from the
  *source search result*, never from the installed `manifest.yml` on disk —
  an installed-but-unsourced package renders as a row of blanks even when the
  manifest is complete, and the empty Version dropdown is the tell. Package
  Manager ▸ settings ▸ package sources; it persists into a running Rhino's
  settings file, which Rhino rewrites on exit, so the pipeline can't set it
  for you. Date published stays blank regardless — a folder repository has no
  server-side publish timestamp.
- **`install` parks a loose `Libraries/<Name>.gha` as `.gha.disabled`** — a
  hand-copied `.gha` left alongside the package loads the component twice and
  the two collide on the component's guid — and **deletes the previous
  install of the same version before reinstalling**, which is safe with Rhino
  running (`cp` over a mapped `.gha` is not).
- **Only a Rhino restart loads a new build.** The file on disk changing does not
  swap what a running instance already mapped, and a stale binary reporting a
  *newer* file on disk is the classic confusing result. Verify by reflecting on
  something the new build changed, not by looking at the file.
- **The `.yak` carries `icon.png`**, rasterized from the project's SVG icon by
  the package stage — that's the Package Manager entry's icon, unrelated to
  any canvas icon the build embeds as a resource. The manifest's `icon:` key
  and the conf's `PACKAGE_ICON_SVG` must agree; the pipeline fails the build
  if one is set without the other.

## Yak

```yaml
# yak/manifest.yml — the package name is free of everything else
name: my-suite
version: 0.1.0
authors: [ ... ]
description: ...
```

`yak spec` will infer a name from `GH_AssemblyInfo.Name`; write the manifest by
hand if you want a different published name. `yak build` packages the *current
directory*, so stage a clean folder holding exactly `manifest.yml` and the
`.gha` — `publish.sh` does this for you. Keep the manifest `version` and the
csproj `<Version>` in step: one is what the Yak server indexes, the other is
what Grasshopper's plugin list shows. The package name is also the display name
and **cannot contain a space** — `yak install` cannot be made to accept one.

Yak carries `GH_AssemblyInfo.Id` through as a `guid:` keyword, which is how an
installed package upgrades in place rather than appearing twice. **Reuse the
library id** when replacing an earlier package for the same suite.

## Versioning: the numbers are independent on purpose

A project built with this pipeline carries **two** version numbers, and one that
also ships as a Claude Code plugin carries **three**. They are not meant to agree.

| number | lives in | moves when | enforced by |
|---|---|---|---|
| plugin | `.claude-plugin/plugin.json` `version` | any commit changing what a session reads — `skills/`, `commands/`, `docs/`, `tooling/`, `hooks/`, `examples/`, `script-forge/docs/`, `script-forge/tooling/` | `tooling/hooks/pre-commit` auto-bumps the patch |
| package | `<repo>/yak/manifest.yml` `version:` | the compiled component's own behaviour or build changes | — |
| assembly | `<repo>/<csproj>` `<Version>` | never independently — it must equal the package version | `publish.sh` refuses to build on a mismatch |

The last two are one number in two files: the manifest is what a yak source
indexes, the csproj is what Grasshopper's plugin list reports (via
`AssemblyInfo.AssemblyVersion` reading the informational version, since a numeric
assembly version cannot hold `-beta`). A drift between them is invisible until
someone tries to work out which build they are running, which is why the gate
exists.

**The first is a different kind of number, and it must be free to drift from the
other two.** `claude plugin update` is version-gated, so the plugin version bumps
on nearly every commit whether or not the compiled component was touched (see
[kit-releases.md](../maintain-the-kit/kit-releases.md) for that loop). Moving the
package version in lockstep would re-version the component for doc and skill edits
that never reach its source — noise, not signal.

**Pinning them together is the failure mode, not letting them drift.** Two numbers
tied to different rates of change cannot both stay meaningful: the slower one gets
dragged along, or it freezes while real fixes ship under a version that no longer
identifies any one build. A package version earns its meaning by moving only when
the package does.

### The gate that keeps it honest

`tooling/hooks/pre-commit` enforces both halves, and the two halves work
differently on purpose:

| | plugin version | package version |
|---|---|---|
| trigger | a cache-served path changed | `script-forge.cs`, `src/`, or the icon changed |
| action | **bumps the patch for you** | **refuses the commit** until you bump it |

The plugin version is mechanical — every cache-served change must move it, so a
hook can do that unattended. The package version is a judgement: it should move
when the component changes, and only a person can say whether an edit is a real
change or a reworded comment. Auto-bumping it would make the number noisy in the
other direction, which is the failure this section exists to prevent.

For an edit that genuinely does not reach a user:

```bash
FORGE_NO_BUMP=1 git commit …
```

The hook also refuses any commit where `manifest.yml` and the csproj disagree —
`publish.sh` checks that at build time, but a commit that splits them is one
nobody can build — and warns when the installed copy has drifted from the tracked
one. Hooks are not tracked by git, so **install it per clone**:

```bash
cp tooling/hooks/pre-commit .git/hooks/pre-commit && chmod +x .git/hooks/pre-commit
```

### Tags

An unprefixed `vX.Y.Z` cannot say which of the three it means. Prefix by product:

| tag | marks | cut by | carries a release asset? |
|---|---|---|---|
| `rhino-gh-kit--v*` | the Claude Code plugin | `claude plugin tag` | **no** — the kit has no binary; it installs from the repo |
| `forge-v*` | the compiled component | `release.sh` | **yes** — the `.yak` |

Neither is automatic, and they are used differently:

- **`forge-v*` is cut by `release.sh`**, and always anchors a release asset. Every
  one names a `.yak` somebody can install.
- **The plugin tag is cut by `claude plugin tag`, only when you want a marker.**
  Claude Code's own command derives `{name}--v{version}` from `plugin.json`, and
  checks that the manifest and any enclosing marketplace entry agree before
  tagging — the same "never type a version" property `release.sh` has, supplied
  by the toolchain rather than by us. It refuses a dirty tree and an existing tag;
  `--dry-run` shows the tag, `--push` pushes it.

  ```bash
  claude plugin tag --dry-run .
  claude plugin tag --push .
  ```

  The plugin version moves on nearly every commit, so tagging each would be about
  one tag per commit and the tag would stop meaning anything. Most plugin versions
  are therefore untagged, and that is the intended state, not an oversight.

(`kit-v0.8.23-beta` and `kit-v0.8.24-beta` predate the command and are left alone.)

(`v0.5.0` and `v0.5.1` predate the convention and are left alone.)

## Cutting a release

Only the compiled component produces an artifact, and the artifact is the
**`.yak`** — not the raw `.gha`. A `.yak` is a zip that a recipient can drop into
any folder and install from, which is the same private-folder-repo route
`publish.sh install` uses locally:

```bash
yak install --source <that folder> ScriptForge 0.4.0-beta
```

Handing them a bare `.gha` instead means a hand-copy into Grasshopper's
`Libraries/`, which has no version story and collides with a packaged install.

Two commands. There is deliberately **no `release` stage** in `publish.sh`: its
stages are a straight line where each runs everything above it, so a release
block would be inherited by whichever stages sit below it — making either
`install` (the everyday command) or `push` (the public upload) cut a GitHub
release as a side effect. Releases are rare enough to stay explicit.

```bash
tooling/release.sh --repo script-forge --dry-run   # every check + the build; no tag, no release
tooling/release.sh --repo script-forge             # tag, push, release
```

**You never type a version.** `release.sh` reads it from the manifest, derives the
tag as `TAG_PREFIX + version` (a conf key — `forge-v` here), and checks the built
`.yak` carries it. That is the point: the tag, the commit and the asset cannot end
up naming different builds. It refuses to proceed on any of:

| refusal | why it matters |
|---|---|
| dirty working tree | the asset would be built from something not in git, so nobody could reproduce it |
| HEAD not on origin | a release pointing at a commit nobody can fetch |
| tag exists, points elsewhere | you are releasing from the wrong commit — never move a published tag |
| a release already exists for the tag | re-uploading silently changes what people already have |
| asset filename lacks the version | stale `build/yak/` output; the build did not see this version |

Notes come from `--notes-file <f>`, or are generated from the commits if you pass
nothing. The release title is `PRODUCT_NAME` from the conf plus the version —
a display name, since the manifest `name:` is the installable spelling and cannot
contain a space.

Like `publish.sh`, this is shared: a project keeps `TAG_PREFIX` and
`PRODUCT_NAME` in its own `tooling/publish.conf` and calls the kit's copy. Every
git operation targets the repository containing `--repo`, never the kit, so a
project consuming the kit from elsewhere tags and releases *itself*.

Two things worth knowing anyway. The `.yak` filename is generated, not chosen —
`<name>-<version>-rh<abi>-any.yak`, lowercased, with the ABI (`rh8_34`) coming from
the SDK it built against. And `package` also drops a copy into `$YAK_LOCAL_REPO`,
so the version you just built is immediately installable locally — the same
artifact, not a second one.
Release assets live outside the git object database, so shipped binaries stay
out of the repo entirely — no tracked `Releases/` folder, and no exemption
needed in the filename gate; see [file-naming.md](file-naming.md).

**Cutting a release does not decide whether to publish.** The `.yak` attached to
it is downloadable and installable on its own — publishing to the public package
manager (`publish.sh push`, reaching yak.rhino3d.com) is a separate, irreversible
act, and whether a project may take it belongs in its own `CLAUDE.md`.

## Where the detail lives

[dotnet-build.md](dotnet-build.md) is the full reference: the three shim types,
the generator, `componentGuid` and the ship list, upgraders, marker discovery
across the script/compiled boundary, and a boilerplate checklist for standing a
new project up.
