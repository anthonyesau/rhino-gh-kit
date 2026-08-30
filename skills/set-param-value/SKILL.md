---
name: set-param-value
description: Write a value into any Grasshopper param or input object on the live canvas — panels, sliders, value lists, toggles, buttons, colour swatches, component inputs addressed by name, and Rhino-document geometry references. One payload, `tooling/set-param-value.cs`, run through `mcp__rhino__run_csharp`. Use whenever something on the canvas needs a value set from an agent: feeding Script Forge its Source and Target, rebuilding a test fixture's references after the source `.3dm` changed, or swapping reference targets in bulk. The Platform's own tools can place a slider and nothing else.
allowed-tools: mcp__rhino__run_csharp, mcp__rhino__g1_solve_graph, mcp__rhino__g1_get_canvas_graph, Read, Bash
---

# Set a value on a Grasshopper param

Everything lives in one payload: `${CLAUDE_PLUGIN_ROOT}/tooling/set-param-value.cs`.
Read it, fill in the `edits` array at the top — the only block you edit — and paste
the whole file into `mcp__rhino__run_csharp`. Do not retype the resolution logic
into a skill-local snippet; there is one copy on purpose.

One call handles many edits, of mixed kinds, across many objects.

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

`"auto"` is right for everything that is not a Rhino-geometry reference.

| Kind | For |
|---|---|
| `auto` | panels, sliders, toggles, value lists, swatches, strings, numbers, bools — the raw value goes through `IGH_Goo.CastFrom` |
| `ref` | a Rhino object reference: values are Rhino object GUIDs. Sets `ReferenceID`, then `LoadGeometry` — what right-click → "Set One Rhino Object" does |
| `arc` `circle` `line` `rect` | as `ref`, plus the manual `.Value` extraction those four goo types need |
| `view` | `Param_ModelView`: values are **named-view names**, not GUIDs |
| `state` | `IGH_StateAwareObject` (Gene Pool, …): one value, the `LoadState` string |
| `chunk` | general fallback: one value, a base64 `GH_LooseChunk` read straight into `PersistentData` |

`Param_Plane` and `Param_Box` use `ref`, not a primitive kind — their
`LoadGeometry` converts from a planar Brep / a box-Extrusion on its own.

The four primitive kinds exist because `LoadGeometry` does **not** convert an
`ArcCurve`/`LineCurve`/`PolylineCurve` into an `Arc`/`Circle`/`Line`/`Rectangle3d`;
`ReferenceID` alone leaves those goos `IsValid=false`.

### 3. Run it

Paste the filled-in file into `run_csharp`. Each edit prints a `·` line with the
resolved type and NickName, then what it did. A `FAIL` line names the reason —
including, for a bad `:<ParamName>`, the list of input names the component
actually has.

### 4. Read the result back in a *second* call

Values land immediately; the recompute is **scheduled** (see below), so nothing
the payload prints reflects post-solve state. Read `VolatileData` in a separate
`run_csharp` call. `g1_solve_graph` first if you want that deterministic rather
than relying on GH's message pump having got to the scheduled solution.

**Verify through `VolatileData`, never `PersistentData`.** For a reference,
`PersistentData` prints `Null Curve` with `IsValid=false` while `VolatileData`
holds a valid, referenced curve — GH lazy-loads referenced geometry and the
persistent side is the un-loaded shell. That is not a failed set.

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

## Things that will look like bugs

- **A reference goes internalized when you save.** `GH_Arc`/`GH_Circle`/`GH_Line`/
  `GH_Rectangle` serialize their *Value*, not a `ReferenceID`. Reopen a saved
  document and the geometry is right but `ReferenceID` is back to `Guid.Empty` —
  no longer tracking the Rhino object. Same limitation as the manual `.Value`
  extraction, seen from the other end. If you need a live reference to a circle or
  a line, target a `Param_Curve` or `Param_Geometry` with kind `ref`.
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
- **Data trees with explicit paths.** The payload appends into a single default
  path. Multi-branch input needs the `Append(T, GH_Path)` overload.
- **Wires, names, descriptions, placement.** It writes values and expires; it
  touches nothing else.
- **Params that cannot reference a Rhino object at all** — `Param_Vector`,
  `Param_Field`, `Param_Transform`, `Param_MeshFace`, `Parameter_TwistedBox`,
  `GH_GeometryCache`, `GH_GeometryPipeline`. There is nothing to set.
- MCP tools for Grasshopper aren't loaded — stop and ask the user to start them
  (see `${CLAUDE_PLUGIN_ROOT}/docs/write-scripts/workflow.md`); don't improvise a workaround.

## Verified

Rhino 8, 2026-08-13. One `run_csharp` call, six edits, zero failures; read back
after `g1_solve_graph`:

| Target | Kind | Volatile data after the solve |
|---|---|---|
| `GH_Panel` | `auto` | 3 items — `alpha`, `beta`, `gamma`, Multiline off |
| `GH_NumberSlider` | `auto` | `42.5` |
| `GH_ValueList` | `auto` | `Gamma` selected, emitting `3` |
| `Param_Curve` | `ref` | 1 valid `GH_Curve`, `ReferenceID` intact (persistent side `Null Curve`, as expected) |
| `<forge guid>:Source` | `auto` | the `.cs` path, on a component input resolved by name |
| `<forge guid>:Force` | `auto` | `True` |

(That run predates Script Forge 0.4.0-beta, which removed the `Force` input. The
measurement stands for what it tested — addressing a *component input by name* —
and `<forge guid>:Run` behaves identically today.)

`GH_ButtonObject` added 2026-08-27, Rhino 8.34: before the fix it fell through
every step of `setParam` and reported *"has no PersistentData, no
ContextualParameter base and no LoadState"* — the skill claimed buttons, the
payload failed them. Now `ButtonDown=true` → volatile `True` after the scheduled
solve, `false` → `False`.

Reference and value durability across a save/reopen was verified separately —
see the notes in `tooling/set-param-value.cs`.
