---
paths:
  - "tooling/*.py"
  - "tooling/**/*.py"
---

<!--
  Loads only when editing the kit's own tooling. This rule is NOT symlinked into
  consuming projects (they carry no tooling/*.py — the tooling is single-sourced in
  the plugin), so it only ever fires here in the kit repo. Paths are repo-relative.
-->

# Tooling — the metadata header and the compile pipeline

- **Tooling lives in `tooling/`** (none of it ships in the built plugin's runtime output): `gh_meta.py` (header parser), `gh_codegen.py` (script sources → a buildable `.gha`), `publish.sh` (the release pipeline), `check_filenames.py` (the filename gate), and `kit_scopes.py` (kit maintenance). See [tooling/README.md](../../tooling/README.md).
- **`gh_meta.py` is the single parser this kit's own tooling calls** for the `@component` header grammar — the codegen, the publish pipeline, and the skills all call it so the grammar lives in exactly one place here; don't re-implement it elsewhere. The grammar itself is specified once, canonically, in [`docs/write-scripts/header-reference.md`](../../docs/write-scripts/header-reference.md) — `gh_meta.py` is a Python port of it, and that doc's appendix carries the tooling-facing bits (`gh-meta: ignore`, `--check` semantics, the `SetDesc` relationship).
- **`check_filenames.py` is deliberately *not* part of the ported parser.** Repo-hygiene rules never belong in `gh_meta.py`: Script Forge has no opinion about filenames, so folding one into `check_meta` would break the parity invariant above, and the checker shells out to `git ls-files`, which the parser has no business doing. It *does* reuse `gh_meta.all_sources()` and `gh_meta.root_arg()` — keep it that way, so the filename check covers exactly the files `--all --check` walks. `all_sources()` is public for that reason; don't re-privatize it. The convention it enforces is in [docs/ship-a-plugin/file-naming.md](../../docs/ship-a-plugin/file-naming.md).
