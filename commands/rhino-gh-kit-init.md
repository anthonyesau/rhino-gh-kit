---
description: Adopt the rhino-gh-kit plugin in the current project — create icons/, optionally drop a starter component, confirm prerequisites.
---

# rhino-gh-kit-init — adopt rhino-gh-kit in this project

Scaffold the **project-local** pieces of the rhino-gh-kit plugin into the current
working directory (a Grasshopper scripting project). The reusable machinery — skills,
tooling, docs, hooks — already comes from the installed plugin; do **not** copy it. Only
wire in references and create project-owned folders.

**All consumer-facing authoring guidance now ships with the plugin install itself — no
per-project symlink step.** The per-language authoring rules are the `write-csharp-script`
and `write-python-script` skills (both invoked by intent), and the MCP/workflow guard is a
`UserPromptSubmit` hook (fires only on
a Grasshopper-flavored prompt, once per session, injecting `docs/write-scripts/workflow.md`).
Both are auto-discovered the moment the plugin is enabled — there is nothing in this command
that wires either one up. `tooling-python.md` in `.claude/rules/` stays kit-only and is never
symlinked; it never applied to consumers.

**Two things are prerequisites, not options** (step 6 checks both):

- **McNeel's Rhino MCP Platform**, installed per machine and registered under the user's
  own config as `rhino`. The kit ships no `.mcp.json` — a project-scope entry would shadow
  that registration.
- **Script Forge**, the compiled Grasshopper plugin `forge-push` drives. Source reaches a
  script component only through it; there is no reflection fallback. **A project without
  Script Forge cannot author components.**

`${CLAUDE_PLUGIN_ROOT}` below is the plugin's install directory — the **frozen copy** under
`~/.claude/plugins/cache/…`, not the dev clone, and it is version-pinned. It is substituted
to an absolute path when this command loads; a shell can't see it, so `echo
"${CLAUDE_PLUGIN_ROOT}"` first if you need it there. Run everything from the **project
root** (the user's current directory), not the plugin directory.

On a machine with a local dev clone, resolve that clone once — the tooling invocation in
step 4 should prefer it over the cache:

```bash
KIT="$(python3 -c "import json,os;d=json.load(open(os.path.expanduser('~/.claude/plugins/known_marketplaces.json')));m=d.get('rhino-gh-kit',{});s=m.get('source',{});print(m.get('installLocation','') if s.get('source')=='directory' else '')")"
echo "dev clone: ${KIT:-<none — fall back to \$CLAUDE_PLUGIN_ROOT>}"
```

## Steps

1. **Confirm the target.** `pwd`; if it looks like the plugin's own dir or is ambiguous,
   confirm with the user before writing anything.

2. **Create `icons/`** at the project root if missing — it holds one SVG per component
   (kebab-case of the name) plus a `<stem>-dark.svg` dark-theme variant. Conventions
   and rasterizing: `${CLAUDE_PLUGIN_ROOT}/docs/write-scripts/icons.md`.

3. **Offer a starter component (optional).** Ask if the user wants a copy-paste template.
   If yes and the project has no component sources yet, copy the plugin's example:

   ```bash
   cp "${CLAUDE_PLUGIN_ROOT}/examples/list-stats.cs" ./list-stats.cs
   ```

   They rename and rewrite it. Skip if the project already has `*.cs` component sources.

4. **Print the tooling invocation.** The tooling ships in the plugin but scans the
   **project cwd** for component sources, so it is run from the project root:

   ```bash
   python3 "${KIT:-${CLAUDE_PLUGIN_ROOT}}/tooling/gh_meta.py" --all --check
   ```

   If the project keeps unpublished examples in a subfolder, they are checked separately
   with `--root <dir>`.

   Record the working invocation in the project's `CLAUDE.md` (with `$KIT` resolved to a
   literal path), along with the note that the kit's tooling is single-sourced and invoked
   through `${CLAUDE_PLUGIN_ROOT}` — do not create a `tooling/` directory in the consuming
   project.

5. **Say plainly that the C# authoring guidance is now a skill and the MCP/workflow guard
   is a hook, not a rule to configure.** Nothing to verify here — both are live as soon as
   the plugin shows up in `/plugin` for this project. If the user wants the workflow guard
   proven out, they can open a fresh session and send a Grasshopper-flavored prompt; the
   injected context is visible in the transcript as a system message on that turn.

6. **Check the two prerequisites, and say plainly if either is missing.**

   - **Script Forge.** Look for it in Grasshopper's plugin list, by `yak list | grep
     ScriptForge`, or on the canvas by its proxy GUID
     `41822538-1827-4da2-bf84-58074c49b3ad`. Its source ships in this same repo, under
     its own `script-forge/` folder (`script-forge/script-forge.cs`, `script-forge/src/ScriptForge/`),
     but the **Grasshopper plugin install is
     a separate step from the Claude Code plugin install** — building and installing
     the `.gha` is `tooling/publish.sh --repo script-forge install`, run from a clone
     of this repo. It is
     a Yak package, but a **privately distributed** one, installed with `yak install
     --source <folder holding the .yak> ScriptForge` — it is **not** on the public
     server, so plain `yak install ScriptForge` fails. If it is absent, tell the user
     the project cannot author components until it is built and installed, and point
     at `${CLAUDE_PLUGIN_ROOT}/docs/use-the-forge/script-forge.md`. Don't offer a workaround; there
     isn't one.
   - **The Platform.** The session should expose `mcp__rhino__run_csharp` and
     `mcp__rhino__g1_place_component`. If it exposes some other tool set, the config is
     pointing at a different server. If the tools are there but calls fail with *"Could
     not connect to Rhino"*, the user needs to run **`MCPStart`** — see
     `${CLAUDE_PLUGIN_ROOT}/docs/write-scripts/rhino-mcp-platform.md`.

   Record in the project's `CLAUDE.md` that `forge-push` is the only authoring path, and
   that a canvas needs a Script Forge on it (place one by proxy GUID if not).
