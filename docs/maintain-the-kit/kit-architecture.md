# Kit architecture — why each piece is shaped the way it is

Maintainer rationale for the skills, the hook, and the one kit-internal rule
under [skills/](../../skills/), [hooks/](../../hooks/), and
[.claude/rules/](../../.claude/rules/). Claude doesn't need this to *use* the
kit — every piece here is auto-discovered the moment the plugin is enabled.
It's for deciding whether a new piece of agent guidance should be a skill, a
rule, or a hook, and why the existing ones aren't interchangeable.

- `write-csharp-script` (skill, [skills/write-csharp-script/](../../skills/write-csharp-script/))
  — the C# Script format rules (keep the class wrapper, out-param quirk,
  RunScript-rewrite, the durable identity slots, …). A skill rather than a rule so it
  needs no per-project symlink; invocation is triggered by intent (creating/editing a
  `.cs` script body) rather than by an existing file to path-scope against, which is
  what covers the "writing a brand-new component" case a path-scoped rule can't.
- `write-python-script` (skill, [skills/write-python-script/](../../skills/write-python-script/))
  — the Python 3 Script counterpart, same pattern and same trigger-by-intent rule
  (script mode vs. SDK mode, outputs as namespace assignments, the shared-label
  allowance Python has and C# doesn't, unwired inputs, `MarshOutputs` vs. output
  hints, …).
- `gh-workflow-guard.sh` (hook, [hooks/hooks.json](../../hooks/hooks.json)) — a
  `UserPromptSubmit` hook that, on a Grasshopper-flavored prompt, injects
  [docs/write-scripts/workflow.md](../write-scripts/workflow.md) (the MCP-not-loaded guard,
  the metadata-header workflow overview, references) as `additionalContext`, once per
  session. Conditional on the prompt rather than paid on every message, which
  matters because this repo (and plenty of others) sees non-Grasshopper work too.
  `additionalContext` reaches the model on a matching prompt and stays silent on an
  unrelated one; the dedup is per session and holds across a continued conversation.
- `tooling-python.md` (rule) — path-scoped to `tooling/*.py`: the manifest / normalization-parity
  invariants. Kit-only (never symlinked into consumers); its job is *editing* existing
  tooling, which is read-first, so path-scoping fits.

The five skills (`forge-push`, `set-param-value`, `ship-plugin`,
`write-csharp-script`, `write-python-script`) plus the metadata-header tooling
(`tooling/`) ship with the plugin.
