# Script Forge — the authoring path

**Script Forge** is a compiled Grasshopper component that *creates or updates other
script components* from source text. Its source lives in this same repo, under
[`script-forge/`](../../script-forge/) (`script-forge.cs`, `src/`, `yak/`) — that
folder is for *building* it; this one is for *driving* it. It is what makes the
authoring workflow below possible, but it is a **separate install**
from this Claude Code plugin: a Grasshopper plugin (`.gha`, via Yak), built with
`tooling/publish.sh --repo script-forge install` from the repo root.
See [CLAUDE.md](../../CLAUDE.md) for that build's project-specific facts.

It is not one option among several. Source reaches a script component **only** through
the forge, driven by the `forge-push` skill — there is no reflection fallback in this kit
any more. A project without Script Forge installed cannot author components until it is.

## What one pass does

The full behavior — create-or-update by `Target`, param-sync and type hints from the
`@component` header, tree fan-out, idempotency — is documented in
[`component-reference.md`](component-reference.md) (**Component Reference**). Read that for
the gist; what follows here is only what driving it from an agent needs.

## Installing it

**Download the `.yak` from this repo's GitHub Releases page**, then install it
from that folder:

```bash
yak install --source ~/Downloads ScriptForge
```

Building it yourself — `tooling/publish.sh --repo script-forge install` from a
clone of this repo — is the contributor path, not the only one; see
[README.md](../../README.md#install-once-per-machine). Either way, restart Rhino
afterward: there is no hot reload on macOS.

Two facts worth carrying into an agent session specifically, because getting
either wrong produces a confusing failure rather than an obvious one:

- **It is not on the public Yak server.** The package is named `ScriptForge`;
  `yak search ScriptForge` returns nothing there, and `yak install ScriptForge`
  with no `--source` will fail — it installs only from a folder you name
  (`yak install --source <folder> ScriptForge`). Don't tell a consumer to run it
  any other way. **GitHub Releases are the distribution channel**; publishing to
  yak.rhino3d.com is a separate, irreversible decision this project has not
  taken.
- **Confirm which build is live by reading the Grasshopper plugin list**, not the
  `.gha`'s file date — macOS has no hot reload, so a rebuild needs a Rhino restart
  before it's the one actually running. `yak list` reports the installed version
  the same way.

## Driving it from an agent

Feed `Source` the `.cs` / `.py` text (the native *Read File* component, a file path, or
a panel), set `Target`, pulse `Run`. `Log` carries the per-branch report — including the
GUID of anything newly created (`creating new C# component <guid>`), which is how you get
a guid to paste into a header `instanceGuid`. The procedure, with the payloads, is the
`forge-push` skill; this is the reference behind it.

`Target` accepts:

| Target | Effect |
|---|---|
| *(unwired)* | walk the identity ladder: header `instanceGuid`, else a component matching the header's `name`, else create new |
| GUID string / component goo | update that component |
| keyword `name` or `nickname` | update **every** on-canvas stock script component of the source's language whose name/nickname matches the source header's; skip the branch when none match |
| `name+create` / `nickname+create` | same, but forge one new component when nothing matches |
| several targets in one branch | that branch's source is forged into each |
| wired but **empty** branch | that source is skipped — safe for name-matching rigs where some scripts have no instance |

Four behaviours that cost a wrong result before they were understood (all measured
2026-08-13):

- **`Target` must be *wired*, not internalized.** The forge decides create-vs-update on
  `SourceCount > 0`, so persistent data written onto the `Target` param is invisible and
  the branch falls through to create-new, forging a stray. Wire a `GH_Panel` and set its
  text. `Source` and `Run` take persistent data fine — though a `Run` internalised as
  `true` can never *transition*, so it arms the file watchers and never pushes.
- **Expire the *param*, not the component.** After writing PersistentData into a forge
  input, `component.ExpireSolution(false)` leaves the param's already-collected
  VolatileData in place and the forge re-reads the *old* `Source`. Call
  `IGH_Param.ExpireSolution` on the input itself.
- **Set `Run` last, and pulse it — false, then true.** Since 0.4.0 the forge pushes on
  the *rising edge*, so a `Run` already sitting true forges nothing no matter what else
  changes. Write `false`, expire, then `true`, expire. Putting it back to false afterwards
  disarms the file watchers.
- **Read the `Log` with `Run` false.** On the pushing solve the Log ends at
  `push scheduled — …`; the param sync happens one solution later. With `Run` false (or
  merely held true) the forge replays the stored per-slot reports as `— last run —` plus
  the full text, including `renamed input X -> Y (n wire(s) kept)` and `lost wire: …`.
  Stamping and the target's own compile result are never in the Log at all — a failure
  there raises the forge's own error bubble instead.

Feeding a panel a list of paths or GUIDs? A panel with `Properties → Multiline` **off**
splits its text into one item per line, which is the shape the forge wants; multiline on
emits a single blob.

**Don't echo the `Log` straight to stdout from a `run_csharp` payload.** It can contain
compiler diagnostics, and the Platform moves the entire stdout into its `error` field —
returning stdout *empty* — when it spots `error CS`, `Compile Error` or `Exception:` in
the text. Filter or mangle those substrings first. See
[rhino-mcp-platform.md](../write-scripts/rhino-mcp-platform.md).

## What it cannot do

**A forge refuses its own `InstanceGuid`** ("target is this Script Forge component
itself — refusing"). So the forge's own source has to be pushed from a **second forge** —
a forge can update other forges, just not itself. Place one, point it at the other, push,
then delete the helper. Bootstrapping onto a bare canvas is `g1_place_component` with the
proxy GUID `41822538-1827-4da2-bf84-58074c49b3ad`.

`g1_get_canvas_graph` will find a forge and show whether `Target` is wired, but it samples
one item per param, which truncates the `Log` to its first line. Read the Log through
`run_csharp` and `VolatileData`.

## Related

- [**`header-reference.md`**](../write-scripts/header-reference.md) (the
  `@component` grammar — kit-wide, since the forge, `gh_meta.py` and
  `gh_codegen.py` all read it) and
  [**`component-reference.md`**](component-reference.md) (the forge's own
  inputs, outputs and data model, alongside this file). The header reference's
  appendix adds this kit's tooling bits (`gh-meta: ignore`, `--check`
  semantics, the `SetDesc` relationship); when `tooling/gh_meta.py` disagrees
  with the forge, fix the parser to match the forge, not the reverse.
- `docs/write-scripts/rhino-mcp-platform.md` — the MCP server the forge is driven through.
- `docs/ship-a-plugin/publishing.md` — turning forged components into a distributable plugin.
