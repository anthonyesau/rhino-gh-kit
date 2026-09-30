---
name: set-param-value
description: Write a value into any Grasshopper param or input object on the live canvas — panels, sliders, value lists, toggles, buttons, colour swatches, component inputs addressed by name, and Rhino-document geometry references. Two payloads run through `mcp__rhino__run_csharp` — `tooling/set-param-value.cs` for everyday values, `tooling/set-param-value-special.cs` for Rhino references and the rare kinds — picked by the kind of value. Use whenever something on the canvas needs a value set from an agent: feeding Script Forge its Source and Target, pressing its Run button, rebuilding a test fixture's references after the source `.3dm` changed, or swapping reference targets in bulk. The Platform's own tools can place a new slider and nothing else.
allowed-tools: mcp__rhino__run_csharp, mcp__rhino__g1_solve_graph, mcp__rhino__g1_get_canvas_graph, Read, Bash
---

# Set a value on a Grasshopper param

Two payloads, one per group of kinds. Both take the same `edits` array; fill it in —
the only block you edit — and paste the whole file into `mcp__rhino__run_csharp`.

| Script | Kinds | Size |
|---|---|---|
| `${CLAUDE_PLUGIN_ROOT}/tooling/set-param-value.cs` | `auto` — everything that is not a Rhino reference | ~10.7 KB |
| `${CLAUDE_PLUGIN_ROOT}/tooling/set-param-value-special.cs` | `ref` `arc` `circle` `line` `rect` `view` `state` `chunk` | ~11.3 KB |

Pick by kind; almost every call is the first. Given a kind it does not handle, a
script prints `FAIL … kind 'x' is handled by <the other script>` and sets nothing for
that edit, so a mixed batch is two calls. Do not retype their logic into a
skill-local snippet — the code they share is kept byte-identical by
`tooling/check_set_param_sync.py`, which CI runs.

One call handles many edits across many objects.

## Procedure

### 1. Resolve the address

`g1_get_canvas_graph` gives you every object's `Id`. Two address forms:

| Form | Resolves to |
|---|---|
| `"<guid>"` | a canvas object, or a param, by `InstanceGuid` |
| `"<guid>:<ParamName>"` | an **input** param of that component, by param `Name` (or NickName, case-insensitive) |

Prefer the second form for component inputs — it survives a param reshuffle and
reads as what it is. It is what `forge-push` uses (`"<forge guid>:Source"`).

### 2. Pick the kind

| Kind | Script | For |
|---|---|---|
| `auto` | everyday | panels, sliders, toggles, buttons, value lists, swatches, and any param holding numbers, text, booleans or GUIDs. The raw value goes through `IGH_Goo.CastFrom` |
| `ref` | special | a Rhino object reference: values are Rhino object GUIDs. Sets `ReferenceID`, then `LoadGeometry` — what right-click → "Set One Rhino Object" does |
| `arc` `circle` `line` `rect` | special | as `ref`, plus setting `.Value` by hand |
| `view` | special | `Param_ModelView`: values are **named-view names**, not GUIDs |
| `state` | special | `IGH_StateAwareObject` (Gene Pool, …): one value, the `LoadState` string |
| `chunk` | special | fallback for anything else with persistent data: one value, a base64 `GH_LooseChunk` read straight into `PersistentData` — pair it with `Write` + `Serialize_Binary` on the capture side |

`Param_Plane` and `Param_Box` use `ref` — their `LoadGeometry` converts from a planar
Brep / a box-Extrusion on its own. The four primitive kinds exist because
`LoadGeometry` does **not** convert an `ArcCurve`/`LineCurve`/`PolylineCurve` into an
`Arc`/`Circle`/`Line`/`Rectangle3d`; `ReferenceID` alone leaves those goos
`IsValid=false`.

**Value formats** for `auto`: a slider takes a number; a toggle or button a bool; a
value list item names (case-insensitive) or 0-based indices; a swatch an HTML colour
(`"#FF8000"`); a panel one value per line; a param one value per item, and an empty
array clears it.

### 3. Run it

Paste the filled-in file into `run_csharp`. Each edit prints a `·` line with the
resolved type and NickName, then what it did. A `FAIL` line names the reason —
including, for a bad `:<ParamName>`, the input names the component actually has.

### 4. Read the result back in a *second* call

Values land immediately; the recompute is **scheduled**, so nothing the payload
prints reflects post-solve state. Read `VolatileData` in a separate `run_csharp`
call. `g1_solve_graph` first if you want that deterministic rather than relying on
GH's message pump having got to the scheduled solution.

**Verify through `VolatileData`, never `PersistentData`.** For a reference,
`PersistentData` prints `Null Curve` with `IsValid=false` while `VolatileData`
holds a valid, referenced curve — GH lazy-loads referenced geometry and the
persistent side is the un-loaded shell. That is not a failed set.

## How a plain param is set

Both scripts share this path, for anything that is not one of the special objects
above:

1. **`ContextualParameter<T>`** (the RhinoCode Get\* parameters) → `ClearContextualData`
   then `AssignContextualData`. Checked first: these have no usable `PersistentData`,
   and the type is matched by name because it lives outside the Grasshopper assembly.
2. **`GH_PersistentParam<T>`** → `PersistentData.Clear()`, then `Append(goo)` per value,
   with the goo type read off the generic argument. Clearing first is not optional —
   see *Leftover values* below.

## Solver state

Mutation happens inline — `run_csharp` is on the UI thread, outside a solution —
but the `ExpireSolution` calls are deferred into `ghdoc.ScheduleSolution`.
Expiring an object mid-solution trips GH 8's *object expired during a solution*
guard and locks the canvas.

For a **single** call that is all you need. Across **several** calls, each one
schedules its own solve, so bracket the batch with the global solver switch:

```csharp
GH_Document.EnableSolutions = false;   // static, not per-document
```

…your payload calls…

```csharp
GH_Document.EnableSolutions = true;
```

Two things to know about that switch:

- **`GH_Document.SolutionLocked` does not exist.** `EnableSolutions` (static, the
  Solution ▸ Enable Solver menu item) and `GH_Document.Enabled` (per-document) are
  the real members.
- **Setting it back to `true` solves synchronously.** Verified: with solutions off,
  an expired panel reads `VolatileDataCount = 0`; re-enabling in the *same* call
  leaves it fully recomputed with `SolutionState = PostProcess`. So re-enabling is
  itself the recompute — and it is why the toggle belongs in its own calls
  bracketing the payload, never pasted inside it, where it would run a solution
  inline and defeat the `ScheduleSolution` deferral.

A document that is not on the canvas starts with `Enabled = false` and never solves;
set it `true` before expecting a scheduled solution to run there.

## Things that will look like bugs

- **A reference goes internalized when you save.** `GH_Arc`/`GH_Circle`/`GH_Line`/
  `GH_Rectangle` serialize their *Value*, not a `ReferenceID`. Reopen a saved
  document and the geometry is right but `ReferenceID` is back to `Guid.Empty` —
  no longer tracking the Rhino object. Same limitation as setting `.Value` by hand,
  seen from the other end. If you need a live reference to a circle or a line,
  target a `Param_Curve` or `Param_Geometry` with kind `ref`.
- **A `ref` goo reads `Null Curve` straight after a reopen** while `ReferenceID` is
  intact and `VolatileData` is correct — the lazy loading above.
- **A panel's item count is set by Multiline, not by you.** The payload joins N
  values with newlines and sets `Multiline` OFF for several values (one item per
  line, which is how a panel feeds a list downstream) and ON for one (so text
  containing newlines stays a single item). Set the text you want the *downstream
  component* to receive, not the text you want to look at.
- **Leftover values on an item-access input multiply the outputs.** N persistent
  values make GH solve the component N times and fan the outputs into N branches.
  The payload calls `Clear()` before every `Append` for exactly this reason —
  `Append` accumulates. If outputs come back with more items than you expect,
  check the inputs' persistent data count before suspecting the logic.
- **A value list's volatile item is its *value*, not its name.** Selecting `Gamma`
  on a three-item list emits `3` if that is the item's expression.
- **Setting a button is a hold, not a press.** `GH_ButtonObject` has no persistent
  data — `ButtonDown` is its only writable state, and the value downstream sees is
  whatever `ButtonDown` was *during the solve*. Since the payload defers every
  expiry into `ScheduleSolution`, one call with `true` presses and **holds**: the
  button stays down on the canvas until something sets it back. A press is two
  calls — `true`, then `false` — with the downstream solve happening between them.
  Send the `false` even if you don't care about the release, or you leave the
  canvas in a state a person has to click out of.

## Not this skill

- **Pushing source into a script component.** That is `forge-push`, through Script
  Forge — params, hints, identity, tooltips and icon in one pass. Never hand-write
  persistent data onto script params to fake it.
- **Data trees with explicit paths.** The payloads append into a single default
  path. Multi-branch input needs the `Append(T, GH_Path)` overload.
- **Wires, names, descriptions, placement.** They write values and expire; they
  touch nothing else.
- **Params that cannot reference a Rhino object at all** — `Param_Vector`,
  `Param_Field`, `Param_Transform`, `Param_MeshFace`, `Parameter_TwistedBox`,
  `GH_GeometryCache`, `GH_GeometryPipeline`. There is nothing to set.
- MCP tools for Grasshopper aren't loaded — stop and ask the user to start them
  (see `${CLAUDE_PLUGIN_ROOT}/docs/write-scripts/workflow.md`); don't improvise a workaround.

## Verified

Rhino 8.35, Platform 0.3.0, 2026-09-30, in a scratch document; read back from
`VolatileData` after the solve.

| Target | Kind | Volatile data after the solve |
|---|---|---|
| `GH_Panel` | `auto` | 3 items — `alpha`, `beta`, `gamma`, Multiline off |
| `GH_NumberSlider` | `auto` | `42.5`, then `10` through the scheduled solve alone |
| `GH_BooleanToggle` | `auto` | `True` |
| `GH_ButtonObject` | `auto` | `True`, then `False` on the second call |
| `GH_ValueList` | `auto` | `Gamma` selected, emitting `3` |
| `GH_ColourSwatch` | `auto` | `255,128,0` |
| `Param_Number` holding a leftover `99` | `auto` | `5` — replaced, not appended; an empty array then cleared it |
| Addition `:A` and `:b` | `auto` | inputs by name and by lower-case name; output `5` |
| `Param_Curve` / `Param_Geometry` | `ref` | 1 valid referenced curve each |
| `Param_Line` / `Param_Circle` | `line` / `circle` | `Line(L:10)`, `Circle(R:2.5)` |
| `Param_Number` | `chunk` | `7`, `8` |
| Gene Pool | `state` | `10`, `20` |

The reference kinds ran against a headless `RhinoDoc` substituted for
`__rhino_doc__`, so the test added nothing to the open model. A bad input name
listed the component's real inputs, and a kind sent to the wrong script named the
right one. Reference and value durability across a save/reopen: Rhino 8, 2026-08-13 —
the first two bullets under *Things that will look like bugs*.
