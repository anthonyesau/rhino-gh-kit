---
name: forge-push
description: THE authoring path for Grasshopper script components. Drive an on-canvas Script Forge to create or update C# / Python 3 script components from `.cs` / `.py` files — params, type hints, access, defaults, identity, per-param tooltips and icon all synced from the `@component` header in one pass, wires kept on unchanged and renamed param names, and one Source branch per script so N components are forged at once. Use whenever source needs to reach the canvas: a new component, a changed param list, a re-push after editing a file. Do NOT use it to update the forge component itself — a forge refuses its own InstanceGuid; drive a second forge instead.
allowed-tools: mcp__rhino__run_csharp, mcp__rhino__g1_get_canvas_graph, mcp__rhino__g1_place_component, mcp__rhino__g1_solve_graph, mcp__rhino__g1_search_components, Bash, Read, Edit, Write
---

# Push script components with Script Forge

Script Forge is a compiled Grasshopper component that creates and updates *other*
script components from source. It is the kit's authoring path: there is no
reflection route for this any more, and nothing else in the kit pushes source.

**It must be installed** — proxy GUID `41822538-1827-4da2-bf84-58074c49b3ad`,
palette Params ▸ Util. If it is not, stop and tell the user; do not improvise a
`set_Text` payload.

Param semantics (trees, target resolution, change detection, state):
`${CLAUDE_PLUGIN_ROOT}/docs/use-the-forge/script-forge.md`. Header grammar:
`${CLAUDE_PLUGIN_ROOT}/docs/write-scripts/header-reference.md` — the canonical
spec, covering the forged and the compiled path both, with this kit's
tooling-specific notes in its appendix.

## Procedure

### 1. Validate the headers

```bash
python3 "${CLAUDE_PLUGIN_ROOT}/tooling/gh_meta.py" --all --check --root <dir>
```

A header error costs a whole failed branch at solve time; catching it here is
free. **`name`, `type` and `access` are required on every param object** — the
forge fails the branch with e.g. `outputs[0] missing required access`, and the
parser is strict JSON (no comments, no trailing commas).

### 2. Find or place the forge

`g1_get_canvas_graph` lists the canvas. Look for `Name: "Script Forge"` and note
its `Id`, and whether its `Target` input has any `Sources`. If none is present:

```
g1_place_component(selector="41822538-1827-4da2-bf84-58074c49b3ad", x=…, y=…, solve=false)
```

Note the guid of every *other* forge too: never target the one you are driving,
and exclude forges from a broad `name`-match target list.

A bare forge is all **you** need — you drive its params directly. If a *human*
will be working this canvas, `${CLAUDE_PLUGIN_ROOT}/tooling/build-forge-rig.cs`
builds them a control surface instead (Source panel, Target value list, Run
button, grouped and wired): paste it into `run_csharp` on the canvas you want it
on.

### 3. Set Source

`Source` is tree access, **one branch per script**, and a branch may be either
source text or a **file path** — the forge reads `.cs`/`.py` files itself, so
pushing from disk needs no *Read File* rig. Prefer paths: change detection then
sees every edit to the file.

Write it with `${CLAUDE_PLUGIN_ROOT}/tooling/set-param-value.cs`, addressing the
input by name:

```csharp
("f028c4cc-…-forge-guid:Source", "auto", new object[] {
   "/abs/path/list-stats.cs",
   "/abs/path/range-split.py" }),
```

Several items in one branch expand to one component per path. Relative paths
resolve against the folder of the **saved `.gh`**, not the source file's — from
an agent, use absolute paths.

### 4. Set Target

| Goal | Target |
|---|---|
| Create new components | leave `Target` **unwired** |
| Update specific components | a wired panel holding their guid(s) |
| Update every on-canvas instance of each source | a wired panel holding `name` (or `nickname`) |
| …and create one where nothing matches | `name+create` / `nickname+create` |
| Skip a branch | a wired branch with **zero** items |
| Update in place with no wire at all | a `"instanceGuid"` key in the source header |

**A header `"instanceGuid"` beats the create-new marker, not a real target.** The
top row assumes the source carries no pin. If it does, an unwired `Target` — *and*
a `Target` item that is the create-new marker (null or empty) — both fall back to
the pinned Guid and update that component, logging `using header instanceGuid as
target`. Only a `Target` item naming an actual component overrides the pin. So a
pinned source cannot be made to create a spare copy; drop the key to get one.

**`Target` must be *wired*, not internalized.** The forge decides create-vs-update
on `Target.SourceCount > 0` (`script-forge.cs:232`), so persistent data written
straight onto the `Target` param is never seen: the branch silently falls back to
create-new and you get a stray component instead of an update. This is the one
object you have to add to the canvas — `Source` and `Run` you set in place with
`set-param-value`. Through `run_csharp`:

```csharp
using System; using System.Drawing; using System.Linq;
using Grasshopper; using Grasshopper.Kernel; using Grasshopper.Kernel.Special;

var ghDoc = Instances.ActiveCanvas.Document;
var forge = (IGH_Component) ghDoc.FindObject(new Guid("<forge guid>"), true);
var fb    = forge.Attributes.Bounds;

var panel = new GH_Panel();
panel.CreateAttributes();
panel.NickName = "Target";
panel.UserText = "name+create";        // or one guid per line
panel.Properties.Multiline = false;    // OFF = one item per line, which is what
                                       // makes a multi-target list fan out
panel.Attributes.Pivot  = new PointF(fb.Left - 290f, fb.Top);
panel.Attributes.Bounds = new RectangleF(fb.Left - 290f, fb.Top, 230f, 36f);
ghDoc.AddObject(panel, false);

forge.Params.Input.First(p => p.Name == "Target").AddSource(panel);
ghDoc.ScheduleSolution(5, d => d.NewSolution(false));
```

A panel needs **both** `Pivot` and `Bounds`: layout recomputes X/Y from the pivot,
so `Bounds` alone leaves it at the canvas origin, and `Pivot` alone leaves it the
default width. Re-run to change the target later — or just rewrite `UserText`
through `set-param-value`, since the wire is already there.

A blank panel is not "empty" — it emits one empty string, which is the
*create-new* marker. Zero items is what skips.

### 5. Pulse Run

The forge pushes on the **rising edge** of `Run` — the transition from false to
true — so a `Run` that is *already* true forges nothing however much else you
change. Set it **last**, after Source and Target are final, and set it in two
steps:

```csharp
("…forge-guid:Run", "auto", new object[] { false }),   // ensure a low baseline
("…forge-guid:Run", "auto", new object[] { true }),    // the edge that forges
```

with a `g1_solve_graph` after each. On a human's rig `Run` is usually a Button;
pressing it by setting `GH_ButtonObject.ButtonDown` true → `ExpireSolution(true)`
→ false → expire produces exactly the same edge. Driving the param directly is
simpler and is what this skill does.

**Put `Run` back to false when you are done.** While it is true the forge is
*armed*: file-path sources are watched on disk and re-forge by themselves when the
file changes. That is the point of holding it, but it is not what you want left
behind.

### 6. Read the report

Set `Run` **false**, solve, then read `Success` / `Log`. This is the step people
get wrong. On the pushing solve the Log ends at `push scheduled — …`; the param
sync lands one solution later. With `Run` false the forge replays its stored
per-slot report and you get the real thing:

```
Run is false — press Run (a button) or switch it on (a toggle) to forge.
— last run —
source file: list-stats-v2.cs (2636 chars)
Target unwired — header name: 1 component(s) named 'List Stats'
header: List Stats [C#] (2 in / 2 out)
updating existing component e17ccb9b-… (wires kept for unchanged param names)
renamed input Places -> Digits (1 wire(s) kept)
lost wire: input Offset (1 source(s))
lost wire: output Average (1 recipient(s))
params synced, source pushed
```

Read it through `run_csharp` (`param.VolatileData`), **not**
`g1_get_canvas_graph` — the graph tool samples one item per param and shows only
the Log's first line. Use the graph tool to find the rig; use `run_csharp` to
read it.

`Success` is one bool per target slot: true when the forge's own pass succeeded.
An empty branch means the branch was skipped. **The guid of a newly created
component is in the Log**, on the `creating new C# component <guid>` line — that
is where you get one to paste into a header `instanceGuid`.

**Two things the Log will never tell you**, because they happen after the target
compiles and the forge deliberately does not re-solve itself to fetch them back:
identity/icon **stamping**, and the target's own compile result. A stamping
failure or a missing icon raises a message on **the forge's own bubble**
(`component.RuntimeMessages(...)`); the target's errors are on the target.

## Two things that will waste an hour

**Expire the param, not the component.** After writing persistent data into a
forge input, `component.ExpireSolution(...)` is not enough — the param keeps its
previously collected volatile data and the forge re-reads the *old* Source.
`IGH_Param.ExpireSolution` is what forces the recollect. `set-param-value.cs`
already expires exactly what it resolved, which is why you address the input as
`"<forge guid>:Source"` and not by the forge's own guid.

**A forge refuses its own `InstanceGuid`.** To update a Script Forge, drive a
*second* forge — a forge can update other forges, just not itself.

## Notes

- **Identity is declared, never remembered.** The forge stores nothing in the
  `.gh` and compares nothing; every press is a real push, and what keeps presses
  from duplicating is the identity ladder — wired `Target`, else header
  `instanceGuid`, else a component whose **Name** matches the header's `name`,
  else create new. So give any source you plan to re-forge a header `name`, and
  leaving `Target` unwired is then the simplest correct rig.
- **A header `default` seeds an empty param only.** It never resets persistent
  data you have already set by hand.
- **Python outputs are never hinted** — an output hint is an active converter that
  breaks list outputs, so the forge leaves them on No Type Hint and `type` is
  documentation there. It is still required by the parser. Marshalling flags
  (`MarshInputs` / `MarshOutputs` / `MarshGuids`) survive the push.
- **Languages must match.** A C# source will not update a Python component or vice
  versa; delete and forge new, losing the wires.
- MCP tools for Grasshopper aren't loaded — stop and ask the user to start them
  (see `${CLAUDE_PLUGIN_ROOT}/docs/write-scripts/workflow.md`); don't improvise a workaround.

## Verified

Against Rhino 8 + Script Forge, 2026-08-13, driving a forge entirely through
`run_csharp` with no human at the canvas:

| | Result |
|---|---|
| Create from a file path, `Target` unwired | component built with correct params, access, type hints (`double`/`int`/`bool`), identity, durable `Tooltip`, per-param `ToolTip`, 24×24 icon, and the header's `default` in persistent data |
| Update with a changed param list, wired on 3 inputs and 3 outputs | unchanged names kept their param objects and wires; `Places → Digits` recycled positionally with its wire; removed params reported as `lost wire:` |
| Same, with the standard `out` param **hidden** | updated cleanly, `out` stayed hidden, no duplicated outputs, no shifted params |
| Python 3 parity | identity, defaults, input hints (`int`/`float`), outputs left unhinted, all three Marshal flags true, outputs marshalled as 6-item lists |
| Save + reopen (read back off disk) | identity, Description, durable `Tooltip`, icon, hidden `out`, every param tooltip and hint, and the Marshal flags all intact |
