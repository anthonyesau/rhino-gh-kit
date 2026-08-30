# Grasshopper scripting — workflow

This project authors **inline Rhino 8 Grasshopper Script components** — the kind
you paste into the built-in "C# Script" (or "Python 3 Script") component rather
than compile into a `.gha`. The reusable machinery (skills, tooling, docs) ships
in the **rhino-gh-kit** plugin; keep project-specific rules (component
invariants, domain logic, snapshot rituals) in this project's own `CLAUDE.md`.

## One server: the Rhino MCP Platform

McNeel's first-party server, installed per machine and registered under the user's
own config as `rhino`. The kit bundles **no** `.mcp.json`, so the tools are
literally `mcp__rhino__*` — no plugin namespacing to resolve.

The five tools the kit uses: **`run_csharp`** (arbitrary C# against the live
document), **`g1_get_canvas_graph`**, **`g1_place_component`**,
**`g1_solve_graph`**, **`g1_search_components`**.

**The tools existing does not mean Rhino is reachable.** The listener only starts
when the user runs **`MCPStart`** in Rhino; until then every call fails with
*"Could not connect to Rhino"*. That is the normal first failure of a session, and
the fix is one command from the user — ask for it rather than working around it.
The port is **whatever `MCPStart` prints** (10501 here, not the 10500 you may see
written down); confirm with `lsof -nP -iTCP:<port> -sTCP:LISTEN`.

If a task needs these tools and they are absent from the session, stop and ask the
user to start the server. Do not improvise a workaround.

## Script Forge is the authoring path

Source reaches a script component **only** through Script Forge, driven by the
`forge-push` skill — params, type hints, access, defaults, identity, per-param
tooltips and icon, all from the `@component` header in one pass. There is no
reflection fallback in the kit any more. Script Forge is a compiled plugin
installed separately; if it is missing, say so and stop.

`set-param-value` covers the other half — writing a value into any param or input
object (`${CLAUDE_PLUGIN_ROOT}/tooling/set-param-value.cs`). The Platform's own
tools can place a slider and nothing else.

## Writing a `run_csharp` payload

- **It compiles against Grasshopper.** `using Grasshopper; using Grasshopper.Kernel;`
  resolve directly — no `AppDomain.CurrentDomain.GetAssemblies()` preamble. It is
  the same environment a GH C# script component runs in.
- **It runs on the UI thread**, outside a solution (`ManagedThreadId=1`,
  `RhinoApp.InvokeRequired=False`). Mutate inline, but **defer `ExpireSolution`
  into `ghdoc.ScheduleSolution(5, …)`** — expiring an object mid-solution trips GH
  8's *object expired during a solution* guard and locks the canvas.
- **`__rhino_doc__` is the injected `RhinoDoc`** (it *is* `RhinoDoc.ActiveDoc`).
  The Grasshopper handle is `Instances.ActiveCanvas.Document` — a different thing;
  an empty `__rhino_doc__` says nothing about the canvas.
- **Results come back only as scraped stdout**, so print everything you want back.
  A `ScheduleSolution` callback fires *after* the call returns, so nothing it
  computes can be printed — read post-solve state in a second call.
- **Never print the substrings `error CS`, `Compile Error` or `Exception:`.** The
  server sniffs stdout for them and, on a match, moves the **entire** stdout into
  the `error` field and returns stdout **empty** — a successful run reported as a
  failure with its output gone. Verified 2026-08-13. This is a live hazard when
  echoing a Script Forge `Log` or any compiler diagnostics: filter or mangle those
  substrings before printing them.
- **`set_Text` is not an editor save — never generalise from one to the other.** A
  reflection push (`IScriptComponent.set_Text`, which is what Script Forge and any
  `run_csharp` payload use) moves the source text and nothing else: the params do not
  move, and the next solve rewrites the `RunScript` signature *from* them. When a person
  pastes the same source into the **ScriptEditor**, the editor does the opposite — it
  creates, names, orders and type-hints the params *from* the signature
  (`RhinoCodeEditor.Editor.RCEState+StateCode.SetCodeParamsFromServers()`). Three
  triggers, three directions; a claim tested only through `set_Text` says nothing about
  what pasting does, and will read as "pasting configures nothing", which is false for
  C#. If a question turns on the editor's behaviour, ask the user to paste it by hand.
  Full table in [csharp-type-hints.md](csharp-type-hints.md), "Which way the sync runs".
- **A throw rolls nothing back.** Mutations made before an exception persist, on
  both the Grasshopper and the Rhino side, and stdout up to the throw is kept
  while the exception lands in `error`. A payload that fails halfway leaves a
  half-applied change — make payloads idempotent, or check state before re-running one.

## Never let a component be its own witness

A component with both a write side and a read side must be verified by something
**other than itself**. Read the store back through the component under test and a
bug in its reporting path masks a bug in its write path — and the two failures
together are indistinguishable from a pass: a component that stores nothing but
reports three items looks exactly like one that works.

Read ground truth from a **second script component** that dumps the store
directly, or from a `run_csharp` payload that does. Neither has a stake in the
answer, so what it prints is evidence rather than a restatement of the claim
under test.

The rule generalizes past storage — it applies wherever one method's output is the
only proof another method ran:

- **Forge pushes.** `Success` and the `Log` are the forge's account of its own
  work. Confirm a param, hint or tooltip by reflecting on the live component.
- **Type hints.** The param's own `TypeName` always reads `Generic Data`; the hint
  is `ScriptVariableParam.TypeHints.GetSelected().TypeName`, one level down. See
  [csharp-type-hints.md](csharp-type-hints.md).
- **Compiled behaviour.** There is no hot reload on macOS, so *which binary is
  live* is exactly the kind of claim a build cannot make about itself — reflect on
  something the build changed, never trust the file's timestamp.

## Benchmarking through MCP is fair — but measure, don't assume

`run_csharp` runs on the **UI thread**, so a solve triggered through it carries no
threading penalty. Worth knowing for any other server: macOS confines a background
thread's QoS — and the worker threads inheriting it — to efficiency cores, and an
identical solve measured 98 s backgrounded vs 2.3 s on the UI thread.

Re-measured 2026-08-13, a script component burning a fixed CPU load
(4 units serially, then the same 4 through `Parallel.For`), solved with
`ExpireSolution(true)`:

| trigger | wall clock |
| --- | --- |
| `run_csharp`, Rhino **backgrounded** | 3216 / 3281 ms |
| `run_csharp`, Rhino **frontmost** | 3302 / 3296 ms |
| `RhinoApp.InvokeOnUiThread`, frontmost | 3253 / 3328 ms |

All within 3% — and the parallel section ran 4 units in ~680 ms against 2.5 s
serial, i.e. real multi-core throughput, not E-core confinement. Sustained raw
compute in a payload showed the same: 5062 ms backgrounded vs 4992 ms frontmost.

**So:** numbers from a `run_csharp`-triggered solve are reportable, backgrounded
or not, and `InvokeOnUiThread` buys nothing. Still *time* it rather than assuming
— and remember the solve is synchronous on the UI thread, so a slow one blocks the
call until it finishes.

## Metadata-header workflow

Each component source carries a machine-readable `@component` header (component
Name/NickName/Description/icon/category, plus one object per param: name, type
hint, access, description, optional `default`). **The header is the canonical
source of that metadata** — a live GH component's tooltip, icon, and identity are
stamped *from* the header, never harvested back out of it. Canonical format spec:
`${CLAUDE_PLUGIN_ROOT}/docs/write-scripts/header-reference.md`. Parse/validate
with `python3 ${CLAUDE_PLUGIN_ROOT}/tooling/gh_meta.py --all --check` **run
from your project root** (it scans the project's root-level component
sources); that module is this kit's port of the grammar — don't re-implement
it elsewhere. That same reference's appendix covers the tooling-specific bits
(`gh-meta: ignore`, what `--check` enforces). See
`${CLAUDE_PLUGIN_ROOT}/examples/list-stats.cs` — a minimal component that
carries a header and exercises the tooling end-to-end.

- **Stamping live components:** `forge-push` does it. Identity, per-param
  tooltips, type hints, access, defaults and icon are all one forge pass; there is
  no separate stamping step and no restamp-after-push ritual.
- **Icons:** source SVGs in the project's `icons/` (one per component, kebab-case
  of the name), with a `<stem>-dark.svg` light-ink variant for dark themes. The
  canvas icon slot is raster only, so the SVG is rasterized to PNG via `sips`
  (built-in; gitignored output) before stamping. GH draws a stamped icon at the
  bitmap's **native pixel size**, so rasterize to 24×24 to match the canvas slot.
  See `${CLAUDE_PLUGIN_ROOT}/docs/write-scripts/icons.md`.
- **Publishing a plugin** (Yak/`.gha`) is a `dotnet` build driven by
  `tooling/publish.sh` — `gh_meta.py --check` → `gh_codegen.py` → `dotnet build`
  → `yak build` → `yak install`, cumulative stages. See
  `${CLAUDE_PLUGIN_ROOT}/docs/ship-a-plugin/publishing.md`.

## Reference

- [Rhino - Grasshopper Scripting: C#](https://developer.rhino3d.com/guides/scripting/scripting-gh-csharp/) — detailed reference on the GH C# Script component.
- [Rhino - Essential C# Scripting for Grasshopper](https://developer.rhino3d.com/guides/grasshopper/csharp-essentials/) — full RhinoCommon-in-GH course.
- [Rhino - Grasshopper Guides](https://developer.rhino3d.com/en/guides/grasshopper/) — custom components and plugins.
- [Rhino - Creating Rhino/Grasshopper Script Plugins](https://developer.rhino3d.com/guides/scripting/projects-create/)
- [mcneel/rhino-developer-samples](https://github.com/mcneel/rhino-developer-samples) — sample code.
