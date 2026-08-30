# Docs — pick by what you're doing

Each folder is named for a job. Three of the four are for people *using* what
this repo ships; only `maintain-the-kit/` is about changing the repo itself.

| Folder | You are… | Contents |
|---|---|---|
| [`use-the-forge/`](use-the-forge/) | driving **Script Forge** on a canvas | [`script-forge.md`](use-the-forge/script-forge.md) — the authoring path, agent-facing · [`component-reference.md`](use-the-forge/component-reference.md) — its three inputs, two outputs, and the tree/branch model |
| [`write-scripts/`](write-scripts/) | writing a C# or Python 3 **script component** | [`workflow.md`](write-scripts/workflow.md) · [`header-reference.md`](write-scripts/header-reference.md) — the `@component` grammar · [`csharp-type-hints.md`](write-scripts/csharp-type-hints.md) · [`python3-marshalling.md`](write-scripts/python3-marshalling.md) · [`identity-properties.md`](write-scripts/identity-properties.md) · [`naming-guide.md`](write-scripts/naming-guide.md) · [`icons.md`](write-scripts/icons.md) · [`hand-rolled-json.md`](write-scripts/hand-rolled-json.md) · [`rhino-mcp-platform.md`](write-scripts/rhino-mcp-platform.md) |
| [`ship-a-plugin/`](ship-a-plugin/) | compiling **your own** components into a `.gha` | [`dotnet-build.md`](ship-a-plugin/dotnet-build.md) — the whole mechanism · [`publishing.md`](ship-a-plugin/publishing.md) — Yak packaging and install · [`file-naming.md`](ship-a-plugin/file-naming.md) — the gate `publish.sh` runs first |
| [`maintain-the-kit/`](maintain-the-kit/) | changing **this repo** | [`kit-releases.md`](maintain-the-kit/kit-releases.md) — the install cache, version gating, `--scope` |

Script Forge's own internals — how to test a change to it, and its known
limitations — are **not** here. They live with the source that implements them, in
[`../script-forge/docs/`](../script-forge/docs/).

## Which folder does a new doc go in?

Two questions settle it:

1. **Would a stranger who installed either artifact ever read it?** If no —
   only someone editing this repo would — it's `maintain-the-kit/`.
2. **Does it describe using Script Forge, writing a component, or shipping
   one?** That's the folder.

And one rule above the `docs/` tree: **`script-forge/` holds what changes
`ScriptForge.gha`.** A doc about how to *use* the forge belongs in
`use-the-forge/`, never in `script-forge/`, however much it is "about" the
forge.
