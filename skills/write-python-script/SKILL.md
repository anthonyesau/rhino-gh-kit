---
name: write-python-script
description: TRIGGER — load before creating or editing the body of a Rhino Grasshopper Python 3 Script component (a `.py` authored for the "Python 3 Script" component, or a `.py` carrying an `@component` header in a rhino-gh-kit project), whenever: writing a fresh Python component from scratch; adding, renaming, or retyping an input or output; choosing between script mode and SDK mode; touching `@component` header `"type"`, `"default"`, or `"optional"` for a Python param; or diagnosing a symptom of these rules — `NameError: name 'X' is not defined`, a list output arriving as one opaque item or empty, an output param still called `a` after a paste, a type hint that disappeared when a signature gained a default, or output paths reading `{i;0}` where `{i}` was expected. SKIP for C# Script components (that's `write-csharp-script` — the `out`-param quirk and the C# signature rewrite do not apply here) and for pushing a finished file to the canvas (that's `forge-push`, not authoring).
allowed-tools: Read, Write, Edit, Grep, Glob, Bash
---

# Write a Grasshopper Python 3 Script component

Rules for the *body* of a `.py` file authored for Rhino Grasshopper's Python 3
Script component. This is authoring guidance, not the push mechanism — once the
file is ready, hand it to `forge-push`; never hand-write source directly onto a
live param to work around it.

Header grammar and the full `type` vocabulary live in
`${CLAUDE_PLUGIN_ROOT}/docs/write-scripts/header-reference.md`,
`${CLAUDE_PLUGIN_ROOT}/docs/write-scripts/python3-marshalling.md`,
`${CLAUDE_PLUGIN_ROOT}/docs/write-scripts/naming-guide.md`,
`${CLAUDE_PLUGIN_ROOT}/docs/write-scripts/identity-properties.md` — this file
only covers what changes about the *body* as a result. What a Python component
can and cannot configure about itself, measured side by side against C#, is
`${CLAUDE_PLUGIN_ROOT}/examples/native-ceiling/README.md`.

## Script mode, not SDK mode

A Python 3 Script component runs its source one of two ways, and they are
different languages for the purposes of every rule below.

| | **script mode** | **SDK mode** |
|---|---|---|
| shape | bare statements at module level | `class MyComponent(Grasshopper.Kernel.GH_ScriptInstance): def RunScript(self, …)` |
| inputs arrive as | module-level globals, named after the params | `RunScript` arguments |
| a paste declares | **nothing** — params stay `x` / `y` | **inputs only** — names, order, hints, access, off the annotations |
| outputs | assign a module-level variable per output name | undeclarable; `return` returns values, not names |
| GH rewrites your source | **no** | **yes** — the `RunScript` argument list, on solve (but not from every param change; see the access freeze below) |

**Write script mode.** It is what every headered `.py` in this kit uses, what
`forge-push` stamps params onto, and the only mode where an input and an output
can share a label (below). SDK mode buys exactly one thing — a *human paste*
into the ScriptEditor builds the input params for free — and that route is
irrelevant here, because `IScriptComponent.set_Text` is not an editor save: a
programmatic push moves the text and leaves the params where they were, in both
modes. Reach for SDK mode only when the file is meant to be pasted by hand and
its outputs will be built by hand too. `${CLAUDE_PLUGIN_ROOT}/examples/native-ceiling/ring-array-stock.py`
is the canonical SDK-mode file; every other `.py` example is script mode.

## The rules

- **Outputs are module-level assignments, matched by name.** There is no `out`
  param and no `return`: after the body runs, Grasshopper reads each output
  param's name back out of the exec namespace. So an output declared
  `{"name": "Points"}` in the header is produced by a line `Points = pts`, and
  a name never assigned emits nothing. Assign each output **once, at the end**,
  from a local you built up — the same discipline C# needs for a different
  reason.

- **An input and an output may share one label — Python only.** C# hard-fails
  on it (*"The parameter name 'x' is a duplicate"*), because both sides land in
  one `RunScript` parameter list. Python has no such list: outputs are read back
  from the namespace, not declared. The mechanism in script mode is exactly one
  global doing two jobs — the input's value is read during the body, then the
  **same name** is reassigned to the output value on the last lines:

  ```python
  n = max(Count, 0) if Count else 0   # Count is still the INPUT here
  …
  Count = placed                      # from here on it is the OUTPUT
  ```

  So the reassignment must come **after every read of the input value** — that
  ordering is the whole rule, and it is the only part of this that belongs to
  the body. Give both sides `{"name": "Count"}` and nothing else; the header
  rules are the Header Reference's, under *Component keys*. Worked demo:
  `${CLAUDE_PLUGIN_ROOT}/examples/ring-array-shared-labels.py`.

- **Never share a label within one side.** Two inputs — or two outputs — reading
  the same label have no upside and a real downside: Grasshopper keeps only one
  of them when it rebuilds the component's binding, so one wired input's data
  goes nowhere, silently and with no error. `gh_meta.py --check` blocks it.

- **PascalCase every param name.** What Grasshopper draws on a param is its `NickName`, and
  on a script component `NickName` *is* the identifier the code binds to — they
  are the same storage (`ScriptVariableParam.VariableName`). A param cannot draw
  *Point count* while the body reads `Count`.

- **Every param gets a one-character `nickname`; every component gets a short one.** An
  omitted `nickname` falls back to the full `name`, so a compiled build draws
  `BasePlane` on the canvas where it should draw `P`. Take the **first letter of the head
  noun, capitalised** — the head noun, not the first word (`BasePlane` → `P`,
  `IgnoreZeros` → `Z`). Where the name is a role word (`Path`, `Location`, `Motion`) take
  the *type's* letter instead: `Path` (a `Curve`) → `C`. Uniqueness is **per side and
  case-sensitive**, so on a collision drop to the lowercase form for a curve parameter /
  index / interpolation weight (`t`, `i`), then to domain notation (`p`/`q`, `m`, `k`,
  `dt`), then to letter + ordinal (`Nx`/`Ny`, `C1`/`C2`). Sharing a nickname *across*
  sides is fine and often right — a pass-through `Geometry` draws `G` on both. **Never
  rename the param to dodge a collision**: `name` is the human label and outranks the
  nickname. Component `nickname`: an abbreviation or
  trim, roughly eight characters or fewer (`Curve Frames` → `Frames`, `List Stats` →
  `Stats`). Lexicon, component-nickname patterns and a worked example:
  `${CLAUDE_PLUGIN_ROOT}/docs/write-scripts/naming-guide.md`. Note which surface this reaches: a
  forged script component draws `variableName`, never `nickname`, and `gh_codegen.py`
  compiles C# only — so on a `.py` the nickname is documentation of intent today rather
  than a live label. Set it anyway; the header is meant to be a complete description of
  the component, and `gh_meta.py --check` validates it either way.

- **An unwired input arrives as `None`, so read it by bare name — `NameError` is
  about a missing *param*, not a missing wire.** Measured 2026-08-26 on Rhino
  8.34.26223.11002 / GH 1.0.0008: on a component whose params exist, an unwired
  optional input with no persistent data **is** injected into the namespace,
  bound to `None`, for a hinted `int` and a `GeometryBase` alike. So
  `n = Count if Count else 3` is safe and `globals().get("Count")` buys nothing.
  What raises `NameError: name 'X' is not defined` is a name **no param
  carries** — the shape a fresh paste has, where script mode declares nothing
  and the params are still `x` / `y`. Two consequences:

  1. **Guard with `globals().get(…)` only in a script that builds its own
     params**, where the first solve genuinely runs before the param exists —
     `${CLAUDE_PLUGIN_ROOT}/examples/native-ceiling/ring-array-auto-configured.py`. In an ordinary
     forged component the guard is noise.
  2. **`None` is not the fallback your tooltip promised.** An input documented
     as "defaults to 3 when unwired" is only telling the truth because the body
     writes `else 3`. **Declare `"default": 3` in the header** to make it real —
     for `bool` / `int` / `double` / `string`, the types JSON has a scalar for.
     It is also the *only* route by which an unwired `bool` can read `true`,
     since `None` and a wired `false` are both falsy. Geometry, points and
     planes have no JSON literal and so no `default`; internalise those on the
     param instead.

  Check first that the param is not **presence-sensed**: a `default` lands in
  the same `PersistentData` slot as right-click ▸ *Internalise data*, so it
  answers *"what value?"* and can never answer *"did the user wire anything?"*.
  Any input read through `SourceCount` / `PersistentDataCount` is asking the
  second question, and a `default` makes its answer permanently *yes*. It is
  also close to one-way: the forge applies a `default` only to a param with no
  persistent data, so a user's internalised value survives every re-forge and
  the header can never reset one. Full reasoning, and the sibling key
  `"optional": false` (which instead makes an unwired input a hard stop, orange
  *failed to collect data*, body never runs), in the header reference.

- **Never write a default into an SDK-mode signature.** `Radius: float = 5.0` is
  not merely ignored — it **costs the type hint**, and buys nothing back.
  Measured 2026-08-26 against a control param in the same paste: `Control: float`
  kept its hint, while `Defaulted: float = 5.0` and `Curvy: … = None` both came
  back at `object` and regenerated as **bare** arguments. So it is *any* default,
  not just a numeric one; the value never reaches persistent data; and GH strips
  the `= 5.0` from the signature, so it does not even survive as documentation.
  Declare defaults in the header, where they do reach persistent data.

- **Always set descriptions for every input and output — in the header, and they
  reach the canvas from there.** Write them as the `"description"` of each object
  in the header's `"inputs"` / `"outputs"` arrays, in plain language for someone
  who does not know the component; `forge-push` stamps them on every pass. A
  param whose tooltip still reads the generic *"Converts to a collection of…"*
  converter text is a defect. Each of the two levels has a volatile slot and a
  durable one and only the durable one is archived into the `.gh`, which is why
  a tooltip can come back generic after a reopen; `identity-properties.md` has
  the table and the measurements. There is no "restamp after every push" rule —
  the forge writes the durable slots on every pass, so this is background for
  reading a component, not a procedure.

- **Python outputs are never hinted; an output's header `type` is
  documentation.** The forge leaves Python outputs on No Type Hint, matching
  stock components, and a hint there buys nothing in either language. Crucially
  it is **not destructive** either — measured across all 35 converters, a hint
  on a Python output converts *element-wise* and preserves every item. So if a
  list output comes back as **one** opaque `GH_ObjectWrapper`, or **empty**,
  removing the hint will not fix it: check the component's `MarshOutputs` flag,
  which a source push can clear. `forge-push` brackets its push with a
  capture-and-restore of `MarshInputs` / `MarshOutputs` / `MarshGuids`, so this
  is a diagnosis aid, not a step you carry out. `python3-marshalling.md` owns
  the mechanism.

- **Spell the header's `type` the C# way — `double`, not `float`; `string`, not
  `str`.** The Python right-click menu names those two converters differently,
  and the forge resolves either spelling onto a live param, but `double` and
  `string` are canonical and are what the `default` vocabulary keys on: a
  `"default"` on a `"float"` param is rejected with a message telling you to
  respell it. **Three spellings are in play and they do not line up** — the
  header's (`double` / `string`), the live API's
  (`TypeHints.Select("float")` / `Select("string")`), and the annotation
  Grasshopper regenerates (`float` / `str`). Measured 2026-08-26: on a Python
  param `Select("double")` and `Select("str")` both **throw**
  `InvalidOperationException` and leave the old hint in place, so a
  self-configuring script that guesses wrong fails loudly mid-solve. Only the
  header is forgiving — the forge resolves either spelling there.

- **Implicit iteration appends the iteration index to output paths — an item- or
  list-access component never path-mirrors its input tree.** Feed a list-access
  input a 30-branch tree `{0}..{29}` and the outputs land at `{0;0}..{29;0}`, so
  anything downstream pairing trees **by branch path** silently matches nothing.
  When outputs must mirror the input tree, take the input at `tree` access, loop
  the branches yourself, and emit on the **input** path. Identical to C#.

- **State that must survive between solves goes in `GH_Document.ValueTable`, not
  module-level variables.** Key it by the component's `InstanceGuid`; it is
  archived into the `.gh` across saves and reopens. `Component.Message` (the grey
  caption) is not archived either — re-set it every solve if you use it.

- **Look params up by `Name`, never by index.** The built-in `out` print stream
  occupies output 0 *when shown*, and the user can hide it from the right-click
  menu, which shifts every user-defined output down one; a `script` input param,
  when present, does the same at input 0. Neither exists on a freshly dropped
  component. Index-based code crashes or renames the wrong param. Count what
  Grasshopper owns instead — `gh_owned()` in
  `${CLAUDE_PLUGIN_ROOT}/examples/native-ceiling/ring-array-auto-configured.py` is the pattern.

- **Never read a param's access off the `RunScript` signature — read it off the
  param.** In SDK mode Grasshopper regenerates the argument list from the params
  on every solve, but not from *every* change: a **type hint** edit propagates on
  the next solve, while an **access** edit (and a rename) does not — the
  signature stays frozen until a param is actually added or removed. Measured
  2026-08-26: a param left at `access=list` still read `Geo: Rhino.Geometry.GeometryBase`
  after three solves, and only an added param produced
  `Geo: list[Rhino.Geometry.GeometryBase]`. A signature can therefore disagree
  with its own params indefinitely. (List access spells `list[T]`, tree access
  `Grasshopper.DataTree[T]`, and an `object`-hinted param comes back as a bare
  name with no annotation at all.)

- **Python reaches `ScriptVariableParam` members as plain attributes — no
  reflection.** Python.NET binds by the runtime type, so `p.ToolTip = …`,
  `p.TypeHints.Select("float")`, `p.Access = GH_ParamAccess.list`,
  `Component.SetIconOverride(bmp)` all work directly, where C# in the same
  sandbox needs reflection (`RhinoCodePluginGH` is unreferenced, so
  `using RhinoCodePluginGH.Parameters;` is a `CS0246`). One exception:
  `SetVariableName(…)` is non-public and Python cannot reach it, so a rename must
  write the `VariableName` property and let a re-solve settle the binding. This
  is why a self-configuring component is markedly shorter in Python than in C#.

- **No double-quote `"` characters in component or param descriptions.** Use
  single quotes or apostrophes. `gh_meta.py --check` refuses a quoted description
  on every route, so it is a hard gate whether or not your canvas would cope.

- **Add `"language": "python"` when the language could be ambiguous.** Detection
  runs: header `language` key → a `#! python` shebang → the header comment style
  (`"""@component` or `# @component` ⇒ Python) → for headerless updates the
  target component's own type → a leading docstring or `import`. Both header
  styles are valid; the docstring form is the kit's convention and hash comments
  are there for files that already open with one.

**Prefer the dedicated "Python 3 Script" entry over the generic "Script" entry**
for new script components.

**Creating or updating a script component: `forge-push`, always.** One pass takes
the `@component` header and the body to the canvas together — params, type hints,
access, `default`s, identity, per-param tooltips and icon — reusing the existing
`IGH_Param` for every name that did not change, so those wires never break. Never
hand-write source, or poke persistent data onto a script param, to work around it.
