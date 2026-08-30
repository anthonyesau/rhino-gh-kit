# The `@component` header — reference

How to write the `@component` header that declares a `.cs` or `.py` source's
name, params, type hints, tooltips and icon. **This is the canonical grammar,
and it is kit-wide**: three things read it — **Script Forge** (the Grasshopper
component that creates or updates script components on canvas), `gh_meta.py`
(the validator), and `gh_codegen.py` (the compiled-`.gha` generator). It is not
Script Forge's private format, even though the forge is what most sessions
drive.

This documents the header contract only. Canvas wiring, targeting, and the
identity ladder live in
[`docs/use-the-forge/component-reference.md`](../use-the-forge/component-reference.md).

**The header is optional.** With no header the source is injected into a
stock script component as-is (params and metadata untouched). Add a header
when you want the forge to build params, type hints, tooltips, name, and
icon for you.

---

## The shape

The header is the **first comment block** of the source. It opens with
`@component` and its body is **one JSON object** — the object's own closing
brace ends the header, so there is no terminator keyword.

**C#** — a block comment:

```csharp
/* @component
{
  "name":        "Curve Frames",
  "nickname":    "Frames",
  "icon":        "icons/curve-frames.svg",
  "description": "Divides a curve into equal-parameter segments and returns a frame at each division point.",

  "inputs": [
    { "name": "Path", "type": "Curve", "access": "item",
      "description": "The curve to divide." },
    { "name": "Count", "type": "int", "access": "item", "default": 10,
      "description": "Number of segments." }
  ],

  "outputs": [
    { "name": "Planes", "type": "Plane", "access": "list",
      "description": "One frame per division point." }
  ]
}
*/
```

**Python** — a module docstring, or hash comments. Same body; the parser
strips the comment prefix per line, which is safe because a JSON string can
never span a line break.

```python
"""@component
{
  "name": "Point Grid PY",
  "description": "Builds a grid of points.",
  "inputs":  [ { "name": "Count", "type": "int", "access": "item",
                 "description": "Points per side." } ],
  "outputs": [ { "name": "Pts", "type": "Point3d", "access": "list",
                 "description": "The grid points." } ]
}
"""
```

```python
# @component
# {
#   "name": "Curve Divide PY",
#   "description": "Divides a curve."
# }
```

**It is strict JSON.** No comments, no trailing commas, no single quotes — a
stock JSON parser refuses all three. A syntax error is reported with its line
and column, and the forge stops on that branch and forges the others.

Anything after the closing brace is ignored, which is what lets the `*/` or
`"""` sit there. **Unrecognized keys are ignored by design** — extra keys
never break a forge. `guid` is the one rejection; see
[below](#component-keys).

---

## Component keys

This is every key `gh_meta.py` and the forge's own parser recognize at the
component level. Most readers only need the **Forged component** column —
skip **Compiled component** entirely unless a source is also headed for
`gh_codegen.py`'s ship list (see [For a compiled build](#for-a-compiled-build)).

| Key | Required | Forged component | Compiled component |
|---|---|---|---|
| `name` | **yes** | Component Name (menus, tooltips). Also what the `name` target keyword matches. | `Name` |
| `description` | **yes** | The hover tooltip. **No double quotes** (see [Warnings](#warnings)). | same |
| `nickname` | no | Canvas nickname. Defaults to `name`. Matched by the `nickname` target keyword. | `NickName`, same default |
| `category` | no | *ignored* | Ribbon tab |
| `subcategory` | no | *ignored* | Ribbon panel |
| `exposure` | no | *ignored* | Ribbon prominence (`level1` … `level7`, `hidden`, `obscure`) |
| `icon` | no | Path to an SVG/PNG, or an embedded PNG. See [Icons](#icons). | same source, different resolution — see [For a compiled build](#for-a-compiled-build) |
| `language` | no | `csharp` or `python`. Usually auto-detected (see [Language detection](#language-detection)). | n/a — only `.cs` sources are ever compiled |
| `instanceGuid` | no | Pins the source to one component: with no `Target` wire, the forge updates (or recreates) that exact instance. | *ignored* |
| `componentGuid` | no | *ignored* | The permanent published `ComponentGuid`, and the codegen's ship-list opt-in — no `componentGuid`, not shipped. Never regenerate one that has reached a user. |
| `markers` | no | Preserved verbatim in the source, for `Text.Contains(...)` sibling scans. | same, plus advertised through an interface for compiled discovery |
| `upgradeFrom` | no | *ignored* | Array of retired `ComponentGuid`s this component supersedes; one `IGH_UpgradeObject` per entry |
| `inputs` / `outputs` | no | Arrays of param objects. See below. | same |

`instanceGuid` and `componentGuid` are unrelated facts that happen to rhyme.
`instanceGuid` says *which component on this canvas to update*; the forge
reads only this one. `componentGuid` is the compiled build's permanent
published identity — burned into a `.gha`, referenced by every `.gh` that
ever placed the component, so it must never be regenerated; `gh_codegen.py`
reads only this one. A source may legitimately carry both with the *same*
value (`script-forge.cs` does), but that is a coincidence of intent, not of
meaning. See `docs/ship-a-plugin/dotnet-build.md` in the kit for how the ship list and upgraders
actually work — this table says what each key *means*, not what the build
*does* with it.

> **`guid` is not a valid key.** An older header grammar used a bare `guid:`,
> and it meant two different things depending on which tool read it. The forge
> rejects it by name rather than ignoring it — being ignored would silently
> stop the header pinning anything — with a message naming the replacement.
> Say `instanceGuid` or `componentGuid`, or both.

### `instanceGuid` — pinning a source to one component

`instanceGuid` is the `InstanceGuid` of a component on the canvas: the one this
source owns. It is what makes an edit-and-re-forge loop land on the same
component every time instead of dropping a new one beside it. Forge the source
once with no header guid, read the guid out of the `Log` line
`creating new C# component <guid>`, and paste it into the header.

**It is rung 2 of the identity ladder**, not an override. A wired `Target`
naming an actual component wins, so a pinned source can still be pushed
somewhere else deliberately. The pin is consulted when `Target` is **unwired**,
and it beats rung 3 (matching the header's `name`), so it is the way to pin one
specific instance when several components share a name.

**An explicitly empty `Target` item ignores the pin and always creates.** That
is the escape hatch for deliberately making a second copy of a pinned source.
(Before 0.4.0 the pin filled in there too, and "create new" on a pinned source
quietly re-pinned instead.)

**A source with no pin is not adrift.** Rung 3 finds the component by the
`name` the header itself stamped on it, so an ordinary named source already
re-forges into the same component press after press. Pin only when the name is
ambiguous or you want a specific instance.

**It recreates, too.** If the pinned component isn't on the canvas — you deleted
it, or you opened a fresh document — the forge creates one *and gives it that
guid*, so the pin keeps working. Delete a forged component, re-forge, and it
comes back with the same identity (its wires do not).

There is no need to invent a guid by hand, and no reason to change one: it is
the component's identity, and rewriting it orphans whatever the old value
pointed at.

---

## Param objects

Each entry of `inputs` / `outputs` is a JSON object:

```json
{ "name": "Keys", "variableName": "OutKeys", "type": "string", "access": "list",
  "optional": false, "default": null,
  "description": "Entry names." }
```

| Key | Required | Meaning |
|---|---|---|
| `name` | **yes** | The param's **PrettyName** — the human label, and the tooltip title. |
| `variableName` | no | The identifier your code receives. Defaults to `name`. On a forged component this is also what Grasshopper draws on the param. |
| `nickname` | no | The `NickName` a compiled build draws. Never reaches a forged script component. |
| `type` | **yes** | The type-hint converter. See [Type hints](#type-hints). |
| `access` | **yes** | `item`, `list`, or `tree`. |
| `description` | no | The param tooltip. Defaults to empty — but write one; a param with no tooltip is a defect. |
| `optional` | no | Whether the param may be left unwired. Defaults to `true`. Inputs only. |
| `default` | no | A declared default value. Inputs only. See [Defaults](#optional-and-default). |

**Params end up in exactly the header's order.** Index-based body code and
downstream tooling stay valid — the header *is* the index order.

### Three name slots, and which surface each reaches

This is the part worth reading twice. Rhino gives a script param two label
slots and they do not mean what their names suggest: a `ScriptVariableParam`'s
**`NickName` is its `VariableName`** (the C# identifier Grasshopper draws on
the param), and its **`Name` is its `PrettyName`** (a free-form human label). A
compiled component has the ordinary Grasshopper pair instead.

| header key | forged script component | compiled component |
|---|---|---|
| `variableName` | `NickName` — drawn on the param **and** the C# identifier | a generated C# local only; reaches no surface |
| `name` | `Name` (PrettyName) — the tooltip title | `Name` |
| `nickname` | *not written* — the drawn name is the variable | `NickName` — drawn on the param |

**The defaults are a fan, not a chain:** `variableName` ← `name` and
`nickname` ← `name`, each independently. A param carrying only `name`
collapses all three, which is what you want almost always. Chaining them would
let a short compiled `NickName` (`"R"`) become the C# identifier.

On a **C#** forged component, `variableName` exists for the case where an
input and an output must show the same label but cannot share a C# identifier
— `{"name": "Keys", "variableName": "OutKeys"}` gives a compiled param drawn
as `Keys` and a legal signature. On a forged script component the param
necessarily draws `OutKeys`, and the tooltip title reads `Keys (OutKeys)`.

**Python has no such conflict, and needs no `variableName` fan for it.** A
Python `RunScript` has no fixed output parameter list to collide in — outputs
are read back from the exec namespace by name, not declared — and a compiled
build never processes a `.py` source at all (`docs/ship-a-plugin/dotnet-build.md`,
"Scope: C# only"). So an input and an output can share one `variableName`
outright: `{"name": "Keys"}` on both sides gives a live Python component with
two params both drawing `Keys`, each independently. Verified end to end on a
live canvas, 2026-08-26 — see
`examples/ring-array-shared-labels.py`.

Rules `gh_meta.py --check` enforces and the forge warns about, message for
message:

- `variableName` must be an identifier (`^[A-Za-z_][A-Za-z0-9_]*$`) and not a
  C# keyword (C# sources only). **Rhino validates nothing here** —
  `VariableName = "Not An Identifier"` is stored without complaint — so this
  check is the only guard.
- `variableName` must be unique **within its own side**, both languages: two
  same-named params on one side can't be told apart, and Grasshopper silently
  keeps only one of them when it rebuilds the live signature.
- **C# only:** `variableName` must ALSO be unique **across inputs and outputs
  combined** — both become locals in one generated `Invoke`, and live, one
  `RunScript` parameter list. That cross-side constraint does not apply to
  Python, for the reason above.
- `name` and `nickname` must each be unique **per side**. Sharing a label
  across sides is the feature (and, for Python, `variableName` now shares in
  that feature too); sharing one within a side compiles to a component with
  two identically-labelled params.

### Picking a nickname

Conventions for what to *write* in each slot — the abbreviation ladder, the
lexicon, component-nickname patterns — are in
[naming-guide.md](naming-guide.md). The short version: param `nickname` is one
capitalised character, first letter of `name`, and nickname uniqueness is
per-side and **case-sensitive**, so `P` and `p` legally coexist the way stock
`Divide Curve` emits `P`, `T` and `t` together.

Remember which surface it reaches: on a **forged script component** the param
nickname is never drawn — `variableName` is. It is the compiled build's label.

### `access`

| access | C# signature | Meaning |
|---|---|---|
| `item` | `T` | a single value |
| `list` | `List<T>` | one branch |
| `tree` | `DataTree<T>` | multi-branch |

An item- or list-access component under Grasshopper's implicit iteration
appends the iteration index to its output paths (`{i}` in ⇒ `{i;0}` out). If a
component's outputs must mirror its input tree, take `tree` and loop the
branches yourself.

### Type hints

`type` names the param's **Converter**. Matching is case-insensitive; the same
vocabulary works for C# and Python.

| Hint | Notes |
|---|---|
| `bool`, `int`, `double`, `string` | primitives |
| `object` / `none` / *blank* | **No Type Hint** — raw goo, no conversion |
| `Guid`, `DateTime`, `Complex`, `Color` | .NET / GH types |
| `Point3d` (`point`), `Vector3d` (`vector`), `Plane` (`plane`) | frames & vectors |
| `Interval`, `UVInterval` | Domain and Domain² |
| `Line`, `Circle`, `Arc`, `Polyline`, `Rectangle3d` | primitive curves |
| `Curve` (`curve`) | any curve |
| `Box`, `Transform`, `Point3dList` | |
| `Mesh` (`mesh`), `Surface`, `Extrusion`, `SubD`, `Brep` (`brep`) | surfaces & solids |
| `PointCloud`, `GeometryBase` | `GeometryBase` accepts any geometry |
| `Hatch`, `TextDot`, `TextEntity`, `Leader` | annotations |

An **unknown hint falls back to No Type Hint** and the forge logs a
`HINT WARNING` — hint typos never fail silently.

> **Spell the C# name.** Grasshopper names two converters differently per
> language: the C# `double` converter is `float` on Python, and `string` is
> `str`. The forge resolves either spelling onto a live param, so a Python
> header saying `"type": "float"` *works* — but `double` and `string` are the
> canonical spellings, and they are what the `default` vocabulary keys on. A
> `"default"` on a `float` param is rejected, with a message telling you to
> spell it `double`.

> **Python outputs are never hinted.** The forge leaves Python outputs on No
> Type Hint (matching stock components); an output's `type` serves as
> documentation there. C# outputs are write-only, so their hints are applied
> but harmless.
>
> The reason is that the hint buys nothing, **not** that it breaks list
> outputs.
> Measured across all 35 converters, a hint on a Python output converts
> *element-wise* and preserves every item; what collapses a list is the
> component's `MarshOutputs` flag, which a `set_Text` push clears while the
> component is still detached from the document. See
> [python3-marshalling.md](python3-marshalling.md).

---

## `optional` and `default`

Two input-side keys. Between them they let a script tell "the user wired
`false`" from "nothing is wired at all" — which a bare `bool` input cannot.

### `optional`

Defaults to `true`, so omitting it changes nothing. `"optional": false` makes
Grasshopper *require* a value: an unwired param goes orange with
*Input parameter X failed to collect data* and the component does not solve at
all.

That last part is the trade. A non-optional unwired input stops `SolveInstance`
from running, which is exactly what you asked for — but it also means no code
on that component runs to explain itself.

### `default`

A declared value the param starts with. It becomes the param's
**persistent data** — the internalized value Grasshopper hands the script when
nothing is wired in. An unwired `{"type": "bool", "default": true}` input
therefore reads `true`, which a plain unwired `bool` never could.

**The vocabulary is `bool`, `int`, `double`, `string`, and nothing else** —
the types JSON has a scalar for. There is no JSON literal that spells a
`Point3d`, so there is no way to declare one. Internalize such a value on the
param itself instead — the forge leaves existing persistent data alone (see
below), so it survives every re-forge.

The value's JSON kind must agree with `type`. A whole number is accepted on a
`double` param and arrives as `2.0`, not as an int; everything else that
disagrees is rejected, out loud, on both surfaces:

| header | verdict |
|---|---|
| `"type": "bool", "default": "yes"` | `default 'yes' is not true or false (bool param)` |
| `"type": "int", "default": 2.5` | `default 2.5 is not a whole number (int param)` |
| `"type": "int", "default": true` | `default True is not a whole number (int param)` |
| `"type": "Point3d", "default": 0` | `default is only supported on bool/int/double/string, not 'Point3d'` |
| `"type": "double", "default": [0,0,1]` | `default [0, 0, 1] is not a number (double param)` |
| a `default` on an **output** | `an output has no default — nothing is ever collected into it` |
| `optional` on an **output** | `'optional' is an input-side idea — RegisterOutputParams has no Optional pass to mirror it` |

A rejected `default` is never applied; the param is left alone.

### Re-forging does not clobber a user's value

Persistent data is user-visible and user-editable (right-click ▸ *Internalise
data*). **The forge applies a declared `default` only when the param has no
persistent data, and leaves an existing value alone.** So a value someone
internalised survives every subsequent re-forge — at the cost that the header
cannot *reset* one. Clear it on the component if you want the header's value
back.

---

## Multi-line tooltips

JSON's own escapes, so `\n` inside a description string is a real newline by
the time the parser hands it over:

```json
{ "name": "Target", "type": "object", "access": "tree",
  "description": "Which component each branch updates:\n• a guid\n• the keyword name\n• null to create new" }
```

There is nothing header-specific to know here, which is the point: write the
escape JSON already defines, and the tooltip on the canvas breaks where you
said it does.

---

## Icons

`icon` accepts three forms:

- **SVG path** — rasterized to a 24×24 PNG automatically via macOS `sips`
  (reused until the SVG is newer);
- **PNG path** — used directly (make it 24×24; GH draws at native pixel size);
- **embedded PNG** — `base64:<payload>` or a full `data:image/png;base64,`
  URI, so the icon travels *inside* the source.

**Path resolution:** absolute paths are used as-is; **relative paths resolve
against the folder of the saved `.gh`** — *not* the folder of the source file.
The forge is handed source text and has no way to know where that text came
from, so the document is the only anchor there is. Keep icons next to the `.gh`
(e.g. in an `icons/` subfolder beside it), or give an absolute path. In an
unsaved document, relative icons are skipped with a warning; save the document
and press `Run` again to pick them up.

**Icons are strictly best-effort.** A missing file, bad extension, corrupt
image, or SVG rasterization off macOS produces an `icon warning:` on the
forge's own bubble (stamping runs after the target compiles, past where the Log
can reach) and the forge carries on — params, source, and other metadata are unaffected.
**On Windows, use a PNG path or a base64 icon** (SVG-only icons are skipped
there).

---

## Language detection

The forge picks the language in this order:

1. header `language` key (`csharp` / `python`);
2. a `#! python` shebang in the first lines;
3. the header comment style — `/* @component` ⇒ C#, `"""@component` or
   `# @component` ⇒ Python;
4. for headerless **updates**, the target component's own type;
5. headerless heuristics — `Script_Instance` or `using X;` ⇒ C#; a leading
   docstring or `import` ⇒ Python.

If none decide, the forge errors and asks for a `language` key. **Add
`language` whenever the language would otherwise be ambiguous.**

---

## Headerless sources

A source with **no header** is injected as-is: the forge creates a stock
component (default `x, y` inputs / `out, a` outputs) or pushes into the target,
touching **no** params, metadata, or icon.

A headerless **create** leans entirely on the heuristics above, so a
marker-less body (e.g. a bare `a = x + y`) can't be classified and errors —
add a recognizable marker (`import`, a docstring, a `using`) or give it a
header. A headerless **update** never hits this: the target's own type settles
the language.

---

## Warnings

Header problems short of a JSON syntax error are reported in the `Log` output
and **never block the forge** — the component is built, and the warning tells
you what it could not honour. Read the Log when a forge doesn't do what you
expected; it is the whole diagnostic surface.

| Log line | Cause |
|---|---|
| `DRIFT WARNING` | a `variableName` is declared in the header but missing from the `RunScript` signature, or vice versa — the header and your code disagree about the param list. |
| `HINT WARNING` | a `type` wasn't recognized; the param fell back to No Type Hint. |
| `NAME WARNING` | a `variableName` is not an identifier, is a C# keyword, is claimed twice on the same side, or (C# only) is claimed by both an input and an output. |
| `LABEL WARNING` | two params on the same side share a `name` or `nickname`. |
| `DEFAULT WARNING` | a `default` the param can't take — the table under [`default`](#default) — or a param with no persistent-data slot to put one in. |
| `OPTIONAL WARNING` | `optional` was declared on an output. |
| `QUOTE WARNING` | a description or a label contains a `"`. Harmless on the canvas, and a compiled build escapes it correctly — but `gh_meta.py --check` still rejects it, which is what fails `publish.sh`'s first gate. |
| `icon warning: …` | see [Icons](#icons) — always best-effort. |
| `lost wire: …` | a removed param dropped its connections. A *renamed* param keeps them and logs `renamed X -> Y (n wire(s) kept)` instead. |

`DRIFT WARNING` is the one worth wiring your attention to: the header is what
builds the params, and your `RunScript` signature is what receives them, so a
disagreement means a param exists on exactly one of the two.

---

## For a compiled build

Skip this if the source is only ever going to be forged. It matters once a
source also carries a `componentGuid` and reaches `gh_codegen.py`'s ship
list. The [Component keys](#component-keys) table above says what each
compile-only key *means*; `docs/ship-a-plugin/dotnet-build.md` in the kit is where the ship
list, hooks, and upgraders are actually built — this section is the two
mechanics too small to earn a section of their own there:

- **Icon resolution differs.** On canvas a relative `icon` path resolves
  against the folder of the saved `.gh` (see [Icons](#icons)). A compiled
  build resolves the same path against the **repo root** instead and embeds
  the result, so the two never fight over one path — but a source meant for
  both has to keep its icon reachable from both anchors.
- **`default` reaches a different mechanism for the same value.** On a forged
  component it becomes the param's PersistentData (see
  [`default`](#default)); in a compiled build it becomes the value argument
  of the matching `Add*Parameter` overload instead. Same declared value,
  different plumbing.

---

## Complete example

```csharp
/* @component
{
  "name":          "Remap Number",
  "nickname":      "Remap",
  "description":   "Remaps a number from one interval into another. Values outside the source interval are extrapolated, not clamped.",
  "icon":          "icons/remap-number.svg",
  "instanceGuid":  "3f2a9c10-8b41-4e6d-9a2f-1c7e5b0d4a88",

  "inputs": [
    { "name": "Value", "type": "double", "access": "item",
      "description": "The number to remap." },
    { "name": "Source", "type": "Interval", "access": "item", "optional": false,
      "description": "The interval the value currently lives in." },
    { "name": "Target", "type": "Interval", "access": "item",
      "description": "The interval to map into." },
    { "name": "Clamp", "type": "bool", "access": "item", "default": false,
      "description": "Hold the result inside Target instead of extrapolating." }
  ],

  "outputs": [
    { "name": "Mapped", "type": "double", "access": "item",
      "description": "The remapped number." }
  ]
}
*/

using System;
using Grasshopper.Kernel;

public class Script_Instance : GH_ScriptInstance
{
  private void RunScript(double Value, Rhino.Geometry.Interval Source,
    Rhino.Geometry.Interval Target, bool Clamp, out object Mapped)
  {
    double t = Source.NormalizedParameterAt(Value);
    if (Clamp) t = Math.Max(0.0, Math.Min(1.0, t));
    Mapped = Target.ParameterAt(t);
  }
}
```


---

# Appendix — validating a header with this kit's tooling

Everything above is the header grammar itself. What follows is about running
`tooling/gh_meta.py` over it, which is this kit's business rather than the
format's.

## Relationship to the runtime `SetDesc` block

A component may set its param descriptions at solve time via a `SetDesc` /
`_paramsInitialized` block. The header coexists with that block non-destructively:
the header is the canonical metadata for the stamping tooling, and the runtime
block is a fallback for anyone hand-pasting source without the tooling. Where the
tooling runs, the block is redundant — it costs a check on every solve and
duplicates the header — so it can be dropped; keep the two in sync wherever it
stays.

**Stamping now covers param descriptions, and it is the kit standard**: Script
Forge writes `ScriptVariableParam.ToolTip` from the header's param objects on
every pass, so every param's tooltip matches the header by default. `ToolTip`,
not `Description`, is the slot that is archived into the `.gh` and that survives
the regeneration following a `set_Text` push; stamping it is what makes the
runtime `SetDesc` block redundant rather than merely duplicated. See
[`identity-properties.md`](identity-properties.md).

## Opting a source out (`gh-meta: ignore`)

`--all` is a blunt sweep of every root-level `.cs` and `.py`, but a project root
legitimately holds sources that are *not* components — a Rhino command script run
through `_RunPythonScript`, a build helper, a scratch file. Put the token
`gh-meta: ignore` in a comment near the top (the first 2 KB) and the sweep skips
the file entirely:

```python
"""
Restore State from Selection

gh-meta: ignore — this is a Rhino command script run through _RunPythonScript,
not a Grasshopper component, so it carries no @component header.
"""
```

Without it such a file reports `no @component header found`, which is
indistinguishable from a real component whose header went missing — so the check
becomes either permanently noisy or something you learn to ignore wholesale, and
a genuine regression hides in the noise. The marker lives in the file rather
than in a project-side ignore list so it travels with the file and states its own
reason. Naming a file explicitly on the command line parses it regardless.

## Validation (`gh_meta.py --check`)

`python3 tooling/gh_meta.py --all --check` (from the project root) validates
every root-level `.cs` **and** `.py` source that has not opted out:

- the body parses as JSON, and `name` and `description` are present;
- **no double quotes** in the component description, any param description, or
  either param label slot (they reach a generated C# literal via `cs_string` —
  see the `write-csharp-script` skill);
- **no duplicate label** among the inputs, or among the outputs. Both slots are
  checked; a collision in both at once (the usual case, since one key defaults
  the other) reports as one problem, not two;
- **every variable name is an identifier** — `^[A-Za-z_][A-Za-z0-9_]*$`, and not
  a C# keyword in a `.cs` source. Nothing else guards this: Rhino stores whatever
  it is given in a `ScriptVariableParam.VariableName` without validating it;
- **no duplicate variable name across inputs *and* outputs together** — unlike
  the labels, where sharing across sides is the point. The two become locals in
  one generated `Invoke`;
- **a declared `default` is input-side and matches its `type`.** Only
  `bool` / `int` / `double` / `string` may carry one — the only hints a JSON
  scalar can name — and the value's JSON kind must agree (`true` on an `int`
  param is rejected, as is `2.5`). An output may carry neither `default` nor
  `optional: false`: Grasshopper only ever collects *into* an input, and
  `RegisterOutputParams` has no `Optional` pass or value overload to mirror
  either one;
- **drift check (C# only):** every header `variableName` must appear in the
  `RunScript` signature and vice versa. Drift is otherwise invisible — the
  canvas hints silently win over the signature at solve time.

A file that will not parse at all is reported on **both** invocations, not just
`--check`: the bare form prints the same `FAIL <path>: <reason>` line on stderr,
keeping stdout as the JSON a caller may be piping, and exits 1. A total failure
therefore reads as `{}` on stdout with the reason on stderr — never a silent
`{}` alone.

Script Forge mirrors every one of these at forge time and reports them as
warnings in its Log output. The two are a sync pair — a rule added to one belongs
in the other in the same commit.

One naming wrinkle worth knowing before reading `gh_meta.py` itself: the header
spells the upgrader key `upgradeFrom`, but `parse_header()`'s returned dict
carries it under `upgrades`. Both names refer to the same header key — the
difference is only between what's written in a `.cs`/`.py` source and what
the Python parser calls it internally once decoded.
